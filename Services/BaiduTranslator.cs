using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lingo.Infrastructure;
using Lingo.Models;

namespace Lingo.Services;

internal sealed class BaiduTranslator : ITranslator
{
    private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly BaiduSettings _settings;
    private readonly string _defaultTargetLanguage;

    public BaiduTranslator(HttpClient httpClient, BaiduSettings settings, string defaultTargetLanguage)
    {
        _httpClient = httpClient;
        _settings = settings;
        _defaultTargetLanguage = defaultTargetLanguage;
    }

    public string Name => "百度翻译";

    // 百度接口不支持流式，onPartial 忽略
    public async Task<TranslationResult> TranslateAsync(string text, Action<string>? onPartial, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            return TranslationResult.Failure(Name, "未配置 App ID 或 Secret Key，请在设置中填写。", stopwatch.Elapsed);
        }

        string targetLanguage = string.IsNullOrWhiteSpace(_settings.TargetLanguage)
            ? _defaultTargetLanguage
            : _settings.TargetLanguage;

        return await TranslateCoreAsync(text, targetLanguage, allowRetarget: true, stopwatch, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TranslationResult> TranslateCoreAsync(
        string text,
        string targetLanguage,
        bool allowRetarget,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        string salt = Random.Shared.Next(100000, 999999).ToString();
        string sign = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(_settings.AppId + text + salt + _settings.SecretKey)))
            .ToLowerInvariant();

        // 长文本用 POST 表单，避免超出 URL 长度限制
        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = string.IsNullOrWhiteSpace(_settings.SourceLanguage) ? "auto" : _settings.SourceLanguage,
            ["to"] = targetLanguage,
            ["appid"] = _settings.AppId,
            ["salt"] = salt,
            ["sign"] = sign,
        });

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        string json;
        try
        {
            using HttpResponseMessage response = await _httpClient
                .PostAsync(Endpoint, content, timeoutCts.Token).ConfigureAwait(false);
            json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Failure(Name, "请求超时，请检查网络。", stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Error("百度翻译网络请求失败", ex);
            return TranslationResult.Failure(Name, $"网络错误：{ex.Message}", stopwatch.Elapsed);
        }

        return await ParseResponseAsync(json, text, targetLanguage, allowRetarget, stopwatch, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TranslationResult> ParseResponseAsync(
        string json,
        string sourceText,
        string targetLanguage,
        bool allowRetarget,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error_code", out JsonElement errorCode))
            {
                string code = errorCode.ToString();
                string message = root.TryGetProperty("error_msg", out JsonElement errorMsg)
                    ? errorMsg.ToString()
                    : "未知错误";
                return TranslationResult.Failure(Name, $"[{code}] {message}{DescribeErrorCode(code)}", stopwatch.Elapsed);
            }

            // 保留原版行为：目标为中文但检测出源语言也是中文时，自动改译为英文
            if (allowRetarget
                && targetLanguage == "zh"
                && root.TryGetProperty("from", out JsonElement fromElement)
                && fromElement.GetString() == "zh")
            {
                return await TranslateCoreAsync(sourceText, "en", allowRetarget: false, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (root.TryGetProperty("trans_result", out JsonElement transResult)
                && transResult.ValueKind == JsonValueKind.Array)
            {
                StringBuilder builder = new();
                foreach (JsonElement item in transResult.EnumerateArray())
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(item.GetProperty("dst").GetString());
                }

                if (builder.Length == 0)
                {
                    return TranslationResult.Failure(Name, "翻译结果为空。", stopwatch.Elapsed);
                }

                return TranslationResult.Ok(Name, builder.ToString(), stopwatch.Elapsed);
            }

            return TranslationResult.Failure(Name, "接口返回格式异常。", stopwatch.Elapsed);
        }
        catch (JsonException)
        {
            return TranslationResult.Failure(Name, "接口返回内容无法解析。", stopwatch.Elapsed);
        }
    }

    private static string DescribeErrorCode(string code) => code switch
    {
        "52003" => "（请检查 App ID 是否正确）",
        "54001" => "（签名错误，请检查 Secret Key）",
        "54003" => "（访问频率受限，请稍后重试）",
        "54004" => "（账户余额不足）",
        "58002" => "（服务已关闭，请到百度翻译开放平台开通）",
        _ => string.Empty,
    };
}
