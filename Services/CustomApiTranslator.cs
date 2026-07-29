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

    // 面板标题优先用显示名称，其次模型名
    public string Name => !string.IsNullOrWhiteSpace(_settings.Name) ? _settings.Name
        : !string.IsNullOrWhiteSpace(_settings.Model) ? _settings.Model
        : "模型翻译";

    public async Task<TranslationResult> TranslateAsync(string text, Action<string>? onPartial, CancellationToken cancellationToken)
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
            // ResponseHeadersRead 配合 stream=true，首个 token 到达即可开始回调
            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            statusCode = (int)response.StatusCode;

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (statusCode is >= 200 and < 300
                && mediaType is not null
                && mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                return await ReadStreamAsync(response, onPartial, stopwatch, timeoutCts.Token).ConfigureAwait(false);
            }

            // 非流式回包（错误、或服务端不支持 stream）按完整 JSON 解析
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

    // 逐行读取 SSE 流：解析 delta.content 累积译文，每次追加后回调 onPartial
    private async Task<TranslationResult> ReadStreamAsync(
        HttpResponseMessage response,
        Action<string>? onPartial,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using StreamReader reader = new(stream, Encoding.UTF8);

        StringBuilder builder = new();
        string? errorMessage = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            string payload = line["data:".Length..].Trim();
            if (payload.Length == 0 || payload == "[DONE]")
            {
                if (payload.Length != 0)
                {
                    break;
                }

                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("error", out JsonElement error))
                {
                    errorMessage = error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out JsonElement message)
                        ? message.ToString()
                        : error.ToString();
                    break;
                }

                if (root.TryGetProperty("choices", out JsonElement choices)
                    && choices.ValueKind == JsonValueKind.Array
                    && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out JsonElement delta)
                    && delta.TryGetProperty("content", out JsonElement content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    string chunk = content.GetString() ?? string.Empty;
                    if (chunk.Length > 0)
                    {
                        builder.Append(chunk);
                        onPartial?.Invoke(builder.ToString());
                    }
                }
            }
            catch (JsonException)
            {
                // 单个坏块不影响后续内容，直接跳过
            }
        }

        if (errorMessage is not null)
        {
            return TranslationResult.Failure(Name, $"接口错误：{errorMessage}", stopwatch.Elapsed);
        }

        string result = builder.ToString().Trim();
        return result.Length == 0
            ? TranslationResult.Failure(Name, "接口返回了空结果。", stopwatch.Elapsed)
            : TranslationResult.Ok(Name, result, stopwatch.Elapsed);
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
            writer.WriteBoolean("stream", true);
            writer.WriteStartArray("messages");
            if (prompt.Contains("{text}", StringComparison.Ordinal))
            {
                // 兼容旧版模板：占位符替换后作为单条 user 消息发送
                WriteMessage(writer, "user", prompt.Replace("{text}", text, StringComparison.Ordinal));
            }
            else
            {
                // 固定规则放 System，User 只传待翻译文本，减小每次请求的重复 token
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
