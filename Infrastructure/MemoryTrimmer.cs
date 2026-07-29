using System.Runtime;
using System.Runtime.InteropServices;

namespace Lingo.Infrastructure;

// 托盘常驻工具的内存整理：窗口隐藏后做一次完整压缩 GC 并裁剪工作集，
// 把翻译过程中膨胀的托管堆与缓存还给系统，驻留占用回到基线
internal static class MemoryTrimmer
{
    // 延迟触发：等取消中的翻译任务与 UI 回调收尾后再整理；shouldSkip 返回 true（如窗口又被唤出）则放弃本次
    public static void TrimLater(int delayMilliseconds = 1000, Func<bool>? shouldSkip = null)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            if (shouldSkip?.Invoke() == true)
            {
                return;
            }

            Trim();
        });
    }

    public static void Trim()
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
