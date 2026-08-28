using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Drawing;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class TradingViewFibGannSettingsWindow : Window
{
    private readonly ChartDrawing _original;
    private readonly DrawingToolDefinition _tool;
    private readonly ObservableCollection<AnchorRow> _anchors = new();
    private readonly ObservableCollection<LevelRow> _levels = new();
    private readonly ObservableCollection<OptionRow> _options = new();
    private readonly ObservableCollection<DrawingTemplate> _templates = new();
    private readonly List<string> _templateIdsToDelete = new();
    private string _templateIdToMakeDefault = string.Empty;
    private readonly DispatcherTimer _previewTimer;
    private bool _suppressPreview;

    public TradingViewFibGannSettingsWindow(ChartDrawing drawing, IReadOnlyList<DrawingTemplate>? templates = null)
    {
        InitializeComponent();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _previewTimer.Tick += (_, _) => EmitLivePreview();
        Closed += (_, _) => _previewTimer.Stop();
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnySettingChanged));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
        FillOpacitySlider.ValueChanged += FillOpacitySlider_ValueChanged;
        _original = drawing;
        _tool = DrawingToolCatalog.Find(drawing.ToolId) ?? DrawingToolCatalog.Find("fib-retracement")!;
        if (_tool.Category != DrawingToolCategory.FibonacciGann)
            throw new InvalidOperationException("TradingViewFibGannSettingsWindow only accepts Fibonacci/Gann tools.");
        bool positionTool = _tool.Geometry == DrawingGeometryKind.Position;
        bool mediaTool = _tool.Geometry is DrawingGeometryKind.Image or DrawingGeometryKind.Icon;
        TextLabelsTab.Visibility = _tool.SupportsText && !positionTool && !mediaTool
            ? Visibility.Visible
            : Visibility.Collapsed;
        LevelsSection.Visibility = _tool.SupportsLevels ? Visibility.Visible : Visibility.Collapsed;
        InputsTab.Visibility = DrawingParityDefaults.NumericOptions(_tool.Id).Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CoordinatesTab.Visibility = positionTool || mediaTool ? Visibility.Collapsed : Visibility.Visible;
        bool inputsFirst = positionTool || _tool.Id is "regression-trend" or "anchored-vwap" or
            "fixed-volume-profile" or "anchored-volume-profile" or "bars-pattern" or "ghost-feed";
        if (inputsFirst && InputsTab.Visibility == Visibility.Visible)
        {
            SettingsTabs.Items.Remove(InputsTab);
            SettingsTabs.Items.Insert(0, InputsTab);
        }
        ConfigureToolSpecificControls();
        UpdatedDrawing = drawing;
        Title = drawing.DisplayName;
        TitleText.Text = string.IsNullOrWhiteSpace(drawing.Name) ? drawing.DisplayName : drawing.Name;
        ToolTypeText.Text = drawing.DisplayName;
        TitleText.Text = string.IsNullOrWhiteSpace(drawing.Name) ? drawing.DisplayName : drawing.Name;
        ApplicationThemeManager.ApplyToWindow(this);
        LineStyleBox.ItemsSource = Enum.GetValues<DrawingLineStyle>();
        VisualLayerBox.ItemsSource = Enum.GetValues<DrawingVisualLayer>();
        SyncModeBox.ItemsSource = Enum.GetValues<DrawingSyncMode>();
        HorizontalAlignmentBox.ItemsSource = new[] { "Left", "Center", "Right" };
        VerticalAlignmentBox.ItemsSource = new[] { "Top", "Center", "Bottom" };
        LabelHorizontalBox.ItemsSource = new[] { "Left", "Center", "Right" };
        LabelVerticalBox.ItemsSource = new[] { "Above", "Middle", "Below" };
        CoordinatesGrid.ItemsSource = _anchors;
        LevelsGrid.ItemsSource = _levels;
        OptionsGrid.ItemsSource = _options;
        foreach (DrawingTemplate template in (templates ?? Array.Empty<DrawingTemplate>()).Where(item => item.ToolId == drawing.ToolId))
            _templates.Add(template);
        ExistingTemplatesBox.ItemsSource = _templates;
        ExistingTemplatesBox.SelectedItem = _templates.FirstOrDefault(item => item.IsDefault) ?? _templates.FirstOrDefault();
        _suppressPreview = true;
        LoadDrawing(drawing);
        _suppressPreview = false;
    }

    public bool WasAccepted { get; private set; }

    private void ConfigureToolSpecificControls()
    {
        static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        bool fibonacciGann = _tool.Category == DrawingToolCategory.FibonacciGann;
        bool fill = _tool.SupportsFill || fibonacciGann;
        bool commonFillColor = _tool.SupportsFill && !fibonacciGann;
        bool text = _tool.SupportsText;
        FillColorLabel.Visibility = V(commonFillColor);
        FillColorEditor.Visibility = V(commonFillColor);
        FillOpacityLabel.Visibility = V(fill);
        FillOpacityEditor.Visibility = V(fill);
        FillEnabledBox.Visibility = V(commonFillColor);
        TextColorLabel.Visibility = V(text);
        TextColorEditor.Visibility = V(text);

        string id = _tool.Id;
        bool extend = id is "trend-line" or "arrow" or "ray" or "info-line" or "extended-line" or "trend-angle" or
            "parallel-channel" or "flat-top-bottom" or "disjoint-channel" or "regression-trend" or
            "fib-retracement" or "trend-fib-extension" or "fib-channel";
        bool middleLine = id is "parallel-channel" or "flat-top-bottom" or "disjoint-channel";
        bool middlePoint = id is "trend-line" or "info-line" or "trend-angle" or "ray" or "extended-line";
        bool priceLabels = fibonacciGann || id is "trend-line" or "info-line" or "trend-angle" or "ray" or "extended-line" or
            "horizontal-line" or "horizontal-ray" or "cross-line" or
            "long-position" or "short-position" or "price-label" or "price-note";
        bool timeLabels = id is "vertical-line" or "cross-line" or "trend-line" or "info-line" or "trend-angle";
        bool arrows = id is "trend-line" or "arrow" or "ray" or "extended-line" or "info-line";
        bool stats = id is "trend-line" or "info-line" or "trend-angle" or "long-position" or "short-position" or
            "price-range" or "date-range" or "date-price-range";
        bool angle = id is "trend-line" or "info-line" or "trend-angle";

        ExtendLeftBox.Visibility = V(extend);
        ExtendRightBox.Visibility = V(extend);
        MiddleLineBox.Visibility = V(middleLine);
        MiddlePointBox.Visibility = V(middlePoint);
        PriceLabelsBox.Visibility = V(priceLabels);
        TimeLabelsBox.Visibility = V(timeLabels);
        ArrowStartBox.Visibility = V(arrows);
        ArrowEndBox.Visibility = V(arrows);
        StatisticsBox.Visibility = V(stats);
        AngleBox.Visibility = V(angle);
        AlwaysStatsBox.Visibility = V(stats);

        bool fibonacci = _tool.Category == DrawingToolCategory.FibonacciGann;
        bool fibRatioTool = id.StartsWith("fib-", StringComparison.OrdinalIgnoreCase) || id.StartsWith("trend-fib-", StringComparison.OrdinalIgnoreCase) || id == "pitchfan";
        bool gannGrid = id is "gann-box" or "gann-square" or "gann-square-fixed";
        ReverseBox.Visibility = V(fibonacci);
        UseOneColorBox.Visibility = V(fibonacci);
        LabelsPercentBox.Visibility = V(fibRatioTool);
        LabelsLeftBox.Visibility = Visibility.Collapsed; // superseded by the precise reading-position controls
        FibReadingsMasterPanel.Visibility = V(fibonacci);
        FibReadingButtonsPanel.Visibility = V(fibonacci);
        ShowLevelReadingsBox.Visibility = V(fibonacci);
        PriceLabelsBox.Visibility = V(fibonacci);
        BandsBox.Visibility = V(fibonacci);
        LabelsOutsideBox.Visibility = V(fibonacci);
        ReadingPositionPanel.Visibility = V(fibonacci);
        FanBox.Visibility = V(gannGrid);
        ArcsBox.Visibility = V(id is "gann-square" or "gann-square-fixed");
    }

    public event Action<ChartDrawing>? PreviewChanged;
    public ChartDrawing UpdatedDrawing { get; private set; }
    public bool SaveAsTemplate => SaveTemplateBox.IsChecked == true;
    public bool MakeDefaultTemplate => DefaultTemplateBox.IsChecked == true;
    public string TemplateName => TemplateNameBox.Text.Trim();
    public IReadOnlyList<string> TemplateIdsToDelete => _templateIdsToDelete;
    public string TemplateIdToMakeDefault => _templateIdToMakeDefault;

    private void LoadDrawing(ChartDrawing drawing)
    {
        DrawingStyle style = drawing.Style;
        LineColorBox.Text = style.LineColor;
        FillColorBox.Text = style.FillColor;
        TextColorBox.Text = style.TextColor;
        BackgroundColorBox.Text = style.BackgroundColor;
        LineWidthBox.Text = style.LineWidth.ToString(CultureInfo.InvariantCulture);
        LineStyleBox.SelectedItem = style.LineStyle;
        OpacityBox.Text = style.Opacity.ToString(CultureInfo.InvariantCulture);
        FillOpacityBox.Text = style.FillOpacity.ToString(CultureInfo.InvariantCulture);
        OpacitySlider.Value = Math.Clamp(style.Opacity, 0, 1);
        FillOpacitySlider.Value = Math.Clamp(style.FillOpacity, 0, 1);
        FillEnabledBox.IsChecked = style.FillOpacity > 0.0001;
        ExtendLeftBox.IsChecked = style.ExtendLeft;
        ExtendRightBox.IsChecked = style.ExtendRight;
        MiddleLineBox.IsChecked = style.ShowMiddleLine;
        MiddlePointBox.IsChecked = OptionFlag(drawing.NumericOptions, "MiddlePoint", DrawingParityDefaults.NumericOptions(drawing.ToolId));
        ArrowStartBox.IsChecked = style.ArrowStart;
        ArrowEndBox.IsChecked = style.ArrowEnd;
        PriceLabelsBox.IsChecked = _tool.Category == DrawingToolCategory.FibonacciGann
            ? OptionFlag(drawing.NumericOptions, "ShowLevelPrices", DrawingParityDefaults.NumericOptions(drawing.ToolId), style.ShowPriceLabels)
            : style.ShowPriceLabels;
        TimeLabelsBox.IsChecked = style.ShowTimeLabels;
        StatisticsBox.IsChecked = style.ShowStatistics;
        AngleBox.IsChecked = OptionFlag(drawing.NumericOptions, "AngleLabel", DrawingParityDefaults.NumericOptions(drawing.ToolId));
        AlwaysStatsBox.IsChecked = OptionFlag(drawing.NumericOptions, "AlwaysShowStats", DrawingParityDefaults.NumericOptions(drawing.ToolId));
        IReadOnlyDictionary<string, double> parityDefaults = DrawingParityDefaults.NumericOptions(drawing.ToolId);
        ReverseBox.IsChecked = OptionFlag(drawing.NumericOptions, "Reverse", parityDefaults);
        UseOneColorBox.IsChecked = OptionFlag(drawing.NumericOptions, "UseOneColor", parityDefaults);
        LabelsPercentBox.IsChecked = OptionFlag(drawing.NumericOptions, "LabelsPercent", parityDefaults);
        LabelsLeftBox.IsChecked = OptionFlag(drawing.NumericOptions, "LabelsLeft", parityDefaults);
        ShowLevelReadingsBox.IsChecked = OptionFlag(drawing.NumericOptions, "ShowLevelReadings", parityDefaults);
        BandsBox.IsChecked = OptionFlag(drawing.NumericOptions, "Bands", parityDefaults);
        LabelsOutsideBox.IsChecked = OptionFlag(drawing.NumericOptions, "LabelsOutside", parityDefaults);
        LabelHorizontalBox.SelectedItem = ReadingHorizontalName(OptionNumber(drawing.NumericOptions, "LabelHorizontal", parityDefaults, 1));
        LabelVerticalBox.SelectedItem = ReadingVerticalName(OptionNumber(drawing.NumericOptions, "LabelVertical", parityDefaults, -1));
        FanBox.IsChecked = OptionFlag(drawing.NumericOptions, "Fan", parityDefaults);
        ArcsBox.IsChecked = OptionFlag(drawing.NumericOptions, "Arcs", parityDefaults);
        NameBox.Text = drawing.Name;
        PlaceAsBackgroundBox.IsChecked = drawing.VisualLayer == DrawingVisualLayer.BelowCandles;
        VisualLayerBox.SelectedItem = drawing.VisualLayer;
        SyncModeBox.SelectedItem = drawing.SyncMode;
        DrawingTextBox.Text = drawing.Text;
        FontFamilyBox.Text = style.FontFamily;
        FontSizeBox.Text = style.FontSize.ToString(CultureInfo.InvariantCulture);
        BoldBox.IsChecked = style.Bold;
        ItalicBox.IsChecked = style.Italic;
        HorizontalAlignmentBox.SelectedItem = NormalizeHorizontalAlignment(style.HorizontalTextAlignment);
        VerticalAlignmentBox.SelectedItem = NormalizeVerticalAlignment(style.VerticalTextAlignment);
        SecondsBox.IsChecked = drawing.Visibility.Seconds;
        MinutesBox.IsChecked = drawing.Visibility.Minutes;
        HoursBox.IsChecked = drawing.Visibility.Hours;
        DailyBox.IsChecked = drawing.Visibility.Daily;
        WeeklyBox.IsChecked = drawing.Visibility.Weekly;
        MonthlyBox.IsChecked = drawing.Visibility.Monthly;
        MinimumTimeframeBox.Text = drawing.Visibility.MinimumTimeframe;
        MaximumTimeframeBox.Text = drawing.Visibility.MaximumTimeframe;
        LockedBox.IsChecked = drawing.IsLocked;
        HiddenBox.IsChecked = drawing.IsHidden;
        TemplateNameBox.Text = $"{drawing.DisplayName} template";

        _anchors.Clear();
        for (int i = 0; i < drawing.Anchors.Count; i++)
        {
            DrawingAnchor anchor = drawing.Anchors[i];
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(anchor.StartUnix);
            _anchors.Add(new AnchorRow
            {
                Index = i + 1,
                DateText = timestamp.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                TimeText = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                PriceText = anchor.Price.ToString("0.##########", CultureInfo.InvariantCulture)
            });
        }

        LoadLevels(drawing.Levels.Count > 0 || !_tool.SupportsLevels
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(_tool.Id));
        _options.Clear();
        foreach ((string name, double value) in DrawingParityDefaults.MergeOptions(drawing.ToolId, drawing.NumericOptions).OrderBy(item => item.Key))
            _options.Add(new OptionRow { Name = name, ValueText = value.ToString(CultureInfo.InvariantCulture), Description = DrawingParityDefaults.OptionDescription(name) });
        UpdateColorPreviews();
    }

    private void LoadLevels(IEnumerable<DrawingLevel> levels)
    {
        _levels.Clear();
        foreach (DrawingLevel level in levels)
        {
            var row = new LevelRow
            {
                Enabled = level.Enabled,
                ValueText = level.Value.ToString("0.########", CultureInfo.InvariantCulture),
                Label = level.Label,
                Color = level.Color,
                FillColor = level.Color,
                FillOpacityText = "-1",
                WidthText = level.Width.ToString(CultureInfo.InvariantCulture),
                LineStyleText = level.LineStyle.ToString(),
                ShowPrice = level.ShowPrice,
                ShowValue = level.ShowValue
            };
            row.PropertyChanged += LevelRow_PropertyChanged;
            _levels.Add(row);
        }
    }

    private void LineColorButton_Click(object sender, RoutedEventArgs e) => PickColor(LineColorBox);
    private void FillColorButton_Click(object sender, RoutedEventArgs e) => PickColor(FillColorBox);
    private void TextColorButton_Click(object sender, RoutedEventArgs e) => PickColor(TextColorBox);
    private void BackgroundColorButton_Click(object sender, RoutedEventArgs e) => PickColor(BackgroundColorBox);

    private void PickColor(TextBox target)
    {
        string original = target.Text;
        var picker = new DrawingColorPickerWindow(original) { Owner = this };
        picker.ColorPreviewChanged += color =>
        {
            target.Text = color;
            UpdateColorPreviews();
            ScheduleLivePreview();
        };
        if (picker.ShowDialog() == true)
            target.Text = picker.SelectedColor;
        else
            target.Text = original;
        UpdateColorPreviews();
        ScheduleLivePreview();
    }

    private void UpdateColorPreviews()
    {
        ColorDisplayHelper.ApplyToButton(LineColorButton, LineColorBox.Text);
        ColorDisplayHelper.ApplyToButton(FillColorButton, FillColorBox.Text);
        ColorDisplayHelper.ApplyToButton(TextColorButton, TextColorBox.Text);
        ColorDisplayHelper.ApplyToButton(BackgroundColorButton, BackgroundColorBox.Text);
    }

    private static Brush BrushFrom(string value, string fallback)
    {
        string normalized = NormalizeColor(value, fallback);
        return new SolidColorBrush(TryParseColor(normalized, out Color color) ? color : Colors.Gray);
    }

    private void LevelRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Level value, visibility, price/readings and colours are true live settings.
        // Do not require leaving the grid cell or pressing OK before the chart updates.
        ScheduleLivePreview();
    }

    private void AnySettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPreview)
            return;
        if (ReferenceEquals(e.OriginalSource, OpacityBox) &&
            double.TryParse(OpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity))
        {
            _suppressPreview = true;
            OpacitySlider.Value = Math.Clamp(opacity, 0, 1);
            _suppressPreview = false;
        }
        else if (ReferenceEquals(e.OriginalSource, FillOpacityBox) &&
                 double.TryParse(FillOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fillOpacity))
        {
            _suppressPreview = true;
            FillOpacitySlider.Value = Math.Clamp(fillOpacity, 0, 1);
            FillEnabledBox.IsChecked = fillOpacity > 0.0001;
            _suppressPreview = false;
        }
        UpdateColorPreviews();
        ScheduleLivePreview();
    }

    private void AnySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressPreview)
            ScheduleLivePreview();
    }

    private void AnyToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPreview)
            return;
        if (ReferenceEquals(e.OriginalSource, FillEnabledBox))
        {
            _suppressPreview = true;
            double value = FillEnabledBox.IsChecked == true
                ? Math.Max(0.12, FillOpacitySlider.Value)
                : 0;
            FillOpacitySlider.Value = value;
            FillOpacityBox.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
            _suppressPreview = false;
        }
        ScheduleLivePreview();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPreview)
            return;
        _suppressPreview = true;
        OpacityBox.Text = Math.Clamp(e.NewValue, 0, 1).ToString("0.##", CultureInfo.InvariantCulture);
        _suppressPreview = false;
        ScheduleLivePreview();
    }

    private void FillOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPreview)
            return;
        _suppressPreview = true;
        double value = Math.Clamp(e.NewValue, 0, 1);
        FillOpacityBox.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
        FillEnabledBox.IsChecked = value > 0.0001;
        _suppressPreview = false;
        ScheduleLivePreview();
    }

    private void ScheduleLivePreview()
    {
        if (_suppressPreview)
            return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void EmitLivePreview()
    {
        _previewTimer.Stop();
        if (_suppressPreview)
            return;
        if (TryBuildUpdatedDrawing(showErrors: false, out ChartDrawing updated))
        {
            UpdatedDrawing = updated;
            PreviewChanged?.Invoke(updated);
        }
    }

    private void AddLevelButton_Click(object sender, RoutedEventArgs e)
    {
        var row = new LevelRow { Enabled = true, ValueText = "0.5", Label = "0.5", Color = LineColorBox.Text, FillColor = LineColorBox.Text, FillOpacityText = "-1", WidthText = "1", LineStyleText = "Solid", ShowPrice = true, ShowValue = true };
        row.PropertyChanged += LevelRow_PropertyChanged;
        int index = LevelsGrid.SelectedIndex >= 0 ? LevelsGrid.SelectedIndex + 1 : _levels.Count;
        _levels.Insert(Math.Clamp(index, 0, _levels.Count), row);
        LevelsGrid.SelectedItem = row;
        ScheduleLivePreview();
    }

    private void RemoveLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (LevelsGrid.SelectedItem is LevelRow row)
        {
            _levels.Remove(row);
            ScheduleLivePreview();
        }
    }

    private void AddReadingButton_Click(object sender, RoutedEventArgs e)
    {
        if (LevelsGrid.SelectedItem is not LevelRow row)
        {
            MessageBox.Show(this, "Select a level first.", "Add reading", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // A reading belongs to a level; adding it never changes the level geometry.
        // Turn on the selected row and the master switch so the result is visible immediately.
        row.ShowValue = true;
        ShowLevelReadingsBox.IsChecked = true;
        ScheduleLivePreview();
    }

    private void RemoveReadingButton_Click(object sender, RoutedEventArgs e)
    {
        if (LevelsGrid.SelectedItem is not LevelRow row)
        {
            MessageBox.Show(this, "Select a level first.", "Remove reading", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Remove only the selected level reading. Keep its line, price flag and custom value intact.
        row.ShowValue = false;
        ScheduleLivePreview();
    }

    private void MoveLevelUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedLevel(-1);
    private void MoveLevelDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedLevel(1);

    private void MoveSelectedLevel(int delta)
    {
        if (LevelsGrid.SelectedItem is not LevelRow row)
            return;
        int oldIndex = _levels.IndexOf(row);
        int newIndex = Math.Clamp(oldIndex + delta, 0, _levels.Count - 1);
        if (newIndex == oldIndex)
            return;
        _levels.Move(oldIndex, newIndex);
        LevelsGrid.SelectedItem = row;
        ScheduleLivePreview();
    }

    private void LevelColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (LevelsGrid.SelectedItem is not LevelRow row)
        {
            MessageBox.Show(this, "Select a level first.", "Level colour", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var temporary = new TextBox { Text = row.Color };
        PickColor(temporary);
        row.Color = temporary.Text;
        row.FillColor = row.Color;
        LevelsGrid.Items.Refresh();
        ScheduleLivePreview();
    }

    private void LevelFillColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (LevelsGrid.SelectedItem is not LevelRow row)
        {
            MessageBox.Show(this, "Select a level first.", "Zone fill colour", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var temporary = new TextBox { Text = row.FillColor };
        PickColor(temporary);
        row.FillColor = temporary.Text;
        LevelsGrid.Items.Refresh();
        ScheduleLivePreview();
    }

    private void ResetLevelsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLevels(DrawingParityDefaults.LevelsForTool(_tool.Id));
        ScheduleLivePreview();
    }

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExistingTemplatesBox.SelectedItem is not DrawingTemplate template)
            return;
        ApplyStyle(template.Style);
        LoadLevels(template.Levels);
        _options.Clear();
        foreach ((string name, double value) in DrawingParityDefaults.MergeOptions(_tool.Id, template.NumericOptions).OrderBy(item => item.Key))
            _options.Add(new OptionRow { Name = name, ValueText = value.ToString(CultureInfo.InvariantCulture), Description = DrawingParityDefaults.OptionDescription(name) });
        ApplyReadingMasterOptions(template.NumericOptions, template.Style);
        ScheduleLivePreview();
    }

    private void SetDefaultTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExistingTemplatesBox.SelectedItem is DrawingTemplate template)
        {
            _templateIdToMakeDefault = template.Id;
            MessageBox.Show(this, $"'{template.Name}' will become the default after Apply.", "Default template", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExistingTemplatesBox.SelectedItem is not DrawingTemplate template)
            return;
        if (!_templateIdsToDelete.Contains(template.Id, StringComparer.Ordinal))
            _templateIdsToDelete.Add(template.Id);
        _templates.Remove(template);
        ExistingTemplatesBox.SelectedItem = _templates.FirstOrDefault();
    }

    private void ResetDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressPreview = true;
        LoadDrawing(_original);
        _suppressPreview = false;
        ScheduleLivePreview();
    }

    private void DefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressPreview = true;
        ApplyStyle(DrawingToolCatalog.DefaultStyle(_tool));
        if (_tool.SupportsLevels)
            LoadLevels(DrawingParityDefaults.LevelsForTool(_tool.Id));
        _options.Clear();
        IReadOnlyDictionary<string, double> defaultOptions = DrawingParityDefaults.NumericOptions(_tool.Id);
        foreach ((string name, double value) in defaultOptions.OrderBy(item => item.Key))
            _options.Add(new OptionRow { Name = name, ValueText = value.ToString(CultureInfo.InvariantCulture), Description = DrawingParityDefaults.OptionDescription(name) });
        ApplyReadingMasterOptions(defaultOptions, DrawingToolCatalog.DefaultStyle(_tool));
        _suppressPreview = false;
        ScheduleLivePreview();
    }

    private void ApplyReadingMasterOptions(IReadOnlyDictionary<string, double> options, DrawingStyle style)
    {
        IReadOnlyDictionary<string, double> defaults = DrawingParityDefaults.NumericOptions(_tool.Id);
        ShowLevelReadingsBox.IsChecked = OptionFlag(options, "ShowLevelReadings", defaults, true);
        PriceLabelsBox.IsChecked = OptionFlag(options, "ShowLevelPrices", defaults, style.ShowPriceLabels);
    }

    private void ApplyStyle(DrawingStyle style)
    {
        LineColorBox.Text = style.LineColor; FillColorBox.Text = style.FillColor; TextColorBox.Text = style.TextColor; BackgroundColorBox.Text = style.BackgroundColor;
        LineWidthBox.Text = style.LineWidth.ToString(CultureInfo.InvariantCulture); LineStyleBox.SelectedItem = style.LineStyle;
        OpacityBox.Text = style.Opacity.ToString(CultureInfo.InvariantCulture); FillOpacityBox.Text = style.FillOpacity.ToString(CultureInfo.InvariantCulture);
        OpacitySlider.Value = Math.Clamp(style.Opacity, 0, 1); FillOpacitySlider.Value = Math.Clamp(style.FillOpacity, 0, 1); FillEnabledBox.IsChecked = style.FillOpacity > 0.0001;
        ExtendLeftBox.IsChecked = style.ExtendLeft; ExtendRightBox.IsChecked = style.ExtendRight; MiddleLineBox.IsChecked = style.ShowMiddleLine;
        ArrowStartBox.IsChecked = style.ArrowStart; ArrowEndBox.IsChecked = style.ArrowEnd; PriceLabelsBox.IsChecked = style.ShowPriceLabels;
        TimeLabelsBox.IsChecked = style.ShowTimeLabels; StatisticsBox.IsChecked = style.ShowStatistics;
        FontFamilyBox.Text = style.FontFamily; FontSizeBox.Text = style.FontSize.ToString(CultureInfo.InvariantCulture); BoldBox.IsChecked = style.Bold; ItalicBox.IsChecked = style.Italic;
        HorizontalAlignmentBox.SelectedItem = NormalizeHorizontalAlignment(style.HorizontalTextAlignment);
        VerticalAlignmentBox.SelectedItem = NormalizeVerticalAlignment(style.VerticalTextAlignment);
        UpdateColorPreviews();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        if (!TryBuildUpdatedDrawing(showErrors: true, out ChartDrawing updated))
            return;

        UpdatedDrawing = updated;
        WasAccepted = true;
        Close();
    }

    private bool TryBuildUpdatedDrawing(bool showErrors, out ChartDrawing updated)
    {
        updated = _original;
        if (!TryDouble(LineWidthBox.Text, 0.5, 20, out double lineWidth) ||
            !TryDouble(OpacityBox.Text, 0, 1, out double opacity) ||
            !TryDouble(FillOpacityBox.Text, 0, 1, out double fillOpacity) ||
            !TryDouble(FontSizeBox.Text, 8, 72, out double fontSize))
        {
            if (showErrors)
                MessageBox.Show(this, "Check width, opacity and font-size values.", "Invalid drawing settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var anchors = new List<DrawingAnchor>();
        foreach (AnchorRow row in _anchors)
        {
            if (!DateTimeOffset.TryParseExact($"{row.DateText} {row.TimeText}", "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTimeOffset time) ||
                !double.TryParse(row.PriceText, NumberStyles.Float, CultureInfo.InvariantCulture, out double price))
            {
                if (showErrors)
                    MessageBox.Show(this, $"Anchor {row.Index} has an invalid date, time or price.", "Invalid coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            anchors.Add(new DrawingAnchor(time.ToUnixTimeSeconds(), price));
        }

        var levels = new List<DrawingLevel>();
        foreach (LevelRow row in _levels.Where(item => !string.IsNullOrWhiteSpace(item.ValueText)))
        {
            if (!double.TryParse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.TryParse(row.WidthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
            {
                if (showErrors)
                    MessageBox.Show(this, "One level has an invalid value or width.", "Invalid levels", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            DrawingLineStyle levelStyle = Enum.TryParse(row.LineStyleText, true, out DrawingLineStyle parsed)
                ? parsed
                : DrawingLineStyle.Solid;
            string effectiveLabel = string.IsNullOrWhiteSpace(row.Label) ||
                double.TryParse(row.Label, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? row.ValueText
                : row.Label;
            levels.Add(new DrawingLevel(
                value,
                effectiveLabel,
                row.Enabled,
                NormalizeColor(row.Color, "#94A3B8"),
                Math.Clamp(width, 0.5, 20),
                levelStyle,
                row.ShowPrice,
                row.ShowValue,
                NormalizeColor(row.Color, "#94A3B8"),
                -1));
        }

        var options = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (OptionRow row in _options.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            if (!double.TryParse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                if (showErrors)
                    MessageBox.Show(this, $"Option '{row.Name}' has an invalid value.", "Invalid option", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            options[row.Name.Trim()] = value;
        }

        options["MiddlePoint"] = MiddlePointBox.IsChecked == true ? 1 : 0;
        options["AngleLabel"] = AngleBox.IsChecked == true ? 1 : 0;
        options["AlwaysShowStats"] = AlwaysStatsBox.IsChecked == true ? 1 : 0;
        options["Reverse"] = ReverseBox.IsChecked == true ? 1 : 0;
        options["UseOneColor"] = UseOneColorBox.IsChecked == true ? 1 : 0;
        options["LabelsPercent"] = LabelsPercentBox.IsChecked == true ? 1 : 0;
        options["LabelsLeft"] = LabelHorizontalBox.SelectedItem as string == "Left" ? 1 : 0;
        options["ShowLevelReadings"] = ShowLevelReadingsBox.IsChecked == true ? 1 : 0;
        options["ShowLevelPrices"] = PriceLabelsBox.IsChecked == true ? 1 : 0;
        options["Bands"] = BandsBox.IsChecked == true ? 1 : 0;
        options["LabelsOutside"] = LabelsOutsideBox.IsChecked == true ? 1 : 0;
        options["LabelHorizontal"] = ReadingHorizontalValue(LabelHorizontalBox.SelectedItem as string);
        options["LabelVertical"] = ReadingVerticalValue(LabelVerticalBox.SelectedItem as string);
        options["Fan"] = FanBox.IsChecked == true ? 1 : 0;
        options["Arcs"] = ArcsBox.IsChecked == true ? 1 : 0;

        double effectiveFillOpacity = (_tool.Category == DrawingToolCategory.FibonacciGann
            ? BandsBox.IsChecked == true
            : FillEnabledBox.IsChecked == true) ? fillOpacity : 0;
        DrawingStyle style = _original.Style with
        {
            LineColor = NormalizeColor(LineColorBox.Text, _original.Style.LineColor),
            FillColor = NormalizeColor(FillColorBox.Text, _original.Style.FillColor),
            TextColor = NormalizeColor(TextColorBox.Text, _original.Style.TextColor),
            BackgroundColor = NormalizeColor(BackgroundColorBox.Text, _original.Style.BackgroundColor),
            LineWidth = lineWidth,
            Opacity = opacity,
            FillOpacity = effectiveFillOpacity,
            LineStyle = LineStyleBox.SelectedItem is DrawingLineStyle lineStyle ? lineStyle : _original.Style.LineStyle,
            ExtendLeft = ExtendLeftBox.IsChecked == true,
            ExtendRight = ExtendRightBox.IsChecked == true,
            ShowMiddleLine = MiddleLineBox.IsChecked == true,
            ArrowStart = ArrowStartBox.IsChecked == true,
            ArrowEnd = ArrowEndBox.IsChecked == true,
            ShowPriceLabels = PriceLabelsBox.IsChecked == true,
            ShowTimeLabels = TimeLabelsBox.IsChecked == true,
            ShowStatistics = StatisticsBox.IsChecked == true,
            FontFamily = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? "Segoe UI" : FontFamilyBox.Text.Trim(),
            FontSize = fontSize,
            Bold = BoldBox.IsChecked == true,
            Italic = ItalicBox.IsChecked == true,
            HorizontalTextAlignment = HorizontalAlignmentBox.SelectedItem as string ?? "Center",
            VerticalTextAlignment = VerticalAlignmentBox.SelectedItem as string ?? "Center"
        };

        updated = _original with
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _original.DisplayName : NameBox.Text.Trim(),
            Text = DrawingTextBox.Text,
            Style = style,
            Anchors = anchors,
            Levels = levels,
            NumericOptions = options,
            Visibility = new DrawingVisibility(
                SecondsBox.IsChecked == true,
                MinutesBox.IsChecked == true,
                HoursBox.IsChecked == true,
                DailyBox.IsChecked == true,
                WeeklyBox.IsChecked == true,
                MonthlyBox.IsChecked == true,
                MinimumTimeframeBox.Text.Trim(),
                MaximumTimeframeBox.Text.Trim()),
            VisualLayer = PlaceAsBackgroundBox.IsChecked == true
                ? DrawingVisualLayer.BelowCandles
                : (VisualLayerBox.SelectedItem is DrawingVisualLayer layer && layer != DrawingVisualLayer.BelowCandles
                    ? layer
                    : DrawingVisualLayer.AboveCandles),
            SyncMode = SyncModeBox.SelectedItem is DrawingSyncMode sync ? sync : _original.SyncMode,
            IsLocked = LockedBox.IsChecked == true,
            IsHidden = HiddenBox.IsChecked == true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return true;
    }

    private static double OptionNumber(IReadOnlyDictionary<string, double> current, string name, IReadOnlyDictionary<string, double> defaults, double fallback)
    {
        if (current.TryGetValue(name, out double currentValue)) return currentValue;
        return defaults.TryGetValue(name, out double defaultValue) ? defaultValue : fallback;
    }

    private static string ReadingHorizontalName(double value) => value < -0.5 ? "Left" : value > 0.5 ? "Right" : "Center";
    private static string ReadingVerticalName(double value) => value < -0.5 ? "Above" : value > 0.5 ? "Below" : "Middle";
    private static double ReadingHorizontalValue(string? value) => value == "Left" ? -1 : value == "Right" ? 1 : 0;
    private static double ReadingVerticalValue(string? value) => value == "Above" ? -1 : value == "Below" ? 1 : 0;

    private static bool OptionFlag(IReadOnlyDictionary<string, double> current, string name, IReadOnlyDictionary<string, double> defaults)
    {
        if (current.TryGetValue(name, out double currentValue))
            return currentValue >= 0.5;
        return defaults.TryGetValue(name, out double defaultValue) && defaultValue >= 0.5;
    }

    private static bool OptionFlag(IReadOnlyDictionary<string, double> current, string name, IReadOnlyDictionary<string, double> defaults, bool fallback)
    {
        if (current.TryGetValue(name, out double currentValue))
            return currentValue >= 0.5;
        if (defaults.TryGetValue(name, out double defaultValue))
            return defaultValue >= 0.5;
        return fallback;
    }

    private static string NormalizeHorizontalAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "left" => "Left",
            "right" => "Right",
            _ => "Center"
        };
    }

    private static string NormalizeVerticalAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "top" => "Top",
            "bottom" => "Bottom",
            _ => "Center"
        };
    }

    private static bool TryDouble(string text, double min, double max, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) { value = Math.Clamp(value, min, max); return true; }
        return false;
    }

    private static string NormalizeColor(string input, string fallback)
    {
        string value = (input ?? string.Empty).Trim(); if (!value.StartsWith('#')) value = "#" + value;
        return TryParseColor(value, out _) ? value : fallback;
    }

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
            // Invalid user-entered colour; the caller supplies the fallback.
        }
        color = default;
        return false;
    }

    private void LevelLineColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LevelRow row }) return;
        string original = row.Color;
        var picker = new DrawingColorPickerWindow(original) { Owner = this };
        picker.ColorPreviewChanged += color =>
        {
            row.Color = NormalizeColor(color, original);
            row.FillColor = row.Color;
            LevelsGrid.Items.Refresh();
            ScheduleLivePreview();
        };
        if (picker.ShowDialog() == true)
            row.Color = NormalizeColor(picker.SelectedColor, original);
        else
            row.Color = original;
        row.FillColor = row.Color;
        LevelsGrid.Items.Refresh();
        ScheduleLivePreview();
    }

    private void LevelFillSwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LevelRow row }) return;
        string original = row.FillColor;
        var picker = new DrawingColorPickerWindow(original) { Owner = this };
        picker.ColorPreviewChanged += color =>
        {
            row.FillColor = NormalizeColor(color, original);
            LevelsGrid.Items.Refresh();
            ScheduleLivePreview();
        };
        if (picker.ShowDialog() == true)
            row.FillColor = NormalizeColor(picker.SelectedColor, original);
        else
            row.FillColor = original;
        LevelsGrid.Items.Refresh();
        ScheduleLivePreview();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        WasAccepted = false;
        Close();
    }

    public sealed class AnchorRow { public int Index { get; init; } public string DateText { get; set; } = string.Empty; public string TimeText { get; set; } = string.Empty; public string PriceText { get; set; } = string.Empty; }
    public sealed class LevelRow : INotifyPropertyChanged
    {
        private bool _enabled = true;
        private string _valueText = string.Empty;
        private string _label = string.Empty;
        private string _color = "#94A3B8";
        private string _fillColor = "#334155";
        private string _fillOpacityText = "0.16";
        private string _widthText = "1";
        private string _lineStyleText = "Solid";
        private bool _showPrice = true;
        private bool _showValue = true;

        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
        public string Label { get => _label; set => Set(ref _label, value); }
        public string Color { get => _color; set => Set(ref _color, value); }
        public string FillColor { get => _fillColor; set => Set(ref _fillColor, value); }
        public string FillOpacityText { get => _fillOpacityText; set => Set(ref _fillOpacityText, value); }
        public string WidthText { get => _widthText; set => Set(ref _widthText, value); }
        public string LineStyleText { get => _lineStyleText; set => Set(ref _lineStyleText, value); }
        public bool ShowPrice { get => _showPrice; set => Set(ref _showPrice, value); }
        public bool ShowValue { get => _showValue; set => Set(ref _showValue, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public sealed class OptionRow
    {
        public string Name { get; set; } = string.Empty;
        public string ValueText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
