using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace TickLab.Desktop.Controls;

/// <summary>
/// Offline emoji artwork used by both the media picker and chart renderer.
/// Every emoji exposed by TickLab's Emoji catalog resolves to a bundled SVG
/// entry, so rendering does not depend on the Windows Segoe UI Emoji version.
/// The bundle is opened once and individual DrawingImage objects are parsed and
/// cached only when they become visible/used.
/// </summary>
internal static class EmojiVectorAssets
{
    private const string BundleResource = "Assets/Emoji/emoji-vector-assets.zip";

    private static readonly Dictionary<string, DrawingImage> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheGate = new();
    private static readonly object ArchiveGate = new();
    private static MemoryStream? _archiveBytes;
    private static ZipArchive? _archive;

    private static readonly Regex NumberRegex = new(
        @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TransformRegex = new(
        @"(?<name>matrix|translate|scale|rotate)\s*\((?<args>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool HasAsset(string emoji)
    {
        if (string.IsNullOrEmpty(emoji))
            return false;

        string entryName = AssetEntryName(emoji);
        lock (ArchiveGate)
        {
            return EnsureArchiveNoThrow() && _archive!.GetEntry(entryName) is not null;
        }
    }

    public static bool TryGetDrawingImage(string emoji, out DrawingImage image)
    {
        if (string.IsNullOrEmpty(emoji))
        {
            image = null!;
            return false;
        }

        lock (CacheGate)
        {
            if (Cache.TryGetValue(emoji, out DrawingImage? cached))
            {
                image = cached;
                return true;
            }
        }

        byte[] svgBytes;
        lock (ArchiveGate)
        {
            if (!EnsureArchiveNoThrow())
            {
                image = null!;
                return false;
            }

            ZipArchiveEntry? entry = _archive!.GetEntry(AssetEntryName(emoji));
            if (entry is null)
            {
                image = null!;
                return false;
            }

            using Stream source = entry.Open();
            using var copy = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            source.CopyTo(copy);
            svgBytes = copy.ToArray();
        }

        try
        {
            using var stream = new MemoryStream(svgBytes, writable: false);
            XDocument document = XDocument.Load(stream, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null)
            {
                image = null!;
                return false;
            }

            DrawingGroup drawing = BuildSvgDrawing(root);
            if (drawing.CanFreeze)
                drawing.Freeze();
            image = new DrawingImage(drawing);
            if (image.CanFreeze)
                image.Freeze();

            lock (CacheGate)
                Cache[emoji] = image;
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    public static DrawingImage GetDrawingImageOrPlaceholder(string emoji)
    {
        if (TryGetDrawingImage(emoji, out DrawingImage image))
            return image;

        // Defensive fallback for malformed old workspace data. This is a vector
        // placeholder, never a system-font tofu rectangle.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(63, 81, 181)),
            null,
            new EllipseGeometry(new Rect(2, 2, 44, 44))));
        group.Children.Add(new GeometryDrawing(
            Brushes.White,
            null,
            Geometry.Parse("M20,13 C20,8 24,6 29,6 C35,6 40,10 40,16 C40,22 36,25 31,28 C28,30 27,32 27,36 L21,36 C21,31 22,27 27,24 C31,21 34,20 34,16 C34,13 32,11 29,11 C26,11 25,13 25,16 Z M21,39 L28,39 L28,46 L21,46 Z")));
        if (group.CanFreeze)
            group.Freeze();
        var fallback = new DrawingImage(group);
        if (fallback.CanFreeze)
            fallback.Freeze();
        return fallback;
    }

    private static bool EnsureArchiveNoThrow()
    {
        if (_archive is not null)
            return true;

        try
        {
            Uri uri = new($"pack://application:,,,/TickLab;component/{BundleResource}", UriKind.Absolute);
            System.Windows.Resources.StreamResourceInfo? resource = Application.GetResourceStream(uri);
            if (resource?.Stream is null)
                return false;

            _archiveBytes = new MemoryStream();
            using (resource.Stream)
                resource.Stream.CopyTo(_archiveBytes);
            _archiveBytes.Position = 0;
            _archive = new ZipArchive(_archiveBytes, ZipArchiveMode.Read, leaveOpen: true);
            return true;
        }
        catch
        {
            _archive?.Dispose();
            _archive = null;
            _archiveBytes?.Dispose();
            _archiveBytes = null;
            return false;
        }
    }

    private static string AssetEntryName(string emoji)
    {
        var builder = new StringBuilder();
        foreach (Rune rune in emoji.EnumerateRunes())
        {
            if (builder.Length > 0)
                builder.Append('-');
            builder.Append(rune.Value.ToString("X", CultureInfo.InvariantCulture));
        }
        builder.Append(".svg");
        return builder.ToString();
    }

    private static DrawingGroup BuildSvgDrawing(XElement root)
    {
        double[] viewBox = ParseNumbers((string?)root.Attribute("viewBox") ?? string.Empty);
        double x = 0, y = 0, width = 512, height = 512;
        if (viewBox.Length >= 4 && viewBox[2] > 0 && viewBox[3] > 0)
        {
            x = viewBox[0];
            y = viewBox[1];
            width = viewBox[2];
            height = viewBox[3];
        }

        var rootGroup = new DrawingGroup();
        rootGroup.Children.Add(new GeometryDrawing(
            Brushes.Transparent,
            null,
            new RectangleGeometry(new Rect(x, y, width, height))));

        SvgStyle defaultStyle = new("#000000", null, 1.0, PenLineCap.Flat, PenLineJoin.Miter, 10.0, 1.0);
        foreach (XElement child in root.Elements())
        {
            Drawing? drawing = BuildElement(child, defaultStyle);
            if (drawing is not null)
                rootGroup.Children.Add(drawing);
        }
        return rootGroup;
    }

    private static Drawing? BuildElement(XElement element, SvgStyle inherited)
    {
        string name = element.Name.LocalName.ToLowerInvariant();
        SvgStyle style = ResolveStyle(element, inherited);

        Drawing? drawing = name switch
        {
            "g" => BuildGroup(element, style),
            "path" => BuildPath(element, style),
            "circle" => BuildCircle(element, style),
            "ellipse" => BuildEllipse(element, style),
            "rect" => BuildRect(element, style),
            "line" => BuildLine(element, style),
            "polygon" => BuildPoints(element, style, close: true),
            "polyline" => BuildPoints(element, style, close: false),
            _ => null
        };

        if (drawing is null)
            return null;

        Matrix matrix = ParseTransform((string?)element.Attribute("transform"));
        if (!matrix.IsIdentity)
        {
            var wrapper = new DrawingGroup { Transform = new MatrixTransform(matrix) };
            wrapper.Children.Add(drawing);
            return wrapper;
        }
        return drawing;
    }

    private static DrawingGroup BuildGroup(XElement element, SvgStyle style)
    {
        var group = new DrawingGroup();
        foreach (XElement child in element.Elements())
        {
            Drawing? drawing = BuildElement(child, style);
            if (drawing is not null)
                group.Children.Add(drawing);
        }
        return group;
    }

    private static GeometryDrawing? BuildPath(XElement element, SvgStyle style)
    {
        string? data = (string?)element.Attribute("d");
        if (string.IsNullOrWhiteSpace(data))
            return null;
        try
        {
            Geometry geometry = Geometry.Parse(data);
            return CreateGeometryDrawing(geometry, style);
        }
        catch
        {
            return null;
        }
    }

    private static GeometryDrawing BuildCircle(XElement element, SvgStyle style)
    {
        double cx = Number(element, "cx");
        double cy = Number(element, "cy");
        double r = Math.Abs(Number(element, "r"));
        return CreateGeometryDrawing(new EllipseGeometry(new Point(cx, cy), r, r), style);
    }

    private static GeometryDrawing BuildEllipse(XElement element, SvgStyle style)
    {
        double cx = Number(element, "cx");
        double cy = Number(element, "cy");
        double rx = Math.Abs(Number(element, "rx"));
        double ry = Math.Abs(Number(element, "ry"));
        return CreateGeometryDrawing(new EllipseGeometry(new Point(cx, cy), rx, ry), style);
    }

    private static GeometryDrawing BuildRect(XElement element, SvgStyle style)
    {
        double x = Number(element, "x");
        double y = Number(element, "y");
        double width = Math.Max(0, Number(element, "width"));
        double height = Math.Max(0, Number(element, "height"));
        double rx = Math.Abs(Number(element, "rx"));
        double ry = Math.Abs(Number(element, "ry"));
        return CreateGeometryDrawing(new RectangleGeometry(new Rect(x, y, width, height), rx, ry), style);
    }

    private static GeometryDrawing BuildLine(XElement element, SvgStyle style)
    {
        var geometry = new LineGeometry(
            new Point(Number(element, "x1"), Number(element, "y1")),
            new Point(Number(element, "x2"), Number(element, "y2")));
        return CreateGeometryDrawing(geometry, style);
    }

    private static GeometryDrawing? BuildPoints(XElement element, SvgStyle style, bool close)
    {
        string? raw = (string?)element.Attribute("points");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        double[] numbers = ParseNumbers(raw);
        if (numbers.Length < 4)
            return null;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(numbers[0], numbers[1]), style.Fill is not null, close);
            for (int i = 2; i + 1 < numbers.Length; i += 2)
                context.LineTo(new Point(numbers[i], numbers[i + 1]), isStroked: true, isSmoothJoin: true);
        }
        return CreateGeometryDrawing(geometry, style);
    }

    private static GeometryDrawing CreateGeometryDrawing(Geometry geometry, SvgStyle style)
    {
        Brush? fill = ApplyOpacity(ParseBrush(style.Fill), style.Opacity);
        Pen? pen = null;
        Brush? stroke = ApplyOpacity(ParseBrush(style.Stroke), style.Opacity);
        if (stroke is not null && style.StrokeWidth > 0)
        {
            pen = new Pen(stroke, style.StrokeWidth)
            {
                StartLineCap = style.LineCap,
                EndLineCap = style.LineCap,
                DashCap = style.LineCap,
                LineJoin = style.LineJoin,
                MiterLimit = style.MiterLimit
            };
        }
        return new GeometryDrawing(fill, pen, geometry);
    }

    private static Brush? ApplyOpacity(Brush? brush, double opacity)
    {
        if (brush is null)
            return null;
        opacity = Math.Clamp(opacity, 0, 1);
        if (opacity >= 0.999)
            return brush;
        if (brush is SolidColorBrush solid)
        {
            Color c = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B));
        }
        Brush clone = brush.Clone();
        clone.Opacity *= opacity;
        return clone;
    }

    private static SvgStyle ResolveStyle(XElement element, SvgStyle inherited)
    {
        string? fill = AttributeOr(element, "fill", inherited.Fill);
        string? stroke = AttributeOr(element, "stroke", inherited.Stroke);
        double width = TryNumber((string?)element.Attribute("stroke-width"), inherited.StrokeWidth);
        double miter = TryNumber((string?)element.Attribute("stroke-miterlimit"), inherited.MiterLimit);
        PenLineCap cap = ParseCap((string?)element.Attribute("stroke-linecap"), inherited.LineCap);
        PenLineJoin join = ParseJoin((string?)element.Attribute("stroke-linejoin"), inherited.LineJoin);
        double opacity = inherited.Opacity;
        opacity *= Math.Clamp(TryNumber((string?)element.Attribute("opacity"), 1.0), 0, 1);
        opacity *= Math.Clamp(TryNumber((string?)element.Attribute("fill-opacity"), 1.0), 0, 1);
        return new SvgStyle(fill, stroke, width, cap, join, miter, opacity);
    }

    private static string? AttributeOr(XElement element, string name, string? fallback)
    {
        XAttribute? attribute = element.Attribute(name);
        return attribute is null ? fallback : attribute.Value;
    }

    private static PenLineCap ParseCap(string? value, PenLineCap fallback) => value?.ToLowerInvariant() switch
    {
        "round" => PenLineCap.Round,
        "square" => PenLineCap.Square,
        "butt" => PenLineCap.Flat,
        _ => fallback
    };

    private static PenLineJoin ParseJoin(string? value, PenLineJoin fallback) => value?.ToLowerInvariant() switch
    {
        "round" => PenLineJoin.Round,
        "bevel" => PenLineJoin.Bevel,
        "miter" => PenLineJoin.Miter,
        _ => fallback
    };

    private static Brush? ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return null;

        string normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            string hex = normalized[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                return new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
                return new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        }

        try
        {
            object? converted = ColorConverter.ConvertFromString(normalized);
            if (converted is Color color)
                return new SolidColorBrush(color);
        }
        catch
        {
            // Ignore invalid optional SVG paint.
        }
        return null;
    }

    private static Matrix ParseTransform(string? raw)
    {
        Matrix result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (Match match in TransformRegex.Matches(raw))
        {
            string name = match.Groups["name"].Value.ToLowerInvariant();
            double[] args = ParseNumbers(match.Groups["args"].Value);
            Matrix next = Matrix.Identity;
            switch (name)
            {
                case "matrix" when args.Length >= 6:
                    next = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                    break;
                case "translate" when args.Length >= 1:
                    next.Translate(args[0], args.Length >= 2 ? args[1] : 0);
                    break;
                case "scale" when args.Length >= 1:
                    next.Scale(args[0], args.Length >= 2 ? args[1] : args[0]);
                    break;
                case "rotate" when args.Length >= 1:
                    if (args.Length >= 3)
                        next.RotateAt(args[0], args[1], args[2]);
                    else
                        next.Rotate(args[0]);
                    break;
            }
            result.Append(next);
        }
        return result;
    }

    private static double Number(XElement element, string attribute) =>
        TryNumber((string?)element.Attribute(attribute), 0.0);

    private static double TryNumber(string? raw, double fallback) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

    private static double[] ParseNumbers(string raw) => NumberRegex.Matches(raw)
        .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
        .ToArray();

    private readonly record struct SvgStyle(
        string? Fill,
        string? Stroke,
        double StrokeWidth,
        PenLineCap LineCap,
        PenLineJoin LineJoin,
        double MiterLimit,
        double Opacity);
}
