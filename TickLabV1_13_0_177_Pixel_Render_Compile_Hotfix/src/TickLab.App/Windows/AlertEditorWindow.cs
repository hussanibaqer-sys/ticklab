using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TickLab.Core.Alerts;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed record AlertEditorChartOption(
    int ChartId,
    string Symbol,
    string Timeframe,
    IReadOnlyDictionary<string, string> Drawings,
    IReadOnlyDictionary<string, string> Indicators)
{
    public override string ToString() => $"Chart {ChartId} · {Symbol} · {Timeframe}";
}

public sealed class AlertEditorWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly ComboBox _chartBox;
    private readonly ComboBox _conditionBox;
    private readonly ComboBox _sourceBox;
    private readonly TextBox _thresholdBox;
    private readonly ComboBox _targetBox;
    private readonly ComboBox _frequencyBox;
    private readonly CheckBox _soundCheck;
    private readonly CheckBox _popupCheck;
    private readonly Button _lineColorButton;
    private readonly ComboBox _lineThicknessBox;
    private string _selectedLineColor;
    private readonly AlertRule _original;

    public AlertEditorWindow(
        IReadOnlyList<AlertEditorChartOption> charts,
        AlertRule? existing = null)
    {
        _original = existing ?? new AlertRule();
        _selectedLineColor = _original.LineColor;
        Title = existing is null ? "Create Alert" : "Edit Alert";
        Width = 520;
        Height = 575;
        MinWidth = 480;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Loaded += (_, _) => ApplicationThemeManager.ApplyToWindow(this);

        var root = new Grid { Margin = new Thickness(18) };
        for (int i = 0; i < 9; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _nameBox = AddTextRow(root, 0, "Alert name", _original.Name);
        _chartBox = AddComboRow(root, 1, "Chart", charts.Cast<object>().ToArray());
        _conditionBox = AddComboRow(root, 2, "Condition", Enum.GetValues<AlertConditionType>().Cast<object>().ToArray());
        _sourceBox = AddComboRow(root, 3, "Price source", Enum.GetValues<AlertPriceSource>().Cast<object>().ToArray());
        _thresholdBox = AddTextRow(root, 4, "Threshold / value", _original.Threshold.ToString("G17", CultureInfo.InvariantCulture));
        _targetBox = AddComboRow(root, 5, "Drawing / indicator", Array.Empty<object>());
        _frequencyBox = AddComboRow(root, 6, "Frequency", Enum.GetValues<AlertFrequency>().Cast<object>().ToArray());

        AddLabel(root, 7, "Line appearance");
        var lineStyle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _lineColorButton = new Button { Content = "Colour", Width = 92, Height = 29, Margin = new Thickness(0, 0, 8, 0) };
        ThemeColorScope.SetPreserveExactColors(_lineColorButton, true);
        _lineColorButton.Click += (_, _) => ChooseLineColor();
        ApplyLineColorButton();
        _lineThicknessBox = new ComboBox { Width = 100, Height = 29, ItemsSource = new object[] { 1.0, 2.0, 3.0, 4.0, 5.0 } };
        _lineThicknessBox.ItemTemplate = CreatePixelTemplate();
        _lineThicknessBox.SelectedItem = NearestThickness(_original.LineThickness);
        lineStyle.Children.Add(_lineColorButton);
        lineStyle.Children.Add(_lineThicknessBox);
        Grid.SetRow(lineStyle, 7);
        Grid.SetColumn(lineStyle, 1);
        root.Children.Add(lineStyle);

        var checks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 12) };
        _soundCheck = new CheckBox { Content = "Sound", IsChecked = _original.PlaySound, Margin = new Thickness(0, 0, 18, 0) };
        _popupCheck = new CheckBox { Content = "Desktop popup", IsChecked = _original.ShowDesktopPopup, Margin = new Thickness(0, 0, 18, 0) };
        var testBell = new Button { Content = "Test bell", Width = 82, Height = 27 };
        testBell.Click += (_, _) => AlertBellPlayer.PlayOnce();
        checks.Children.Add(_soundCheck);
        checks.Children.Add(_popupCheck);
        checks.Children.Add(testBell);
        Grid.SetRow(checks, 8);
        Grid.SetColumn(checks, 1);
        root.Children.Add(checks);

        var note = new TextBlock
        {
            Text = "Alerts are evaluated from live data. Replay never triggers live alerts unless explicitly enabled in a future replay setting.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(note, 9);
        Grid.SetColumnSpan(note, 2);
        root.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 82, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Save alert", Width = 96, Height = 30 };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 10);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        ApplicationThemeManager.ApplyToWindow(this);

        _chartBox.SelectedItem = charts.FirstOrDefault(item => item.ChartId == _original.ChartId) ?? charts.FirstOrDefault();
        _conditionBox.SelectedItem = _original.Condition;
        _sourceBox.SelectedItem = _original.PriceSource;
        _frequencyBox.SelectedItem = _original.Frequency;
        _chartBox.SelectionChanged += (_, _) => RefreshTargets();
        _conditionBox.SelectionChanged += (_, _) => RefreshTargets();
        RefreshTargets();
    }

    public AlertRule? Result { get; private set; }

    private void RefreshTargets()
    {
        _targetBox.ItemsSource = null;
        _targetBox.Items.Clear();
        AlertEditorChartOption? chart = _chartBox.SelectedItem as AlertEditorChartOption;
        AlertConditionType condition = _conditionBox.SelectedItem is AlertConditionType selected
            ? selected
            : AlertConditionType.PriceCrossesUp;

        if (chart is not null && condition == AlertConditionType.DrawingCross)
        {
            foreach ((string id, string name) in chart.Drawings)
                _targetBox.Items.Add(new TargetOption(id, name));
            _targetBox.IsEnabled = true;
        }
        else if (chart is not null && condition is AlertConditionType.IndicatorAbove or AlertConditionType.IndicatorBelow or AlertConditionType.IndicatorCrossesUp or AlertConditionType.IndicatorCrossesDown)
        {
            foreach ((string id, string name) in chart.Indicators)
                _targetBox.Items.Add(new TargetOption(id, name));
            _targetBox.IsEnabled = true;
        }
        else
        {
            _targetBox.IsEnabled = false;
        }

        string wanted = condition == AlertConditionType.DrawingCross
            ? _original.DrawingId
            : _original.IndicatorKey;
        _targetBox.SelectedItem = _targetBox.Items.Cast<TargetOption>().FirstOrDefault(item => item.Id == wanted)
                                  ?? _targetBox.Items.Cast<object>().FirstOrDefault();

        bool needsThreshold = condition is not AlertConditionType.CandleOpened and not AlertConditionType.CandleClosed and not AlertConditionType.DrawingCross;
        _thresholdBox.IsEnabled = needsThreshold;
        _sourceBox.IsEnabled = condition is AlertConditionType.PriceAbove or AlertConditionType.PriceBelow or AlertConditionType.PriceCrossesUp or AlertConditionType.PriceCrossesDown or AlertConditionType.PriceTouches;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_chartBox.SelectedItem is not AlertEditorChartOption chart ||
            _conditionBox.SelectedItem is not AlertConditionType condition ||
            _frequencyBox.SelectedItem is not AlertFrequency frequency ||
            _sourceBox.SelectedItem is not AlertPriceSource source)
        {
            MessageBox.Show(this, "Choose a chart, condition, source and frequency.", "Alert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double threshold = 0;
        if (condition is not AlertConditionType.CandleOpened and not AlertConditionType.CandleClosed and not AlertConditionType.DrawingCross &&
            !double.TryParse(_thresholdBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out threshold))
        {
            MessageBox.Show(this, "Enter a valid numeric threshold using a dot as decimal separator.", "Alert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TargetOption? target = _targetBox.SelectedItem as TargetOption;
        if ((condition == AlertConditionType.DrawingCross || condition is AlertConditionType.IndicatorAbove or AlertConditionType.IndicatorBelow or AlertConditionType.IndicatorCrossesUp or AlertConditionType.IndicatorCrossesDown) && target is null)
        {
            MessageBox.Show(this, "Choose the drawing or indicator used by this alert.", "Alert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = _original with
        {
            Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Alert" : _nameBox.Text.Trim(),
            ChartId = chart.ChartId,
            Symbol = chart.Symbol,
            Timeframe = chart.Timeframe,
            Condition = condition,
            PriceSource = source,
            Threshold = threshold,
            LineColor = _selectedLineColor,
            LineThickness = _lineThicknessBox.SelectedItem is double pixels ? pixels : 1.25,
            DrawingId = condition == AlertConditionType.DrawingCross ? target?.Id ?? string.Empty : string.Empty,
            IndicatorKey = condition is AlertConditionType.IndicatorAbove or AlertConditionType.IndicatorBelow or AlertConditionType.IndicatorCrossesUp or AlertConditionType.IndicatorCrossesDown ? target?.Id ?? string.Empty : string.Empty,
            Frequency = frequency,
            PlaySound = _soundCheck.IsChecked == true,
            ShowDesktopPopup = _popupCheck.IsChecked == true
        };
        DialogResult = true;
    }

    private void ChooseLineColor()
    {
        var picker = new DrawingColorPickerWindow(_selectedLineColor) { Owner = this };
        if (picker.ShowDialog() != true)
            return;
        _selectedLineColor = picker.SelectedColor;
        ApplyLineColorButton();
    }

    private void ApplyLineColorButton()
    {
        try
        {
            object? parsed = System.Windows.Media.ColorConverter.ConvertFromString(_selectedLineColor);
            if (parsed is System.Windows.Media.Color color)
            {
                _lineColorButton.Background = new System.Windows.Media.SolidColorBrush(color);
                _lineColorButton.Foreground = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 >= 145
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.White;
                return;
            }
        }
        catch
        {
        }
        _lineColorButton.Background = System.Windows.Media.Brushes.Gray;
        _lineColorButton.Foreground = System.Windows.Media.Brushes.White;
    }

    private static double NearestThickness(double value)
    {
        double[] values = { 1.0, 2.0, 3.0, 4.0, 5.0 };
        return values.OrderBy(candidate => Math.Abs(candidate - value)).First();
    }

    private static DataTemplate CreatePixelTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(".") { StringFormat = "{0:0.#} px" });
        template.VisualTree = factory;
        return template;
    }

    private static TextBox AddTextRow(Grid root, int row, string label, string value)
    {
        AddLabel(root, row, label);
        var box = new TextBox { Text = value, Height = 29, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        root.Children.Add(box);
        return box;
    }

    private static ComboBox AddComboRow(Grid root, int row, string label, object[] items)
    {
        AddLabel(root, row, label);
        var box = new ComboBox { Height = 29, Margin = new Thickness(0, 0, 0, 8), ItemsSource = items };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        root.Children.Add(box);
        return box;
    }

    private static void AddLabel(Grid root, int row, string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
        Grid.SetRow(label, row);
        root.Children.Add(label);
    }

    private sealed record TargetOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
