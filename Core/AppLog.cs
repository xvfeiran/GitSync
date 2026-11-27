using System.Text;

namespace VendorSync.Core;

/// <summary>
/// 全局日志通道。核心层不直接依赖界面，
/// 通过这里把消息转发给已注册的接收者（主窗口）。
/// </summary>
public static class AppLog
{
    private static Action<string>? _sink;
    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();
    private const int MaxBuffered = 4000;

    /// <summary>
    /// 静默模式：为 true 时界面不得弹模态对话框。
    /// 自测在无人值守下运行，任何 MessageBox 都会让进程永久挂起。
    /// </summary>
    public static bool Silent { get; set; }

    public static void Attach(Action<string> sink)
    {
        lock (Gate)
        {
            _sink = sink;
            if (Buffer.Length > 0)
            {
                sink(Buffer.ToString());
                Buffer.Clear();
            }
        }
    }

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        Action<string>? sink;
        lock (Gate)
        {
            sink = _sink;
            if (sink is null)
            {
                if (Buffer.Length < MaxBuffered)
                {
                    Buffer.Append(message).Append(Environment.NewLine);
                }
                return;
            }
        }

        try { sink(message); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}