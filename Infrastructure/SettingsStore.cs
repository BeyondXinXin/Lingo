using System.Text.Json;
using Lingo.Models;

namespace Lingo.Infrastructure;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeyondXinXin",
        "Lingo");

    // 旧版配置目录（无厂商层），仅用于一次性搬迁
    private static string LegacySettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lingo");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        MigrateLegacyDirectory();

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

        // 旧版默认 Prompt 自动迁移到新的精简 System Prompt（用户自定义的保留不动）
        foreach (CustomApiSettings api in settings.CustomApis)
        {
            if (string.Equals(api.Prompt?.Trim(), CustomApiSettings.LegacyDefaultPrompt, StringComparison.Ordinal))
            {
                api.Prompt = CustomApiSettings.DefaultPrompt;
            }
        }

        return settings;
    }

    // 旧版目录 %LocalAppData%\Lingo 一次性搬迁到 BeyondXinXin\Lingo，与其他项目的厂商目录对齐
    private static void MigrateLegacyDirectory()
    {
        try
        {
            if (!Directory.Exists(LegacySettingsDirectory))
            {
                return;
            }

            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsDirectory)!);
                Directory.Move(LegacySettingsDirectory, SettingsDirectory);
                return;
            }

            // 新目录已被提前创建（如日志）时逐个搬文件，已存在的不覆盖
            foreach (string file in Directory.GetFiles(LegacySettingsDirectory))
            {
                string target = Path.Combine(SettingsDirectory, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Move(file, target);
                }
            }

            Directory.Delete(LegacySettingsDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            AppLogger.Error("旧配置目录迁移失败", ex);
        }
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
