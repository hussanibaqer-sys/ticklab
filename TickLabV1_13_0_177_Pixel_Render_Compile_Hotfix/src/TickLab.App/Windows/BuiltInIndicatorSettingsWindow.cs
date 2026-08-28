using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Indicators;
using TickLab.Core.Settings;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed class BuiltInIndicatorSettingsWindow : Window
{
    private readonly BuiltInIndicatorDefinition _definition;
    private readonly BuiltInIndicatorInstance _original;
    private readonly Dictionary<string, Control> _parameterControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StyleEditor> _styleEditors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<EditableLevel> _levels;
    private DataGrid _levelsGrid = null!;
    private readonly CheckBox _useMinimum;
    private readonly CheckBox _useMaximum;
    private readonly TextBox _minimumBox;
    private readonly TextBox _maximumBox;
    private CheckBox _allTimeframes = null!;
    private readonly Dictionary<string, CheckBox> _timeframeChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _status;
    private readonly IndicatorPlacementEditor? _placementEditor;
    private readonly IndicatorPlacementOptions? _placementOptions;

    private static readonly string[] Timeframes =
    {
        "1s", "15s", "30s", "45s", "1m", "2m", "3m", "4m", "5m", "10m", "15m", "30m", "45m",
        "1h", "2h", "3h", "4h", "6h", "8h", "12h", "Daily", "Weekly", "Monthly"
    };

    public BuiltInIndicatorSettingsWindow(
        BuiltInIndicatorInstance instance,
        IndicatorPlacementOptions? placementOptions = null)
    {
        _original = Clone(instance);
        _definition = BuiltInIndicatorCatalog.Find(instance.Kind);
        _levels = new ObservableCollection<EditableLevel>(instance.Levels.Select(EditableLevel.From));
        _placementOptions = placementOptions;
        _placementEditor = placementOptions is null ? null : new IndicatorPlacementEditor(placementOptions);

        Title = $"{_definition.Name} — Properties";
        Width = 1040;
        Height = 650;
        MinWidth = 820;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brush("#111111");
        Foreground = Brushes.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = _definition.Name, FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = _definition.Placement == BuiltInIndicatorPlacement.Overlay ? "Main chart window" : "Separate indicator window",
            Foreground = Brush("#B8B8B8"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        root.Children.Add(header);

        var tabs = new TabControl { Background = Brush("#151515"), BorderBrush = Brush("#444444") };
        tabs.Items.Add(Tab("Parameters", BuildParametersTab(instance)));
        tabs.Items.Add(Tab("Levels", BuildLevelsTab()));

        var scalePanel = new StackPanel { Margin = new Thickness(16) };
        _useMinimum = Check("Fixed minimum", instance.UseFixedMinimum);
        _minimumBox = Text(instance.FixedMinimum.ToString("0.########", CultureInfo.InvariantCulture));
        _useMaximum = Check("Fixed maximum", instance.UseFixedMaximum);
        _maximumBox = Text(instance.FixedMaximum.ToString("0.########", CultureInfo.InvariantCulture));
        scalePanel.Children.Add(FormRow(_useMinimum, _minimumBox));
        scalePanel.Children.Add(FormRow(_useMaximum, _maximumBox));
        scalePanel.Children.Add(new TextBlock
        {
            Text = "When disabled, TickLab automatically scales the visible indicator values like MT5.",
            Foreground = Brush("#A8A8A8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });
        tabs.Items.Add(Tab("Scale", scalePanel));

        tabs.Items.Add(Tab("Visualization", BuildVisualizationTab(instance)));
        tabs.Items.Add(Tab("Style", BuildStyleTab(instance)));
        if (_placementEditor is not null)
            tabs.Items.Add(Tab("Placement", _placementEditor.BuildView()));
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status = new TextBlock { Foreground = Brush("#F0A0A0"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_status);
        var reset = Button("Reset", Reset_Click);
        var ok = Button("OK", Ok_Click, "#2F6DB2");
        var cancel = Button("Cancel", (_, _) => { DialogResult = false; Close(); });
        Grid.SetColumn(reset, 1); Grid.SetColumn(ok, 2); Grid.SetColumn(cancel, 3);
        footer.Children.Add(reset); footer.Children.Add(ok); footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    public BuiltInIndicatorInstance Result { get; private set; } = null!;
    public IndicatorPlacementResult? PlacementResult { get; private set; }

    private FrameworkElement BuildParametersTab(BuiltInIndicatorInstance instance)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Parameters",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (_definition.Parameters.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "This MT5 indicator has no calculation parameters.", Foreground = Brush("#B8B8B8") });
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        foreach (IndicatorParameterDefinition parameter in _definition.Parameters)
        {
            Control editor;
            if (parameter.Type == IndicatorParameterType.Choice)
            {
                string value = instance.Text(parameter.Key, parameter.Choices?.FirstOrDefault() ?? string.Empty);
                var combo = new ComboBox
                {
                    ItemsSource = parameter.Choices ?? Array.Empty<string>(),
                    SelectedItem = value,
                    MinWidth = 190,
                    Height = 30,
                    Background = Brush("#202020"),
                    Foreground = Brushes.White
                };
                if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
                editor = combo;
            }
            else if (parameter.Type == IndicatorParameterType.Boolean)
            {
                editor = Check(string.Empty, instance.Flag(parameter.Key, false));
            }
            else
            {
                double value = instance.Number(parameter.Key, parameter.Minimum > double.MinValue ? parameter.Minimum : 0);
                editor = Text(value.ToString("0.########", CultureInfo.InvariantCulture));
            }
            _parameterControls[parameter.Key] = editor;
            panel.Children.Add(FormRow(new TextBlock { Text = parameter.Label, VerticalAlignment = VerticalAlignment.Center }, editor));
        }

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement BuildLevelsTab()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _levelsGrid = new DataGrid
        {
            ItemsSource = _levels,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Background = Brush("#161616"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#444444"),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        _levelsGrid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding(nameof(EditableLevel.Value)), Width = 110 });
        _levelsGrid.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding(nameof(EditableLevel.Label)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _levelsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Colour",
            CellTemplate = CreateColourSwatchTemplate(nameof(EditableLevel.Color)),
            Width = 72
        });
        _levelsGrid.Columns.Add(new DataGridTextColumn { Header = "Width", Binding = new System.Windows.Data.Binding(nameof(EditableLevel.Width)), Width = 70 });
        _levelsGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Style", ItemsSource = Enum.GetNames<ChartLineStyle>(), SelectedItemBinding = new System.Windows.Data.Binding(nameof(EditableLevel.LineStyle)), Width = 100 });
        grid.Children.Add(_levelsGrid);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Button("Edit colour…", (_, _) =>
        {
            if (_levelsGrid.SelectedItem is not EditableLevel level)
            {
                _status.Text = "Select a level first.";
                return;
            }
            var picker = new DrawingColorPickerWindow(level.Color) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                level.Color = picker.SelectedColor;
                _levelsGrid.Items.Refresh();
            }
        }));
        buttons.Children.Add(Button("Add", (_, _) =>
        {
            var level = new EditableLevel { Value = 0, Color = "#808080", Width = 1, LineStyle = ChartLineStyle.Dashed.ToString() };
            _levels.Add(level);
            _levelsGrid.SelectedItem = level;
        }));
        buttons.Children.Add(Button("Delete", (_, _) => { if (_levelsGrid.SelectedItem is EditableLevel level) _levels.Remove(level); }));
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private FrameworkElement BuildVisualizationTab(BuiltInIndicatorInstance instance)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        _allTimeframes = Check("Show on all timeframes", instance.VisibleOnAllTimeframes);
        panel.Children.Add(_allTimeframes);
        panel.Children.Add(new TextBlock { Text = "Show only on selected timeframes when the option above is disabled:", Foreground = Brush("#B8B8B8"), Margin = new Thickness(0, 12, 0, 8) });
        var wrap = new WrapPanel();
        foreach (string timeframe in Timeframes)
        {
            var box = Check(timeframe, instance.VisibleOnAllTimeframes || instance.VisibleTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase));
            box.Width = 90;
            box.Margin = new Thickness(0, 3, 8, 3);
            _timeframeChecks[timeframe] = box;
            wrap.Children.Add(box);
        }
        panel.Children.Add(wrap);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement BuildStyleTab(BuiltInIndicatorInstance instance)
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        var headings = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        foreach (double width in new[] { 125d, 108d, 108d, 108d, 72d, 108d, 68d, 104d })
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        headings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headings.Children.Add(new TextBlock { Text = "Series", Foreground = Brush("#A8A8A8") });
        headings.Children.Add(HeaderText("Colour", 1));
        headings.Children.Add(HeaderText("Down / negative", 2));
        headings.Children.Add(HeaderText("Fill", 3));
        headings.Children.Add(HeaderText("Opacity", 4));
        headings.Children.Add(HeaderText("Label", 5));
        headings.Children.Add(HeaderText("Width", 6));
        headings.Children.Add(HeaderText("Style", 7));
        panel.Children.Add(headings);

        foreach (IndicatorStyleSetting style in instance.Styles)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            foreach (double width in new[] { 125d, 108d, 108d, 108d, 72d, 108d, 68d, 104d })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = style.Label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Button color = CreateColorButton(style.Color);
            Grid.SetColumn(color, 1); row.Children.Add(color);

            Button? negativeColor = null;
            if (style.ColorBySign || style.ColorBySlope)
            {
                negativeColor = CreateColorButton(style.NegativeColor);
                Grid.SetColumn(negativeColor, 2); row.Children.Add(negativeColor);
            }
            else
            {
                var notUsed = new TextBlock
                {
                    Text = "—",
                    Foreground = Brush("#707070"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(notUsed, 2); row.Children.Add(notUsed);
            }

            Button fillColor = CreateColorButton(style.FillColor);
            Grid.SetColumn(fillColor, 3); row.Children.Add(fillColor);

            TextBox fillOpacity = Text(style.FillOpacity.ToString("0.##", CultureInfo.InvariantCulture));
            fillOpacity.MinWidth = 58;
            Grid.SetColumn(fillOpacity, 4); row.Children.Add(fillOpacity);

            Button labelColor = CreateColorButton(style.LabelColor);
            Grid.SetColumn(labelColor, 5); row.Children.Add(labelColor);

            var widthBox = new ComboBox
            {
                ItemsSource = new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0 },
                SelectedItem = style.Width,
                Height = 30,
                Background = Brush("#202020"),
                Foreground = Brushes.White
            };
            if (widthBox.SelectedIndex < 0) widthBox.SelectedItem = 1.0;
            Grid.SetColumn(widthBox, 6); row.Children.Add(widthBox);

            var lineStyle = new ComboBox
            {
                ItemsSource = Enum.GetNames<ChartLineStyle>(),
                SelectedItem = style.LineStyle.ToString(),
                Height = 30,
                Background = Brush("#202020"),
                Foreground = Brushes.White
            };
            Grid.SetColumn(lineStyle, 7); row.Children.Add(lineStyle);

            var visible = Check("Visible", style.Visible);
            Grid.SetColumn(visible, 8); row.Children.Add(visible);
            panel.Children.Add(row);
            _styleEditors[style.SeriesKey] = new StyleEditor(
                color,
                negativeColor,
                fillColor,
                fillOpacity,
                labelColor,
                widthBox,
                lineStyle,
                visible);
        }
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static TextBlock HeaderText(string text, int column)
    {
        var block = new TextBlock { Text = text, Foreground = Brush("#A8A8A8") };
        Grid.SetColumn(block, column);
        return block;
    }

    private Button CreateColorButton(string initialColor)
    {
        var button = Button(initialColor, EmptyHandler);
        ThemeColorScope.SetPreserveExactColors(button, true);
        SetColorButton(button, initialColor);
        button.Click -= EmptyHandler;
        button.Click += (_, _) =>
        {
            string current = button.Tag?.ToString() ?? initialColor;
            var picker = new DrawingColorPickerWindow(current) { Owner = this };
            if (picker.ShowDialog() == true)
                SetColorButton(button, picker.SelectedColor);
        };
        return button;
    }

    private static void SetColorButton(Button button, string color) =>
        ColorDisplayHelper.ApplyToButton(button, color);

    private static DataTemplate CreateColourSwatchTemplate(string propertyName)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(FrameworkElement.WidthProperty, 42.0);
        border.SetValue(FrameworkElement.HeightProperty, 20.0);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Gray);
        border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(propertyName)
        {
            Converter = new ColorValueToBrushConverter()
        });
        border.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(propertyName)
        {
            Converter = new ColorValueToNameConverter()
        });
        border.SetValue(ToolTipService.InitialShowDelayProperty, 1000);
        border.SetValue(ToolTipService.ShowDurationProperty, 5000);
        return new DataTemplate { VisualTree = border };
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        BuiltInIndicatorInstance defaults = _definition.CreateDefault();
        var replacement = new BuiltInIndicatorSettingsWindow(defaults, _placementOptions) { Owner = Owner };
        if (replacement.ShowDialog() == true)
        {
            Result = replacement.Result with { InstanceId = _original.InstanceId };
            PlacementResult = replacement.PlacementResult;
            DialogResult = true;
            Close();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var numbers = new Dictionary<string, double>(_original.NumericParameters, StringComparer.OrdinalIgnoreCase);
            var texts = new Dictionary<string, string>(_original.TextParameters, StringComparer.OrdinalIgnoreCase);
            var flags = new Dictionary<string, bool>(_original.BooleanParameters, StringComparer.OrdinalIgnoreCase);

            foreach (IndicatorParameterDefinition parameter in _definition.Parameters)
            {
                Control control = _parameterControls[parameter.Key];
                if (parameter.Type == IndicatorParameterType.Choice)
                {
                    texts[parameter.Key] = (control as ComboBox)?.SelectedItem?.ToString() ?? parameter.Choices?.FirstOrDefault() ?? string.Empty;
                }
                else if (parameter.Type == IndicatorParameterType.Boolean)
                {
                    flags[parameter.Key] = (control as CheckBox)?.IsChecked == true;
                }
                else
                {
                    if (!double.TryParse((control as TextBox)?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        throw new InvalidOperationException($"{parameter.Label} must be a number.");
                    if (value < parameter.Minimum || value > parameter.Maximum)
                        throw new InvalidOperationException($"{parameter.Label} must be between {parameter.Minimum:0.####} and {parameter.Maximum:0.####}.");
                    numbers[parameter.Key] = parameter.Type == IndicatorParameterType.Integer ? Math.Round(value) : value;
                }
            }

            bool useMin = _useMinimum.IsChecked == true;
            bool useMax = _useMaximum.IsChecked == true;
            double min = 0, max = 0;
            if (useMin && !double.TryParse(_minimumBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out min))
                throw new InvalidOperationException("Fixed minimum must be a number.");
            if (useMax && !double.TryParse(_maximumBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out max))
                throw new InvalidOperationException("Fixed maximum must be a number.");
            if (useMin && useMax && max <= min)
                throw new InvalidOperationException("Fixed maximum must be greater than fixed minimum.");

            var styles = _original.Styles.Select(style =>
            {
                if (!_styleEditors.TryGetValue(style.SeriesKey, out StyleEditor? editor)) return style;
                if (!double.TryParse(editor.FillOpacity.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fillOpacity) ||
                    fillOpacity < 0 || fillOpacity > 1)
                {
                    throw new InvalidOperationException($"{style.Label} fill opacity must be between 0 and 1.");
                }
                return style with
                {
                    Color = editor.Color.Tag?.ToString() ?? style.Color,
                    NegativeColor = editor.NegativeColor?.Tag?.ToString() ?? style.NegativeColor,
                    FillColor = editor.FillColor.Tag?.ToString() ?? style.FillColor,
                    FillOpacity = fillOpacity,
                    LabelColor = editor.LabelColor.Tag?.ToString() ?? style.LabelColor,
                    Width = editor.Width.SelectedItem is double width ? width : style.Width,
                    LineStyle = Enum.TryParse(editor.LineStyle.SelectedItem?.ToString(), true, out ChartLineStyle parsed) ? parsed : style.LineStyle,
                    Visible = editor.Visible.IsChecked == true
                };
            }).ToArray();

            var levels = _levels.Select(level => level.ToModel()).ToArray();
            bool all = _allTimeframes.IsChecked == true;
            string[] visibleTimeframes = all ? Array.Empty<string>() : _timeframeChecks.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray();

            Result = _original with
            {
                NumericParameters = numbers,
                TextParameters = texts,
                BooleanParameters = flags,
                Styles = styles,
                Levels = levels,
                UseFixedMinimum = useMin,
                FixedMinimum = min,
                UseFixedMaximum = useMax,
                FixedMaximum = max,
                VisibleOnAllTimeframes = all,
                VisibleTimeframes = visibleTimeframes
            };
            PlacementResult = _placementEditor?.Capture();
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private static BuiltInIndicatorInstance Clone(BuiltInIndicatorInstance instance) => instance with
    {
        NumericParameters = new Dictionary<string, double>(instance.NumericParameters, StringComparer.OrdinalIgnoreCase),
        TextParameters = new Dictionary<string, string>(instance.TextParameters, StringComparer.OrdinalIgnoreCase),
        BooleanParameters = new Dictionary<string, bool>(instance.BooleanParameters, StringComparer.OrdinalIgnoreCase),
        Styles = instance.Styles.Select(item => item with { }).ToArray(),
        Levels = instance.Levels.Select(item => item with { }).ToArray(),
        VisibleTimeframes = instance.VisibleTimeframes.ToArray()
    };

    private static TabItem Tab(string header, FrameworkElement content) => new() { Header = header, Content = content };
    private static TextBox Text(string value) => new() { Text = value, Height = 30, MinWidth = 190, Padding = new Thickness(6, 3, 6, 3), Background = Brush("#202020"), Foreground = Brushes.White, BorderBrush = Brush("#555555") };
    private static CheckBox Check(string label, bool selected) => new() { Content = label, IsChecked = selected, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private static Grid FormRow(UIElement label, UIElement editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(label); Grid.SetColumn(editor, 1); grid.Children.Add(editor); return grid;
    }
    private static Button Button(string label, RoutedEventHandler click, string background = "#292929")
    {
        var button = new Button { Content = label, MinWidth = 82, Height = 32, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), Background = Brush(background), Foreground = Brushes.White, BorderBrush = Brush("#555555"), FocusVisualStyle = null };
        button.Click += click; return button;
    }
    private static void EmptyHandler(object sender, RoutedEventArgs e) { }
    private static SolidColorBrush Brush(string value)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch { return new SolidColorBrush(Colors.Black); }
    }

    private sealed record StyleEditor(
        Button Color,
        Button? NegativeColor,
        Button FillColor,
        TextBox FillOpacity,
        Button LabelColor,
        ComboBox Width,
        ComboBox LineStyle,
        CheckBox Visible);

    private sealed class EditableLevel
    {
        public double Value { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = "#808080";
        public double Width { get; set; } = 1.0;
        public string LineStyle { get; set; } = ChartLineStyle.Dashed.ToString();
        public static EditableLevel From(IndicatorLevelSetting model) => new() { Value = model.Value, Label = model.Label, Color = model.Color, Width = model.Width, LineStyle = model.LineStyle.ToString() };
        public IndicatorLevelSetting ToModel() => new() { Value = Value, Label = Label ?? string.Empty, Color = string.IsNullOrWhiteSpace(Color) ? "#808080" : Color, Width = Math.Clamp(Width, 0.5, 8), LineStyle = Enum.TryParse(LineStyle, true, out ChartLineStyle style) ? style : ChartLineStyle.Dashed };
    }
}
