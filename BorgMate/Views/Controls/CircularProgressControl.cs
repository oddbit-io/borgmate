using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BorgMate.Views.Controls;

/// <summary>Circular progress arc drawn over a track ring. Value 0–100 or null.</summary>
public class CircularProgressControl : Control
{
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<CircularProgressControl, double?>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CircularProgressControl, IBrush?>(nameof(Foreground));

    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static CircularProgressControl()
    {
        AffectsRender<CircularProgressControl>(ValueProperty, ForegroundProperty);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var strokeWidth = Math.Max(1.5, size * 0.13);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - strokeWidth) / 2;

        var foreColor = (Foreground as ISolidColorBrush)?.Color ?? Colors.Gray;

        // Track ring
        var trackColor = new Color(50, foreColor.R, foreColor.G, foreColor.B);
        var trackPen = new Pen(new SolidColorBrush(trackColor), strokeWidth);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        // Progress arc
        var progress = Math.Clamp(Value ?? 0, 0, 100);
        if (progress <= 0) return;

        var progressPen = new Pen(Foreground ?? Brushes.Gray, strokeWidth, lineCap: PenLineCap.Round);

        if (progress >= 100)
        {
            context.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var sweepAngle = progress / 100.0 * 360.0;
        const double startAngle = -90.0;
        var startRad = startAngle * Math.PI / 180;
        var endRad = (startAngle + sweepAngle) * Math.PI / 180;

        var startPoint = new Point(center.X + radius * Math.Cos(startRad), center.Y + radius * Math.Sin(startRad));
        var endPoint = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, progressPen, geometry);
    }
}
