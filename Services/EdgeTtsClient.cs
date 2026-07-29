using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Lingo.Services;

// Microsoft Edge“大声朗读”在线 TTS：WebSocket 发送 SSML、收取 MP3 音频
// 协议与 EchoEdge（Go 版）一致；按需连接、合成完即断开，不常驻后台
internal static class EdgeTtsClient
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumFullVersion = "134.0.3124.66";
    private const string WssUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken="
        + TrustedClientToken;

    public static async Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken cancellationToken)
    {
        // 整体 30 秒护栏，避免网络异常时无限等待
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken ct = linked.Token;

        string url = $"{WssUrl}&Sec-MS-GEC={GenerateSecMsGec()}" +
                     $"&Sec-MS-GEC-Version=1-{ChromiumFullVersion}&ConnectionId={Guid.NewGuid():N}";

        using ClientWebSocket socket = new();
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            $"Chrome/{ChromiumFullVersion[..3]}.0.0.0 Safari/537.36 Edg/{ChromiumFullVersion[..3]}.0.0.0");
        await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);

        await SendTextAsync(socket, BuildSpeechConfig(), ct).ConfigureAwait(false);
        await SendTextAsync(socket, BuildSsmlMessage(text, voice), ct).ConfigureAwait(false);

        using MemoryStream audio = new();
        byte[] buffer = new byte[32 * 1024];
        while (true)
        {
            // 组装一条完整消息（可能分多帧到达）
            using MemoryStream message = new();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException($"语音服务提前断开连接：{result.CloseStatusDescription}");
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            byte[] data = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                // turn.end 表示本次合成结束
                if (Encoding.UTF8.GetString(data).Contains("Path:turn.end", StringComparison.Ordinal))
                {
                    break;
                }
            }
            else if (data.Length > 2)
            {
                // 二进制帧：前 2 字节为大端头部长度，其后是音频数据
                int headerLength = (data[0] << 8) | data[1];
                int offset = 2 + headerLength;
                if (data.Length > offset)
                {
                    audio.Write(data, offset, data.Length - offset);
                }
            }
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // 音频已收完，关闭握手失败可忽略
        }

        return audio.ToArray();
    }

    private static Task SendTextAsync(ClientWebSocket socket, string content, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(content), WebSocketMessageType.Text, endOfMessage: true, ct);

    private static string BuildSpeechConfig() =>
        $"X-Timestamp:{JsDateString()}\r\n" +
        "Content-Type:application/json; charset=utf-8\r\n" +
        "Path:speech.config\r\n\r\n" +
        "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
        "{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
        "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";

    private static string BuildSsmlMessage(string text, string voice)
    {
        string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        string ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{voice}'><prosody pitch='+0Hz' rate='+0%' volume='+0%'>{escaped}</prosody></voice></speak>";
        return $"X-RequestId:{Guid.NewGuid():N}\r\n" +
               "Content-Type:application/ssml+xml\r\n" +
               $"X-Timestamp:{JsDateString()}Z\r\n" +
               "Path:ssml\r\n\r\n" +
               ssml;
    }

    // Sec-MS-GEC 防滥用令牌：Windows 文件时间取整到 5 分钟后与 Token 拼接做 SHA256
    private static string GenerateSecMsGec()
    {
        long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L;
        seconds -= seconds % 300;
        long ticks = seconds * 10_000_000L;
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(ticks.ToString() + TrustedClientToken));
        return Convert.ToHexString(hash);
    }

    private static string JsDateString() => DateTime.UtcNow.ToString(
        "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
        System.Globalization.CultureInfo.InvariantCulture);
}
