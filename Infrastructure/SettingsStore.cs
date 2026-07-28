using System.Text.Json;
using Lingo.Models;

namespace Lingo.Infrastructure;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lingo");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            return Migrate(settings ?? new AppSettings());
        }
        catch (Exception ex)
        {
            AppLogger.Error("配置文件读取失败，已恢复默认配置", ex);
            BackupCorruptedFile();
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception ex)
        {
            AppLogger.Error("配置保存失败", ex);
        }
    }

    // 旧版配置只有单个 CustomApi 字段，加载后并入 CustomApis 列表
    private static AppSettings Migrate(AppSettings settings)
    {
        if (settings.CustomApi is not null)
        {
            if (settings.CustomApis.Count == 0)
            {
                settings.CustomApis.Add(settings.CustomApi);
            }

            settings.CustomApi = null;
        }

        return settings;
    }

    private static void BackupCorruptedFile()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Move(SettingsPath, Path.Combine(SettingsDirectory, "settings.corrupted.json"), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("损坏配置文件备份失败", ex);
        }
    }
}
