using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Drawing;
using TickLab.Core.Market;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class DrawingSettingsWindow : Window
{
    private readonly ChartDrawing _original;
    private readonly DrawingToolDefinition _tool;
    private readonly IReadOnlyList<Candle> _candles;
    private readonly ObservableCollection<AnchorRow> _anchors = new();
    private readonly ObservableCollection<LevelRow> _levels = new();
    private readonly ObservableCollection<OptionRow> _options = new();
    private readonly Dictionary<string, double> _hiddenOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<DrawingTemplate> _templates = new();
    private readonly List<string> _templateIdsToDelete = new();
    private string _templateIdToMakeDefault = string.Empty;
    private readonly DispatcherTimer _previewTimer;
    private bool _suppressPreview;

    public DrawingSettingsWindow(ChartDrawing drawing, IReadOnlyList<DrawingTemplate>? templates = null, IReadOnlyList<Candle>? candles = null)
    {
        InitializeComponent();
        _original = drawing;
        _candles = candles ?? Array.Empty<Candle>();
        _tool = DrawingToolCatalog.Find(drawing.ToolId) ?? DrawingToolCatalog.Find("trend-line")!;
        Loaded += (_, _) => ApplicationThemeManager.ApplyToWindow(this);
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _previewTimer.Tick += (_, _) => EmitLivePreview();
        Closed += (_, _) => _previewTimer.Stop();
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(AnySettingChanged));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(AnySelectionChanged));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnyToggleChanged));
        OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
        FillOpacitySlider.ValueChanged += FillOpacitySlider_ValueChanged;
        PositionTargetOpacitySlider.ValueChanged += PositionRoleOpacitySlider_ValueChanged;
        PositionStopOpacitySlider.ValueChanged += PositionRoleOpacitySlider_ValueChanged;
        VpValuesOpacity.ValueChanged += VolumeProfileValuesOpacitySlider_ValueChanged;
        foreach (var role in VolumeProfileStyleRows())
            role.OpacitySlider.ValueChanged += VolumeProfileOpacitySlider_ValueChanged;
        VpPlacementBox.ItemsSource = new[] { "Left", "Right" };
        VpRowsLayoutBox.ItemsSource = new[] { "Number of Rows", "Ticks per Row" };
        VpVolumeModeBox.ItemsSource = new[] { "Up/Down", "Total" };
        bool positionTool = _tool.Geometry == DrawingGeometryKind.Position;
        bool mediaTool = _tool.Geometry is DrawingGeometryKind.Image or DrawingGeometryKind.Icon;
        TextLabelsTab.Visibility = _tool.SupportsText && !positionTool && !mediaTool
            ? Visibility.Visible
            : Visibility.Collapsed;
        bool longShortPosition = _tool.Id is "long-position" or "short-position";
        bool volumeProfileTool = _tool.Id is "fixed-volume-profile" or "anchored-volume-profile";
        LevelsSection.Visibility = _tool.SupportsLevels && !longShortPosition && !volumeProfileTool ? Visibility.Visible : Visibility.Collapsed;
        PositionStyleSection.Visibility = longShortPosition ? Visibility.Visible : Visibility.Collapsed;
        VolumeProfileStyleSection.Visibility = volumeProfileTool ? Visibility.Visible : Visibility.Collapsed;
        OptionsIntroText.Visibility = volumeProfileTool ? Visibility.Collapsed : Visibility.Visible;
        OptionsGrid.Visibility = volumeProfileTool ? Visibility.Collapsed : Visibility.Visible;
        VolumeProfileInputsPanel.Visibility = volumeProfileTool ? Visibility.Visible : Visibility.Collapsed;
        InputsTab.Visibility = DrawingParityDefaults.NumericOptions(_tool.Id).Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CoordinatesTab.Visibility = positionTool || mediaTool ? Visibility.Collapsed : Visibility.Visible;
        bool profileBarCoordinates = volumeProfileTool && _candles.Count > 0;
        CoordinatesIntroText.Visibility = profileBarCoordinates ? Visibility.Collapsed : Visibility.Visible;
        CoordinatesGrid.Visibility = profileBarCoordinates ? Visibility.Collapsed : Visibility.Visible;
        VolumeProfileCoordinatesPanel.Visibility = profileBarCoordinates ? Visibility.Visible : Visibility.Collapsed;
        VpCoordinate2Row.Visibility = _tool.Id == "fixed-volume-profile" ? Visibility.Visible : Visibility.Collapsed;
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
        LineStyleBox.ItemsSource = Enum.GetValues<DrawingLineStyle>();
        VisualLayerBox.ItemsSource = Enum.GetValues<DrawingVisualLayer>();
        SyncModeBox.ItemsSource = Enum.GetValues<DrawingSyncMode>();
        HorizontalAlignmentBox.ItemsSource = new[] { "Left", "Center", "Right" };
        VerticalAlignmentBox.ItemsSource = new[] { "Top", "Center", "Bottom" };
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
        bool positionRoleFill = _tool.Id is "long-position" or "short-position";
        bool volumeProfileTool = _tool.Id is "fixed-volume-profile" or "anchored-volume-profile";
        bool fill = _tool.SupportsFill && !positionRoleFill && !volumeProfileTool;
        bool text = _tool.SupportsText;
        LineLabel.Text = positionRoleFill ? "Lines" : "Line";
        TextColorLabel.Text = positionRoleFill ? "Text" : "Text colour";
        FillColorLabel.Visibility = V(fill);
        FillColorEditor.Visibility = V(fill);
        FillOpacityLabel.Visibility = V(fill);
        FillOpacityEditor.Visibility = V(fill);
        FillEnabledBox.Visibility = V(fill);
        TextColorLabel.Visibility = V(text);
        TextColorEditor.Visibility = V(text);

        string id = _tool.Id;
        bool extend = id is "trend-line" or "arrow" or "ray" or "info-line" or "extended-line" or "trend-angle" or
            "parallel-channel" or "flat-top-bottom" or "disjoint-channel" or "regression-trend" or
            "fib-retracement" or "trend-fib-extension" or "fib-channel";
        bool middleLine = id is "parallel-channel" or "flat-top-bottom" or "disjoint-channel";
        bool middlePoint = id is "trend-line" or "info-line" or "trend-angle" or "ray" or "extended-line";
        bool priceLabels = id is "trend-line" or "info-line" or "trend-angle" or "ray" or "extended-line" or
            "horizontal-line" or "horizontal-ray" or "cross-line" or "fib-retracement" or "trend-fib-extension" or
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

        if (id is "long-position" or "short-position")
        {
            // Position tools have their own TradingView-style Target/Stop controls.
            FillColorLabel.Visibility = Visibility.Collapsed;
            FillColorEditor.Visibility = Visibility.Collapsed;
            FillOpacityLabel.Visibility = Visibility.Collapsed;
            FillOpacityEditor.Visibility = Visibility.Collapsed;
            FillEnabledBox.Visibility = Visibility.Collapsed;
            TextColorLabel.Text = "Text";
            PriceLabelsBox.Visibility = Visibility.Collapsed;
            StatisticsBox.Visibility = Visibility.Collapsed;
            AlwaysStatsBox.Visibility = Visibility.Collapsed;
            LevelsTitleText.Text = "Position colors";
        }
        else if (id == "anchored-vwap")
        {
            LevelsTitleText.Text = "VWAP deviation bands";
            FillColorLabel.Visibility = Visibility.Collapsed;
            FillColorEditor.Visibility = Visibility.Collapsed;
            FillOpacityLabel.Visibility = Visibility.Collapsed;
            FillOpacityEditor.Visibility = Visibility.Collapsed;
            FillEnabledBox.Visibility = Visibility.Collapsed;
        }
        else if (volumeProfileTool)
        {
            FillColorLabel.Visibility = Visibility.Collapsed;
            FillColorEditor.Visibility = Visibility.Collapsed;
            FillOpacityLabel.Visibility = Visibility.Collapsed;
            FillOpacityEditor.Visibility = Visibility.Collapsed;
            FillEnabledBox.Visibility = Visibility.Collapsed;
            LevelsTitleText.Text = "Volume profile style";
        }
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
        PriceLabelsBox.IsChecked = style.ShowPriceLabels;
        TimeLabelsBox.IsChecked = style.ShowTimeLabels;
        StatisticsBox.IsChecked = style.ShowStatistics;
        AngleBox.IsChecked = OptionFlag(drawing.NumericOptions, "AngleLabel", DrawingParityDefaults.NumericOptions(drawing.ToolId));
        AlwaysStatsBox.IsChecked = OptionFlag(drawing.NumericOptions, "AlwaysShowStats", DrawingParityDefaults.NumericOptions(drawing.ToolId));
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

        if (_tool.Id is "fixed-volume-profile" or "anchored-volume-profile")
            LoadLevels(NormalizeVolumeProfileLevels(drawing));
        else
            LoadLevels(drawing.Levels.Count > 0 || !_tool.SupportsLevels
                ? drawing.Levels
                : DrawingParityDefaults.LevelsForTool(_tool.Id));
        _options.Clear();
        _hiddenOptions.Clear();
        bool volumeProfileTool = _tool.Id is "fixed-volume-profile" or "anchored-volume-profile";
        string[] profileInputKeys = { "RowsLayout", "RowSize", "VolumeMode", "ValueAreaPercent", "ExtendRight" };
        foreach ((string name, double value) in DrawingParityDefaults.MergeOptions(drawing.ToolId, drawing.NumericOptions).OrderBy(item => item.Key))
        {
            if (volumeProfileTool && !profileInputKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _hiddenOptions[name] = value;
                continue;
            }
            _options.Add(new OptionRow
            {
                Key = name,
                Name = volumeProfileTool ? VolumeProfileInputDisplayName(name) : name,
                ValueText = value.ToString(CultureInfo.InvariantCulture),
                Description = DrawingParityDefaults.OptionDescription(name)
            });
        }
        LoadPositionStyleControls(drawing);
        LoadVolumeProfileCoordinateControls(drawing);
        LoadVolumeProfileInputsControls(drawing);
        LoadVolumeProfileStyleControls(drawing);
        UpdateColorPreviews();
    }

    private void LoadLevels(IEnumerable<DrawingLevel> levels)
    {
        _levels.Clear();
        foreach (DrawingLevel level in levels)
        {
            _levels.Add(new LevelRow
            {
                Enabled = level.Enabled,
                ValueText = level.Value.ToString("0.########", CultureInfo.InvariantCulture),
                Label = level.Label,
                Color = level.Color,
                FillColor = string.IsNullOrWhiteSpace(level.FillColor) ? level.Color : level.FillColor,
                FillOpacityText = (level.FillOpacity >= 0 ? level.FillOpacity : 0.16).ToString("0.##", CultureInfo.InvariantCulture),
                WidthText = level.Width.ToString(CultureInfo.InvariantCulture),
                LineStyleText = level.LineStyle.ToString(),
                ShowPrice = level.ShowPrice,
                ShowValue = level.ShowValue
            });
        }
    }

    private void LoadPositionStyleControls(ChartDrawing drawing)
    {
        if (_tool.Id != "long-position" && _tool.Id != "short-position")
            return;

        LevelRow? target = FindPositionLevel("Target", "Profit", 0);
        LevelRow? stop = FindPositionLevel("Stop", "Loss", 1);
        PositionTargetColorBox.Text = target?.FillColor ?? "#089981";
        PositionStopColorBox.Text = stop?.FillColor ?? "#F23645";
        double targetOpacity = TryLevelOpacity(target, 0.24);
        double stopOpacity = TryLevelOpacity(stop, 0.24);
        PositionTargetOpacitySlider.Value = targetOpacity;
        PositionStopOpacitySlider.Value = stopOpacity;
        PositionTargetOpacityBox.Text = targetOpacity.ToString("0.##", CultureInfo.InvariantCulture);
        PositionStopOpacityBox.Text = stopOpacity.ToString("0.##", CultureInfo.InvariantCulture);
        PositionFontSizeBox.Text = drawing.Style.FontSize.ToString(CultureInfo.InvariantCulture);
        PositionPriceLabelsBox.IsChecked = drawing.Style.ShowPriceLabels || OptionValue("PriceLabels", 1) >= 0.5;
        PositionCompactStatsBox.IsChecked = OptionValue("CompactStats", 0) >= 0.5;
        PositionStatsModeBox.ItemsSource = new[] { "Full stats", "Compact stats", "Hidden" };
        int statsMode = (int)Math.Clamp(Math.Round(OptionValue("StatsMode", 0)), 0, 2);
        PositionStatsModeBox.SelectedIndex = statsMode;
    }

    private LevelRow? FindPositionLevel(string preferred, string legacy, int fallbackIndex)
    {
        LevelRow? row = _levels.FirstOrDefault(item => string.Equals(item.Label, preferred, StringComparison.OrdinalIgnoreCase))
            ?? _levels.FirstOrDefault(item => string.Equals(item.Label, legacy, StringComparison.OrdinalIgnoreCase));
        return row ?? (fallbackIndex >= 0 && fallbackIndex < _levels.Count ? _levels[fallbackIndex] : null);
    }

    private static double TryLevelOpacity(LevelRow? row, double fallback) =>
        row is not null && double.TryParse(row.FillOpacityText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, 0, 1)
            : fallback;

    private double OptionValue(string name, double fallback)
    {
        OptionRow? row = _options.FirstOrDefault(item => string.Equals(OptionKey(item), name, StringComparison.OrdinalIgnoreCase));
        if (row is not null && double.TryParse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return value;
        return _hiddenOptions.TryGetValue(name, out double hidden) ? hidden : fallback;
    }

    private void ApplyPositionStyleControlsToRows()
    {
        if (_tool.Id != "long-position" && _tool.Id != "short-position")
            return;

        LevelRow? target = FindPositionLevel("Target", "Profit", 0);
        LevelRow? stop = FindPositionLevel("Stop", "Loss", 1);
        if (target is not null)
        {
            target.Label = "Target";
            target.Color = PositionTargetColorBox.Text;
            target.FillColor = PositionTargetColorBox.Text;
            target.FillOpacityText = PositionTargetOpacityBox.Text;
        }
        if (stop is not null)
        {
            stop.Label = "Stop";
            stop.Color = PositionStopColorBox.Text;
            stop.FillColor = PositionStopColorBox.Text;
            stop.FillOpacityText = PositionStopOpacityBox.Text;
        }

        SetOptionValue("PriceLabels", PositionPriceLabelsBox.IsChecked == true ? 1 : 0);
        SetOptionValue("CompactStats", PositionCompactStatsBox.IsChecked == true ? 1 : 0);
        SetOptionValue("StatsMode", Math.Clamp(PositionStatsModeBox.SelectedIndex, 0, 2));
    }

    private void SetOptionValue(string name, double value)
    {
        bool profileTool = _tool.Id is "fixed-volume-profile" or "anchored-volume-profile";
        bool profileInput = name is "RowsLayout" or "RowSize" or "VolumeMode" or "ValueAreaPercent" or "ExtendRight";
        if (profileTool && !profileInput)
        {
            _hiddenOptions[name] = value;
            return;
        }
        OptionRow? row = _options.FirstOrDefault(item => string.Equals(OptionKey(item), name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            _options.Add(new OptionRow { Key = name, Name = name, ValueText = value.ToString(CultureInfo.InvariantCulture), Description = DrawingParityDefaults.OptionDescription(name) });
        else
            row.ValueText = value.ToString(CultureInfo.InvariantCulture);
    }

    private static string OptionKey(OptionRow row) => string.IsNullOrWhiteSpace(row.Key) ? row.Name.Trim() : row.Key.Trim();

    private int FindNearestProfileCandleIndex(long unix)
    {
        if (_candles.Count == 0) return -1;
        int lo = 0, hi = _candles.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            long value = _candles[mid].StartUnix;
            if (value == unix) return mid;
            if (value < unix) lo = mid + 1;
            else hi = mid - 1;
        }
        int right = Math.Clamp(lo, 0, _candles.Count - 1);
        int left = Math.Clamp(lo - 1, 0, _candles.Count - 1);
        return Math.Abs(_candles[left].StartUnix - unix) <= Math.Abs(_candles[right].StartUnix - unix) ? left : right;
    }

    private void LoadVolumeProfileCoordinateControls(ChartDrawing drawing)
    {
        if ((_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile") || _candles.Count == 0 || drawing.Anchors.Count == 0)
            return;
        int first = FindNearestProfileCandleIndex(drawing.Anchors[0].StartUnix);
        VpCoordinate1Box.Text = (first + 1).ToString(CultureInfo.InvariantCulture);
        if (_tool.Id == "fixed-volume-profile" && drawing.Anchors.Count > 1)
        {
            int second = FindNearestProfileCandleIndex(drawing.Anchors[1].StartUnix);
            VpCoordinate2Box.Text = (second + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private bool ApplyVolumeProfileCoordinateControls(bool showErrors)
    {
        if ((_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile") || _candles.Count == 0)
            return true;
        if (!int.TryParse(VpCoordinate1Box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int firstBar) || firstBar < 1 || firstBar > _candles.Count)
        {
            if (showErrors) MessageBox.Show(this, $"#1 bar must be between 1 and {_candles.Count}.", "Invalid volume profile coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_anchors.Count > 0)
        {
            DateTimeOffset first = DateTimeOffset.FromUnixTimeSeconds(_candles[firstBar - 1].StartUnix);
            _anchors[0].DateText = first.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            _anchors[0].TimeText = first.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (_tool.Id == "fixed-volume-profile")
        {
            if (!int.TryParse(VpCoordinate2Box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondBar) || secondBar < 1 || secondBar > _candles.Count)
            {
                if (showErrors) MessageBox.Show(this, $"#2 bar must be between 1 and {_candles.Count}.", "Invalid volume profile coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_anchors.Count > 1)
            {
                DateTimeOffset second = DateTimeOffset.FromUnixTimeSeconds(_candles[secondBar - 1].StartUnix);
                _anchors[1].DateText = second.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
                _anchors[1].TimeText = second.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }
        return true;
    }

    private static string VolumeProfileInputDisplayName(string key) => key switch
    {
        "RowsLayout" => "Rows Layout",
        "RowSize" => "Row Size",
        "VolumeMode" => "Volume",
        "ValueAreaPercent" => "Value Area Volume",
        "ExtendRight" => "Extend Right",
        _ => key
    };

    private IReadOnlyList<DrawingLevel> NormalizeVolumeProfileLevels(ChartDrawing drawing)
    {
        IReadOnlyList<DrawingLevel> defaults = DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        if (drawing.Levels.Count == 0)
            return defaults;

        DrawingLevel? Legacy(string role, int index)
        {
            DrawingLevel? exact = drawing.Levels.FirstOrDefault(level => string.Equals(level.Label, role, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            if (role is "Value Area Up" or "Value Area Down" or "VAH" or "VAL")
                return drawing.Levels.FirstOrDefault(level => level.Label.Contains("Value area", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < drawing.Levels.Count)
                return drawing.Levels[index];
            return null;
        }

        var result = new List<DrawingLevel>(defaults.Count);
        foreach (DrawingLevel fallback in defaults)
        {
            int legacyIndex = fallback.Label switch
            {
                "Up Volume" => 0,
                "Down Volume" => 1,
                "POC" => 2,
                "Value Area Up" or "Value Area Down" or "VAH" or "VAL" => 3,
                _ => -1
            };
            DrawingLevel? existing = Legacy(fallback.Label, legacyIndex);
            if (existing is null)
            {
                result.Add(fallback);
                continue;
            }
            string fillColor = string.IsNullOrWhiteSpace(existing.FillColor) ? fallback.FillColor : existing.FillColor;
            double fillOpacity = existing.FillOpacity >= 0 ? existing.FillOpacity : fallback.FillOpacity;
            result.Add(fallback with
            {
                Enabled = fallback.Label is "Developing POC" or "Developing VA" or "Histogram Box" ? fallback.Enabled : existing.Enabled,
                Color = string.IsNullOrWhiteSpace(existing.Color) ? fallback.Color : existing.Color,
                FillColor = string.IsNullOrWhiteSpace(fillColor) ? fallback.FillColor : fillColor,
                FillOpacity = fillOpacity,
                Width = existing.Width > 0 ? existing.Width : fallback.Width,
                LineStyle = existing.LineStyle
            });
        }
        return result;
    }

    private IEnumerable<(string Role, CheckBox EnabledBox, Button ColorButton, Slider OpacitySlider, TextBox OpacityBox, bool FillRole)> VolumeProfileStyleRows()
    {
        yield return ("Up Volume", VpUpEnabled, VpUpColor, VpUpOpacity, VpUpOpacityBox, true);
        yield return ("Down Volume", VpDownEnabled, VpDownColor, VpDownOpacity, VpDownOpacityBox, true);
        yield return ("Value Area Up", VpValueUpEnabled, VpValueUpColor, VpValueUpOpacity, VpValueUpOpacityBox, true);
        yield return ("Value Area Down", VpValueDownEnabled, VpValueDownColor, VpValueDownOpacity, VpValueDownOpacityBox, true);
        yield return ("VAH", VpVahEnabled, VpVahColor, VpVahOpacity, VpVahOpacityBox, false);
        yield return ("VAL", VpValEnabled, VpValColor, VpValOpacity, VpValOpacityBox, false);
        yield return ("POC", VpPocEnabled, VpPocColor, VpPocOpacity, VpPocOpacityBox, false);
        yield return ("Developing POC", VpDevelopingPocEnabled, VpDevelopingPocColor, VpDevelopingPocOpacity, VpDevelopingPocOpacityBox, false);
        yield return ("Developing VA", VpDevelopingVaEnabled, VpDevelopingVaColor, VpDevelopingVaOpacity, VpDevelopingVaOpacityBox, false);
        yield return ("Histogram Box", VpHistogramEnabled, VpHistogramColor, VpHistogramOpacity, VpHistogramOpacityBox, false);
    }

    private LevelRow? FindVolumeProfileLevel(string role) =>
        _levels.FirstOrDefault(item => string.Equals(item.Label, role, StringComparison.OrdinalIgnoreCase));

    private void LoadVolumeProfileInputsControls(ChartDrawing drawing)
    {
        if (_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile")
            return;
        IReadOnlyDictionary<string, double> merged = DrawingParityDefaults.MergeOptions(drawing.ToolId, drawing.NumericOptions);
        double Read(string key, double fallback) => merged.TryGetValue(key, out double value) ? value : fallback;
        VpRowsLayoutBox.SelectedIndex = Read("RowsLayout", 0) >= 0.5 ? 1 : 0;
        VpRowSizeBox.Text = Read("RowSize", Read("Rows", 24)).ToString("0.##", CultureInfo.InvariantCulture);
        VpVolumeModeBox.SelectedIndex = Read("VolumeMode", Read("UpDownVolume", 1) >= 0.5 ? 0 : 1) >= 0.5 ? 1 : 0;
        VpValueAreaBox.Text = Read("ValueAreaPercent", 70).ToString("0.##", CultureInfo.InvariantCulture);
        VpExtendRightBox.IsChecked = Read("ExtendRight", 0) >= 0.5;
    }

    private bool ApplyVolumeProfileInputsControls(bool showErrors)
    {
        if (_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile")
            return true;

        if (!TryDouble(VpRowSizeBox.Text, 1, 10000, out double rowSize) ||
            !TryDouble(VpValueAreaBox.Text, 1, 100, out double valueArea))
        {
            if (showErrors)
                MessageBox.Show(this, "Check Row Size and Value Area Volume. Value Area must be 1–100%.", "Invalid volume profile inputs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        int rowsLayout = Math.Clamp(VpRowsLayoutBox.SelectedIndex, 0, 1);
        int volumeMode = Math.Clamp(VpVolumeModeBox.SelectedIndex, 0, 1);
        SetOptionValue("RowsLayout", rowsLayout);
        SetOptionValue("RowSize", rowSize);
        SetOptionValue("Rows", rowSize); // legacy workspace compatibility
        SetOptionValue("VolumeMode", volumeMode);
        SetOptionValue("UpDownVolume", volumeMode == 0 ? 1 : 0);
        SetOptionValue("ValueAreaPercent", valueArea);
        SetOptionValue("ExtendRight", VpExtendRightBox.IsChecked == true ? 1 : 0);
        return true;
    }

    private void LoadVolumeProfileStyleControls(ChartDrawing drawing)
    {
        if (_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile")
            return;
        IReadOnlyDictionary<string, double> merged = DrawingParityDefaults.MergeOptions(drawing.ToolId, drawing.NumericOptions);
        double Read(string key, double fallback) => merged.TryGetValue(key, out double value) ? value : fallback;
        VpShowProfileBox.IsChecked = Read("ShowProfile", 1) >= 0.5;
        VpShowValuesBox.IsChecked = Read("ShowValues", 0) >= 0.5;
        double valuesOpacity = Math.Clamp(Read("ValuesOpacity", 0.92), 0, 1);
        VpValuesOpacity.Value = valuesOpacity;
        VpValuesOpacityBox.Text = valuesOpacity.ToString("0.##", CultureInfo.InvariantCulture);
        VpWidthBox.Text = Read("WidthPercent", 30).ToString("0.##", CultureInfo.InvariantCulture);
        VpPlacementBox.SelectedIndex = Read("Placement", _tool.Id == "anchored-volume-profile" ? 1 : -1) < 0 ? 0 : 1;
        foreach (var role in VolumeProfileStyleRows())
        {
            LevelRow? level = FindVolumeProfileLevel(role.Role);
            role.EnabledBox.IsChecked = level?.Enabled ?? false;
            double opacity = TryLevelOpacity(level, role.FillRole ? 0.72 : 1.0);
            role.OpacitySlider.Value = opacity;
            role.OpacityBox.Text = opacity.ToString("0.##", CultureInfo.InvariantCulture);
        }
        VpVahEnabled.IsChecked = Read("ShowVAH", VpVahEnabled.IsChecked == true ? 1 : 0) >= 0.5;
        VpValEnabled.IsChecked = Read("ShowVAL", VpValEnabled.IsChecked == true ? 1 : 0) >= 0.5;
        VpPocEnabled.IsChecked = Read("ShowPOC", VpPocEnabled.IsChecked == true ? 1 : 0) >= 0.5;
        VpDevelopingPocEnabled.IsChecked = Read("ShowDevelopingPOC", 0) >= 0.5;
        VpDevelopingVaEnabled.IsChecked = Read("ShowDevelopingVA", 0) >= 0.5;
        VpHistogramEnabled.IsChecked = Read("ShowHistogramBox", 0) >= 0.5;
    }

    private void ApplyVolumeProfileStyleControlsToRows()
    {
        if (_tool.Id != "fixed-volume-profile" && _tool.Id != "anchored-volume-profile")
            return;
        foreach (var role in VolumeProfileStyleRows())
        {
            LevelRow? level = FindVolumeProfileLevel(role.Role);
            if (level is null) continue;
            level.Enabled = role.EnabledBox.IsChecked == true;
            level.FillOpacityText = role.OpacityBox.Text;
        }
        SetOptionValue("ShowProfile", VpShowProfileBox.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowValues", VpShowValuesBox.IsChecked == true ? 1 : 0);
        SetOptionValue("ValuesOpacity", double.TryParse(VpValuesOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double valuesOpacity) ? Math.Clamp(valuesOpacity, 0, 1) : 0.92);
        SetOptionValue("WidthPercent", double.TryParse(VpWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ? width : 30);
        SetOptionValue("Placement", VpPlacementBox.SelectedIndex == 0 ? -1 : 1);
        SetOptionValue("ShowVAH", VpVahEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowVAL", VpValEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowPOC", VpPocEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowDevelopingPOC", VpDevelopingPocEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowDevelopingVA", VpDevelopingVaEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowHistogramBox", VpHistogramEnabled.IsChecked == true ? 1 : 0);
        SetOptionValue("ShowValueArea", (VpValueUpEnabled.IsChecked == true || VpValueDownEnabled.IsChecked == true) ? 1 : 0);
        SetOptionValue("UpDownVolume", OptionValue("VolumeMode", 0) < 0.5 ? 1 : 0);
    }

    private void VolumeProfileColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        // Do not resolve profile colours through Button.Tag + LINQ First().  Routed/theme
        // states can leave Tag with a value that does not exactly match a role and First()
        // then throws InvalidOperationException before the colour picker is even shown.
        // Resolve by the actual button instance instead: each style swatch has one stable
        // button identity for the lifetime of this settings window.
        var matched = VolumeProfileStyleRows().FirstOrDefault(item => ReferenceEquals(item.ColorButton, button));
        if (matched.ColorButton is null || string.IsNullOrWhiteSpace(matched.Role))
            return;

        LevelRow? level = FindVolumeProfileLevel(matched.Role);
        if (level is null)
            return;

        string original = NormalizeColor(matched.FillRole ? level.FillColor : level.Color, "#94A3B8");
        OpenVolumeProfileColorPicker(button, original, color =>
        {
            string normalized = NormalizeColor(color, original);
            if (matched.FillRole)
            {
                level.FillColor = normalized;
                level.Color = normalized;
            }
            else
            {
                level.Color = normalized;
                level.FillColor = normalized;
            }
            ColorDisplayHelper.ApplyToButton(button, normalized);
            ScheduleLivePreview();
        });
    }

    private void OpenVolumeProfileColorPicker(Button previewButton, string original, Action<string> apply)
    {
        bool TryOpen(bool attachOwner)
        {
            DrawingColorPickerWindow? picker = null;
            try
            {
                picker = new DrawingColorPickerWindow(original);
                if (attachOwner && IsLoaded && IsVisible)
                    picker.Owner = this;

                picker.ColorPreviewChanged += apply;
                bool accepted = picker.ShowDialog() == true;
                apply(accepted ? picker.SelectedColor : original);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Some WPF owner/modal states can reject ShowDialog.  The caller retries
                // once without an owner rather than allowing a colour click to suspend TickLab.
                return false;
            }
            finally
            {
                if (picker is not null)
                    picker.ColorPreviewChanged -= apply;
            }
        }

        if (TryOpen(attachOwner: true) || TryOpen(attachOwner: false))
            return;

        // Last-resort safety: preserve the original colour and keep the settings/chart usable.
        apply(original);
        ColorDisplayHelper.ApplyToButton(previewButton, original);
    }

    private void VolumeProfileOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPreview) return;
        foreach (var role in VolumeProfileStyleRows())
        {
            if (!ReferenceEquals(sender, role.OpacitySlider)) continue;
            _suppressPreview = true;
            role.OpacityBox.Text = role.OpacitySlider.Value.ToString("0.##", CultureInfo.InvariantCulture);
            _suppressPreview = false;
            ScheduleLivePreview();
            break;
        }
    }

    private void VolumeProfileValuesColorButton_Click(object sender, RoutedEventArgs e)
    {
        string original = NormalizeColor(TextColorBox.Text, "#FFFFFF");
        OpenVolumeProfileColorPicker(VpValuesColor, original, color =>
        {
            string normalized = NormalizeColor(color, original);
            TextColorBox.Text = normalized;
            ColorDisplayHelper.ApplyToButton(VpValuesColor, normalized);
            ScheduleLivePreview();
        });
    }

    private void VolumeProfileValuesOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPreview) return;
        _suppressPreview = true;
        VpValuesOpacityBox.Text = Math.Clamp(e.NewValue, 0, 1).ToString("0.##", CultureInfo.InvariantCulture);
        _suppressPreview = false;
        ScheduleLivePreview();
    }

    private void LineColorButton_Click(object sender, RoutedEventArgs e) => PickColor(LineColorBox);
    private void FillColorButton_Click(object sender, RoutedEventArgs e) => PickColor(FillColorBox);
    private void TextColorButton_Click(object sender, RoutedEventArgs e) => PickColor(TextColorBox);
    private void BackgroundColorButton_Click(object sender, RoutedEventArgs e) => PickColor(BackgroundColorBox);
    private void PositionTargetColorButton_Click(object sender, RoutedEventArgs e) => PickColor(PositionTargetColorBox);
    private void PositionStopColorButton_Click(object sender, RoutedEventArgs e) => PickColor(PositionStopColorBox);

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
        ColorDisplayHelper.ApplyToButton(PositionTargetColorButton, PositionTargetColorBox.Text);
        ColorDisplayHelper.ApplyToButton(PositionStopColorButton, PositionStopColorBox.Text);
        ColorDisplayHelper.ApplyToButton(VpValuesColor, TextColorBox.Text);
        foreach (var role in VolumeProfileStyleRows())
        {
            LevelRow? level = FindVolumeProfileLevel(role.Role);
            if (level is null) continue;
            ColorDisplayHelper.ApplyToButton(role.ColorButton, role.FillRole ? level.FillColor : level.Color);
        }
    }

    private static Brush BrushFrom(string value, string fallback)
    {
        string normalized = NormalizeColor(value, fallback);
        return new SolidColorBrush(TryParseColor(normalized, out Color color) ? color : Colors.Gray);
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
        else if (ReferenceEquals(e.OriginalSource, PositionTargetOpacityBox) &&
                 double.TryParse(PositionTargetOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double targetOpacity))
        {
            _suppressPreview = true;
            PositionTargetOpacitySlider.Value = Math.Clamp(targetOpacity, 0, 1);
            _suppressPreview = false;
        }
        else if (ReferenceEquals(e.OriginalSource, PositionStopOpacityBox) &&
                 double.TryParse(PositionStopOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double stopOpacity))
        {
            _suppressPreview = true;
            PositionStopOpacitySlider.Value = Math.Clamp(stopOpacity, 0, 1);
            _suppressPreview = false;
        }
        else if (ReferenceEquals(e.OriginalSource, VpValuesOpacityBox) &&
                 double.TryParse(VpValuesOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double valuesOpacity))
        {
            _suppressPreview = true;
            VpValuesOpacity.Value = Math.Clamp(valuesOpacity, 0, 1);
            _suppressPreview = false;
        }
        else
        {
            foreach (var role in VolumeProfileStyleRows())
            {
                if (!ReferenceEquals(e.OriginalSource, role.OpacityBox) ||
                    !double.TryParse(role.OpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double profileOpacity))
                    continue;
                _suppressPreview = true;
                role.OpacitySlider.Value = Math.Clamp(profileOpacity, 0, 1);
                _suppressPreview = false;
                break;
            }
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

    private void PositionRoleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPreview)
            return;
        _suppressPreview = true;
        if (ReferenceEquals(sender, PositionTargetOpacitySlider))
            PositionTargetOpacityBox.Text = Math.Clamp(e.NewValue, 0, 1).ToString("0.##", CultureInfo.InvariantCulture);
        else if (ReferenceEquals(sender, PositionStopOpacitySlider))
            PositionStopOpacityBox.Text = Math.Clamp(e.NewValue, 0, 1).ToString("0.##", CultureInfo.InvariantCulture);
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
        var row = new LevelRow { Enabled = true, ValueText = "0.5", Label = "0.5", Color = LineColorBox.Text, WidthText = "1", LineStyleText = "Solid", ShowPrice = true, ShowValue = true };
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
            _options.Add(new OptionRow { Key = name, Name = name, ValueText = value.ToString(CultureInfo.InvariantCulture), Description = DrawingParityDefaults.OptionDescription(name) });
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
        _hiddenOptions.Clear();
        bool volumeProfileTool = _tool.Id is "fixed-volume-profile" or "anchored-volume-profile";
        string[] profileInputKeys = { "RowsLayout", "RowSize", "VolumeMode", "ValueAreaPercent", "ExtendRight" };
        IReadOnlyDictionary<string, double> defaults = DrawingParityDefaults.NumericOptions(_tool.Id);
        foreach ((string name, double value) in defaults.OrderBy(item => item.Key))
        {
            if (volumeProfileTool && !profileInputKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _hiddenOptions[name] = value;
                continue;
            }
            _options.Add(new OptionRow
            {
                Key = name,
                Name = volumeProfileTool ? VolumeProfileInputDisplayName(name) : name,
                ValueText = value.ToString(CultureInfo.InvariantCulture),
                Description = DrawingParityDefaults.OptionDescription(name)
            });
        }

        if (volumeProfileTool)
        {
            ChartDrawing defaultDrawing = _original with
            {
                Style = DrawingToolCatalog.DefaultStyle(_tool),
                Levels = DrawingParityDefaults.LevelsForTool(_tool.Id),
                NumericOptions = defaults
            };
            LoadVolumeProfileInputsControls(defaultDrawing);
            LoadVolumeProfileStyleControls(defaultDrawing);
        }
        _suppressPreview = false;
        ScheduleLivePreview();
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

        if (!ApplyVolumeProfileCoordinateControls(showErrors))
            return false;

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

        if (_tool.Id is "long-position" or "short-position")
        {
            if (!TryDouble(PositionTargetOpacityBox.Text, 0, 1, out _) ||
                !TryDouble(PositionStopOpacityBox.Text, 0, 1, out _) ||
                !TryDouble(PositionFontSizeBox.Text, 8, 72, out double positionFontSize))
            {
                if (showErrors)
                    MessageBox.Show(this, "Check Target/Stop transparency and position text-size values.", "Invalid position settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            fontSize = positionFontSize;
            ApplyPositionStyleControlsToRows();
        }

        if (_tool.Id is "fixed-volume-profile" or "anchored-volume-profile")
        {
            if (!ApplyVolumeProfileInputsControls(showErrors))
                return false;
            if (!TryDouble(VpWidthBox.Text, 5, 95, out _) ||
                !TryDouble(VpValuesOpacityBox.Text, 0, 1, out _))
            {
                if (showErrors)
                    MessageBox.Show(this, "Volume-profile width must be 5–95%, and Values transparency must be 0–1.", "Invalid volume profile settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            foreach (var role in VolumeProfileStyleRows())
            {
                if (TryDouble(role.OpacityBox.Text, 0, 1, out _))
                    continue;
                if (showErrors)
                    MessageBox.Show(this, $"{role.Role} transparency must be between 0 and 1.", "Invalid volume profile settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            ApplyVolumeProfileStyleControlsToRows();
        }

        var levels = new List<DrawingLevel>();
        foreach (LevelRow row in _levels.Where(item => !string.IsNullOrWhiteSpace(item.ValueText)))
        {
            if (!double.TryParse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.TryParse(row.WidthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
                !double.TryParse(row.FillOpacityText, NumberStyles.Float, CultureInfo.InvariantCulture, out double levelFillOpacity))
            {
                if (showErrors)
                    MessageBox.Show(this, "One level has an invalid value or width.", "Invalid levels", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            DrawingLineStyle levelStyle = Enum.TryParse(row.LineStyleText, true, out DrawingLineStyle parsed)
                ? parsed
                : DrawingLineStyle.Solid;
            levels.Add(new DrawingLevel(
                value,
                string.IsNullOrWhiteSpace(row.Label) ? row.ValueText : row.Label,
                row.Enabled,
                NormalizeColor(row.Color, "#94A3B8"),
                Math.Clamp(width, 0.5, 20),
                levelStyle,
                row.ShowPrice,
                row.ShowValue,
                NormalizeColor(row.FillColor, NormalizeColor(row.Color, "#94A3B8")),
                Math.Clamp(levelFillOpacity, 0, 1)));
        }

        var options = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (OptionRow row in _options.Where(item => !string.IsNullOrWhiteSpace(OptionKey(item))))
        {
            if (!double.TryParse(row.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                if (showErrors)
                    MessageBox.Show(this, $"Option '{row.Name}' has an invalid value.", "Invalid option", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            options[OptionKey(row)] = value;
        }

        foreach ((string key, double value) in _hiddenOptions)
            options[key] = value;

        options["MiddlePoint"] = MiddlePointBox.IsChecked == true ? 1 : 0;
        options["AngleLabel"] = AngleBox.IsChecked == true ? 1 : 0;
        options["AlwaysShowStats"] = AlwaysStatsBox.IsChecked == true ? 1 : 0;

        double effectiveFillOpacity = FillEnabledBox.IsChecked == true ? fillOpacity : 0;
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
            ShowPriceLabels = _tool.Id is "long-position" or "short-position" ? PositionPriceLabelsBox.IsChecked == true : PriceLabelsBox.IsChecked == true,
            ShowTimeLabels = TimeLabelsBox.IsChecked == true,
            ShowStatistics = _tool.Id is "long-position" or "short-position" ? PositionStatsModeBox.SelectedIndex != 2 : StatisticsBox.IsChecked == true,
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

    private static bool OptionFlag(IReadOnlyDictionary<string, double> current, string name, IReadOnlyDictionary<string, double> defaults)
    {
        if (current.TryGetValue(name, out double currentValue))
            return currentValue >= 0.5;
        return defaults.TryGetValue(name, out double defaultValue) && defaultValue >= 0.5;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        WasAccepted = false;
        Close();
    }

    public sealed class AnchorRow { public int Index { get; init; } public string DateText { get; set; } = string.Empty; public string TimeText { get; set; } = string.Empty; public string PriceText { get; set; } = string.Empty; }
    public sealed class LevelRow
    {
        public bool Enabled { get; set; } = true;
        public string ValueText { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = "#94A3B8";
        public string FillColor { get; set; } = "#334155";
        public string FillOpacityText { get; set; } = "0.16";
        public string WidthText { get; set; } = "1";
        public string LineStyleText { get; set; } = "Solid";
        public bool ShowPrice { get; set; } = true;
        public bool ShowValue { get; set; } = true;
    }
    public sealed class OptionRow
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ValueText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
