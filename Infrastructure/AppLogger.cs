using System.Diagnostics;

namespace Lingo.Infrastructure;

internal static class AppLogger
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly Lock Sync = new();

    private static string LogPath => Path.Combine(SettingsStore.SettingsDirectory, "lingo.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(SettingsStore.SettingsDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            // 日志本身不允许抛出异常影响主流程，仅输出到调试器
            Debug.WriteLine($"AppLogger write failed: {ex.Message}");
        }
    }

    private static void RotateIfNeeded()
    {
        FileInfo file = new(LogPath);
        if (file.Exists && file.Length > MaxLogBytes)
        {
            File.Move(LogPath, Path.Combine(SettingsStore.SettingsDirectory, "lingo.old.log"), overwrite: true);
        }
    }
}
