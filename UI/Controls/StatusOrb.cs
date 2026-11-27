using System.Windows;
using System.Windows.Controls;

namespace VendorSync.UI.Controls;

public enum OrbState
{
    Unknown,
    Running,
    UpToDate,
    Pending,
    Failed
}

/// <summary>
/// 状态核：凹槽圆环里的一颗彩色发光圆。外观与动画全部在 Theme.xaml 的模板里，
/// 进行中外圈旋转，待处理 / 失败时呼吸发光。<see cref="Text"/> 用于显示数字（例如待同步数）。
/// </summary>
public class StatusOrb : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(OrbState), typeof(StatusOrb), new FrameworkPropertyMetadata(OrbState.Unknown));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusOrb), new FrameworkPropertyMetadata(string.Empty));

    public OrbState State { get => (OrbState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}
