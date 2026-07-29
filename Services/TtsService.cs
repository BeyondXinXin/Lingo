using System.Runtime.InteropServices;
using System.Text;
using Lingo.Infrastructure;

namespace Lingo.Services;

// 朗读调度：点击时按需合成（Edge TTS）并经 winmm MCI 播放 MP3
// MCI 设备窗口归属发起 open 的线程，必须有消息泵，因此所有 MCI 操作固定在 UI 线程执行
// 同一内容再次点击 = 停止；播放结束后再点 = 重新播放；无常驻线程，仅播放期间有一个低频轮询定时器
internal static class TtsService
{
    private const string Alias = "LingoTts";

    private static CancellationTokenSource? _cts;
    private static System.Windows.Forms.Timer? _pollTimer;
    private static string? _activeKey;   // 正在合成或播放的内容标识
    private static string? _cachedKey;   // 临时文件中已缓存音频的内容标识（重复播放免重新合成）
    private static bool _deviceOpen;

    private static string TempFile => Path.Combine(Path.GetTempPath(), "Lingo-tts.mp3");

    // 点击朗读入口（仅限 UI 线程）：同一内容播放中则停止，否则停掉旧的开始播新的
    public static void Toggle(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        string voice = PickVoice(text);
        string key = voice + "\n" + text;
        if (_activeKey == key)
        {
            Stop();
            return;
        }

        Stop();
        _activeKey = key;
        if (_cachedKey == key)
        {
            StartPlayback();
            return;
        }

        SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();
        CancellationTokenSource cts = new();
        _cts = cts;
        _ = Task.Run(() => SynthesizeAsync(text, voice, key, ui, cts.Token));
    }

    // 停止合成与播放（仅限 UI 线程）
    public static void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _activeKey = null;
        _pollTimer?.Stop();
        if (_deviceOpen)
        {
            _deviceOpen = false;
            Mci($"stop {Alias}");
            Mci($"close {Alias}");
        }
    }

    // 中文用中文女声，其余按英文朗读
    private static string PickVoice(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF')
            {
                return "zh-CN-XiaoxiaoNeural";
            }
        }

        return "en-US-JennyNeural";
    }

    // 后台合成音频写入临时文件，完成后回到 UI 线程开始播放
    private static async Task SynthesizeAsync(
        string text, string voice, string key, SynchronizationContext ui, CancellationToken ct)
    {
        try
        {
            byte[] audio = await EdgeTtsClient.SynthesizeAsync(text, voice, ct).ConfigureAwait(false);
            if (audio.Length == 0)
            {
                throw new InvalidOperationException("语音服务返回了空音频。");
            }

            await File.WriteAllBytesAsync(TempFile, audio, ct).ConfigureAwait(false);
            _cachedKey = key;
            ui.Post(_ =>
            {
                if (!ct.IsCancellationRequested && _activeKey == key)
                {
                    StartPlayback();
                }
            }, null);
        }
        catch (OperationCanceledException)
        {
            // 用户停止或超时护栏触发，无需处理
        }
        catch (Exception ex)
        {
            AppLogger.Error("朗读失败", ex);
            ui.Post(_ =>
            {
                if (_activeKey == key)
                {
                    _activeKey = null;
                }
            }, null);
        }
    }

    private static void StartPlayback()
    {
        Mci($"open \"{TempFile}\" type mpegvideo alias {Alias}");
        Mci($"play {Alias} from 0");
        _deviceOpen = true;
        // 低频轮询检测播放结束后释放 MCI 设备；仅播放期间运行
        _pollTimer ??= CreatePollTimer();
        _pollTimer.Start();
    }

    private static System.Windows.Forms.Timer CreatePollTimer()
    {
        System.Windows.Forms.Timer timer = new() { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            if (!_deviceOpen)
            {
                _pollTimer!.Stop();
                return;
            }

            StringBuilder mode = new(32);
            _ = mciSendString($"status {Alias} mode", mode, mode.Capacity, IntPtr.Zero);
            if (!mode.ToString().Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                Stop();
            }
        };
        return timer;
    }

    private static void Mci(string command) => mciSendString(command, null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);
}
