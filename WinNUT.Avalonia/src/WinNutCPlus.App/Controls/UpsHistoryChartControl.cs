using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WinNutCPlus.App.Models;

namespace WinNutCPlus.App.Controls;

/// <summary>
/// A combined, linear-gradient-filled live area chart of the three metrics most useful for
/// spotting spikes over the app's running time — load, battery charge, and input voltage — drawn
/// as overlaid, semi-transparent series sharing one plot area rather than three separate charts.
/// Custom-drawn (DrawingContext) to match <see cref="UpsGaugeControl"/> rather than pulling in a
/// third-party charting package for what's a fairly simple shape.
/// </summary>
public sealed class UpsHistoryChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistorySample>> SamplesProperty =
        AvaloniaProperty.Register<UpsHistoryChartControl, IReadOnlyList<HistorySample>>(
            nameof(Samples), Array.Empty<HistorySample>());

    public static readonly StyledProperty<double> InVMinProperty =
        AvaloniaProperty.Register<UpsHistoryChartControl, double>(nameof(InVMin));

    public static readonly StyledProperty<double> InVMaxProperty =
        AvaloniaProperty.Register<UpsHistoryChartControl, double>(nameof(InVMax), 300);

    public IReadOnlyList<HistorySample> Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double InVMin
    {
        get => GetValue(InVMinProperty);
        set => SetValue(InVMinProperty, value);
    }

    public double InVMax
    {
        get => GetValue(InVMaxProperty);
        set => SetValue(InVMaxProperty, value);
    }

    private static readonly Color LoadColor = Color.Parse("#4F7CFF");
    private static readonly Color BatteryColor = Color.Parse("#22C55E");
    private static readonly Color InputVoltageColor = Color.Parse("#F59E0B");

    static UpsHistoryChartControl()
    {
        AffectsRender<UpsHistoryChartControl>(SamplesProperty, InVMinProperty, InVMaxProperty, BoundsProperty);
    }

    private int? _hoverIndex;

    public UpsHistoryChartControl()
    {
        MinHeight = 140;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHover(e.GetPosition(this).X);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIndex is null) return;
        _hoverIndex = null;
        InvalidateVisual();
    }

    private void UpdateHover(double pointerX)
    {
        var samples = Samples;
        if (samples.Count < 2 || Bounds.Width <= 0) return;

        var fraction = Math.Clamp(pointerX / Bounds.Width, 0, 1);
        var index = Math.Clamp((int)Math.Round(fraction * (samples.Count - 1)), 0, samples.Count - 1);
        if (_hoverIndex == index) return;

        _hoverIndex = index;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var gridBrush = GetBrush("SurfaceBorderBrush", new Color(255, 60, 62, 68));
        DrawGridLines(context, width, height, gridBrush);

        var samples = Samples;
        if (samples.Count < 2)
        {
            DrawEmptyState(context, width, height);
            return;
        }

        var invMin = InVMin;
        var invMax = InVMax;
        if (invMax <= invMin) invMax = invMin + 1;

        Func<HistorySample, double> loadFraction = s => Math.Clamp(s.Load / 100.0, 0, 1);
        Func<HistorySample, double> batteryFraction = s => Math.Clamp(s.BatteryCharge / 100.0, 0, 1);
        Func<HistorySample, double> inputVFraction = s => Math.Clamp((s.InputVoltage - invMin) / (invMax - invMin), 0, 1);

        DrawSeries(context, samples, width, height, LoadColor, loadFraction);
        DrawSeries(context, samples, width, height, BatteryColor, batteryFraction);
        DrawSeries(context, samples, width, height, InputVoltageColor, inputVFraction);

        if (_hoverIndex is { } hoverIndex && hoverIndex < samples.Count)
        {
            DrawHover(context, samples, hoverIndex, width, height, loadFraction, batteryFraction, inputVFraction);
        }
    }

    private static void DrawSeries(DrawingContext context, IReadOnlyList<HistorySample> samples, double width,
        double height, Color color, Func<HistorySample, double> normalize)
    {
        const double topInset = 4;
        const double bottomInset = 2;
        var plotHeight = Math.Max(1, height - topInset - bottomInset);

        var points = new Point[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var x = samples.Count == 1 ? width : width * i / (samples.Count - 1);
            var y = topInset + plotHeight * (1 - normalize(samples[i]));
            points[i] = new Point(x, y);
        }

        // Area fill: the line path, then straight down to the baseline and back to the start.
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i]);
            }
            ctx.LineTo(new Point(points[^1].X, height));
            ctx.LineTo(new Point(points[0].X, height));
            ctx.EndFigure(isClosed: true);
        }

        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, height, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(255 * 0.35), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        context.DrawGeometry(gradient, null, area);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i]);
            }
            ctx.EndFigure(isClosed: false);
        }

        var pen = new Pen(new SolidColorBrush(color), 1.75, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, line);
    }

    private void DrawHover(DrawingContext context, IReadOnlyList<HistorySample> samples, int index, double width,
        double height, Func<HistorySample, double> loadFraction, Func<HistorySample, double> batteryFraction,
        Func<HistorySample, double> inputVFraction)
    {
        const double topInset = 4;
        const double bottomInset = 2;
        var plotHeight = Math.Max(1, height - topInset - bottomInset);
        var x = samples.Count == 1 ? width : width * index / (samples.Count - 1);
        var sample = samples[index];

        var crosshairBrush = GetBrush("TextSecondaryBrush", Color.Parse("#9AA0A8"));
        context.DrawLine(new Pen(crosshairBrush, 1, dashStyle: DashStyle.Dash), new Point(x, topInset), new Point(x, height - bottomInset));

        var surfaceBrush = GetBrush("SurfaceBgBrush", Color.Parse("#212226"));
        foreach (var (color, fraction) in new[]
                 {
                     (LoadColor, loadFraction(sample)),
                     (BatteryColor, batteryFraction(sample)),
                     (InputVoltageColor, inputVFraction(sample)),
                 })
        {
            var y = topInset + plotHeight * (1 - fraction);
            context.DrawEllipse(new SolidColorBrush(color), new Pen(surfaceBrush, 1.5), new Point(x, y), 3.5, 3.5);
        }

        DrawTooltip(context, sample, x, width);
    }

    private void DrawTooltip(DrawingContext context, HistorySample sample, double anchorX, double width)
    {
        var lines = new[]
        {
            sample.Timestamp.ToString("HH:mm:ss"),
            $"{Localization.Localize.Get("GaugeLoadPower")}: {sample.Load:0.0}%",
            $"{Localization.Localize.Get("GaugeBattery")}: {sample.BatteryCharge:0.0}%",
            $"{Localization.Localize.Get("GaugeInputV")}: {sample.InputVoltage:0.0} V",
        };

        var textBrush = GetBrush("TextPrimaryBrush", Colors.White);
        var formatted = new FormattedText[lines.Length];
        double boxWidth = 0;
        double boxHeight = 8;
        for (var i = 0; i < lines.Length; i++)
        {
            var typeface = i == 0 ? new Typeface(FontFamily.Default, weight: FontWeight.SemiBold) : Typeface.Default;
            formatted[i] = new FormattedText(lines[i], System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, textBrush);
            boxWidth = Math.Max(boxWidth, formatted[i].Width);
            boxHeight += formatted[i].Height + 2;
        }
        boxWidth += 16;

        var boxX = anchorX + 10;
        if (boxX + boxWidth > width) boxX = anchorX - boxWidth - 10;
        boxX = Math.Clamp(boxX, 0, Math.Max(0, width - boxWidth));
        const double boxY = 4;

        var bgBrush = GetBrush("SurfaceBgBrush", Color.Parse("#212226"));
        var borderBrush = GetBrush("SurfaceBorderBrush", Color.Parse("#33353B"));
        context.DrawRectangle(bgBrush, new Pen(borderBrush, 1), new RoundedRect(new Rect(boxX, boxY, boxWidth, boxHeight), 6));

        var textY = boxY + 4;
        foreach (var t in formatted)
        {
            context.DrawText(t, new Point(boxX + 8, textY));
            textY += t.Height + 2;
        }
    }

    private static void DrawGridLines(DrawingContext context, double width, double height, IBrush brush)
    {
        var pen = new Pen(brush, 1);
        for (var i = 1; i < 4; i++)
        {
            var y = height * i / 4;
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    private void DrawEmptyState(DrawingContext context, double width, double height)
    {
        var text = new FormattedText(
            Localization.Localize.Get("MainHistoryCollecting"),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 12,
            GetBrush("TextSecondaryBrush", Color.Parse("#9AA0A8")));
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    private IBrush GetBrush(string resourceKey, Color fallback)
    {
        if (this.TryFindResource(resourceKey, out var value) && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
