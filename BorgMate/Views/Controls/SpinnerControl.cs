using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace BorgMate.Views.Controls;

/// <summary>
/// macOS-style indeterminate spinner with 8 radiating lines that fade sequentially.
/// </summary>
public class SpinnerControl : Control
{
    private const int LineCount = 8;
    private int _activeIndex;
    private DispatcherTimer? _timer;

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SpinnerControl, IBrush?>(nameof(Foreground));

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static SpinnerControl()
    {
        AffectsRender<SpinnerControl>(ForegroundProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            _activeIndex = (_activeIndex + 1) % LineCount;
            InvalidateVisual();
        };
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var innerRadius = size * 0.26;
        var outerRadius = size * 0.48;
        var lineWidth = Math.Max(1.2, size * 0.14);

        var baseBrush = Foreground ?? Brushes.Gray;
        var baseColor = (baseBrush as ISolidColorBrush)?.Color ?? Colors.Gray;

        var angleStep = 360.0 / LineCount;

        for (var i = 0; i < LineCount; i++)
        {
            // Lines behind the active one fade out; the active line is darkest
            var offset = (_activeIndex - i + LineCount) % LineCount;
            var opacity = Math.Max(0.15, 1.0 - offset * (1.0 / LineCount));

            var color = new Color((byte)(opacity * baseColor.A), baseColor.R, baseColor.G, baseColor.B);
            var pen = new Pen(new SolidColorBrush(color), lineWidth, lineCap: PenLineCap.Round);

            var angle = i * angleStep - 90;
            var rad = angle * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            var p1 = new Point(center.X + cos * innerRadius, center.Y + sin * innerRadius);
            var p2 = new Point(center.X + cos * outerRadius, center.Y + sin * outerRadius);

            context.DrawLine(pen, p1, p2);
        }
    }
}
