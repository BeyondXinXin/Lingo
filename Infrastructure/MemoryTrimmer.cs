using System.Runtime;
using System.Runtime.InteropServices;

namespace Lingo.Infrastructure;

// 仅在启动收尾时使用的一次性内存整理：把单文件解压与 JIT 产生的启动垃圾还给系统。
// 运行期间不再整理，窗体与面板常驻复用，内存保持稳定
internal static class MemoryTrimmer
{
    // 延迟触发：等启动初始化与预建控件收尾后再整理
    public static void TrimLater(int delayMilliseconds)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            Trim();
        });
    }

    private static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch
        {
            // 内存整理只是优化，失败不影响任何功能
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimumWorkingSetSize, nint maximumWorkingSetSize);
}
