using Lingo.Infrastructure;
using Lingo.Models;

namespace Lingo.Services;

// 负责并发调度启用的翻译服务，并保证新一轮翻译会取消上一轮
internal sealed class TranslationService : IDisposable
{
    // HttpClient 全局复用；超时由每个翻译器按需控制
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private CancellationTokenSource? _activeCts;

    public static IReadOnlyList<ITranslator> CreateEnabledTranslators(AppSettings settings)
    {
        List<ITranslator> translators = [];
        if (settings.Baidu.Enabled)
        {
            translators.Add(new BaiduTranslator(SharedHttpClient, settings.Baidu, settings.DefaultTargetLanguage));
        }

        foreach (CustomApiSettings api in settings.CustomApis)
        {
            if (api.Enabled)
            {
                translators.Add(new CustomApiTranslator(SharedHttpClient, api));
            }
        }

        return translators;
    }

    // onPartial/onResult 在线程池线程上回调，带回发起的翻译器实例供 UI 层定位面板；UI 层需自行切回 UI 线程
    public void StartTranslation(
        string text,
        IReadOnlyList<ITranslator> translators,
        Action<ITranslator, string> onPartial,
        Action<ITranslator, TranslationResult> onResult)
    {
        CancelActive();

        CancellationTokenSource cts = new();
        _activeCts = cts;
        foreach (ITranslator translator in translators)
        {
            _ = RunOneAsync(translator, text, cts.Token, onPartial, onResult);
        }
    }

    public void CancelActive()
    {
        CancellationTokenSource? cts = _activeCts;
        _activeCts = null;
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void Dispose() => CancelActive();

    private static async Task RunOneAsync(
        ITranslator translator,
        string text,
        CancellationToken cancellationToken,
        Action<ITranslator, string> onPartial,
        Action<ITranslator, TranslationResult> onResult)
    {
        TranslationResult result;
        try
        {
            result = await translator.TranslateAsync(
                text,
                partial =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        onPartial(translator, partial);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{translator.Name} 翻译过程出现未预期异常", ex);
            result = TranslationResult.Failure(translator.Name, "翻译失败，详细信息见日志。", TimeSpan.Zero);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            onResult(translator, result);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lingo/1.0");
        return client;
    }
}
