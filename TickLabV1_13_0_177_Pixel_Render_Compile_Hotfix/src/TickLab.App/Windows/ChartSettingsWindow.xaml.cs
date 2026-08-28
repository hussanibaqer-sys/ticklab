using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Settings;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class ChartSettingsWindow : Window
{
    private readonly Dictionary<string, TextBox> _colorBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Slider> _numberSliders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _styleBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _dynamicChecks = new(StringComparer.Ordinal);
    private readonly ChartTemplateStore _templateStore = new();
    private bool _loading;

    public ChartSettingsWindow(
        int chartId,
        string symbol,
        string timeframe,
        ChartSettings settings,
        string applicationTheme,
        IReadOnlyList<int> availableChartIds)
    {
        InitializeComponent();
        ChartId = chartId;
        ChartIdentityText.Text = $"Chart {chartId} · {(string.IsNullOrWhiteSpace(symbol) ? "Price Chart" : symbol)} · {timeframe}";
        BuildAppearanceControls();
        BuildCopyTargets(availableChartIds);
        Settings = settings;
        ApplicationTheme = string.Equals(applicationTheme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        LoadControls(settings);
        ApplicationThemeCombo.SelectedIndex = ApplicationTheme == "Light" ? 1 : 0;
        RegisterPreviewEvents();
    }

    public int ChartId { get; }
    public ChartSettings Settings { get; private set; }
    public string ApplicationTheme { get; private set; } = "Dark";
    public bool UseAsDefaultForFutureCharts => UseAsDefaultCheckBox.IsChecked == true;
    public IReadOnlyList<int> CopyTargetChartIds => CopyTargetsPanel.Children.OfType<CheckBox>()
        .Where(item => item.IsChecked == true && item.Tag is int)
        .Select(item => (int)item.Tag)
        .ToArray();

    public event Action<ChartSettings>? PreviewChanged;
    public event Action<string>? ThemePreviewChanged;

    private void BuildAppearanceControls()
    {
        AddSection("Chart and scales");
        AddColor("Chart background", nameof(ChartSettings.ChartBackgroundColor));
        AddColor("General chart text", nameof(ChartSettings.ChartTextColor));
        AddColor("Selected-candle marker", nameof(ChartSettings.SelectedCandleColor));
        AddColor("History-boundary line", nameof(ChartSettings.HistoryBoundaryColor));
        AddColor("Go-live button", nameof(ChartSettings.LatestButtonColor));
        AddColor("Go-live button text", nameof(ChartSettings.LatestButtonTextColor));
        AddColor("Price-scale background", nameof(ChartSettings.PriceScaleBackgroundColor));
        AddColor("Price-scale text", nameof(ChartSettings.PriceScaleTextColor));
        AddColor("Time-scale background", nameof(ChartSettings.TimeScaleBackgroundColor));
        AddColor("Time-scale text", nameof(ChartSettings.TimeScaleTextColor));

        AddSection("Grid");
        AddColor("Grid colour", nameof(ChartSettings.GridColor));
        AddSlider("Grid transparency", nameof(ChartSettings.GridOpacity), 0, 1, 0.05);
        AddSlider("Grid pixels", nameof(ChartSettings.GridThickness), 0.25, 5, 0.25);

        AddSection("Up candle");
        AddColor("Body", nameof(ChartSettings.UpBodyColor));
        AddColor("Border", nameof(ChartSettings.UpBorderColor));
        AddColor("Wick", nameof(ChartSettings.UpWickColor));
        AddSection("Down candle");
        AddColor("Body", nameof(ChartSettings.DownBodyColor));
        AddColor("Border", nameof(ChartSettings.DownBorderColor));
        AddColor("Wick", nameof(ChartSettings.DownWickColor));
        AddSlider("Candle border pixels", nameof(ChartSettings.CandleBorderThickness), 0.25, 5, 0.25);
        AddSlider("Wick pixels", nameof(ChartSettings.CandleWickThickness), 0.25, 5, 0.25);

        AddSection("Tick chart");
        AddColor("Bid tick colour", nameof(ChartSettings.TickBidColor));
        AddSlider("Bid tick pixels", nameof(ChartSettings.TickBidThickness), 0.25, 8, 0.25);
        AddColor("Ask tick colour", nameof(ChartSettings.TickAskColor));
        AddSlider("Ask tick pixels", nameof(ChartSettings.TickAskThickness), 0.25, 8, 0.25);

        AddSection("Price line");
        AddCheck("Show price line", nameof(ChartSettings.ShowPriceLine));
        AddColor("Price-line colour", nameof(ChartSettings.PriceLineColor));
        AddColor("Price ticket text", nameof(ChartSettings.PriceLineTextColor));
        AddLineStyle("Price-line style", nameof(ChartSettings.PriceLineStyle));
        AddSlider("Price-line pixels", nameof(ChartSettings.PriceLineThickness), 0.25, 8, 0.25);
        AddCheck("Show candle countdown", nameof(ChartSettings.ShowCandleCountdown));

        AddSection("Ask price line");
        AddCheck("Show Ask price line", nameof(ChartSettings.ShowAskPriceLine));
        AddColor("Ask-line colour", nameof(ChartSettings.AskPriceLineColor));
        AddColor("Ask ticket text", nameof(ChartSettings.AskPriceLineTextColor));
        AddLineStyle("Ask-line style", nameof(ChartSettings.AskPriceLineStyle));
        AddSlider("Ask-line pixels", nameof(ChartSettings.AskPriceLineThickness), 0.25, 8, 0.25);

        AddSection("Spread line");
        AddCheck("Show spread line", nameof(ChartSettings.ShowSpreadLine));
        AddColor("Spread-line colour", nameof(ChartSettings.SpreadLineColor));
        AddLineStyle("Spread-line style", nameof(ChartSettings.SpreadLineStyle));
        AddSlider("Spread-line pixels", nameof(ChartSettings.SpreadLineThickness), 0.25, 8, 0.25);
        AddCheck("Show spread value", nameof(ChartSettings.ShowSpreadLabel));
        AddCheck("Fill between price and spread", nameof(ChartSettings.ShowSpreadFill));
        AddSlider("Spread-fill transparency", nameof(ChartSettings.SpreadFillOpacity), 0, 1, 0.05);

        AddSection("Crosshair");
        AddColor("Crosshair colour", nameof(ChartSettings.CrosshairColor));
        AddColor("Crosshair-label background", nameof(ChartSettings.CrosshairLabelBackgroundColor));
        AddColor("Crosshair-label text", nameof(ChartSettings.CrosshairLabelTextColor));
        AddLineStyle("Crosshair style", nameof(ChartSettings.CrosshairLineStyle));
        AddSlider("Crosshair pixels", nameof(ChartSettings.CrosshairThickness), 0.25, 8, 0.25);
    }

    private void AddSection(string title) => AppearancePanel.Children.Add(new TextBlock
    {
        Text = title,
        FontWeight = FontWeights.SemiBold,
        FontSize = 12.5,
        Margin = new Thickness(0, AppearancePanel.Children.Count == 0 ? 0 : 14, 0, 6)
    });

    private Grid CreateRow(string label)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("MutedTextBrush") });
        AppearancePanel.Children.Add(row);
        return row;
    }

    private void AddColor(string label, string key)
    {
        Grid row = CreateRow(label);
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        var swatch = new Button
        {
            Width = 44,
            Height = 27,
            MinHeight = 27,
            Padding = new Thickness(0),
            Uid = key,
            Tag = string.Empty,
            ToolTip = "Choose colour",
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 96, 96)),
            BorderThickness = new Thickness(1),
            Style = (Style)FindResource("ChartColourSwatchButton")
        };
        ThemeColorScope.SetPreserveExactColors(swatch, true);
        var box = new TextBox { Width = 0, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(box, 1);
        box.MinHeight = 29; box.Padding = new Thickness(8, 4, 8, 4); box.Tag = key;
        swatch.Click += ColorButton_Click;
        box.TextChanged += SettingControl_Changed;
        panel.Children.Add(swatch); panel.Children.Add(box);
        Grid.SetColumn(panel, 1); row.Children.Add(panel);
        _colorBoxes[key] = box;
    }

    private void AddSlider(string label, string key, double minimum, double maximum, double tick)
    {
        Grid row = CreateRow(label);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = tick, IsSnapToTickEnabled = true, Width = 250, HorizontalAlignment = HorizontalAlignment.Left, Tag = key };
        slider.ValueChanged += SettingControl_Changed;
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        _numberSliders[key] = slider;
    }

    private void AddLineStyle(string label, string key)
    {
        Grid row = CreateRow(label);
        var combo = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = Enum.GetValues<ChartLineStyle>(), Tag = key };
        combo.SelectionChanged += SettingControl_Changed;
        Grid.SetColumn(combo, 1); row.Children.Add(combo);
        _styleBoxes[key] = combo;
    }

    private void AddCheck(string label, string key)
    {
        var check = new CheckBox { Content = label, Tag = key, Margin = new Thickness(0, 2, 0, 2) };
        check.Checked += SettingControl_Changed;
        check.Unchecked += SettingControl_Changed;
        AppearancePanel.Children.Add(check);
        _dynamicChecks[key] = check;
    }

    private void BuildCopyTargets(IReadOnlyList<int> chartIds)
    {
        foreach (int id in chartIds.Where(id => id != ChartId).Distinct().OrderBy(id => id))
            CopyTargetsPanel.Children.Add(new CheckBox { Content = $"Chart {id}", Tag = id, Margin = new Thickness(0, 0, 12, 4) });
        if (CopyTargetsPanel.Children.Count == 0)
            CopyTargetsPanel.Children.Add(new TextBlock { Text = "No other chart is open.", Foreground = (Brush)FindResource("MutedTextBrush") });
    }

    private void RegisterPreviewEvents()
    {
        CheckBox[] fixedChecks =
        {
            ShowCandleGridCheckBox, ShowCandleCrosshairCheckBox, SnapCandleCrosshairCheckBox,
            ShowCrosshairLabelsCheckBox, ShowTickGridCheckBox, ShowBidLineCheckBox,
            ShowAskLineCheckBox, ShowMidLineCheckBox, ShowTickPointsCheckBox, ShowTickCrosshairCheckBox
        };
        foreach (CheckBox checkBox in fixedChecks)
        {
            checkBox.Checked += SettingControl_Changed;
            checkBox.Unchecked += SettingControl_Changed;
        }
        ZoomModeRadioButton.Checked += SettingControl_Changed;
        ScrollModeRadioButton.Checked += SettingControl_Changed;
    }

    private void SettingControl_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        string requestedTheme = ApplicationThemeCombo.SelectedIndex == 1 ? "Light" : "Dark";
        bool themeChanged = !string.Equals(ApplicationTheme, requestedTheme, StringComparison.Ordinal);
        ApplicationTheme = requestedTheme;
        Settings = ReadControls();
        PreviewChanged?.Invoke(Settings);
        if (themeChanged)
            ThemePreviewChanged?.Invoke(ApplicationTheme);
        RefreshSwatches();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button swatch || string.IsNullOrWhiteSpace(swatch.Uid) ||
            !_colorBoxes.TryGetValue(swatch.Uid, out TextBox? box)) return;
        var picker = new DrawingColorPickerWindow(box.Text) { Owner = this };
        picker.ColorPreviewChanged += value => box.Text = value;
        string original = box.Text;
        if (picker.ShowDialog() == true) box.Text = picker.SelectedColor;
        else box.Text = original;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadControls(ChartSettings.Default);
        Settings = ChartSettings.Default;
        PreviewChanged?.Invoke(Settings);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Settings = ReadControls();
        ApplicationTheme = ApplicationThemeCombo.SelectedIndex == 1 ? "Light" : "Dark";
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChartTemplateNameDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (_templateStore.Contains(dialog.TemplateName) && MessageBox.Show(this, "A template with this name exists. Replace it?", "Replace template", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _templateStore.Save(dialog.TemplateName, ReadControls());
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ChartTemplateEntry> entries = _templateStore.LoadAll();
        var dialog = new ChartTemplatePickerDialog("Load chart preset", entries, "Load") { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedTemplate is not null)
        {
            LoadControls(dialog.SelectedTemplate.Settings);
            Settings = dialog.SelectedTemplate.Settings;
            PreviewChanged?.Invoke(Settings);
        }
    }

    private void ImportPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var open = new OpenFileDialog { Filter = "TickLab chart template (*.tltchart)|*.tltchart|JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (open.ShowDialog(this) != true) return;
        try
        {
            ChartTemplateEntry entry = _templateStore.Import(open.FileName);
            LoadControls(entry.Settings); Settings = entry.Settings; PreviewChanged?.Invoke(Settings);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportPresetButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ChartTemplateEntry> entries = _templateStore.LoadAll();
        var picker = new ChartTemplatePickerDialog("Export chart preset", entries, "Export") { Owner = this };
        if (picker.ShowDialog() != true) return;
        ChartTemplateEntry? selectedTemplate = picker.SelectedTemplate;
        if (selectedTemplate is null) return;
        var save = new SaveFileDialog { Filter = "TickLab chart template (*.tltchart)|*.tltchart", FileName = selectedTemplate.Name + ".tltchart" };
        if (save.ShowDialog(this) == true) _templateStore.Export(save.FileName, selectedTemplate.Name);
    }

    private void LoadControls(ChartSettings settings)
    {
        _loading = true;
        ZoomModeRadioButton.IsChecked = settings.ScrollWheelMode == ChartScrollWheelMode.Zoom;
        ScrollModeRadioButton.IsChecked = settings.ScrollWheelMode == ChartScrollWheelMode.Scroll;
        ShowCandleGridCheckBox.IsChecked = settings.ShowCandleGrid;
        ShowCandleCrosshairCheckBox.IsChecked = settings.ShowCandleCrosshair;
        SnapCandleCrosshairCheckBox.IsChecked = settings.SnapCandleCrosshair;
        ShowCrosshairLabelsCheckBox.IsChecked = settings.ShowCrosshairLabels;
        ShowTickGridCheckBox.IsChecked = settings.ShowTickGrid;
        ShowBidLineCheckBox.IsChecked = settings.ShowBidLine;
        ShowAskLineCheckBox.IsChecked = settings.ShowAskLine;
        ShowMidLineCheckBox.IsChecked = settings.ShowMidLine;
        ShowTickPointsCheckBox.IsChecked = settings.ShowTickPoints;
        ShowTickCrosshairCheckBox.IsChecked = settings.ShowTickCrosshair;

        SetColor(nameof(ChartSettings.ChartBackgroundColor), settings.ChartBackgroundColor);
        SetColor(nameof(ChartSettings.ChartTextColor), settings.ChartTextColor);
        SetColor(nameof(ChartSettings.SelectedCandleColor), settings.SelectedCandleColor);
        SetColor(nameof(ChartSettings.HistoryBoundaryColor), settings.HistoryBoundaryColor);
        SetColor(nameof(ChartSettings.LatestButtonColor), settings.LatestButtonColor);
        SetColor(nameof(ChartSettings.LatestButtonTextColor), settings.LatestButtonTextColor);
        SetColor(nameof(ChartSettings.PriceScaleBackgroundColor), settings.PriceScaleBackgroundColor);
        SetColor(nameof(ChartSettings.PriceScaleTextColor), settings.PriceScaleTextColor);
        SetColor(nameof(ChartSettings.TimeScaleBackgroundColor), settings.TimeScaleBackgroundColor);
        SetColor(nameof(ChartSettings.TimeScaleTextColor), settings.TimeScaleTextColor);
        SetColor(nameof(ChartSettings.GridColor), settings.GridColor);
        SetColor(nameof(ChartSettings.UpBodyColor), settings.UpBodyColor);
        SetColor(nameof(ChartSettings.UpBorderColor), settings.UpBorderColor);
        SetColor(nameof(ChartSettings.UpWickColor), settings.UpWickColor);
        SetColor(nameof(ChartSettings.DownBodyColor), settings.DownBodyColor);
        SetColor(nameof(ChartSettings.DownBorderColor), settings.DownBorderColor);
        SetColor(nameof(ChartSettings.DownWickColor), settings.DownWickColor);
        SetColor(nameof(ChartSettings.TickBidColor), settings.TickBidColor);
        SetColor(nameof(ChartSettings.TickAskColor), settings.TickAskColor);
        SetColor(nameof(ChartSettings.PriceLineColor), settings.PriceLineColor);
        SetColor(nameof(ChartSettings.PriceLineTextColor), settings.PriceLineTextColor);
        SetColor(nameof(ChartSettings.AskPriceLineColor), settings.AskPriceLineColor);
        SetColor(nameof(ChartSettings.AskPriceLineTextColor), settings.AskPriceLineTextColor);
        SetColor(nameof(ChartSettings.SpreadLineColor), settings.SpreadLineColor);
        SetColor(nameof(ChartSettings.CrosshairColor), settings.CrosshairColor);
        SetColor(nameof(ChartSettings.CrosshairLabelBackgroundColor), settings.CrosshairLabelBackgroundColor);
        SetColor(nameof(ChartSettings.CrosshairLabelTextColor), settings.CrosshairLabelTextColor);

        SetNumber(nameof(ChartSettings.GridOpacity), settings.GridOpacity);
        SetNumber(nameof(ChartSettings.GridThickness), settings.GridThickness);
        SetNumber(nameof(ChartSettings.CandleBorderThickness), settings.CandleBorderThickness);
        SetNumber(nameof(ChartSettings.CandleWickThickness), settings.CandleWickThickness);
        SetNumber(nameof(ChartSettings.TickBidThickness), settings.TickBidThickness);
        SetNumber(nameof(ChartSettings.TickAskThickness), settings.TickAskThickness);
        SetNumber(nameof(ChartSettings.PriceLineThickness), settings.PriceLineThickness);
        SetNumber(nameof(ChartSettings.AskPriceLineThickness), settings.AskPriceLineThickness);
        SetNumber(nameof(ChartSettings.SpreadLineThickness), settings.SpreadLineThickness);
        SetNumber(nameof(ChartSettings.SpreadFillOpacity), settings.SpreadFillOpacity);
        SetNumber(nameof(ChartSettings.CrosshairThickness), settings.CrosshairThickness);

        SetStyle(nameof(ChartSettings.PriceLineStyle), settings.PriceLineStyle);
        SetStyle(nameof(ChartSettings.AskPriceLineStyle), settings.AskPriceLineStyle);
        SetStyle(nameof(ChartSettings.SpreadLineStyle), settings.SpreadLineStyle);
        SetStyle(nameof(ChartSettings.CrosshairLineStyle), settings.CrosshairLineStyle);
        SetCheck(nameof(ChartSettings.ShowPriceLine), settings.ShowPriceLine);
        SetCheck(nameof(ChartSettings.ShowAskPriceLine), settings.ShowAskPriceLine);
        SetCheck(nameof(ChartSettings.ShowSpreadLine), settings.ShowSpreadLine);
        SetCheck(nameof(ChartSettings.ShowSpreadLabel), settings.ShowSpreadLabel);
        SetCheck(nameof(ChartSettings.ShowSpreadFill), settings.ShowSpreadFill);
        SetCheck(nameof(ChartSettings.ShowCandleCountdown), settings.ShowCandleCountdown);
        _loading = false;
        RefreshSwatches();
    }

    private ChartSettings ReadControls()
    {
        ChartSettings current = Settings;
        return current with
        {
            ScrollWheelMode = ScrollModeRadioButton.IsChecked == true ? ChartScrollWheelMode.Scroll : ChartScrollWheelMode.Zoom,
            ShowCandleGrid = ShowCandleGridCheckBox.IsChecked == true,
            ShowCandleCrosshair = ShowCandleCrosshairCheckBox.IsChecked == true,
            SnapCandleCrosshair = SnapCandleCrosshairCheckBox.IsChecked == true,
            ShowCrosshairLabels = ShowCrosshairLabelsCheckBox.IsChecked == true,
            ShowTickGrid = ShowTickGridCheckBox.IsChecked == true,
            ShowBidLine = ShowBidLineCheckBox.IsChecked == true,
            ShowAskLine = ShowAskLineCheckBox.IsChecked == true,
            ShowMidLine = ShowMidLineCheckBox.IsChecked == true,
            ShowTickPoints = ShowTickPointsCheckBox.IsChecked == true,
            ShowTickCrosshair = ShowTickCrosshairCheckBox.IsChecked == true,
            ChartBackgroundColor = GetColor(nameof(ChartSettings.ChartBackgroundColor), current.ChartBackgroundColor),
            ChartTextColor = GetColor(nameof(ChartSettings.ChartTextColor), current.ChartTextColor),
            SelectedCandleColor = GetColor(nameof(ChartSettings.SelectedCandleColor), current.SelectedCandleColor),
            HistoryBoundaryColor = GetColor(nameof(ChartSettings.HistoryBoundaryColor), current.HistoryBoundaryColor),
            LatestButtonColor = GetColor(nameof(ChartSettings.LatestButtonColor), current.LatestButtonColor),
            LatestButtonTextColor = GetColor(nameof(ChartSettings.LatestButtonTextColor), current.LatestButtonTextColor),
            PriceScaleBackgroundColor = GetColor(nameof(ChartSettings.PriceScaleBackgroundColor), current.PriceScaleBackgroundColor),
            PriceScaleTextColor = GetColor(nameof(ChartSettings.PriceScaleTextColor), current.PriceScaleTextColor),
            TimeScaleBackgroundColor = GetColor(nameof(ChartSettings.TimeScaleBackgroundColor), current.TimeScaleBackgroundColor),
            TimeScaleTextColor = GetColor(nameof(ChartSettings.TimeScaleTextColor), current.TimeScaleTextColor),
            GridColor = GetColor(nameof(ChartSettings.GridColor), current.GridColor),
            GridOpacity = GetNumber(nameof(ChartSettings.GridOpacity), current.GridOpacity),
            GridThickness = GetNumber(nameof(ChartSettings.GridThickness), current.GridThickness),
            UpBodyColor = GetColor(nameof(ChartSettings.UpBodyColor), current.UpBodyColor),
            UpBorderColor = GetColor(nameof(ChartSettings.UpBorderColor), current.UpBorderColor),
            UpWickColor = GetColor(nameof(ChartSettings.UpWickColor), current.UpWickColor),
            DownBodyColor = GetColor(nameof(ChartSettings.DownBodyColor), current.DownBodyColor),
            DownBorderColor = GetColor(nameof(ChartSettings.DownBorderColor), current.DownBorderColor),
            DownWickColor = GetColor(nameof(ChartSettings.DownWickColor), current.DownWickColor),
            CandleBorderThickness = GetNumber(nameof(ChartSettings.CandleBorderThickness), current.CandleBorderThickness),
            CandleWickThickness = GetNumber(nameof(ChartSettings.CandleWickThickness), current.CandleWickThickness),
            TickBidColor = GetColor(nameof(ChartSettings.TickBidColor), current.TickBidColor),
            TickAskColor = GetColor(nameof(ChartSettings.TickAskColor), current.TickAskColor),
            TickBidThickness = GetNumber(nameof(ChartSettings.TickBidThickness), current.TickBidThickness),
            TickAskThickness = GetNumber(nameof(ChartSettings.TickAskThickness), current.TickAskThickness),
            ShowPriceLine = GetCheck(nameof(ChartSettings.ShowPriceLine), current.ShowPriceLine),
            PriceLineColor = GetColor(nameof(ChartSettings.PriceLineColor), current.PriceLineColor),
            PriceLineTextColor = GetColor(nameof(ChartSettings.PriceLineTextColor), current.PriceLineTextColor),
            PriceLineStyle = GetStyle(nameof(ChartSettings.PriceLineStyle), current.PriceLineStyle),
            PriceLineThickness = GetNumber(nameof(ChartSettings.PriceLineThickness), current.PriceLineThickness),
            ShowAskPriceLine = GetCheck(nameof(ChartSettings.ShowAskPriceLine), current.ShowAskPriceLine),
            AskPriceLineColor = GetColor(nameof(ChartSettings.AskPriceLineColor), current.AskPriceLineColor),
            AskPriceLineTextColor = GetColor(nameof(ChartSettings.AskPriceLineTextColor), current.AskPriceLineTextColor),
            AskPriceLineStyle = GetStyle(nameof(ChartSettings.AskPriceLineStyle), current.AskPriceLineStyle),
            AskPriceLineThickness = GetNumber(nameof(ChartSettings.AskPriceLineThickness), current.AskPriceLineThickness),
            ShowSpreadLine = GetCheck(nameof(ChartSettings.ShowSpreadLine), current.ShowSpreadLine),
            SpreadLineColor = GetColor(nameof(ChartSettings.SpreadLineColor), current.SpreadLineColor),
            SpreadLineStyle = GetStyle(nameof(ChartSettings.SpreadLineStyle), current.SpreadLineStyle),
            SpreadLineThickness = GetNumber(nameof(ChartSettings.SpreadLineThickness), current.SpreadLineThickness),
            ShowSpreadLabel = GetCheck(nameof(ChartSettings.ShowSpreadLabel), current.ShowSpreadLabel),
            ShowSpreadFill = GetCheck(nameof(ChartSettings.ShowSpreadFill), current.ShowSpreadFill),
            SpreadFillOpacity = GetNumber(nameof(ChartSettings.SpreadFillOpacity), current.SpreadFillOpacity),
            ShowCandleCountdown = GetCheck(nameof(ChartSettings.ShowCandleCountdown), current.ShowCandleCountdown),
            CrosshairColor = GetColor(nameof(ChartSettings.CrosshairColor), current.CrosshairColor),
            CrosshairLabelBackgroundColor = GetColor(nameof(ChartSettings.CrosshairLabelBackgroundColor), current.CrosshairLabelBackgroundColor),
            CrosshairLabelTextColor = GetColor(nameof(ChartSettings.CrosshairLabelTextColor), current.CrosshairLabelTextColor),
            CrosshairLineStyle = GetStyle(nameof(ChartSettings.CrosshairLineStyle), current.CrosshairLineStyle),
            CrosshairThickness = GetNumber(nameof(ChartSettings.CrosshairThickness), current.CrosshairThickness)
        };
    }

    private void SetColor(string key, string value) { if (_colorBoxes.TryGetValue(key, out TextBox? box)) box.Text = value; }
    private string GetColor(string key, string fallback) => _colorBoxes.TryGetValue(key, out TextBox? box) && IsColor(box.Text) ? box.Text.Trim().ToUpperInvariant() : fallback;
    private void SetNumber(string key, double value) { if (_numberSliders.TryGetValue(key, out Slider? slider)) slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum); }
    private double GetNumber(string key, double fallback) => _numberSliders.TryGetValue(key, out Slider? slider) ? slider.Value : fallback;
    private void SetStyle(string key, ChartLineStyle value) { if (_styleBoxes.TryGetValue(key, out ComboBox? combo)) combo.SelectedItem = value; }
    private ChartLineStyle GetStyle(string key, ChartLineStyle fallback) => _styleBoxes.TryGetValue(key, out ComboBox? combo) && combo.SelectedItem is ChartLineStyle value ? value : fallback;
    private void SetCheck(string key, bool value) { if (_dynamicChecks.TryGetValue(key, out CheckBox? check)) check.IsChecked = value; }
    private bool GetCheck(string key, bool fallback) => _dynamicChecks.TryGetValue(key, out CheckBox? check) ? check.IsChecked == true : fallback;

    private void RefreshSwatches()
    {
        foreach ((string key, TextBox box) in _colorBoxes)
        {
            if (box.Parent is not Grid panel || panel.Children.OfType<Button>().FirstOrDefault() is not Button swatch) continue;
            ColorDisplayHelper.ApplyToButton(swatch, box.Text);
        }
    }

    private static bool IsColor(string? value)
    {
        try { return ColorConverter.ConvertFromString(value) is Color; }
        catch { return false; }
    }

}
