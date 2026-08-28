using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Drawing;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class TradingViewLineSettingsWindow : Window
{
    private readonly ChartDrawing _original;
    private readonly DrawingToolDefinition _tool;
    private readonly CandleChartControl _chart;
    private readonly ObservableCollection<CoordinateRow> _coordinates = new();
    private readonly ObservableCollection<LevelRow> _levels = new();
    private readonly IReadOnlyList<DrawingTemplate> _templates;
    private bool _loading = true;
    private string _lineColor;
    private string _textColor;

    public TradingViewLineSettingsWindow(
        ChartDrawing drawing,
        IReadOnlyList<DrawingTemplate>? templates,
        CandleChartControl chart,
        string initialTab = "Style")
    {
        InitializeComponent();
        Loaded += (_, _) => ApplicationThemeManager.ApplyToWindow(this);
        _original = drawing;
        _chart = chart;
        _tool = DrawingToolCatalog.Find(drawing.ToolId) ?? DrawingToolCatalog.Find("trend-line")!;
        _templates = (templates ?? Array.Empty<DrawingTemplate>())
            .Where(item => string.Equals(item.ToolId, drawing.ToolId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _lineColor = drawing.Style.LineColor;
        _textColor = drawing.Style.TextColor;
        UpdatedDrawing = drawing;

        TitleText.Text = drawing.DisplayName;
        LineStyleBox.ItemsSource = Enum.GetValues<DrawingLineStyle>();
        LineWidthBox.ItemsSource = new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0 };
        ExtendBox.ItemsSource = new[] { "Don't extend", "Extend left", "Extend right", "Extend both" };
        StatsBox.ItemsSource = new[] { "Hidden", "Visible" };
        StatsPositionBox.ItemsSource = new[] { "Right", "Left" };
        FontSizeBox.ItemsSource = new[] { 9.0, 10.0, 11.0, 12.0, 14.0, 16.0, 18.0, 20.0, 24.0, 28.0, 32.0 };
        RegressionSourceBox.ItemsSource = new[] { "Open", "High", "Low", "Close", "HL2", "HLC3", "OHLC4" };

        CoordinatesGrid.ItemsSource = _coordinates;
        LevelsGrid.ItemsSource = _levels;
        ChannelLevelsGrid.ItemsSource = _levels;
        PitchforkStyleBox.ItemsSource = new[] { "Original", "Schiff", "Modified Schiff", "Inside" };
        LoadDrawing(drawing);
        ConfigureToolSpecificUi();

        TemplateBox.ItemsSource = new[] { "Template" }.Concat(_templates.Select(item => item.Name)).ToArray();
        TemplateBox.SelectedIndex = 0;

        Tabs.SelectedItem = initialTab.Equals("Text", StringComparison.OrdinalIgnoreCase) && TextTab.Visibility == Visibility.Visible
            ? TextTab
            : initialTab.Equals("Coordinates", StringComparison.OrdinalIgnoreCase) ? CoordinatesTab
            : initialTab.Equals("Visibility", StringComparison.OrdinalIgnoreCase) ? VisibilityTab
            : StyleTab;

        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnyTextChanged));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        _loading = false;
        RefreshSwatches();
        RefreshOpacityLabels();
        RefreshVisibilityRangeLabels();
    }

    public event Action<ChartDrawing>? PreviewChanged;
    public ChartDrawing UpdatedDrawing { get; private set; }
    public bool WasAccepted { get; private set; }

    private void ConfigureToolSpecificUi()
    {
        bool simpleLine = _tool.Id is "trend-line" or "ray" or "info-line" or "extended-line" or "trend-angle";
        bool channel = _tool.Id is "parallel-channel" or "flat-top-bottom" or "disjoint-channel";
        bool regression = _tool.Id == "regression-trend";
        bool pitchfork = _tool.Id is "pitchfork" or "schiff-pitchfork" or "modified-schiff-pitchfork" or "inside-pitchfork";
        bool oneAnchor = _tool.Id is "horizontal-line" or "horizontal-ray" or "vertical-line" or "cross-line";

        MiddlePointBox.Visibility = simpleLine ? Visibility.Visible : Visibility.Collapsed;
        ArrowStartBox.Visibility = simpleLine && _tool.Id != "trend-angle" ? Visibility.Visible : Visibility.Collapsed;
        ArrowEndBox.Visibility = simpleLine && _tool.Id != "trend-angle" ? Visibility.Visible : Visibility.Collapsed;
        PriceLabelsBox.Visibility = _tool.Id is "trend-line" or "ray" or "info-line" or "extended-line" or "trend-angle" or "horizontal-line" or "horizontal-ray" or "cross-line"
            ? Visibility.Visible : Visibility.Collapsed;
        ExtendBox.IsEnabled = !oneAnchor && _tool.Id != "trend-angle";
        GeneralExtendPanel.Visibility = pitchfork ? Visibility.Collapsed : Visibility.Visible;
        PrimaryLineLabel.Text = pitchfork ? "Median" : "Line";
        ChannelSection.Visibility = channel ? Visibility.Visible : Visibility.Collapsed;
        RegressionSection.Visibility = regression ? Visibility.Visible : Visibility.Collapsed;
        PitchforkSection.Visibility = pitchfork ? Visibility.Visible : Visibility.Collapsed;
        FillOpacityPanel.Visibility = (channel || pitchfork) ? Visibility.Visible : Visibility.Collapsed;
        InfoSection.Visibility = simpleLine ? Visibility.Visible : Visibility.Collapsed;
        AnglePanel.Visibility = _tool.Id == "trend-angle" ? Visibility.Visible : Visibility.Collapsed;
        TextTab.Visibility = _tool.SupportsText && _tool.Id != "trend-angle" ? Visibility.Visible : Visibility.Collapsed;

        if (oneAnchor)
        {
            ExtendBox.SelectedIndex = _tool.Id == "horizontal-ray" ? 2 : 0;
            MiddlePointBox.Visibility = Visibility.Collapsed;
            ArrowStartBox.Visibility = Visibility.Collapsed;
            ArrowEndBox.Visibility = Visibility.Collapsed;
            InfoSection.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadDrawing(ChartDrawing drawing)
    {
        DrawingStyle s = drawing.Style;
        LineStyleBox.SelectedItem = s.LineStyle;
        LineWidthBox.SelectedItem = ((double[])LineWidthBox.ItemsSource).OrderBy(value => Math.Abs(value - s.LineWidth)).First();
        LineOpacitySlider.Value = Math.Clamp(s.Opacity, 0, 1);
        FillOpacitySlider.Value = Math.Clamp(s.FillOpacity, 0, 1);
        RefreshOpacityLabels();
        ExtendBox.SelectedIndex = s.ExtendLeft && s.ExtendRight ? 3 : s.ExtendLeft ? 1 : s.ExtendRight ? 2 : 0;
        MiddlePointBox.IsChecked = Flag(drawing, "MiddlePoint", false);
        PriceLabelsBox.IsChecked = s.ShowPriceLabels;
        PlaceAsBackgroundBox.IsChecked = drawing.VisualLayer == DrawingVisualLayer.BelowCandles;
        ArrowStartBox.IsChecked = s.ArrowStart;
        ArrowEndBox.IsChecked = s.ArrowEnd;
        StatsBox.SelectedItem = s.ShowStatistics ? "Visible" : "Hidden";
        StatsPositionBox.SelectedItem = drawing.TextOptions.TryGetValue("StatsPosition", out string? position) ? position : "Right";
        AlwaysStatsBox.IsChecked = Flag(drawing, "AlwaysShowStats", drawing.ToolId == "info-line");
        MiddleLineBox.IsChecked = s.ShowMiddleLine || Flag(drawing, "MiddleLine", false);
        BackgroundBox.IsChecked = s.FillOpacity > 0.0001;
        PitchforkExtendBox.IsChecked = Flag(drawing, "ExtendRight", false);
        PitchforkUseOneColorBox.IsChecked = Flag(drawing, "UseOneColor", false);
        PitchforkBackgroundBox.IsChecked = Flag(drawing, "Background", true);
        UpperDeviationBox.Text = Option(drawing, "UpperDeviation", 2).ToString("0.###", CultureInfo.InvariantCulture);
        LowerDeviationBox.Text = Option(drawing, "LowerDeviation", 2).ToString("0.###", CultureInfo.InvariantCulture);
        RegressionSourceBox.SelectedIndex = (int)Math.Clamp(Option(drawing, "Source", 3), 0, 6);
        PearsonsRBox.IsChecked = Flag(drawing, "PearsonsR", true);
        PitchforkStyleBox.SelectedIndex = drawing.ToolId switch
        {
            "schiff-pitchfork" => 1,
            "modified-schiff-pitchfork" => 2,
            "inside-pitchfork" => 3,
            _ => 0
        };
        DrawingTextBox.Text = drawing.Text;
        FontSizeBox.SelectedItem = ((double[])FontSizeBox.ItemsSource).OrderBy(value => Math.Abs(value - s.FontSize)).First();
        BoldBox.IsChecked = s.Bold;
        ItalicBox.IsChecked = s.Italic;
        TicksBox.IsChecked = Option(drawing, "VisibilityTicks", 1) >= 0.5;
        SecondsBox.IsChecked = drawing.Visibility.Seconds;
        MinutesBox.IsChecked = drawing.Visibility.Minutes;
        HoursBox.IsChecked = drawing.Visibility.Hours;
        DailyBox.IsChecked = drawing.Visibility.Daily;
        WeeklyBox.IsChecked = drawing.Visibility.Weekly;
        MonthlyBox.IsChecked = drawing.Visibility.Monthly;
        RangesBox.IsChecked = Option(drawing, "VisibilityRanges", 1) >= 0.5;
        LoadVisibilityRange(SecondsMinBox, SecondsMaxSlider, SecondsMaxText, drawing, "VisibilitySeconds", 59);
        LoadVisibilityRange(MinutesMinBox, MinutesMaxSlider, MinutesMaxText, drawing, "VisibilityMinutes", 59);
        LoadVisibilityRange(HoursMinBox, HoursMaxSlider, HoursMaxText, drawing, "VisibilityHours", 24);
        LoadVisibilityRange(DailyMinBox, DailyMaxSlider, DailyMaxText, drawing, "VisibilityDays", 366);
        LoadVisibilityRange(WeeklyMinBox, WeeklyMaxSlider, WeeklyMaxText, drawing, "VisibilityWeeks", 52);
        LoadVisibilityRange(MonthlyMinBox, MonthlyMaxSlider, MonthlyMaxText, drawing, "VisibilityMonths", 12);
        AngleBox.Text = (_chart.GetDrawingScreenAngle(drawing) ?? 0).ToString("0.##", CultureInfo.InvariantCulture);

        _coordinates.Clear();
        for (int i = 0; i < drawing.Anchors.Count; i++)
        {
            DrawingAnchor a = drawing.Anchors[i];
            int barIndex = _chart.GetDrawingAnchorBarIndex(a);
            _coordinates.Add(new CoordinateRow
            {
                Index = i + 1,
                OriginalUnix = a.StartUnix,
                LabelText = $"#{i + 1} (price, bar)",
                PriceText = a.Price.ToString("0.##########", CultureInfo.InvariantCulture),
                BarText = (barIndex + 1).ToString(CultureInfo.InvariantCulture)
            });
        }

        _levels.Clear();
        IReadOnlyList<DrawingLevel> sourceLevels = drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        foreach (DrawingLevel level in sourceLevels)
            _levels.Add(new LevelRow
            {
                Enabled = level.Enabled,
                ValueText = level.Value.ToString("0.###", CultureInfo.InvariantCulture),
                Color = level.Color,
                WidthText = level.Width.ToString("0.##", CultureInfo.InvariantCulture),
                LineStyle = level.LineStyle,
                Label = level.Label,
                ShowPrice = level.ShowPrice,
                ShowValue = level.ShowValue,
                FillColor = level.FillColor,
                FillOpacity = level.FillOpacity
            });
    }

    private ChartDrawing BuildDrawing()
    {
        ChartDrawing baseDrawing = UpdatedDrawing;
        DrawingStyle old = baseDrawing.Style;
        int extend = Math.Max(0, ExtendBox.SelectedIndex);
        var numeric = DrawingParityDefaults.MergeOptions(baseDrawing.ToolId, baseDrawing.NumericOptions)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        numeric["MiddlePoint"] = MiddlePointBox.IsChecked == true ? 1 : 0;
        numeric["AlwaysShowStats"] = AlwaysStatsBox.IsChecked == true ? 1 : 0;
        numeric["MiddleLine"] = MiddleLineBox.IsChecked == true ? 1 : 0;
        numeric["UpperDeviation"] = ParseDouble(UpperDeviationBox.Text, Option(baseDrawing, "UpperDeviation", 2));
        numeric["LowerDeviation"] = ParseDouble(LowerDeviationBox.Text, Option(baseDrawing, "LowerDeviation", 2));
        numeric["Source"] = Math.Max(0, RegressionSourceBox.SelectedIndex);
        numeric["PearsonsR"] = PearsonsRBox.IsChecked == true ? 1 : 0;
        numeric["ExtendRight"] = PitchforkExtendBox.IsChecked == true ? 1 : 0;
        numeric["UseOneColor"] = PitchforkUseOneColorBox.IsChecked == true ? 1 : 0;
        numeric["Background"] = PitchforkBackgroundBox.IsChecked == true ? 1 : 0;
        numeric["VisibilityTicks"] = TicksBox.IsChecked == true ? 1 : 0;
        numeric["VisibilityRanges"] = RangesBox.IsChecked == true ? 1 : 0;
        SaveVisibilityRange(numeric, SecondsMinBox, SecondsMaxSlider, "VisibilitySeconds", 59);
        SaveVisibilityRange(numeric, MinutesMinBox, MinutesMaxSlider, "VisibilityMinutes", 59);
        SaveVisibilityRange(numeric, HoursMinBox, HoursMaxSlider, "VisibilityHours", 24);
        SaveVisibilityRange(numeric, DailyMinBox, DailyMaxSlider, "VisibilityDays", 366);
        SaveVisibilityRange(numeric, WeeklyMinBox, WeeklyMaxSlider, "VisibilityWeeks", 52);
        SaveVisibilityRange(numeric, MonthlyMinBox, MonthlyMaxSlider, "VisibilityMonths", 12);
        if (baseDrawing.ToolId == "trend-angle") numeric["AngleLabel"] = 1;

        var textOptions = baseDrawing.TextOptions
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        textOptions["StatsPosition"] = StatsPositionBox.SelectedItem?.ToString() ?? "Right";

        DrawingStyle style = old with
        {
            LineColor = _lineColor,
            TextColor = _textColor,
            LineStyle = LineStyleBox.SelectedItem is DrawingLineStyle ls ? ls : old.LineStyle,
            LineWidth = LineWidthBox.SelectedItem is double lw ? lw : old.LineWidth,
            Opacity = Math.Clamp(LineOpacitySlider.Value, 0, 1),
            ExtendLeft = extend is 1 or 3,
            ExtendRight = extend is 2 or 3,
            ShowPriceLabels = PriceLabelsBox.IsChecked == true,
            ShowStatistics = string.Equals(StatsBox.SelectedItem?.ToString(), "Visible", StringComparison.OrdinalIgnoreCase),
            ShowMiddleLine = MiddleLineBox.IsChecked == true,
            ArrowStart = ArrowStartBox.IsChecked == true,
            ArrowEnd = ArrowEndBox.IsChecked == true,
            FillOpacity = (BackgroundBox.IsChecked == true || PitchforkBackgroundBox.IsChecked == true) ? Math.Clamp(FillOpacitySlider.Value, 0, 1) : 0,
            FontSize = FontSizeBox.SelectedItem is double fs ? fs : old.FontSize,
            Bold = BoldBox.IsChecked == true,
            Italic = ItalicBox.IsChecked == true
        };

        var anchors = new List<DrawingAnchor>();
        for (int i = 0; i < _coordinates.Count; i++)
        {
            CoordinateRow row = _coordinates[i];
            DrawingAnchor fallback = i < baseDrawing.Anchors.Count ? baseDrawing.Anchors[i] : new DrawingAnchor(row.OriginalUnix, 0);
            int bar = int.TryParse(row.BarText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBar)
                ? Math.Max(1, parsedBar) - 1
                : _chart.GetDrawingAnchorBarIndex(fallback);
            long unix = _chart.GetDrawingBarStartUnix(bar, fallback.StartUnix);
            double price = ParseDouble(row.PriceText, fallback.Price);
            anchors.Add(fallback with { StartUnix = unix, Price = price });
        }

        IReadOnlyList<DrawingLevel> levels = _levels.Select(row => new DrawingLevel(
            ParseDouble(row.ValueText, 0), row.Label, row.Enabled, NormalizeColor(row.Color, _lineColor),
            ParseDouble(row.WidthText, 1), row.LineStyle, row.ShowPrice, row.ShowValue,
            row.FillColor, row.FillOpacity)).ToArray();

        string resultToolId = baseDrawing.ToolId;
        if (_tool.Geometry == DrawingGeometryKind.Pitchfork)
        {
            resultToolId = PitchforkStyleBox.SelectedIndex switch
            {
                1 => "schiff-pitchfork",
                2 => "modified-schiff-pitchfork",
                3 => "inside-pitchfork",
                _ => "pitchfork"
            };
        }
        DrawingToolDefinition? resultTool = DrawingToolCatalog.Find(resultToolId);

        ChartDrawing result = baseDrawing with
        {
            ToolId = resultToolId,
            DisplayName = resultTool?.DisplayName ?? baseDrawing.DisplayName,
            Anchors = anchors,
            Style = style,
            Visibility = baseDrawing.Visibility with
            {
                Seconds = SecondsBox.IsChecked == true,
                Minutes = MinutesBox.IsChecked == true,
                Hours = HoursBox.IsChecked == true,
                Daily = DailyBox.IsChecked == true,
                Weekly = WeeklyBox.IsChecked == true,
                Monthly = MonthlyBox.IsChecked == true
            },
            Levels = levels,
            Text = DrawingTextBox.Text,
            NumericOptions = numeric,
            TextOptions = textOptions,
            VisualLayer = PlaceAsBackgroundBox.IsChecked == true
                ? DrawingVisualLayer.BelowCandles
                : (baseDrawing.VisualLayer == DrawingVisualLayer.BelowCandles ? DrawingVisualLayer.AboveCandles : baseDrawing.VisualLayer),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (baseDrawing.ToolId == "trend-angle" && double.TryParse(AngleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle))
            result = _chart.WithDrawingScreenAngle(result, angle);
        return result;
    }

    private void EmitPreview()
    {
        if (_loading) return;
        try
        {
            UpdatedDrawing = BuildDrawing();
            PreviewChanged?.Invoke(UpdatedDrawing);
        }
        catch
        {
            // Keep the last valid preview while the user is midway through typing.
        }
    }

    private void LineOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshOpacityLabels();
        EmitPreview();
    }

    private void FillOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshOpacityLabels();
        EmitPreview();
    }

    private void VisibilityRangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // WPF can raise ValueChanged while InitializeComponent() is still creating
        // the Visibility tab. At that point later label controls may not exist yet.
        // Ignore those construction-time events; LoadDrawing() sets the values and
        // the constructor performs one explicit refresh after loading completes.
        if (_loading) return;

        RefreshVisibilityRangeLabels();
        EmitPreview();
    }

    private void RefreshOpacityLabels()
    {
        if (LineOpacityValueText is not null)
            LineOpacityValueText.Text = $"{Math.Round(Math.Clamp(LineOpacitySlider.Value, 0, 1) * 100):0}%";
        if (FillOpacityValueText is not null)
            FillOpacityValueText.Text = $"{Math.Round(Math.Clamp(FillOpacitySlider.Value, 0, 1) * 100):0}%";
    }

    private void RefreshVisibilityRangeLabels()
    {
        // Be defensive even outside the constructor: each XAML field is created in
        // declaration order, so a routed ValueChanged event can observe a partially
        // initialized control tree. Never assume that if the first label exists all
        // of the later labels/sliders already exist as well.
        if (SecondsMaxText is not null && SecondsMaxSlider is not null)
            SecondsMaxText.Text = Math.Round(SecondsMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
        if (MinutesMaxText is not null && MinutesMaxSlider is not null)
            MinutesMaxText.Text = Math.Round(MinutesMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
        if (HoursMaxText is not null && HoursMaxSlider is not null)
            HoursMaxText.Text = Math.Round(HoursMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
        if (DailyMaxText is not null && DailyMaxSlider is not null)
            DailyMaxText.Text = Math.Round(DailyMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
        if (WeeklyMaxText is not null && WeeklyMaxSlider is not null)
            WeeklyMaxText.Text = Math.Round(WeeklyMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
        if (MonthlyMaxText is not null && MonthlyMaxSlider is not null)
            MonthlyMaxText.Text = Math.Round(MonthlyMaxSlider.Value).ToString(CultureInfo.InvariantCulture);
    }

    private static void LoadVisibilityRange(TextBox minBox, Slider maxSlider, TextBlock maxText, ChartDrawing drawing, string prefix, double defaultMax)
    {
        double minimum = Option(drawing, prefix + "Min", 1);
        double maximum = Option(drawing, prefix + "Max", defaultMax);
        minBox.Text = Math.Max(1, Math.Round(minimum)).ToString(CultureInfo.InvariantCulture);
        maxSlider.Value = Math.Clamp(Math.Round(maximum), 1, defaultMax);
        maxText.Text = Math.Round(maxSlider.Value).ToString(CultureInfo.InvariantCulture);
    }

    private static void SaveVisibilityRange(Dictionary<string, double> numeric, TextBox minBox, Slider maxSlider, string prefix, double maxAllowed)
    {
        double minimum = Math.Clamp(ParseDouble(minBox.Text, 1), 1, maxAllowed);
        double maximum = Math.Clamp(maxSlider.Value, minimum, maxAllowed);
        numeric[prefix + "Min"] = minimum;
        numeric[prefix + "Max"] = maximum;
    }

    private void SettingChanged(object sender, SelectionChangedEventArgs e) => EmitPreview();
    private void SettingChanged(object sender, RoutedEventArgs e) => EmitPreview();
    private void TextSettingChanged(object sender, TextChangedEventArgs e) => EmitPreview();
    private void AnyTextChanged(object sender, TextChangedEventArgs e) => EmitPreview();
    private void AnyToggleChanged(object sender, RoutedEventArgs e) => EmitPreview();

    private void LineColorButton_Click(object sender, RoutedEventArgs e) => PickColor(true);
    private void TextColorButton_Click(object sender, RoutedEventArgs e) => PickColor(false);

    private void PitchforkLevelColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LevelRow row }) return;
        string original = row.Color;
        var picker = new DrawingColorPickerWindow(original) { Owner = this };
        picker.ColorPreviewChanged += value =>
        {
            row.Color = value;
            row.FillColor = value;
            LevelsGrid.Items.Refresh();
            EmitPreview();
        };
        if (picker.ShowDialog() == true)
        {
            row.Color = picker.SelectedColor;
            row.FillColor = picker.SelectedColor;
        }
        else
        {
            row.Color = original;
            row.FillColor = original;
        }
        LevelsGrid.Items.Refresh();
        EmitPreview();
    }

    private void PickColor(bool line)
    {
        string original = line ? _lineColor : _textColor;
        var picker = new DrawingColorPickerWindow(original) { Owner = this };
        picker.ColorPreviewChanged += value =>
        {
            if (line) _lineColor = value; else _textColor = value;
            RefreshSwatches();
            EmitPreview();
        };
        if (picker.ShowDialog() == true)
        {
            if (line) _lineColor = picker.SelectedColor; else _textColor = picker.SelectedColor;
        }
        else
        {
            if (line) _lineColor = original; else _textColor = original;
        }
        RefreshSwatches();
        EmitPreview();
    }

    private void RefreshSwatches()
    {
        LineColorButton.Background = Brush(_lineColor, Colors.DodgerBlue);
        TextColorButton.Background = Brush(_textColor, Colors.Black);
    }

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TemplateBox.SelectedIndex <= 0) return;
        int index = TemplateBox.SelectedIndex - 1;
        if (index < 0 || index >= _templates.Count) return;
        DrawingTemplate template = _templates[index];
        UpdatedDrawing = UpdatedDrawing with
        {
            Style = template.Style,
            Levels = template.Levels,
            NumericOptions = template.NumericOptions,
            TextOptions = template.TextOptions
        };
        _loading = true;
        _lineColor = UpdatedDrawing.Style.LineColor;
        _textColor = UpdatedDrawing.Style.TextColor;
        LoadDrawing(UpdatedDrawing);
        ConfigureToolSpecificUi();
        _loading = false;
        RefreshSwatches();
        EmitPreview();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatedDrawing = BuildDrawing();
        WasAccepted = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasAccepted = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        WasAccepted = false;
        Close();
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

    private static double Option(ChartDrawing drawing, string key, double fallback) =>
        drawing.NumericOptions.TryGetValue(key, out double value) ? value :
        DrawingParityDefaults.NumericOptions(drawing.ToolId).TryGetValue(key, out value) ? value : fallback;
    private static bool Flag(ChartDrawing drawing, string key, bool fallback) => Option(drawing, key, fallback ? 1 : 0) >= 0.5;
    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
    private static string NormalizeColor(string? value, string fallback)
    {
        try { return ColorConverter.ConvertFromString(value) is Color ? value! : fallback; }
        catch { return fallback; }
    }
    private static Brush Brush(string value, Color fallback)
    {
        try { if (ColorConverter.ConvertFromString(value) is Color c) return new SolidColorBrush(c); }
        catch { }
        return new SolidColorBrush(fallback);
    }

    public sealed class CoordinateRow
    {
        public int Index { get; set; }
        public long OriginalUnix { get; set; }
        public string LabelText { get; set; } = string.Empty;
        public string PriceText { get; set; } = string.Empty;
        public string BarText { get; set; } = string.Empty;
    }

    public sealed class LevelRow
    {
        public bool Enabled { get; set; }
        public string ValueText { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = "#46A5FF";
        public string WidthText { get; set; } = "1";
        public DrawingLineStyle LineStyle { get; set; }
        public bool ShowPrice { get; set; }
        public bool ShowValue { get; set; }
        public string FillColor { get; set; } = string.Empty;
        public double FillOpacity { get; set; } = -1;
    }
}
