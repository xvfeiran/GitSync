using System.Windows;

namespace VendorSync.UI;

/// <summary>
/// 统一的用户提示入口。自测/自动化运行时（AppLog.Silent）一律不弹窗，
/// 只写日志并返回默认结果，避免无人值守时被模态对话框挂死。
/// </summary>
public static class Prompt
{
    /// <summary>信息提示。静默模式下直接跳过。</summary>
    public static void Info(Window? owner, string text, string caption = "提示")
    {
        if (AppLog.Silent) { AppLog.Write($"[提示] {caption}: {text}"); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>警告提示。静默模式下写日志。</summary>
    public static void Warn(Window? owner, string text, string caption = "注意")
    {
        if (AppLog.Silent) { AppLog.Write($"[警告] {caption}: {text}"); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>错误提示。静默模式下写日志。</summary>
    public static void Error(Window? owner, string text, string caption = "出错了")
    {
        if (AppLog.Silent) { AppLog.Write($"[错误] {caption}: {text}"); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// 是非确认。静默模式下返回 <paramref name="defaultWhenSilent"/>，
    /// 绝不阻塞等待用户。
    /// </summary>
    public static bool Confirm(Window? owner, string text, string caption, bool defaultWhenSilent = false)
    {
        if (AppLog.Silent)
        {
            AppLog.Write($"[确认-自动{(defaultWhenSilent ? "是" : "否")}] {caption}: {text}");
            return defaultWhenSilent;
        }
        return MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
