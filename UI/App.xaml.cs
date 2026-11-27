using System.Windows;
using System.Windows.Threading;
using VendorSync.Core;

namespace VendorSync.UI;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        // 命令行模式已在 Program.Main 里处理；这里只负责 GUI。
        new MainWindow().Show();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("✖ 未处理的异常：" + e.Exception);
        Prompt.Error(null, e.Exception.Message, "出错了");
        e.Handled = true;
    }

    // 后台线程 / 任务池的未处理异常不会经过 DispatcherUnhandledException，
    // 不接住的话进程会直接闪退。这里记日志并保活。
    public void OnStartup()
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write("✖ 后台任务异常（已忽略）：" + e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write("✖ 致命异常：" + e.ExceptionObject);
    }
}
