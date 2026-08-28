using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

/// <summary>
/// A lightweight horizontal time scale used only by an independent indicator
/// workspace. Chart-attached indicator panes continue to use the price chart's
/// existing time scale.
/// </summary>
public sealed class IndicatorTimeScaleControl : FrameworkElement
{
    private const double ValueScaleWidth = 48.0;
    private IReadOnlyList<Candle> _candles = Array.Empty<Candle>();
    private ChartViewportSnapshot? _viewport;
    private ChartSettings _settings = ChartSettings.Default;

    public IReadOnlyList<Candle> Candles
    {
        get => _candles;
        set
        {
            _candles = value ?? Array.Empty<Candle>();
            InvalidateVisual();
        }
    }

    public ChartViewportSnapshot? Viewport
    {
        get => _viewport;
        set
        {
            _viewport = value;
            InvalidateVisual();
        }
    }

    public ChartSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? ChartSettings.Default;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect bounds = new(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        if (bounds.IsEmpty)
            return;

        Brush background = BrushFrom(Settings.ChartBackgroundColor, Color.FromRgb(8, 8, 8));
        Brush grid = BrushFrom(Settings.GridColor, Color.FromRgb(55, 55, 55));
        Brush textBrush = BrushFrom(Settings.TimeScaleTextColor, Color.FromRgb(145, 164, 186));
        drawingContext.DrawRectangle(background, null, bounds);
        drawingContext.DrawLine(new Pen(grid, 1), new Point(0, 0.5), new Point(bounds.Right, 0.5));

        ChartViewportSnapshot? viewport = Viewport;
        if (viewport is null || Candles.Count == 0 || viewport.LastExclusive <= viewport.FirstIndex)
            return;

        int first = Math.Clamp(viewport.FirstIndex, 0, Candles.Count - 1);
        int last = Math.Clamp(viewport.LastExclusive, first + 1, Candles.Count);
        int visible = Math.Max(1, last - first);
        int step = Math.Max(1, visible / 6);
        double plotWidth = Math.Max(1, ActualWidth - ValueScaleWidth);
        int slotCount = Math.Max(1, viewport.SlotCount);

        for (int visibleIndex = 0; visibleIndex < visible; visibleIndex += step)
        {
            int candleIndex = first + visibleIndex;
            if (candleIndex < 0 || candleIndex >= Candles.Count)
                continue;

            Candle candle = Candles[candleIndex];
            DateTimeOffset time = candle.StartTime.ToUniversalTime();
            string format = visible > 180 ? "dd MMM" : "HH:mm";
            FormattedText text = new(
                time.ToString(format, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            int slot = visibleIndex < viewport.VisibleSlots.Count
                ? viewport.VisibleSlots[visibleIndex]
                : visibleIndex;
            double centerX = plotWidth * (slot + 0.5) / slotCount;
            drawingContext.DrawText(
                text,
                new Point(
                    Math.Clamp(centerX - text.Width / 2, 0, Math.Max(0, plotWidth - text.Width)),
                    Math.Max(2, (ActualHeight - text.Height) / 2)));
        }
    }

    private static SolidColorBrush BrushFrom(string? value, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        catch
        {
        }
        return new SolidColorBrush(fallback);
    }
}
