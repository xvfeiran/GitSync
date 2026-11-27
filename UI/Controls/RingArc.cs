using System.Windows;
using System.Windows.Media;

namespace VendorSync.UI.Controls;

/// <summary>圆环进度：底轨 + 一段从 12 点方向顺时针扫过的弧。Progress 可直接做动画。</summary>
public class RingArc : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RingArc),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingArc),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(RingArc),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingArc),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Thickness;
        var r = Math.Min(ActualWidth, ActualHeight) / 2 - t / 2;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        if (TrackBrush is not null) dc.DrawEllipse(null, new Pen(TrackBrush, t), c, r, r);

        var p = Math.Clamp(Progress, 0, 1);
        if (p <= 0.001) return;
        var pen = new Pen(Stroke, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (p >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }

        Point At(double deg)
        {
            var a = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }

        const double start = -90;
        var end = start + 360 * p;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(start), false, false);
            ctx.ArcTo(At(end), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
