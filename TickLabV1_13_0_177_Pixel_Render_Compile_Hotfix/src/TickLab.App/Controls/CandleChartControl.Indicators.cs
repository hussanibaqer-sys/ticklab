using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Indicators;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl
{
    private IReadOnlyList<BuiltInIndicatorResult> _builtInIndicatorOverlays = Array.Empty<BuiltInIndicatorResult>();

    public IReadOnlyList<BuiltInIndicatorResult> BuiltInIndicatorOverlays
    {
        get => _builtInIndicatorOverlays;
        set
        {
            _builtInIndicatorOverlays = (value ?? Array.Empty<BuiltInIndicatorResult>())
                .Where(item => item.Placement == BuiltInIndicatorPlacement.Overlay)
                .ToArray();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<double> GetBuiltInIndicatorValuesAt(int candleIndex)
    {
        if (candleIndex < 0)
            return Array.Empty<double>();
        var values = new List<double>();
        foreach (BuiltInIndicatorResult result in _builtInIndicatorOverlays)
        {
            foreach (IndicatorSeriesResult series in result.Series)
            {
                int sourceIndex = candleIndex - series.Shift;
                if (sourceIndex < 0 || sourceIndex >= series.Values.Count)
                    continue;
                double? value = series.Values[sourceIndex];
                if (value.HasValue && double.IsFinite(value.Value))
                    values.Add(value.Value);
            }
        }
        return values;
    }

    private void DrawBuiltInIndicatorOverlays(DrawingContext dc, ChartLayout layout)
    {
        foreach (BuiltInIndicatorResult result in _builtInIndicatorOverlays)
        {
            foreach (IndicatorSeriesResult series in result.Series.Where(item => item.Style.Visible))
            {
                if (!string.IsNullOrWhiteSpace(series.FillToSeriesKey))
                {
                    IndicatorSeriesResult? partner = result.Series.FirstOrDefault(item =>
                        string.Equals(item.Key, series.FillToSeriesKey, StringComparison.OrdinalIgnoreCase));
                    if (partner is not null)
                        DrawOverlayCloud(dc, layout, series, partner);
                }
            }

            foreach (IndicatorSeriesResult series in result.Series.Where(item => item.Style.Visible))
            {
                switch (series.Style.DrawMode)
                {
                    case IndicatorSeriesDrawMode.Dots:
                        DrawOverlayDots(dc, layout, series);
                        break;
                    case IndicatorSeriesDrawMode.ArrowUp:
                    case IndicatorSeriesDrawMode.ArrowDown:
                        DrawOverlayArrows(dc, layout, series);
                        break;
                    case IndicatorSeriesDrawMode.Histogram:
                        DrawOverlayHistogram(dc, layout, series);
                        break;
                    default:
                        DrawOverlayLine(dc, layout, series);
                        break;
                }
            }
        }
    }

    private void DrawOverlayLine(DrawingContext dc, ChartLayout layout, IndicatorSeriesResult series)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            bool started = false;
            int start = Math.Max(0, layout.FirstIndex - Math.Max(0, series.Shift) - 2);
            int end = Math.Min(series.Values.Count, layout.LastExclusive - Math.Min(0, series.Shift) + 2);
            for (int sourceIndex = start; sourceIndex < end; sourceIndex++)
            {
                double? value = series.Values[sourceIndex];
                if (!value.HasValue || !double.IsFinite(value.Value)) { started = false; continue; }
                int targetTimelineSlot = GetCandleTimelineSlot(sourceIndex) + series.Shift;
                int slot = targetTimelineSlot - layout.TimelineFirst;
                if (slot < 0 || slot >= layout.SlotCount) { started = false; continue; }
                double x = GetSlotCenterDip(layout, slot);
                double y = PriceToY(value.Value, layout);
                if (!started) { context.BeginFigure(new Point(x, y), false, false); started = true; }
                else context.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, IndicatorPen(series.Style), geometry);
    }

    private void DrawOverlayDots(DrawingContext dc, ChartLayout layout, IndicatorSeriesResult series)
    {
        Brush brush = IndicatorBrush(series.Style.Color, Colors.Cyan);
        double radius = Math.Clamp(series.Style.Width + 0.5, 1.5, 5);
        VisitOverlayPoints(layout, series, (x, y, _, _) => dc.DrawEllipse(brush, null, new Point(x, y), radius, radius));
    }

    private void DrawOverlayArrows(DrawingContext dc, ChartLayout layout, IndicatorSeriesResult series)
    {
        Brush brush = IndicatorBrush(series.Style.Color, series.Style.DrawMode == IndicatorSeriesDrawMode.ArrowUp ? Colors.LimeGreen : Colors.IndianRed);
        VisitOverlayPoints(layout, series, (x, y, sourceIndex, value) =>
        {
            double candleRange = sourceIndex >= 0 && sourceIndex < Candles.Count
                ? Math.Max(Candles[sourceIndex].Point * 8, Candles[sourceIndex].High - Candles[sourceIndex].Low)
                : Math.Max(1e-8, layout.MaximumPrice - layout.MinimumPrice);
            double offset = Math.Max(5.0, Math.Abs(PriceToY(value + candleRange * 0.06, layout) - y));
            bool up = series.Style.DrawMode == IndicatorSeriesDrawMode.ArrowUp;
            double tipY = up ? y - offset : y + offset;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(x, tipY), true, true);
                context.LineTo(new Point(x - 4, tipY + (up ? 7 : -7)), true, false);
                context.LineTo(new Point(x + 4, tipY + (up ? 7 : -7)), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        });
    }

    private void DrawOverlayHistogram(DrawingContext dc, ChartLayout layout, IndicatorSeriesResult series)
    {
        VisitOverlayPoints(layout, series, (x, y, sourceIndex, value) =>
        {
            bool negative = series.Style.ColorBySign && value < 0;
            if (series.Style.ColorBySlope && sourceIndex > 0 && sourceIndex - 1 < series.Values.Count && series.Values[sourceIndex - 1].HasValue)
                negative = value < series.Values[sourceIndex - 1]!.Value;
            Brush brush = IndicatorBrush(negative ? series.Style.NegativeColor : series.Style.Color, negative ? Colors.IndianRed : Colors.SeaGreen);
            double baseline = PriceToY(Math.Clamp(0, layout.MinimumPrice, layout.MaximumPrice), layout);
            double width = Math.Max(1, GetSlotWidthDip(layout, GetCandleTimelineSlot(sourceIndex) + series.Shift - layout.TimelineFirst) * 0.7);
            dc.DrawRectangle(brush, null, new Rect(x - width / 2, Math.Min(y, baseline), width, Math.Max(1, Math.Abs(y - baseline))));
        });
    }

    private void DrawOverlayCloud(DrawingContext dc, ChartLayout layout, IndicatorSeriesResult top, IndicatorSeriesResult bottom)
    {
        var upper = new List<Point>();
        var lower = new List<Point>();
        int start = Math.Max(0, layout.FirstIndex - Math.Max(top.Shift, bottom.Shift) - 2);
        int end = Math.Min(Math.Min(top.Values.Count, bottom.Values.Count), layout.LastExclusive - Math.Min(top.Shift, bottom.Shift) + 2);
        for (int sourceIndex = start; sourceIndex < end; sourceIndex++)
        {
            double? a = top.Values[sourceIndex];
            double? b = bottom.Values[sourceIndex];
            if (!a.HasValue || !b.HasValue || !double.IsFinite(a.Value) || !double.IsFinite(b.Value))
                continue;
            int targetTimelineSlot = GetCandleTimelineSlot(sourceIndex) + top.Shift;
            int slot = targetTimelineSlot - layout.TimelineFirst;
            if (slot < 0 || slot >= layout.SlotCount)
                continue;
            double x = GetSlotCenterDip(layout, slot);
            upper.Add(new Point(x, PriceToY(a.Value, layout)));
            lower.Add(new Point(x, PriceToY(b.Value, layout)));
        }
        if (upper.Count < 2 || upper.Count != lower.Count)
            return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(upper[0], true, true);
            for (int index = 1; index < upper.Count; index++) context.LineTo(upper[index], true, false);
            for (int index = lower.Count - 1; index >= 0; index--) context.LineTo(lower[index], true, false);
        }
        geometry.Freeze();
        Color color = IndicatorColor(top.Style.FillColor, IndicatorColor(top.Style.Color, Color.FromRgb(83, 217, 138)));
        color.A = (byte)Math.Round(Math.Clamp(top.Style.FillOpacity, 0, 1) * 255);
        if (color.A > 0)
            dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private void VisitOverlayPoints(ChartLayout layout, IndicatorSeriesResult series, Action<double, double, int, double> visitor)
    {
        int start = Math.Max(0, layout.FirstIndex - Math.Max(0, series.Shift) - 1);
        int end = Math.Min(series.Values.Count, layout.LastExclusive - Math.Min(0, series.Shift) + 1);
        for (int sourceIndex = start; sourceIndex < end; sourceIndex++)
        {
            double? value = series.Values[sourceIndex];
            if (!value.HasValue || !double.IsFinite(value.Value))
                continue;
            int slot = GetCandleTimelineSlot(sourceIndex) + series.Shift - layout.TimelineFirst;
            if (slot < 0 || slot >= layout.SlotCount)
                continue;
            visitor(GetSlotCenterDip(layout, slot), PriceToY(value.Value, layout), sourceIndex, value.Value);
        }
    }

    private static Pen IndicatorPen(IndicatorStyleSetting style)
    {
        var pen = new Pen(IndicatorBrush(style.Color, Colors.White), Math.Clamp(style.Width, 0.5, 8));
        pen.DashStyle = style.LineStyle switch
        {
            ChartLineStyle.Dashed => DashStyles.Dash,
            ChartLineStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };
        pen.StartLineCap = PenLineCap.Flat;
        pen.EndLineCap = PenLineCap.Flat;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static Brush IndicatorBrush(string colorText, Color fallback)
    {
        var brush = new SolidColorBrush(IndicatorColor(colorText, fallback));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Color IndicatorColor(string colorText, Color fallback)
    {
        try
        {
            object value = ColorConverter.ConvertFromString(colorText);
            return value is Color color ? color : fallback;
        }
        catch { return fallback; }
    }
    private ChartIndicatorMenuEntry? HitTestBuiltInIndicatorOverlay(
        ChartLayout layout,
        Point mouse,
        IReadOnlyList<ChartIndicatorMenuEntry> entries)
    {
        if (!layout.Plot.Contains(mouse) || _builtInIndicatorOverlays.Count == 0)
            return null;

        const double baseTolerance = 8.0;
        ChartIndicatorMenuEntry? bestEntry = null;
        double bestDistance = double.MaxValue;
        foreach (BuiltInIndicatorResult result in _builtInIndicatorOverlays)
        {
            ChartIndicatorMenuEntry? entry = entries.FirstOrDefault(item =>
                string.Equals(item.Key, "builtin:" + result.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            foreach (IndicatorSeriesResult series in result.Series.Where(item => item.Style.Visible))
            {
                Point? previous = null;
                double tolerance = baseTolerance + Math.Clamp(series.Style.Width, 0.5, 8.0) / 2.0;
                VisitOverlayPoints(layout, series, (x, y, _, _) =>
                {
                    var current = new Point(x, y);
                    double distance = (mouse - current).Length;
                    if (previous is Point previousPoint)
                        distance = Math.Min(distance, DistanceToSegment(mouse, previousPoint, current));
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestEntry = entry;
                    }
                    previous = current;
                });
            }
        }
        return bestEntry;
    }

    private ContextMenu BuildExactIndicatorContextMenu(ChartIndicatorMenuEntry entry)
    {
        var menu = new ContextMenu { PlacementTarget = this };
        menu.Items.Add(new MenuItem
        {
            Header = $"{entry.DisplayName} — {entry.Placement}",
            IsEnabled = false
        });
        menu.Items.Add(new Separator());
        menu.Items.Add(IndicatorContextAction("Refresh", () => IndicatorRefreshRequested?.Invoke(entry.Key)));
        menu.Items.Add(IndicatorContextAction("Properties…", () => IndicatorEditRequested?.Invoke(entry.Key)));
        menu.Items.Add(IndicatorContextAction("Move to Window…", () => IndicatorMoveToWindowRequested?.Invoke(entry.Key)));
        menu.Items.Add(IndicatorContextAction("Move to Chart…", () => IndicatorMoveToChartRequested?.Invoke(entry.Key)));
        menu.Items.Add(new Separator());
        menu.Items.Add(IndicatorContextAction("Remove", () => IndicatorRemoveRequested?.Invoke(entry.Key)));
        return menu;
    }

    private static MenuItem IndicatorContextAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

}
