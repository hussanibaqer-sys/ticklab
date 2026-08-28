using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Core;

internal static class TopBarIconFactory
{
    public static FrameworkElement CreateAction(string action, double size, Brush brush)
    {
        var canvas = Canvas24();
        string key = (action ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "new-chart":
                // Chart window + add badge: unmistakable New Chart action.
                Rect(canvas, 2.5, 4.5, 14.5, 14.5, brush, null, 1.45, 2.2);
                Poly(canvas, "M5.5,14 L8.3,11.2 L10.6,12.8 L14,8.5", brush, 1.35);
                Circle(canvas, 18.2, 16.8, 4.1, brush, null, 1.35);
                Line(canvas, 18.2, 14.5, 18.2, 19.1, brush, 1.55);
                Line(canvas, 15.9, 16.8, 20.5, 16.8, brush, 1.55);
                break;
            case "layout":
                Rect(canvas, 3, 4, 8, 7, brush, null, 1.4, 1.5);
                Rect(canvas, 13, 4, 8, 7, brush, null, 1.4, 1.5);
                Rect(canvas, 3, 13, 8, 7, brush, null, 1.4, 1.5);
                Rect(canvas, 13, 13, 8, 7, brush, null, 1.4, 1.5);
                break;
            case "indicators":
                // Vector recreation of the user's bar-chart + rising-line indicator symbol.
                Rect(canvas, 3.2, 13.2, 3.2, 7.3, brush, null, 1.25, 0.5);
                Rect(canvas, 8.0, 9.2, 3.2, 11.3, brush, null, 1.25, 0.5);
                Rect(canvas, 12.8, 14.8, 3.2, 5.7, brush, null, 1.25, 0.5);
                Rect(canvas, 17.6, 10.8, 3.2, 9.7, brush, null, 1.25, 0.5);
                Poly(canvas, "M3.8,9.7 L8.2,6.8 L11.5,8.1 L15.8,4.7 L20.2,6.1", brush, 1.55);
                Circle(canvas, 8.2, 6.8, 0.9, brush, brush, 0);
                Circle(canvas, 15.8, 4.7, 0.9, brush, brush, 0);
                Poly(canvas, "M17.7,3.9 L21.1,5.7 L19.1,9.0", brush, 1.45);
                break;
            case "alerts":
                Poly(canvas, "M12,4 C8,4 6,7 6,11 L6,16 L4,18 L20,18 L18,16 L18,11 C18,7 16,4 12,4 Z", brush, 1.4);
                Circle(canvas, 12, 20, 1.2, brush, brush, 0);
                break;
            case "replay":
                Circle(canvas, 12, 12, 8, brush, null, 1.4);
                Poly(canvas, "M10,8 L16,12 L10,16 Z", brush, 1.2, brush);
                Poly(canvas, "M4,8 L4,4 L8,4", brush, 1.2);
                break;
            case "connections":
                // MT5 connection: trading terminal + live cable/plug. Clear at 18 px and unlike a generic chain icon.
                Rect(canvas, 2.5, 4.0, 13.0, 15.0, brush, null, 1.35, 1.8);
                Line(canvas, 5.0, 16.3, 13.0, 16.3, brush, 1.0);
                Poly(canvas, "M5.0,12.8 L7.2,10.4 L9.2,11.6 L12.7,7.6", brush, 1.35);
                Circle(canvas, 12.7, 7.6, 0.75, brush, brush, 0);
                Poly(canvas, "M15.5,11.3 C17.1,11.3 17.5,12.6 17.5,14.2 L17.5,17.6", brush, 1.45);
                Rect(canvas, 16.0, 17.0, 5.2, 3.3, brush, null, 1.3, 0.8);
                Line(canvas, 17.3, 17.0, 17.3, 14.8, brush, 1.2);
                Line(canvas, 19.9, 17.0, 19.9, 14.8, brush, 1.2);
                break;
            case "refresh":
                Poly(canvas, "M19,8 C17,4 11,3 7,6 C3,9 4,15 8,18 C12,21 18,19 20,15", brush, 1.7);
                Poly(canvas, "M19,4 L19,8 L15,8", brush, 1.5);
                break;
            case "markers":
                Poly(canvas, "M12,3 C8,3 5,6 5,10 C5,16 12,21 12,21 C12,21 19,16 19,10 C19,6 16,3 12,3 Z", brush, 1.4);
                Circle(canvas, 12, 10, 2.2, brush, null, 1.2);
                break;
            case "theme-light":
                Circle(canvas, 12, 12, 3.8, brush, null, 1.4);
                foreach (var (x1,y1,x2,y2) in new[]{(12d,2d,12d,5d),(12d,19d,12d,22d),(2d,12d,5d,12d),(19d,12d,22d,12d),(4.8d,4.8d,7d,7d),(17d,17d,19.2d,19.2d),(19.2d,4.8d,17d,7d),(7d,17d,4.8d,19.2d)}) Line(canvas,x1,y1,x2,y2,brush,1.2);
                break;
            case "theme-dark":
                Poly(canvas, "M16.5,4 C10,4 6,8 6,13 C6,18 10,21 15,20 C11,18 9,14 10,10 C11,7 13,5 16.5,4 Z", brush, 1.4);
                break;
            case "settings":
                // True toothed mechanical gear: irregular outer teeth + hub, deliberately not a target/GPS symbol.
                Poly(canvas, "M9.5,2.4 L14.5,2.4 L15.1,5.0 L17.2,5.9 L19.5,4.5 L21.8,8.6 L19.8,10.2 L20.0,12.6 L22.2,14.0 L20.0,18.1 L17.4,17.2 L15.4,18.8 L14.8,21.6 L9.9,21.6 L9.2,18.9 L6.9,17.9 L4.4,19.2 L2.1,15.1 L4.2,13.5 L4.0,10.8 L1.9,9.3 L4.2,5.2 L6.8,6.2 L8.9,5.0 Z", brush, 1.35);
                Circle(canvas, 12, 12, 3.25, brush, null, 1.45);
                break;
            case "folder":
                Poly(canvas, "M3,7 L9,7 L11,9 L21,9 L20,19 L4,19 Z", brush, 1.4);
                break;
            case "code":
                Poly(canvas, "M9,7 L4,12 L9,17", brush, 1.6);
                Poly(canvas, "M15,7 L20,12 L15,17", brush, 1.6);
                Line(canvas, 13, 5, 11, 19, brush, 1.3);
                break;
            case "record":
                // Film/video recorder: twin reels, camera body and lens hood.
                Circle(canvas, 7.2, 6.7, 2.7, brush, null, 1.25);
                Circle(canvas, 12.8, 6.7, 2.7, brush, null, 1.25);
                Rect(canvas, 3.5, 9.3, 13.2, 9.4, brush, null, 1.4, 2.0);
                Poly(canvas, "M16.7,11.3 L21.2,8.9 L21.2,19.1 L16.7,16.7 Z", brush, 1.35);
                Circle(canvas, 9.9, 14, 2.0, brush, brush, 0);
                break;
            case "screenshot":
                // Proper digital still camera: top housing, shutter, double-ring lens and status light.
                Rect(canvas, 2.2, 7.2, 19.6, 12.4, brush, null, 1.4, 2.1);
                Poly(canvas, "M6.0,7.2 L7.7,4.8 L14.2,4.8 L16.0,7.2", brush, 1.35);
                Rect(canvas, 4.3, 5.6, 2.2, 1.5, brush, null, 1.0, 0.5);
                Circle(canvas, 12.1, 13.4, 4.0, brush, null, 1.35);
                Circle(canvas, 12.1, 13.4, 2.25, brush, null, 1.1);
                Circle(canvas, 18.4, 9.8, 0.75, brush, brush, 0);
                break;
            case "restore":
                Rect(canvas, 5, 7, 12, 11, brush, null, 1.3, 1.5);
                Rect(canvas, 8, 4, 12, 11, brush, null, 1.3, 1.5);
                break;
            default:
                Circle(canvas, 12, 12, 7.5, brush, null, 1.4);
                break;
        }
        return Wrap(canvas, size);
    }

    public static FrameworkElement CreateChartType(ChartVisualType type, double size, Brush brush)
    {
        var c = Canvas24();
        switch (type)
        {
            case ChartVisualType.Tick:
                Poly(c, "M3,15 L7,9 L10,13 L14,7 L18,11 L21,6", brush, 1.6);
                Circle(c, 7, 9, 1.1, brush, brush, 0); Circle(c, 14, 7, 1.1, brush, brush, 0); Circle(c, 21, 6, 1.1, brush, brush, 0);
                break;
            case ChartVisualType.Candles:
                // Canonical candlestick geometry based on the user's supplied reference:
                // square body, centered vertical wick, and clear separation of upper/lower wick segments.
                // Normal Candles use filled bodies so they remain visually distinct from Hollow Candles.
                ReferenceCandleGlyph(c, 4.8,  2.4, 20.8, 6.4, 13.6, brush, true);
                ReferenceCandleGlyph(c, 12.0, 4.8, 22.4, 8.8, 17.6, brush, true);
                ReferenceCandleGlyph(c, 19.2, 1.6, 18.4, 5.6, 12.8, brush, true);
                break;
            case ChartVisualType.HollowCandles:
                // Same centered-wick construction as Candles, with open/hollow bodies.
                ReferenceCandleGlyph(c, 4.8,  2.4, 20.8, 6.4, 13.6, brush, false);
                ReferenceCandleGlyph(c, 12.0, 4.8, 22.4, 8.8, 17.6, brush, false);
                ReferenceCandleGlyph(c, 19.2, 1.6, 18.4, 5.6, 12.8, brush, false);
                break;
            case ChartVisualType.Bars:
                Ohlc(c, 6, 5, 19, 9, 14, brush); Ohlc(c, 12, 4, 17, 7, 11, brush); Ohlc(c, 18, 7, 21, 12, 17, brush);
                break;
            case ChartVisualType.VolumeCandles:
                Candle(c, 7, 4, 16, 7, 11, brush, true); Candle(c, 14, 6, 18, 10, 15, brush, true); Candle(c, 20, 5, 15, 8, 12, brush, true);
                Rect(c, 4, 18, 4, 3, brush, brush, 0, 0); Rect(c, 10, 16, 4, 5, brush, brush, 0, 0); Rect(c, 16, 17, 4, 4, brush, brush, 0, 0);
                break;
            case ChartVisualType.Line:
                Poly(c, "M3,17 L7,13 L11,15 L15,8 L21,11", brush, 1.8); break;
            case ChartVisualType.LineWithMarkers:
                Poly(c, "M3,17 L7,13 L11,15 L15,8 L21,11", brush, 1.6);
                foreach (var p in new[]{(7d,13d),(11d,15d),(15d,8d),(21d,11d)}) Circle(c,p.Item1,p.Item2,1.25,brush,brush,0); break;
            case ChartVisualType.StepLine:
                Poly(c, "M3,17 L8,17 L8,13 L13,13 L13,8 L19,8 L19,11 L22,11", brush, 1.7); break;
            case ChartVisualType.Area:
            case ChartVisualType.HlcArea:
                Poly(c, "M3,16 L8,12 L12,14 L17,7 L21,10 L21,20 L3,20 Z", brush, 1.3, WithOpacity(brush, 0.22)); break;
            case ChartVisualType.Baseline:
                Line(c, 3, 12, 21, 12, brush, 1.1); Poly(c, "M3,16 L8,10 L12,14 L17,7 L21,9", brush, 1.7); break;
            case ChartVisualType.Columns:
                Rect(c, 4, 13, 4, 7, brush, brush, 0, 0); Rect(c, 10, 8, 4, 12, brush, brush, 0, 0); Rect(c, 16, 5, 4, 15, brush, brush, 0, 0); break;
            case ChartVisualType.HighLow:
                Line(c, 6, 5, 6, 19, brush, 1.6); Line(c, 12, 8, 12, 16, brush, 1.6); Line(c, 18, 4, 18, 20, brush, 1.6); break;
            case ChartVisualType.HeikinAshi:
                Candle(c, 6, 6, 18, 9, 13, brush, true); Candle(c, 12, 5, 16, 8, 12, brush, true); Candle(c, 18, 8, 20, 11, 16, brush, true); break;
            case ChartVisualType.Renko:
                Rect(c, 4, 13, 6, 6, brush, null, 1.3, 0); Rect(c, 9, 8, 6, 6, brush, null, 1.3, 0); Rect(c, 14, 3, 6, 6, brush, null, 1.3, 0); break;
            case ChartVisualType.LineBreak:
                Rect(c, 4, 11, 4, 8, brush, null, 1.3, 0); Rect(c, 9, 7, 4, 8, brush, null, 1.3, 0); Rect(c, 14, 4, 4, 8, brush, null, 1.3, 0); break;
            case ChartVisualType.Kagi:
                Poly(c, "M4,18 L9,18 L9,7 L14,7 L14,14 L20,14 L20,5", brush, 2.0); break;
            case ChartVisualType.PointAndFigure:
                Line(c,4,5,9,10,brush,1.3); Line(c,9,5,4,10,brush,1.3); Circle(c,15,8,2.5,brush,null,1.3); Line(c,4,14,9,19,brush,1.3); Line(c,9,14,4,19,brush,1.3); Circle(c,15,17,2.5,brush,null,1.3); break;
            case ChartVisualType.Range:
                Rect(c, 4, 5, 5, 14, brush, null, 1.3, 0); Rect(c, 10, 8, 5, 11, brush, null, 1.3, 0); Rect(c, 16, 4, 5, 15, brush, null, 1.3, 0); break;
            case ChartVisualType.TimePriceOpportunity:
                for(int i=0;i<4;i++){ Rect(c,4+i*4,5+i*2,3,14-i*3,brush,null,1.1,0);} break;
            case ChartVisualType.SessionVolumeProfile:
                Line(c, 5, 4, 5, 20, brush, 1.2); Rect(c, 6, 6, 10, 2.5, brush, brush, 0, 0); Rect(c,6,10,14,2.5,brush,brush,0,0); Rect(c,6,14,7,2.5,brush,brush,0,0); Rect(c,6,18,11,2.5,brush,brush,0,0); break;
            case ChartVisualType.VolumeFootprint:
                Rect(c, 6, 4, 12, 16, brush, null, 1.2, 1); Line(c,12,4,12,20,brush,1.0); Line(c,6,9,18,9,brush,1.0); Line(c,6,14,18,14,brush,1.0); break;
            default:
                Candle(c, 6, 5, 19, 9, 14, brush, true); Candle(c, 12, 4, 17, 7, 11, brush, true); Candle(c, 18, 7, 21, 12, 17, brush, true); break;
        }
        return Wrap(c, size);
    }

    private static Canvas Canvas24() => new() { Width = 24, Height = 24, SnapsToDevicePixels = true, UseLayoutRounding = true };
    private static FrameworkElement Wrap(Canvas c, double size) => new Viewbox
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        Child = c
    };
    private static void Line(Canvas c,double x1,double y1,double x2,double y2,Brush b,double t){ c.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=b,StrokeThickness=t,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round}); }
    private static void Circle(Canvas c,double x,double y,double r,Brush stroke,Brush? fill,double t){ var e=new Ellipse{Width=r*2,Height=r*2,Stroke=stroke,StrokeThickness=t,Fill=fill}; Canvas.SetLeft(e,x-r); Canvas.SetTop(e,y-r); c.Children.Add(e); }
    private static void Rect(Canvas c,double x,double y,double w,double h,Brush stroke,Brush? fill,double t,double radius){ var r=new Rectangle{Width=w,Height=h,Stroke=stroke,StrokeThickness=t,Fill=fill,RadiusX=radius,RadiusY=radius}; Canvas.SetLeft(r,x); Canvas.SetTop(r,y); c.Children.Add(r); }
    private static void Poly(Canvas c,string data,Brush stroke,double t,Brush? fill=null){ c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = t,
            Fill = fill,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        }); }
    private static Brush WithOpacity(Brush brush, double opacity) { Brush copy = brush.CloneCurrentValue(); copy.Opacity = opacity; return copy; }
    private static void Candle(Canvas c,double x,double high,double low,double top,double bottom,Brush b,bool filled){ Line(c,x,high,x,low,b,1.2); Rect(c,x-2,top,4,Math.Max(2,bottom-top),b,filled?b:null,1.1,0.4); }
    private static void ReferenceCandleGlyph(Canvas c,double centerX,double high,double low,double bodyTop,double bodyBottom,Brush b,bool filled)
    {
        // Every body and wick is derived from the exact same centerX. This guarantees that the
        // vertical wick stays mathematically centered in the rectangular candle body at every DPI.
        double top = Math.Min(bodyTop, bodyBottom);
        double bottom = Math.Max(bodyTop, bodyBottom);
        const double bodyWidth = 4.8;
        const double stroke = 1.6;

        c.Children.Add(new Line
        {
            X1 = centerX,
            Y1 = high,
            X2 = centerX,
            Y2 = top,
            Stroke = b,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
            SnapsToDevicePixels = true
        });

        var body = new Rectangle
        {
            Width = bodyWidth,
            Height = Math.Max(3.0, bottom - top),
            Stroke = b,
            StrokeThickness = stroke,
            Fill = filled ? b : null,
            RadiusX = 0,
            RadiusY = 0,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(body, centerX - bodyWidth / 2.0);
        Canvas.SetTop(body, top);
        c.Children.Add(body);

        c.Children.Add(new Line
        {
            X1 = centerX,
            Y1 = bottom,
            X2 = centerX,
            Y2 = low,
            Stroke = b,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
            SnapsToDevicePixels = true
        });
    }
    private static void Ohlc(Canvas c,double x,double high,double low,double open,double close,Brush b){ Line(c,x,high,x,low,b,1.25); Line(c,x-3,open,x,open,b,1.25); Line(c,x,close,x+3,close,b,1.25); }
}
