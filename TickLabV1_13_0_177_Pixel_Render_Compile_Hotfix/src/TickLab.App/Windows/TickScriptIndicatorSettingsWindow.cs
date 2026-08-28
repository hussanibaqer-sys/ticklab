using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Scripting;
using TickLab.Core.Settings;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed class TickScriptIndicatorSettingsWindow : Window
{
    private readonly TickScriptEntry _entry;
    private readonly Button _lineColor;
    private readonly ComboBox _lineWidth;
    private readonly ComboBox _lineStyle;
    private readonly Button _upperLevelColor;
    private readonly Button _lowerLevelColor;
    private readonly ComboBox _levelWidth;
    private readonly ComboBox _levelStyle;
    private readonly Button _fillColor;
    private readonly TextBox _fillOpacity;
    private readonly Button _labelColor;
    private readonly CheckBox _visible;
    private readonly TextBlock _status;
    private readonly IndicatorPlacementEditor? _placementEditor;

    public TickScriptIndicatorSettingsWindow(
        TickScriptEntry entry,
        TickScriptIndicatorAppearance appearance,
        IndicatorPlacementOptions? placementOptions = null)
    {
        _entry = entry;
        appearance ??= TickScriptIndicatorAppearance.Default;
        _placementEditor = placementOptions is null ? null : new IndicatorPlacementEditor(placementOptions);

        Title = $"{entry.Name} — Properties";
        Width = 690;
        Height = 560;
        MinWidth = 610;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brush("#111111");
        Foreground = Brushes.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "TickScript indicator colours and display properties",
            Foreground = Brush("#B8B8B8"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        root.Children.Add(header);

        var panel = new StackPanel { Margin = new Thickness(8) };
        _visible = Check("Show indicator", appearance.Visible);
        panel.Children.Add(_visible);
        panel.Children.Add(Section("Main series"));
        _lineColor = ColorButton(appearance.LineColor);
        _lineWidth = WidthBox(appearance.LineWidth);
        _lineStyle = StyleBox(appearance.LineStyle);
        panel.Children.Add(FormRow("Line colour", _lineColor));
        panel.Children.Add(FormRow("Line width", _lineWidth));
        panel.Children.Add(FormRow("Line style", _lineStyle));

        panel.Children.Add(Section("Horizontal levels"));
        _upperLevelColor = ColorButton(appearance.UpperLevelColor);
        _lowerLevelColor = ColorButton(appearance.LowerLevelColor);
        _levelWidth = WidthBox(appearance.LevelWidth);
        _levelStyle = StyleBox(appearance.LevelLineStyle);
        panel.Children.Add(FormRow("Upper level colour", _upperLevelColor));
        panel.Children.Add(FormRow("Lower level colour", _lowerLevelColor));
        panel.Children.Add(FormRow("Level width", _levelWidth));
        panel.Children.Add(FormRow("Level style", _levelStyle));

        panel.Children.Add(Section("Fill and labels"));
        _fillColor = ColorButton(appearance.FillColor);
        _fillOpacity = Text(appearance.FillOpacity.ToString("0.##", CultureInfo.InvariantCulture));
        _labelColor = ColorButton(appearance.LabelColor);
        panel.Children.Add(FormRow("Fill colour", _fillColor));
        panel.Children.Add(FormRow("Fill opacity (0–1)", _fillOpacity));
        panel.Children.Add(FormRow("Label colour", _labelColor));

        FrameworkElement body;
        var appearanceScroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        if (_placementEditor is null)
        {
            body = appearanceScroll;
        }
        else
        {
            var tabs = new TabControl { Background = Brush("#151515"), BorderBrush = Brush("#444444") };
            tabs.Items.Add(new TabItem { Header = "Appearance", Content = appearanceScroll });
            tabs.Items.Add(new TabItem { Header = "Placement", Content = _placementEditor.BuildView() });
            body = tabs;
        }
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status = new TextBlock { Foreground = Brush("#F0A0A0"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_status);
        Button editor = MakeButton("Open code editor", (_, _) => { OpenCodeEditorRequested = true; DialogResult = false; Close(); });
        Button ok = MakeButton("OK", Ok_Click, "#2F6DB2");
        Button cancel = MakeButton("Cancel", (_, _) => { DialogResult = false; Close(); });
        Grid.SetColumn(editor, 1);
        Grid.SetColumn(ok, 2);
        Grid.SetColumn(cancel, 3);
        footer.Children.Add(editor);
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    public TickScriptIndicatorAppearance Result { get; private set; } = TickScriptIndicatorAppearance.Default;
    public IndicatorPlacementResult? PlacementResult { get; private set; }
    public bool OpenCodeEditorRequested { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(_fillOpacity.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) ||
            opacity < 0 || opacity > 1)
        {
            _status.Text = "Fill opacity must be between 0 and 1.";
            return;
        }

        Result = new TickScriptIndicatorAppearance
        {
            LineColor = ColorValue(_lineColor, "#5B86C4"),
            LineWidth = SelectedWidth(_lineWidth, 1.6),
            LineStyle = SelectedStyle(_lineStyle, ChartLineStyle.Solid),
            UpperLevelColor = ColorValue(_upperLevelColor, "#F59E0B"),
            LowerLevelColor = ColorValue(_lowerLevelColor, "#F59E0B"),
            LevelWidth = SelectedWidth(_levelWidth, 1.0),
            LevelLineStyle = SelectedStyle(_levelStyle, ChartLineStyle.Dashed),
            FillColor = ColorValue(_fillColor, "#5B86C4"),
            FillOpacity = opacity,
            LabelColor = ColorValue(_labelColor, "#D8D8D8"),
            Visible = _visible.IsChecked == true
        };
        try
        {
            PlacementResult = _placementEditor?.Capture();
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            return;
        }
        DialogResult = true;
        Close();
    }

    private Button ColorButton(string initial)
    {
        var button = MakeButton(initial, (_, _) => { });
        ThemeColorScope.SetPreserveExactColors(button, true);
        SetColorButton(button, initial);
        button.Click += (_, _) =>
        {
            string current = button.Tag?.ToString() ?? initial;
            var picker = new DrawingColorPickerWindow(current) { Owner = this };
            if (picker.ShowDialog() == true)
                SetColorButton(button, picker.SelectedColor);
        };
        return button;
    }

    private static void SetColorButton(Button button, string value) =>
        ColorDisplayHelper.ApplyToButton(button, value);

    private static string ColorValue(Button button, string fallback) =>
        string.IsNullOrWhiteSpace(button.Tag?.ToString()) ? fallback : button.Tag!.ToString()!;

    private static double SelectedWidth(ComboBox box, double fallback) =>
        box.SelectedItem is double value ? value : fallback;

    private static ChartLineStyle SelectedStyle(ComboBox box, ChartLineStyle fallback) =>
        Enum.TryParse(box.SelectedItem?.ToString(), true, out ChartLineStyle value) ? value : fallback;

    private static ComboBox WidthBox(double selected)
    {
        var box = new ComboBox
        {
            ItemsSource = new[] { 0.5, 1.0, 1.5, 1.6, 2.0, 3.0, 4.0, 5.0, 6.0 },
            SelectedItem = selected,
            Height = 30,
            MinWidth = 190,
            Background = Brush("#202020"),
            Foreground = Brushes.White
        };
        if (box.SelectedIndex < 0)
            box.SelectedItem = 1.0;
        return box;
    }

    private static ComboBox StyleBox(ChartLineStyle selected) => new()
    {
        ItemsSource = Enum.GetNames<ChartLineStyle>(),
        SelectedItem = selected.ToString(),
        Height = 30,
        MinWidth = 190,
        Background = Brush("#202020"),
        Foreground = Brushes.White
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 8)
    };

    private static Grid FormRow(string label, UIElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private static TextBox Text(string value) => new()
    {
        Text = value,
        Height = 30,
        MinWidth = 190,
        Padding = new Thickness(6, 3, 6, 3),
        Background = Brush("#202020"),
        Foreground = Brushes.White,
        BorderBrush = Brush("#555555")
    };

    private static CheckBox Check(string text, bool selected) => new()
    {
        Content = text,
        IsChecked = selected,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button MakeButton(string text, RoutedEventHandler handler, string background = "#292929")
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
            Height = 32,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#555555"),
            FocusVisualStyle = null
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string value)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch { return new SolidColorBrush(Colors.Black); }
    }
}
