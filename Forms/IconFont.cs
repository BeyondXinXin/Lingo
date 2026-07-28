using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Lingo.Forms;

// Material Symbols Rounded 图标字体（取自 AIxyz 的子集字体），用于绘制工具图标
internal static class IconFont
{
    // 与 AIxyz StyleUtil.h 中 IconType 一致的码点
    public const char ContentCopy = '\uE14D'; // 复制
    public const char Headphones = '\uF01F';  // 朗读翻译结果
    public const char MediaOutput = '\uF4F2'; // 朗读原文

    private static readonly PrivateFontCollection Collection = LoadCollection();

    public static Font Create(float size) => new(Collection.Families[0], size, GraphicsUnit.Point);

    private static PrivateFontCollection LoadCollection()
    {
        PrivateFontCollection collection = new();
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Lingo.Assets.MaterialSymbolsRounded.ttf");
        if (stream is null)
        {
            throw new InvalidOperationException("缺少内嵌图标字体资源 MaterialSymbolsRounded.ttf");
        }

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);

        // AddMemoryFont 要求内存在字体生命周期内有效，进程级持有不释放
        IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
        Marshal.Copy(data, 0, memory, data.Length);
        collection.AddMemoryFont(memory, data.Length);
        return collection;
    }
}
