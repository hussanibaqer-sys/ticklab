using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace TickLab.Desktop.Settings;

public static class ColorDisplayHelper
{
    private static readonly IReadOnlyDictionary<uint, string> NamedColors = BuildNamedColors();

    public static string GetName(string? value)
    {
        if (!TryParse(value, out Color color))
            return "Custom colour";

        uint key = ToKey(color);
        return NamedColors.TryGetValue(key, out string name)
            ? name
            : "Custom colour";
    }

    public static SolidColorBrush GetBrush(string? value, Color? fallback = null)
    {
        Color color = TryParse(value, out Color parsed)
            ? parsed
            : fallback ?? Colors.Transparent;
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    public static void ApplyToButton(Button button, string? value)
    {
        string stored = Normalize(value);
        button.Tag = stored;
        button.Content = null;
        button.Background = GetBrush(stored, Colors.Transparent);
        button.ToolTip = CreateNameToolTip(stored);
        ToolTipService.SetInitialShowDelay(button, 1000);
        ToolTipService.SetShowDuration(button, 5000);
        ToolTipService.SetBetweenShowDelay(button, 100);
    }

    public static void ApplyToolTip(FrameworkElement element, string? value)
    {
        element.ToolTip = CreateNameToolTip(value);
        ToolTipService.SetInitialShowDelay(element, 1000);
        ToolTipService.SetShowDuration(element, 5000);
        ToolTipService.SetBetweenShowDelay(element, 100);
    }

    public static string Normalize(string? value, string fallback = "#000000")
    {
        if (!TryParse(value, out Color color))
            return fallback;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static ToolTip CreateNameToolTip(string? value) => new()
    {
        Content = GetName(value),
        Placement = PlacementMode.Mouse,
        HasDropShadow = true
    };

    private static IReadOnlyDictionary<uint, string> BuildNamedColors()
    {
        var result = new Dictionary<uint, string>();

        // Prefer familiar names for TickLab's palette before falling back to
        // the full WPF named-colour catalogue.
        Add(result, "Black", "#000000");
        Add(result, "Default Chart Background", "#07101B");
        Add(result, "Near Black", "#080808");
        Add(result, "Charcoal", "#202020");
        Add(result, "Dark grey", "#404040");
        Add(result, "Grey", "#606060");
        Add(result, "Mid grey", "#808080");
        Add(result, "Silver", "#A0A0A0");
        Add(result, "Light grey", "#C0C0C0");
        Add(result, "Pale grey", "#E0E0E0");
        Add(result, "Off white", "#F2F2F2");
        Add(result, "White", "#FFFFFF");
        Add(result, "Dark red", "#7A0000");
        Add(result, "Red", "#C00000");
        Add(result, "Bright red", "#FF0000");
        Add(result, "Coral", "#FF5050");
        Add(result, "Light coral", "#FF9999");
        Add(result, "Dark orange", "#8A3A00");
        Add(result, "Orange", "#D85A00");
        Add(result, "Bright orange", "#FF8C00");
        Add(result, "Amber", "#FFB000");
        Add(result, "Peach", "#FFD0A0");
        Add(result, "Dark yellow", "#7A6500");
        Add(result, "Ochre", "#B08A00");
        Add(result, "Gold", "#E0B000");
        Add(result, "Yellow", "#FFFF00");
        Add(result, "Light yellow", "#FFF3A0");
        Add(result, "Dark lime", "#3C6500");
        Add(result, "Olive green", "#648A00");
        Add(result, "Lime", "#8BCF00");
        Add(result, "Bright lime", "#B6FF00");
        Add(result, "Pale lime", "#DFFF9F");
        Add(result, "Dark green", "#005A20");
        Add(result, "Green", "#008A32");
        Add(result, "Bright green", "#00C853");
        Add(result, "Mint", "#53D98A");
        Add(result, "Pale mint", "#A8F0C5");
        Add(result, "Dark teal", "#005A5A");
        Add(result, "Teal", "#008C8C");
        Add(result, "Cyan", "#00C8C8");
        Add(result, "Bright cyan", "#00FFFF");
        Add(result, "Pale cyan", "#A6FFFF");
        Add(result, "Dark blue", "#003B73");
        Add(result, "Blue", "#005EB8");
        Add(result, "Bright blue", "#0078D7");
        Add(result, "Sky blue", "#4AA3FF");
        Add(result, "Pale blue", "#A9D4FF");
        Add(result, "Navy", "#111A70");
        Add(result, "Indigo", "#2937A8");
        Add(result, "Royal blue", "#4169E1");
        Add(result, "Periwinkle", "#7D8FFF");
        Add(result, "Lavender blue", "#BEC6FF");
        Add(result, "Dark purple", "#4B166E");
        Add(result, "Purple", "#6F2DA8");
        Add(result, "Violet", "#8A2BE2");
        Add(result, "Light violet", "#B26CFF");
        Add(result, "Pale violet", "#D9B8FF");
        Add(result, "Dark magenta", "#72004E");
        Add(result, "Magenta", "#B00078");
        Add(result, "Bright magenta", "#FF00A8");
        Add(result, "Pink", "#FF5EBB");
        Add(result, "Light pink", "#FFB3DD");
        Add(result, "Dark brown", "#4A2600");
        Add(result, "Brown", "#7A3E00");
        Add(result, "Sienna", "#A65A20");
        Add(result, "Tan", "#D09560");
        Add(result, "Light tan", "#E8C6A3");
        Add(result, "Dark rose", "#6A2638");
        Add(result, "Rose", "#A33B57");
        Add(result, "Salmon", "#E0647D");
        Add(result, "Light salmon", "#F49AAA");
        Add(result, "Pale rose", "#F7CAD3");

        foreach (PropertyInfo property in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.PropertyType != typeof(Color) || property.GetValue(null) is not Color color)
                continue;
            result.TryAdd(ToKey(color), SplitPascalCase(property.Name));
        }

        return result;
    }

    private static void Add(IDictionary<uint, string> map, string name, string value)
    {
        if (TryParse(value, out Color color))
            map[ToKey(color)] = name;
    }

    private static uint ToKey(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    private static bool TryParse(string? value, out Color color)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString((value ?? string.Empty).Trim());
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
        }

        color = default;
        return false;
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var characters = new List<char>(value.Length + 8) { value[0] };
        for (int i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && char.IsLower(value[i - 1]))
                characters.Add(' ');
            characters.Add(value[i]);
        }
        return new string(characters.ToArray());
    }
}

public sealed class ColorValueToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ColorDisplayHelper.GetBrush(value?.ToString(), Colors.Transparent);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ColorValueToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ColorDisplayHelper.GetName(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
