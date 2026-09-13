using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WinNUT.App.Controls;

/// <summary>
/// A modern radial progress-ring gauge: a muted background track plus a colored progress arc
/// (red→green along the filled length) with a large centered value readout — no needle, no
/// tick-mark clutter, just min/max shown subtly at the arc's ends. Replaces the original app's
/// classic automotive-dial look (UPSVarGauge/AGaugeClassic) with a flatter, dashboard-style
/// presentation while keeping the same data contract (Value/Value2, Min/MaxValue, Unit/Unit2,
/// Label) so call sites didn't need to change.
/// </summary>
public sealed class UpsGaugeControl : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<UpsGaugeControl, double>(nameof(Value));

    public static readonly StyledProperty<double?> Value2Property =
        AvaloniaProperty.Register<UpsGaugeControl, double?>(nameof(Value2));

    public static readonly StyledProperty<double> MinValueProperty =
        AvaloniaProperty.Register<UpsGaugeControl, double>(nameof(MinValue), 0);

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<UpsGaugeControl, double>(nameof(MaxValue), 100);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<UpsGaugeControl, string>(nameof(Unit), string.Empty);

    public static readonly StyledProperty<string?> Unit2Property =
        AvaloniaProperty.Register<UpsGaugeControl, string?>(nameof(Unit2));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<UpsGaugeControl, string>(nameof(Label), string.Empty);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double? Value2
    {
        get => GetValue(Value2Property);
        set => SetValue(Value2Property, value);
    }

    public double MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public string Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string? Unit2
    {
        get => GetValue(Unit2Property);
        set => SetValue(Unit2Property, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private const double StartAngleDeg = 135;
    private const double SweepDeg = 270;

    static UpsGaugeControl()
    {
        AffectsRender<UpsGaugeControl>(ValueProperty, Value2Property, MinValueProperty, MaxValueProperty, UnitProperty, Unit2Property, LabelProperty, BoundsProperty);
    }

    public UpsGaugeControl()
    {
        Width = 158;
        Height = 158;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2 + size * 0.03);
        var radius = size * 0.40;
        var thickness = Math.Max(6, radius * 0.16);

        var min = MinValue;
        var max = MaxValue;
        if (max <= min) max = min + 1;
        var fraction = Math.Clamp((Value - min) / (max - min), 0, 1);

        var trackBrush = GetBrush("SurfaceBorderBrush", new Color(255, 60, 62, 68));
        var textPrimary = GetBrush("TextPrimaryBrush", Colors.White);
        var textSecondary = GetBrush("TextSecondaryBrush", Color.Parse("#9AA0A8"));

        DrawTrack(context, center, radius, thickness, trackBrush);
        if (fraction > 0)
        {
            DrawProgressArc(context, center, radius, thickness, fraction);
        }
        DrawEndLabels(context, center, radius, min, max, textSecondary);
        DrawCenterText(context, center, textPrimary, textSecondary);
        DrawTopLabel(context, textSecondary);
    }

    private IBrush GetBrush(string resourceKey, Color fallback)
    {
        if (this.TryFindResource(resourceKey, out var value) && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private static StreamGeometry ArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        ctx.BeginFigure(start, false);
        ctx.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise);
        return geometry;
    }

    private void DrawTrack(DrawingContext context, Point center, double radius, double thickness, IBrush brush)
    {
        var geometry = ArcGeometry(center, radius, StartAngleDeg, SweepDeg);
        var pen = new Pen(brush, thickness, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawProgressArc(DrawingContext context, Point center, double radius, double thickness, double fraction)
    {
        var sweep = SweepDeg * fraction;
        var geometry = ArcGeometry(center, radius, StartAngleDeg, sweep);

        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#EF4444"), 0),
                new GradientStop(Color.Parse("#F59E0B"), 0.5),
                new GradientStop(Color.Parse("#22C55E"), 1),
            },
        };

        var pen = new Pen(gradientBrush, thickness, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawEndLabels(DrawingContext context, Point center, double radius, double min, double max, IBrush brush)
    {
        DrawSmallLabel(context, min.ToString("0"), PointOnCircle(center, radius * 1.26, StartAngleDeg), brush);
        DrawSmallLabel(context, max.ToString("0"), PointOnCircle(center, radius * 1.26, StartAngleDeg + SweepDeg), brush);
    }

    private static void DrawSmallLabel(DrawingContext context, string text, Point at, IBrush brush)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 9.5, brush);
        context.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
    }

    private void DrawCenterText(DrawingContext context, Point center, IBrush primary, IBrush secondary)
    {
        var valueStr = Value.ToString("0.0");
        var valueText = new FormattedText(valueStr, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.Bold), 21, primary);

        var unitText = string.IsNullOrEmpty(Unit) ? null : new FormattedText(Unit, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, secondary);

        var hasSecondLine = Value2 is not null;
        var blockHeight = valueText.Height + (unitText?.Height ?? 0) * 0.6 + (hasSecondLine ? 16 : 0);
        var y = center.Y - blockHeight / 2;

        var totalWidth = valueText.Width + (unitText is null ? 0 : unitText.Width + 4);
        var x = center.X - totalWidth / 2;
        context.DrawText(valueText, new Point(x, y));
        if (unitText is not null)
        {
            context.DrawText(unitText, new Point(x + valueText.Width + 4, y + valueText.Height - unitText.Height - 1));
        }

        if (Value2 is { } v2)
        {
            var line2 = $"{v2:0.0}{(string.IsNullOrEmpty(Unit2) ? "" : " " + Unit2)}";
            var text2 = new FormattedText(line2, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 11.5, secondary);
            context.DrawText(text2, new Point(center.X - text2.Width / 2, y + valueText.Height + 3));
        }
    }

    private void DrawTopLabel(DrawingContext context, IBrush brush)
    {
        if (string.IsNullOrEmpty(Label)) return;

        var labelText = new FormattedText(Label.ToUpperInvariant(), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 10, brush);
        context.DrawText(labelText, new Point(Bounds.Width / 2 - labelText.Width / 2, 0));
    }
}
