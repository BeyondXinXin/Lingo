namespace Lingo.Models;

internal sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+T";
    public bool LaunchAtStartup { get; set; }
    public string DefaultTargetLanguage { get; set; } = "zh";
    public BaiduSettings Baidu { get; set; } = new();
    public CustomApiSettings CustomApi { get; set; } = new();
    public WindowBounds TranslateWindow { get; set; } = new();
}

internal sealed class BaiduSettings
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = "auto";

    // 留空表示跟随全局默认目标语言
    public string TargetLanguage { get; set; } = string.Empty;
}

internal sealed class CustomApiSettings
{
    // {text} 为原文占位符；不含占位符时 Prompt 作为 system 消息、原文作为 user 消息发送
    public static readonly string DefaultPrompt =
        "你是一个严格遵循规则的机器翻译官，仅执行精准的跨语言转换。" +
        "规则：1.当输入为中文时，翻译为英文；2.当输入为非中文时，翻译为简体中文；" +
        "3.保持原始格式结构，仅替换语言文字部分。" +
        "禁止添加任何解释、注释或表情，只输出译文。\n\n待翻译文本：\n{text}";

    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Prompt { get; set; } = DefaultPrompt;
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
