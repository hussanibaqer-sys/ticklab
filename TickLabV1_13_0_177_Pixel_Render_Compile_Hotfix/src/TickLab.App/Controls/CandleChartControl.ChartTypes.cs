using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl
{
    private void DrawBodyCandles(
        DrawingContext drawingContext,
        ChartLayout layout,
        bool hollowBullish,
        bool volumeWeighted)
    {
        CandlePixelGrid grid = CreateCandlePixelGrid(layout);
        int targetGapPixels = Math.Max(1, (int)Math.Round(CandleGapPixels * grid.ScaleX));
        int minimumSlotWidthPixels = Math.Max(1, (int)Math.Floor(grid.RawSlotWidthPixels));

        // Keep real candle bodies through the micro-candle tier. A three-
        // physical-pixel slot is enough for a crisp three-pixel body and a
        // one-pixel centred wick, so the compact high/low renderer is delayed
        // until the frame contains substantially more than 400 bars.
        if (minimumSlotWidthPixels < 3)
        {
            DrawCompressedCandles(drawingContext, layout);
            return;
        }

        // Every ordinary candle in the frame uses exactly the same odd pixel
        // width. Odd widths have a true physical centre column, allowing a
        // one-pixel wick to stay mathematically centred inside the body.
        int commonBodyWidthPixels = GetUniformCandleBodyWidthPixels(
            minimumSlotWidthPixels,
            targetGapPixels);

        long maximumVolume = 1;
        if (volumeWeighted)
        {
            for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
            {
                Candle candle = Candles[layout.FirstIndex + visibleIndex];
                maximumVolume = Math.Max(
                    maximumVolume,
                    candle.RealVolume > 0 ? candle.RealVolume : candle.TickVolume);
            }
        }

        Brush upBodyBrush = BrushFrom(Settings.UpBodyColor, Color.FromRgb(47, 184, 137));
        Brush downBodyBrush = BrushFrom(Settings.DownBodyColor, Color.FromRgb(223, 92, 104));
        Brush upBorderBrush = BrushFrom(Settings.UpBorderColor, Color.FromRgb(47, 184, 137));
        Brush downBorderBrush = BrushFrom(Settings.DownBorderColor, Color.FromRgb(223, 92, 104));
        Brush upWickBrush = BrushFrom(Settings.UpWickColor, Color.FromRgb(47, 184, 137));
        Brush downWickBrush = BrushFrom(Settings.DownWickColor, Color.FromRgb(223, 92, 104));

        int borderWidthPixels = Math.Max(1, (int)Math.Round(
            Math.Clamp(Settings.CandleBorderThickness, 0.25, 5.0) * grid.ScaleX));
        int borderHeightPixels = Math.Max(1, (int)Math.Round(
            Math.Clamp(Settings.CandleBorderThickness, 0.25, 5.0) * grid.ScaleY));
        int wickWidthPixels = minimumSlotWidthPixels < 7
            ? 1
            : MakeOddPixelWidth(Math.Max(1, (int)Math.Round(
                Math.Clamp(Settings.CandleWickThickness, 0.25, 5.0) * grid.ScaleX)));

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            PixelSpan slot = GetSlotPixelSpan(grid, layout.VisibleSlots[visibleIndex]);

            int bodyWidthPixels = commonBodyWidthPixels;
            if (volumeWeighted)
            {
                long volume = Math.Max(
                    1,
                    candle.RealVolume > 0 ? candle.RealVolume : candle.TickVolume);
                double ratio = Math.Sqrt(volume / (double)maximumVolume);
                int requestedWidth = Math.Max(
                    1,
                    (int)Math.Round(commonBodyWidthPixels * Math.Clamp(ratio, 0.18, 1.0)));
                bodyWidthPixels = MakeOddPixelWidthAtMost(
                    requestedWidth,
                    commonBodyWidthPixels);
            }

            // Body width never changes from candle to candle. When a fractional
            // frame width creates an occasional one-pixel-wider slot, centre
            // the same body inside it; only the surrounding gap absorbs that
            // spare pixel. Wick and body are then derived from one final centre.
            int slotWidthPixels = Math.Max(1, slot.Right - slot.Left);
            bodyWidthPixels = Math.Min(bodyWidthPixels, slotWidthPixels);
            if ((bodyWidthPixels & 1) == 0 && bodyWidthPixels > 1)
                bodyWidthPixels--;

            int freePixels = Math.Max(0, slotWidthPixels - bodyWidthPixels);
            int bodyLeftPixels = slot.Left + freePixels / 2;
            int bodyRightPixels = bodyLeftPixels + bodyWidthPixels;
            double bodyCenterPixels = (bodyLeftPixels + bodyRightPixels) / 2.0;
            int highPixels = PriceToPixelY(candle.High, layout, grid);
            int lowPixels = PriceToPixelY(candle.Low, layout, grid);
            int openPixels = PriceToPixelY(candle.Open, layout, grid);
            int closePixels = PriceToPixelY(candle.Close, layout, grid);
            int bodyTopPixels = Math.Min(openPixels, closePixels);
            int bodyBottomPixels = Math.Max(openPixels, closePixels) + 1;
            if (bodyBottomPixels <= bodyTopPixels)
                bodyBottomPixels = bodyTopPixels + 1;

            bool bullish = candle.IsBullish;
            Brush bodyBrush = bullish ? upBodyBrush : downBodyBrush;
            Brush borderBrush = bullish ? upBorderBrush : downBorderBrush;
            Brush wickBrush = bullish ? upWickBrush : downWickBrush;

            DrawVerticalPixelBar(
                drawingContext,
                wickBrush,
                bodyCenterPixels,
                Math.Min(highPixels, lowPixels),
                Math.Max(highPixels, lowPixels) + 1,
                wickWidthPixels,
                grid);

            DrawCandlePixelBody(
                drawingContext,
                bullish && hollowBullish ? null : bodyBrush,
                borderBrush,
                bodyLeftPixels,
                bodyTopPixels,
                bodyRightPixels,
                bodyBottomPixels,
                borderWidthPixels,
                borderHeightPixels,
                grid);
        }
    }

    private static int GetUniformCandleBodyWidthPixels(
        int minimumSlotWidthPixels,
        int targetGapPixels)
    {
        // Micro-candle tier used between roughly 200 and 480 visible bars on
        // a typical desktop chart. A fixed three-pixel body keeps Open/Close
        // visibly wider than the one-pixel wick instead of turning every bar
        // into a high/low line. At four pixels there is one clean gap pixel;
        // at three pixels the bodies touch only at the far edge of this tier.
        if (minimumSlotWidthPixels <= 4)
            return 3;

        int maximumBodyWidth = Math.Max(3, minimumSlotWidthPixels - 2);
        int preferredWidth = Math.Clamp(
            minimumSlotWidthPixels - targetGapPixels,
            3,
            maximumBodyWidth);

        // Prefer an odd width so the wick has one exact physical centre
        // column. Every candle in the frame receives this same width.
        if ((preferredWidth & 1) == 0)
        {
            if (preferredWidth + 1 <= maximumBodyWidth)
                preferredWidth++;
            else
                preferredWidth--;
        }

        return Math.Max(3, preferredWidth);
    }

    private static int MakeOddPixelWidth(int widthPixels)
    {
        int width = Math.Max(1, widthPixels);
        return (width & 1) == 1 ? width : width + 1;
    }

    private static int MakeOddPixelWidthAtMost(int requestedWidth, int maximumWidth)
    {
        int maximumOdd = Math.Max(1, maximumWidth);
        if ((maximumOdd & 1) == 0)
            maximumOdd--;

        int width = Math.Clamp(requestedWidth, 1, maximumOdd);
        if ((width & 1) == 0)
            width--;
        return Math.Max(1, width);
    }

    private CandlePixelGrid CreateCandlePixelGrid(ChartLayout layout)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = Math.Max(0.01, dpi.DpiScaleX);
        double scaleY = Math.Max(0.01, dpi.DpiScaleY);
        int viewportLeftPixels = (int)Math.Round(layout.Plot.Left * scaleX, MidpointRounding.AwayFromZero);
        int viewportRightPixels = (int)Math.Round(layout.Plot.Right * scaleX, MidpointRounding.AwayFromZero);
        int plotTopPixels = (int)Math.Round(layout.Plot.Top * scaleY, MidpointRounding.AwayFromZero);
        int plotBottomPixels = (int)Math.Round(layout.Plot.Bottom * scaleY, MidpointRounding.AwayFromZero);
        if (viewportRightPixels <= viewportLeftPixels)
            viewportRightPixels = viewportLeftPixels + 1;
        if (plotBottomPixels <= plotTopPixels)
            plotBottomPixels = plotTopPixels + 1;

        int slotCount = Math.Max(1, layout.SlotCount);
        double rawSlotWidthPixels =
            (viewportRightPixels - viewportLeftPixels) / (double)slotCount;

        // Always span the complete fixed viewport. Slot boundaries use one
        // deterministic error-distribution sequence, so the few unavoidable
        // spare physical pixels become one-pixel gaps distributed across the
        // frame instead of one large strip that moves at the left edge. Candle
        // body width remains common for the complete frame and each wick is
        // still derived from that body's exact centre.
        return new CandlePixelGrid(
            scaleX,
            scaleY,
            viewportLeftPixels,
            viewportRightPixels,
            plotTopPixels,
            plotBottomPixels,
            slotCount,
            rawSlotWidthPixels);
    }

    private static PixelSpan GetSlotPixelSpan(CandlePixelGrid grid, int slotIndex)
    {
        int clampedSlot = Math.Clamp(slotIndex, 0, grid.SlotCount - 1);
        int left = grid.PlotLeftPixels + (int)Math.Round(
            clampedSlot * grid.RawSlotWidthPixels,
            MidpointRounding.AwayFromZero);
        int right = grid.PlotLeftPixels + (int)Math.Round(
            (clampedSlot + 1) * grid.RawSlotWidthPixels,
            MidpointRounding.AwayFromZero);
        if (right <= left)
            right = left + 1;
        return new PixelSpan(left, right);
    }

    private int PriceToPixelY(double price, ChartLayout layout, CandlePixelGrid grid)
    {
        int pixel = (int)Math.Round(PriceToY(price, layout) * grid.ScaleY, MidpointRounding.AwayFromZero);

        // Pixel rectangles use an exclusive bottom edge. Returning the exact
        // plot-bottom boundary made a candle whose OHLC rounded to that row
        // collapse to zero height and disappear during deep vertical zoom-out.
        // Keep every plotted price on an actual drawable pixel row.
        int maximumDrawableRow = Math.Max(
            grid.PlotTopPixels,
            grid.PlotBottomPixels - 1);
        return Math.Clamp(pixel, grid.PlotTopPixels, maximumDrawableRow);
    }

    private static void DrawVerticalPixelBar(
        DrawingContext drawingContext,
        Brush brush,
        double centerPixels,
        int topPixels,
        int bottomPixels,
        int widthPixels,
        CandlePixelGrid grid)
    {
        int width = Math.Max(1, widthPixels);
        int left = (int)Math.Round(centerPixels - width / 2.0, MidpointRounding.AwayFromZero);
        int right = left + width;
        int maximumTop = Math.Max(grid.PlotTopPixels, grid.PlotBottomPixels - 1);
        int top = Math.Clamp(
            Math.Min(topPixels, bottomPixels),
            grid.PlotTopPixels,
            maximumTop);
        int bottom = Math.Clamp(
            Math.Max(topPixels, bottomPixels),
            grid.PlotTopPixels + 1,
            grid.PlotBottomPixels);
        if (bottom <= top)
            bottom = Math.Min(grid.PlotBottomPixels, top + 1);
        if (right <= left || bottom <= top)
            return;
        Rect rect = PixelRectToDip(left, top, right, bottom, grid);
        var guidelines = new GuidelineSet();
        guidelines.GuidelinesX.Add(rect.Left);
        guidelines.GuidelinesX.Add(rect.Right);
        guidelines.GuidelinesY.Add(rect.Top);
        guidelines.GuidelinesY.Add(rect.Bottom);
        drawingContext.PushGuidelineSet(guidelines);
        drawingContext.DrawRectangle(brush, null, rect);
        drawingContext.Pop();
    }

    private static void DrawCandlePixelBody(
        DrawingContext drawingContext,
        Brush? fillBrush,
        Brush borderBrush,
        int leftPixels,
        int topPixels,
        int rightPixels,
        int bottomPixels,
        int borderWidthPixels,
        int borderHeightPixels,
        CandlePixelGrid grid)
    {
        int left = Math.Clamp(leftPixels, grid.PlotLeftPixels, grid.PlotRightPixels);
        int right = Math.Clamp(rightPixels, grid.PlotLeftPixels, grid.PlotRightPixels);
        int maximumTop = Math.Max(grid.PlotTopPixels, grid.PlotBottomPixels - 1);
        int top = Math.Clamp(topPixels, grid.PlotTopPixels, maximumTop);
        int bottom = Math.Clamp(
            bottomPixels,
            grid.PlotTopPixels + 1,
            grid.PlotBottomPixels);
        if (right <= left)
            right = Math.Min(grid.PlotRightPixels, left + 1);
        if (bottom <= top)
            bottom = Math.Min(grid.PlotBottomPixels, top + 1);
        if (right <= left || bottom <= top)
            return;

        int width = right - left;
        int height = bottom - top;
        int borderX = Math.Clamp(borderWidthPixels, 1, Math.Max(1, width));
        int borderY = Math.Clamp(borderHeightPixels, 1, Math.Max(1, height));

        Rect outer = PixelRectToDip(left, top, right, bottom, grid);
        var guidelines = new GuidelineSet();
        guidelines.GuidelinesX.Add(outer.Left);
        guidelines.GuidelinesX.Add(outer.Right);
        guidelines.GuidelinesY.Add(outer.Top);
        guidelines.GuidelinesY.Add(outer.Bottom);
        drawingContext.PushGuidelineSet(guidelines);

        if (fillBrush is not null)
            drawingContext.DrawRectangle(fillBrush, null, outer);

        // Render borders as filled physical-pixel strips rather than a WPF
        // rectangle Pen. Pen strokes are centred on an edge and can become
        // half-pixel anti-aliased at non-100% DPI; inside strips remain sharp.
        if (width <= borderX * 2 || height <= borderY * 2)
        {
            drawingContext.DrawRectangle(borderBrush, null, outer);
            drawingContext.Pop();
            return;
        }

        drawingContext.DrawRectangle(borderBrush, null, PixelRectToDip(left, top, right, top + borderY, grid));
        drawingContext.DrawRectangle(borderBrush, null, PixelRectToDip(left, bottom - borderY, right, bottom, grid));
        drawingContext.DrawRectangle(borderBrush, null, PixelRectToDip(left, top + borderY, left + borderX, bottom - borderY, grid));
        drawingContext.DrawRectangle(borderBrush, null, PixelRectToDip(right - borderX, top + borderY, right, bottom - borderY, grid));
        drawingContext.Pop();
    }

    private static Rect PixelRectToDip(
        int leftPixels,
        int topPixels,
        int rightPixels,
        int bottomPixels,
        CandlePixelGrid grid)
    {
        return new Rect(
            leftPixels / grid.ScaleX,
            topPixels / grid.ScaleY,
            Math.Max(1, rightPixels - leftPixels) / grid.ScaleX,
            Math.Max(1, bottomPixels - topPixels) / grid.ScaleY);
    }

    private readonly record struct CandlePixelGrid(
        double ScaleX,
        double ScaleY,
        int PlotLeftPixels,
        int PlotRightPixels,
        int PlotTopPixels,
        int PlotBottomPixels,
        int SlotCount,
        double RawSlotWidthPixels);

    private readonly record struct PixelSpan(int Left, int Right)
    {
        public double Center => (Left + Right) / 2.0;
    }


    private double GetSlotCenterDip(ChartLayout layout, int slotIndex)
    {
        CandlePixelGrid grid = CreateCandlePixelGrid(layout);
        PixelSpan span = GetSlotPixelSpan(grid, slotIndex);
        return span.Center / grid.ScaleX;
    }

    private Rect GetSlotRectDip(ChartLayout layout, int slotIndex)
    {
        CandlePixelGrid grid = CreateCandlePixelGrid(layout);
        PixelSpan span = GetSlotPixelSpan(grid, slotIndex);
        return new Rect(
            span.Left / grid.ScaleX,
            layout.Plot.Top,
            Math.Max(1, span.Right - span.Left) / grid.ScaleX,
            layout.Plot.Height);
    }

    private double GetSlotWidthDip(ChartLayout layout, int slotIndex) =>
        GetSlotRectDip(layout, slotIndex).Width;

    private void DrawOhlcBars(DrawingContext drawingContext, ChartLayout layout)
    {
        Pen upPen = CreatePen(Settings.UpBorderColor, Math.Clamp(Settings.CandleBorderThickness, 0.5, 4.0), ChartLineStyle.Solid);
        Pen downPen = CreatePen(Settings.DownBorderColor, Math.Clamp(Settings.CandleBorderThickness, 0.5, 4.0), ChartLineStyle.Solid);

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double x = GetSlotCenterDip(layout, slotIndex);
            double tickLength = Math.Clamp(GetSlotWidthDip(layout, slotIndex) * 0.32, 1.5, 7.0);
            Pen pen = candle.IsBullish ? upPen : downPen;
            x = SnapStrokeCoordinate(x, pen.Thickness);
            double highY = PriceToY(candle.High, layout);
            double lowY = PriceToY(candle.Low, layout);
            double openY = PriceToY(candle.Open, layout);
            double closeY = PriceToY(candle.Close, layout);
            drawingContext.DrawLine(pen, new Point(x, highY), new Point(x, lowY));
            drawingContext.DrawLine(pen, new Point(x - tickLength, openY), new Point(x, openY));
            drawingContext.DrawLine(pen, new Point(x, closeY), new Point(x + tickLength, closeY));
        }
    }

    private void DrawCloseLine(DrawingContext drawingContext, ChartLayout layout, bool showMarkers, bool stepped)
    {
        if (layout.Count == 0)
            return;
        Pen pen = CreatePen(Settings.PriceLineColor, 1.6, ChartLineStyle.Solid);
        Brush markerBrush = BrushFrom(Settings.PriceLineColor, Color.FromRgb(91, 134, 196));
        Point? previous = null;

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            Point point = new(
                GetSlotCenterDip(layout, slotIndex),
                PriceToY(candle.Close, layout));
            if (previous.HasValue)
            {
                if (stepped)
                {
                    Point corner = new(point.X, previous.Value.Y);
                    drawingContext.DrawLine(pen, previous.Value, corner);
                    drawingContext.DrawLine(pen, corner, point);
                }
                else
                {
                    drawingContext.DrawLine(pen, previous.Value, point);
                }
            }
            if (showMarkers && GetSlotWidthDip(layout, slotIndex) >= 5)
                drawingContext.DrawEllipse(markerBrush, null, point, 2.4, 2.4);
            previous = point;
        }
    }

    private void DrawAreaChart(DrawingContext drawingContext, ChartLayout layout)
    {
        IReadOnlyList<Point> points = GetClosePoints(layout);
        if (points.Count == 0)
            return;
        Brush fill = BrushWithOpacity(Settings.PriceLineColor, 0.24, Color.FromRgb(91, 134, 196));
        Pen line = CreatePen(Settings.PriceLineColor, 1.5, ChartLineStyle.Solid);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, layout.Plot.Bottom), true, true);
            context.LineTo(points[0], true, false);
            foreach (Point point in points.Skip(1))
                context.LineTo(point, true, false);
            context.LineTo(new Point(points[^1].X, layout.Plot.Bottom), true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(fill, null, geometry);
        DrawPolyline(drawingContext, line, points);
    }

    private void DrawHlcAreaChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (layout.Count == 0)
            return;
        var highs = new List<Point>(layout.Count);
        var lows = new List<Point>(layout.Count);
        var closes = new List<Point>(layout.Count);
        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
            highs.Add(new Point(x, PriceToY(candle.High, layout)));
            lows.Add(new Point(x, PriceToY(candle.Low, layout)));
            closes.Add(new Point(x, PriceToY(candle.Close, layout)));
        }
        StreamGeometry band = new();
        using (StreamGeometryContext context = band.Open())
        {
            context.BeginFigure(highs[0], true, true);
            foreach (Point point in highs.Skip(1))
                context.LineTo(point, true, false);
            foreach (Point point in lows.AsEnumerable().Reverse())
                context.LineTo(point, true, false);
        }
        band.Freeze();
        drawingContext.DrawGeometry(BrushWithOpacity(Settings.PriceLineColor, 0.16, Color.FromRgb(91, 134, 196)), null, band);
        DrawPolyline(drawingContext, CreatePen(Settings.UpWickColor, 0.9, ChartLineStyle.Solid), highs);
        DrawPolyline(drawingContext, CreatePen(Settings.DownWickColor, 0.9, ChartLineStyle.Solid), lows);
        DrawPolyline(drawingContext, CreatePen(Settings.PriceLineColor, 1.5, ChartLineStyle.Solid), closes);
    }

    private void DrawBaselineChart(DrawingContext drawingContext, ChartLayout layout)
    {
        IReadOnlyList<Point> points = GetClosePoints(layout);
        if (points.Count == 0)
            return;
        double baselinePrice = Candles[layout.FirstIndex].Close;
        double baselineY = PriceToY(baselinePrice, layout);
        Brush above = BrushWithOpacity(Settings.UpBodyColor, 0.28, Color.FromRgb(47, 184, 137));
        Brush below = BrushWithOpacity(Settings.DownBodyColor, 0.28, Color.FromRgb(223, 92, 104));
        Pen baselinePen = CreatePen(Settings.GridColor, 1.0, ChartLineStyle.Dashed);
        drawingContext.DrawLine(baselinePen, new Point(layout.Plot.Left, baselineY), new Point(layout.Plot.Right, baselineY));

        for (int index = 1; index < points.Count; index++)
        {
            Point first = points[index - 1];
            Point second = points[index];
            bool isAbove = (first.Y + second.Y) / 2.0 <= baselineY;
            StreamGeometry segment = new();
            using (StreamGeometryContext context = segment.Open())
            {
                context.BeginFigure(new Point(first.X, baselineY), true, true);
                context.LineTo(first, true, false);
                context.LineTo(second, true, false);
                context.LineTo(new Point(second.X, baselineY), true, false);
            }
            segment.Freeze();
            drawingContext.DrawGeometry(isAbove ? above : below, null, segment);
        }
        DrawPolyline(drawingContext, CreatePen(Settings.PriceLineColor, 1.5, ChartLineStyle.Solid), points);
    }

    private void DrawColumnsChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (layout.Count == 0)
            return;
        double baseline = Candles[layout.FirstIndex].Close;
        double baselineY = PriceToY(baseline, layout);
        Brush up = BrushFrom(Settings.UpBodyColor, Color.FromRgb(47, 184, 137));
        Brush down = BrushFrom(Settings.DownBodyColor, Color.FromRgb(223, 92, 104));
        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double centerX = GetSlotCenterDip(layout, slotIndex);
            double width = Math.Max(1.0, GetSlotWidthDip(layout, slotIndex) - CandleGapPixels);
            double y = PriceToY(candle.Close, layout);
            var rect = new Rect(
                centerX - width / 2.0,
                Math.Min(y, baselineY),
                width,
                Math.Max(1.0, Math.Abs(y - baselineY)));
            drawingContext.DrawRectangle(candle.Close >= baseline ? up : down, null, rect);
        }
    }

    private void DrawHighLowChart(DrawingContext drawingContext, ChartLayout layout)
    {
        Pen up = CreatePen(Settings.UpWickColor, Math.Clamp(Settings.CandleWickThickness, 0.5, 4.0), ChartLineStyle.Solid);
        Pen down = CreatePen(Settings.DownWickColor, Math.Clamp(Settings.CandleWickThickness, 0.5, 4.0), ChartLineStyle.Solid);
        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double x = GetSlotCenterDip(layout, slotIndex);
            double closeTick = Math.Clamp(GetSlotWidthDip(layout, slotIndex) * 0.22, 1.0, 5.0);
            Pen pen = candle.IsBullish ? up : down;
            drawingContext.DrawLine(pen, new Point(x, PriceToY(candle.High, layout)), new Point(x, PriceToY(candle.Low, layout)));
            double closeY = PriceToY(candle.Close, layout);
            drawingContext.DrawLine(pen, new Point(x - closeTick, closeY), new Point(x + closeTick, closeY));
        }
    }


    private void DrawKagiChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (layout.Count == 0)
            return;

        Pen upPen = CreatePen(Settings.UpBorderColor, 2.4, ChartLineStyle.Solid);
        Pen downPen = CreatePen(Settings.DownBorderColor, 1.4, ChartLineStyle.Solid);
        Point? previousEnd = null;

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle segment = Candles[layout.FirstIndex + visibleIndex];
            double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
            double openY = PriceToY(segment.Open, layout);
            double closeY = PriceToY(segment.Close, layout);
            Pen pen = segment.IsBullish ? upPen : downPen;
            x = SnapStrokeCoordinate(x, pen.Thickness);

            if (previousEnd.HasValue)
                drawingContext.DrawLine(pen, previousEnd.Value, new Point(x, openY));
            drawingContext.DrawLine(pen, new Point(x, openY), new Point(x, closeY));
            previousEnd = new Point(x, closeY);
        }
    }

    private void DrawPointAndFigureChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (layout.Count == 0)
            return;

        double point = Candles[layout.FirstIndex].Point;
        double boxSize = Math.Max(point, point * Math.Clamp(Settings.SyntheticBoxSizePoints, 1, 1_000_000));
        Pen xPen = CreatePen(Settings.UpBorderColor, 1.5, ChartLineStyle.Solid);
        Pen oPen = CreatePen(Settings.DownBorderColor, 1.5, ChartLineStyle.Solid);

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle column = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double centerX = GetSlotCenterDip(layout, slotIndex);
            double slotWidth = GetSlotWidthDip(layout, slotIndex);
            double halfWidth = Math.Clamp(slotWidth * 0.28, 1.5, 7.0);
            int boxes = Math.Clamp((int)Math.Round((column.High - column.Low) / boxSize) + 1, 1, 10_000);

            if (slotWidth < 9.0 || layout.Count > 300 || boxes > 80)
            {
                Pen compactPen = column.IsBullish ? xPen : oPen;
                drawingContext.DrawLine(
                    compactPen,
                    new Point(centerX, PriceToY(column.High, layout)),
                    new Point(centerX, PriceToY(column.Low, layout)));
                continue;
            }

            for (int boxIndex = 0; boxIndex < boxes; boxIndex++)
            {
                double price = column.Low + boxIndex * boxSize;
                double y = PriceToY(price, layout);
                double halfHeight = Math.Clamp(Math.Abs(PriceToY(price + boxSize * 0.42, layout) - y), 1.5, 7.0);
                if (column.IsBullish)
                {
                    drawingContext.DrawLine(xPen, new Point(centerX - halfWidth, y - halfHeight), new Point(centerX + halfWidth, y + halfHeight));
                    drawingContext.DrawLine(xPen, new Point(centerX - halfWidth, y + halfHeight), new Point(centerX + halfWidth, y - halfHeight));
                }
                else
                {
                    drawingContext.DrawEllipse(null, oPen, new Point(centerX, y), halfWidth, halfHeight);
                }
            }
        }
    }

    private void DrawTimePriceOpportunityChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (layout.Count == 0)
            return;

        int rows = Math.Clamp(Settings.MarketProfileRows, 12, 200);
        double rowPrice = (layout.MaximumPrice - layout.MinimumPrice) / rows;
        if (rowPrice <= 0)
            return;

        Brush textBrush = BrushFrom(Settings.ChartTextColor, Color.FromRgb(216, 216, 216));
        Brush sessionBrush = BrushWithOpacity(Settings.GridColor, 0.70, Color.FromRgb(42, 42, 42));
        Pen sessionPen = new(sessionBrush, 1.0);
        if (sessionPen.CanFreeze)
            sessionPen.Freeze();
        int bracketSeconds = Math.Clamp(Settings.TpoBracketMinutes, 1, 240) * 60;

        foreach (IGrouping<long, (Candle Candle, int VisibleIndex)> session in Enumerable
                     .Range(0, layout.Count)
                     .Select(visibleIndex => (Candle: Candles[layout.FirstIndex + visibleIndex], VisibleIndex: visibleIndex))
                     .GroupBy(item => GetProfileSessionStartUnix(item.Candle.StartUnix)))
        {
            var entries = session.OrderBy(item => item.Candle.StartUnix).ToArray();
            if (entries.Length == 0)
                continue;

            int firstSlot = layout.VisibleSlots[entries[0].VisibleIndex];
            int lastSlot = layout.VisibleSlots[entries[^1].VisibleIndex];
            double sessionLeft = GetSlotRectDip(layout, firstSlot).Left;
            double sessionRight = GetSlotRectDip(layout, lastSlot).Right;
            drawingContext.DrawLine(sessionPen, new Point(sessionLeft, layout.Plot.Top), new Point(sessionLeft, layout.Plot.Bottom));

            var rowsText = new Dictionary<int, List<char>>();
            foreach ((Candle candle, _) in entries)
            {
                int bracket = Math.Max(0, (int)((candle.StartUnix - session.Key) / bracketSeconds));
                char symbol = TpoSymbol(bracket);
                int lowRow = Math.Clamp((int)Math.Floor((candle.Low - layout.MinimumPrice) / rowPrice), 0, rows - 1);
                int highRow = Math.Clamp((int)Math.Floor((candle.High - layout.MinimumPrice) / rowPrice), 0, rows - 1);
                for (int row = lowRow; row <= highRow; row++)
                {
                    if (!rowsText.TryGetValue(row, out List<char>? symbols))
                    {
                        symbols = new List<char>();
                        rowsText[row] = symbols;
                    }
                    if (symbols.Count == 0 || symbols[^1] != symbol)
                        symbols.Add(symbol);
                }
            }

            double fontSize = Math.Clamp(layout.Plot.Height / rows * 0.72, 6.0, 11.0);
            foreach ((int row, List<char> symbols) in rowsText)
            {
                string label = new(symbols.ToArray());
                FormattedText text = CreateText(label, fontSize, textBrush);
                text.MaxTextWidth = Math.Max(1.0, sessionRight - sessionLeft - 2.0);
                text.Trimming = TextTrimming.CharacterEllipsis;
                double price = layout.MinimumPrice + (row + 0.5) * rowPrice;
                double y = PriceToY(price, layout) - text.Height / 2.0;
                drawingContext.DrawText(text, new Point(sessionLeft + 2.0, y));
            }
        }
    }

    private void DrawSessionVolumeProfileChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (!OrderFlowProfile.HasRealVolume || OrderFlowProfile.Sessions.Count == 0)
        {
            DrawOrderFlowUnavailable(drawingContext, layout);
            return;
        }

        Brush volumeBrush = BrushWithOpacity(Settings.UpBodyColor, 0.58, Color.FromRgb(47, 184, 137));
        Brush valueAreaBrush = BrushWithOpacity(Settings.LatestButtonColor, 0.12, Color.FromRgb(91, 134, 196));
        Pen pocPen = CreatePen(Settings.SelectedCandleColor, 1.5, ChartLineStyle.Solid);
        Pen sessionPen = CreatePen(Settings.GridColor, 1.0, ChartLineStyle.Dotted, 0.85);

        foreach (SessionVolumeProfileData session in OrderFlowProfile.Sessions)
        {
            int firstVisible = -1;
            int lastVisible = -1;
            for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
            {
                Candle candle = Candles[layout.FirstIndex + visibleIndex];
                if (candle.StartUnix >= session.SessionStartUnix && candle.StartUnix < session.SessionEndUnix)
                {
                    firstVisible = firstVisible < 0 ? visibleIndex : firstVisible;
                    lastVisible = visibleIndex;
                }
            }
            if (firstVisible < 0 || lastVisible < 0 || session.MaximumLevelVolume <= 0)
                continue;

            double sessionLeft = GetSlotRectDip(layout, layout.VisibleSlots[firstVisible]).Left;
            double sessionRight = GetSlotRectDip(layout, layout.VisibleSlots[lastVisible]).Right;
            double maximumWidth = Math.Max(8.0, (sessionRight - sessionLeft) * 0.42);
            drawingContext.DrawLine(sessionPen, new Point(sessionLeft, layout.Plot.Top), new Point(sessionLeft, layout.Plot.Bottom));

            if (Settings.ShowVolumeProfileValueArea && session.ValueAreaHigh > session.ValueAreaLow)
            {
                double top = PriceToY(session.ValueAreaHigh, layout);
                double bottom = PriceToY(session.ValueAreaLow, layout);
                drawingContext.DrawRectangle(valueAreaBrush, null, new Rect(sessionLeft, top, Math.Max(1.0, sessionRight - sessionLeft), Math.Max(1.0, bottom - top)));
            }

            double levelHeight = Math.Clamp(
                Math.Abs(PriceToY(session.PointOfControlPrice + OrderFlowProfile.PriceStep, layout) - PriceToY(session.PointOfControlPrice, layout)),
                1.0,
                18.0);
            foreach (VolumeProfileLevel level in session.Levels)
            {
                if (level.Price < layout.MinimumPrice || level.Price > layout.MaximumPrice)
                    continue;
                double width = maximumWidth * level.Volume / session.MaximumLevelVolume;
                double y = PriceToY(level.Price, layout) - levelHeight / 2.0;
                drawingContext.DrawRectangle(volumeBrush, null, new Rect(sessionRight - width, y, Math.Max(1.0, width), levelHeight));
            }

            double pocY = SnapToDeviceStroke(PriceToY(session.PointOfControlPrice, layout), pocPen.Thickness);
            drawingContext.DrawLine(pocPen, new Point(sessionRight - maximumWidth, pocY), new Point(sessionRight, pocY));
        }
    }

    private void DrawVolumeFootprintChart(DrawingContext drawingContext, ChartLayout layout)
    {
        if (!OrderFlowProfile.HasRealVolume || OrderFlowProfile.Footprints.Count == 0)
        {
            DrawOrderFlowUnavailable(drawingContext, layout);
            return;
        }

        double averageSlotWidth = layout.Plot.Width / layout.SlotCount;
        Brush bidBrush = BrushWithOpacity(Settings.DownBodyColor, 0.24, Color.FromRgb(223, 92, 104));
        Brush askBrush = BrushWithOpacity(Settings.UpBodyColor, 0.24, Color.FromRgb(47, 184, 137));
        Brush bidText = BrushFrom(Settings.DownBorderColor, Color.FromRgb(240, 123, 133));
        Brush askText = BrushFrom(Settings.UpBorderColor, Color.FromRgb(83, 207, 164));
        Pen upBorder = CreatePen(Settings.UpBorderColor, Math.Max(1.0, Settings.CandleBorderThickness), ChartLineStyle.Solid);
        Pen downBorder = CreatePen(Settings.DownBorderColor, Math.Max(1.0, Settings.CandleBorderThickness), ChartLineStyle.Solid);
        double fontSize = Math.Clamp(averageSlotWidth * 0.13, 6.0, 10.0);

        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double centerX = GetSlotCenterDip(layout, slotIndex);
            double slotWidth = GetSlotWidthDip(layout, slotIndex);
            Pen border = candle.IsBullish ? upBorder : downBorder;
            double bodyWidth = Math.Max(1.0, slotWidth - CandleGapPixels);
            Rect body = SnapRectangleToDevicePixels(
                new Rect(
                    centerX - bodyWidth / 2.0,
                    Math.Min(PriceToY(candle.Open, layout), PriceToY(candle.Close, layout)),
                    bodyWidth,
                    Math.Max(1.0, Math.Abs(PriceToY(candle.Open, layout) - PriceToY(candle.Close, layout)))),
                border.Thickness);
            drawingContext.DrawRectangle(null, border, body);

            if (!OrderFlowProfile.Footprints.TryGetValue(candle.StartUnix, out FootprintCandleProfile? footprint))
                continue;

            if (slotWidth < 52.0)
            {
                double maximum = Math.Max(1.0, Math.Max(Math.Abs(footprint.Delta), footprint.TotalAskVolume + footprint.TotalBidVolume));
                double ratio = Math.Clamp(Math.Abs(footprint.Delta) / maximum, 0.08, 1.0);
                Brush deltaBrush = footprint.Delta >= 0 ? askText : bidText;
                double compactWidth = Math.Max(2.0, (slotWidth - CandleGapPixels) * ratio);
                drawingContext.DrawRectangle(deltaBrush, null, new Rect(centerX - compactWidth / 2.0, layout.Plot.Bottom - 5.0, compactWidth, 3.0));
                continue;
            }

            foreach (FootprintPriceLevel level in footprint.Levels)
            {
                if (level.Price < layout.MinimumPrice || level.Price > layout.MaximumPrice)
                    continue;
                double y = PriceToY(level.Price, layout);
                double nextY = PriceToY(level.Price + OrderFlowProfile.PriceStep, layout);
                double rowHeight = Math.Clamp(Math.Abs(nextY - y), 8.0, 22.0);
                if (rowHeight < 8.0)
                    continue;
                double half = Math.Max(8.0, (slotWidth - CandleGapPixels) / 2.0);
                double intensity = footprint.MaximumLevelVolume <= 0 ? 0 : level.TotalVolume / footprint.MaximumLevelVolume;
                double fillWidth = half * Math.Clamp(intensity, 0.08, 1.0);
                drawingContext.DrawRectangle(bidBrush, null, new Rect(centerX - fillWidth, y - rowHeight / 2.0, fillWidth, rowHeight));
                drawingContext.DrawRectangle(askBrush, null, new Rect(centerX, y - rowHeight / 2.0, fillWidth, rowHeight));

                FormattedText bid = CreateText(FormatProfileVolume(level.BidVolume), fontSize, bidText);
                FormattedText ask = CreateText(FormatProfileVolume(level.AskVolume), fontSize, askText);
                drawingContext.DrawText(bid, new Point(centerX - bid.Width - 2.0, y - bid.Height / 2.0));
                drawingContext.DrawText(ask, new Point(centerX + 2.0, y - ask.Height / 2.0));
            }

            if (Settings.ShowFootprintDelta)
            {
                Brush deltaTextBrush = footprint.Delta >= 0 ? askText : bidText;
                FormattedText delta = CreateText($"Δ {FormatProfileVolume(footprint.Delta)}", Math.Max(6.0, fontSize - 0.5), deltaTextBrush);
                drawingContext.DrawText(delta, new Point(centerX - delta.Width / 2.0, layout.Plot.Bottom - delta.Height - 2.0));
            }
        }

        if (averageSlotWidth < 52.0)
        {
            FormattedText hint = CreateText("Zoom in to display bid × ask numbers", 10.0, BrushFrom(Settings.ChartTextColor, Colors.White));
            drawingContext.DrawText(hint, new Point(layout.Plot.Left + 8.0, layout.Plot.Top + 8.0));
        }
    }

    private void DrawOrderFlowUnavailable(DrawingContext drawingContext, ChartLayout layout)
    {
        string message = string.IsNullOrWhiteSpace(OrderFlowProfile.StatusMessage)
            ? "This chart requires real trade volume, but the connected broker is not providing it."
            : OrderFlowProfile.StatusMessage;
        FormattedText text = CreateText(message, 12.0, BrushFrom(Settings.ChartTextColor, Colors.White));
        text.MaxTextWidth = Math.Max(80.0, layout.Plot.Width - 40.0);
        text.TextAlignment = TextAlignment.Center;
        drawingContext.DrawText(
            text,
            new Point(layout.Plot.Left + 20.0, layout.Plot.Top + Math.Max(10.0, (layout.Plot.Height - text.Height) / 2.0)));
    }

    private long GetProfileSessionStartUnix(long unixSeconds)
    {
        TimeSpan offset = TimeSpan.FromMinutes(Math.Clamp(ServerUtcOffsetMinutes, -14 * 60, 14 * 60));
        DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset);
        int hour = Math.Clamp(Settings.ProfileSessionStartHour, 0, 23);
        DateOnly date = DateOnly.FromDateTime(local.DateTime);
        if (local.Hour < hour)
            date = date.AddDays(-1);
        DateTime localStart = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(localStart, offset).ToUnixTimeSeconds();
    }

    private static char TpoSymbol(int bracket)
    {
        int normalized = Math.Abs(bracket) % 52;
        return normalized < 26 ? (char)('A' + normalized) : (char)('a' + normalized - 26);
    }

    private static string FormatProfileVolume(double value)
    {
        double absolute = Math.Abs(value);
        string prefix = value < 0 ? "-" : string.Empty;
        if (absolute >= 1_000_000_000)
            return prefix + (absolute / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "B";
        if (absolute >= 1_000_000)
            return prefix + (absolute / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        if (absolute >= 1_000)
            return prefix + (absolute / 1_000d).ToString("0.##", CultureInfo.InvariantCulture) + "K";
        return prefix + absolute.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<Point> GetClosePoints(ChartLayout layout)
    {
        var points = new List<Point>(layout.Count);
        for (int visibleIndex = 0; visibleIndex < layout.Count; visibleIndex++)
        {
            Candle candle = Candles[layout.FirstIndex + visibleIndex];
            points.Add(new Point(
                GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]),
                PriceToY(candle.Close, layout)));
        }
        return points;
    }

    private static void DrawPolyline(DrawingContext drawingContext, Pen pen, IReadOnlyList<Point> points)
    {
        for (int index = 1; index < points.Count; index++)
            drawingContext.DrawLine(pen, points[index - 1], points[index]);
    }

    private static Pen CreateCrispCandlePen(string colorText, double thickness)
    {
        Color color = ColorFrom(colorText, Colors.White);
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
            brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            DashStyle = DashStyles.Solid,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Miter
        };
        if (pen.CanFreeze)
            pen.Freeze();
        return pen;
    }

    private static Brush BrushWithOpacity(string value, double opacity, Color fallback)
    {
        Brush source = BrushFrom(value, fallback);
        Color color = source is SolidColorBrush solid ? solid.Color : fallback;
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255),
            color.R,
            color.G,
            color.B));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }
}
