using System.Windows;
using VendorSync;
using VendorSync.Core;
using VendorSync.UI;

namespace VendorSync;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 命令行模式不创建 WPF Application，避免与窗口/调度器纠缠。
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                return SelfTest.RunAll();
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory("selftest-crash");
                    File.WriteAllText("selftest-crash\\crash.txt", ex.ToString());
                }
                catch { /* 尽力而为 */ }
                return 3;
            }
        }

        if (args.Any(a => a.Equals("--inspect", StringComparison.OrdinalIgnoreCase)))
        {
            return HeadlessInspect.Run();
        }

        if (args.Any(a => a.Equals("--version", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("GitSync 1.1.0");
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        app.OnStartup();
        app.Run();
        return Environment.ExitCode;
    }
}
