using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TickLab.Core.Drawing;

/// <summary>
/// TickLab-owned vector artwork for drawing tools.  It deliberately avoids
/// copying third-party icon assets while keeping the compact professional
/// grammar traders expect from a chart drawing rail.
/// </summary>
public static class DrawingToolIconFactory
{
    private const double DesignSize = 24;

    public static FrameworkElement CreateToolIcon(string? toolId, double size = 20, Brush? brush = null)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(toolId);
        return CreateToolIcon(tool, size, brush);
    }

    public static FrameworkElement CreateToolIcon(DrawingToolDefinition? tool, double size = 20, Brush? brush = null)
    {
        Brush stroke = brush ?? new SolidColorBrush(Color.FromRgb(203, 213, 225));
        var canvas = NewCanvas(size, normalizeToolStroke: true);
        if (tool is null)
        {
            AddLine(canvas, 5, 19, 19, 5, stroke, 1.8);
            FinalizeCanvasQuality(canvas);
            return canvas;
        }

        if (TryDrawSpecificToolIcon(canvas, tool, stroke))
        {
            FinalizeCanvasQuality(canvas);
            return canvas;
        }

        switch (tool.Geometry)
        {
            case DrawingGeometryKind.Cursor:
                AddPath(canvas, "M5,3 L17,14 L12,15 L15,21 L12,22 L9,16 L5,19 Z", stroke, 1.5, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Eraser:
                AddPath(canvas, "M6,16 L14,8 L20,14 L12,22 L6,22 L3,19 Z", stroke, 1.7, Brushes.Transparent);
                AddLine(canvas, 8, 14, 14, 20, stroke, 1.2);
                break;
            case DrawingGeometryKind.Line:
            case DrawingGeometryKind.Ray:
            case DrawingGeometryKind.ExtendedLine:
            case DrawingGeometryKind.ArrowLine:
                AddLine(canvas, 4, 19, 20, 5, stroke, 1.8);
                AddCircle(canvas, 4, 19, 2.1, stroke, Brushes.Transparent, 1.4);
                if (tool.Geometry == DrawingGeometryKind.ArrowLine)
                {
                    AddLine(canvas, 20, 5, 15, 6, stroke, 1.5);
                    AddLine(canvas, 20, 5, 19, 10, stroke, 1.5);
                }
                else
                {
                    AddCircle(canvas, 20, 5, 2.1, stroke, Brushes.Transparent, 1.4);
                }
                break;
            case DrawingGeometryKind.HorizontalLine:
            case DrawingGeometryKind.HorizontalRay:
                AddLine(canvas, 3, 12, 21, 12, stroke, 1.8);
                AddCircle(canvas, 8, 12, 2, stroke, Brushes.Transparent, 1.3);
                break;
            case DrawingGeometryKind.VerticalLine:
                AddLine(canvas, 12, 3, 12, 21, stroke, 1.8);
                AddCircle(canvas, 12, 10, 2, stroke, Brushes.Transparent, 1.3);
                break;
            case DrawingGeometryKind.CrossLine:
                AddLine(canvas, 3, 12, 21, 12, stroke, 1.5);
                AddLine(canvas, 12, 3, 12, 21, stroke, 1.5);
                AddCircle(canvas, 12, 12, 2, stroke, Brushes.Transparent, 1.2);
                break;
            case DrawingGeometryKind.Channel:
            case DrawingGeometryKind.Regression:
                AddLine(canvas, 4, 17, 19, 7, stroke, 1.5);
                AddLine(canvas, 6, 21, 21, 11, stroke, 1.5);
                AddLine(canvas, 3, 13, 18, 3, stroke, 1.0);
                break;
            case DrawingGeometryKind.AnchoredVwap:
                AddPolyline(canvas, new[] { new Point(3,17), new Point(7,13), new Point(11,15), new Point(15,8), new Point(21,10) }, stroke, 1.7);
                AddCircle(canvas, 3, 17, 2, stroke, Brushes.Transparent, 1.2);
                break;
            case DrawingGeometryKind.Fibonacci:
            case DrawingGeometryKind.FibonacciExtension:
            case DrawingGeometryKind.FibonacciChannel:
            case DrawingGeometryKind.FibonacciTime:
            case DrawingGeometryKind.FibonacciFan:
            case DrawingGeometryKind.FibonacciCircles:
            case DrawingGeometryKind.FibonacciSpiral:
            case DrawingGeometryKind.FibonacciWedge:
            case DrawingGeometryKind.FibonacciArcs:
                AddLine(canvas, 4, 5, 20, 5, stroke, 1.2);
                AddLine(canvas, 4, 9, 17, 9, stroke, 1.2);
                AddLine(canvas, 4, 13, 20, 13, stroke, 1.2);
                AddLine(canvas, 4, 18, 15, 18, stroke, 1.2);
                AddLine(canvas, 4, 3, 4, 21, stroke, 1.5);
                break;
            case DrawingGeometryKind.Pitchfork:
                AddLine(canvas, 4, 20, 19, 5, stroke, 1.6);
                AddLine(canvas, 7, 20, 21, 8, stroke, 1.1);
                AddLine(canvas, 3, 16, 16, 3, stroke, 1.1);
                break;
            case DrawingGeometryKind.GannBox:
                AddRect(canvas, 4, 4, 16, 16, stroke, Brushes.Transparent, 1.4, 1.5);
                AddLine(canvas, 4, 20, 20, 4, stroke, 1.1);
                AddLine(canvas, 4, 12, 20, 12, stroke, 0.9);
                AddLine(canvas, 12, 4, 12, 20, stroke, 0.9);
                break;
            case DrawingGeometryKind.GannFan:
                AddLine(canvas, 4, 20, 20, 4, stroke, 1.6);
                AddLine(canvas, 4, 20, 20, 10, stroke, 1.0);
                AddLine(canvas, 4, 20, 15, 3, stroke, 1.0);
                break;
            case DrawingGeometryKind.Brush:
            case DrawingGeometryKind.Highlighter:
                AddPath(canvas, "M3,17 C6,6 11,21 15,10 C17,5 20,7 21,3", stroke, tool.Geometry == DrawingGeometryKind.Highlighter ? 3.8 : 1.8, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Rectangle:
            case DrawingGeometryKind.RotatedRectangle:
                if (tool.Geometry == DrawingGeometryKind.RotatedRectangle)
                    AddPath(canvas, "M12,3 L21,12 L12,21 L3,12 Z", stroke, 1.5, Brushes.Transparent);
                else
                    AddRect(canvas, 4, 5, 16, 14, stroke, Brushes.Transparent, 1.5, 2.0);
                break;
            case DrawingGeometryKind.Ellipse:
                AddEllipse(canvas, 3, 6, 18, 12, stroke, Brushes.Transparent, 1.5);
                break;
            case DrawingGeometryKind.Triangle:
                AddPath(canvas, "M12,3 L21,20 L3,20 Z", stroke, 1.5, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Polyline:
            case DrawingGeometryKind.Curve:
            case DrawingGeometryKind.DoubleCurve:
            case DrawingGeometryKind.Arc:
                AddPath(canvas, "M3,18 C7,4 14,21 21,6", stroke, 1.7, Brushes.Transparent);
                if (tool.Geometry == DrawingGeometryKind.DoubleCurve)
                    AddPath(canvas, "M3,14 C8,1 15,18 21,3", stroke, 1.0, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Text:
            case DrawingGeometryKind.Note:
            case DrawingGeometryKind.Callout:
            case DrawingGeometryKind.PriceLabel:
                AddText(canvas, tool.Geometry == DrawingGeometryKind.Text ? "T" : tool.Geometry == DrawingGeometryKind.PriceLabel ? "$" : "Aa", stroke, tool.Geometry == DrawingGeometryKind.Text ? 16 : 11, FontWeights.SemiBold);
                break;
            case DrawingGeometryKind.ArrowMarker:
                if (tool.Id.Contains("down", StringComparison.OrdinalIgnoreCase))
                {
                    // Folder-5 parity: Arrow mark down must actually point down in
                    // the palette instead of reusing the upward marker artwork.
                    AddPath(canvas, "M12,21 L4,12 L9,12 L9,3 L15,3 L15,12 L20,12 Z", stroke, 1.5, Brushes.Transparent);
                }
                else if (tool.Id.Contains("left", StringComparison.OrdinalIgnoreCase))
                {
                    AddPath(canvas, "M3,12 L12,4 L12,9 L21,9 L21,15 L12,15 L12,20 Z", stroke, 1.5, Brushes.Transparent);
                }
                else if (tool.Id.Contains("right", StringComparison.OrdinalIgnoreCase))
                {
                    AddPath(canvas, "M21,12 L12,4 L12,9 L3,9 L3,15 L12,15 L12,20 Z", stroke, 1.5, Brushes.Transparent);
                }
                else if (tool.Id == "arrow-marker")
                {
                    // Generic Arrow Marker is a rotatable/scalable two-point arrow.
                    AddPath(canvas, "M3,20 L8,9 L11,12 L20,3 L21,4 L12,13 L15,16 Z", stroke, 1.2, stroke);
                }
                else
                {
                    AddPath(canvas, "M12,3 L20,12 L15,12 L15,21 L9,21 L9,12 L4,12 Z", stroke, 1.5, Brushes.Transparent);
                }
                break;
            case DrawingGeometryKind.Flag:
                AddPath(canvas, "M6,3 L6,21 M7,4 L19,7 L7,11 Z", stroke, 1.5, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Pattern:
                AddAngularPolyline(canvas, new[] { new Point(3,17), new Point(7,7), new Point(11,14), new Point(16,5), new Point(21,12) }, stroke, 1.0);
                foreach (Point p in new[] { new Point(3,17), new Point(7,7), new Point(11,14), new Point(16,5), new Point(21,12) })
                    AddCircle(canvas, p.X, p.Y, 1.05, stroke, Brushes.Transparent, 1.0);
                break;
            case DrawingGeometryKind.Cycles:
                AddLine(canvas, 5, 3, 5, 21, stroke, 1.1);
                AddLine(canvas, 12, 3, 12, 21, stroke, 1.1);
                AddLine(canvas, 19, 3, 19, 21, stroke, 1.1);
                break;
            case DrawingGeometryKind.Sine:
                AddPath(canvas, "M2,12 C5,3 9,3 12,12 C15,21 19,21 22,12", stroke, 1.7, Brushes.Transparent);
                break;
            case DrawingGeometryKind.Position:
                AddRect(canvas, 4, 4, 16, 7, stroke, new SolidColorBrush(Color.FromArgb(45, 34, 197, 94)), 1.2, 1.5);
                AddRect(canvas, 4, 13, 16, 7, stroke, new SolidColorBrush(Color.FromArgb(45, 239, 68, 68)), 1.2, 1.5);
                AddLine(canvas, 4, 12, 20, 12, stroke, 1.2);
                break;
            case DrawingGeometryKind.Range:
                AddLine(canvas, 5, 5, 19, 19, stroke, 1.4);
                AddLine(canvas, 5, 5, 10, 5, stroke, 1.2);
                AddLine(canvas, 5, 5, 5, 10, stroke, 1.2);
                AddLine(canvas, 19, 19, 14, 19, stroke, 1.2);
                AddLine(canvas, 19, 19, 19, 14, stroke, 1.2);
                break;
            case DrawingGeometryKind.BarsPattern:
            case DrawingGeometryKind.GhostFeed:
                AddAngularPolyline(canvas, new[] { new Point(3,17), new Point(6,10), new Point(9,14), new Point(12,5), new Point(16,12), new Point(21,7) }, stroke, 1.0);
                break;
            case DrawingGeometryKind.Sector:
                AddPath(canvas, "M4,20 L4,4 A16,16 0 0 1 20,20 Z", stroke, 1.4, Brushes.Transparent);
                break;
            case DrawingGeometryKind.VolumeProfile:
                AddRect(canvas, 4, 5, 5, 3, stroke, stroke, 0, 0);
                AddRect(canvas, 4, 10, 12, 3, stroke, stroke, 0, 0);
                AddRect(canvas, 4, 15, 16, 3, stroke, stroke, 0, 0);
                break;
            case DrawingGeometryKind.Icon:
                AddCircle(canvas, 12, 12, 8.5, stroke, Brushes.Transparent, 1.4);
                AddCircle(canvas, 9, 10, 0.9, stroke, stroke, 0);
                AddCircle(canvas, 15, 10, 0.9, stroke, stroke, 0);
                AddPath(canvas, "M8,15 C10,18 14,18 16,15", stroke, 1.2, Brushes.Transparent);
                break;
            default:
                AddLine(canvas, 5, 19, 19, 5, stroke, 1.8);
                break;
        }
        FinalizeCanvasQuality(canvas);
        return canvas;
    }

    private static bool TryDrawSpecificToolIcon(Canvas canvas, DrawingToolDefinition tool, Brush stroke)
    {
        switch (tool.Id)
        {
            case "cursor-crosshair":
                // TradingView reference: a clean thin cross, without the extra center ring.
                AddLine(canvas, 4, 12, 20, 12, stroke, 1.15);
                AddLine(canvas, 12, 4, 12, 20, stroke, 1.15);
                return true;
            case "cursor-dot":
                // Reference flyout uses a simple filled dot.
                AddCircle(canvas, 12, 12, 2.45, stroke, stroke, 0);
                return true;
            case "cursor-arrow":
                AddPath(canvas, "M5,3 L18,14 L12.5,15 L15.5,21 L12.2,22.3 L9.3,16.2 L5,19.4 Z", stroke, 1.35, Brushes.Transparent);
                return true;
            case "cursor-demo":
                AddCircle(canvas, 12, 12, 8.1, stroke, Brushes.Transparent, 1.2);
                AddPath(canvas, "M10,7.7 L17,12 L10,16.3 Z", stroke, 1.15, Brushes.Transparent);
                return true;
            case "cursor-magic":
                AddPath(canvas, "M5,20 L15.6,9.4 L18.7,12.5 L8.1,23 Z", stroke, 1.35, Brushes.Transparent);
                AddPath(canvas, "M17,1.8 L18.2,5.1 L21.5,6.3 L18.2,7.5 L17,10.8 L15.8,7.5 L12.5,6.3 L15.8,5.1 Z", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M7,4 L7.7,5.8 L9.5,6.5 L7.7,7.2 L7,9 L6.3,7.2 L4.5,6.5 L6.3,5.8 Z", stroke, .85, Brushes.Transparent);
                return true;
            case "eraser":
                AddPath(canvas, "M5,15.5 L13.5,7 L20,13.5 L11.5,22 L5.8,22 L2,18.2 Z", stroke, 1.45, Brushes.Transparent);
                AddLine(canvas, 8.4, 12.1, 15.1, 18.8, stroke, 1.05);
                return true;
            case "selection":
                AddLine(canvas, 4, 4, 9, 4, stroke, 1.2); AddLine(canvas, 4, 4, 4, 9, stroke, 1.2);
                AddLine(canvas, 20, 4, 15, 4, stroke, 1.2); AddLine(canvas, 20, 4, 20, 9, stroke, 1.2);
                AddLine(canvas, 4, 20, 9, 20, stroke, 1.2); AddLine(canvas, 4, 20, 4, 15, stroke, 1.2);
                AddLine(canvas, 20, 20, 15, 20, stroke, 1.2); AddLine(canvas, 20, 20, 20, 15, stroke, 1.2);
                AddPath(canvas, "M8,7 L16,14 L12.5,14.8 L15,19 L12.4,20.2 L10,16 L7,18 Z", stroke, 1.2, Brushes.Transparent);
                return true;
            case "trend-line":
                AddLine(canvas, 4, 19, 20, 5, stroke, 1.55);
                AddCircle(canvas, 4, 19, 1.75, stroke, Brushes.Transparent, 1.05);
                AddCircle(canvas, 20, 5, 1.75, stroke, Brushes.Transparent, 1.05);
                return true;
            case "ray":
                AddCircle(canvas, 5, 18, 1.8, stroke, Brushes.Transparent, 1.05);
                AddLine(canvas, 5, 18, 21, 4, stroke, 1.55);
                AddPath(canvas, "M18,4 L21,4 L21,7", stroke, 1.0, Brushes.Transparent);
                return true;
            case "extended-line":
                AddLine(canvas, 2, 21, 22, 3, stroke, 1.45);
                AddCircle(canvas, 7, 16.5, 1.65, stroke, Brushes.White, 1.0);
                AddCircle(canvas, 17, 7.5, 1.65, stroke, Brushes.White, 1.0);
                return true;
            case "horizontal-line":
                AddLine(canvas, 2.5, 12, 21.5, 12, stroke, 1.55);
                AddCircle(canvas, 12, 12, 1.55, stroke, Brushes.White, 1.0);
                return true;
            case "horizontal-ray":
                AddCircle(canvas, 5, 12, 1.7, stroke, Brushes.White, 1.0);
                AddLine(canvas, 5, 12, 22, 12, stroke, 1.55);
                return true;
            case "vertical-line":
                AddLine(canvas, 12, 2.5, 12, 21.5, stroke, 1.55);
                AddCircle(canvas, 12, 12, 1.55, stroke, Brushes.White, 1.0);
                return true;
            case "cross-line":
                AddLine(canvas, 2.5, 12, 21.5, 12, stroke, 1.25);
                AddLine(canvas, 12, 2.5, 12, 21.5, stroke, 1.25);
                AddCircle(canvas, 12, 12, 1.55, stroke, Brushes.White, 1.0);
                return true;
            case "parallel-channel":
                AddLine(canvas, 3, 17, 18, 6, stroke, 1.45);
                AddLine(canvas, 6, 21, 21, 10, stroke, 1.45);
                AddCircle(canvas, 3, 17, 1.45, stroke, Brushes.White, .9);
                AddCircle(canvas, 18, 6, 1.45, stroke, Brushes.White, .9);
                AddCircle(canvas, 6, 21, 1.45, stroke, Brushes.White, .9);
                return true;
            case "regression-trend":
                AddLine(canvas, 3, 18, 21, 6, stroke, 1.55);
                AddLine(canvas, 4, 13, 18, 3.5, stroke, .85);
                AddLine(canvas, 6, 22, 20, 12.5, stroke, .85);
                AddPath(canvas, "M4,18 C7,15 10,17 13,12 C16,8 18,10 21,6", stroke, .8, Brushes.Transparent);
                return true;
            case "pitchfork":
                AddCircle(canvas, 4.5, 19.5, 1.45, stroke, Brushes.White, .9);
                AddLine(canvas, 4.5, 19.5, 19.5, 4.5, stroke, 1.45);
                AddLine(canvas, 7, 21, 21, 8, stroke, .9);
                AddLine(canvas, 2.5, 16, 16.5, 2.5, stroke, .9);
                return true;
            case "info-line":
                AddLine(canvas, 3, 18, 20, 5, stroke, 1.5);
                AddCircle(canvas, 17.5, 16.5, 4.2, stroke, Brushes.Transparent, 1.15);
                AddTextAt(canvas, "i", 17.5, 16.0, stroke, 8.5, FontWeights.Bold);
                return true;
            case "trend-angle":
                AddLine(canvas, 4, 19, 20, 19, stroke, 1.35);
                AddLine(canvas, 4, 19, 18, 5, stroke, 1.55);
                AddPath(canvas, "M9,19 A5,5 0 0 0 7.5,15.5", stroke, 1.0, Brushes.Transparent);
                return true;
            case "flat-top-bottom":
                AddLine(canvas, 4, 6, 19, 6, stroke, 1.45);
                AddLine(canvas, 5, 18, 20, 18, stroke, 1.45);
                AddLine(canvas, 5, 18, 19, 6, stroke, 1.0);
                return true;
            case "disjoint-channel":
                AddLine(canvas, 3, 17, 10, 12, stroke, 1.45);
                AddLine(canvas, 14, 9, 21, 4, stroke, 1.45);
                AddLine(canvas, 3, 21, 10, 16, stroke, 1.0);
                AddLine(canvas, 14, 13, 21, 8, stroke, 1.0);
                return true;
            case "fib-retracement":
                AddLine(canvas, 5, 4, 5, 20, stroke, 1.25);
                AddLine(canvas, 5, 5, 20, 5, stroke, 1.35);
                AddLine(canvas, 5, 8.5, 18, 8.5, stroke, 1.05);
                AddLine(canvas, 5, 12, 21, 12, stroke, 1.05);
                AddLine(canvas, 5, 15.5, 17, 15.5, stroke, 1.05);
                AddLine(canvas, 5, 19, 20, 19, stroke, 1.35);
                AddCircle(canvas, 5, 5, 1.25, stroke, Brushes.Transparent, .9);
                AddCircle(canvas, 5, 19, 1.25, stroke, Brushes.Transparent, .9);
                return true;
            case "trend-fib-extension":
                AddPolyline(canvas, new[] { new Point(3,17), new Point(9,7), new Point(14,15) }, stroke, 1.35);
                AddCircle(canvas, 3, 17, 1.2, stroke, Brushes.Transparent, .85);
                AddCircle(canvas, 9, 7, 1.2, stroke, Brushes.Transparent, .85);
                AddCircle(canvas, 14, 15, 1.2, stroke, Brushes.Transparent, .85);
                AddLine(canvas, 14, 7, 22, 7, stroke, 1.0);
                AddLine(canvas, 14, 11, 20, 11, stroke, 1.0);
                AddLine(canvas, 14, 15, 22, 15, stroke, 1.2);
                AddLine(canvas, 14, 19, 20, 19, stroke, 1.0);
                return true;
            case "gann-box":
                AddPath(canvas, "M4,4 L20,4 L20,20 L4,20 Z", stroke, 1.3, Brushes.Transparent);
                AddLine(canvas, 4, 12, 20, 12, stroke, .8);
                AddLine(canvas, 12, 4, 12, 20, stroke, .8);
                AddLine(canvas, 4, 20, 20, 4, stroke, 1.05);
                return true;
            case "gann-square":
            case "gann-square-fixed":
                AddPath(canvas, "M4,4 L20,4 L20,20 L4,20 Z", stroke, 1.3, Brushes.Transparent);
                AddLine(canvas, 4, 20, 20, 4, stroke, .95);
                AddLine(canvas, 4, 12, 20, 12, stroke, .75);
                AddLine(canvas, 12, 4, 12, 20, stroke, .75);
                AddPath(canvas, "M4,20 A16,16 0 0 1 20,4", stroke, .9, Brushes.Transparent);
                AddPath(canvas, "M4,20 A8,8 0 0 1 12,12", stroke, .75, Brushes.Transparent);
                return true;
            case "gann-fan":
                AddCircle(canvas, 4, 20, 1.35, stroke, Brushes.Transparent, .9);
                AddLine(canvas, 4, 20, 20, 4, stroke, 1.35);
                AddLine(canvas, 4, 20, 20, 9, stroke, .95);
                AddLine(canvas, 4, 20, 20, 14, stroke, .95);
                AddLine(canvas, 4, 20, 14, 4, stroke, .95);
                AddLine(canvas, 4, 20, 9, 4, stroke, .95);
                return true;
            case "fib-time-zone":
            case "trend-fib-time":
                AddLine(canvas, 4, 4, 4, 21, stroke, 1.4);
                AddLine(canvas, 8, 6, 8, 21, stroke, 1.0);
                AddLine(canvas, 13, 4, 13, 21, stroke, 1.0);
                AddLine(canvas, 20, 7, 20, 21, stroke, 1.0);
                AddLine(canvas, 3, 21, 21, 21, stroke, 1.1);
                return true;
            case "fib-channel":
                AddLine(canvas, 3, 18, 19, 6, stroke, 1.4);
                AddLine(canvas, 4, 13, 20, 1, stroke, 1.0);
                AddLine(canvas, 7, 22, 22, 11, stroke, 1.0);
                AddCircle(canvas, 3, 18, 1.7, stroke, Brushes.Transparent, 1.0);
                return true;
            case "fib-speed-fan":
            case "pitchfan":
                AddCircle(canvas, 4, 20, 1.8, stroke, Brushes.Transparent, 1.1);
                AddLine(canvas, 4, 20, 20, 4, stroke, 1.4);
                AddLine(canvas, 4, 20, 21, 10, stroke, 1.0);
                AddLine(canvas, 4, 20, 14, 3, stroke, 1.0);
                AddLine(canvas, 4, 20, 21, 16, stroke, 0.9);
                return true;
            case "fib-circles":
                AddCircle(canvas, 8, 16, 3.0, stroke, Brushes.Transparent, 1.0);
                AddCircle(canvas, 8, 16, 6.0, stroke, Brushes.Transparent, 1.0);
                AddCircle(canvas, 8, 16, 10.0, stroke, Brushes.Transparent, 1.15);
                AddCircle(canvas, 8, 16, 1.4, stroke, stroke, 0);
                return true;
            case "fib-spiral":
                AddPath(canvas, "M12,12 C12,8 18,8 18,13 C18,20 7,21 5,13 C3,3 18,0 22,10", stroke, 1.45, Brushes.Transparent);
                return true;
            case "fib-speed-arcs":
                AddPath(canvas, "M4,20 A7,7 0 0 1 11,13", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M4,20 A12,12 0 0 1 16,8", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M4,20 A18,18 0 0 1 22,2", stroke, 1.35, Brushes.Transparent);
                AddCircle(canvas, 4, 20, 1.5, stroke, stroke, 0);
                return true;
            case "fib-wedge":
                AddLine(canvas, 4, 20, 19, 4, stroke, 1.35);
                AddLine(canvas, 4, 20, 22, 16, stroke, 1.35);
                AddPath(canvas, "M9,19 A5,5 0 0 1 8,15", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M15,18 A11,11 0 0 1 11,10", stroke, 1.0, Brushes.Transparent);
                return true;
            case "schiff-pitchfork":
            case "modified-schiff-pitchfork":
            case "inside-pitchfork":
                AddLine(canvas, 5, 20, 19, 5, stroke, 1.5);
                AddLine(canvas, tool.Id == "inside-pitchfork" ? 7 : 3, 17, 17, 3, stroke, 1.0);
                AddLine(canvas, tool.Id == "modified-schiff-pitchfork" ? 10 : 8, 21, 21, 9, stroke, 1.0);
                AddCircle(canvas, 5, 20, 1.6, stroke, Brushes.Transparent, 1.0);
                return true;
            case "circle":
                AddCircle(canvas, 12, 12, 8.2, stroke, Brushes.Transparent, 1.5);
                return true;
            case "text":
                // Full-size rail Text glyph built from vectors so it matches the visual
                // footprint of the other 30 px drawing-tool icons and keeps the same
                // one-pixel stroke grammar as the rest of the rail.
                AddLine(canvas, 4, 4.4, 20, 4.4, stroke, 1.0);
                AddLine(canvas, 12.4, 4.4, 12.4, 21.2, stroke, 1.0);
                return true;
            case "path":
            {
                // Clean angular path with small hollow construction nodes.  The final
                // segment terminates in an arrow head, matching the tool's direction
                // cue instead of ending in the old oversized circular point.
                var points = new[]
                {
                    new Point(3.6, 18.4),
                    new Point(8.4, 7.2),
                    new Point(13.6, 15.2),
                    new Point(20.6, 5.2)
                };
                AddAngularPolyline(canvas, points, stroke, 1.0);
                AddCircle(canvas, 3.6, 18.4, 1.0, stroke, Brushes.Transparent, 1.0);
                AddCircle(canvas, 8.4, 7.2, 1.0, stroke, Brushes.Transparent, 1.0);
                AddCircle(canvas, 13.6, 15.2, 1.0, stroke, Brushes.Transparent, 1.0);
                AddLine(canvas, 20.6, 5.2, 16.9, 6.8, stroke, 1.0);
                AddLine(canvas, 20.6, 5.2, 19.9, 9.2, stroke, 1.0);
                return true;
            }
            case "polyline":
            {
                // Keep Polyline visually distinct from Path: no arrow, balanced zig-zag,
                // and restrained hollow nodes instead of the old heavy filled dots.
                var points = new[]
                {
                    new Point(3.6, 18.4),
                    new Point(8.4, 8.0),
                    new Point(13.8, 15.4),
                    new Point(20.6, 5.6)
                };
                AddAngularPolyline(canvas, points, stroke, 1.0);
                foreach (Point point in points)
                    AddCircle(canvas, point.X, point.Y, 1.0, stroke, Brushes.Transparent, 1.0);
                return true;
            }
            case "anchored-note":
                AddRect(canvas, 7, 4, 13, 11, stroke, Brushes.Transparent, 1.3, 2);
                AddLine(canvas, 7, 15, 4, 21, stroke, 1.2);
                AddCircle(canvas, 4, 21, 1.5, stroke, stroke, 0);
                AddLine(canvas, 10, 8, 17, 8, stroke, 0.9);
                AddLine(canvas, 10, 11, 15, 11, stroke, 0.9);
                return true;
            case "signpost":
                AddLine(canvas, 7, 3, 7, 22, stroke, 1.4);
                AddPath(canvas, "M7,5 L20,5 L17,10 L7,10 Z", stroke, 1.25, Brushes.Transparent);
                AddCircle(canvas, 7, 22, 1.4, stroke, stroke, 0);
                return true;
            case "comment":
                AddPath(canvas, "M4,5 L20,5 L20,16 L12,16 L8,21 L8,16 L4,16 Z", stroke, 1.35, Brushes.Transparent);
                AddLine(canvas, 8, 9, 16, 9, stroke, 0.9);
                AddLine(canvas, 8, 12, 14, 12, stroke, 0.9);
                return true;
            case "pin":
                AddCircle(canvas, 12, 9, 5.5, stroke, Brushes.Transparent, 1.35);
                AddPath(canvas, "M9,13 L12,21 L15,13", stroke, 1.25, Brushes.Transparent);
                AddCircle(canvas, 12, 9, 1.5, stroke, stroke, 0);
                return true;
            case "table":
                AddRect(canvas, 4, 5, 16, 14, stroke, Brushes.Transparent, 1.35, 1);
                AddLine(canvas, 4, 10, 20, 10, stroke, 1.0);
                AddLine(canvas, 12, 5, 12, 19, stroke, 1.0);
                AddLine(canvas, 4, 15, 20, 15, stroke, 1.0);
                return true;
            case "price-note":
                AddPath(canvas, "M4,6 L17,6 L21,12 L17,18 L4,18 Z", stroke, 1.35, Brushes.Transparent);
                AddTextAt(canvas, "$", 11, 11.5, stroke, 9, FontWeights.SemiBold);
                return true;
            case "image":
                AddRect(canvas, 3, 5, 18, 14, stroke, Brushes.Transparent, 1.35, 1.5);
                AddCircle(canvas, 8, 10, 2.0, stroke, Brushes.Transparent, 1.0);
                AddPath(canvas, "M5,17 L10,12 L13,15 L16,11 L20,17", stroke, 1.2, Brushes.Transparent);
                return true;
            case "post":
                AddRect(canvas, 4, 4, 16, 16, stroke, Brushes.Transparent, 1.25, 2);
                AddLine(canvas, 7, 8, 17, 8, stroke, 1.0);
                AddLine(canvas, 7, 11, 15, 11, stroke, 1.0);
                AddPath(canvas, "M13,15 L19,15 L19,9", stroke, 1.2, Brushes.Transparent);
                return true;
            case "idea":
                AddCircle(canvas, 12, 9, 5.0, stroke, Brushes.Transparent, 1.25);
                AddLine(canvas, 9, 15, 15, 15, stroke, 1.1);
                AddLine(canvas, 10, 18, 14, 18, stroke, 1.1);
                AddLine(canvas, 12, 2, 12, 0.5, stroke, 1.0);
                AddLine(canvas, 5, 4, 3.5, 2.5, stroke, 1.0);
                AddLine(canvas, 19, 4, 20.5, 2.5, stroke, 1.0);
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
                DrawPatternSpecificIcon(canvas, tool.Id, stroke);
                return true;
            case "time-cycles":
                AddCircle(canvas, 7, 13, 4.5, stroke, Brushes.Transparent, 1.15);
                AddCircle(canvas, 15, 13, 6.5, stroke, Brushes.Transparent, 1.15);
                AddLine(canvas, 3, 21, 22, 21, stroke, 1.0);
                return true;
            case "long-position":
                AddRect(canvas, 4, 4, 16, 8, stroke, Brushes.Transparent, 1.25, 1.5);
                AddLine(canvas, 4, 12.4, 20, 12.4, stroke, 1.0);
                AddPath(canvas, "M12,10 L12,5 M9,8 L12,5 L15,8", stroke, 1.2, Brushes.Transparent);
                AddRect(canvas, 4, 14, 16, 6, stroke, Brushes.Transparent, 0.9, 1.5);
                return true;
            case "short-position":
                AddRect(canvas, 4, 4, 16, 6, stroke, Brushes.Transparent, 0.9, 1.5);
                AddLine(canvas, 4, 10.8, 20, 10.8, stroke, 1.0);
                AddRect(canvas, 4, 12, 16, 8, stroke, Brushes.Transparent, 1.25, 1.5);
                AddPath(canvas, "M12,14 L12,19 M9,16 L12,19 L15,16", stroke, 1.2, Brushes.Transparent);
                return true;
            case "date-range":
                AddLine(canvas, 4, 12, 20, 12, stroke, 1.45);
                AddPath(canvas, "M4,12 L8,9 M4,12 L8,15 M20,12 L16,9 M20,12 L16,15", stroke, 1.2, Brushes.Transparent);
                AddLine(canvas, 4, 6, 4, 18, stroke, 0.9); AddLine(canvas, 20, 6, 20, 18, stroke, 0.9);
                return true;
            case "price-range":
                AddLine(canvas, 12, 4, 12, 20, stroke, 1.45);
                AddPath(canvas, "M12,4 L9,8 M12,4 L15,8 M12,20 L9,16 M12,20 L15,16", stroke, 1.2, Brushes.Transparent);
                AddLine(canvas, 6, 4, 18, 4, stroke, 0.9); AddLine(canvas, 6, 20, 18, 20, stroke, 0.9);
                return true;
            case "date-price-range":
                AddRect(canvas, 5, 5, 14, 14, stroke, Brushes.Transparent, 1.1, 1.5);
                AddLine(canvas, 5, 12, 19, 12, stroke, 1.0); AddLine(canvas, 12, 5, 12, 19, stroke, 1.0);
                AddPath(canvas, "M5,12 L8,10 M5,12 L8,14 M19,12 L16,10 M19,12 L16,14", stroke, 0.9, Brushes.Transparent);
                return true;
            case "ghost-feed":
                AddPath(canvas, "M5,20 L5,9 C5,3 19,3 19,9 L19,20 L16,17 L13,20 L10,17 Z", stroke, 1.25, Brushes.Transparent);
                AddCircle(canvas, 10, 10, 1.2, stroke, stroke, 0); AddCircle(canvas, 15, 10, 1.2, stroke, stroke, 0);
                return true;
            case "anchored-volume-profile":
                AddLine(canvas, 4, 3, 4, 21, stroke, 1.25);
                AddCircle(canvas, 4, 3, 1.5, stroke, stroke, 0);
                AddRect(canvas, 6, 5, 6, 3, stroke, stroke, 0, 0);
                AddRect(canvas, 6, 10, 12, 3, stroke, stroke, 0, 0);
                AddRect(canvas, 6, 15, 16, 3, stroke, stroke, 0, 0);
                return true;
            case "icons":
                AddPath(canvas, "M12,3 L14.8,8.7 L21,9.6 L16.5,14 L17.6,20.3 L12,17.3 L6.4,20.3 L7.5,14 L3,9.6 L9.2,8.7 Z", stroke, 1.25, Brushes.Transparent);
                return true;
            case "stickers":
                AddPath(canvas, "M4,5 L17,5 L21,9 L21,19 L4,19 Z", stroke, 1.3, Brushes.Transparent);
                AddPath(canvas, "M17,5 L17,9 L21,9", stroke, 1.0, Brushes.Transparent);
                AddTextAt(canvas, "A", 11.5, 12, stroke, 9, FontWeights.Bold);
                return true;
            case "emojis":
                AddCircle(canvas, 12, 12, 8.5, stroke, Brushes.Transparent, 1.35);
                AddCircle(canvas, 9, 10, 1.0, stroke, stroke, 0);
                AddCircle(canvas, 15, 10, 1.0, stroke, stroke, 0);
                AddPath(canvas, "M8,14 C10,18 14,18 16,14", stroke, 1.25, Brushes.Transparent);
                return true;
            default:
                return false;
        }
    }

    private static void DrawPatternSpecificIcon(Canvas canvas, string toolId, Brush stroke)
    {
        Point[] points = toolId switch
        {
            "abcd-pattern" => new[] { new Point(3,18), new Point(8,6), new Point(13,15), new Point(21,4) },
            "triangle-pattern" => new[] { new Point(3,18), new Point(8,5), new Point(13,16), new Point(18,8), new Point(21,14) },
            "three-drives-pattern" => new[] { new Point(3,18), new Point(6,9), new Point(10,16), new Point(13,6), new Point(17,14), new Point(21,3) },
            "head-shoulders" => new[] { new Point(3,17), new Point(7,9), new Point(10,15), new Point(13,4), new Point(16,15), new Point(20,9), new Point(22,17) },
            "elliott-impulse" => new[] { new Point(3,18), new Point(7,10), new Point(10,15), new Point(14,6), new Point(17,12), new Point(21,3) },
            "elliott-triangle" => new[] { new Point(3,5), new Point(7,19), new Point(11,8), new Point(15,16), new Point(20,11) },
            "elliott-correction" => new[] { new Point(3,5), new Point(9,17), new Point(15,9), new Point(21,19) },
            "elliott-double-combo" => new[] { new Point(3,5), new Point(7,15), new Point(11,9), new Point(15,18), new Point(21,10) },
            "elliott-triple-combo" => new[] { new Point(3,5), new Point(6,14), new Point(9,8), new Point(12,17), new Point(15,10), new Point(18,19), new Point(21,12) },
            "cypher-pattern" => new[] { new Point(3,18), new Point(8,5), new Point(12,13), new Point(17,8), new Point(21,19) },
            _ => new[] { new Point(3,17), new Point(7,7), new Point(11,14), new Point(16,5), new Point(21,12) }
        };
        AddAngularPolyline(canvas, points, stroke, 1.0);
        foreach (Point point in points)
            AddCircle(canvas, point.X, point.Y, 1.05, stroke, Brushes.Transparent, 1.0);
    }

    private static void AddTextAt(Canvas canvas, string text, double centerX, double centerY, Brush brush, double fontSize, FontWeight weight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = ScaleValue(canvas, fontSize),
            FontWeight = weight,
            TextAlignment = TextAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, ScaleValue(canvas, centerX) - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, ScaleValue(canvas, centerY) - label.DesiredSize.Height / 2 - 0.5);
        canvas.Children.Add(label);
    }

    public static FrameworkElement CreateCategoryIcon(DrawingToolCategory category, double size = 20, Brush? brush = null)
    {
        string toolId = category switch
        {
            DrawingToolCategory.Cursor => "cursor-arrow",
            DrawingToolCategory.TrendLine => "trend-line",
            DrawingToolCategory.FibonacciGann => "fib-retracement",
            DrawingToolCategory.Geometry => "rectangle",
            DrawingToolCategory.Annotation => "text",
            DrawingToolCategory.Pattern => "xabcd-pattern",
            DrawingToolCategory.PredictionMeasurement => "date-price-range",
            DrawingToolCategory.IconsMedia => "emojis",
            _ => "trend-line"
        };
        return CreateToolIcon(toolId, size, brush);
    }

    public static FrameworkElement CreateActionIcon(string action, double size = 20, Brush? brush = null)
    {
        Brush stroke = brush ?? new SolidColorBrush(Color.FromRgb(203, 213, 225));
        // Action icons share the same one-device-pixel visual stroke rule as drawing-tool logos.
        // The design-space thickness is compensated for the requested icon scale in NewCanvas.
        var canvas = NewCanvas(size, normalizeToolStroke: true);
        switch (action.ToLowerInvariant())
        {
            case "favorites":
                // Symmetric, centered star with a true one-pixel outline at every requested size.
                AddPath(canvas, "M12,3.2 L14.7,8.7 L20.8,9.6 L16.4,13.9 L17.5,20.1 L12,17.2 L6.5,20.1 L7.6,13.9 L3.2,9.6 L9.3,8.7 Z", stroke, 1.0, Brushes.Transparent);
                break;
            case "star-filled":
                AddPath(canvas, "M12,3 L14.8,8.7 L21,9.6 L16.5,14 L17.6,20.3 L12,17.3 L6.4,20.3 L7.5,14 L3,9.6 L9.2,8.7 Z", stroke, 1.0, stroke);
                break;
            case "measure":
                // Strong, continuous ruler silhouette. It keeps the one-pixel visual-stroke rule,
                // but uses a larger closed body and deliberate graduated ticks so it does not
                // look thin, fragmented or broken at the 30 px drawing-rail size.
                AddRect(canvas, 3.5, 7.0, 17.0, 10.0, stroke, Brushes.Transparent, 1.0, 0.8);
                AddLine(canvas, 7.0, 7.5, 7.0, 11.5, stroke, 1.0);
                AddLine(canvas, 10.0, 7.5, 10.0, 10.0, stroke, 1.0);
                AddLine(canvas, 13.0, 7.5, 13.0, 11.5, stroke, 1.0);
                AddLine(canvas, 16.0, 7.5, 16.0, 10.0, stroke, 1.0);
                AddLine(canvas, 19.0, 7.5, 19.0, 11.5, stroke, 1.0);
                break;
            case "zoom":
                // Balanced magnifier with a lighter handle and centered plus sign.
                AddCircle(canvas, 9.5, 9.5, 5.8, stroke, Brushes.Transparent, 1.0);
                AddLine(canvas, 13.7, 13.7, 20.0, 20.0, stroke, 1.0);
                AddLine(canvas, 6.5, 9.5, 12.5, 9.5, stroke, 1.0);
                AddLine(canvas, 9.5, 6.5, 9.5, 12.5, stroke, 1.0);
                break;
            case "scroll":
                // Four-way navigation grammar without the old heavy horizontal bar.
                AddLine(canvas, 4.0, 12.0, 20.0, 12.0, stroke, 1.0);
                AddPath(canvas, "M8,8 L4,12 L8,16", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M16,8 L20,12 L16,16", stroke, 1.0, Brushes.Transparent);
                AddLine(canvas, 12.0, 5.0, 12.0, 19.0, stroke, 1.0);
                AddPath(canvas, "M9,8 L12,5 L15,8", stroke, 1.0, Brushes.Transparent);
                AddPath(canvas, "M9,16 L12,19 L15,16", stroke, 1.0, Brushes.Transparent);
                break;
            case "magnet":
                // Open U-magnet with equal legs and clean pole caps.
                AddPath(canvas, "M5,4 L9,4 L9,13 C9,16.5 10.2,18 12,18 C13.8,18 15,16.5 15,13 L15,4 L19,4 L19,13 C19,19.2 16.2,21 12,21 C7.8,21 5,19.2 5,13 Z", stroke, 1.0, Brushes.Transparent);
                AddLine(canvas, 5.0, 8.0, 9.0, 8.0, stroke, 1.0);
                AddLine(canvas, 15.0, 8.0, 19.0, 8.0, stroke, 1.0);
                break;
            case "stay":
                // Keep-drawing infinity mark, centered with equal lobes.
                AddPath(canvas, "M3.5,12 C3.5,7.6 6.0,5.5 9.0,5.5 C12.0,5.5 14.0,8.1 16.0,11.0 C17.4,13.0 18.5,14.5 20.0,14.5 C21.0,14.5 21.0,13.1 21.0,12 C21.0,8.7 19.0,5.5 16.0,5.5 C13.0,5.5 11.0,8.1 9.0,11.0 C7.6,13.0 6.5,14.5 5.0,14.5 C4.0,14.5 3.5,13.3 3.5,12", stroke, 1.0, Brushes.Transparent);
                break;
            case "lock":
                // Crisp geometric padlock: straight centered body, symmetric shackle,
                // and a compact keyhole.  Avoids the soft/uneven old silhouette.
                AddRect(canvas, 6.0, 10.0, 12.0, 10.0, stroke, Brushes.Transparent, 1.0, 1.0);
                AddPath(canvas, "M8.5,10 L8.5,7 C8.5,4.6 9.9,3.5 12,3.5 C14.1,3.5 15.5,4.6 15.5,7 L15.5,10", stroke, 1.0, Brushes.Transparent);
                AddCircle(canvas, 12.0, 14.0, 1.0, stroke, stroke, 0);
                AddLine(canvas, 12.0, 15.0, 12.0, 17.2, stroke, 1.0);
                break;
            case "hide":
                // Visibility-off eye with a thinner, fully crossing slash.
                AddPath(canvas, "M2.5,12 C5.5,7.2 8.4,5.5 12,5.5 C15.6,5.5 18.5,7.2 21.5,12 C18.5,16.8 15.6,18.5 12,18.5 C8.4,18.5 5.5,16.8 2.5,12 Z", stroke, 1.0, Brushes.Transparent);
                AddCircle(canvas, 12, 12, 2.7, stroke, Brushes.Transparent, 1.0);
                AddLine(canvas, 4.0, 4.0, 20.0, 20.0, stroke, 1.0);
                break;
            case "sync":
                AddPath(canvas, "M5,8 C7,4 13,3 17,6 L20,9 M20,9 L16,9 M20,9 L20,5", stroke, 1.4, Brushes.Transparent);
                AddPath(canvas, "M19,16 C17,20 11,21 7,18 L4,15 M4,15 L8,15 M4,15 L4,19", stroke, 1.4, Brushes.Transparent);
                break;
            case "tree":
                AddLine(canvas, 7, 5, 7, 19, stroke, 1.3);
                AddLine(canvas, 7, 8, 12, 8, stroke, 1.3);
                AddLine(canvas, 7, 16, 12, 16, stroke, 1.3);
                AddRect(canvas, 12, 5, 8, 6, stroke, Brushes.Transparent, 1.2, 1);
                AddRect(canvas, 12, 13, 8, 6, stroke, Brushes.Transparent, 1.2, 1);
                break;
            case "undo":
                AddPath(canvas, "M9,6 L4,11 L9,16 M5,11 L13,11 C18,11 20,14 20,19", stroke, 1.6, Brushes.Transparent);
                break;
            case "redo":
                AddPath(canvas, "M15,6 L20,11 L15,16 M19,11 L11,11 C6,11 4,14 4,19", stroke, 1.6, Brushes.Transparent);
                break;
            case "delete":
                // Rebuilt trash can: centered body, balanced lid/handle, straight inner rails.
                AddRect(canvas, 7.0, 8.0, 10.0, 12.5, stroke, Brushes.Transparent, 1.0, 0.8);
                AddLine(canvas, 5.0, 7.0, 19.0, 7.0, stroke, 1.0);
                AddPath(canvas, "M9,7 L9,4.5 L15,4.5 L15,7", stroke, 1.0, Brushes.Transparent);
                AddLine(canvas, 10.0, 10.5, 10.0, 18.0, stroke, 1.0);
                AddLine(canvas, 14.0, 10.5, 14.0, 18.0, stroke, 1.0);
                break;
            case "unlock":
                AddRect(canvas, 6.0, 10.0, 12.0, 10.0, stroke, Brushes.Transparent, 1.0, 1.0);
                AddPath(canvas, "M8.5,10 L8.5,7 C8.5,4.6 9.9,3.5 12,3.5 C14.0,3.5 15.2,4.3 16.0,5.6 L18.2,5.6", stroke, 1.0, Brushes.Transparent);
                AddCircle(canvas, 12.0, 14.0, 1.0, stroke, stroke, 0);
                AddLine(canvas, 12.0, 15.0, 12.0, 17.2, stroke, 1.0);
                break;
            case "line":
                AddLine(canvas, 3, 17, 21, 7, stroke, 1.8);
                AddLine(canvas, 4, 21, 20, 21, stroke, 2.5);
                break;
            case "visibility":
                AddPath(canvas, "M2,12 C6,5.5 18,5.5 22,12 C18,18.5 6,18.5 2,12 Z", stroke, 1.2, Brushes.Transparent);
                AddCircle(canvas, 12, 12, 3.2, stroke, Brushes.Transparent, 1.2);
                break;
            case "settings":
                AddCircle(canvas, 12, 12, 3.2, stroke, Brushes.Transparent, 1.4);
                AddCircle(canvas, 12, 12, 8.0, stroke, Brushes.Transparent, 1.3);
                AddLine(canvas, 12, 2, 12, 5, stroke, 1.5);
                AddLine(canvas, 12, 19, 12, 22, stroke, 1.5);
                AddLine(canvas, 2, 12, 5, 12, stroke, 1.5);
                AddLine(canvas, 19, 12, 22, 12, stroke, 1.5);
                AddLine(canvas, 4.8, 4.8, 6.9, 6.9, stroke, 1.4);
                AddLine(canvas, 17.1, 17.1, 19.2, 19.2, stroke, 1.4);
                AddLine(canvas, 19.2, 4.8, 17.1, 6.9, stroke, 1.4);
                AddLine(canvas, 6.9, 17.1, 4.8, 19.2, stroke, 1.4);
                break;
            case "template":
                AddPath(canvas, "M5,4 L19,4 L19,20 L12,16 L5,20 Z", stroke, 1.4, Brushes.Transparent);
                break;
            case "alert":
                AddPath(canvas, "M12,4 C8,4 6,7 6,11 L6,16 L4,18 L20,18 L18,16 L18,11 C18,7 16,4 12,4 Z", stroke, 1.25, Brushes.Transparent);
                AddCircle(canvas, 12, 20, 1.1, stroke, stroke, 0);
                AddLine(canvas, 18.5, 5.5, 22, 5.5, stroke, 1.2);
                AddLine(canvas, 20.25, 3.75, 20.25, 7.25, stroke, 1.2);
                break;
            case "more":
                AddCircle(canvas, 6, 12, 1.45, stroke, stroke, 0);
                AddCircle(canvas, 12, 12, 1.45, stroke, stroke, 0);
                AddCircle(canvas, 18, 12, 1.45, stroke, stroke, 0);
                break;
            case "close":
                AddLine(canvas, 6, 6, 18, 18, stroke, 1.5);
                AddLine(canvas, 18, 6, 6, 18, stroke, 1.5);
                break;
            case "chevron-up":
                AddLine(canvas, 6, 15, 12, 9, stroke, 1.5);
                AddLine(canvas, 12, 9, 18, 15, stroke, 1.5);
                break;
            case "chevron-down":
                AddLine(canvas, 6, 9, 12, 15, stroke, 1.5);
                AddLine(canvas, 12, 15, 18, 9, stroke, 1.5);
                break;
            case "chevron-right":
                AddLine(canvas, 9, 6, 15, 12, stroke, 1.5);
                AddLine(canvas, 15, 12, 9, 18, stroke, 1.5);
                break;
            case "collapse":
                AddLine(canvas, 14, 6, 8, 12, stroke, 1.5);
                AddLine(canvas, 8, 12, 14, 18, stroke, 1.5);
                break;
            default:
                AddCircle(canvas, 12, 12, 8, stroke, Brushes.Transparent, 1.4);
                break;
        }
        FinalizeCanvasQuality(canvas);
        return canvas;
    }

    private sealed class IconRenderMetrics
    {
        public IconRenderMetrics(double scale, bool normalizeStroke)
        {
            Scale = scale;
            NormalizeStroke = normalizeStroke;
        }

        public double Scale { get; }
        public bool NormalizeStroke { get; }
    }

    private static Canvas NewCanvas(double size, bool normalizeToolStroke = false)
    {
        double safeSize = Math.Max(1.0, size);
        return new Canvas
        {
            // Render directly at the requested final size.  The old implementation
            // used a 24-DIP Canvas + LayoutTransform, so 30/33 px rail icons were
            // rasterized through a 1.25/1.375 transform and could look soft.
            Width = safeSize,
            Height = safeSize,
            Background = Brushes.Transparent,
            ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Tag = new IconRenderMetrics(safeSize / DesignSize, normalizeToolStroke)
        };
    }

    private static double IconScale(Canvas canvas) =>
        canvas.Tag is IconRenderMetrics metrics ? metrics.Scale : 1.0;

    private static double ScaleValue(Canvas canvas, double value) => value * IconScale(canvas);

    private static Point ScalePoint(Canvas canvas, Point point) =>
        new(ScaleValue(canvas, point.X), ScaleValue(canvas, point.Y));

    private static double EffectiveStrokeThickness(Canvas canvas, double requestedThickness)
    {
        if (requestedThickness <= 0)
            return 0;

        return canvas.Tag is IconRenderMetrics metrics && metrics.NormalizeStroke
            ? 1.0
            : requestedThickness * IconScale(canvas);
    }

    private static bool UsesNormalizedToolStroke(Canvas canvas) =>
        canvas.Tag is IconRenderMetrics metrics && metrics.NormalizeStroke;

    private static void FinalizeCanvasQuality(Canvas canvas)
    {
        foreach (object child in canvas.Children)
        {
            if (child is UIElement element)
                element.SnapsToDevicePixels = true;

            if (child is FrameworkElement frameworkElement)
                frameworkElement.UseLayoutRounding = true;

            switch (child)
            {
                case Shape shape:
                    shape.StrokeMiterLimit = Math.Max(shape.StrokeMiterLimit, 2.0);
                    shape.SnapsToDevicePixels = true;
                    shape.UseLayoutRounding = true;
                    break;
                case TextBlock label:
                    label.SnapsToDevicePixels = true;
                    label.UseLayoutRounding = true;
                    break;
            }
        }
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        double sx1 = ScaleValue(canvas, x1);
        double sy1 = ScaleValue(canvas, y1);
        double sx2 = ScaleValue(canvas, x2);
        double sy2 = ScaleValue(canvas, y2);
        var line = new Line
        {
            X1 = sx1, Y1 = sy1, X2 = sx2, Y2 = sy2,
            Stroke = stroke, StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        if (UsesNormalizedToolStroke(canvas) && (Math.Abs(y1 - y2) < 0.001 || Math.Abs(x1 - x2) < 0.001))
            RenderOptions.SetEdgeMode(line, EdgeMode.Aliased);
        canvas.Children.Add(line);
    }

    private static void AddPolyline(Canvas canvas, IEnumerable<Point> points, Brush stroke, double thickness)
    {
        var polyline = new Polyline
        {
            Points = new PointCollection(points.Select(point => ScalePoint(canvas, point))),
            Stroke = stroke,
            StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeMiterLimit = 2.0,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        canvas.Children.Add(polyline);
    }

    private static void AddAngularPolyline(Canvas canvas, IEnumerable<Point> points, Brush stroke, double thickness)
    {
        var polyline = new Polyline
        {
            Points = new PointCollection(points.Select(point => ScalePoint(canvas, point))),
            Stroke = stroke,
            StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            StrokeLineJoin = PenLineJoin.Miter,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
            StrokeMiterLimit = 2.0,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        canvas.Children.Add(polyline);
    }

    private static void AddPath(Canvas canvas, string data, Brush stroke, double thickness, Brush fill)
    {
        Geometry geometry = Geometry.Parse(data).Clone();
        double scale = IconScale(canvas);
        if (Math.Abs(scale - 1.0) > 0.0001)
            geometry.Transform = new ScaleTransform(scale, scale);

        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            Fill = fill,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeMiterLimit = 2.0,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        canvas.Children.Add(path);
    }

    private static void AddRect(Canvas canvas, double x, double y, double width, double height, Brush stroke, Brush fill, double thickness, double radius)
    {
        var rectangle = new Rectangle
        {
            Width = ScaleValue(canvas, width),
            Height = ScaleValue(canvas, height),
            Stroke = stroke,
            Fill = fill,
            StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            RadiusX = ScaleValue(canvas, radius),
            RadiusY = ScaleValue(canvas, radius),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            StrokeLineJoin = PenLineJoin.Miter,
            StrokeMiterLimit = 2.0
        };
        Canvas.SetLeft(rectangle, ScaleValue(canvas, x));
        Canvas.SetTop(rectangle, ScaleValue(canvas, y));
        canvas.Children.Add(rectangle);
    }

    private static void AddEllipse(Canvas canvas, double x, double y, double width, double height, Brush stroke, Brush fill, double thickness)
    {
        var ellipse = new Ellipse
        {
            Width = ScaleValue(canvas, width),
            Height = ScaleValue(canvas, height),
            Stroke = stroke,
            Fill = fill,
            StrokeThickness = EffectiveStrokeThickness(canvas, thickness),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        Canvas.SetLeft(ellipse, ScaleValue(canvas, x));
        Canvas.SetTop(ellipse, ScaleValue(canvas, y));
        canvas.Children.Add(ellipse);
    }

    private static void AddCircle(Canvas canvas, double centerX, double centerY, double radius, Brush stroke, Brush fill, double thickness) =>
        AddEllipse(canvas, centerX - radius, centerY - radius, radius * 2, radius * 2, stroke, fill, thickness);

    private static void AddText(Canvas canvas, string text, Brush brush, double fontSize, FontWeight weight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = ScaleValue(canvas, fontSize),
            FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, (canvas.Width - label.DesiredSize.Width) / 2);
        Canvas.SetTop(label, (canvas.Height - label.DesiredSize.Height) / 2 - 0.5);
        canvas.Children.Add(label);
    }
}
