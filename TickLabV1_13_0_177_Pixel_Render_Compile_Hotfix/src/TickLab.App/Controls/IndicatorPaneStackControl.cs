using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Indicators;
using TickLab.Core.Market;
using TickLab.Core.Scripting;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed class IndicatorPaneStackControl : Grid
{
    private readonly List<PaneState> _panes = new();
    private ChartViewportSnapshot? _viewport;
    private ChartViewportSnapshot? _sourceViewport;
    private double? _crosshairRatio;
    private ChartSettings _chartSettings = ChartSettings.Default;
    private readonly CheckBox _syncToggle;
    private readonly Button _connectButton;
    private readonly TextBlock _sourceLabel;
    private readonly Border _syncHeader;
    private readonly IndicatorTimeScaleControl _timeScale;
    private bool _syncWithPriceChart = true;
    private bool _independentWorkspaceMode;
    private bool _hasConnectedSource = true;
    private int? _restoredVisibleCount;
    private int? _restoredRightOffset;

    public IndicatorPaneStackControl()
    {
        ClipToBounds = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _sourceLabel = new TextBlock
        {
            Text = "Price chart",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed
        };
        _connectButton = HeaderButton("Connect", "Choose the price chart that supplies this indicator workspace");
        _connectButton.Width = 76;
        _connectButton.Visibility = Visibility.Collapsed;
        _connectButton.Click += (_, _) => ConnectSourceRequested?.Invoke();
        _syncToggle = new CheckBox
        {
            Content = "Sync with Price Chart",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            ToolTip = "When on: synchronize horizontal scroll, horizontal zoom, free drag, replay position, crosshair time and the relative vertical zoom/pan gesture. Indicator values keep their own scale."
        };
        _syncToggle.Checked += (_, _) => SetSyncWithPriceChart(true, notify: true);
        _syncToggle.Unchecked += (_, _) => SetSyncWithPriceChart(false, notify: true);

        var syncHeaderGrid = new Grid();
        syncHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        syncHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        syncHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        syncHeaderGrid.Children.Add(_sourceLabel);
        Grid.SetColumn(_connectButton, 1);
        Grid.SetColumn(_syncToggle, 2);
        syncHeaderGrid.Children.Add(_connectButton);
        syncHeaderGrid.Children.Add(_syncToggle);

        _syncHeader = new Border
        {
            Height = 30,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = syncHeaderGrid
        };
        _timeScale = new IndicatorTimeScaleControl
        {
            Height = 24,
            Visibility = Visibility.Collapsed
        };
        ApplyContainerTheme();
    }

    public event Action<TickScriptEntry>? RefreshIndicatorRequested;
    public event Action<TickScriptEntry>? EditIndicatorRequested;
    public event Action<TickScriptEntry>? OpenIndicatorEditorRequested;
    public event Action<TickScriptEntry>? MoveIndicatorToWindowRequested;
    public event Action<TickScriptEntry>? MoveIndicatorToChartRequested;
    public event Action<TickScriptEntry>? RemoveIndicatorRequested;
    public event Action<TickScriptEntry, IndicatorRouteAction>? RouteIndicatorRequested;
    public event Action<BuiltInIndicatorInstance>? RefreshBuiltInIndicatorRequested;
    public event Action<BuiltInIndicatorInstance>? EditBuiltInIndicatorRequested;
    public event Action<BuiltInIndicatorInstance>? MoveBuiltInIndicatorToWindowRequested;
    public event Action<BuiltInIndicatorInstance>? MoveBuiltInIndicatorToChartRequested;
    public event Action<BuiltInIndicatorInstance>? RemoveBuiltInIndicatorRequested;
    public event Action<BuiltInIndicatorInstance, IndicatorRouteAction>? RouteBuiltInIndicatorRequested;
    public event Action<double?>? CrosshairRatioChanged;
    public event Action<int, double>? HorizontalWheelRequested;
    public event Action<int>? HorizontalPanRequested;
    public event Action<bool>? SyncWithPriceChartChanged;
    public event Action? ConnectSourceRequested;

    public Func<string>? PlacementAddressProvider { get; set; }

    public void RequestOpenIndicatorEditor(TickScriptEntry entry) =>
        OpenIndicatorEditorRequested?.Invoke(entry);

    public bool SyncWithPriceChart
    {
        get => _syncWithPriceChart;
        set => SetSyncWithPriceChart(value, notify: false);
    }

    public bool IndependentWorkspaceMode
    {
        get => _independentWorkspaceMode;
        set
        {
            _independentWorkspaceMode = value;
            _connectButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _sourceLabel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value && !_hasConnectedSource)
                SetSyncWithPriceChart(false, notify: false);
            _timeScale.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateConnectionControls();
            RebuildRows();
        }
    }

    public bool HasConnectedSource => _hasConnectedSource;
    public bool HasIndicators => _panes.Count > 0;

    public void SetSourceChart(string? label, bool connected)
    {
        _hasConnectedSource = connected;
        _sourceLabel.Text = connected
            ? string.IsNullOrWhiteSpace(label) ? "Connected price chart" : label
            : "Not connected to a price chart";
        if (!connected)
            SetSyncWithPriceChart(false, notify: false);
        UpdateConnectionControls();
    }

    private void UpdateConnectionControls()
    {
        _connectButton.Content = _hasConnectedSource ? "Change" : "Connect";
        _connectButton.ToolTip = _hasConnectedSource
            ? "Choose a different price chart for this indicator workspace"
            : "Connect this indicator workspace to a price chart";
        _syncToggle.IsEnabled = !_independentWorkspaceMode || _hasConnectedSource;
    }

    public void SetTimeScaleCandles(IReadOnlyList<Candle>? candles)
    {
        _timeScale.Candles = candles ?? Array.Empty<Candle>();
    }

    public ChartViewportState CaptureViewportState()
    {
        int count = GetIndicatorDataCount();
        if (_viewport is not ChartViewportSnapshot viewport || count <= 0)
            return ChartViewportState.Default;
        int rightOffset = Math.Max(0, count - viewport.LastExclusive);
        return new ChartViewportState(viewport.VisibleCount, rightOffset, true, 0, 0);
    }

    public void RestoreViewportState(ChartViewportState? state)
    {
        ChartViewportState value = state ?? ChartViewportState.Default;
        _restoredVisibleCount = Math.Max(1, value.VisibleCount);
        _restoredRightOffset = Math.Max(0, value.RightOffset);
        EnsureLocalViewport();
    }

    public IReadOnlyList<TickScriptEntry> Entries => _panes.Where(item => item.TickScriptEntry is not null).Select(item => item.TickScriptEntry!).ToArray();
    public IReadOnlyList<BuiltInIndicatorInstance> BuiltInEntries => _panes.Where(item => item.BuiltInInstance is not null).Select(item => item.BuiltInInstance!).ToArray();

    public void SetChartSettings(ChartSettings settings)
    {
        _chartSettings = settings ?? ChartSettings.Default;
        _timeScale.Settings = _chartSettings;
        ApplyContainerTheme();
        foreach (PaneState pane in _panes)
        {
            ApplyPaneTheme(pane);
            if (pane.Plot is TickScriptIndicatorPlotControl script) script.ChartSettings = _chartSettings;
            else if (pane.Plot is BuiltInIndicatorPlotControl builtIn) builtIn.ChartSettings = _chartSettings;
        }
    }

    public void AddOrReplace(
        TickScriptEntry entry,
        TickScriptIndicatorResult result,
        TickScriptIndicatorAppearance? appearance = null)
    {
        string key = "script:" + entry.SourcePath;
        PaneState? existing = Find(key);
        if (existing is not null)
        {
            existing.TickScriptEntry = entry;
            existing.TickScriptAppearance = appearance ?? existing.TickScriptAppearance ?? TickScriptIndicatorAppearance.Default;
            if (existing.Plot is TickScriptIndicatorPlotControl plot)
            {
                plot.PlacementAddressProvider = () => PlacementAddressProvider?.Invoke() ?? "Current chart";
                plot.Result = result;
                plot.Appearance = existing.TickScriptAppearance;
            }
            existing.Title.Text = result.Name;
            existing.Title.Foreground = BrushFrom(existing.TickScriptAppearance.LabelColor, Colors.White);
            EnsureLocalViewport();
            return;
        }

        var plotControl = new TickScriptIndicatorPlotControl
        {
            Result = result,
            Viewport = _viewport,
            ChartSettings = _chartSettings,
            Appearance = appearance ?? TickScriptIndicatorAppearance.Default,
            PlacementAddressProvider = () => PlacementAddressProvider?.Invoke() ?? "Current chart",
            MinHeight = 105
        };
        plotControl.SetSharedCrosshairRatio(_crosshairRatio);
        plotControl.CrosshairRatioChanged += HandlePlotCrosshairChanged;
        plotControl.HorizontalWheelRequested += HandlePlotHorizontalWheel;
        plotControl.HorizontalPanRequested += HandlePlotHorizontalPan;
        var pane = CreatePane(key, result.Name, plotControl);
        pane.TickScriptEntry = entry;
        pane.TickScriptAppearance = appearance ?? TickScriptIndicatorAppearance.Default;
        pane.Title.Foreground = BrushFrom(pane.TickScriptAppearance.LabelColor, Colors.White);
        plotControl.RefreshRequested += () => RefreshIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        plotControl.EditRequested += () => EditIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        plotControl.MoveToWindowRequested += () => MoveIndicatorToWindowRequested?.Invoke(pane.TickScriptEntry!);
        plotControl.MoveToChartRequested += () => MoveIndicatorToChartRequested?.Invoke(pane.TickScriptEntry!);
        plotControl.RemoveRequested += () => RemoveIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        pane.Refresh.Click += (_, _) => RefreshIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        pane.Edit.Click += (_, _) => EditIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        pane.Route.Click += (_, _) => ShowRouteMenu(
            pane.Route,
            action => RouteIndicatorRequested?.Invoke(pane.TickScriptEntry!, action));
        pane.Remove.Click += (_, _) => RemoveIndicatorRequested?.Invoke(pane.TickScriptEntry!);
        ConfigureTickScriptPaneContextMenu(pane);
        AddPane(pane);
        EnsureLocalViewport();
    }

    public void AddOrReplace(BuiltInIndicatorInstance instance, BuiltInIndicatorResult result)
    {
        string key = "builtin:" + instance.InstanceId;
        PaneState? existing = Find(key);
        if (existing is not null)
        {
            existing.BuiltInInstance = instance;
            if (existing.Plot is BuiltInIndicatorPlotControl plot)
            {
                plot.PlacementAddressProvider = () => PlacementAddressProvider?.Invoke() ?? "Current chart";
                plot.Result = result;
            }
            existing.Title.Text = result.Name;
            existing.Title.Foreground = BuiltInTitleBrush(result, _chartSettings);
            EnsureLocalViewport();
            return;
        }

        var plotControl = new BuiltInIndicatorPlotControl
        {
            Result = result,
            Viewport = _viewport,
            ChartSettings = _chartSettings,
            AllowManualFixedRangeOverride = _independentWorkspaceMode,
            PlacementAddressProvider = () => PlacementAddressProvider?.Invoke() ?? "Current chart",
            MinHeight = 105
        };
        plotControl.SetSharedCrosshairRatio(_crosshairRatio);
        plotControl.CrosshairRatioChanged += HandlePlotCrosshairChanged;
        plotControl.HorizontalWheelRequested += HandlePlotHorizontalWheel;
        plotControl.HorizontalPanRequested += HandlePlotHorizontalPan;
        var pane = CreatePane(key, result.Name, plotControl);
        pane.BuiltInInstance = instance;
        pane.Title.Foreground = BuiltInTitleBrush(result, _chartSettings);
        plotControl.RefreshRequested += () => RefreshBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        plotControl.EditRequested += () => EditBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        plotControl.MoveToWindowRequested += () => MoveBuiltInIndicatorToWindowRequested?.Invoke(pane.BuiltInInstance!);
        plotControl.MoveToChartRequested += () => MoveBuiltInIndicatorToChartRequested?.Invoke(pane.BuiltInInstance!);
        plotControl.RemoveRequested += () => RemoveBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        pane.Refresh.Click += (_, _) => RefreshBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        pane.Edit.Click += (_, _) => EditBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        pane.Route.Click += (_, _) => ShowRouteMenu(
            pane.Route,
            action => RouteBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!, action));
        pane.Remove.Click += (_, _) => RemoveBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!);
        ConfigureBuiltInPaneContextMenu(pane);
        AddPane(pane);
        EnsureLocalViewport();
    }

    public void UpdateResult(
        TickScriptEntry entry,
        TickScriptIndicatorResult result,
        TickScriptIndicatorAppearance? appearance = null)
    {
        PaneState? pane = Find("script:" + entry.SourcePath);
        if (pane is null) { AddOrReplace(entry, result, appearance); return; }
        pane.TickScriptEntry = entry;
        pane.TickScriptAppearance = appearance ?? pane.TickScriptAppearance ?? TickScriptIndicatorAppearance.Default;
        if (pane.Plot is TickScriptIndicatorPlotControl plot)
        {
            plot.Result = result;
            plot.Appearance = pane.TickScriptAppearance;
        }
        pane.Title.Text = result.Name;
        pane.Title.Foreground = BrushFrom(pane.TickScriptAppearance.LabelColor, Colors.White);
        EnsureLocalViewport();
    }

    public void UpdateResult(BuiltInIndicatorInstance instance, BuiltInIndicatorResult result)
    {
        PaneState? pane = Find("builtin:" + instance.InstanceId);
        if (pane is null) { AddOrReplace(instance, result); return; }
        pane.BuiltInInstance = instance;
        if (pane.Plot is BuiltInIndicatorPlotControl plot) plot.Result = result;
        pane.Title.Text = result.Name;
        pane.Title.Foreground = BuiltInTitleBrush(result, _chartSettings);
        EnsureLocalViewport();
    }

    public void Remove(TickScriptEntry entry) => RemoveByKey("script:" + entry.SourcePath);
    public void Remove(BuiltInIndicatorInstance instance) => RemoveByKey("builtin:" + instance.InstanceId);

    public void SetViewport(ChartViewportSnapshot? viewport)
    {
        _sourceViewport = viewport;
        if (_syncWithPriceChart || _viewport is null)
            ApplyViewport(viewport);
        else
            EnsureLocalViewport();
    }

    public void SetCrosshairRatio(double? ratio)
    {
        if (!_syncWithPriceChart)
            return;
        ApplyCrosshairRatio(ratio);
    }

    public void ApplyLinkedVerticalAction(ChartVerticalSyncAction action)
    {
        if (!_syncWithPriceChart)
            return;

        foreach (PaneState pane in _panes)
        {
            if (pane.Plot is TickScriptIndicatorPlotControl script)
                script.ApplyLinkedVerticalAction(action);
            else if (pane.Plot is BuiltInIndicatorPlotControl builtIn)
                builtIn.ApplyLinkedVerticalAction(action);
        }
    }

    private void ApplyViewport(ChartViewportSnapshot? viewport)
    {
        _viewport = viewport;
        _timeScale.Viewport = viewport;
        foreach (PaneState pane in _panes)
        {
            if (pane.Plot is TickScriptIndicatorPlotControl script) script.Viewport = viewport;
            else if (pane.Plot is BuiltInIndicatorPlotControl builtIn) builtIn.Viewport = viewport;
        }
    }

    private void ApplyCrosshairRatio(double? ratio)
    {
        _crosshairRatio = ratio;
        foreach (PaneState pane in _panes)
        {
            if (pane.Plot is TickScriptIndicatorPlotControl script) script.SetSharedCrosshairRatio(ratio);
            else if (pane.Plot is BuiltInIndicatorPlotControl builtIn) builtIn.SetSharedCrosshairRatio(ratio);
        }
    }

    private void SetSyncWithPriceChart(bool value, bool notify)
    {
        if (_independentWorkspaceMode && !_hasConnectedSource && value)
            value = false;
        if (_syncWithPriceChart == value)
        {
            if (_syncToggle.IsChecked != value)
                _syncToggle.IsChecked = value;
            return;
        }
        _syncWithPriceChart = value;
        if (_syncToggle.IsChecked != value)
            _syncToggle.IsChecked = value;
        if (value)
        {
            ApplyViewport(_sourceViewport);
            ApplyCrosshairRatio(null);
        }
        else
        {
            EnsureLocalViewport();
            ApplyCrosshairRatio(null);
        }
        if (notify)
            SyncWithPriceChartChanged?.Invoke(value);
    }

    private void HandlePlotCrosshairChanged(double? ratio)
    {
        ApplyCrosshairRatio(ratio);
        if (_syncWithPriceChart)
            CrosshairRatioChanged?.Invoke(ratio);
    }

    private void HandlePlotHorizontalWheel(int delta, double ratio)
    {
        if (_syncWithPriceChart)
            HorizontalWheelRequested?.Invoke(delta, ratio);
        else
            ApplyLocalWheel(delta, ratio);
    }

    private void HandlePlotHorizontalPan(int slots)
    {
        if (_syncWithPriceChart)
            HorizontalPanRequested?.Invoke(slots);
        else
            ApplyLocalPan(slots);
    }

    private int GetIndicatorDataCount()
    {
        int count = 0;
        foreach (PaneState pane in _panes)
        {
            if (pane.Plot is TickScriptIndicatorPlotControl script && script.Result is TickScriptIndicatorResult custom)
                count = Math.Max(count, custom.Values.Count);
            else if (pane.Plot is BuiltInIndicatorPlotControl builtIn && builtIn.Result is BuiltInIndicatorResult result)
                count = Math.Max(count, result.Series.Select(series => series.Values.Count + Math.Max(0, series.Shift)).DefaultIfEmpty(0).Max());
        }
        return count;
    }

    private void EnsureLocalViewport()
    {
        if (_syncWithPriceChart)
            return;
        int count = GetIndicatorDataCount();
        if (count <= 0)
            return;
        if (_restoredVisibleCount is int restoredVisible)
        {
            ChartViewportSnapshot restoredBasis = _sourceViewport ?? _viewport ?? new ChartViewportSnapshot(
                0, Math.Min(count, restoredVisible), 0, Math.Min(count, restoredVisible),
                Enumerable.Range(0, Math.Min(count, restoredVisible)).ToArray(), 0, Math.Max(1, ActualWidth - 48),
                Math.Min(count, restoredVisible), 0);
            int visibleFromState = Math.Clamp(restoredVisible, 1, count);
            int rightOffsetFromState = Math.Clamp(_restoredRightOffset ?? 0, 0, Math.Max(0, count - visibleFromState));
            int firstFromState = Math.Max(0, count - visibleFromState - rightOffsetFromState);
            _restoredVisibleCount = null;
            _restoredRightOffset = null;
            ApplyViewport(CreateLocalViewport(firstFromState, visibleFromState, count, restoredBasis));
            return;
        }
        ChartViewportSnapshot basis = _viewport ?? _sourceViewport ?? new ChartViewportSnapshot(
            0, Math.Min(count, 120), 0, Math.Min(count, 120),
            Enumerable.Range(0, Math.Min(count, 120)).ToArray(), 0, Math.Max(1, ActualWidth - 48),
            Math.Min(count, 120), Math.Max(0, count - Math.Min(count, 120)));
        int visible = Math.Clamp(basis.VisibleCount, 1, Math.Max(1, count));
        int first = Math.Clamp(basis.FirstIndex, 0, Math.Max(0, count - visible));
        ApplyViewport(CreateLocalViewport(first, visible, count, basis));
    }

    private void ApplyLocalWheel(int delta, double anchorRatio)
    {
        int count = GetIndicatorDataCount();
        if (delta == 0 || count <= 0)
            return;
        EnsureLocalViewport();
        if (_viewport is not ChartViewportSnapshot current)
            return;

        if (_chartSettings.ScrollWheelMode == ChartScrollWheelMode.Scroll)
        {
            int shift = Math.Max(1, (int)Math.Round(current.VisibleCount * 0.12));
            ApplyLocalPan(delta > 0 ? -shift : shift);
            return;
        }

        int oldVisible = Math.Clamp(current.VisibleCount, 1, count);
        int newVisible = delta > 0
            ? Math.Max(1, (int)Math.Round(oldVisible / 1.14))
            : Math.Min(count, Math.Max(oldVisible + 1, (int)Math.Round(oldVisible * 1.14)));
        double ratio = Math.Clamp(anchorRatio, 0, 1);
        double anchor = current.FirstIndex + ratio * Math.Max(0, oldVisible - 1);
        int first = (int)Math.Round(anchor - ratio * Math.Max(0, newVisible - 1));
        first = Math.Clamp(first, 0, Math.Max(0, count - newVisible));
        ApplyViewport(CreateLocalViewport(first, newVisible, count, current));
    }

    private void ApplyLocalPan(int slots)
    {
        int count = GetIndicatorDataCount();
        if (slots == 0 || count <= 0)
            return;
        EnsureLocalViewport();
        if (_viewport is not ChartViewportSnapshot current)
            return;
        int visible = Math.Clamp(current.VisibleCount, 1, count);
        int first = Math.Clamp(current.FirstIndex - slots, 0, Math.Max(0, count - visible));
        ApplyViewport(CreateLocalViewport(first, visible, count, current));
    }

    private static ChartViewportSnapshot CreateLocalViewport(
        int first,
        int visible,
        int count,
        ChartViewportSnapshot basis)
    {
        visible = Math.Clamp(visible, 1, Math.Max(1, count));
        first = Math.Clamp(first, 0, Math.Max(0, count - visible));
        int last = Math.Min(count, first + visible);
        int actual = Math.Max(1, last - first);
        return new ChartViewportSnapshot(
            first,
            last,
            first,
            actual,
            Enumerable.Range(0, actual).ToArray(),
            basis.PlotLeft,
            basis.PlotWidth,
            actual,
            Math.Max(0, count - last));
    }

    private PaneState CreatePane(string key, string titleText, FrameworkElement plot)
    {
        var title = new TextBlock
        {
            Text = titleText,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var refresh = HeaderButton("↻", "Refresh indicator");
        var edit = HeaderButton("⚙", "Indicator properties");
        var route = HeaderButton("↗", "Connect, copy or move indicator");
        var hide = HeaderButton("—", "Hide/show indicator plot");
        var remove = HeaderButton("×", "Remove indicator");

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 5; i++) headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(title);
        Grid.SetColumn(refresh, 1); Grid.SetColumn(edit, 2); Grid.SetColumn(route, 3); Grid.SetColumn(hide, 4); Grid.SetColumn(remove, 5);
        headerGrid.Children.Add(refresh); headerGrid.Children.Add(edit); headerGrid.Children.Add(route); headerGrid.Children.Add(hide); headerGrid.Children.Add(remove);
        header.Child = headerGrid;
        content.Children.Add(header);
        Grid.SetRow(plot, 1);
        content.Children.Add(plot);

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content,
            MinHeight = 130,
            ClipToBounds = true
        };
        var pane = new PaneState(key, plot, title, header, border, refresh, edit, route, hide, remove);
        hide.Click += (_, _) =>
        {
            plot.Visibility = plot.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            content.RowDefinitions[1].Height = plot.Visibility == Visibility.Visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        };
        ApplyPaneTheme(pane);
        return pane;
    }

    private void ConfigureTickScriptPaneContextMenu(PaneState pane)
    {
        ContextMenu menu = BuildIndicatorContextMenu(
            () => pane.TickScriptEntry?.Name ?? pane.Title.Text,
            () => RefreshIndicatorRequested?.Invoke(pane.TickScriptEntry!),
            () => EditIndicatorRequested?.Invoke(pane.TickScriptEntry!),
            () => MoveIndicatorToWindowRequested?.Invoke(pane.TickScriptEntry!),
            () => MoveIndicatorToChartRequested?.Invoke(pane.TickScriptEntry!),
            () => RemoveIndicatorRequested?.Invoke(pane.TickScriptEntry!));
        AttachPaneHeaderContextMenu(pane, menu);
    }

    private void ConfigureBuiltInPaneContextMenu(PaneState pane)
    {
        ContextMenu menu = BuildIndicatorContextMenu(
            () => pane.BuiltInInstance?.DisplayName ?? pane.Title.Text,
            () => RefreshBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!),
            () => EditBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!),
            () => MoveBuiltInIndicatorToWindowRequested?.Invoke(pane.BuiltInInstance!),
            () => MoveBuiltInIndicatorToChartRequested?.Invoke(pane.BuiltInInstance!),
            () => RemoveBuiltInIndicatorRequested?.Invoke(pane.BuiltInInstance!));
        AttachPaneHeaderContextMenu(pane, menu);
    }

    private static void AttachPaneHeaderContextMenu(PaneState pane, ContextMenu menu)
    {
        pane.Header.ContextMenu = menu;
        pane.Header.PreviewMouseRightButtonDown += (_, e) =>
        {
            menu.PlacementTarget = pane.Header;
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    private ContextMenu BuildIndicatorContextMenu(
        Func<string> name,
        Action refresh,
        Action properties,
        Action moveToWindow,
        Action moveToChart,
        Action remove)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            if (menu.Items.Count > 0 && menu.Items[0] is MenuItem heading)
                heading.Header = $"{name()} — {PlacementAddressProvider?.Invoke() ?? "Current chart"}";
        };
        menu.Items.Add(new MenuItem { IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(ContextAction("Refresh", refresh));
        menu.Items.Add(ContextAction("Properties…", properties));
        menu.Items.Add(ContextAction("Move to Window…", moveToWindow));
        menu.Items.Add(ContextAction("Move to Chart…", moveToChart));
        menu.Items.Add(new Separator());
        menu.Items.Add(ContextAction("Remove", remove));
        return menu;
    }

    private static MenuItem ContextAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void AddPane(PaneState pane)
    {
        CapturePaneWeights();
        _panes.Add(pane);
        pane.HeightWeight = _panes.Count > 1 ? _panes.Take(_panes.Count - 1).Select(item => item.HeightWeight).DefaultIfEmpty(1).Average() : 1;
        RebuildRows();
    }

    private PaneState? Find(string key) => _panes.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    private void RemoveByKey(string key)
    {
        CapturePaneWeights();
        _panes.RemoveAll(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        RebuildRows();
    }

    private void CapturePaneWeights()
    {
        foreach (PaneState pane in _panes)
        {
            if (pane.Container.ActualHeight > 0)
                pane.HeightWeight = Math.Max(1, pane.Container.ActualHeight);
        }
    }

    private void RebuildRows()
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        Grid.SetRow(_syncHeader, 0);
        Children.Add(_syncHeader);
        for (int index = 0; index < _panes.Count; index++)
        {
            if (index > 0)
            {
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(9) });
                GridSplitter splitter = CreatePaneSplitter();
                Grid.SetRow(splitter, RowDefinitions.Count - 1);
                Children.Add(splitter);
            }

            RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(Math.Max(1, _panes[index].HeightWeight), GridUnitType.Star),
                MinHeight = 90
            });
            Grid.SetRow(_panes[index].Container, RowDefinitions.Count - 1);
            Children.Add(_panes[index].Container);
        }
        if (_independentWorkspaceMode)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            Grid.SetRow(_timeScale, RowDefinitions.Count - 1);
            Children.Add(_timeScale);
        }
    }

    private GridSplitter CreatePaneSplitter()
    {
        return new GridSplitter
        {
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = BrushFrom(_chartSettings.GridColor, Color.FromRgb(70, 70, 70)),
            Cursor = Cursors.SizeNS,
            ShowsPreview = false,
            ToolTip = "Drag up or down to resize indicator windows"
        };
    }

    private void ApplyContainerTheme()
    {
        Background = BrushFrom(_chartSettings.ChartBackgroundColor, Color.FromRgb(8, 8, 8));
        Brush headerBackground = Mix(_chartSettings.ChartBackgroundColor, _chartSettings.GridColor, 0.22, Color.FromRgb(20, 20, 20));
        _syncHeader.Background = headerBackground;
        _syncHeader.BorderBrush = BrushFrom(_chartSettings.GridColor, Color.FromRgb(55, 55, 55));
        _syncToggle.Foreground = BrushFrom(_chartSettings.ChartTextColor, Colors.White);
        _sourceLabel.Foreground = BrushFrom(_chartSettings.ChartTextColor, Colors.White);
        _connectButton.Background = headerBackground;
        _connectButton.Foreground = BrushFrom(_chartSettings.ChartTextColor, Colors.White);
        _connectButton.BorderBrush = BrushFrom(_chartSettings.GridColor, Color.FromRgb(55, 55, 55));
    }

    private void ApplyPaneTheme(PaneState pane)
    {
        Brush background = BrushFrom(_chartSettings.ChartBackgroundColor, Color.FromRgb(8, 8, 8));
        Brush headerBackground = Mix(_chartSettings.ChartBackgroundColor, _chartSettings.GridColor, 0.22, Color.FromRgb(20, 20, 20));
        Brush border = BrushFrom(_chartSettings.GridColor, Color.FromRgb(55, 55, 55));
        pane.Container.Background = background;
        pane.Container.BorderBrush = border;
        pane.Header.Background = headerBackground;
        pane.Header.BorderBrush = border;
        pane.Title.Foreground = pane.TickScriptAppearance is not null
            ? BrushFrom(pane.TickScriptAppearance.LabelColor, Colors.White)
            : pane.Plot is BuiltInIndicatorPlotControl builtIn && builtIn.Result is BuiltInIndicatorResult result
                ? BuiltInTitleBrush(result, _chartSettings)
                : BrushFrom(_chartSettings.ChartTextColor, Colors.White);
        foreach (Button button in new[] { pane.Refresh, pane.Edit, pane.Route, pane.Hide, pane.Remove })
        {
            button.Background = headerBackground;
            button.Foreground = BrushFrom(_chartSettings.ChartTextColor, Colors.White);
            button.BorderBrush = border;
        }
    }

    private static void ShowRouteMenu(Button owner, Action<IndicatorRouteAction> route)
    {
        var menu = new ContextMenu();
        foreach (IndicatorRouteAction action in Enum.GetValues<IndicatorRouteAction>())
        {
            var item = new MenuItem { Header = action.ToString() };
            item.Click += (_, _) => route(action);
            menu.Items.Add(item);
        }
        owner.ContextMenu = menu;
        menu.PlacementTarget = owner;
        menu.IsOpen = true;
    }

    private static Button HeaderButton(string text, string tooltip) => new()
    {
        Content = text,
        ToolTip = tooltip,
        Width = 30,
        Height = 24,
        Padding = new Thickness(0),
        Margin = new Thickness(2),
        FocusVisualStyle = null,
        Cursor = Cursors.Hand
    };


    private static SolidColorBrush BuiltInTitleBrush(BuiltInIndicatorResult result, ChartSettings chartSettings)
    {
        string color = result.Series.FirstOrDefault(item => item.Style.Visible)?.Style.LabelColor
            ?? chartSettings.ChartTextColor;
        return BrushFrom(color, Colors.White);
    }
    private static SolidColorBrush BrushFrom(string value, Color fallback)
    {
        try
        {
            object converted = ColorConverter.ConvertFromString(value);
            var brush = new SolidColorBrush(converted is Color color ? color : fallback);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(fallback);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }

    private static SolidColorBrush Mix(string first, string second, double secondWeight, Color fallback)
    {
        Color a = Parse(first, fallback);
        Color b = Parse(second, fallback);
        double w = Math.Clamp(secondWeight, 0, 1);
        return new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(a.R * (1 - w) + b.R * w),
            (byte)Math.Round(a.G * (1 - w) + b.G * w),
            (byte)Math.Round(a.B * (1 - w) + b.B * w)));
    }

    private static Color Parse(string value, Color fallback)
    {
        try { object converted = ColorConverter.ConvertFromString(value); return converted is Color color ? color : fallback; }
        catch { return fallback; }
    }

    private sealed class PaneState
    {
        public PaneState(string key, FrameworkElement plot, TextBlock title, Border header, Border container, Button refresh, Button edit, Button route, Button hide, Button remove)
        {
            Key = key; Plot = plot; Title = title; Header = header; Container = container;
            Refresh = refresh; Edit = edit; Route = route; Hide = hide; Remove = remove;
        }
        public string Key { get; }
        public FrameworkElement Plot { get; }
        public TextBlock Title { get; }
        public Border Header { get; }
        public Border Container { get; }
        public Button Refresh { get; }
        public Button Edit { get; }
        public Button Route { get; }
        public Button Hide { get; }
        public Button Remove { get; }
        public TickScriptEntry? TickScriptEntry { get; set; }
        public TickScriptIndicatorAppearance? TickScriptAppearance { get; set; }
        public BuiltInIndicatorInstance? BuiltInInstance { get; set; }
        public double HeightWeight { get; set; } = 1;
    }
}
