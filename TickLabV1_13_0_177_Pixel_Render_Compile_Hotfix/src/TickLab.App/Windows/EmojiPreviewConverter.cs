using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop.Windows;

/// <summary>
/// Creates and caches bundled vector emoji previews on demand. With the picker
/// row list virtualized, this converter is only invoked for emoji cells that are
/// actually on-screen; no Windows emoji-font lookup is performed.
/// </summary>
public sealed class EmojiPreviewConverter : IValueConverter
{
    private static readonly Dictionary<string, DrawingImage> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string mark = value as string ?? string.Empty;
        if (string.IsNullOrEmpty(mark))
            return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(mark, out DrawingImage? cached))
                return cached;
        }

        DrawingImage image = EmojiVectorAssets.GetDrawingImageOrPlaceholder(mark);

        lock (Gate)
            Cache[mark] = image;
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
