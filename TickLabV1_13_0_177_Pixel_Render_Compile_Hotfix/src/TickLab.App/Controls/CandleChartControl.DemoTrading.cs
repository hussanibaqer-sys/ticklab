using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public enum DemoTradeLineKind
{
    Entry,
    Exit,
    StopLoss,
    TakeProfit,
    HistoryEntry,
    HistoryExit,
    HistoryStopLoss,
    HistoryTakeProfit
}

public sealed record DemoTradeLineOverlay(
    string LineId,
    int PositionId,
    DemoTradeLineKind Kind,
    double Price,
    string Label,
    bool IsDraggable,
    bool IsBuy,
    bool IsHistorical = false,
    long? StartUnix = null,
    long? EndUnix = null,
    bool IncludeInAutoScale = true,
    double EntryPrice = 0,
    double Volume = 0,
    double TickSize = 0,
    double TickValuePerLot = 0,
    double ContractSize = 0);

public sealed partial class CandleChartControl
{
    private IReadOnlyList<DemoTradeLineOverlay> _demoTradeLines = Array.Empty<DemoTradeLineOverlay>();
    private string? _draggingDemoTradeLineId;
    private double? _draggingDemoTradePrice;
    private DemoTradeLineOverlay? _draggingDemoTradeSourceLine;
    private readonly ToolTip _demoTradeHistoryToolTip = new()
    {
        Placement = PlacementMode.Mouse,
        HasDropShadow = true,
        StaysOpen = true,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        Padding = new Thickness(8, 6, 8, 6)
    };
    private int? _hoveredDemoHistoryTradeId;

    public IReadOnlyList<DemoTradeLineOverlay> DemoTradeLines
    {
        get => _demoTradeLines;
        set
        {
            _demoTradeLines = (value ?? Array.Empty<DemoTradeLineOverlay>())
                .Where(item => double.IsFinite(item.Price) && !string.IsNullOrWhiteSpace(item.LineId))
                .OrderBy(item => item.Price)
                .ToArray();
            if (_draggingDemoTradeLineId is not null &&
                !_demoTradeLines.Any(item => string.Equals(item.LineId, _draggingDemoTradeLineId, StringComparison.Ordinal)))
            {
                _draggingDemoTradeLineId = null;
                _draggingDemoTradePrice = null;
                _draggingDemoTradeSourceLine = null;
            }
            if (_hoveredDemoHistoryTradeId.HasValue &&
                !_demoTradeLines.Any(item => item.IsHistorical && item.PositionId == _hoveredDemoHistoryTradeId.Value))
            {
                CloseDemoTradeHistoryToolTip();
            }
            InvalidateVisual();
        }
    }

    public event Action<string, double>? DemoTradeLineMoved;
    public event Action<DemoTradeLineOverlay>? DemoTradeLineContextRequested;

    private void DrawDemoTradeLines(DrawingContext drawingContext, ChartLayout layout)
    {
        if (_demoTradeLines.Count == 0)
            return;

        DrawDemoTradeHistoryPaths(drawingContext, layout);

        foreach (DemoTradeLineOverlay line in _demoTradeLines)
        {
            if (line.Kind is DemoTradeLineKind.HistoryEntry or DemoTradeLineKind.HistoryExit)
                continue;

            bool isDraggingThisLine = string.Equals(line.LineId, _draggingDemoTradeLineId, StringComparison.Ordinal);
            double price = isDraggingThisLine
                ? _draggingDemoTradePrice ?? line.Price
                : line.Price;
            if (!double.IsFinite(price))
                continue;

            DemoTradeLineOverlay visualLine = line;
            if (isDraggingThisLine)
            {
                if (line.Kind == DemoTradeLineKind.Entry && Math.Abs(price - line.Price) > double.Epsilon)
                {
                    DemoTradeLineKind previewKind = ResolveEntryDragPreviewKind(line, price);
                    string previewName = previewKind == DemoTradeLineKind.StopLoss ? "NEW SL" : "NEW TP";
                    visualLine = line with
                    {
                        Kind = previewKind,
                        Label = $"{previewName} · {FormatProjectedDemoUsd(line, price)} · release to place",
                        IsDraggable = true
                    };
                }
                else if (line.Kind is DemoTradeLineKind.StopLoss or DemoTradeLineKind.TakeProfit)
                {
                    string levelName = line.Kind == DemoTradeLineKind.StopLoss ? "SL" : "TP";
                    visualLine = line with
                    {
                        Label = $"{levelName} · {FormatProjectedDemoUsd(line, price)}"
                    };
                }
            }

            double y = PriceToY(price, layout);
            if (y < layout.Plot.Top - 1 || y > layout.Plot.Bottom + 1)
                continue;

            ResolveDemoLineAppearance(visualLine, out string color, out ChartLineStyle style, out double thickness, out byte alpha);
            Pen pen = CreatePen(color, thickness, style);
            if (alpha < 255 && pen.Brush is SolidColorBrush solid)
            {
                Color faded = Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B);
                pen = new Pen(new SolidColorBrush(faded), pen.Thickness) { DashStyle = pen.DashStyle };
                if (pen.CanFreeze)
                    pen.Freeze();
            }
            y = SnapStrokeCoordinate(y, pen.Thickness);

            double startX = layout.Plot.Left;
            double endX = layout.Plot.Right;
            if (visualLine.IsHistorical && visualLine.StartUnix.HasValue && visualLine.EndUnix.HasValue)
            {
                startX = GetUnixX(visualLine.StartUnix.Value, layout);
                endX = GetUnixX(visualLine.EndUnix.Value, layout);
                if (endX < startX)
                    (startX, endX) = (endX, startX);
                if (endX < layout.Plot.Left || startX > layout.Plot.Right)
                    continue;
                startX = Math.Clamp(startX, layout.Plot.Left, layout.Plot.Right);
                endX = Math.Clamp(endX, layout.Plot.Left, layout.Plot.Right);
                if (visualLine.Kind is DemoTradeLineKind.HistoryExit)
                {
                    startX = Math.Max(layout.Plot.Left, endX - 13);
                    endX = Math.Min(layout.Plot.Right, endX + 13);
                }
            }

            drawingContext.DrawLine(pen, new Point(startX, y), new Point(endX, y));

            if (visualLine.IsHistorical && (visualLine.Kind is DemoTradeLineKind.HistoryStopLoss or DemoTradeLineKind.HistoryTakeProfit))
                continue;

            Color ticketColor = ResolveDemoTicketColor(visualLine);
            Brush ticketBrush = new SolidColorBrush(Color.FromArgb(visualLine.IsHistorical ? (byte)205 : (byte)230, ticketColor.R, ticketColor.G, ticketColor.B));
            if (ticketBrush.CanFreeze)
                ticketBrush.Freeze();
            string priceText = price.ToString($"F{GetDemoPriceDigits()}", CultureInfo.InvariantCulture);
            string dragHint = visualLine.IsDraggable ? " ↕" : string.Empty;
            FormattedText text = CreateText($"{visualLine.Label}{dragHint}", visualLine.IsHistorical ? 9 : 10, Brushes.White);
            double height = Math.Max(visualLine.IsHistorical ? 17 : 19, text.Height + 5);
            double width = Math.Min(Math.Max(54, text.Width + 12), Math.Max(54, layout.Plot.Width * 0.42));
            double ticketAnchor = visualLine.IsHistorical ? endX : layout.Plot.Right;
            double left = Math.Clamp(ticketAnchor - width - 3, layout.Plot.Left + 2, Math.Max(layout.Plot.Left + 2, layout.Plot.Right - width - 3));
            double top = Math.Clamp(y - height / 2, layout.Plot.Top + 1, layout.Plot.Bottom - height - 1);
            var ticket = new Rect(left, top, width, height);
            drawingContext.DrawRoundedRectangle(ticketBrush, null, ticket, 3, 3);
            drawingContext.PushClip(new RectangleGeometry(ticket));
            drawingContext.DrawText(text, new Point(ticket.Left + 6, ticket.Top + 2));
            drawingContext.Pop();

            if (!visualLine.IsHistorical)
                DrawDemoPriceScaleTicket(drawingContext, layout, y, priceText, ticketBrush);
        }
    }


    private static string FormatProjectedDemoUsd(DemoTradeLineOverlay line, double valuationPrice)
    {
        double difference = line.IsBuy
            ? valuationPrice - line.EntryPrice
            : line.EntryPrice - valuationPrice;
        double tickSize = line.TickSize > 0 ? line.TickSize : 0;
        double tickValue = line.TickValuePerLot > 0
            ? line.TickValuePerLot
            : tickSize > 0 ? tickSize * Math.Max(0, line.ContractSize) : 0;
        double amount = tickSize > 0
            ? difference / tickSize * tickValue * line.Volume
            : difference * line.Volume * line.ContractSize;
        if (!double.IsFinite(amount))
            amount = 0;
        return amount > 0.0000001
            ? $"+${amount.ToString("N2", CultureInfo.InvariantCulture)}"
            : amount < -0.0000001
                ? $"-${Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture)}"
                : "$0.00";
    }

    private int GetDemoPriceDigits() => Candles.Count > 0
        ? Math.Clamp(Candles[^1].Digits, 0, 10)
        : 5;

    private void DrawDemoPriceScaleTicket(DrawingContext drawingContext, ChartLayout layout, double y, string priceText, Brush background)
    {
        FormattedText scaleText = CreateText(priceText, 10, Brushes.White);
        double height = Math.Max(18, scaleText.Height + 5);
        double top = Math.Clamp(y - height / 2, layout.Plot.Top, layout.Plot.Bottom - height);
        var ticket = new Rect(layout.Plot.Right + 2, top, Math.Max(1, RightMargin - 4), height);
        drawingContext.DrawRoundedRectangle(background, null, ticket, 2, 2);
        drawingContext.PushClip(new RectangleGeometry(ticket));
        drawingContext.DrawText(scaleText, new Point(ticket.Left + 4, ticket.Top + 2));
        drawingContext.Pop();
    }

    private void DrawDemoTradeHistoryPaths(DrawingContext drawingContext, ChartLayout layout)
    {
        IGrouping<int, DemoTradeLineOverlay>[] pairs = _demoTradeLines
            .Where(item => item.IsHistorical && item.Kind is DemoTradeLineKind.HistoryEntry or DemoTradeLineKind.HistoryExit)
            .GroupBy(item => item.PositionId)
            .ToArray();
        bool denseHistory = pairs.Length > 600;

        drawingContext.PushClip(new RectangleGeometry(layout.Plot));
        foreach (IGrouping<int, DemoTradeLineOverlay> group in pairs)
        {
            DemoTradeLineOverlay? entry = group.FirstOrDefault(item => item.Kind == DemoTradeLineKind.HistoryEntry);
            DemoTradeLineOverlay? exit = group.FirstOrDefault(item => item.Kind == DemoTradeLineKind.HistoryExit);
            if (entry is null || exit is null || !entry.StartUnix.HasValue || !exit.EndUnix.HasValue)
                continue;

            Point start = new(GetUnixX(entry.StartUnix.Value, layout), PriceToY(entry.Price, layout));
            Point end = new(GetUnixX(exit.EndUnix.Value, layout), PriceToY(exit.Price, layout));
            double minimumX = Math.Min(start.X, end.X);
            double maximumX = Math.Max(start.X, end.X);
            if (maximumX < layout.Plot.Left || minimumX > layout.Plot.Right)
                continue;

            // History uses fixed MT5-style lifecycle colours: entry is blue and exit is red.
            // Direction remains visible in the BUY/SELL caption and arrow orientation.
            Color entryColor = Color.FromRgb(47, 128, 237);
            Color exitColor = Color.FromRgb(224, 75, 90);
            var entryBrush = new SolidColorBrush(Color.FromArgb(238, entryColor.R, entryColor.G, entryColor.B));
            var exitBrush = new SolidColorBrush(Color.FromArgb(238, exitColor.R, exitColor.G, exitColor.B));
            var connectorBrush = new SolidColorBrush(Color.FromArgb(denseHistory ? (byte)112 : (byte)190, 118, 126, 140));
            var pen = new Pen(connectorBrush, denseHistory ? 0.85 : 1.35)
            {
                DashStyle = denseHistory ? DashStyles.Solid : DashStyles.Dash
            };
            if (entryBrush.CanFreeze) entryBrush.Freeze();
            if (exitBrush.CanFreeze) exitBrush.Freeze();
            if (pen.CanFreeze) pen.Freeze();
            drawingContext.DrawLine(pen, start, end);

            if (layout.Plot.Contains(start))
            {
                DrawDemoHistoryArrow(drawingContext, start, entry.IsBuy, isEntry: true, entryBrush);
                if (!denseHistory)
                    DrawDemoHistoryCaption(drawingContext, layout, start, entry.IsBuy ? "BUY" : "SELL", entryBrush, placeAbove: !entry.IsBuy);
            }
            if (layout.Plot.Contains(end))
            {
                DrawDemoHistoryArrow(drawingContext, end, entry.IsBuy, isEntry: false, exitBrush);
                if (!denseHistory)
                    DrawDemoHistoryCaption(drawingContext, layout, end, "EXIT", exitBrush, placeAbove: entry.IsBuy);
            }
        }
        drawingContext.Pop();
    }

    private static void DrawDemoHistoryArrow(DrawingContext drawingContext, Point point, bool isBuy, bool isEntry, Brush brush)
    {
        bool pointsUp = isEntry ? isBuy : !isBuy;
        double direction = pointsUp ? -1.0 : 1.0;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            Point tip = new(point.X, point.Y + direction * 9.0);
            Point left = new(point.X - 6.0, point.Y - direction * 2.0);
            Point innerLeft = new(point.X - 2.2, point.Y - direction * 2.0);
            Point stemLeft = new(point.X - 2.2, point.Y - direction * 8.0);
            Point stemRight = new(point.X + 2.2, point.Y - direction * 8.0);
            Point innerRight = new(point.X + 2.2, point.Y - direction * 2.0);
            Point right = new(point.X + 6.0, point.Y - direction * 2.0);
            context.BeginFigure(tip, true, true);
            context.LineTo(left, true, false);
            context.LineTo(innerLeft, true, false);
            context.LineTo(stemLeft, true, false);
            context.LineTo(stemRight, true, false);
            context.LineTo(innerRight, true, false);
            context.LineTo(right, true, false);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        drawingContext.DrawGeometry(brush, new Pen(Brushes.White, 1.0), geometry);
    }

    private void DrawDemoHistoryCaption(
        DrawingContext drawingContext,
        ChartLayout layout,
        Point anchor,
        string caption,
        Brush background,
        bool placeAbove)
    {
        // Captions sit on the flat/base side of the arrow, never beside its
        // pointed tip. v48 enlarges the compact v47 caption by exactly 50% and
        // leaves only a tiny ticket border around the text.
        FormattedText text = CreateText(caption, 6.75, Brushes.White);
        double width = text.Width + 4;
        double height = text.Height + 2;
        double left = Math.Clamp(anchor.X - width / 2, layout.Plot.Left + 1, layout.Plot.Right - width - 1);
        double desiredTop = placeAbove ? anchor.Y - height - 10 : anchor.Y + 10;
        double top = Math.Clamp(desiredTop, layout.Plot.Top + 1, layout.Plot.Bottom - height - 1);
        var rect = new Rect(left, top, width, height);
        drawingContext.DrawRoundedRectangle(background, null, rect, 1.5, 1.5);
        drawingContext.DrawText(text, new Point(rect.Left + 2, rect.Top + 1));
    }

    private static DemoTradeLineKind ResolveEntryDragPreviewKind(DemoTradeLineOverlay entry, double requestedPrice)
    {
        bool stopLoss = entry.IsBuy
            ? requestedPrice < entry.Price
            : requestedPrice > entry.Price;
        return stopLoss ? DemoTradeLineKind.StopLoss : DemoTradeLineKind.TakeProfit;
    }

    private void UpdateDemoTradeHistoryHover(ChartLayout layout, Point mouse)
    {
        if (!layout.Plot.Contains(mouse))
        {
            CloseDemoTradeHistoryToolTip();
            return;
        }

        int? hitTradeId = null;
        string? text = null;
        double bestDistance = 8.0;
        IGrouping<int, DemoTradeLineOverlay>[] hoverGroups = _demoTradeLines
            .Where(item => item.IsHistorical && item.Kind is DemoTradeLineKind.HistoryEntry or DemoTradeLineKind.HistoryExit)
            .GroupBy(item => item.PositionId)
            .ToArray();
        // Extremely dense projections stay lightweight. Zooming into a smaller time
        // range re-enables exact per-trade hover automatically.
        if (hoverGroups.Length > 1200)
        {
            CloseDemoTradeHistoryToolTip();
            return;
        }
        foreach (IGrouping<int, DemoTradeLineOverlay> group in hoverGroups)
        {
            DemoTradeLineOverlay? entry = group.FirstOrDefault(item => item.Kind == DemoTradeLineKind.HistoryEntry);
            DemoTradeLineOverlay? exit = group.FirstOrDefault(item => item.Kind == DemoTradeLineKind.HistoryExit);
            if (entry is null || exit is null || !entry.StartUnix.HasValue || !exit.EndUnix.HasValue)
                continue;
            Point start = new(GetUnixX(entry.StartUnix.Value, layout), PriceToY(entry.Price, layout));
            Point end = new(GetUnixX(exit.EndUnix.Value, layout), PriceToY(exit.Price, layout));
            double distance = DistanceToDemoHistorySegment(mouse, start, end);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                hitTradeId = entry.PositionId;
                text = entry.Label;
            }
        }

        if (hitTradeId.HasValue && !string.IsNullOrWhiteSpace(text))
        {
            if (_hoveredDemoHistoryTradeId != hitTradeId || !Equals(_demoTradeHistoryToolTip.Content, text))
            {
                _demoTradeHistoryToolTip.IsOpen = false;
                _demoTradeHistoryToolTip.PlacementTarget = this;
                _demoTradeHistoryToolTip.Content = text;
                _demoTradeHistoryToolTip.IsOpen = true;
            }
            _hoveredDemoHistoryTradeId = hitTradeId;
        }
        else
        {
            CloseDemoTradeHistoryToolTip();
        }
    }

    private void CloseDemoTradeHistoryToolTip()
    {
        _hoveredDemoHistoryTradeId = null;
        _demoTradeHistoryToolTip.IsOpen = false;
    }

    private static double DistanceToDemoHistorySegment(Point point, Point start, Point end)
    {
        Vector segment = end - start;
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared <= double.Epsilon)
            return (point - start).Length;
        Vector fromStart = point - start;
        double t = Math.Clamp((fromStart.X * segment.X + fromStart.Y * segment.Y) / lengthSquared, 0.0, 1.0);
        Point projection = start + segment * t;
        return (point - projection).Length;
    }

    private bool DemoHistoryOverlayOverlapsCandleRange(
        DemoTradeLineOverlay line,
        int firstIndex,
        int lastExclusive)
    {
        if (!line.IsHistorical || !line.StartUnix.HasValue || !line.EndUnix.HasValue ||
            Candles.Count == 0 || firstIndex < 0 || lastExclusive <= firstIndex)
        {
            return false;
        }

        firstIndex = Math.Clamp(firstIndex, 0, Candles.Count - 1);
        lastExclusive = Math.Clamp(lastExclusive, firstIndex + 1, Candles.Count);
        long visibleStart = Candles[firstIndex].StartUnix;
        long visibleEnd = Candles[lastExclusive - 1].EndUnix;
        long tradeStart = Math.Min(line.StartUnix.Value, line.EndUnix.Value);
        long tradeEnd = Math.Max(line.StartUnix.Value, line.EndUnix.Value);
        return tradeEnd >= visibleStart && tradeStart <= visibleEnd;
    }

    private static void ResolveDemoLineAppearance(DemoTradeLineOverlay line, out string color, out ChartLineStyle style, out double thickness, out byte alpha)
    {
        color = line.Kind switch
        {
            DemoTradeLineKind.StopLoss => "#E04B5A",
            DemoTradeLineKind.TakeProfit => "#25B66F",
            DemoTradeLineKind.HistoryStopLoss => "#B76A73",
            DemoTradeLineKind.HistoryTakeProfit => "#5A9B78",
            DemoTradeLineKind.HistoryExit => "#7C8796",
            DemoTradeLineKind.HistoryEntry => line.IsBuy ? "#5C86BE" : "#9A70AD",
            DemoTradeLineKind.Exit => "#6D7887",
            _ => line.IsBuy ? "#2F80ED" : "#C56CF0"
        };
        style = line.Kind switch
        {
            DemoTradeLineKind.Entry => ChartLineStyle.Solid,
            DemoTradeLineKind.Exit => ChartLineStyle.Solid,
            DemoTradeLineKind.HistoryEntry => ChartLineStyle.Dashed,
            DemoTradeLineKind.HistoryExit => ChartLineStyle.Solid,
            DemoTradeLineKind.HistoryStopLoss => ChartLineStyle.Dotted,
            DemoTradeLineKind.HistoryTakeProfit => ChartLineStyle.Dotted,
            _ => ChartLineStyle.Dashed
        };
        thickness = line.IsHistorical ? 1.0 : line.Kind == DemoTradeLineKind.Entry ? 1.35 : 1.55;
        alpha = line.IsHistorical ? (byte)175 : (byte)255;
    }

    private static Color ResolveDemoTicketColor(DemoTradeLineOverlay line) => line.Kind switch
    {
        DemoTradeLineKind.StopLoss => Color.FromRgb(224, 75, 90),
        DemoTradeLineKind.TakeProfit => Color.FromRgb(37, 182, 111),
        DemoTradeLineKind.HistoryStopLoss => Color.FromRgb(145, 82, 91),
        DemoTradeLineKind.HistoryTakeProfit => Color.FromRgb(67, 130, 93),
        DemoTradeLineKind.HistoryExit => Color.FromRgb(92, 102, 116),
        DemoTradeLineKind.HistoryEntry => line.IsBuy ? Color.FromRgb(65, 104, 153) : Color.FromRgb(121, 79, 139),
        DemoTradeLineKind.Exit => Color.FromRgb(92, 102, 116),
        _ => line.IsBuy ? Color.FromRgb(47, 128, 237) : Color.FromRgb(197, 108, 240)
    };

    private double GetUnixX(long unix, ChartLayout layout)
    {
        if (Candles.Count == 0)
            return layout.Plot.Left;
        int low = 0;
        int high = Candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Candles[middle].StartUnix < unix)
                low = middle + 1;
            else
                high = middle;
        }
        int index = Math.Clamp(low, 0, Candles.Count - 1);
        if (index > 0 && Math.Abs(Candles[index - 1].StartUnix - unix) <= Math.Abs(Candles[index].StartUnix - unix))
            index--;
        int relativeSlot = GetCandleTimelineSlot(index) - layout.TimelineFirst;
        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        return layout.Plot.Left + (relativeSlot + 0.5) * slotWidth;
    }

    private DemoTradeLineOverlay? HitTestDemoTradeLine(ChartLayout layout, Point mouse, bool draggableOnly)
    {
        if (!layout.Plot.Contains(mouse) || _demoTradeLines.Count == 0)
            return null;

        const double tolerance = 8.0;
        DemoTradeLineOverlay? best = null;
        double bestDistance = double.MaxValue;
        IEnumerable<DemoTradeLineOverlay> candidates = draggableOnly
            ? _demoTradeLines.Where(item => item.IsDraggable)
            : _demoTradeLines.Where(item => !item.IsHistorical);
        foreach (DemoTradeLineOverlay line in candidates)
        {
            if (line.Kind is DemoTradeLineKind.HistoryEntry or DemoTradeLineKind.HistoryExit)
                continue;

            double price = string.Equals(line.LineId, _draggingDemoTradeLineId, StringComparison.Ordinal)
                ? _draggingDemoTradePrice ?? line.Price
                : line.Price;
            double distance = Math.Abs(mouse.Y - PriceToY(price, layout));
            if (distance <= tolerance && distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
        }
        return best;
    }

    private DemoTradeLineOverlay? HitTestDemoTradeLine(ChartLayout layout, Point mouse) => HitTestDemoTradeLine(layout, mouse, draggableOnly: true);

    private bool TryOpenDemoTradeLineContextMenu(ChartLayout layout, Point mouse)
    {
        DemoTradeLineOverlay? line = HitTestDemoTradeLine(layout, mouse, draggableOnly: false);
        if (line is null)
            return false;
        DemoTradeLineContextRequested?.Invoke(line);
        return true;
    }

    private bool BeginDemoTradeLineDrag(ChartLayout layout, Point mouse)
    {
        DemoTradeLineOverlay? line = HitTestDemoTradeLine(layout, mouse);
        if (line is null)
            return false;

        _draggingDemoTradeLineId = line.LineId;
        _draggingDemoTradePrice = line.Price;
        _draggingDemoTradeSourceLine = line;
        CaptureMouse();
        return true;
    }

    private bool UpdateDemoTradeLineDrag(Point mouse, ChartLayout layout)
    {
        if (_draggingDemoTradeLineId is null)
            return false;
        _draggingDemoTradePrice = Math.Clamp(YToPrice(mouse.Y, layout), layout.MinimumPrice, layout.MaximumPrice);
        return true;
    }

    private bool CompleteDemoTradeLineDrag()
    {
        string? pendingLineId = _draggingDemoTradeLineId;
        if (pendingLineId is null)
            return false;

        string lineId = pendingLineId;
        double? price = _draggingDemoTradePrice;
        DemoTradeLineOverlay? sourceLine = _draggingDemoTradeSourceLine;
        _draggingDemoTradeLineId = null;
        _draggingDemoTradePrice = null;
        _draggingDemoTradeSourceLine = null;
        ReleaseMouseCapture();
        if (price.HasValue && double.IsFinite(price.Value) && sourceLine?.IsDraggable == true &&
            Math.Abs(price.Value - sourceLine.Price) > double.Epsilon)
        {
            DemoTradeLineMoved?.Invoke(lineId, price.Value);
        }
        InvalidateVisual();
        return true;
    }

    private bool IsDemoTradeLineHovered(ChartLayout layout, Point mouse) =>
        _draggingDemoTradeLineId is not null || HitTestDemoTradeLine(layout, mouse) is not null;
}
