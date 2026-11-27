using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VendorSync.UI.Controls;

public enum NeuDepth
{
    /// <summary>从底板上凸起（双向柔光阴影）。</summary>
    Raised,
    /// <summary>压进底板（内阴影）。</summary>
    Inset,
    /// <summary>平的，只有底色。</summary>
    Flat
}

/// <summary>
/// 新拟态面板：凸起 / 凹陷 / 平面。阴影只画在独立的「影子层」上，
/// 内容本身不挂 Effect，文字始终清晰。
/// 软件渲染（老电脑、远程桌面）时 <see cref="ReducedEffects"/> 为 true，
/// 自动退化为 1px 描边，不再生成阴影特效。
/// </summary>
[TemplatePart(Name = "PART_Dark", Type = typeof(Border))]
[TemplatePart(Name = "PART_Light", Type = typeof(Border))]
[TemplatePart(Name = "PART_InsetHost", Type = typeof(Grid))]
[TemplatePart(Name = "PART_InsetDark", Type = typeof(Border))]
[TemplatePart(Name = "PART_InsetLight", Type = typeof(Border))]
[TemplatePart(Name = "PART_Face", Type = typeof(Border))]
public class Neu : ContentControl
{
    /// <summary>全局开关：简洁模式（不画阴影）。启动时按显卡渲染能力决定。</summary>
    public static bool ReducedEffects { get; set; }

    public static readonly Color ShadowColor = Color.FromRgb(0xA3, 0xB1, 0xC6);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(Neu),
        new FrameworkPropertyMetadata(new CornerRadius(16), (d, _) => ((Neu)d).UpdateVisuals()));

    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(NeuDepth), typeof(Neu),
        new FrameworkPropertyMetadata(NeuDepth.Raised, (d, _) => ((Neu)d).UpdateVisuals()));

    public static readonly DependencyProperty ShadowSizeProperty = DependencyProperty.Register(
        nameof(ShadowSize), typeof(double), typeof(Neu),
        new FrameworkPropertyMetadata(7.0, (d, _) => ((Neu)d).UpdateVisuals(true)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public NeuDepth Depth
    {
        get => (NeuDepth)GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public double ShadowSize
    {
        get => (double)GetValue(ShadowSizeProperty);
        set => SetValue(ShadowSizeProperty, value);
    }

    private Border? _dark, _light, _insetDark, _insetLight, _face;
    private Grid? _insetHost;

    public Neu()
    {
        SizeChanged += (_, _) => UpdateClip();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dark = GetTemplateChild("PART_Dark") as Border;
        _light = GetTemplateChild("PART_Light") as Border;
        _insetHost = GetTemplateChild("PART_InsetHost") as Grid;
        _insetDark = GetTemplateChild("PART_InsetDark") as Border;
        _insetLight = GetTemplateChild("PART_InsetLight") as Border;
        _face = GetTemplateChild("PART_Face") as Border;
        UpdateVisuals(true);
    }

    private void UpdateVisuals(bool rebuildEffects = false)
    {
        if (_face is null) return;
        var s = ShadowSize;
        var raised = Depth == NeuDepth.Raised;
        var inset = Depth == NeuDepth.Inset;
        var reduced = ReducedEffects;

        if (_dark is not null && _light is not null)
        {
            _dark.Visibility = _light.Visibility = raised && !reduced ? Visibility.Visible : Visibility.Collapsed;
            if (rebuildEffects || _dark.Effect is null)
            {
                _dark.Effect = Shadow(ShadowColor, 315, s, 0.55);
                _light.Effect = Shadow(Colors.White, 135, s, 0.95);
            }
        }

        if (_insetHost is not null && _insetDark is not null && _insetLight is not null)
        {
            _insetHost.Visibility = inset && !reduced ? Visibility.Visible : Visibility.Collapsed;
            var t = Math.Max(4, s * 1.6);
            foreach (var (b, sign, color) in new[] { (_insetDark, 1.0, ShadowColor), (_insetLight, -1.0, Colors.White) })
            {
                b.BorderThickness = new Thickness(t);
                b.Margin = new Thickness(-t);
                b.BorderBrush = new SolidColorBrush(color) { Opacity = sign > 0 ? 0.75 : 0.9 };
                var r = CornerRadius;
                b.CornerRadius = new CornerRadius(r.TopLeft + t, r.TopRight + t, r.BottomRight + t, r.BottomLeft + t);
                b.RenderTransform = new TranslateTransform(sign * s * 0.45, sign * s * 0.45);
                if (rebuildEffects || b.Effect is null)
                    b.Effect = new BlurEffect { Radius = s * 1.4, RenderingBias = RenderingBias.Performance };
            }
        }

        // 简洁模式：用细描边代替阴影，仍然能分出层次。
        _face.BorderThickness = reduced && Depth != NeuDepth.Flat ? new Thickness(1) : new Thickness(0);
        _face.BorderBrush = new SolidColorBrush(inset ? Color.FromRgb(0xC5, 0xCD, 0xD9) : Color.FromRgb(0xD3, 0xDA, 0xE4));
        UpdateClip();
    }

    private static DropShadowEffect Shadow(Color color, double direction, double size, double opacity) => new()
    {
        Color = color,
        Direction = direction,
        ShadowDepth = size * 0.75,
        BlurRadius = size * 2.2,
        Opacity = opacity,
        RenderingBias = RenderingBias.Performance
    };

    /// <summary>内阴影必须按圆角裁剪，否则角上会漏出来。</summary>
    private void UpdateClip()
    {
        if (_insetHost is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var r = Math.Min(CornerRadius.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2);
        _insetHost.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), r, r);
    }
}
