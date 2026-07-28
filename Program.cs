using Lingo.Infrastructure;

namespace Lingo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex singleInstanceMutex = new(initiallyOwned: true, "Lingo_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在托盘常驻，直接退出
            return;
        }

        Application.ThreadException += (_, e) => AppLogger.Error("UI 线程未处理异常", e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLogger.Error("应用程序域未处理异常", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("后台任务未观察异常", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // WinForms 深色模式尚在评估期 API，行为已稳定
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        // 与系统输入法候选窗观感一致的字体字号，避免小字发虚
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 10.5F));
        Application.Run(new TrayApplicationContext());
    }
}
