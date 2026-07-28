using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingo.Infrastructure;
using Lingo.Models;

namespace Lingo.Services;

// 兼容 OpenAI Chat Completions 格式的翻译服务
internal sealed class CustomApiTranslator : ITranslator
{
    private readonly HttpClient _httpClient;
    private readonly CustomApiSettings _settings;

    public CustomApiTranslator(HttpClient httpClient, CustomApiSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string Name => "模型翻译";

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(_settings.Endpoint) || string.IsNullOrWhiteSpace(_settings.Model))
        {
            return TranslationResult.Failure(Name, "未配置 Endpoint 或 Model，请在设置中填写。", stopwatch.Elapsed);
        }

        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return TranslationResult.Failure(Name, "Endpoint 不是有效的 HTTP 地址。", stopwatch.Elapsed);
        }

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildRequestBody(text), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }

        int timeoutSeconds = Math.Clamp(_settings.TimeoutSeconds, 5, 300);
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        string json;
        int statusCode;
        try
        {
            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                .ConfigureAwait(false);
            statusCode = (int)response.StatusCode;
            json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Failure(Name, $"请求超时（{timeoutSeconds} 秒）。", stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Error("自定义 API 网络请求失败", ex);
            return TranslationResult.Failure(Name, $"网络错误：{ex.Message}", stopwatch.Elapsed);
        }

        return ParseResponse(json, statusCode, stopwatch);
    }

    private string BuildRequestBody(string text)
    {
        string prompt = string.IsNullOrWhiteSpace(_settings.Prompt)
            ? CustomApiSettings.DefaultPrompt
            : _settings.Prompt;

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _settings.Model);
            writer.WriteBoolean("stream", false);
            writer.WriteStartArray("messages");
            if (prompt.Contains("{text}", StringComparison.Ordinal))
            {
                WriteMessage(writer, "user", prompt.Replace("{text}", text, StringComparison.Ordinal));
            }
            else
            {
                // 没有占位符时按 system + user 的常规方式发送
                WriteMessage(writer, "system", prompt);
                WriteMessage(writer, "user", text);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    private TranslationResult ParseResponse(string json, int statusCode, Stopwatch stopwatch)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                string message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement errorMessage)
                    ? errorMessage.ToString()
                    : error.ToString();
                return TranslationResult.Failure(Name, $"接口错误：{message}", stopwatch.Elapsed);
            }

            if (statusCode is < 200 or >= 300)
            {
                return TranslationResult.Failure(Name, $"HTTP {statusCode}，请检查 Endpoint 和 API Key。", stopwatch.Elapsed);
            }

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out JsonElement message2)
                && message2.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.String)
            {
                string result = content.GetString()?.Trim() ?? string.Empty;
                if (result.Length == 0)
                {
                    return TranslationResult.Failure(Name, "接口返回了空结果。", stopwatch.Elapsed);
                }

                return TranslationResult.Ok(Name, result, stopwatch.Elapsed);
            }

            return TranslationResult.Failure(Name, "接口返回格式异常，无法解析出译文。", stopwatch.Elapsed);
        }
        catch (JsonException)
        {
            string hint = statusCode is < 200 or >= 300 ? $"HTTP {statusCode}，" : string.Empty;
            return TranslationResult.Failure(Name, $"{hint}接口返回内容无法解析。", stopwatch.Elapsed);
        }
    }
}
