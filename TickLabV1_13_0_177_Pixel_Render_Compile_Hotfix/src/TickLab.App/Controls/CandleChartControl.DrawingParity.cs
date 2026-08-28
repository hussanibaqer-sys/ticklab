using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TickLab.Core.Drawing;
using TickLab.Core.Market;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl
{
    private bool DrawTradingViewParityDrawing(
        DrawingContext dc,
        ChartLayout layout,
        ChartDrawing drawing,
        DrawingToolDefinition tool,
        Point[] points,
        Pen pen,
        Brush fill,
        Brush textBrush,
        bool preview)
    {
        switch (drawing.ToolId)
        {
            case "trend-line":
            case "info-line":
            case "trend-angle":
                if (points.Length >= 2)
                    DrawParityTrendLine(dc, layout, drawing, points[0], points[1], pen, textBrush);
                return true;
            case "ray":
                if (points.Length >= 2)
                    DrawParityRay(dc, layout, drawing, points[0], points[1], pen, textBrush, false);
                return true;
            case "extended-line":
                if (points.Length >= 2)
                    DrawParityRay(dc, layout, drawing, points[0], points[1], pen, textBrush, true);
                return true;
            case "horizontal-line":
                DrawHorizontal(dc, layout.Plot, points[0], pen, true, true);
                if (drawing.Style.ShowPriceLabels)
                    DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, points[0], textBrush);
                return true;
            case "horizontal-ray":
                DrawHorizontal(dc, layout.Plot, points[0], pen, false, true);
                if (drawing.Style.ShowPriceLabels)
                    DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, points[0], textBrush);
                return true;
            case "vertical-line":
                dc.DrawLine(pen, new Point(points[0].X, layout.Plot.Top), new Point(points[0].X, layout.Plot.Bottom));
                if (drawing.Style.ShowTimeLabels)
                    DrawParityTimeLabel(dc, layout, drawing.Anchors[0].StartUnix, points[0].X, textBrush);
                return true;
            case "cross-line":
                dc.DrawLine(pen, new Point(layout.Plot.Left, points[0].Y), new Point(layout.Plot.Right, points[0].Y));
                dc.DrawLine(pen, new Point(points[0].X, layout.Plot.Top), new Point(points[0].X, layout.Plot.Bottom));
                if (drawing.Style.ShowPriceLabels)
                    DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, points[0], textBrush);
                if (drawing.Style.ShowTimeLabels)
                    DrawParityTimeLabel(dc, layout, drawing.Anchors[0].StartUnix, points[0].X, textBrush);
                return true;
            case "parallel-channel":
                if (points.Length >= 3)
                    DrawParityParallelChannel(dc, layout, drawing, points, pen, fill);
                return true;
            case "flat-top-bottom":
                if (points.Length >= 3)
                    DrawParityFlatTopBottom(dc, layout, drawing, points, pen, fill);
                return true;
            case "disjoint-channel":
                if (points.Length >= 3)
                    DrawParityDisjointChannel(dc, layout, drawing, points, pen, fill);
                return true;
            case "regression-trend":
                DrawParityRegression(dc, layout, drawing, pen, fill, textBrush);
                return true;
            case "anchored-vwap":
                DrawParityAnchoredVwap(dc, layout, drawing, pen);
                return true;
            case "fib-retracement":
                if (points.Length >= 2)
                    DrawParityFibRetracement(dc, layout, drawing, points, pen);
                return true;
            case "fib-channel":
                if (points.Length >= 3)
                    DrawParityFibChannel(dc, layout, drawing, points, pen, fill);
                return true;
            case "fib-time-zone":
                if (points.Length >= 2)
                    DrawParityFibTimeZone(dc, layout, drawing, points, pen);
                return true;
            case "fib-spiral":
                if (points.Length >= 2)
                    DrawParityFibSpiral(dc, layout, drawing, points[0], points[1], pen);
                return true;
            case "trend-fib-extension":
                if (points.Length >= 3)
                    DrawParityFibExtension(dc, layout, drawing, points, pen);
                return true;
            case "trend-fib-time":
                if (points.Length >= 3)
                    DrawParityTrendFibTime(dc, layout, drawing, points, pen);
                return true;
            case "fib-circles":
                if (points.Length >= 2)
                    DrawParityFibCircles(dc, layout, drawing, points, pen);
                return true;
            case "fib-speed-arcs":
                if (points.Length >= 2)
                    DrawParityFibArcs(dc, layout, drawing, points, pen);
                return true;
            case "fib-speed-fan":
                if (points.Length >= 2)
                    DrawParityFibFan(dc, layout, drawing, points, pen, false);
                return true;
            case "gann-fan":
                if (points.Length >= 2)
                    DrawParityFibFan(dc, layout, drawing, points, pen, true);
                return true;
            case "pitchfan":
                if (points.Length >= 3)
                    DrawParityPitchfan(dc, layout, drawing, points, pen);
                return true;
            case "fib-wedge":
                if (points.Length >= 3)
                    DrawParityFibWedge(dc, layout, drawing, points, pen);
                return true;
            case "pitchfork":
            case "schiff-pitchfork":
            case "modified-schiff-pitchfork":
            case "inside-pitchfork":
                if (points.Length >= 3)
                    DrawParityPitchfork(dc, layout.Plot, drawing, points, pen, fill);
                return true;
            case "gann-box":
            case "gann-square":
            case "gann-square-fixed":
                if (points.Length >= 2)
                    DrawParityGann(dc, layout, drawing, points[0], points[1], pen, fill);
                return true;
            case "circle":
                if (points.Length >= 2)
                    DrawParityCircle(dc, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "text":
                DrawParityText(dc, drawing, points[0], textBrush);
                return true;
            case "note":
                if (points.Length >= 2)
                    DrawParityNote(dc, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "pin":
                DrawParityPin(dc, drawing, points[0], pen, fill, textBrush);
                return true;
            case "table":
                if (points.Length >= 2)
                    DrawParityTable(dc, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "callout":
                if (points.Length >= 2)
                    DrawParityCallout(dc, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "comment":
                DrawParityComment(dc, drawing, points[0], pen, fill, textBrush);
                return true;
            case "signpost":
                DrawParitySignpost(dc, layout, drawing, points[0], pen, fill, textBrush);
                return true;
            case "price-label":
                DrawParityPriceLabel(dc, drawing, points[0], pen, fill, textBrush);
                return true;
            case "price-note":
                if (points.Length >= 2)
                    DrawParityPriceNote(dc, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "flag-mark":
                DrawParityFlag(dc, drawing, points[0], pen, fill);
                return true;
            case "anchored-note":
                if (points.Length >= 1)
                    DrawParityAnchoredNote(dc, drawing, points[0], pen, fill, textBrush);
                return true;
            case "path":
                DrawParityPath(dc, points, pen);
                return true;
            case "xabcd-pattern":
            case "cypher-pattern":
            case "abcd-pattern":
            case "triangle-pattern":
            case "three-drives-pattern":
            case "head-shoulders":
            case "elliott-impulse":
            case "elliott-triangle":
            case "elliott-triple-combo":
            case "elliott-correction":
            case "elliott-double-combo":
                DrawParityPattern(dc, layout, drawing, points, pen, fill, textBrush);
                return true;
            case "long-position":
            case "short-position":
                if (points.Length >= 3)
                    DrawParityPosition(dc, layout, drawing, points, pen, textBrush);
                return true;
            case "position-forecast":
                if (points.Length >= 2)
                    DrawParityForecast(dc, layout, drawing, points, pen, fill, textBrush);
                return true;
            case "price-range":
            case "date-range":
            case "date-price-range":
                if (points.Length >= 2)
                    DrawParityRangeMeasurement(dc, layout, drawing, points[0], points[1], pen, fill, textBrush);
                return true;
            case "cyclic-lines":
            case "time-cycles":
                if (points.Length >= 2)
                    DrawParityCycles(dc, layout.Plot, drawing, points, pen);
                return true;
            case "sine-line":
                if (points.Length >= 2)
                    DrawParitySine(dc, layout.Plot, drawing, points[0], points[1], pen);
                return true;
            case "sector":
                if (points.Length >= 3)
                    DrawParitySector(dc, drawing, points, pen, fill, textBrush);
                return true;
            case "bars-pattern":
                if (points.Length >= 3)
                    DrawParityBarsPattern(dc, layout, drawing, pen);
                return true;
            case "ghost-feed":
                if (points.Length >= 2)
                    DrawParityGhostFeed(dc, layout, drawing, pen);
                return true;
            case "fixed-volume-profile":
            case "anchored-volume-profile":
                DrawParityVolumeProfile(dc, layout, drawing, points, pen);
                return true;
        }
        return false;
    }

    private static double ParityOption(ChartDrawing drawing, string name, double fallback)
    {
        if (drawing.NumericOptions.TryGetValue(name, out double value))
            return value;
        IReadOnlyDictionary<string, double> defaults = DrawingParityDefaults.NumericOptions(drawing.ToolId);
        return defaults.TryGetValue(name, out value) ? value : fallback;
    }

    private static bool ParityFlag(ChartDrawing drawing, string name, bool fallback = false) =>
        ParityOption(drawing, name, fallback ? 1 : 0) >= 0.5;

    private static Pen ParityLevelPen(ChartDrawing drawing, DrawingLevel level, double opacityMultiplier = 1)
    {
        var p = new Pen(
            CreateDrawingBrush(level.Color, drawing.Style.Opacity * opacityMultiplier),
            Math.Clamp(level.Width, 0.5, 20));
        p.DashStyle = level.LineStyle switch
        {
            DrawingLineStyle.Dashed => DashStyles.Dash,
            DrawingLineStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };
        p.StartLineCap = PenLineCap.Flat;
        p.EndLineCap = PenLineCap.Flat;
        p.DashCap = PenLineCap.Flat;
        if (p.CanFreeze) p.Freeze();
        return p;
    }

    private static DrawingLevel? ParityRoleLevel(ChartDrawing drawing, string label, int fallbackIndex = -1)
    {
        IReadOnlyList<DrawingLevel> levels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        DrawingLevel? byLabel = levels.FirstOrDefault(level => string.Equals(level.Label, label, StringComparison.OrdinalIgnoreCase));
        if (byLabel is not null)
            return byLabel;
        return fallbackIndex >= 0 && fallbackIndex < levels.Count ? levels[fallbackIndex] : null;
    }

    private static Brush ParityRoleFill(ChartDrawing drawing, DrawingLevel? level, string fallbackColor, double fallbackOpacity)
    {
        string color = level is null || string.IsNullOrWhiteSpace(level.FillColor) ? fallbackColor : level.FillColor;
        double opacity = level is not null && level.FillOpacity >= 0
            ? Math.Clamp(level.FillOpacity, 0, 1)
            : Math.Clamp(fallbackOpacity, 0, 1);
        return CreateDrawingBrush(color, opacity);
    }

    // Folder-2 common visual rules.  Level strokes keep their own colour;
    // background bands follow the colour of the line immediately above/inside
    // them and all bands share the one Background opacity control.
    private static double ParityBandOpacity(ChartDrawing drawing) =>
        ParityFlag(drawing, "Bands", true) ? Math.Clamp(drawing.Style.FillOpacity, 0, 1) : 0;

    private static bool ParityShowReadings(ChartDrawing drawing) =>
        ParityFlag(drawing, "ShowLevelReadings", true);

    private static bool ParityShowPrices(ChartDrawing drawing) =>
        drawing.NumericOptions.TryGetValue("ShowLevelPrices", out double value)
            ? value >= 0.5
            : drawing.Style.ShowPriceLabels;

    private static string ParityReadingText(ChartDrawing drawing, DrawingLevel level, double? price = null)
    {
        bool showValue = ParityShowReadings(drawing) && level.ShowValue;
        bool showPrice = ParityShowPrices(drawing) && level.ShowPrice && price.HasValue;
        if (!showValue && !showPrice) return string.Empty;

        string ratio = ParityFlag(drawing, "LabelsPercent", false)
            ? $"{level.Value * 100:0.###}%"
            : level.Label;
        string label = showValue ? ratio : string.Empty;
        if (showPrice)
        {
            string p = price.GetValueOrDefault().ToString("0.########", CultureInfo.InvariantCulture);
            label = string.IsNullOrWhiteSpace(label) ? p : $"{label}  {p}";
        }
        return label;
    }

    private static Point ParityHorizontalReadingPoint(ChartDrawing drawing, double left, double right, double y)
    {
        int horizontal = (int)Math.Round(ParityOption(drawing, "LabelHorizontal", ParityFlag(drawing, "LabelsLeft", false) ? -1 : 1));
        bool outside = ParityFlag(drawing, "LabelsOutside", false);
        double x = horizontal < 0
            ? (outside ? left - 104 : left + 4)
            : horizontal > 0
                ? (outside ? right + 6 : Math.Max(left + 4, right - 104))
                : (left + right) / 2.0 - 48;
        int vertical = (int)Math.Round(ParityOption(drawing, "LabelVertical", -1));
        double yy = vertical < 0 ? y - 14 : vertical > 0 ? y + 3 : y - 5;
        return new Point(x, yy);
    }

    private static bool Nearly(double a, double b, double epsilon = 0.0005) => Math.Abs(a - b) <= epsilon;

    private static double CrispStroke(double value, double width)
    {
        // Align odd-pixel strokes to half pixels and even-pixel strokes to full
        // pixels. This avoids the soft/blurry look caused by WPF sub-pixel lines.
        double roundedWidth = Math.Max(1, Math.Round(width));
        return ((long)roundedWidth & 1) == 1 ? Math.Floor(value) + 0.5 : Math.Round(value);
    }

    private static Point GannFanTarget(Point origin, Point anchor, double ratio, bool reverse)
    {
        Vector v = anchor - origin;
        if (reverse) v = new Vector(v.X, -v.Y);
        double r = Math.Max(0.000001, Math.Abs(ratio));
        return r <= 1
            ? new Point(origin.X + v.X, origin.Y + v.Y * r)
            : new Point(origin.X + v.X / r, origin.Y + v.Y);
    }

    private static Point GannBoxAngleTarget(Rect rect, Point origin, double ratio, bool reverse)
    {
        double r = Math.Max(0.000001, Math.Abs(ratio));
        double sx = reverse ? -1 : 1;
        return r <= 1
            ? new Point(origin.X + sx * rect.Width * r, origin.Y - rect.Height)
            : new Point(origin.X + sx * rect.Width, origin.Y - rect.Height / r);
    }

    private static string GannFanRatioLabel(double ratio)
    {
        double r = Math.Abs(ratio);
        if (Nearly(r, 0.125)) return "1/8";
        if (Nearly(r, 0.25)) return "1/4";
        if (Nearly(r, 1.0 / 3.0)) return "1/3";
        if (Nearly(r, 0.5)) return "1/2";
        if (Nearly(r, 1)) return "1/1";
        if (Nearly(r, 2)) return "2/1";
        if (Nearly(r, 3)) return "3/1";
        if (Nearly(r, 4)) return "4/1";
        if (Nearly(r, 8)) return "8/1";
        return ratio.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string GannFanReadingText(ChartDrawing drawing, DrawingLevel level, double? price)
    {
        // The Gann Fan master reading switch is authoritative.  Older saved
        // workspaces could contain stale per-level ShowValue=false flags even
        // while "Level readings" was visibly checked, which hid every ratio.
        bool showReading = ParityShowReadings(drawing);
        bool showPrice = ParityShowPrices(drawing) && level.ShowPrice && price.HasValue;
        if (!showReading && !showPrice)
            return string.Empty;

        string label = showReading ? GannFanRatioLabel(level.Value) : string.Empty;
        if (showPrice)
        {
            string p = price.GetValueOrDefault().ToString("0.########", CultureInfo.InvariantCulture);
            label = string.IsNullOrWhiteSpace(label) ? p : $"{label}  {p}";
        }
        return label;
    }


    private void DrawParityTrendLine(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush textBrush)
    {
        DrawLineWithOptions(dc, layout.Plot, p1, p2, pen, drawing.Style);
        bool selected = _selectedDrawingIds.Contains(drawing.Id);
        bool showStats = drawing.Style.ShowStatistics &&
            (ParityFlag(drawing, "AlwaysShowStats", false) || selected || drawing.ToolId == "info-line");
        bool trendAngle = drawing.ToolId == "trend-angle";
        bool showAngle = trendAngle || ParityFlag(drawing, "AngleLabel", false);
        bool middlePoint = ParityFlag(drawing, "MiddlePoint", false) || drawing.Style.ShowMiddleLine;

        if (middlePoint)
            DrawParityAnchorGlyph(dc, Mid(p1, p2), pen.Brush, 2.8, false);
        if (drawing.Style.ShowPriceLabels)
        {
            DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, p1, textBrush);
            DrawPriceLabel(dc, layout, drawing.Anchors[1].Price, p2, textBrush);
        }

        double angle = Math.Atan2(-(p2.Y - p1.Y), p2.X - p1.X) * 180 / Math.PI;
        if (trendAngle)
            DrawParityTrendAngleGuide(dc, layout.Plot, p1, p2, pen, angle);

        if (showStats)
        {
            double priceDifference = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
            double percent = drawing.Anchors[0].Price == 0 ? 0 : priceDifference / drawing.Anchors[0].Price * 100;
            int horizontalCount = Math.Abs(FindNearestDrawingCandleIndex(drawing.Anchors[1]) - FindNearestDrawingCandleIndex(drawing.Anchors[0]));
            string horizontalUnit = _rawTickDrawingSurface
                ? (horizontalCount == 1 ? "tick" : "ticks")
                : (horizontalCount == 1 ? "bar" : "bars");
            string duration = _rawTickDrawingSurface
                ? FormatRawTickMeasurementDuration(Math.Abs(
                    DrawingAnchorMilliseconds(drawing.Anchors[1]) - DrawingAnchorMilliseconds(drawing.Anchors[0])))
                : FormatParityDuration(Math.Abs(drawing.Anchors[1].StartUnix - drawing.Anchors[0].StartUnix));
            string label = $"{priceDifference:+0.########;-0.########;0} ({percent:+0.##;-0.##;0}%)\n{horizontalCount} {horizontalUnit}, {duration}";
            bool left = drawing.TextOptions.TryGetValue("StatsPosition", out string? position) &&
                string.Equals(position, "Left", StringComparison.OrdinalIgnoreCase);
            Point at = left ? p1 + new Vector(-126, 9) : p2 + new Vector(9, 9);
            DrawParityLabel(dc, label, at, textBrush, drawing.Style, true);
        }

        if (showAngle && !trendAngle)
        {
            Point at = Mid(p1, p2) + new Vector(7, -22);
            DrawParityPlainLabel(dc, $"{angle:0.##}°", at, pen.Brush, Math.Min(11, drawing.Style.FontSize));
        }
        if (!string.IsNullOrWhiteSpace(drawing.Text))
            DrawParityLabel(dc, drawing.Text, Mid(p1, p2) + new Vector(7, 7), textBrush, drawing.Style, false);
    }

    private static string FormatParityDuration(long totalSeconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    private void DrawParityTrendAngleGuide(DrawingContext dc, Rect plot, Point p1, Point p2, Pen sourcePen, double displayAngle)
    {
        if (Math.Abs(p2.X - p1.X) < 0.5 && Math.Abs(p2.Y - p1.Y) < 0.5)
            return;

        double direction = p2.X >= p1.X ? 1 : -1;
        double baselineLength = Math.Min(58, Math.Max(25, Math.Abs(p2.X - p1.X)));
        Point baselineEnd = new(Math.Clamp(p1.X + direction * baselineLength, plot.Left, plot.Right), p1.Y);
        var guidePen = new Pen(sourcePen.Brush, Math.Max(0.8, sourcePen.Thickness * 0.65))
        {
            DashStyle = DashStyles.Dot,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(guidePen, p1, baselineEnd);

        // TradingView shows a small angle arc at the first anchor.  Sampling the arc
        // rather than using a Path control keeps it inside the chart's retained drawing pass.
        double visualAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
        double baselineAngle = direction > 0 ? 0 : Math.PI;
        double delta = visualAngle - baselineAngle;
        while (delta > Math.PI) delta -= Math.PI * 2;
        while (delta < -Math.PI) delta += Math.PI * 2;
        double radius = 22;
        const int steps = 14;
        Point previous = new(p1.X + Math.Cos(baselineAngle) * radius, p1.Y + Math.Sin(baselineAngle) * radius);
        for (int i = 1; i <= steps; i++)
        {
            double a = baselineAngle + delta * i / steps;
            Point next = new(p1.X + Math.Cos(a) * radius, p1.Y + Math.Sin(a) * radius);
            dc.DrawLine(guidePen, previous, next);
            previous = next;
        }

        double labelAngle = baselineAngle + delta * 0.52;
        Point labelAt = new(p1.X + Math.Cos(labelAngle) * 31 + 3, p1.Y + Math.Sin(labelAngle) * 31 - 7);
        DrawParityPlainLabel(dc, $"{displayAngle:0.##}°", labelAt, sourcePen.Brush, 10.5);
    }

    private void DrawParityPlainLabel(DrawingContext dc, string value, Point at, Brush brush, double size)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var style = new DrawingStyle { FontSize = size, TextColor = "#2962FF" };
        FormattedText text = CreateDrawingText(value, style, brush, size);
        dc.DrawText(text, at);
    }

    private void DrawParityRay(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush textBrush, bool both)
    {
        DrawRay(dc, layout.Plot, p1, p2, pen, both);
        if (drawing.Style.ArrowStart && both) DrawArrowHead(dc, pen, p2, p1);
        if (drawing.Style.ArrowEnd) DrawArrowHead(dc, pen, p1, p2);
        if (ParityFlag(drawing, "MiddlePoint", false)) DrawParityAnchorGlyph(dc, Mid(p1, p2), pen.Brush, 2.8, false);
        if (drawing.Style.ShowPriceLabels)
        {
            DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, p1, textBrush);
            DrawPriceLabel(dc, layout, drawing.Anchors[1].Price, p2, textBrush);
        }
        if (!string.IsNullOrWhiteSpace(drawing.Text))
            DrawParityLabel(dc, drawing.Text, Mid(p1, p2) + new Vector(6, -20), textBrush, drawing.Style, false);
    }

    private void DrawParityTimeLabel(DrawingContext dc, ChartLayout layout, long unix, double x, Brush textBrush)
    {
        string text = DateTimeOffset.FromUnixTimeSeconds(unix).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        DrawParityLabel(dc, text, new Point(Math.Clamp(x - 58, layout.Plot.Left, Math.Max(layout.Plot.Left, layout.Plot.Right - 116)), layout.Plot.Bottom + 5), textBrush, new DrawingStyle { FontSize = 10 }, true);
    }

    private static void DrawParityAnchorGlyph(DrawingContext dc, Point point, Brush brush, double radius, bool filled)
    {
        dc.DrawEllipse(filled ? brush : Brushes.Transparent, new Pen(brush, 1.2), point, radius, radius);
    }

    private void DrawParityLabel(DrawingContext dc, string value, Point at, Brush textBrush, DrawingStyle style, bool compact)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        FormattedText text = CreateDrawingText(value, style, textBrush, compact ? Math.Min(11, style.FontSize) : style.FontSize);
        var back = new SolidColorBrush(Color.FromArgb(210, 13, 19, 28));
        var border = new Pen(new SolidColorBrush(Color.FromArgb(180, 70, 86, 107)), 1);
        var box = new Rect(at.X - 4, at.Y - 2, text.Width + 8, text.Height + 4);
        dc.DrawRoundedRectangle(back, border, box, 3, 3);
        dc.DrawText(text, at);
    }

    private static void DrawParityParallelChannel(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        if (points.Length < 3) return;

        // #1 and #2 define level 0. Point #3 is a PRICE OFFSET from #2;
        // its horizontal coordinate is intentionally ignored.
        Point a = points[0];
        Point b = points[1];
        Vector offset = new(0, points[2].Y - b.Y);
        Point c = a + offset;
        Point d = b + offset;

        if (drawing.Style.FillOpacity > 0)
            DrawPolygon(dc, new[] { a, b, d, c }, null!, fill);

        IReadOnlyList<DrawingLevel> levels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        bool drewAny = false;
        foreach (DrawingLevel level in levels.Where(level => level.Enabled))
        {
            Point levelA = a + offset * level.Value;
            Point levelB = b + offset * level.Value;
            DrawLineWithOptions(dc, layout.Plot, levelA, levelB, ParityLevelPen(drawing, level), drawing.Style);
            drewAny = true;
        }

        // Old workspaces may not contain channel levels yet. Keep both rails
        // visible until the drawing has been upgraded by opening its settings.
        if (!drewAny)
        {
            DrawLineWithOptions(dc, layout.Plot, a, b, pen, drawing.Style);
            DrawLineWithOptions(dc, layout.Plot, c, d, pen, drawing.Style);
        }
    }

    private static void DrawParityFlatTopBottom(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        // First edge is free-angle; the opposite edge is forced horizontal.
        Point a = points[0];
        Point b = points[1];
        double flatY = points[2].Y;
        Point c = new(a.X, flatY);
        Point d = new(b.X, flatY);
        if (drawing.Style.FillOpacity > 0)
            DrawPolygon(dc, new[] { a, b, d, c }, null!, fill);

        IReadOnlyList<DrawingLevel> levels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        foreach (DrawingLevel level in levels.Where(level => level.Enabled))
        {
            Point levelA = a + (c - a) * level.Value;
            Point levelB = b + (d - b) * level.Value;
            DrawLineWithOptions(dc, layout.Plot, levelA, levelB, ParityLevelPen(drawing, level), drawing.Style);
        }
    }

    private static void DrawParityDisjointChannel(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        // TradingView Disjoint Channel is NOT a parallel-channel alias. The
        // first rail (#1 -> #2) defines the angle. Point #3 is vertically
        // constrained during placement (same X/time as #2) and starts a second
        // rail whose slope is the exact mirror of the first (+45° -> -45°).
        Point a = points[0];
        Point b = points[1];
        Vector first = b - a;
        Point c = new(b.X, points[2].Y);
        Vector opposite = new(first.X, -first.Y);
        Point d = c + opposite;

        if (drawing.Style.FillOpacity > 0)
        {
            // Split the bow-tie body into two triangles so WPF does not apply
            // an unpredictable self-intersecting polygon fill rule.
            DrawPolygon(dc, new[] { a, b, c }, null!, fill);
            DrawPolygon(dc, new[] { a, c, d }, null!, fill);
        }

        IReadOnlyList<DrawingLevel> levels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        foreach (DrawingLevel level in levels.Where(level => level.Enabled))
        {
            // 0 = first rail, 1 = mirrored rail. Intermediate levels connect
            // corresponding points while retaining the disjoint crossing shape.
            Point levelA = a + (c - a) * level.Value;
            Point levelB = b + (d - b) * level.Value;
            DrawLineWithOptions(dc, layout.Plot, levelA, levelB, ParityLevelPen(drawing, level), drawing.Style);
        }
    }

    private double ParitySource(Candle candle, int source) => source switch
    {
        0 => candle.Open,
        1 => candle.High,
        2 => candle.Low,
        4 => (candle.High + candle.Low) / 2.0,
        5 => (candle.High + candle.Low + candle.Close) / 3.0,
        6 => (candle.Open + candle.High + candle.Low + candle.Close) / 4.0,
        _ => candle.Close
    };

    private bool TryGetParityRegressionGeometry(
        ChartDrawing drawing, ChartLayout layout,
        out Point p1, out Point p2, out Point u1, out Point u2, out Point l1, out Point l2, out double pearsonsR)
    {
        p1 = p2 = u1 = u2 = l1 = l2 = default;
        pearsonsR = 0;
        if (DrawingCandles.Count == 0 || drawing.Anchors.Count < 2) return false;

        int a = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int b = FindNearestDrawingCandleIndex(drawing.Anchors[1]);
        int start = Math.Clamp(Math.Min(a, b), 0, DrawingCandles.Count - 1);
        int end = Math.Clamp(Math.Max(a, b), 0, DrawingCandles.Count - 1);
        if (end - start < 1) return false;

        int source = (int)Math.Clamp(ParityOption(drawing, "Source", 3), 0, 6);
        int n = end - start + 1;
        double sx = n * (n - 1) / 2.0;
        double sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            double y = ParitySource(DrawingCandles[start + i], source);
            sy += y;
            sxx += i * i;
            sxy += i * y;
        }

        double denominator = n * sxx - sx * sx;
        double slope = Math.Abs(denominator) < 1e-12 ? 0 : (n * sxy - sx * sy) / denominator;
        double intercept = (sy - slope * sx) / n;
        double residualSquares = 0;
        double mean = sy / n;
        double sst = 0;
        for (int i = 0; i < n; i++)
        {
            double y = ParitySource(DrawingCandles[start + i], source);
            double residual = y - (intercept + slope * i);
            residualSquares += residual * residual;
            double delta = y - mean;
            sst += delta * delta;
        }

        double std = Math.Sqrt(residualSquares / Math.Max(1, n));
        pearsonsR = sst <= 1e-16 ? 0 : Math.Sqrt(Math.Max(0, 1 - residualSquares / sst)) * Math.Sign(slope);
        double upperDev = Math.Max(0, ParityOption(drawing, "UpperDeviation", 2));
        double lowerDev = Math.Max(0, ParityOption(drawing, "LowerDeviation", 2));
        DrawingAnchor first = CreateDrawingAnchorAtIndex(start, intercept);
        DrawingAnchor last = CreateDrawingAnchorAtIndex(end, intercept + slope * (n - 1));
        p1 = AnchorToPoint(first, layout);
        p2 = AnchorToPoint(last, layout);
        u1 = AnchorToPoint(first with { Price = first.Price + std * upperDev }, layout);
        u2 = AnchorToPoint(last with { Price = last.Price + std * upperDev }, layout);
        l1 = AnchorToPoint(first with { Price = first.Price - std * lowerDev }, layout);
        l2 = AnchorToPoint(last with { Price = last.Price - std * lowerDev }, layout);
        return true;
    }

    private void DrawParityRegression(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen, Brush fill, Brush textBrush)
    {
        if (!TryGetParityRegressionGeometry(drawing, layout, out Point p1, out Point p2, out Point u1, out Point u2, out Point l1, out Point l2, out double r))
            return;

        DrawingLevel? baseLevel = ParityRoleLevel(drawing, "Base", 0);
        DrawingLevel? upperLevel = ParityRoleLevel(drawing, "Upper", 1);
        DrawingLevel? lowerLevel = ParityRoleLevel(drawing, "Lower", 2);
        bool showBase = ParityFlag(drawing, "ShowBase", true) && (baseLevel?.Enabled ?? true);
        bool showUpper = ParityFlag(drawing, "ShowUpper", true) && (upperLevel?.Enabled ?? true);
        bool showLower = ParityFlag(drawing, "ShowLower", true) && (lowerLevel?.Enabled ?? true);
        Pen basePen = baseLevel is null ? pen : ParityLevelPen(drawing, baseLevel);
        Pen upperPen = upperLevel is null ? pen : ParityLevelPen(drawing, upperLevel);
        Pen lowerPen = lowerLevel is null ? pen : ParityLevelPen(drawing, lowerLevel);

        if (drawing.Style.FillOpacity > 0)
        {
            if (showUpper)
            {
                Brush upperFill = CreateDrawingBrush(
                    string.IsNullOrWhiteSpace(upperLevel?.FillColor) ? "#2962FF" : upperLevel!.FillColor,
                    upperLevel is not null && upperLevel.FillOpacity >= 0 ? upperLevel.FillOpacity : drawing.Style.FillOpacity);
                var upperArea = new StreamGeometry();
                using (StreamGeometryContext ctx = upperArea.Open())
                {
                    ctx.BeginFigure(u1, true, true);
                    ctx.LineTo(u2, true, false);
                    ctx.LineTo(p2, true, false);
                    ctx.LineTo(p1, true, false);
                }
                dc.DrawGeometry(upperFill, null, upperArea);
            }
            if (showLower)
            {
                Brush lowerFill = CreateDrawingBrush(
                    string.IsNullOrWhiteSpace(lowerLevel?.FillColor) ? "#F23645" : lowerLevel!.FillColor,
                    lowerLevel is not null && lowerLevel.FillOpacity >= 0 ? lowerLevel.FillOpacity : drawing.Style.FillOpacity);
                var lowerArea = new StreamGeometry();
                using (StreamGeometryContext ctx = lowerArea.Open())
                {
                    ctx.BeginFigure(p1, true, true);
                    ctx.LineTo(p2, true, false);
                    ctx.LineTo(l2, true, false);
                    ctx.LineTo(l1, true, false);
                }
                dc.DrawGeometry(lowerFill, null, lowerArea);
            }
        }
        if (showBase) dc.DrawLine(basePen, p1, p2);
        if (showUpper) dc.DrawLine(upperPen, u1, u2);
        if (showLower) dc.DrawLine(lowerPen, l1, l2);

        if (ParityFlag(drawing, "ExtendRight", false) || drawing.Style.ExtendRight)
        {
            if (showBase) DrawRay(dc, layout.Plot, p1, p2, basePen, false);
            if (showUpper) DrawRay(dc, layout.Plot, u1, u2, upperPen, false);
            if (showLower) DrawRay(dc, layout.Plot, l1, l2, lowerPen, false);
        }
        if (ParityFlag(drawing, "PearsonsR", true))
            DrawParityLabel(dc, $"Pearson's R  {r:0.###}", Mid(p1, p2) + new Vector(7, -22), textBrush, drawing.Style, true);
    }

    private sealed record AnchoredVwapVisual(
        IReadOnlyList<Point> Main,
        IReadOnlyList<(IReadOnlyList<Point> Up, IReadOnlyList<Point> Down, DrawingLevel Level)> Bands);

    private AnchoredVwapVisual? BuildParityAnchoredVwapVisual(ChartLayout layout, ChartDrawing drawing)
    {
        if (DrawingCandles.Count == 0 || drawing.Anchors.Count == 0)
            return null;

        int start = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[0]), 0, DrawingCandles.Count - 1);
        int end = DrawingCandles.Count - 1;
        int source = (int)Math.Clamp(ParityOption(drawing, "Source", 5), 0, 6);
        DrawingLevel[] bandLevels = (drawing.Levels.Count > 0
                ? drawing.Levels
                : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Label.StartsWith("Band", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        double[] multipliers =
        {
            ParityOption(drawing, "Band1Multiplier", 1),
            ParityOption(drawing, "Band2Multiplier", 2),
            ParityOption(drawing, "Band3Multiplier", 3)
        };
        bool[] optionEnabled =
        {
            ParityFlag(drawing, "Band1", true),
            ParityFlag(drawing, "Band2", false),
            ParityFlag(drawing, "Band3", false)
        };

        var main = new List<Point>(Math.Max(2, end - start + 1));
        var enabledBands = new List<(List<Point> Up, List<Point> Down, DrawingLevel Level, double Mult)>();
        for (int i = 0; i < 3; i++)
        {
            DrawingLevel level = i < bandLevels.Length
                ? bandLevels[i]
                : new DrawingLevel(i + 1, $"Band #{i + 1}", i == 0, i == 0 ? "#6A9F58" : drawing.Style.LineColor);
            // Once a drawing has explicit band levels, the Style checkbox is
            // authoritative so a user can truly turn a band off. Numeric Band#
            // flags remain only as compatibility defaults for older drawings.
            bool enabled = drawing.Levels.Count > 0 ? level.Enabled : optionEnabled[i];
            if (!enabled)
                continue;
            double mult = multipliers[i];
            if (Math.Abs(mult - (i + 1)) < 1e-12 && Math.Abs(level.Value) > 1e-12)
                mult = Math.Abs(level.Value);
            enabledBands.Add((new List<Point>(), new List<Point>(), level, Math.Max(0.0001, Math.Abs(mult))));
        }

        double cumulativeVolume = 0;
        double cumulativeValue = 0;
        double cumulativeSquared = 0;
        for (int i = start; i <= end; i++)
        {
            Candle candle = DrawingCandles[i];
            double price = ParitySource(candle, source);
            double volume = Math.Max(1, candle.RealVolume > 0 ? candle.RealVolume : candle.TickVolume);
            cumulativeVolume += volume;
            cumulativeValue += price * volume;
            cumulativeSquared += price * price * volume;
            double vwap = cumulativeValue / cumulativeVolume;
            double variance = Math.Max(0, cumulativeSquared / cumulativeVolume - vwap * vwap);
            double std = Math.Sqrt(variance);
            main.Add(AnchorToPoint(new DrawingAnchor(candle.StartUnix, vwap), layout));
            foreach (var band in enabledBands)
            {
                band.Up.Add(AnchorToPoint(new DrawingAnchor(candle.StartUnix, vwap + std * band.Mult), layout));
                band.Down.Add(AnchorToPoint(new DrawingAnchor(candle.StartUnix, vwap - std * band.Mult), layout));
            }
        }

        return new AnchoredVwapVisual(
            main,
            enabledBands.Select(item => ((IReadOnlyList<Point>)item.Up, (IReadOnlyList<Point>)item.Down, item.Level)).ToArray());
    }

    private static StreamGeometry CreateParityPolyline(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0)
            return geometry;
        using StreamGeometryContext ctx = geometry.Open();
        ctx.BeginFigure(points[0], false, false);
        for (int i = 1; i < points.Count; i++)
            ctx.LineTo(points[i], true, false);
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private void DrawParityAnchoredVwap(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen)
    {
        AnchoredVwapVisual? visual = BuildParityAnchoredVwapVisual(layout, drawing);
        if (visual is null || visual.Main.Count == 0)
            return;

        dc.PushClip(new RectangleGeometry(layout.Plot));
        dc.DrawGeometry(null, pen, CreateParityPolyline(visual.Main));
        foreach (var band in visual.Bands)
        {
            double alpha = band.Level.FillOpacity >= 0 ? band.Level.FillOpacity : 0.92;
            var bandPen = new Pen(
                CreateDrawingBrush(band.Level.Color, drawing.Style.Opacity * Math.Clamp(alpha, 0, 1)),
                Math.Clamp(band.Level.Width, 0.5, 20));
            bandPen.DashStyle = band.Level.LineStyle switch
            {
                DrawingLineStyle.Dashed => DashStyles.Dash,
                DrawingLineStyle.Dotted => DashStyles.Dot,
                _ => DashStyles.Solid
            };
            dc.DrawGeometry(null, bandPen, CreateParityPolyline(band.Up));
            dc.DrawGeometry(null, bandPen, CreateParityPolyline(band.Down));
        }
        dc.Pop();
    }

    private void DrawParityFibRetracement(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen basePen)
    {
        double startPrice = drawing.Anchors[0].Price;
        double endPrice = drawing.Anchors[1].Price;
        if (ParityFlag(drawing, "Reverse", false)) (startPrice, endPrice) = (endPrice, startPrice);
        double left = Math.Min(points[0].X, points[1].X);
        double right = Math.Max(points[0].X, points[1].X);
        if (drawing.Style.ExtendLeft || ParityFlag(drawing, "ExtendLeft", false)) left = layout.Plot.Left;
        if (drawing.Style.ExtendRight || ParityFlag(drawing, "ExtendRight", false)) right = layout.Plot.Right;

        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled).ToArray();
        var rendered = levels
            .Select(level => (Level: level, Price: startPrice + (endPrice - startPrice) * level.Value))
            .Select(item => (item.Level, item.Price, Y: PriceToY(item.Price, layout)))
            .OrderBy(item => item.Y).ToArray();

        double bandOpacity = ParityBandOpacity(drawing);
        if (bandOpacity > 0.0001)
        {
            for (int i = 0; i + 1 < rendered.Length; i++)
            {
                var top = rendered[i];
                var bottom = rendered[i + 1];
                dc.DrawRectangle(CreateDrawingBrush(top.Level.Color, bandOpacity), null,
                    new Rect(new Point(left, top.Y), new Point(right, bottom.Y)));
            }
        }

        foreach (var item in rendered)
        {
            Pen lp = ParityFlag(drawing, "UseOneColor", false) ? basePen : ParityLevelPen(drawing, item.Level);
            double y = CrispStroke(item.Y, lp.Thickness);
            dc.DrawLine(lp, new Point(left, y), new Point(right, y));
            string label = ParityReadingText(drawing, item.Level, item.Price);
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, ParityHorizontalReadingPoint(drawing, left, right, y), lp.Brush);
        }

        dc.DrawLine(basePen, points[0], points[1]);
    }


    private void DrawParityFibChannel(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        Vector offset = points[2] - points[0];
        bool reverse = ParityFlag(drawing, "Reverse", false);
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled).OrderBy(level => level.Value).ToArray();

        double bandOpacity = ParityBandOpacity(drawing);
        if (bandOpacity > 0.0001)
        {
            for (int i = 0; i + 1 < levels.Length; i++)
            {
                double r1 = reverse ? -levels[i].Value : levels[i].Value;
                double r2 = reverse ? -levels[i + 1].Value : levels[i + 1].Value;
                Point a1 = points[0] + offset * r1;
                Point b1 = points[1] + offset * r1;
                Point a2 = points[0] + offset * r2;
                Point b2 = points[1] + offset * r2;
                DrawPolygon(dc, new[] { a1, b1, b2, a2 }, null!, CreateDrawingBrush(levels[i].Color, bandOpacity));
            }
        }

        foreach (DrawingLevel level in levels)
        {
            double ratio = reverse ? -level.Value : level.Value;
            Point a = points[0] + offset * ratio;
            Point b = points[1] + offset * ratio;
            Pen lp = ParityFlag(drawing, "UseOneColor", false) ? pen : ParityLevelPen(drawing, level);
            DrawingStyle lineStyle = drawing.Style with
            {
                ExtendLeft = drawing.Style.ExtendLeft || ParityFlag(drawing, "ExtendLeft", false),
                ExtendRight = drawing.Style.ExtendRight || ParityFlag(drawing, "ExtendRight", false)
            };
            if (lineStyle.ExtendLeft || lineStyle.ExtendRight) DrawLineWithOptions(dc, layout.Plot, a, b, lp, lineStyle);
            else dc.DrawLine(lp, a, b);
            string label = ParityReadingText(drawing, level, YToPrice(a.Y, layout));
            if (!string.IsNullOrWhiteSpace(label))
            {
                Point labelPoint = ParityFlag(drawing, "LabelsOutside", false)
                    ? a + new Vector(-105, -8)
                    : a + new Vector(4, -14);
                DrawSmallLabel(dc, label, labelPoint, lp.Brush);
            }
        }
        dc.DrawLine(pen, points[0], points[1]);
        dc.DrawLine(pen, points[2], points[1] + offset);
    }


    private void DrawParityFibTimeZone(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        double span = points[1].X - points[0].X;
        if (ParityFlag(drawing, "Reverse", false)) span = -span;
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value >= 0)
            .OrderBy(level => points[0].X + span * level.Value).ToArray();
        var positioned = levels.Select(level => (Level: level, X: points[0].X + span * level.Value))
            .Where(item => item.X >= layout.Plot.Left - 2 && item.X <= layout.Plot.Right + 2).ToArray();
        double bandOpacity = ParityBandOpacity(drawing);
        for (int i = 0; i + 1 < positioned.Length && bandOpacity > 0.0001; i++)
        {
            double x1 = positioned[i].X;
            double x2 = positioned[i + 1].X;
            dc.DrawRectangle(CreateDrawingBrush(positioned[i].Level.Color, bandOpacity), null,
                new Rect(new Point(Math.Min(x1, x2), layout.Plot.Top), new Point(Math.Max(x1, x2), layout.Plot.Bottom)));
        }
        foreach (var item in positioned)
        {
            Pen lp = ParityLevelPen(drawing, item.Level);
            double x = CrispStroke(item.X, lp.Thickness);
            dc.DrawLine(lp, new Point(x, layout.Plot.Top), new Point(x, layout.Plot.Bottom));
            string label = ParityReadingText(drawing, item.Level);
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, new Point(x + 4, layout.Plot.Top + 4), lp.Brush);
        }
    }


    private void DrawParityFibSpiral(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point center, Point end, Pen pen)
    {
        double baseRadius = Math.Max(1, Distance(center, end));
        double startAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        bool reverse = ParityFlag(drawing, "Reverse", false);
        double sign = reverse ? -1 : 1;
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value > 0).OrderBy(level => level.Value).ToArray();
        if (levels.Length == 0)
            levels = new[] { new DrawingLevel(1, "1", true, drawing.Style.LineColor, drawing.Style.LineWidth) };

        const int steps = 260;
        const double turns = 4.25;
        const double phi = 1.618033988749895;
        double growth = Math.Log(phi) / (Math.PI / 2.0);
        double maxRaw = Math.Exp(growth * turns * Math.PI * 2.0);
        foreach (DrawingLevel level in levels)
        {
            var geometry = new StreamGeometry();
            Point last = center;
            using (StreamGeometryContext ctx = geometry.Open())
            {
                for (int i = 0; i <= steps; i++)
                {
                    double t = i / (double)steps;
                    double theta = sign * t * turns * Math.PI * 2.0;
                    double radius = baseRadius * level.Value * Math.Exp(growth * Math.Abs(theta)) / maxRaw;
                    double angle = startAngle + theta;
                    Point current = new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                    if (i == 0) ctx.BeginFigure(current, false, false); else ctx.LineTo(current, true, false);
                    last = current;
                }
            }
            if (geometry.CanFreeze) geometry.Freeze();
            Pen lp = ParityLevelPen(drawing, level);
            double bandOpacity = ParityBandOpacity(drawing);
            if (bandOpacity > 0.0001)
            {
                var ribbon = new Pen(CreateDrawingBrush(level.Color, bandOpacity), Math.Max(lp.Thickness + 2, 5));
                if (ribbon.CanFreeze) ribbon.Freeze();
                dc.DrawGeometry(null, ribbon, geometry);
            }
            dc.DrawGeometry(null, lp, geometry);
            Point labelPoint = last + new Vector(4, -8);
            string label = ParityReadingText(drawing, level, YToPrice(last.Y, layout));
            if (!string.IsNullOrWhiteSpace(label)) DrawSmallLabel(dc, label, labelPoint, lp.Brush);
        }
    }


    private void DrawParityFibExtension(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen basePen)
    {
        // Three-point TradingView construction: A->B defines the measured move;
        // C is the extension origin. The old renderer could collapse C's level
        // segment to zero width, so levels sometimes disappeared completely.
        double measuredMove = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
        if (ParityFlag(drawing, "Reverse", false)) measuredMove = -measuredMove;
        double originPrice = drawing.Anchors[2].Price;
        double direction = Math.Sign(points[2].X - points[1].X);
        if (Math.Abs(direction) < 0.5) direction = 1;
        double naturalWidth = Math.Max(90, Math.Abs(points[1].X - points[0].X));
        double x1 = points[2].X;
        double x2 = points[2].X + direction * naturalWidth;
        double left = Math.Min(x1, x2);
        double right = Math.Max(x1, x2);
        if (drawing.Style.ExtendLeft || ParityFlag(drawing, "ExtendLeft", false)) left = layout.Plot.Left;
        if (drawing.Style.ExtendRight || ParityFlag(drawing, "ExtendRight", false)) right = layout.Plot.Right;

        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled).ToArray();
        var rendered = levels
            .Select(level => (Level: level, Price: originPrice + measuredMove * level.Value))
            .Select(item => (item.Level, item.Price, Y: PriceToY(item.Price, layout)))
            .OrderBy(item => item.Y).ToArray();

        double bandOpacity = ParityBandOpacity(drawing);
        for (int i = 0; i + 1 < rendered.Length && bandOpacity > 0.0001; i++)
            dc.DrawRectangle(CreateDrawingBrush(rendered[i].Level.Color, bandOpacity), null,
                new Rect(new Point(left, rendered[i].Y), new Point(right, rendered[i + 1].Y)));

        foreach (var item in rendered)
        {
            Pen lp = ParityFlag(drawing, "UseOneColor", false) ? basePen : ParityLevelPen(drawing, item.Level);
            double y = CrispStroke(item.Y, lp.Thickness);
            dc.DrawLine(lp, new Point(left, y), new Point(right, y));
            string label = ParityReadingText(drawing, item.Level, item.Price);
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, ParityHorizontalReadingPoint(drawing, left, right, y), lp.Brush);
        }
        dc.DrawLine(basePen, points[0], points[1]);
        dc.DrawLine(basePen, points[1], points[2]);
    }


    private void DrawParityTrendFibTime(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        double span = points[1].X - points[0].X;
        if (ParityFlag(drawing, "Reverse", false)) span = -span;
        double origin = points[2].X;
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value >= 0)
            .OrderBy(level => origin + span * level.Value).ToArray();
        var positioned = levels.Select(level => (Level: level, X: origin + span * level.Value))
            .Where(item => item.X >= layout.Plot.Left - 2 && item.X <= layout.Plot.Right + 2).ToArray();
        double bandOpacity = ParityBandOpacity(drawing);
        for (int i = 0; i + 1 < positioned.Length && bandOpacity > 0.0001; i++)
            dc.DrawRectangle(CreateDrawingBrush(positioned[i].Level.Color, bandOpacity), null,
                new Rect(new Point(Math.Min(positioned[i].X, positioned[i + 1].X), layout.Plot.Top),
                    new Point(Math.Max(positioned[i].X, positioned[i + 1].X), layout.Plot.Bottom)));
        foreach (var item in positioned)
        {
            Pen lp = ParityLevelPen(drawing, item.Level);
            double x = CrispStroke(item.X, lp.Thickness);
            dc.DrawLine(lp, new Point(x, layout.Plot.Top), new Point(x, layout.Plot.Bottom));
            string label = ParityReadingText(drawing, item.Level);
            if (!string.IsNullOrWhiteSpace(label)) DrawSmallLabel(dc, label, new Point(x + 4, layout.Plot.Top + 4), lp.Brush);
        }
        dc.DrawLine(pen, points[0], points[1]);
        dc.DrawLine(pen, points[1], points[2]);
    }


    private void DrawParityFibCircles(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Vector radius = points[1] - points[0];
        double rx = Math.Max(1, Math.Abs(radius.X));
        double ry = Math.Max(1, Math.Abs(radius.Y));
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value > 0).OrderBy(level => level.Value).ToArray();
        double bandOpacity = ParityBandOpacity(drawing);
        // Paint outer-to-inner so each annular space inherits its enclosing
        // (visually above) level colour rather than one common background.
        if (bandOpacity > 0.0001)
        {
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                DrawingLevel level = levels[i];
                dc.DrawEllipse(CreateDrawingBrush(level.Color, bandOpacity), null, points[0], rx * level.Value, ry * level.Value);
            }
        }
        foreach (DrawingLevel level in levels)
        {
            Pen lp = ParityLevelPen(drawing, level);
            double ex = rx * level.Value;
            double ey = ry * level.Value;
            dc.DrawEllipse(null, lp, points[0], ex, ey);
            Point labelPoint = new(points[0].X + ex + 4, points[0].Y - 8);
            string label = ParityReadingText(drawing, level, YToPrice(points[0].Y - ey, layout));
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, labelPoint, lp.Brush);
        }
    }


    private void DrawParityFibArcs(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        if (points.Length < 2) return;
        Point center = points[0];
        Vector direction = points[1] - center;
        double radius = direction.Length;
        if (radius < 0.5) return;
        direction.Normalize();
        Vector perpendicular = new(-direction.Y, direction.X);
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value > 0).OrderBy(level => level.Value).ToArray();
        double previousRadius = 0;
        double bandOpacity = ParityBandOpacity(drawing);
        foreach (DrawingLevel level in levels)
        {
            double r = Math.Max(1, radius * level.Value);
            Point start = center - perpendicular * r;
            Point end = center + perpendicular * r;
            Pen lp = ParityLevelPen(drawing, level);
            if (bandOpacity > 0.0001)
            {
                StreamGeometry band = CreateSemiAnnulusGeometry(center, perpendicular, previousRadius, r, SweepDirection.Clockwise);
                dc.DrawGeometry(CreateDrawingBrush(level.Color, bandOpacity), null, band);
            }
            StreamGeometry arc = CreateScreenArcGeometry(start, end, r, SweepDirection.Clockwise);
            dc.DrawGeometry(null, lp, arc);
            Point labelPoint = end + new Vector(4, -8);
            string label = ParityReadingText(drawing, level, YToPrice(end.Y, layout));
            if (!string.IsNullOrWhiteSpace(label)) DrawSmallLabel(dc, label, labelPoint, lp.Brush);
            previousRadius = r;
        }
    }


    private void DrawParityFibFan(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, bool gann)
    {
        Rect plot = layout.Plot;
        if (gann)
        {
            DrawParityGannFan(dc, layout, drawing, points, pen);
            return;
        }
        Point origin = points[0];
        Vector vector = points[1] - points[0];
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && Math.Abs(level.Value) > 1e-12).OrderBy(level => level.Value).ToArray();
        if (levels.Length == 0) return;
        var targets = levels.Select(level => (Level: level, Target: new Point(points[1].X, points[0].Y + vector.Y * level.Value))).ToArray();
        double bandOpacity = ParityBandOpacity(drawing);
        dc.PushClip(new RectangleGeometry(plot));
        for (int i = 0; i + 1 < targets.Length && bandOpacity > 0.0001; i++)
        {
            Point a = ExtendRayPoint(origin, targets[i].Target, plot);
            Point b = ExtendRayPoint(origin, targets[i + 1].Target, plot);
            var zone = new StreamGeometry();
            using (StreamGeometryContext ctx = zone.Open())
            {
                ctx.BeginFigure(origin, true, true); ctx.LineTo(a, true, false); ctx.LineTo(b, true, false);
            }
            if (zone.CanFreeze) zone.Freeze();
            dc.DrawGeometry(CreateDrawingBrush(targets[i].Level.Color, bandOpacity), null, zone);
        }
        foreach (var item in targets)
        {
            Pen lp = ParityLevelPen(drawing, item.Level);
            DrawRay(dc, plot, origin, item.Target, lp, false);
            Point labelPoint = origin + (item.Target - origin) * 0.74 + new Vector(4, -8);
            string label = ParityReadingText(drawing, item.Level, YToPrice(labelPoint.Y, layout));
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, labelPoint, lp.Brush);
        }
        dc.Pop();
    }

    private void DrawParityGannFan(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Rect plot = layout.Plot;
        Point origin = points[0];
        bool reverse = ParityFlag(drawing, "Reverse", false);
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && Math.Abs(level.Value) > 1e-12).ToArray();
        var rays = levels.Select(level => (Level: level, Target: GannFanTarget(origin, points[1], level.Value, reverse)))
            .OrderBy(item => Math.Atan2(item.Target.Y - origin.Y, item.Target.X - origin.X)).ToArray();
        var readingLabels = new List<(string Text, Point Point)>();
        double bandOpacity = ParityBandOpacity(drawing);
        dc.PushClip(new RectangleGeometry(plot));
        for (int i = 0; i + 1 < rays.Length && bandOpacity > 0.0001; i++)
        {
            Point a = ExtendRayPoint(origin, rays[i].Target, plot);
            Point b = ExtendRayPoint(origin, rays[i + 1].Target, plot);
            var zone = new StreamGeometry();
            using (StreamGeometryContext ctx = zone.Open())
            {
                ctx.BeginFigure(origin, true, true); ctx.LineTo(a, true, false); ctx.LineTo(b, true, false);
            }
            if (zone.CanFreeze) zone.Freeze();
            dc.DrawGeometry(CreateDrawingBrush(rays[i].Level.Color, bandOpacity), null, zone);
        }
        foreach (var item in rays)
        {
            Pen lp = ParityFlag(drawing, "UseOneColor", false) ? pen : ParityLevelPen(drawing, item.Level);
            Point edge = ExtendRayPoint(origin, item.Target, plot);
            dc.DrawLine(lp, origin, edge);

            // IMPORTANT: ExtendRayPoint intentionally produces a point far beyond
            // the visible plot so the clipped ray reaches every chart edge.  Using
            // that far-away point for label placement pushed the Gann ratio text
            // completely off-screen.  Find the actual visible ray/plot intersection
            // and place the reading along the visible portion of the ray instead.
            Point visibleEdge = RayPlotEdge(origin, item.Target, plot);
            Vector visibleRay = visibleEdge - origin;
            Point labelPoint = origin + visibleRay * 0.66;
            Vector normal = new(-visibleRay.Y, visibleRay.X);
            if (normal.LengthSquared > 0.0001)
            {
                normal.Normalize();
                labelPoint += normal * 7;
            }
            labelPoint = new Point(
                Math.Clamp(labelPoint.X, plot.Left + 5, plot.Right - 78),
                Math.Clamp(labelPoint.Y, plot.Top + 8, plot.Bottom - 18));

            string label = GannFanReadingText(drawing, item.Level, YToPrice(labelPoint.Y, layout));
            if (!string.IsNullOrWhiteSpace(label))
                readingLabels.Add((label, labelPoint));
        }
        dc.Pop();

        // Keep the ratios readable regardless of ray/fill colour or chart theme.
        // Rendering after the fan clip also prevents a clipped text run from
        // disappearing when a label sits close to a plot boundary.
        if (readingLabels.Count > 0)
        {
            Brush readingBrush = GannFanReadingBrush();
            foreach ((string text, Point point) in readingLabels)
                DrawGannFanReading(dc, text, point, readingBrush);
        }
    }

    private Brush GannFanReadingBrush()
    {
        Color background = Color.FromRgb(8, 8, 8);
        try
        {
            if (ColorConverter.ConvertFromString(Settings.ChartBackgroundColor) is Color parsed)
                background = parsed;
        }
        catch
        {
            // Fall back to TickLab's dark chart default.
        }

        double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        Color text = luminance >= 0.58 ? Color.FromRgb(31, 41, 55) : Color.FromRgb(241, 245, 249);
        var brush = new SolidColorBrush(text);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static void DrawGannFanReading(DrawingContext dc, string text, Point point, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            11, brush, 1.0);
        dc.DrawText(formatted, new Point(point.X + 2, point.Y - formatted.Height / 2));
    }


    private void DrawParityPitchfan(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Rect plot = layout.Plot;
        Point origin = points[0];
        Vector span = points[2] - points[1];
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled).OrderBy(level => level.Value).ToArray();
        var rays = levels.Select(level => (Level: level, Target: points[1] + span * level.Value)).ToArray();
        double bandOpacity = ParityBandOpacity(drawing);
        dc.PushClip(new RectangleGeometry(plot));
        for (int i = 0; i + 1 < rays.Length && bandOpacity > 0.0001; i++)
        {
            Point a = ExtendRayPoint(origin, rays[i].Target, plot);
            Point b = ExtendRayPoint(origin, rays[i + 1].Target, plot);
            var zone = new StreamGeometry();
            using (StreamGeometryContext ctx = zone.Open())
            { ctx.BeginFigure(origin, true, true); ctx.LineTo(a, true, false); ctx.LineTo(b, true, false); }
            if (zone.CanFreeze) zone.Freeze();
            dc.DrawGeometry(CreateDrawingBrush(rays[i].Level.Color, bandOpacity), null, zone);
        }
        foreach (var item in rays)
        {
            Pen lp = ParityLevelPen(drawing, item.Level);
            Point edge = ExtendRayPoint(origin, item.Target, plot);
            dc.DrawLine(lp, origin, edge);
            Point labelPoint = origin + (edge-origin)*0.75 + new Vector(4,-8);
            string label = ParityReadingText(drawing, item.Level, YToPrice(labelPoint.Y, layout));
            if (!string.IsNullOrWhiteSpace(label)) DrawSmallLabel(dc, label, labelPoint, lp.Brush);
        }
        dc.Pop();
    }


    private void DrawParityFibWedge(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        if (points.Length < 3) return;
        Point center = points[0];
        Vector a = points[1] - center;
        Vector b = points[2] - center;
        double baseRadius = Math.Max(a.Length, b.Length);
        if (baseRadius < 0.5) return;
        Vector ua = a; ua.Normalize();
        Vector ub = b; ub.Normalize();
        double cross = ua.X * ub.Y - ua.Y * ub.X;
        SweepDirection sweep = cross >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        double dot = Math.Clamp(Vector.Multiply(ua, ub), -1.0, 1.0);
        bool largeArc = Math.Acos(dot) > Math.PI;
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value > 0).OrderBy(level => level.Value).ToArray();
        double previousRadius = 0;
        double bandOpacity = ParityBandOpacity(drawing);
        Vector middle = ua + ub;
        if (middle.LengthSquared < 0.0001) middle = ua;
        middle.Normalize();
        foreach (DrawingLevel level in levels)
        {
            double radius = Math.Max(1, baseRadius * level.Value);
            if (bandOpacity > 0.0001)
            {
                StreamGeometry band = CreateSectorBandGeometry(center, ua, ub, previousRadius, radius, sweep, largeArc);
                dc.DrawGeometry(CreateDrawingBrush(level.Color, bandOpacity), null, band);
            }
            Point start = center + ua * radius;
            Point end = center + ub * radius;
            dc.DrawGeometry(null, ParityLevelPen(drawing, level), CreateScreenArcGeometry(start, end, radius, sweep, largeArc));
            Point labelPoint = center + middle * radius + new Vector(4, -8);
            string label = ParityReadingText(drawing, level, YToPrice(labelPoint.Y, layout));
            if (!string.IsNullOrWhiteSpace(label))
                DrawSmallLabel(dc, label, labelPoint, CreateDrawingBrush(level.Color, drawing.Style.Opacity));
            previousRadius = radius;
        }
        double extent = Math.Max(baseRadius, previousRadius);
        dc.DrawLine(pen, center, center + ua * extent);
        dc.DrawLine(pen, center, center + ub * extent);
    }


    private static StreamGeometry CreateScreenArcGeometry(Point start, Point end, double radius, SweepDirection sweep, bool largeArc = false)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, largeArc, sweep, true, false);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateSemiAnnulusGeometry(Point center, Vector perpendicular, double innerRadius, double outerRadius, SweepDirection sweep)
    {
        Point outerStart = center - perpendicular * outerRadius;
        Point outerEnd = center + perpendicular * outerRadius;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, true, true);
            ctx.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, false, sweep, true, false);
            if (innerRadius <= 0.5)
            {
                ctx.LineTo(center, true, false);
            }
            else
            {
                Point innerEnd = center + perpendicular * innerRadius;
                Point innerStart = center - perpendicular * innerRadius;
                ctx.LineTo(innerEnd, true, false);
                ctx.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, false,
                    sweep == SweepDirection.Clockwise ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, false);
            }
        }
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateSectorBandGeometry(Point center, Vector first, Vector second, double innerRadius, double outerRadius, SweepDirection sweep, bool largeArc)
    {
        Point outerStart = center + first * outerRadius;
        Point outerEnd = center + second * outerRadius;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, true, true);
            ctx.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, largeArc, sweep, true, false);
            if (innerRadius <= 0.5)
            {
                ctx.LineTo(center, true, false);
            }
            else
            {
                Point innerEnd = center + second * innerRadius;
                Point innerStart = center + first * innerRadius;
                ctx.LineTo(innerEnd, true, false);
                ctx.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, largeArc,
                    sweep == SweepDirection.Clockwise ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, false);
            }
        }
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }


    private static Point RayPlotEdge(Point origin, Point through, Rect plot)
    {
        Vector direction = through - origin;
        if (direction.LengthSquared < 0.0001)
            return through;

        double best = double.PositiveInfinity;
        void Consider(double t)
        {
            if (t <= 0 || double.IsNaN(t) || double.IsInfinity(t) || t >= best)
                return;
            Point candidate = origin + direction * t;
            const double epsilon = 0.75;
            if (candidate.X >= plot.Left - epsilon && candidate.X <= plot.Right + epsilon &&
                candidate.Y >= plot.Top - epsilon && candidate.Y <= plot.Bottom + epsilon)
                best = t;
        }

        if (Math.Abs(direction.X) > 1e-9)
        {
            Consider((plot.Left - origin.X) / direction.X);
            Consider((plot.Right - origin.X) / direction.X);
        }
        if (Math.Abs(direction.Y) > 1e-9)
        {
            Consider((plot.Top - origin.Y) / direction.Y);
            Consider((plot.Bottom - origin.Y) / direction.Y);
        }

        if (!double.IsFinite(best))
            return through;

        Point result = origin + direction * best;
        return new Point(
            Math.Clamp(result.X, plot.Left, plot.Right),
            Math.Clamp(result.Y, plot.Top, plot.Bottom));
    }

    private static Point ExtendRayPoint(Point origin, Point through, Rect plot)
    {
        Vector direction = through - origin;
        if (direction.LengthSquared < 0.0001) return through;
        direction.Normalize();
        double extent = Math.Max(plot.Width, plot.Height) * 4;
        return origin + direction * extent;
    }

    private static void DrawParityPitchfork(DrawingContext dc, Rect plot, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        if (points.Length < 3) return;
        Point a = points[0], b = points[1], c = points[2];
        Point bcMid = Mid(b, c);

        IReadOnlyList<DrawingLevel> levels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        DrawingLevel[] active = levels.Where(level => level.Enabled && level.Value > 0).OrderBy(level => level.Value).ToArray();

        // The reference keeps the first A-B construction segment on every
        // completed pitchfork variant.  It is also the red trend-line segment
        // the user sees while choosing point C.
        dc.DrawLine(new Pen(CreateDrawingBrush("#F23645", drawing.Style.Opacity), Math.Max(1.0, pen.Thickness)), a, b);

        Point origin;
        Point levelCenter;
        Vector direction;
        Vector halfWidth;

        if (drawing.ToolId == "inside-pitchfork")
        {
            // TradingView Inside Pitchfork, reconstructed from the supplied
            // reference sequence:
            //   1) A-B is the first red trend-line.
            //   2) O is the midpoint of A-B.
            //   3) O-C defines the direction of the fork.
            //   4) B-C is the mouth of the fork.  Its midpoint is the median
            //      start; level 1 therefore passes through B and C, while 0.5
            //      sits halfway between the median and those boundaries.
            // This is intentionally NOT a standard pitchfork with its width
            // merely halved; that approximation produced the visibly wrong
            // geometry reported by the user.
            origin = Mid(a, b);
            direction = c - origin;
            if (direction.LengthSquared < 0.001) return;
            levelCenter = Mid(b, c);
            halfWidth = (b - c) * 0.5;

            // The short red O-C construction leg remains visible in the final
            // selected geometry, matching the reference video.
            dc.DrawLine(new Pen(CreateDrawingBrush("#F23645", drawing.Style.Opacity), Math.Max(1.0, pen.Thickness)), origin, c);
        }
        else
        {
            // Standard / Schiff-family reference origins. Standard starts at A.
            // Schiff shifts the origin halfway in price; Modified Schiff shifts
            // halfway in both time and price. B-C remains the fork mouth.
            origin = drawing.ToolId switch
            {
                "schiff-pitchfork" => new Point(a.X, (a.Y + b.Y) / 2.0),
                "modified-schiff-pitchfork" => Mid(a, b),
                _ => a
            };
            levelCenter = bcMid;
            direction = levelCenter - origin;
            if (direction.LengthSquared < 0.001) return;
            halfWidth = (c - b) * 0.5;
        }

        Vector unit = direction;
        unit.Normalize();
        double extent = Math.Max(plot.Width, plot.Height) * 4;

        if (ParityFlag(drawing, "Background", true) && active.Length > 0 && drawing.Style.FillOpacity > 0)
        {
            // Each visible strip keeps its own TradingView level colour.  When
            // "Use one color" is enabled the median colour becomes the common
            // line/background colour, exactly as the settings control implies.
            foreach (int side in new[] { -1, 1 })
            {
                double previous = 0;
                foreach (DrawingLevel level in active)
                {
                    Point from = levelCenter + halfWidth * (previous * side);
                    Point to = levelCenter + halfWidth * (level.Value * side);
                    double alpha = Math.Clamp(level.FillOpacity > 0 ? level.FillOpacity : drawing.Style.FillOpacity, 0, 1);
                    alpha = Math.Min(alpha, drawing.Style.FillOpacity);
                    string bandColor = ParityFlag(drawing, "UseOneColor", false)
                        ? drawing.Style.LineColor
                        : (string.IsNullOrWhiteSpace(level.FillColor) ? level.Color : level.FillColor);
                    Brush band = CreateDrawingBrush(bandColor, alpha);
                    DrawPolygon(dc, new[] { from, from + unit * extent, to + unit * extent, to }, null!, band);
                    previous = level.Value;
                }
            }
        }

        // Inside's median starts at the middle of B-C and runs parallel to O-C.
        // Other variants run from their variant origin through the B-C midpoint.
        if (drawing.ToolId == "inside-pitchfork")
            DrawRay(dc, plot, levelCenter, levelCenter + direction, pen, false);
        else
            DrawRay(dc, plot, origin, levelCenter, pen, false);

        foreach (DrawingLevel level in active)
        {
            Pen levelPen = ParityFlag(drawing, "UseOneColor", false)
                ? ParityLevelPen(drawing, level with { Color = drawing.Style.LineColor })
                : ParityLevelPen(drawing, level);
            Vector offset = halfWidth * level.Value;
            Point upper = levelCenter + offset;
            Point lower = levelCenter - offset;
            DrawRay(dc, plot, upper, upper + direction, levelPen, false);
            DrawRay(dc, plot, lower, lower + direction, levelPen, false);
        }
    }

    private void DrawParityGann(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush fill)
    {
        Point adjustedP2 = p2;
        if (drawing.ToolId == "gann-square-fixed")
        {
            // Fixed Gann Square grows/shrinks from its diagonal while preserving
            // a square screen relationship.  The regular Gann Square is NOT
            // forced into a screen square because its price/bar ratio can make
            // the reference object rectangular on screen.
            double side = Math.Max(24, Math.Max(Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y)));
            double sx = p2.X >= p1.X ? 1 : -1;
            double sy = p2.Y >= p1.Y ? 1 : -1;
            adjustedP2 = new Point(p1.X + side * sx, p1.Y + side * sy);
        }

        Rect rect = CreateNormalizedRect(p1, adjustedP2);
        if (rect.Width < 2 || rect.Height < 2)
            return;

        DrawingLevel[] active = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
            .Where(level => level.Enabled && level.Value >= 0 && level.Value <= 1)
            .OrderBy(level => level.Value).ToArray();
        bool reverse = ParityFlag(drawing, "Reverse", false);
        bool useOneColor = ParityFlag(drawing, "UseOneColor", false);
        double bandOpacity = ParityBandOpacity(drawing);

        // Gann Box is a different TradingView construction from Gann Square.
        // Keep the two square tools frozen; only the box uses the dedicated
        // equal time/price grid + nine Gann angles + nested quarter arcs.
        if (drawing.ToolId == "gann-box")
        {
            DrawParityGannBox(dc, layout, drawing, rect, active, pen, reverse, useOneColor, bandOpacity);
            return;
        }

        DrawParityGannSquare(dc, layout, drawing, rect, active, pen, reverse, useOneColor, bandOpacity);
    }

    private void DrawParityGannBox(
        DrawingContext dc,
        ChartLayout layout,
        ChartDrawing drawing,
        Rect rect,
        DrawingLevel[] active,
        Pen pen,
        bool reverse,
        bool useOneColor,
        double bandOpacity)
    {
        DrawingLevel[] grid = (active.Length >= 2
                ? active
                : DrawingParityDefaults.LevelsForTool("gann-box"))
            .Where(level => level.Enabled && level.Value >= 0 && level.Value <= 1)
            .OrderBy(level => level.Value)
            .ToArray();

        if (grid.Length < 2)
            return;

        // The TradingView Gann Box in the user's reference is a pure ratio
        // matrix.  Do not draw Gann-Square arcs/fans/diagonals here.
        dc.PushClip(new RectangleGeometry(rect));
        try
        {
            // Paint both vertical and horizontal translucent bands. Their
            // overlap produces the same mixed-colour cells visible in TV.
            if (bandOpacity > 0.0001)
            {
                for (int i = 0; i + 1 < grid.Length; i++)
                {
                    DrawingLevel level = grid[i];
                    double a = Math.Clamp(grid[i].Value, 0, 1);
                    double b = Math.Clamp(grid[i + 1].Value, 0, 1);
                    if (b <= a) continue;
                    string bandColor = useOneColor ? drawing.Style.LineColor : level.FillColor;
                    Brush bandBrush = CreateDrawingBrush(bandColor, Math.Min(1, bandOpacity * 0.55));
                    dc.DrawRectangle(bandBrush, null,
                        new Rect(rect.Left + rect.Width * a, rect.Top, rect.Width * (b - a), rect.Height));
                    dc.DrawRectangle(bandBrush, null,
                        new Rect(rect.Left, rect.Bottom - rect.Height * b, rect.Width, rect.Height * (b - a)));
                }
            }

            foreach (DrawingLevel level in grid.Where(level => level.Value > 0.000001 && level.Value < 0.999999))
            {
                Pen lp = useOneColor ? pen : ParityLevelPen(drawing, level);
                double x = CrispStroke(rect.Left + rect.Width * level.Value, lp.Thickness);
                double y = CrispStroke(rect.Bottom - rect.Height * level.Value, lp.Thickness);
                dc.DrawLine(lp, new Point(x, rect.Top), new Point(x, rect.Bottom));
                dc.DrawLine(lp, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
        finally
        {
            dc.Pop();
        }

        dc.DrawRectangle(null, pen, rect);

        if (ParityShowReadings(drawing) || ParityShowPrices(drawing))
        {
            foreach (DrawingLevel level in grid)
            {
                Brush lb = CreateDrawingBrush(useOneColor ? drawing.Style.LineColor : level.Color, 1);
                double x = rect.Left + rect.Width * level.Value;
                double y = rect.Bottom - rect.Height * level.Value;
                string horizontal = ParityReadingText(drawing, level);
                string vertical = ParityReadingText(drawing, level, YToPrice(y, layout));

                if (!string.IsNullOrWhiteSpace(horizontal))
                {
                    DrawSmallLabel(dc, horizontal, new Point(x - 11, rect.Top - 17), lb);
                    DrawSmallLabel(dc, horizontal, new Point(x - 11, rect.Bottom + 3), lb);
                }
                if (!string.IsNullOrWhiteSpace(vertical))
                {
                    DrawSmallLabel(dc, vertical, new Point(rect.Left - 60, y - 6), lb);
                    DrawSmallLabel(dc, vertical, new Point(rect.Right + 6, y - 6), lb);
                }
            }
        }
    }

    private void DrawParityGannSquare(
        DrawingContext dc,
        ChartLayout layout,
        ChartDrawing drawing,
        Rect rect,
        DrawingLevel[] active,
        Pen pen,
        bool reverse,
        bool useOneColor,
        double bandOpacity)
    {
        DrawingLevel[] grid = active.Length >= 2
            ? active
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId)
                .Where(level => level.Enabled && level.Value >= 0 && level.Value <= 1)
                .OrderBy(level => level.Value).ToArray();

        Point origin = reverse ? rect.BottomRight : rect.BottomLeft;
        dc.PushClip(new RectangleGeometry(rect));
        try
        {
            // Arc background is a set of quarter-ELLIPTICAL annular sectors.
            // Using circular screen radii was the main mismatch when the Gann
            // Square was rectangular on screen.
            if (bandOpacity > 0.0001 && grid.Length >= 2)
            {
                for (int i = 0; i + 1 < grid.Length; i++)
                {
                    double inner = Math.Clamp(grid[i].Value, 0, 1);
                    double outer = Math.Clamp(grid[i + 1].Value, 0, 1);
                    if (outer <= inner + 0.000001) continue;
                    DrawingLevel bandLevel = grid[i + 1];
                    string color = useOneColor ? drawing.Style.LineColor : bandLevel.Color;
                    Geometry band = CreateGannQuarterEllipseBand(rect, origin, inner, outer, reverse);
                    dc.DrawGeometry(CreateDrawingBrush(color, Math.Min(1, bandOpacity * 0.72)), null, band);
                }
            }

            // Reference default fan: 2x1, 1x1, 1x2.  These are distinct from
            // the level grid and should not turn into one ray per grid level.
            if (ParityFlag(drawing, "Fan", true))
            {
                (double X, double Y, string Color)[] fans =
                {
                    (2, 1, "#00BCD4"),
                    (1, 1, "#4CAF50"),
                    (1, 2, "#089981")
                };
                foreach ((double fx, double fy, string color) in fans)
                {
                    double max = Math.Max(fx, fy);
                    double nx = fx / max;
                    double ny = fy / max;
                    double sx = reverse ? -1 : 1;
                    Point target = new(origin.X + sx * rect.Width * nx, origin.Y - rect.Height * ny);
                    Pen fp = useOneColor
                        ? pen
                        : new Pen(CreateDrawingBrush(color, drawing.Style.Opacity), Math.Max(1, drawing.Style.LineWidth));
                    dc.DrawLine(fp, origin, target);
                }
            }

            if (ParityFlag(drawing, "Arcs", true))
            {
                // TradingView Gann Square arc defaults.  Radius is expressed in
                // data-square units then mapped independently onto screen X/Y,
                // producing the correct quarter ellipses on a rectangular box.
                (double X, double Y, string Color)[] arcs =
                {
                    (1, 0, "#FF9800"), (1, 1, "#FF9800"), (1.5, 0, "#FF9800"),
                    (2, 0, "#00BCD4"), (2, 1, "#00BCD4"),
                    (3, 0, "#4CAF50"), (3, 1, "#4CAF50"),
                    (4, 0, "#089981"), (4, 1, "#089981"),
                    (5, 0, "#2962FF"), (5, 1, "#2962FF")
                };
                foreach ((double ax, double ay, string color) in arcs)
                {
                    double normalizedRadius = Math.Sqrt(ax * ax + ay * ay) / 5.0;
                    Pen ap = useOneColor
                        ? pen
                        : new Pen(CreateDrawingBrush(color, drawing.Style.Opacity), Math.Max(1, drawing.Style.LineWidth));
                    dc.DrawGeometry(null, ap, CreateGannQuarterEllipseArc(rect, origin, normalizedRadius, reverse));
                }
            }
        }
        finally
        {
            dc.Pop();
        }

        // Five equal reference subdivisions: 0, .2, .4, .6, .8, 1 by default.
        foreach (DrawingLevel level in grid.Where(level => level.Value > 0.000001 && level.Value < 0.999999))
        {
            Pen lp = useOneColor ? pen : ParityLevelPen(drawing, level);
            double x = CrispStroke(rect.Left + rect.Width * level.Value, lp.Thickness);
            double y = CrispStroke(rect.Bottom - rect.Height * level.Value, lp.Thickness);
            dc.DrawLine(lp, new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(lp, new Point(rect.Left, y), new Point(rect.Right, y));
        }
        dc.DrawRectangle(null, pen, rect);

        if (ParityShowReadings(drawing) || ParityShowPrices(drawing))
        {
            foreach (DrawingLevel level in grid)
            {
                Brush lb = CreateDrawingBrush(useOneColor ? drawing.Style.LineColor : level.Color, 1);
                double x = rect.Left + rect.Width * level.Value;
                double y = rect.Bottom - rect.Height * level.Value;
                string topLabel = ParityReadingText(drawing, level);
                string sideLabel = ParityReadingText(drawing, level, YToPrice(y, layout));
                if (!string.IsNullOrWhiteSpace(topLabel))
                    DrawSmallLabel(dc, topLabel, new Point(x - 12, rect.Top - 15), lb);
                if (!string.IsNullOrWhiteSpace(sideLabel))
                    DrawSmallLabel(dc, sideLabel, new Point(rect.Left - 64, y - 6), lb);
            }
        }
    }

    private static Geometry CreateGannQuarterEllipseArc(Rect rect, Point origin, double normalizedRadius, bool reverse)
    {
        double radius = Math.Max(0.0001, normalizedRadius);
        double rx = Math.Max(0.5, rect.Width * radius);
        double ry = Math.Max(0.5, rect.Height * radius);
        double sx = reverse ? -1 : 1;
        Point horizontal = new(origin.X + sx * rx, origin.Y);
        Point vertical = new(origin.X, origin.Y - ry);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(horizontal, false, false);
            ctx.ArcTo(vertical, new Size(rx, ry), 0, false,
                reverse ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateGannQuarterEllipseBand(Rect rect, Point origin, double innerRadius, double outerRadius, bool reverse)
    {
        double inner = Math.Max(0, innerRadius);
        double outer = Math.Max(inner + 0.0001, outerRadius);
        double sx = reverse ? -1 : 1;
        double outerRx = Math.Max(0.5, rect.Width * outer);
        double outerRy = Math.Max(0.5, rect.Height * outer);
        double innerRx = rect.Width * inner;
        double innerRy = rect.Height * inner;
        Point outerH = new(origin.X + sx * outerRx, origin.Y);
        Point outerV = new(origin.X, origin.Y - outerRy);
        Point innerH = new(origin.X + sx * innerRx, origin.Y);
        Point innerV = new(origin.X, origin.Y - innerRy);

        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(outerH, true, true);
            ctx.ArcTo(outerV, new Size(outerRx, outerRy), 0, false,
                reverse ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            if (inner <= 0.000001)
            {
                ctx.LineTo(origin, true, false);
            }
            else
            {
                ctx.LineTo(innerV, true, false);
                ctx.ArcTo(innerH, new Size(Math.Max(0.5, innerRx), Math.Max(0.5, innerRy)), 0, false,
                    reverse ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawParityCircle(DrawingContext dc, ChartDrawing drawing, Point center, Point edge, Pen pen, Brush fill, Brush textBrush)
    {
        double radius = Math.Max(1, Distance(center, edge));
        dc.DrawEllipse(fill, pen, center, radius, radius);
        if (!string.IsNullOrWhiteSpace(drawing.Text))
            DrawTextInsideShape(dc, drawing, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2), textBrush);
    }

    private static void DrawParityPath(DrawingContext dc, Point[] points, Pen pen)
    {
        if (points.Length < 2) return;

        // TradingView Path is an angular multi-segment polyline.  Every committed
        // anchor is a real corner; there is no spline/Bezier interpolation between
        // anchors.  Use miter joins so the turns stay visually sharp.
        var pathPen = new Pen(pen.Brush, pen.Thickness)
        {
            DashStyle = pen.DashStyle,
            DashCap = pen.DashCap,
            LineJoin = PenLineJoin.Miter,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
            MiterLimit = 10
        };
        pathPen.Freeze();

        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pathPen, geo);

        // TradingView Path carries an arrowhead only at the final endpoint.
        DrawArrowHead(dc, pathPen, points[^2], points[^1]);
    }

    private void DrawParityPattern(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill, Brush textBrush)
    {
        if (points.Length < 1) return;

        // Elliott-wave markings follow the TradingView reference:
        // the first construction anchor is intentionally unlabelled, impulse labels are circled 1-5,
        // and corrective/combo/triangle labels are parenthesized and inherit the wave-line colour.
        if (drawing.ToolId is "elliott-impulse" or "elliott-triangle" or "elliott-triple-combo" or
            "elliott-correction" or "elliott-double-combo")
        {
            if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
            DrawParityElliottLabels(dc, drawing, points, pen.Brush);
            return;
        }

        Brush patternBrush = pen.Brush;
        Pen guidePen = PatternGuidePen(pen);

        switch (drawing.ToolId)
        {
            case "xabcd-pattern":
            case "cypher-pattern":
            {
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                if (points.Length >= 3 && drawing.Style.FillOpacity > 0.001)
                    DrawPolygon(dc, points, null!, fill);

                DrawPatternPointTags(dc, drawing, points, new[] { "X", "A", "B", "C", "D" }, patternBrush);

                if (points.Length >= 3)
                {
                    dc.DrawLine(guidePen, points[0], points[2]);
                    DrawPatternRatioTag(dc, drawing, 0, 1, 1, 2, Mid(points[0], points[2]) + new Vector(5, -7), patternBrush);
                }
                if (points.Length >= 4)
                {
                    dc.DrawLine(guidePen, points[1], points[3]);
                    DrawPatternRatioTag(dc, drawing, 1, 2, 2, 3, Mid(points[1], points[3]) + new Vector(5, -7), patternBrush);
                }
                if (points.Length >= 5)
                {
                    dc.DrawLine(guidePen, points[2], points[4]);
                    dc.DrawLine(guidePen, points[0], points[4]);
                    DrawPatternRatioTag(dc, drawing, 2, 3, 3, 4, Mid(points[2], points[4]) + new Vector(5, -7), patternBrush);
                    DrawPatternRatioTag(dc, drawing, 0, 1, 0, 4, Mid(points[0], points[4]) + new Vector(5, 7), patternBrush);
                }
                break;
            }

            case "abcd-pattern":
            {
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                DrawPatternPointTags(dc, drawing, points, new[] { "A", "B", "C", "D" }, patternBrush);
                if (points.Length >= 3)
                {
                    dc.DrawLine(guidePen, points[0], points[2]);
                    DrawPatternRatioTag(dc, drawing, 0, 1, 1, 2, Mid(points[0], points[2]) + new Vector(5, -7), patternBrush);
                }
                if (points.Length >= 4)
                {
                    dc.DrawLine(guidePen, points[1], points[3]);
                    DrawPatternRatioTag(dc, drawing, 1, 2, 2, 3, Mid(points[1], points[3]) + new Vector(5, -7), patternBrush);
                }
                break;
            }

            case "triangle-pattern":
            {
                // TradingView Triangle Pattern is A-B-C-D, not the old crossed A-C/B-D bow-tie.
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                DrawPatternPointTags(dc, drawing, points, new[] { "A", "B", "C", "D" }, patternBrush);
                if (points.Length >= 4 && PatternTryLineIntersection(points[0], points[2], points[1], points[3], out Point apex))
                {
                    // Avoid a practically infinite fill if the two guides are almost parallel.
                    double maxSpan = Math.Max(layout.Plot.Width, layout.Plot.Height) * 2.5;
                    Point center = Mid(Mid(points[0], points[1]), Mid(points[2], points[3]));
                    if (Distance(center, apex) <= maxSpan)
                    {
                        dc.DrawLine(guidePen, points[0], apex);
                        dc.DrawLine(guidePen, points[1], apex);
                        if (drawing.Style.FillOpacity > 0.001)
                            DrawPolygon(dc, new[] { points[0], points[1], apex }, null!, fill);
                    }
                    else
                    {
                        dc.DrawLine(guidePen, points[0], points[2]);
                        dc.DrawLine(guidePen, points[1], points[3]);
                    }
                }
                break;
            }

            case "three-drives-pattern":
            {
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                DrawPatternPointTags(dc, drawing, points, new[] { "1", "2", "3", "4", "5", "6" }, patternBrush);
                // TradingView emphasizes the two drive/retracement measurements at the troughs.
                if (points.Length >= 3)
                    DrawPatternRatioTag(dc, drawing, 0, 1, 1, 2, points[1] + new Vector(7, -4), patternBrush);
                if (points.Length >= 5)
                    DrawPatternRatioTag(dc, drawing, 2, 3, 3, 4, points[3] + new Vector(7, -4), patternBrush);
                break;
            }

            case "head-shoulders":
            {
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                if (points.Length >= 2) DrawPatternTag(dc, "Left Shoulder", points[Math.Min(1, points.Length - 1)] + new Vector(-45, -29), patternBrush, 10.5);
                if (points.Length >= 4) DrawPatternTag(dc, "Head", points[3] + new Vector(-17, -31), patternBrush, 10.5);
                if (points.Length >= 6) DrawPatternTag(dc, "Right Shoulder", points[5] + new Vector(-21, -29), patternBrush, 10.5);
                if (points.Length >= 5)
                {
                    // The neckline follows the two troughs and extends across the pattern body.
                    Point left = points[2];
                    Point right = points[4];
                    double dx = right.X - left.X;
                    double slope = Math.Abs(dx) < 0.5 ? 0 : (right.Y - left.Y) / dx;
                    double x1 = points[0].X;
                    double x2 = points[Math.Min(points.Length - 1, 6)].X;
                    Point n1 = new(x1, left.Y + (x1 - left.X) * slope);
                    Point n2 = new(x2, left.Y + (x2 - left.X) * slope);
                    dc.DrawLine(guidePen, n1, n2);
                }
                break;
            }

            default:
                if (points.Length >= 2) DrawPolyline(dc, points, pen, false);
                DrawPatternLabels(dc, drawing, points, patternBrush);
                break;
        }
    }

    private void DrawParityElliottLabels(DrawingContext dc, ChartDrawing drawing, Point[] points, Brush waveBrush)
    {
        if (points.Length <= 1) return;

        switch (drawing.ToolId)
        {
            case "elliott-impulse":
            {
                // TradingView labels wave vertices 1..5; the starting construction anchor has no "0" label.
                int count = Math.Min(5, points.Length - 1);
                for (int wave = 1; wave <= count; wave++)
                {
                    Vector offset = ElliottLabelOffset(points, wave, circled: true);
                    DrawElliottCircledNumber(dc, wave.ToString(CultureInfo.InvariantCulture), points[wave] + offset, waveBrush);
                }
                break;
            }

            case "elliott-correction":
                DrawElliottParenthesizedLabels(dc, points, new[] { "A", "B", "C" }, waveBrush);
                break;

            case "elliott-triangle":
                DrawElliottParenthesizedLabels(dc, points, new[] { "A", "B", "C", "D", "E" }, waveBrush);
                break;

            case "elliott-double-combo":
                DrawElliottParenthesizedLabels(dc, points, new[] { "W", "X", "Y" }, waveBrush);
                break;

            case "elliott-triple-combo":
                DrawElliottParenthesizedLabels(dc, points, new[] { "W", "X", "Y", "X", "Z" }, waveBrush);
                break;
        }
    }

    private void DrawElliottParenthesizedLabels(DrawingContext dc, Point[] points, string[] labels, Brush waveBrush)
    {
        int count = Math.Min(labels.Length, Math.Max(0, points.Length - 1));
        for (int i = 0; i < count; i++)
        {
            int pointIndex = i + 1; // anchor 0 is deliberately unlabelled in the reference.
            Vector offset = ElliottLabelOffset(points, pointIndex, circled: false);
            DrawElliottPlainLabel(dc, $"({labels[i]})", points[pointIndex] + offset, waveBrush);
        }
    }

    private static Vector ElliottLabelOffset(Point[] points, int index, bool circled)
    {
        Point p = points[index];
        double neighborY = 0;
        int neighbors = 0;
        if (index > 0)
        {
            neighborY += points[index - 1].Y;
            neighbors++;
        }
        if (index + 1 < points.Length)
        {
            neighborY += points[index + 1].Y;
            neighbors++;
        }

        bool peak = neighbors > 0 && p.Y < neighborY / neighbors;
        // WPF Y grows downwards. Peaks sit above their anchor; troughs sit below, matching TradingView.
        return peak
            ? new Vector(circled ? -9 : -11, circled ? -30 : -28)
            : new Vector(circled ? -9 : -11, circled ? 9 : 8);
    }

    private void DrawElliottCircledNumber(DrawingContext dc, string value, Point center, Brush waveBrush)
    {
        const double radius = 9.0;
        var outline = new Pen(waveBrush, 1.15)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (outline.CanFreeze) outline.Freeze();
        dc.DrawEllipse(null, outline, center, radius, radius);

        var style = new DrawingStyle { FontFamily = "Segoe UI", FontSize = 10.0 };
        FormattedText text = CreateDrawingText(value, style, waveBrush, 10.0);
        dc.DrawText(text, new Point(center.X - text.Width / 2.0, center.Y - text.Height / 2.0));
    }

    private void DrawElliottPlainLabel(DrawingContext dc, string value, Point at, Brush waveBrush)
    {
        var style = new DrawingStyle { FontFamily = "Segoe UI", FontSize = 11.5 };
        FormattedText text = CreateDrawingText(value, style, waveBrush, 11.5);
        dc.DrawText(text, at);
    }

    private static Pen PatternGuidePen(Pen source)
    {
        var pen = new Pen(source.Brush, Math.Max(0.8, source.Thickness * 0.75))
        {
            DashStyle = DashStyles.Dot,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private void DrawPatternPointTags(DrawingContext dc, ChartDrawing drawing, Point[] points, string[] labels, Brush patternBrush)
    {
        int count = Math.Min(points.Length, labels.Length);
        for (int i = 0; i < count; i++)
        {
            string label = labels[i];
            if (string.IsNullOrWhiteSpace(label)) continue;
            Vector offset = PatternPointLabelOffset(points, i);
            DrawPatternTag(dc, label, points[i] + offset, patternBrush, 10.5);
        }
    }

    private static Vector PatternPointLabelOffset(Point[] points, int index)
    {
        Point p = points[index];
        double neighborY = p.Y;
        int neighbors = 0;
        if (index > 0) { neighborY += points[index - 1].Y; neighbors++; }
        if (index + 1 < points.Length) { neighborY += points[index + 1].Y; neighbors++; }
        if (neighbors > 0) neighborY /= neighbors + 1.0;
        bool peak = p.Y < neighborY;
        return peak ? new Vector(-8, -28) : new Vector(-8, 9);
    }

    private void DrawPatternRatioTag(DrawingContext dc, ChartDrawing drawing,
        int baseStart, int baseEnd, int measuredStart, int measuredEnd, Point at, Brush patternBrush)
    {
        if (drawing.Anchors.Count <= Math.Max(Math.Max(baseStart, baseEnd), Math.Max(measuredStart, measuredEnd))) return;
        double baseLength = Math.Abs(drawing.Anchors[baseEnd].Price - drawing.Anchors[baseStart].Price);
        double measured = Math.Abs(drawing.Anchors[measuredEnd].Price - drawing.Anchors[measuredStart].Price);
        if (baseLength <= 1e-12) return;
        DrawPatternTag(dc, (measured / baseLength).ToString("0.###", CultureInfo.InvariantCulture), at, patternBrush, 10.5);
    }

    private void DrawPatternTag(DrawingContext dc, string value, Point at, Brush patternBrush, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Brush background = PatternTagBackground(patternBrush);
        Brush foreground = PatternTagForeground(patternBrush);
        var style = new DrawingStyle { FontFamily = "Segoe UI", FontSize = fontSize };
        FormattedText text = CreateDrawingText(value, style, foreground, fontSize);
        Rect box = new(at.X - 5, at.Y - 3, text.Width + 10, text.Height + 6);
        dc.DrawRoundedRectangle(background, new Pen(patternBrush, 0.8), box, 4, 4);
        dc.DrawText(text, at);
    }

    private static Brush PatternTagBackground(Brush source)
    {
        if (source is SolidColorBrush solid)
        {
            var brush = new SolidColorBrush(Color.FromArgb(235, solid.Color.R, solid.Color.G, solid.Color.B));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        return source;
    }

    private static Brush PatternTagForeground(Brush source)
    {
        if (source is not SolidColorBrush solid) return Brushes.White;
        double luminance = (0.2126 * solid.Color.R + 0.7152 * solid.Color.G + 0.0722 * solid.Color.B) / 255.0;
        return luminance > 0.68 ? Brushes.Black : Brushes.White;
    }

    private static bool PatternTryLineIntersection(Point a, Point b, Point c, Point d, out Point intersection)
    {
        double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y;
        double x3 = c.X, y3 = c.Y, x4 = d.X, y4 = d.Y;
        double denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denominator) < 1e-8)
        {
            intersection = default;
            return false;
        }
        double determinant1 = x1 * y2 - y1 * x2;
        double determinant2 = x3 * y4 - y3 * x4;
        intersection = new Point(
            (determinant1 * (x3 - x4) - (x1 - x2) * determinant2) / denominator,
            (determinant1 * (y3 - y4) - (y1 - y2) * determinant2) / denominator);
        return double.IsFinite(intersection.X) && double.IsFinite(intersection.Y);
    }

    private void DrawParityPosition(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush textBrush)
    {
        Point entry = points[0], target = points[1], stop = points[2];
        double left = Math.Min(entry.X, Math.Min(target.X, stop.X));
        double right = Math.Max(entry.X, Math.Max(target.X, stop.X));
        if (right - left < 28.0)
            right = left + 28.0;
        bool isShort = drawing.ToolId == "short-position";

        DrawingLevel? targetLevel = ParityRoleLevel(drawing, "Target", 0) ?? ParityRoleLevel(drawing, "Profit", 0);
        DrawingLevel? stopLevel = ParityRoleLevel(drawing, "Stop", 1) ?? ParityRoleLevel(drawing, "Loss", 1);
        DrawingLevel? entryLevel = ParityRoleLevel(drawing, "Entry", 2);
        Brush targetFill = ParityRoleFill(drawing, targetLevel, "#089981", 0.24);
        Brush stopFill = ParityRoleFill(drawing, stopLevel, "#F23645", 0.24);
        Pen targetPen = targetLevel is null ? pen : ParityLevelPen(drawing, targetLevel);
        Pen stopPen = stopLevel is null ? pen : ParityLevelPen(drawing, stopLevel);
        Pen entryPen = pen; // the Style "Lines" control owns the one visible Entry line

        Rect targetRect = new(new Point(left, Math.Min(entry.Y, target.Y)), new Point(right, Math.Max(entry.Y, target.Y)));
        Rect stopRect = new(new Point(left, Math.Min(entry.Y, stop.Y)), new Point(right, Math.Max(entry.Y, stop.Y)));
        // Clean TradingView-style position body: the TP/SL zones have no outer
        // frame. Only the Entry level keeps a visible line when deselected.
        dc.DrawRectangle(targetFill, null, targetRect);
        dc.DrawRectangle(stopFill, null, stopRect);
        dc.DrawLine(entryPen, new Point(left, entry.Y), new Point(right, entry.Y));

        double entryPrice = drawing.Anchors[0].Price;
        double targetPrice = drawing.Anchors[1].Price;
        double stopPrice = drawing.Anchors[2].Price;
        double riskDistance = Math.Abs(entryPrice - stopPrice);
        double rewardDistance = Math.Abs(targetPrice - entryPrice);
        double rr = riskDistance <= 0 ? 0 : rewardDistance / riskDistance;
        double account = Math.Max(0, ParityOption(drawing, "AccountSize", 10000));
        double lot = Math.Max(1e-12, ParityOption(drawing, "LotSize", 1));
        double leverage = Math.Max(1e-12, ParityOption(drawing, "Leverage", 1));
        double pointValue = Math.Max(1e-12, ParityOption(drawing, "PointValue", 1));
        double riskInput = Math.Max(0, ParityOption(drawing, "Risk", 1));
        double riskMoney = ParityFlag(drawing, "RiskIsPercent", true) ? account * riskInput / 100.0 : riskInput;
        double qtyRisk = riskDistance <= 1e-12 ? 0 : (riskMoney / (riskDistance * pointValue)) / lot;
        double qtyLvg = entryPrice <= 1e-12 ? qtyRisk : (account * leverage / entryPrice) * pointValue / lot;
        double qty = qtyRisk <= 0 ? qtyLvg : Math.Min(qtyRisk, qtyLvg);
        int precision = (int)Math.Clamp(ParityOption(drawing, "QtyPrecision", 2), 0, 8);
        double profitPnl = rewardDistance * qty * pointValue * lot;
        double lossPnl = -riskDistance * qty * pointValue * lot;
        double currentPrice = DrawingCandles.Count > 0 ? DrawingCandles[^1].Close : entryPrice;
        double openMove = isShort ? entryPrice - currentPrice : currentPrice - entryPrice;
        double openPnl = openMove * qty * pointValue * lot;
        double pointSize = PositionPointSize(drawing);
        double targetPoints = pointSize > 0 ? rewardDistance / pointSize : rewardDistance;
        double stopPoints = pointSize > 0 ? riskDistance / pointSize : riskDistance;
        double targetPercent = entryPrice == 0 ? 0 : rewardDistance / Math.Abs(entryPrice) * 100.0;
        double stopPercent = entryPrice == 0 ? 0 : riskDistance / Math.Abs(entryPrice) * 100.0;

        int statsMode = (int)Math.Clamp(Math.Round(ParityOption(drawing, "StatsMode", 0)), 0, 2);
        bool compact = ParityFlag(drawing, "CompactStats", false) || statsMode == 1;
        bool selected = _selectedDrawingIds.Contains(drawing.Id);
        bool showStats = selected && drawing.Style.ShowStatistics && statsMode != 2;
        if (showStats)
        {
            Brush targetBrush = targetPen.Brush;
            Brush stopBrush = stopPen.Brush;
            Brush entryBrush = entryPen.Brush;
            string targetText = $"Target: {rewardDistance:0.###} ({targetPercent:0.###}%) {targetPoints:N1}, Amount: {profitPnl:0.##}";
            string stopText = $"Stop: {riskDistance:0.###} ({stopPercent:0.###}%) {stopPoints:N1}, Amount: {Math.Abs(lossPnl):0.##}";
            string centerText = compact
                ? $"R:R {rr:0.##}   Qty: {qty.ToString($"F{precision}", CultureInfo.InvariantCulture)}"
                : $"Open PnL: {openPnl:+0.##;-0.##;0}, Qty: {qty.ToString($"F{precision}", CultureInfo.InvariantCulture)}\nRisk/reward ratio: {rr:0.##}";

            double targetY = target.Y < entry.Y ? targetRect.Top - 28 : targetRect.Bottom + 4;
            double stopY = stop.Y < entry.Y ? stopRect.Top - 28 : stopRect.Bottom + 4;
            DrawParityColoredLabel(dc, targetText, new Point(entry.X + 6, targetY), targetBrush, drawing.Style, 0.94);
            DrawParityColoredLabel(dc, stopText, new Point(entry.X + 6, stopY), stopBrush, drawing.Style, 0.94);
            DrawParityColoredLabel(dc, centerText, new Point(entry.X + 10, entry.Y + (isShort ? 7 : -43)), entryBrush, drawing.Style, 0.92);
        }

        if (selected && (drawing.Style.ShowPriceLabels || ParityFlag(drawing, "PriceLabels", true)))
        {
            DrawPriceLabel(dc, layout, entryPrice, entry, textBrush);
            DrawPriceLabel(dc, layout, targetPrice, target, textBrush);
            DrawPriceLabel(dc, layout, stopPrice, stop, textBrush);
        }
    }

    private void DrawParityForecast(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill, Brush textBrush)
    {
        Point start = points[0];
        Point end = points[1];

        // Two-click Position Forecast. Point A is the forecast start and Point B
        // is the target. A computed control point gives the TradingView-like
        // easing/curve without requiring a third construction click.
        Point control = new((start.X + end.X) / 2.0, start.Y);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.QuadraticBezierTo(control, end, true, false);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        DrawParityAnchorGlyph(dc, start, pen.Brush, 3.2, true);
        DrawParityAnchorGlyph(dc, end, pen.Brush, 3.2, true);

        double startPrice = drawing.Anchors[0].Price;
        double endPrice = drawing.Anchors[1].Price;
        double change = endPrice - startPrice;
        double percent = startPrice == 0 ? 0 : change / Math.Abs(startPrice) * 100.0;
        TimeSpan elapsed = TimeSpan.FromSeconds(Math.Abs(drawing.Anchors[1].StartUnix - drawing.Anchors[0].StartUnix));
        string startTime = DateTimeOffset.FromUnixTimeSeconds(drawing.Anchors[0].StartUnix).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string endTime = DateTimeOffset.FromUnixTimeSeconds(drawing.Anchors[1].StartUnix).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Brush labelBrush = pen.Brush;

        if (ParityFlag(drawing, "ShowStartLabel", true))
            DrawParityColoredLabel(dc, $"{startPrice:0.#####}\n{startTime}", start + new Vector(-34, -54), labelBrush, drawing.Style, 0.90);

        if (ParityFlag(drawing, "ShowStats", true) && ParityFlag(drawing, "ShowEndLabel", true))
        {
            string result = $"{change:+0.###;-0.###;0} ({percent:+0.##;-0.##;0}%) in {FormatDuration(elapsed)}\n{endPrice:0.#####}  {endTime}";
            DrawParityColoredLabel(dc, result, end + new Vector(-36, 14), labelBrush, drawing.Style, 0.92);
        }

        if (ParityFlag(drawing, "ShowStatus", true))
        {
            bool success = ForecastTargetReached(drawing, startPrice, endPrice);
            Brush statusBrush = CreateDrawingBrush(success ? "#16A34A" : "#64748B", 0.96);
            string statusText = success ? "✓ SUCCESS" : "⏱ WAITING — On the way";
            DrawParityColoredLabel(dc, statusText, end + new Vector(-36, 57), statusBrush, drawing.Style, 0.98);
        }
    }

    private bool ForecastTargetReached(ChartDrawing drawing, double startPrice, double targetPrice)
    {
        if (DrawingCandles.Count == 0)
            return false;

        long startUnix = drawing.Anchors[0].StartUnix;
        bool targetAbove = targetPrice >= startPrice;
        foreach (Candle candle in DrawingCandles)
        {
            if (candle.EndUnix < startUnix)
                continue;
            if (targetAbove ? candle.High >= targetPrice : candle.Low <= targetPrice)
                return true;
        }
        return false;
    }

    private void DrawParityRangeMeasurement(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush fill, Brush textBrush)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        string id = drawing.ToolId;
        bool priceOnly = id == "price-range";
        bool dateOnly = id == "date-range";
        bool combined = id == "date-price-range";
        if (((priceOnly || combined) && rect.Height < 0.5) || ((dateOnly || combined) && rect.Width < 0.5))
            return;

        dc.DrawRectangle(fill, null, rect);

        double cx = rect.Left + rect.Width / 2.0;
        double cy = rect.Top + rect.Height / 2.0;

        if (priceOnly || combined)
        {
            dc.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
            dc.DrawLine(pen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
            dc.DrawLine(pen, new Point(cx, rect.Bottom), new Point(cx, rect.Top));
            DrawArrowHead(dc, pen, new Point(cx, rect.Bottom), new Point(cx, rect.Top));
            DrawArrowHead(dc, pen, new Point(cx, rect.Top), new Point(cx, rect.Bottom));
        }

        if (dateOnly || combined)
        {
            dc.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
            dc.DrawLine(pen, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
            dc.DrawLine(pen, new Point(rect.Left, cy), new Point(rect.Right, cy));
            DrawArrowHead(dc, pen, new Point(rect.Left, cy), new Point(rect.Right, cy));
            DrawArrowHead(dc, pen, new Point(rect.Right, cy), new Point(rect.Left, cy));
        }

        double priceChange = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
        double percent = drawing.Anchors[0].Price == 0 ? 0 : priceChange / Math.Abs(drawing.Anchors[0].Price) * 100.0;
        int i1 = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int i2 = FindNearestDrawingCandleIndex(drawing.Anchors[1]);
        int lo = Math.Clamp(Math.Min(i1, i2), 0, Math.Max(0, DrawingCandles.Count - 1));
        int hi = Math.Clamp(Math.Max(i1, i2), 0, Math.Max(0, DrawingCandles.Count - 1));
        int bars = Math.Abs(i2 - i1);
        TimeSpan elapsed = TimeSpan.FromSeconds(Math.Abs(drawing.Anchors[1].StartUnix - drawing.Anchors[0].StartUnix));
        double pointSize = DrawingCandles.Count > 0 && DrawingCandles[lo].Point > 0 ? DrawingCandles[lo].Point : 1.0;
        double pointsMove = pointSize > 0 ? priceChange / pointSize : priceChange;
        long volume = 0;
        if (DrawingCandles.Count > 0 && hi >= lo)
        {
            bool hasReal = false;
            for (int i = lo; i <= hi; i++)
            {
                if (DrawingCandles[i].RealVolume > 0) { hasReal = true; break; }
            }
            for (int i = lo; i <= hi; i++)
                volume += hasReal ? DrawingCandles[i].RealVolume : DrawingCandles[i].TickVolume;
        }

        var lines = new List<string>();
        if (!dateOnly)
        {
            var priceParts = new List<string>();
            if (ParityFlag(drawing, "ShowPriceChange", true)) priceParts.Add($"{priceChange:+0.###;-0.###;0}");
            if (ParityFlag(drawing, "ShowPercent", true)) priceParts.Add($"({percent:+0.##;-0.##;0}%)");
            if (ParityFlag(drawing, "ShowPoints", true)) priceParts.Add(pointsMove.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture));
            if (priceParts.Count > 0) lines.Add(string.Join(" ", priceParts));
        }
        if (!priceOnly)
        {
            var dateParts = new List<string>();
            if (ParityFlag(drawing, "ShowBars", true)) dateParts.Add($"{bars} bars");
            if (ParityFlag(drawing, "ShowDuration", true)) dateParts.Add(FormatDuration(elapsed));
            if (dateParts.Count > 0) lines.Add(string.Join(", ", dateParts));
            if (ParityFlag(drawing, "ShowVolume", true)) lines.Add($"Vol {FormatParityCompactVolume(volume)}");
        }

        if (drawing.Style.ShowStatistics && lines.Count > 0)
        {
            Point labelAt = priceOnly
                ? new Point(Math.Max(layout.Plot.Left + 4, cx - 78), rect.Top - 44)
                : new Point(Math.Max(layout.Plot.Left + 4, cx - 78), rect.Bottom + 10);
            DrawParityMeasurementLabel(dc, string.Join("\n", lines), labelAt, textBrush, drawing.Style);
        }
    }

    private double PositionPointSize(ChartDrawing drawing)
    {
        if (DrawingCandles.Count == 0) return 1.0;
        int index = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[0]), 0, DrawingCandles.Count - 1);
        return DrawingCandles[index].Point > 0 ? DrawingCandles[index].Point : 1.0;
    }

    private static string FormatParityCompactVolume(long value)
    {
        double abs = Math.Abs((double)value);
        if (abs >= 1_000_000_000) return $"{value / 1_000_000_000d:0.##}B";
        if (abs >= 1_000_000) return $"{value / 1_000_000d:0.##}M";
        if (abs >= 1_000) return $"{value / 1_000d:0.##}K";
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void DrawParityColoredLabel(DrawingContext dc, string value, Point at, Brush background, DrawingStyle style, double opacity)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Brush foreground = CreateDrawingBrush(style.TextColor, style.Opacity);
        FormattedText text = CreateDrawingText(value, style, foreground, style.FontSize);
        Color baseColor = background is SolidColorBrush solid ? solid.Color : Color.FromRgb(41, 98, 255);
        var back = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round(opacity * 255), 0, 255), baseColor.R, baseColor.G, baseColor.B));
        var box = new Rect(at.X - 5, at.Y - 3, text.Width + 10, text.Height + 6);
        dc.DrawRoundedRectangle(back, new Pen(CreateDrawingBrush("#FFFFFF", 0.28), 1), box, 4, 4);
        dc.DrawText(text, at);
    }

    private void DrawParityMeasurementLabel(DrawingContext dc, string value, Point at, Brush textBrush, DrawingStyle style)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        FormattedText text = CreateDrawingText(value, style, textBrush, style.FontSize);
        Brush background = CreateDrawingBrush(style.BackgroundColor, 0.90);
        Pen border = new(CreateDrawingBrush(style.LineColor, 0.35), 1);
        Rect box = new(at.X - 7, at.Y - 5, text.Width + 14, text.Height + 10);
        dc.DrawRoundedRectangle(background, border, box, 5, 5);
        dc.DrawText(text, at);
    }

    private static string ParityAnnotationAnchor(ChartDrawing drawing) =>
        drawing.TextOptions.TryGetValue("Anchor", out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "Auto";

    private static Point ParityTextOrigin(Point anchor, double width, double height, string mode, double gap = 8)
    {
        return mode.Trim().ToLowerInvariant() switch
        {
            "top" => new Point(anchor.X - width / 2, anchor.Y - height - gap),
            "bottom" => new Point(anchor.X - width / 2, anchor.Y + gap),
            "left" => new Point(anchor.X - width - gap, anchor.Y - height / 2),
            "right" => new Point(anchor.X + gap, anchor.Y - height / 2),
            _ => new Point(anchor.X - width / 2, anchor.Y - height / 2)
        };
    }

    private void DrawParityText(DrawingContext dc, ChartDrawing drawing, Point anchor, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        // v1.13.0.113 introduced #1F2937 as the plain-Text default, while TickLab's
        // default chart is #080808. Keep explicit user colours untouched, but make
        // the stock Text colour automatically contrast with the current chart.
        Brush effectiveTextBrush = string.Equals(drawing.Style.TextColor, "#1F2937", StringComparison.OrdinalIgnoreCase)
            ? GannFanReadingBrush()
            : textBrush;
        FormattedText text = CreateDrawingText(value, drawing.Style, effectiveTextBrush, drawing.Style.FontSize);
        Point at = ParityTextOrigin(anchor, text.Width, text.Height, ParityAnnotationAnchor(drawing), 7);
        dc.DrawText(text, at);
    }

    private void DrawParityNote(DrawingContext dc, ChartDrawing drawing, Point anchor, Point labelAnchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        const double padX = 9;
        const double padY = 6;
        Point topLeft = ParityTextOrigin(labelAnchor, Math.Max(90, text.Width + padX * 2), text.Height + padY * 2,
            ParityAnnotationAnchor(drawing) == "Auto" ? "Right" : ParityAnnotationAnchor(drawing), 7);
        Rect box = new(topLeft.X, topLeft.Y, Math.Max(90, text.Width + padX * 2), text.Height + padY * 2);
        Point edge = ClosestPointOnRect(box, anchor);
        dc.DrawLine(pen, anchor, edge);
        dc.DrawRoundedRectangle(CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity), pen, box, 5, 5);
        dc.DrawText(text, new Point(box.Left + padX, box.Top + padY));
    }

    private void DrawParityPin(DrawingContext dc, ChartDrawing drawing, Point anchor, Pen pen, Brush fill, Brush textBrush)
    {
        Brush pinFill = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        Point center = anchor + new Vector(0, -11);
        const double radius = 9.0;
        var body = new StreamGeometry();
        using (StreamGeometryContext ctx = body.Open())
        {
            ctx.BeginFigure(new Point(center.X - radius, center.Y), true, true);
            ctx.BezierTo(new Point(center.X - radius, center.Y - 7), new Point(center.X - 5, center.Y - radius), new Point(center.X, center.Y - radius), true, false);
            ctx.BezierTo(new Point(center.X + 5, center.Y - radius), new Point(center.X + radius, center.Y - 7), new Point(center.X + radius, center.Y), true, false);
            ctx.BezierTo(new Point(center.X + radius, center.Y + 6), new Point(center.X + 4, center.Y + 11), anchor, true, false);
            ctx.BezierTo(new Point(center.X - 4, center.Y + 11), new Point(center.X - radius, center.Y + 6), new Point(center.X - radius, center.Y), true, false);
        }
        if (body.CanFreeze) body.Freeze();
        dc.DrawGeometry(pinFill, pen, body);
        dc.DrawEllipse(CreateDrawingBrush("#FFFFFF", 0.95), null, center + new Vector(0, -1), 3.4, 3.4);

        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        double width = Math.Max(82, text.Width + 18);
        double height = text.Height + 12;
        Point labelAnchor = center + new Vector(0, -radius - 8);
        Point topLeft = ParityTextOrigin(labelAnchor, width, height,
            ParityAnnotationAnchor(drawing) == "Auto" ? "Top" : ParityAnnotationAnchor(drawing), 2);
        Rect box = new(topLeft.X, topLeft.Y, width, height);
        Brush background = CreateDrawingBrush(drawing.Style.BackgroundColor, Math.Max(0.08, drawing.Style.FillOpacity));
        dc.DrawRoundedRectangle(background, new Pen(CreateDrawingBrush(drawing.Style.LineColor, 0.35), 1), box, 5, 5);
        dc.DrawText(text, new Point(box.Left + 9, box.Top + 6));
    }

    internal static string[][] ParseParityTableCells(string source)
    {
        string normalized = string.IsNullOrEmpty(source) ? "||\n||" : source.Replace("\r", string.Empty);
        string[] rowTexts = normalized.Split('\n');
        if (rowTexts.Length < 1) rowTexts = new[] { "||", "||" };
        int columns = Math.Max(1, rowTexts.Max(row => row.Split('|').Length));
        return rowTexts.Select(row =>
        {
            string[] values = row.Split('|');
            Array.Resize(ref values, columns);
            for (int i = 0; i < values.Length; i++) values[i] ??= string.Empty;
            return values;
        }).ToArray();
    }

    internal static string SerializeParityTableCells(string[][] cells) =>
        string.Join("\n", cells.Select(row => string.Join("|", row.Select(value => value ?? string.Empty))));

    internal static Rect GetParityTableCellRect(Rect rect, int rows, int columns, int row, int column)
    {
        double rowHeight = rect.Height / Math.Max(1, rows);
        double columnWidth = rect.Width / Math.Max(1, columns);
        return new Rect(rect.Left + column * columnWidth, rect.Top + row * rowHeight, columnWidth, rowHeight);
    }

    private void DrawParityTable(DrawingContext dc, ChartDrawing drawing, Point a, Point b, Pen pen, Brush fill, Brush textBrush)
    {
        Rect rect = CreateNormalizedRect(a, b);
        if (rect.Width < 8 || rect.Height < 8) return;
        Brush tableFill = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        dc.DrawRectangle(tableFill, pen, rect);
        string[][] cells = ParseParityTableCells(drawing.Text);
        int rows = Math.Max(1, cells.Length);
        int columns = Math.Max(1, cells.Max(row => row.Length));
        double rowHeight = rect.Height / rows;
        double columnWidth = rect.Width / columns;
        for (int r = 1; r < rows; r++)
            dc.DrawLine(pen, new Point(rect.Left, rect.Top + r * rowHeight), new Point(rect.Right, rect.Top + r * rowHeight));
        for (int c = 1; c < columns; c++)
            dc.DrawLine(pen, new Point(rect.Left + c * columnWidth, rect.Top), new Point(rect.Left + c * columnWidth, rect.Bottom));

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Rect cellRect = GetParityTableCellRect(rect, rows, columns, r, c);
                if (drawing.Id == _activeTableDrawingId && _selectedDrawingIds.Contains(drawing.Id) &&
                    r == _activeTableCellRow && c == _activeTableCellColumn)
                {
                    dc.DrawRectangle(CreateDrawingBrush("#2962FF", 0.08), new Pen(CreateDrawingBrush("#2962FF", 0.95), 1.4), cellRect);
                }
                string value = c < cells[r].Length ? cells[r][c] : string.Empty;
                Rect inner = new(cellRect.Left + 5, cellRect.Top + 3, Math.Max(0, cellRect.Width - 10), Math.Max(0, cellRect.Height - 6));
                FormattedText? text = string.IsNullOrEmpty(value)
                    ? null
                    : CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
                double textWidth = text?.Width ?? 0;
                double textHeight = text?.Height ?? Math.Max(12, drawing.Style.FontSize * 1.25);
                double x = drawing.Style.HorizontalTextAlignment.Trim().ToLowerInvariant() switch
                {
                    "right" => inner.Right - textWidth,
                    "center" => inner.Left + (inner.Width - textWidth) / 2,
                    _ => inner.Left
                };
                double y = inner.Top + Math.Max(0, (inner.Height - textHeight) / 2);
                x = Math.Max(inner.Left, x);
                if (text is not null)
                    dc.DrawText(text, new Point(x, y));

                if (drawing.Id == _activeTableDrawingId && _selectedDrawingIds.Contains(drawing.Id) &&
                    r == _activeTableCellRow && c == _activeTableCellColumn)
                {
                    // A small insertion caret makes direct cell typing obvious even
                    // though editing remains on the chart rather than a floating dialog.
                    double caretX = Math.Min(inner.Right - 1, x + textWidth + 1.5);
                    double caretTop = y + 1;
                    double caretBottom = Math.Min(inner.Bottom - 1, y + textHeight - 1);
                    dc.DrawLine(new Pen(CreateDrawingBrush(drawing.Style.TextColor, 0.95), 1.15),
                        new Point(caretX, caretTop), new Point(caretX, Math.Max(caretTop + 8, caretBottom)));
                }
            }
        }
    }

    private void DrawParityCallout(DrawingContext dc, ChartDrawing drawing, Point anchor, Point labelAnchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        double width = Math.Max(86, text.Width + 20);
        double height = text.Height + 14;
        Point topLeft = ParityTextOrigin(labelAnchor, width, height, "Right", 8);
        Rect box = new(topLeft.X, topLeft.Y, width, height);

        // Attach the tail to a straight section of the rounded rectangle, never to
        // the rounded corner. Then extend the tail base several pixels INTO the box.
        // The rectangle is painted after the tail, so that overlap hides the base
        // seam and produces one continuous callout silhouette with no visible gap.
        Point center = new(box.Left + box.Width * 0.5, box.Top + box.Height * 0.5);
        double dx = anchor.X - center.X;
        double dy = anchor.Y - center.Y;
        double halfW = Math.Max(1.0, box.Width * 0.5);
        double halfH = Math.Max(1.0, box.Height * 0.5);
        double cornerInset = Math.Min(12.0, Math.Max(8.0, height * 0.28));
        Point edge;
        Vector inward;
        if (Math.Abs(dx) / halfW >= Math.Abs(dy) / halfH)
        {
            bool leftSide = dx < 0;
            edge = new Point(
                leftSide ? box.Left : box.Right,
                Math.Clamp(anchor.Y, box.Top + cornerInset, box.Bottom - cornerInset));
            inward = new Vector(leftSide ? 1 : -1, 0);
        }
        else
        {
            bool topSide = dy < 0;
            edge = new Point(
                Math.Clamp(anchor.X, box.Left + cornerInset, box.Right - cornerInset),
                topSide ? box.Top : box.Bottom);
            inward = new Vector(0, topSide ? 1 : -1);
        }

        Point join = edge + inward * 6.0;
        Vector tangent = join - anchor;
        if (tangent.Length < 1) tangent = -inward;
        tangent.Normalize();
        Vector normal = new(-tangent.Y, tangent.X);
        double neck = Math.Min(7, height * 0.28);
        var tail = new StreamGeometry();
        using (StreamGeometryContext ctx = tail.Open())
        {
            ctx.BeginFigure(anchor, true, true);
            ctx.LineTo(join + normal * neck, true, false);
            ctx.LineTo(join - normal * neck, true, false);
        }
        if (tail.CanFreeze) tail.Freeze();
        Brush bubble = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        dc.DrawGeometry(bubble, pen, tail);
        dc.DrawRoundedRectangle(bubble, pen, box, 8, 8);
        dc.DrawText(text, new Point(box.Left + 10, box.Top + 7));
    }

    private void DrawParityComment(DrawingContext dc, ChartDrawing drawing, Point anchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        double width = Math.Max(76, text.Width + 20);
        double height = text.Height + 14;
        Rect box = new(anchor.X + 10, anchor.Y - height / 2, width, height);
        Brush bubble = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        var tail = new StreamGeometry();
        using (StreamGeometryContext ctx = tail.Open())
        {
            ctx.BeginFigure(anchor, true, true);
            ctx.LineTo(new Point(box.Left + 10, box.Top + height * 0.35), true, false);
            ctx.LineTo(new Point(box.Left + 10, box.Bottom - height * 0.35), true, false);
        }
        if (tail.CanFreeze) tail.Freeze();
        dc.DrawGeometry(bubble, pen, tail);
        dc.DrawRoundedRectangle(bubble, pen, box, height / 2, height / 2);
        dc.DrawText(text, new Point(box.Left + 10, box.Top + 7));
    }

    private void DrawParitySignpost(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point anchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        double width = Math.Max(92, text.Width + 20);
        double height = text.Height + 14;
        Rect box = new(anchor.X - width / 2, anchor.Y - height - 18, width, height);
        Brush background = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        dc.DrawLine(pen, new Point(anchor.X, box.Bottom), new Point(anchor.X, layout.Plot.Bottom));
        dc.DrawRoundedRectangle(background, pen, box, 6, 6);
        var notch = new StreamGeometry();
        using (StreamGeometryContext ctx = notch.Open())
        {
            ctx.BeginFigure(new Point(anchor.X - 7, box.Bottom), true, true);
            ctx.LineTo(new Point(anchor.X + 7, box.Bottom), true, false);
            ctx.LineTo(new Point(anchor.X, box.Bottom + 8), true, false);
        }
        if (notch.CanFreeze) notch.Freeze();
        dc.DrawGeometry(background, pen, notch);
        dc.DrawText(text, new Point(box.Left + 10, box.Top + 7));
    }

    private string FormatParityPrice(DrawingAnchor anchor)
    {
        int digits = 5;
        if (DrawingCandles.Count > 0)
        {
            int index = Math.Clamp(FindNearestDrawingCandleIndex(anchor), 0, DrawingCandles.Count - 1);
            digits = Math.Clamp(DrawingCandles[index].Digits, 0, 10);
        }
        return anchor.Price.ToString($"N{digits}", CultureInfo.InvariantCulture);
    }

    private void DrawParityPriceLabel(DrawingContext dc, ChartDrawing drawing, Point anchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = FormatParityPrice(drawing.Anchors[0]);
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        const double padX = 10;
        const double padY = 6;
        Rect box = new(anchor.X + 10, anchor.Y - text.Height / 2 - padY, text.Width + padX * 2, text.Height + padY * 2);
        Brush bubble = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        var tail = new StreamGeometry();
        using (StreamGeometryContext ctx = tail.Open())
        {
            ctx.BeginFigure(anchor, true, true);
            ctx.LineTo(new Point(box.Left + 10, anchor.Y - 7), true, false);
            ctx.LineTo(new Point(box.Left + 10, anchor.Y + 7), true, false);
        }
        if (tail.CanFreeze) tail.Freeze();
        dc.DrawGeometry(bubble, pen, tail);
        dc.DrawRoundedRectangle(bubble, pen, box, 5, 5);
        dc.DrawText(text, new Point(box.Left + padX, box.Top + padY));
    }

    private void DrawParityPriceNote(DrawingContext dc, ChartDrawing drawing, Point anchor, Point labelAnchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = FormatParityPrice(drawing.Anchors[0]);
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        double width = text.Width + 20;
        double height = text.Height + 12;
        Point topLeft = ParityTextOrigin(labelAnchor, width, height,
            ParityAnnotationAnchor(drawing) == "Auto" ? "Right" : ParityAnnotationAnchor(drawing), 7);
        Rect box = new(topLeft.X, topLeft.Y, width, height);
        Point edge = ClosestPointOnRect(box, anchor);
        dc.DrawLine(pen, anchor, edge);
        Brush bubble = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity);
        dc.DrawRoundedRectangle(bubble, pen, box, 5, 5);
        dc.DrawText(text, new Point(box.Left + 10, box.Top + 6));
    }

    private static void DrawParityFlag(DrawingContext dc, ChartDrawing drawing, Point anchor, Pen pen, Brush fill)
    {
        Point poleTop = anchor + new Vector(0, -30);
        dc.DrawLine(pen, anchor + new Vector(0, 5), poleTop);
        var flag = new StreamGeometry();
        using (StreamGeometryContext ctx = flag.Open())
        {
            ctx.BeginFigure(poleTop + new Vector(1, 1), true, true);
            ctx.LineTo(poleTop + new Vector(26, 1), true, false);
            ctx.LineTo(poleTop + new Vector(20, 8), true, false);
            ctx.LineTo(poleTop + new Vector(26, 15), true, false);
            ctx.LineTo(poleTop + new Vector(1, 15), true, false);
        }
        if (flag.CanFreeze) flag.Freeze();
        dc.DrawGeometry(fill, pen, flag);
    }

    private static Point ClosestPointOnRect(Rect rect, Point point)
    {
        double x = Math.Clamp(point.X, rect.Left, rect.Right);
        double y = Math.Clamp(point.Y, rect.Top, rect.Bottom);
        double left = Math.Abs(point.X - rect.Left);
        double right = Math.Abs(point.X - rect.Right);
        double top = Math.Abs(point.Y - rect.Top);
        double bottom = Math.Abs(point.Y - rect.Bottom);
        double min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (min == left) x = rect.Left;
        else if (min == right) x = rect.Right;
        else if (min == top) y = rect.Top;
        else y = rect.Bottom;
        return new Point(x, y);
    }


    private void DrawParityAnchoredNote(DrawingContext dc, ChartDrawing drawing, Point anchor, Pen pen, Brush fill, Brush textBrush)
    {
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Note" : drawing.Text;
        FormattedText text = CreateDrawingText(value, drawing.Style, textBrush, drawing.Style.FontSize);
        const double pad = 8;
        Rect box = new(anchor.X + 14, anchor.Y - text.Height - pad * 2 - 12, Math.Max(90, text.Width + pad * 2), text.Height + pad * 2);
        dc.DrawRoundedRectangle(fill, pen, box, 4, 4);
        dc.DrawLine(pen, anchor, new Point(box.Left, box.Bottom - 6));
        dc.DrawEllipse(pen.Brush, null, anchor, 2.3, 2.3);
        dc.DrawText(text, new Point(box.Left + pad, box.Top + pad));
    }

    private static void DrawParityCycles(DrawingContext dc, Rect plot, ChartDrawing drawing, Point[] points, Pen pen)
    {
        // Cycle repetitions are viewport-virtualized: the two stored anchors define
        // the spacing, while only repetitions intersecting the current plot (plus a
        // one-period buffer) are generated on each render. Nothing is permanently
        // duplicated, so scrolling behaves like candle virtualization.
        double spacing = Math.Abs(points[1].X - points[0].X);
        if (spacing < 0.75 || plot.Width <= 0 || plot.Height <= 0)
            return;

        double originX = points[0].X;
        double horizontalReach = drawing.ToolId == "time-cycles" ? spacing / 2.0 : 0.0;
        double left = plot.Left - spacing - horizontalReach;
        double right = plot.Right + spacing + horizontalReach;
        long first = (long)Math.Floor((left - originX) / spacing);
        long last = (long)Math.Ceiling((right - originX) / spacing);

        // At TickLab's maximum horizontal zoom this is still far above the expected
        // visible repetition count, but protects rendering from pathological anchors.
        const long maxVisibleRepeats = 8192;
        if (last - first + 1 > maxVisibleRepeats)
        {
            long middle = (first + last) / 2;
            first = middle - maxVisibleRepeats / 2;
            last = first + maxVisibleRepeats - 1;
        }

        dc.PushClip(new RectangleGeometry(plot));
        if (drawing.ToolId == "time-cycles")
        {
            double radiusX = spacing / 2.0;
            double radiusY = Math.Max(4, Math.Min(radiusX, plot.Height / 2.0));
            for (long index = first; index <= last; index++)
            {
                double x = originX + index * spacing;
                if (x + radiusX < plot.Left || x - radiusX > plot.Right)
                    continue;
                dc.DrawEllipse(null, pen, new Point(x, points[0].Y), radiusX, radiusY);
            }
        }
        else
        {
            for (long index = first; index <= last; index++)
            {
                double x = originX + index * spacing;
                if (x < plot.Left - 0.5 || x > plot.Right + 0.5)
                    continue;
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
        }
        dc.Pop();
    }

    private static void DrawParitySine(DrawingContext dc, Rect plot, ChartDrawing drawing, Point p1, Point p2, Pen pen)
    {
        // The anchors define the wave period/amplitude, but the visible curve repeats
        // across the whole viewport. This is generated per-frame rather than stored as
        // thousands of points, so pan/zoom naturally adds and removes visible sections.
        double anchorSpan = p2.X - p1.X;
        if (Math.Abs(anchorSpan) < 0.75 || plot.Width <= 0)
            return;

        double amplitude = Math.Max(1, Math.Abs(p2.Y - p1.Y));
        int cycles = (int)Math.Clamp(ParityOption(drawing, "Cycles", 2), 1, 64);
        double period = Math.Abs(anchorSpan) / cycles;
        if (period < 0.75)
            return;

        double direction = Math.Sign(anchorSpan);
        if (direction == 0) direction = 1;
        double left = plot.Left - period;
        double right = plot.Right + period;
        int steps = Math.Clamp((int)Math.Ceiling((right - left) / 1.75), 160, 8192);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i <= steps; i++)
            {
                double x = left + (right - left) * (i / (double)steps);
                double phase = ((x - p1.X) / period) * Math.PI * 2.0 * direction;
                Point p = new(x, p1.Y + Math.Sin(phase) * amplitude);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, false);
            }
        }
        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    private void DrawParitySector(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen, Brush fill, Brush textBrush)
    {
        Point center = points[0];
        double radius = Distance(center, points[1]);
        if (radius < 1) return;
        double start = Math.Atan2(points[1].Y - center.Y, points[1].X - center.X);
        double end = Math.Atan2(points[2].Y - center.Y, points[2].X - center.X);
        double delta = end - start;
        while (delta < 0) delta += Math.PI * 2;
        while (delta >= Math.PI * 2) delta -= Math.PI * 2;
        Point arcEnd = new(center.X + Math.Cos(end) * radius, center.Y + Math.Sin(end) * radius);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(center, true, true);
            ctx.LineTo(points[1], true, false);
            ctx.ArcTo(arcEnd, new Size(radius, radius), 0, delta > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(fill, pen, geometry);
        if (ParityFlag(drawing, "ShowAngle", true))
            DrawParityLabel(dc, $"{delta * 180.0 / Math.PI:0.##}°", Mid(center, arcEnd), textBrush, drawing.Style, true);
        if (ParityFlag(drawing, "ShowRadius", true))
            DrawParityLabel(dc, $"r {radius:0.##} px", Mid(center, points[1]) + new Vector(4, -16), textBrush, drawing.Style, true);
    }

    private void DrawParityBarsPattern(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen)
    {
        IReadOnlyList<Point> pattern = GetBarsPatternProjectedPoints(drawing, layout);
        if (pattern.Count < 2)
            return;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(pattern[0], false, false);
            for (int i = 1; i < pattern.Count; i++)
                ctx.LineTo(pattern[i], true, false);
        }
        geometry.Freeze();

        var patternPen = new Pen(
            CreateDrawingBrush(drawing.Style.LineColor, drawing.Style.Opacity),
            Math.Max(1.5, pen.Thickness));
        patternPen.StartLineCap = PenLineCap.Round;
        patternPen.EndLineCap = PenLineCap.Round;
        patternPen.LineJoin = PenLineJoin.Round;
        if (patternPen.CanFreeze) patternPen.Freeze();

        dc.PushClip(new RectangleGeometry(layout.Plot));
        dc.DrawGeometry(null, patternPen, geometry);
        dc.Pop();

        if (_selectedDrawingIds.Contains(drawing.Id))
        {
            // TradingView shows hollow endpoint circles on the copied pattern and a
            // highlighted source-time range while the Bars Pattern is selected.
            var handlePen = new Pen(CreateDrawingBrush(drawing.Style.LineColor, 1.0), 1.6);
            dc.DrawEllipse(Brushes.White, handlePen, pattern[0], 7.0, 7.0);
            dc.DrawEllipse(Brushes.White, handlePen, pattern[^1], 7.0, 7.0);
            DrawBarsPatternSourceRange(dc, layout, drawing);
        }
    }

    private IReadOnlyList<Point> GetBarsPatternProjectedPoints(ChartDrawing drawing, ChartLayout layout)
    {
        if (DrawingCandles.Count == 0 || drawing.Anchors.Count < 3)
            return Array.Empty<Point>();

        int start = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[0]), 0, DrawingCandles.Count - 1);
        int end = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[1]), 0, DrawingCandles.Count - 1);
        if (start > end) (start, end) = (end, start);
        if (end - start < 1)
            return Array.Empty<Point>();

        bool mirror = ParityFlag(drawing, "Mirror", false);
        bool flip = ParityFlag(drawing, "Flip", false);
        int mode = (int)Math.Clamp(ParityOption(drawing, "Mode", 4), 0, 6);
        int firstValueIndex = mirror ? end : start;
        double sourceBase = BarsPatternSourceValue(DrawingCandles[firstValueIndex], mode);
        double destinationPrice = drawing.Anchors[2].Price;
        double destinationSlot = DrawingTimestampToTimelineSlot(drawing.Anchors[2].StartUnix);
        double sourceFirstSlot = GetCandleTimelineSlot(start);
        double sign = flip ? -1.0 : 1.0;
        var result = new List<Point>(end - start + 1);

        for (int n = 0; n <= end - start; n++)
        {
            int sourceIndex = mirror ? end - n : start + n;
            int spacingIndex = start + n;
            double sourceValue = BarsPatternSourceValue(DrawingCandles[sourceIndex], mode);
            double price = destinationPrice + (sourceValue - sourceBase) * sign;
            double slotOffset = GetCandleTimelineSlot(spacingIndex) - sourceFirstSlot;
            long timestamp = DrawingTimelineSlotToTimestamp(destinationSlot + slotOffset);
            result.Add(AnchorToPoint(new DrawingAnchor(timestamp, price), layout));
        }
        return result;
    }

    private static double BarsPatternSourceValue(Candle candle, int mode) => mode switch
    {
        1 => candle.Open,
        2 => candle.High,
        3 => candle.Low,
        5 => (candle.High + candle.Low) / 2.0,
        6 => (candle.High + candle.Low + candle.Close) / 3.0,
        _ => candle.Close
    };

    private void DrawBarsPatternSourceRange(DrawingContext dc, ChartLayout layout, ChartDrawing drawing)
    {
        if (DrawingCandles.Count == 0 || drawing.Anchors.Count < 2)
            return;
        int start = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[0]), 0, DrawingCandles.Count - 1);
        int end = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[1]), 0, DrawingCandles.Count - 1);
        if (start > end) (start, end) = (end, start);

        double x1 = AnchorToPoint(CreateDrawingAnchorAtIndex(start, drawing.Anchors[0].Price), layout).X;
        double x2 = AnchorToPoint(CreateDrawingAnchorAtIndex(end, drawing.Anchors[1].Price), layout).X;
        double left = Math.Max(layout.Plot.Left, Math.Min(x1, x2));
        double right = Math.Min(layout.Plot.Right, Math.Max(x1, x2));
        if (right <= left) return;

        double height = 24.0;
        double top = layout.Plot.Bottom - height;
        Brush rangeBrush = CreateDrawingBrush(drawing.Style.LineColor, 0.20);
        Brush labelBrush = CreateDrawingBrush(drawing.Style.LineColor, 0.92);
        dc.DrawRectangle(rangeBrush, null, new Rect(left, top, right - left, height));

        string startText = DrawingCandles[start].StartTime.ToLocalTime().ToString("dd MMM yy  HH:mm", CultureInfo.CurrentCulture);
        string endText = DrawingCandles[end].StartTime.ToLocalTime().ToString("dd MMM yy  HH:mm", CultureInfo.CurrentCulture);
        DrawBarsPatternTimeChip(dc, startText, left, top, labelBrush, alignRight: false, layout.Plot);
        DrawBarsPatternTimeChip(dc, endText, right, top, labelBrush, alignRight: true, layout.Plot);
    }

    private void DrawBarsPatternTimeChip(DrawingContext dc, string text, double x, double y, Brush background, bool alignRight, Rect plot)
    {
        FormattedText formatted = CreateText(text, 10.5, Brushes.White);
        double width = formatted.Width + 14.0;
        double left = alignRight ? x - width : x;
        left = Math.Clamp(left, plot.Left, Math.Max(plot.Left, plot.Right - width));
        Rect box = new(left, y, width, 24.0);
        dc.DrawRoundedRectangle(background, null, box, 3, 3);
        dc.DrawText(formatted, new Point(box.Left + 7, box.Top + 4));
    }

    private readonly record struct GhostFeedVisualBar(Point WickTop, Point WickBottom, Rect Body, bool Up);

    private IReadOnlyList<GhostFeedVisualBar> BuildGhostFeedVisualBars(ChartDrawing drawing, ChartLayout layout)
    {
        if (drawing.Anchors.Count < 2)
            return Array.Empty<GhostFeedVisualBar>();

        double chartRange = Math.Max(1e-12, layout.MaximumPrice - layout.MinimumPrice);
        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        double bodyWidth = Math.Clamp(slotWidth * 0.66, 2.0, 10.0);
        double previousClose = drawing.Anchors[0].Price;
        int ordinal = 0;
        var bars = new List<GhostFeedVisualBar>();

        for (int segment = 0; segment < drawing.Anchors.Count - 1 && bars.Count < 4096; segment++)
        {
            DrawingAnchor a = drawing.Anchors[segment];
            DrawingAnchor b = drawing.Anchors[segment + 1];
            double slotA = DrawingTimestampToTimelineSlot(a.StartUnix);
            double slotB = DrawingTimestampToTimelineSlot(b.StartUnix);
            double slotSpan = slotB - slotA;
            int steps = Math.Clamp((int)Math.Round(Math.Abs(slotSpan)), 1, 512);
            double delta = b.Price - a.Price;
            double wiggle = Math.Max(chartRange * 0.0012, Math.Abs(delta) * 0.035);

            for (int step = 1; step <= steps && bars.Count < 4096; step++)
            {
                double t = step / (double)steps;
                bool atAnchor = step == steps;
                double center = a.Price + delta * t;
                double oscillation = atAnchor ? 0.0 : wiggle *
                    (0.62 * Math.Sin((ordinal + 1) * 1.73 + segment * 0.91) +
                     0.38 * Math.Sin((ordinal + 1) * 0.67 + segment * 1.37));
                double close = center + oscillation;
                double open = previousClose;
                double wickBase = Math.Max(chartRange * 0.00065, Math.Abs(close - open) * 0.30);
                double wickVariation = 0.65 + 0.45 * Math.Abs(Math.Sin((ordinal + 2) * 1.19));
                double wick = wickBase * wickVariation;
                double high = Math.Max(open, close) + wick;
                double low = Math.Min(open, close) - wick * (0.75 + 0.25 * Math.Abs(Math.Sin((ordinal + 3) * 0.83)));
                double slot = slotA + slotSpan * t;
                long timestamp = DrawingTimelineSlotToTimestamp(slot);
                double x = AnchorToPoint(new DrawingAnchor(timestamp, close), layout).X;
                double yOpen = PriceToY(open, layout);
                double yClose = PriceToY(close, layout);
                double yHigh = PriceToY(high, layout);
                double yLow = PriceToY(low, layout);
                Rect body = new(
                    x - bodyWidth / 2.0,
                    Math.Min(yOpen, yClose),
                    bodyWidth,
                    Math.Max(1.25, Math.Abs(yClose - yOpen)));
                bars.Add(new GhostFeedVisualBar(
                    new Point(x, yHigh),
                    new Point(x, yLow),
                    body,
                    close >= open));
                previousClose = close;
                ordinal++;
            }
        }
        return bars;
    }

    private void DrawParityGhostFeed(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen)
    {
        IReadOnlyList<GhostFeedVisualBar> bars = BuildGhostFeedVisualBars(drawing, layout);
        if (bars.Count == 0)
            return;

        double alpha = Math.Clamp(ParityOption(drawing, "Opacity", 0.38), 0.05, 1.0) * drawing.Style.Opacity;
        var upPen = new Pen(CreateDrawingBrush("#089981", Math.Min(1.0, alpha * 1.75)), Math.Max(0.9, pen.Thickness * 0.80));
        var downPen = new Pen(CreateDrawingBrush("#F05261", Math.Min(1.0, alpha * 1.75)), Math.Max(0.9, pen.Thickness * 0.80));
        Brush upFill = CreateDrawingBrush("#089981", Math.Min(0.42, alpha * 0.42));
        Brush downFill = CreateDrawingBrush("#F05261", Math.Min(0.46, alpha * 0.46));

        dc.PushClip(new RectangleGeometry(layout.Plot));
        foreach (GhostFeedVisualBar bar in bars)
        {
            if (bar.Body.Right < layout.Plot.Left - 4 || bar.Body.Left > layout.Plot.Right + 4)
                continue;
            Pen candlePen = bar.Up ? upPen : downPen;
            Brush candleFill = bar.Up ? upFill : downFill;
            dc.DrawLine(candlePen, bar.WickTop, bar.WickBottom);
            dc.DrawRectangle(candleFill, candlePen, bar.Body);
        }
        dc.Pop();
    }

    private sealed record ParityVolumeProfileRowVisual(
        Rect UpRect,
        Rect DownRect,
        Rect TotalRect,
        double TotalVolume,
        bool InValueArea);

    private sealed record ParityVolumeProfileVisual(
        IReadOnlyList<ParityVolumeProfileRowVisual> Rows,
        Rect HistogramBounds,
        double GuideLeft,
        double GuideRight,
        double PocY,
        double VahY,
        double ValY,
        IReadOnlyList<Point> DevelopingPoc,
        IReadOnlyList<Point> DevelopingVah,
        IReadOnlyList<Point> DevelopingVal);

    private ParityVolumeProfileVisual? BuildParityVolumeProfileVisual(ChartLayout layout, ChartDrawing drawing)
    {
        if (DrawingCandles.Count == 0 || drawing.Anchors.Count == 0)
            return null;

        bool anchored = drawing.ToolId == "anchored-volume-profile";
        int start = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[0]), 0, DrawingCandles.Count - 1);
        int end;
        if (!anchored && drawing.Anchors.Count >= 2)
        {
            end = Math.Clamp(FindNearestDrawingCandleIndex(drawing.Anchors[1]), 0, DrawingCandles.Count - 1);
            if (start > end) (start, end) = (end, start);
        }
        else
        {
            end = DrawingCandles.Count - 1;
        }
        if (end < start)
            return null;

        double rowSize = Math.Max(1, ParityOption(drawing, "RowSize", ParityOption(drawing, "Rows", 24)));
        int rows;
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = start; i <= end; i++)
        {
            min = Math.Min(min, DrawingCandles[i].Low);
            max = Math.Max(max, DrawingCandles[i].High);
        }
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
            return null;

        bool ticksPerRow = ParityOption(drawing, "RowsLayout", 0) >= 0.5;
        if (ticksPerRow)
        {
            int digits = Math.Clamp(DrawingCandles[start].Digits, 0, 10);
            double tickSize = Math.Pow(10, -digits);
            double pricePerRow = Math.Max(tickSize, tickSize * rowSize);
            rows = (int)Math.Ceiling((max - min) / pricePerRow);
        }
        else
        {
            rows = (int)Math.Round(rowSize);
        }
        // Keep the profile bounded for smooth chart interaction while preserving
        // TradingView-style Number-of-Rows / Ticks-per-Row semantics.
        rows = Math.Clamp(rows, 8, 400);

        double[] up = new double[rows];
        double[] down = new double[rows];
        void AddCandleToBins(Candle c, double[] targetUp, double[] targetDown)
        {
            double volume = Math.Max(1, c.RealVolume > 0 ? c.RealVolume : c.TickVolume);
            int from = Math.Clamp((int)Math.Floor((c.Low - min) / (max - min) * rows), 0, rows - 1);
            int to = Math.Clamp((int)Math.Floor((c.High - min) / (max - min) * rows), 0, rows - 1);
            if (to < from) (from, to) = (to, from);
            int span = Math.Max(1, to - from + 1);
            double share = volume / span;
            bool bullish = c.Close >= c.Open;
            for (int r = from; r <= to; r++)
            {
                if (bullish) targetUp[r] += share;
                else targetDown[r] += share;
            }
        }

        for (int i = start; i <= end; i++)
            AddCandleToBins(DrawingCandles[i], up, down);

        double[] total = up.Zip(down, (u, d) => u + d).ToArray();
        double maximum = Math.Max(1, total.Max());
        int poc = Array.IndexOf(total, total.Max());
        int valueLow = poc;
        int valueHigh = poc;
        double targetValueArea = total.Sum() * Math.Clamp(ParityOption(drawing, "ValueAreaPercent", 70), 1, 100) / 100.0;
        double accumulated = total[poc];
        while (accumulated < targetValueArea && (valueLow > 0 || valueHigh < rows - 1))
        {
            double below = valueLow > 0 ? total[valueLow - 1] : -1;
            double above = valueHigh < rows - 1 ? total[valueHigh + 1] : -1;
            if (above >= below && valueHigh < rows - 1) accumulated += total[++valueHigh];
            else if (valueLow > 0) accumulated += total[--valueLow];
            else break;
        }

        double firstX = AnchorToPoint(drawing.Anchors[0], layout).X;
        double secondX = !anchored && drawing.Anchors.Count >= 2
            ? AnchorToPoint(drawing.Anchors[1], layout).X
            : layout.Plot.Right;
        double rangeLeft = Math.Clamp(Math.Min(firstX, secondX), layout.Plot.Left, layout.Plot.Right);
        double rangeRight = Math.Clamp(Math.Max(firstX, secondX), layout.Plot.Left, layout.Plot.Right);
        if (rangeRight - rangeLeft < 20)
            rangeRight = Math.Min(layout.Plot.Right, rangeLeft + Math.Max(20, layout.Plot.Width * 0.12));

        double widthPercent = Math.Clamp(ParityOption(drawing, "WidthPercent", 30), 5, 95) / 100.0;
        double profileMaxWidth = Math.Max(24, (rangeRight - rangeLeft) * widthPercent);
        bool leftPlacement = ParityOption(drawing, "Placement", anchored ? 1 : -1) < 0;
        double profileLeft = leftPlacement ? rangeLeft : Math.Max(rangeLeft, rangeRight - profileMaxWidth);
        double profileRight = leftPlacement ? Math.Min(rangeRight, rangeLeft + profileMaxWidth) : rangeRight;
        double availableWidth = Math.Max(1, profileRight - profileLeft);
        bool upDown = ParityOption(drawing, "VolumeMode", ParityFlag(drawing, "UpDownVolume", true) ? 0 : 1) < 0.5;

        var visualRows = new List<ParityVolumeProfileRowVisual>(rows);
        for (int r = 0; r < rows; r++)
        {
            double pLow = min + (max - min) * r / rows;
            double pHigh = min + (max - min) * (r + 1) / rows;
            double y1 = PriceToY(pHigh, layout);
            double y2 = PriceToY(pLow, layout);
            double rowHeight = Math.Max(1, Math.Abs(y2 - y1));
            double width = availableWidth * total[r] / maximum;
            double left = leftPlacement ? profileLeft : profileRight - width;
            Rect totalRect = new(left, Math.Min(y1, y2), width, rowHeight);

            Rect upRect;
            Rect downRect;
            if (upDown && total[r] > 0)
            {
                double upWidth = width * up[r] / total[r];
                if (leftPlacement)
                {
                    upRect = new Rect(left, totalRect.Top, upWidth, rowHeight);
                    downRect = new Rect(left + upWidth, totalRect.Top, Math.Max(0, width - upWidth), rowHeight);
                }
                else
                {
                    downRect = new Rect(left, totalRect.Top, Math.Max(0, width - upWidth), rowHeight);
                    upRect = new Rect(left + Math.Max(0, width - upWidth), totalRect.Top, upWidth, rowHeight);
                }
            }
            else
            {
                upRect = totalRect;
                downRect = Rect.Empty;
            }
            visualRows.Add(new ParityVolumeProfileRowVisual(upRect, downRect, totalRect, total[r], r >= valueLow && r <= valueHigh));
        }

        double pocPrice = min + (max - min) * (poc + 0.5) / rows;
        double valPrice = min + (max - min) * valueLow / rows;
        double vahPrice = min + (max - min) * (valueHigh + 1) / rows;
        double guideLeft = rangeLeft;
        double guideRight = ParityFlag(drawing, "ExtendRight", false) ? layout.Plot.Right : rangeRight;

        var developingPoc = new List<Point>();
        var developingVah = new List<Point>();
        var developingVal = new List<Point>();
        if (ParityFlag(drawing, "ShowDevelopingPOC", false) || ParityFlag(drawing, "ShowDevelopingVA", false))
        {
            double[] cu = new double[rows];
            double[] cd = new double[rows];
            int stride = Math.Max(1, (end - start + 1) / 600);
            for (int i = start; i <= end; i++)
            {
                AddCandleToBins(DrawingCandles[i], cu, cd);
                if ((i - start) % stride != 0 && i != end)
                    continue;
                double[] ct = cu.Zip(cd, (u, d) => u + d).ToArray();
                int cpoc = Array.IndexOf(ct, ct.Max());
                int clow = cpoc, chigh = cpoc;
                double ctarget = ct.Sum() * Math.Clamp(ParityOption(drawing, "ValueAreaPercent", 70), 1, 100) / 100.0;
                double cacc = ct[cpoc];
                while (cacc < ctarget && (clow > 0 || chigh < rows - 1))
                {
                    double below = clow > 0 ? ct[clow - 1] : -1;
                    double above = chigh < rows - 1 ? ct[chigh + 1] : -1;
                    if (above >= below && chigh < rows - 1) cacc += ct[++chigh];
                    else if (clow > 0) cacc += ct[--clow];
                    else break;
                }
                long t = DrawingCandles[i].StartUnix;
                developingPoc.Add(AnchorToPoint(new DrawingAnchor(t, min + (max - min) * (cpoc + 0.5) / rows), layout));
                developingVah.Add(AnchorToPoint(new DrawingAnchor(t, min + (max - min) * (chigh + 1) / rows), layout));
                developingVal.Add(AnchorToPoint(new DrawingAnchor(t, min + (max - min) * clow / rows), layout));
            }
        }

        Rect histogramBounds = new(
            profileLeft,
            Math.Min(PriceToY(max, layout), PriceToY(min, layout)),
            Math.Max(1, profileRight - profileLeft),
            Math.Max(1, Math.Abs(PriceToY(min, layout) - PriceToY(max, layout))));

        return new ParityVolumeProfileVisual(
            visualRows,
            histogramBounds,
            guideLeft,
            guideRight,
            PriceToY(pocPrice, layout),
            PriceToY(vahPrice, layout),
            PriceToY(valPrice, layout),
            developingPoc,
            developingVah,
            developingVal);
    }

    private static DrawingLevel ResolveVolumeProfileLevel(ChartDrawing drawing, string role, int fallbackIndex)
    {
        IReadOnlyList<DrawingLevel> defaults = DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        DrawingLevel fallback = defaults.FirstOrDefault(level => string.Equals(level.Label, role, StringComparison.OrdinalIgnoreCase))
            ?? (fallbackIndex >= 0 && fallbackIndex < defaults.Count ? defaults[fallbackIndex] : new DrawingLevel(0, role));
        if (drawing.Levels.Count == 0)
            return fallback;

        DrawingLevel? exact = drawing.Levels.FirstOrDefault(level => string.Equals(level.Label, role, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        DrawingLevel? legacy = role switch
        {
            "Up Volume" => drawing.Levels.ElementAtOrDefault(0),
            "Down Volume" => drawing.Levels.ElementAtOrDefault(1),
            "POC" => drawing.Levels.FirstOrDefault(level => level.Label.Equals("POC", StringComparison.OrdinalIgnoreCase)) ?? drawing.Levels.ElementAtOrDefault(2),
            "Value Area Up" or "Value Area Down" or "VAH" or "VAL" => drawing.Levels.FirstOrDefault(level => level.Label.Contains("Value area", StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
        if (legacy is null)
            return fallback;
        return fallback with
        {
            Enabled = role is "Developing POC" or "Developing VA" or "Histogram Box" ? fallback.Enabled : legacy.Enabled,
            Color = string.IsNullOrWhiteSpace(legacy.Color) ? fallback.Color : legacy.Color,
            FillColor = string.IsNullOrWhiteSpace(legacy.FillColor) ? fallback.FillColor : legacy.FillColor,
            FillOpacity = legacy.FillOpacity >= 0 ? legacy.FillOpacity : fallback.FillOpacity,
            Width = legacy.Width > 0 ? legacy.Width : fallback.Width,
            LineStyle = legacy.LineStyle
        };
    }

    private static Pen VolumeProfileRolePen(ChartDrawing drawing, DrawingLevel level)
    {
        double alpha = level.FillOpacity >= 0 ? level.FillOpacity : 1.0;
        var result = new Pen(CreateDrawingBrush(level.Color, drawing.Style.Opacity * Math.Clamp(alpha, 0, 1)), Math.Clamp(level.Width, 0.5, 20));
        result.DashStyle = level.LineStyle switch
        {
            DrawingLineStyle.Dashed => DashStyles.Dash,
            DrawingLineStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };
        return result;
    }

    private static Brush VolumeProfileRoleBrush(ChartDrawing drawing, DrawingLevel level, string fallback)
    {
        string color = string.IsNullOrWhiteSpace(level.FillColor) ? (string.IsNullOrWhiteSpace(level.Color) ? fallback : level.Color) : level.FillColor;
        double alpha = level.FillOpacity >= 0 ? level.FillOpacity : drawing.Style.FillOpacity;
        return CreateDrawingBrush(color, drawing.Style.Opacity * Math.Clamp(alpha, 0, 1));
    }

    private void DrawParityVolumeProfile(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        ParityVolumeProfileVisual? visual = BuildParityVolumeProfileVisual(layout, drawing);
        if (visual is null)
            return;

        DrawingLevel upLevel = ResolveVolumeProfileLevel(drawing, "Up Volume", 0);
        DrawingLevel downLevel = ResolveVolumeProfileLevel(drawing, "Down Volume", 1);
        DrawingLevel valueUpLevel = ResolveVolumeProfileLevel(drawing, "Value Area Up", 2);
        DrawingLevel valueDownLevel = ResolveVolumeProfileLevel(drawing, "Value Area Down", 3);
        DrawingLevel vahLevel = ResolveVolumeProfileLevel(drawing, "VAH", 4);
        DrawingLevel valLevel = ResolveVolumeProfileLevel(drawing, "VAL", 5);
        DrawingLevel pocLevel = ResolveVolumeProfileLevel(drawing, "POC", 6);
        DrawingLevel developingPocLevel = ResolveVolumeProfileLevel(drawing, "Developing POC", 7);
        DrawingLevel developingVaLevel = ResolveVolumeProfileLevel(drawing, "Developing VA", 8);
        DrawingLevel histogramLevel = ResolveVolumeProfileLevel(drawing, "Histogram Box", 9);

        Brush upBrush = VolumeProfileRoleBrush(drawing, upLevel, "#6B6CCB");
        Brush downBrush = VolumeProfileRoleBrush(drawing, downLevel, "#D85A78");
        Brush valueUpBrush = VolumeProfileRoleBrush(drawing, valueUpLevel, "#22B8A7");
        Brush valueDownBrush = VolumeProfileRoleBrush(drawing, valueDownLevel, "#D84B91");
        bool showProfile = ParityFlag(drawing, "ShowProfile", true);
        bool showValues = ParityFlag(drawing, "ShowValues", false);
        bool upDown = ParityOption(drawing, "VolumeMode", ParityFlag(drawing, "UpDownVolume", true) ? 0 : 1) < 0.5;

        dc.PushClip(new RectangleGeometry(layout.Plot));
        if (showProfile)
        {
            foreach (ParityVolumeProfileRowVisual row in visual.Rows)
            {
                if (row.TotalRect.Width <= 0 || row.TotalRect.Height <= 0)
                    continue;
                if (upDown)
                {
                    bool showUp = row.InValueArea ? valueUpLevel.Enabled : upLevel.Enabled;
                    bool showDown = row.InValueArea ? valueDownLevel.Enabled : downLevel.Enabled;
                    if (showUp && row.UpRect.Width > 0)
                        dc.DrawRectangle(row.InValueArea ? valueUpBrush : upBrush, null, row.UpRect);
                    if (showDown && row.DownRect.Width > 0)
                        dc.DrawRectangle(row.InValueArea ? valueDownBrush : downBrush, null, row.DownRect);
                }
                else
                {
                    bool showTotal = row.InValueArea ? valueUpLevel.Enabled : upLevel.Enabled;
                    if (showTotal)
                        dc.DrawRectangle(row.InValueArea ? valueUpBrush : upBrush, null, row.TotalRect);
                }

                if (showValues && row.TotalRect.Width >= 36 && row.TotalRect.Height >= 9)
                {
                    string label = FormatParityCompactVolume((long)Math.Round(row.TotalVolume));
                    Point at = new(row.TotalRect.Right + 3, row.TotalRect.Top + Math.Max(0, row.TotalRect.Height / 2 - 6));
                    DrawSmallLabel(dc, label, at, CreateDrawingBrush(drawing.Style.TextColor, drawing.Style.Opacity * Math.Clamp(ParityOption(drawing, "ValuesOpacity", 0.92), 0, 1)));
                }
            }
        }

        if (ParityFlag(drawing, "ShowVAH", ParityFlag(drawing, "ShowValueArea", true)) && vahLevel.Enabled)
            dc.DrawLine(VolumeProfileRolePen(drawing, vahLevel), new Point(visual.GuideLeft, visual.VahY), new Point(visual.GuideRight, visual.VahY));
        if (ParityFlag(drawing, "ShowVAL", ParityFlag(drawing, "ShowValueArea", true)) && valLevel.Enabled)
            dc.DrawLine(VolumeProfileRolePen(drawing, valLevel), new Point(visual.GuideLeft, visual.ValY), new Point(visual.GuideRight, visual.ValY));
        if (ParityFlag(drawing, "ShowPOC", true) && pocLevel.Enabled)
            dc.DrawLine(VolumeProfileRolePen(drawing, pocLevel), new Point(visual.GuideLeft, visual.PocY), new Point(visual.GuideRight, visual.PocY));

        if (ParityFlag(drawing, "ShowDevelopingPOC", false) && developingPocLevel.Enabled && visual.DevelopingPoc.Count > 1)
            dc.DrawGeometry(null, VolumeProfileRolePen(drawing, developingPocLevel), CreateParityPolyline(visual.DevelopingPoc));
        if (ParityFlag(drawing, "ShowDevelopingVA", false) && developingVaLevel.Enabled)
        {
            Pen devVaPen = VolumeProfileRolePen(drawing, developingVaLevel);
            if (visual.DevelopingVah.Count > 1)
                dc.DrawGeometry(null, devVaPen, CreateParityPolyline(visual.DevelopingVah));
            if (visual.DevelopingVal.Count > 1)
                dc.DrawGeometry(null, devVaPen, CreateParityPolyline(visual.DevelopingVal));
        }
        if (ParityFlag(drawing, "ShowHistogramBox", false) && histogramLevel.Enabled)
            dc.DrawRectangle(null, VolumeProfileRolePen(drawing, histogramLevel), visual.HistogramBounds);
        dc.Pop();
    }
}
