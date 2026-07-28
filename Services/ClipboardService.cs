using System.Runtime.InteropServices;
using System.Text;

namespace Lingo.Services;

internal static class ClipboardService
{
    // 剪贴板可能被其他进程短暂占用，允许少量重试
    public static string? TryGetText()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }

        return null;
    }

    public static bool TrySetText(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    // 保留原版有价值的行为：拆开连字符、下划线和驼峰命名，便于翻译代码标识符
    public static string NormalizeForTranslation(string text)
    {
        string replaced = text.Replace('-', ' ').Replace('_', ' ');
        string[] lines = replaced.Split('\n');
        StringBuilder builder = new(replaced.Length + 16);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = CollapseWhitespace(lines[i]);
            if (i > 0)
            {
                builder.Append('\n');
            }

            for (int j = 0; j < line.Length; j++)
            {
                if (j > 0 && char.IsLower(line[j - 1]) && char.IsUpper(line[j]))
                {
                    builder.Append(' ');
                }

                builder.Append(line[j]);
            }
        }

        return builder.ToString();
    }

    private static string CollapseWhitespace(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
