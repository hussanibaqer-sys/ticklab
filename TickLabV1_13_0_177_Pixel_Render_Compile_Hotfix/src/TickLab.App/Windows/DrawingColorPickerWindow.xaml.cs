using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class DrawingColorPickerWindow : Window
{
    private sealed record PaletteColour(string Name, string Hex, Color Color);

    // Familiar Paint-style palette: neutrals first, then colour families from
    // dark to light. Every visual swatch and click value comes from this same
    // immutable entry, so the displayed colour can never map to another value.
    private static readonly PaletteColour[] BasicColours =
    {
        P("Black", "#000000"), P("Default Chart Background", "#07101B"), P("Near Black", "#080808"), P("Charcoal", "#202020"), P("Dark grey", "#404040"), P("Grey", "#606060"), P("Mid grey", "#808080"),
        P("Silver", "#A0A0A0"), P("Light grey", "#C0C0C0"), P("Pale grey", "#E0E0E0"), P("Off white", "#F2F2F2"), P("White", "#FFFFFF"),

        P("Dark red", "#7A0000"), P("Red", "#C00000"), P("Bright red", "#FF0000"), P("Coral", "#FF5050"), P("Light coral", "#FF9999"),
        P("Dark orange", "#8A3A00"), P("Orange", "#D85A00"), P("Bright orange", "#FF8C00"), P("Amber", "#FFB000"), P("Peach", "#FFD0A0"),

        P("Dark yellow", "#7A6500"), P("Ochre", "#B08A00"), P("Gold", "#E0B000"), P("Yellow", "#FFFF00"), P("Light yellow", "#FFF3A0"),
        P("Dark lime", "#3C6500"), P("Olive green", "#648A00"), P("Lime", "#8BCF00"), P("Bright lime", "#B6FF00"), P("Pale lime", "#DFFF9F"),

        P("Dark green", "#005A20"), P("Green", "#008A32"), P("Bright green", "#00C853"), P("Mint", "#53D98A"), P("Pale mint", "#A8F0C5"),
        P("Dark teal", "#005A5A"), P("Teal", "#008C8C"), P("Cyan", "#00C8C8"), P("Bright cyan", "#00FFFF"), P("Pale cyan", "#A6FFFF"),

        P("Dark blue", "#003B73"), P("Blue", "#005EB8"), P("Bright blue", "#0078D7"), P("Sky blue", "#4AA3FF"), P("Pale blue", "#A9D4FF"),
        P("Navy", "#111A70"), P("Indigo", "#2937A8"), P("Royal blue", "#4169E1"), P("Periwinkle", "#7D8FFF"), P("Lavender blue", "#BEC6FF"),

        P("Dark purple", "#4B166E"), P("Purple", "#6F2DA8"), P("Violet", "#8A2BE2"), P("Light violet", "#B26CFF"), P("Pale violet", "#D9B8FF"),
        P("Dark magenta", "#72004E"), P("Magenta", "#B00078"), P("Bright magenta", "#FF00A8"), P("Pink", "#FF5EBB"), P("Light pink", "#FFB3DD"),

        P("Dark brown", "#4A2600"), P("Brown", "#7A3E00"), P("Sienna", "#A65A20"), P("Tan", "#D09560"), P("Light tan", "#E8C6A3"),
        P("Dark rose", "#6A2638"), P("Rose", "#A33B57"), P("Salmon", "#E0647D"), P("Light salmon", "#F49AAA"), P("Pale rose", "#F7CAD3")
    };

    private readonly List<(Button Button, PaletteColour Entry)> _paletteButtons = new();
    private readonly string _initialColor;
    private bool _updating;

    public DrawingColorPickerWindow(string initialColor)
    {
        InitializeComponent();
        _initialColor = Normalize(initialColor, "#000000");
        SelectedColor = _initialColor;
        CurrentColorBorder.Background = Brush(_initialColor);
        BuildPalette();
        SetSelectedColor(_initialColor, updateEditors: true, raisePreview: false);

        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(RestoreExactPaletteVisuals));
    }

    public string SelectedColor { get; private set; }
    public event Action<string>? ColorPreviewChanged;

    private static PaletteColour P(string name, string hex)
    {
        string normalized = Normalize(hex, "#000000");
        _ = TryParseColor(normalized, out Color color);
        return new PaletteColour(name, normalized, color);
    }

    private void BuildPalette()
    {
        PalettePanel.Children.Clear();
        _paletteButtons.Clear();

        foreach (PaletteColour entry in BasicColours)
        {
            var button = new Button
            {
                Width = 36,
                Height = 36,
                MinHeight = 36,
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                Tag = entry,
                Background = new SolidColorBrush(entry.Color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderThickness = new Thickness(1),
                Focusable = false,
                Style = (Style)FindResource("PaletteSwatchButton")
            };

            ThemeColorScope.SetPreserveExactColors(button, true);
            ColorDisplayHelper.ApplyToolTip(button, entry.Hex);
            button.Click += PaletteButton_Click;
            _paletteButtons.Add((button, entry));
            PalettePanel.Children.Add(button);
        }
    }

    private void PaletteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PaletteColour entry })
            SetSelectedColor(entry.Hex, updateEditors: true, raisePreview: true);
        e.Handled = true;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
            return;

        string normalized = Normalize(HexBox.Text, string.Empty);
        if (string.IsNullOrEmpty(normalized))
        {
            ValidationText.Text = "Enter a valid #RRGGBB colour.";
            return;
        }

        SetSelectedColor(normalized, updateEditors: true, raisePreview: true);
    }

    private void ComponentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
            return;

        if (!TryReadComponent(RedBox.Text, out byte red) ||
            !TryReadComponent(GreenBox.Text, out byte green) ||
            !TryReadComponent(BlueBox.Text, out byte blue))
        {
            ValidationText.Text = "RGB values must be whole numbers from 0 to 255.";
            return;
        }

        SetSelectedColor($"#{red:X2}{green:X2}{blue:X2}", updateEditors: false, raisePreview: true);
        _updating = true;
        HexBox.Text = SelectedColor;
        _updating = false;
    }


    private void ComponentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || RedSlider is null || GreenSlider is null || BlueSlider is null)
            return;

        byte red = (byte)Math.Clamp((int)Math.Round(RedSlider.Value), 0, 255);
        byte green = (byte)Math.Clamp((int)Math.Round(GreenSlider.Value), 0, 255);
        byte blue = (byte)Math.Clamp((int)Math.Round(BlueSlider.Value), 0, 255);
        SetSelectedColor($"#{red:X2}{green:X2}{blue:X2}", updateEditors: false, raisePreview: true);
    }

    private void SetSelectedColor(string value, bool updateEditors, bool raisePreview)
    {
        string normalized = Normalize(value, string.Empty);
        if (string.IsNullOrEmpty(normalized) || !TryParseColor(normalized, out Color color))
            return;

        SelectedColor = normalized;
        ValidationText.Text = string.Empty;
        NewColorBorder.Background = new SolidColorBrush(color);

        if (updateEditors)
        {
            _updating = true;
            HexBox.Text = normalized;
            RedBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            _updating = false;
        }

        PaletteColour? matchingEntry = BasicColours.FirstOrDefault(entry =>
            string.Equals(entry.Hex, normalized, StringComparison.OrdinalIgnoreCase));
        SelectedColourText.Text = matchingEntry?.Name ?? ColorDisplayHelper.GetName(normalized);

        foreach ((Button button, PaletteColour entry) in _paletteButtons)
        {
            bool selected = string.Equals(entry.Hex, normalized, StringComparison.OrdinalIgnoreCase);
            button.Background = new SolidColorBrush(entry.Color);
            button.BorderThickness = selected ? new Thickness(3) : new Thickness(1);
            button.BorderBrush = selected
                ? new SolidColorBrush(IsDark(entry.Color) ? Colors.White : Colors.Black)
                : new SolidColorBrush(Color.FromRgb(100, 100, 100));
        }

        ColorDisplayHelper.ApplyToolTip(CurrentColorBorder, _initialColor);
        ColorDisplayHelper.ApplyToolTip(NewColorBorder, normalized);

        if (raisePreview)
            ColorPreviewChanged?.Invoke(normalized);
    }

    private void RestoreExactPaletteVisuals()
    {
        // The application theme intentionally skips these controls. This final
        // loaded pass is an additional guard against any third-party/global
        // style changing a neutral swatch such as white, grey, or black.
        foreach ((Button button, PaletteColour entry) in _paletteButtons)
        {
            ThemeColorScope.SetPreserveExactColors(button, true);
            button.Background = new SolidColorBrush(entry.Color);
        }

        CurrentColorBorder.Background = Brush(_initialColor);
        NewColorBorder.Background = Brush(SelectedColor);
        SetSelectedColor(SelectedColor, updateEditors: false, raisePreview: false);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        string normalized = Normalize(SelectedColor, string.Empty);
        if (string.IsNullOrEmpty(normalized))
        {
            ValidationText.Text = "Choose a valid colour before continuing.";
            return;
        }

        SelectedColor = normalized;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = _initialColor;
        DialogResult = false;
    }

    private static bool TryReadComponent(string? text, out byte value) =>
        byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool IsDark(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) < 130;

    private static string Normalize(string? input, string fallback)
    {
        string value = (input ?? string.Empty).Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;
        if (value.Length == 9)
            value = "#" + value[^6..];
        if (value.Length != 7 || !TryParseColor(value, out _))
            return fallback;
        return value.ToUpperInvariant();
    }

    private static SolidColorBrush Brush(string value) =>
        new(TryParseColor(value, out Color color) ? color : Colors.Transparent);

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString(value);
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // Validation text is shown by the caller.
        }

        color = default;
        return false;
    }
}
