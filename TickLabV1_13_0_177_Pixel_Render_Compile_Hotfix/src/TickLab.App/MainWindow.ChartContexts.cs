using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TickLab.Core.Drawing;
using TickLab.Core.Indicators;
using TickLab.Core.Market;
using TickLab.Core.Scripting;
using TickLab.Core.Settings;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private sealed class ChartRuntimeContext
    {
        public required int PaneId { get; init; }
        public required CandleChartControl Chart { get; init; }
        public required TickChartControl TickChart { get; init; }
        public ChartPaneControl? Host { get; init; }
        public string Symbol { get; set; } = string.Empty;
        public TimeframeDefinition Timeframe { get; set; } =
            TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
        public TimeframeDefinition LastCandleTimeframe { get; set; } =
            TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
        public ChartVisualType LastCandleChartType { get; set; } = ChartVisualType.Candles;
        public ChartSettings Settings { get; set; } = ChartSettings.Default;
        public List<TickScriptEntry> AppliedIndicators { get; } = new();
        public Dictionary<string, TickScriptIndicatorResult> IndicatorResults { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TickScriptIndicatorAppearance> IndicatorAppearances { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<BuiltInIndicatorInstance> BuiltInIndicators { get; } = new();
        public Dictionary<string, BuiltInIndicatorResult> BuiltInIndicatorResults { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public IndicatorPaneStackControl IndicatorStack { get; } = new();
        public List<Candle> SourceCandles { get; set; } = new();
        public List<Candle> DisplayCandles { get; set; } = new();
        public bool AllOlderHistoryLoaded { get; set; }
        public bool AllNewerHistoryLoaded { get; set; } = true;
        public bool SyncIndicatorsWithPriceChart { get; set; } = true;
        public bool EventsWired { get; set; }
        public CancellationTokenSource? OrderFlowLoadCancellation { get; set; }
        public bool IndicatorRefreshRunning { get; set; }
        public bool IndicatorRefreshPending { get; set; }
        public DateTime LastIndicatorRefreshUtc { get; set; } = DateTime.MinValue;
        public bool BuiltInIndicatorRefreshRunning { get; set; }
        public bool BuiltInIndicatorRefreshPending { get; set; }
        public DateTime LastBuiltInIndicatorRefreshUtc { get; set; } = DateTime.MinValue;
        public CancellationTokenSource? OlderHistoryLoadCancellation { get; set; }
        public CancellationTokenSource? NewerHistoryLoadCancellation { get; set; }
        public int OlderHistoryLoadGeneration { get; set; }
        public int NewerHistoryLoadGeneration { get; set; }
        public int IdentityGeneration { get; set; }
        public bool OlderHistoryLoadRunning { get; set; }
        public bool NewerHistoryLoadRunning { get; set; }
        public int CandleRevision { get; set; }
        public bool InitialHistoryLoadRunning { get; set; }
        public DateTime LastIntegrityRepairUtc { get; set; } = DateTime.MinValue;
        public List<MarketTick> TickHistory { get; set; } = new();
        public bool TickAllOlderLoaded { get; set; }
        public bool TickAllNewerLoaded { get; set; } = true;
        public bool TickHistoryLoadRunning { get; set; }
        public bool TickOlderHistoryRequestPending { get; set; }
        public bool TickNewerHistoryRequestPending { get; set; }
        public long LastRawTickMilliseconds { get; set; }
        public DateTime LastRawTickRefreshUtc { get; set; } = DateTime.MinValue;
        // Find Candle navigation anchor. Keep the original historical timestamp
        // stable while the user compares other candle timeframes on this pane.
        public long? HistoricalNavigationAnchorUnix { get; set; }
        public string HistoricalNavigationAnchorSymbol { get; set; } = string.Empty;
    }

    private readonly Dictionary<int, ChartRuntimeContext> _chartContexts = new();
    private int _activePricePaneId = 1;
    private bool _drawingSyncInProgress;

    private ChartRuntimeContext ActiveChartContext => GetChartContext(_activePricePaneId);
    private CandleChartControl CandleChart => ActiveChartContext.Chart;
    private List<TickScriptEntry> _appliedIndicatorEntries => ActiveChartContext.AppliedIndicators;
    private Dictionary<string, TickScriptIndicatorResult> _indicatorResults => ActiveChartContext.IndicatorResults;
    private IndicatorPaneStackControl _indicatorPaneStack => ActiveChartContext.IndicatorStack;

    private void InitializeChartContextSystem()
    {
        RegisterChartContext(1, PrimaryCandleChart, PrimaryTickChart, host: null);
        _activePricePaneId = 1;
    }

    private ChartRuntimeContext GetChartContext(int paneId)
    {
        if (_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? context))
            return context;
        return _chartContexts[1];
    }

    private void RegisterChartContext(int paneId, CandleChartControl chart, TickChartControl tickChart, ChartPaneControl? host)
    {
        if (_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? existing))
        {
            if (!existing.EventsWired)
                WireCandleChartControl(chart);
            return;
        }

        var context = new ChartRuntimeContext
        {
            PaneId = paneId,
            Chart = chart,
            TickChart = tickChart,
            Host = host,
            Symbol = string.IsNullOrWhiteSpace(_requestedSymbol) ? string.Empty : _requestedSymbol,
            Timeframe = _activeTimeframe,
            Settings = EnforceSyntheticSecondsLock(_preferences.Chart, _activeTimeframe)
        };
        chart.Settings = context.Settings;
        chart.DrawingOwnerId = paneId == 1 ? MainChartId : $"main-chart-{paneId}";
        tickChart.Settings = context.Settings;
        context.IndicatorStack.SetChartSettings(context.Settings);
        context.IndicatorStack.PlacementAddressProvider = () => FormatChartIndicatorAddress(context);
        context.IndicatorStack.SyncWithPriceChart = context.SyncIndicatorsWithPriceChart;
        chart.ServerUtcOffsetMinutes = _selectedConnector?.ServerUtcOffsetMinutes ?? 0;
        _chartContexts[paneId] = context;
        if (paneId != 1 && _chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? active) &&
            !string.IsNullOrWhiteSpace(active.Chart.ActiveDrawingToolId))
        {
            chart.SetDrawingTool(active.Chart.ActiveDrawingToolId);
        }
        WireCandleChartControl(chart);
        WireRawTickChartControl(context);
        WireIndicatorStack(context);
        context.EventsWired = true;
        if (_alertDocument.Rules.Count > 0)
            RefreshAlertLines();
        RefreshDemoTradeLines();
    }


    private void UpdateChartContextIdentity(int paneId, string symbol, string timeframeText)
    {
        if (!_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? context))
            return;
        string nextSymbol = symbol ?? string.Empty;
        TimeframeDefinition? timeframe = GetAllTimeframes().FirstOrDefault(item =>
            string.Equals(item.DisplayText, timeframeText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Key, timeframeText, StringComparison.OrdinalIgnoreCase));
        bool identityChanged =
            !string.Equals(context.Symbol, nextSymbol, StringComparison.OrdinalIgnoreCase) ||
            (timeframe is not null && !string.Equals(context.Timeframe.Key, timeframe.Key, StringComparison.OrdinalIgnoreCase));
        if (identityChanged)
            ResetContextHistoryPaging(context);
        context.Symbol = nextSymbol;
        if (timeframe is not null)
        {
            context.Timeframe = timeframe;
            ChartSettings lockedSettings = EnforceSyntheticSecondsLock(context.Settings, timeframe);
            if (lockedSettings != context.Settings)
            {
                context.Settings = lockedSettings;
                context.Chart.Settings = lockedSettings;
                context.TickChart.Settings = lockedSettings;
            }
        }
        if (context.Host is not null)
        {
            context.Host.Symbol = context.Symbol;
            context.Host.Timeframe = context.Timeframe.DisplayText;
        }
        UpdateWorkspacePaneIdentity(paneId, context.Symbol, context.Timeframe.DisplayText);
        RefreshIndicatorWorkspaceSourceLabels(context);
        RefreshAlertLines();
        RefreshDemoTradeLines();
    }
    private void RemoveChartContext(int paneId)
    {
        if (paneId == 1)
            return;
        if (_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? removed))
        {
            removed.OrderFlowLoadCancellation?.Cancel();
            removed.OrderFlowLoadCancellation?.Dispose();
            ResetContextHistoryPaging(removed);
        }
        _chartContexts.Remove(paneId);
        RefreshDemoTradeLines();
        HandlePriceChartRemovedForIndicatorWorkspaces(paneId);
        if (_activePricePaneId == paneId)
            _activePricePaneId = _chartContexts.Keys.OrderBy(id => id).FirstOrDefault(1);
    }


    private static void ResetContextHistoryPaging(ChartRuntimeContext context)
    {
        context.IdentityGeneration++;
        CancellationTokenSource? older = context.OlderHistoryLoadCancellation;
        CancellationTokenSource? newer = context.NewerHistoryLoadCancellation;
        bool olderWasRunning = context.OlderHistoryLoadRunning;
        bool newerWasRunning = context.NewerHistoryLoadRunning;
        context.OlderHistoryLoadCancellation = null;
        context.NewerHistoryLoadCancellation = null;
        context.OlderHistoryLoadRunning = false;
        context.NewerHistoryLoadRunning = false;
        older?.Cancel();
        newer?.Cancel();
        // Active handlers own disposal after their background reads unwind. A
        // non-running source can be released immediately.
        if (!olderWasRunning)
            older?.Dispose();
        if (!newerWasRunning)
            newer?.Dispose();
        context.Chart.CompleteOlderHistoryRequest();
        context.Chart.CompleteNewerHistoryRequest();
        context.TickChart.CompleteOlderHistoryRequest();
        context.TickChart.CompleteNewerHistoryRequest();
        context.TickHistoryLoadRunning = false;
        context.TickOlderHistoryRequestPending = false;
        context.TickNewerHistoryRequestPending = false;
    }

    private void WireCandleChartControl(CandleChartControl chart)
    {
        chart.CandleSelected += CandleChart_CandleSelected;
        chart.CandleUnmarked += CandleChart_CandleUnmarked;
        chart.OlderHistoryRequested += CandleChart_OlderHistoryRequested;
        chart.NewerHistoryRequested += CandleChart_NewerHistoryRequested;
        chart.GoToEarliestRequested += CandleChart_GoToEarliestRequested;
        chart.GoToLatestRequested += CandleChart_GoToLatestRequested;
        chart.MarkerRemoveRequested += RemoveMarker;
        chart.HistoricalNavigationAnchorRemoveRequested += anchorUnix =>
            RemoveHistoricalNavigationAnchor(chart, anchorUnix);
        chart.FindCandleSelectionRemoveRequested += () =>
            RemoveFindCandleSelection(chart);
        chart.ScrollWheelModeChanged += mode => CandleChart_ScrollWheelModeChanged(chart, mode);
        chart.SizeChanged += (_, _) =>
        {
            if (ReferenceEquals(chart, ActiveChartContext.Chart))
                QueueFavoritesTabsPosition();
            if (ReferenceEquals(chart, _quickEditChart) && _quickEditDrawing is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionDrawingQuickEditor));
        };
        chart.ViewportChanged += snapshot =>
        {
            ChartRuntimeContext context = FindChartContext(chart);
            context.IndicatorStack.SetViewport(snapshot);
            SyncIndependentIndicatorWorkspacesViewport(context, snapshot);
            ScheduleOrderFlowRefresh(context);
            ScheduleHistoryProjectionRefresh();
            if (ReferenceEquals(chart, _quickEditChart) && _quickEditDrawing is not null)
                PositionDrawingQuickEditor();
            SaveWorkspace();
        };
        chart.VerticalSyncAction += action =>
        {
            ChartRuntimeContext context = FindChartContext(chart);
            SyncIndependentIndicatorWorkspacesVertical(context, action);
        };
        chart.CrosshairChanged += snapshot =>
        {
            ChartRuntimeContext context = FindChartContext(chart);
            context.IndicatorStack.SetCrosshairRatio(snapshot?.Ratio);
            SyncIndependentIndicatorWorkspacesCrosshair(context, snapshot?.Ratio);
        };
        chart.RefreshRequested += () =>
        {
            ChartRuntimeContext context = FindChartContext(chart);
            ActivateChartControl(chart);
            if (IsReplayChart(context.PaneId))
            {
                StatusText.Text = "End replay before refreshing this chart.";
                return;
            }
            _ = RefreshActiveChartAsync(resetViewport: false);
        };
        chart.InteractiveMarkerMoved += CandleChart_InteractiveMarkerMoved;
        chart.InteractiveMarkerPlacementCompleted += CandleChart_InteractiveMarkerPlacementCompleted;
        chart.InteractiveMarkerRemoveRequested += HandleInteractiveMarkerRemoveRequested;
        chart.DrawingWorkspaceChanged += () => HandleChartDrawingWorkspaceChanged(chart);
        chart.ActiveDrawingToolChanged += _ =>
        {
            if (ReferenceEquals(chart, CandleChart))
                BuildDrawingToolbar();
        };
        chart.DrawingSelectionChanged += drawing =>
        {
            ActivateChartControl(chart);
            _quickEditChart = chart;
            ShowDrawingQuickEditor(drawing);
        };
        chart.MeasurementModeChanged += _ =>
        {
            if (ReferenceEquals(chart, CandleChart))
                UpdateDrawingToolbarState();
        };
        chart.DrawingSettingsRequested += drawing =>
        {
            ActivateChartControl(chart);
            _quickEditChart = chart;
            OpenDrawingSettings(drawing);
        };
        chart.DrawingObjectTreeRequested += () =>
        {
            ActivateChartControl(chart);
            OpenDrawingObjectTree();
        };
        chart.DrawingStatusChanged += message => StatusText.Text = message;
        chart.DrawingAlertRequested += drawing =>
        {
            ActivateChartControl(chart);
            CreateDrawingAlert(FindChartContext(chart), drawing);
        };
        chart.DrawingFavoritesProjectionRequested += visible => SetDrawingFavoritesProjectionVisible(visible);
        chart.ChartSettingsRequested += () => OpenChartSettings(chart);
        chart.SaveChartTemplateRequested += () => SaveChartTemplate(chart);
        chart.LoadChartTemplateRequested += () => LoadChartTemplate(chart);
        chart.DeleteChartTemplateRequested += () => DeleteChartTemplate(chart);
        chart.PriceAlertRequested += price =>
        {
            ActivateChartControl(chart);
            CreatePriceAlert(FindChartContext(chart), price);
        };
        chart.AlertLineMoved += (alertId, price) =>
        {
            ActivateChartControl(chart);
            MoveAlertLine(FindChartContext(chart), alertId, price);
        };
        chart.AlertLineEditRequested += alertId =>
        {
            ActivateChartControl(chart);
            EditAlertById(alertId);
        };
        chart.AlertLineRemoveRequested += alertId =>
        {
            ActivateChartControl(chart);
            RemoveAlertById(alertId);
        };
        chart.DemoTradeLineMoved += (lineId, price) =>
        {
            ActivateChartControl(chart);
            MoveDemoTradeLine(FindChartContext(chart), lineId, price);
        };
        chart.DemoTradeLineContextRequested += line =>
        {
            ActivateChartControl(chart);
            OpenDemoTradeLineContextMenu(FindChartContext(chart), line);
        };
        chart.IndicatorSnapValuesProvider = candleIndex => GetOverlayIndicatorSnapValues(chart, candleIndex);
        chart.IndicatorMenuItemsProvider = () => BuildChartIndicatorMenuEntries(FindChartContext(chart));
        chart.IndicatorAddRequested += () => OpenIndicatorManager(chart, showApplied: false);
        chart.IndicatorManagerRequested += () => OpenIndicatorManager(chart, showApplied: true);
        chart.IndicatorEditRequested += key => EditIndicatorByKey(FindChartContext(chart), key);
        chart.IndicatorRefreshRequested += key => RefreshIndicatorByKey(FindChartContext(chart), key);
        chart.IndicatorMoveToWindowRequested += key => MoveIndicatorToWindowByKey(FindChartContext(chart), key);
        chart.IndicatorMoveToChartRequested += key => MoveIndicatorToChartByKey(FindChartContext(chart), key);
        chart.IndicatorRemoveRequested += key => RemoveIndicatorByKey(FindChartContext(chart), key);
    }

    private void WireIndicatorStack(ChartRuntimeContext context)
    {
        context.IndicatorStack.SyncWithPriceChartChanged += value =>
        {
            context.SyncIndicatorsWithPriceChart = value;
            SaveWorkspace();
        };
        context.IndicatorStack.CrosshairRatioChanged += ratio =>
        {
            context.Chart.SetExternalCrosshairRatio(ratio);
            context.IndicatorStack.SetCrosshairRatio(ratio);
        };
        context.IndicatorStack.RefreshIndicatorRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            RefreshAppliedIndicator(context, entry, force: true);
        };
        context.IndicatorStack.EditIndicatorRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            EditTickScriptIndicatorProperties(context, entry);
        };
        context.IndicatorStack.OpenIndicatorEditorRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            OpenIndicatorInEditor(entry);
        };
        context.IndicatorStack.MoveIndicatorToWindowRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            MoveTickScriptIndicatorToWindow(context, entry);
        };
        context.IndicatorStack.MoveIndicatorToChartRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            RouteTickScriptIndicator(context, entry, IndicatorRouteAction.Move);
        };
        context.IndicatorStack.RemoveIndicatorRequested += entry =>
        {
            ActivateWorkspacePane(context.PaneId);
            RemoveAppliedIndicator(context, entry);
        };
        context.IndicatorStack.RouteIndicatorRequested += (entry, action) =>
        {
            ActivateWorkspacePane(context.PaneId);
            RouteTickScriptIndicator(context, entry, action);
        };
        context.IndicatorStack.RefreshBuiltInIndicatorRequested += instance =>
        {
            ActivateWorkspacePane(context.PaneId);
            RefreshBuiltInIndicatorsForContext(context, force: true);
            StatusText.Text = $"{instance.DisplayName} refreshed.";
        };
        context.IndicatorStack.EditBuiltInIndicatorRequested += instance =>
        {
            ActivateWorkspacePane(context.PaneId);
            EditBuiltInIndicator(context, instance);
        };
        context.IndicatorStack.MoveBuiltInIndicatorToWindowRequested += instance =>
        {
            ActivateWorkspacePane(context.PaneId);
            MoveBuiltInIndicatorToWindow(context, instance);
        };
        context.IndicatorStack.MoveBuiltInIndicatorToChartRequested += instance =>
        {
            ActivateWorkspacePane(context.PaneId);
            RouteBuiltInIndicator(context, instance, IndicatorRouteAction.Move);
        };
        context.IndicatorStack.RemoveBuiltInIndicatorRequested += instance =>
        {
            ActivateWorkspacePane(context.PaneId);
            RemoveBuiltInIndicator(context, instance);
        };
        context.IndicatorStack.RouteBuiltInIndicatorRequested += (instance, action) =>
        {
            ActivateWorkspacePane(context.PaneId);
            RouteBuiltInIndicator(context, instance, action);
        };
        context.IndicatorStack.HorizontalWheelRequested += (delta, ratio) =>
        {
            ActivateWorkspacePane(context.PaneId);
            context.Chart.ApplyLinkedHorizontalWheel(delta, ratio);
        };
        context.IndicatorStack.HorizontalPanRequested += slots =>
        {
            ActivateWorkspacePane(context.PaneId);
            context.Chart.PanHorizontalBySlots(slots);
        };
    }

    private ChartRuntimeContext FindChartContext(CandleChartControl chart) =>
        _chartContexts.Values.FirstOrDefault(item => ReferenceEquals(item.Chart, chart)) ?? _chartContexts[1];

    private void ActivateChartControl(CandleChartControl chart)
    {
        ChartRuntimeContext context = FindChartContext(chart);
        ActivateWorkspacePane(context.PaneId);
    }

    private void ActivateWorkspacePane(int paneId)
    {
        if (!_chartContexts.ContainsKey(paneId))
            return;

        // Clicking or double-clicking the already-active chart must not rebuild
        // its workspace or disturb an in-progress scale interaction.
        if (_activePricePaneId == paneId)
            return;

        SaveActiveChartContext();
        _activePricePaneId = paneId;
        ChartRuntimeContext context = ActiveChartContext;
        context.Chart.ServerUtcOffsetMinutes = _selectedConnector?.ServerUtcOffsetMinutes ?? 0;
        SetRawTickMode(context, context.Timeframe.IsRawTickChart);
        _requestedSymbol = context.Symbol;
        _activeTimeframe = context.Timeframe;
        _sourceTimeframe = context.Timeframe.SourceMt5Code;
        _displayCandles = context.DisplayCandles.Count > 0
            ? context.DisplayCandles.ToList()
            : context.Chart.Candles.ToList();
        _sourceCandles = context.SourceCandles.Count > 0
            ? context.SourceCandles.ToList()
            : _displayCandles.ToList();
        _allOlderHistoryLoaded = context.AllOlderHistoryLoaded;
        _allNewerHistoryLoaded = context.AllNewerHistoryLoaded;

        SetChartIdentityUi();
        BuildTimeframeButtons();
        UpdateChartTypeButton();
        BuildDrawingToolbar();
        UpdateDrawingToolbarState();
        RefreshInlineDrawingObjectTree();
        RefreshInlineDrawingInspector(context.Chart.SelectedDrawing);
        QueueFavoritesTabsPosition();
        ShowIndicatorsForActiveChart();
        RefreshIndicatorsWindowAppliedList();

        // Restored secondary charts do not persist candle arrays in workspace
        // JSON. Load their complete indexed local chart window on first use.
        // The rolling M1/seconds integrity files remain tail-repair inputs only.
        if (_selectedConnector is not null &&
            context.DisplayCandles.Count == 0 &&
            context.Chart.Candles.Count == 0 &&
            !string.IsNullOrWhiteSpace(context.Symbol))
        {
            _ = EnsureActivatedChartHistoryLoadedAsync(context);
        }

        // Selection is intentionally silent. The header and toolbar now follow this chart
        // without adding a visible selection frame or status interruption.
    }

    private async Task EnsureActivatedChartHistoryLoadedAsync(ChartRuntimeContext context)
    {
        if (_isClosing || _selectedConnector is null ||
            !ReferenceEquals(context, ActiveChartContext) ||
            context.InitialHistoryLoadRunning ||
            string.IsNullOrWhiteSpace(context.Symbol))
        {
            return;
        }

        context.InitialHistoryLoadRunning = true;
        int paneId = context.PaneId;
        string symbol = context.Symbol;
        TimeframeDefinition timeframe = context.Timeframe;
        try
        {
            await SafeSelectChartAsync(symbol, timeframe);
            if (_isClosing || !_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? live) ||
                !ReferenceEquals(live, context) || !ReferenceEquals(context, ActiveChartContext))
            {
                return;
            }

            context.SourceCandles = _sourceCandles.ToList();
            context.DisplayCandles = _displayCandles.ToList();
            context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
            context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
            context.CandleRevision++;
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Chart history load paused: {exception.Message}";
        }
        finally
        {
            context.InitialHistoryLoadRunning = false;
        }
    }

    private void SaveActiveChartContext()
    {
        if (!_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            return;
        context.Symbol = _requestedSymbol;
        context.Timeframe = _activeTimeframe;
        context.SourceCandles = _sourceCandles.ToList();
        context.DisplayCandles = _displayCandles.ToList();
        context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
        context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        context.Settings = context.Chart.Settings;
        if (context.Host is not null)
        {
            context.Host.Symbol = context.Symbol;
            context.Host.Timeframe = context.Timeframe.DisplayText;
        }
        UpdateWorkspacePaneIdentity(context.PaneId, context.Symbol, context.Timeframe.DisplayText);
    }

    private async Task SelectTimeframeForActiveChartAsync(TimeframeDefinition timeframe)
    {
        ChartRuntimeContext context = ActiveChartContext;
        bool leavingRawTick = context.Timeframe.IsRawTickChart && !timeframe.IsRawTickChart;
        if (leavingRawTick && context.Settings.ChartType == ChartVisualType.Tick)
        {
            context.Settings = context.Settings with { ChartType = context.LastCandleChartType };
            context.Chart.Settings = context.Settings;
            context.TickChart.Settings = context.Settings;
        }

        ChartSettings timeframeSafeSettings = EnforceSyntheticSecondsLock(context.Settings, timeframe);
        if (timeframeSafeSettings != context.Settings)
        {
            context.Settings = timeframeSafeSettings;
            context.Chart.Settings = timeframeSafeSettings;
            context.TickChart.Settings = timeframeSafeSettings;
            UpdateChartTypeButton();
        }

        if (!timeframe.IsRawTickChart)
            context.LastCandleTimeframe = timeframe;

        string symbol = string.IsNullOrWhiteSpace(context.Symbol) ? _requestedSymbol : context.Symbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            _activeTimeframe = timeframe;
            context.Timeframe = timeframe;
            BuildTimeframeButtons();
            UpdateWorkspacePaneIdentity(context.PaneId, context.Symbol, context.Timeframe.DisplayText);
            return;
        }

        long? historicalAnchor =
            context.HistoricalNavigationAnchorUnix.HasValue &&
            string.Equals(
                context.HistoricalNavigationAnchorSymbol,
                symbol,
                StringComparison.OrdinalIgnoreCase)
                ? context.HistoricalNavigationAnchorUnix
                : null;

        context.Timeframe = timeframe;
        _activeTimeframe = timeframe;
        _requestedSymbol = symbol;
        BuildTimeframeButtons();
        await SafeSelectChartAsync(symbol, timeframe);

        // A Find Candle jump creates a persistent historical navigation anchor.
        // Changing candle timeframe must stay around that same market moment
        // instead of resetting the pane to the latest/live candle.
        if (historicalAnchor.HasValue && timeframe.IsRawTickChart)
        {
            await FindRawTickAsync(new MarkerDraft(
                symbol,
                "Tick",
                historicalAnchor.Value,
                string.Empty,
                historicalAnchor.Value));
        }
        else if (historicalAnchor.HasValue)
        {
            await CenterActiveChartOnHistoricalTimestampAsync(
                context,
                symbol,
                timeframe,
                historicalAnchor.Value,
                "Find Candle timeframe comparison");
        }

        context.Symbol = _requestedSymbol;
        context.Timeframe = _activeTimeframe;
        context.SourceCandles = _sourceCandles.ToList();
        context.DisplayCandles = _displayCandles.ToList();
        context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
        context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        if (context.Host is not null)
        {
            context.Host.Symbol = context.Symbol;
            context.Host.Timeframe = context.Timeframe.DisplayText;
        }
        UpdateWorkspacePaneIdentity(context.PaneId, context.Symbol, context.Timeframe.DisplayText);
        RefreshAlertLines();
        RefreshDemoTradeLines();
        SaveWorkspace();
    }

    private void HandleChartDrawingWorkspaceChanged(CandleChartControl source)
    {
        if (ReferenceEquals(source, _quickEditChart) && _quickEditDrawing is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PositionDrawingQuickEditor));

        ChartRuntimeContext sourceContext = FindChartContext(source);
        if (!_drawingSyncInProgress)
        {
            _drawingSyncInProgress = true;
            try
            {
                IReadOnlyList<ChartDrawing> sourceDrawings = source.ChartDrawings;
                foreach (ChartRuntimeContext target in _chartContexts.Values)
                {
                    if (ReferenceEquals(target.Chart, source))
                        continue;

                    ChartDrawing[] synchronized = sourceDrawings
                        .Where(drawing => string.Equals(drawing.ChartId, source.DrawingOwnerId, StringComparison.Ordinal))
                        .Where(drawing => drawing.SyncMode switch
                        {
                            DrawingSyncMode.CurrentChart => false,
                            DrawingSyncMode.SameSymbol =>
                                !string.IsNullOrWhiteSpace(sourceContext.Symbol) &&
                                string.Equals(target.Symbol, sourceContext.Symbol, StringComparison.OrdinalIgnoreCase),
                            DrawingSyncMode.SameSymbolAndTimeframe =>
                                !string.IsNullOrWhiteSpace(sourceContext.Symbol) &&
                                string.Equals(target.Symbol, sourceContext.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(target.Timeframe.DisplayText, sourceContext.Timeframe.DisplayText, StringComparison.OrdinalIgnoreCase),
                            DrawingSyncMode.CurrentLayout => true,
                            DrawingSyncMode.Global => true,
                            _ => false
                        })
                        .ToArray();

                    target.Chart.ReplaceSynchronizedDrawings(source.DrawingOwnerId, synchronized);
                }
            }
            finally
            {
                _drawingSyncInProgress = false;
            }
        }

        if (ReferenceEquals(source, CandleChart))
        {
            UpdateDrawingToolbarState();
            RefreshDrawingFavoritesWindow();
            RefreshDrawingObjectTree();
            RefreshInlineDrawingInspector(source.SelectedDrawing);
        }
        SaveWorkspace();
    }

    private void SetDrawingToolForAllCharts(string toolId)
    {
        foreach (ChartRuntimeContext context in _chartContexts.Values)
        {
            context.Chart.SetDrawingTool(toolId);
        }
        BuildDrawingToolbar();
        UpdateDrawingToolbarState();
    }

    private static bool HasSeparateIndicatorPane(ChartRuntimeContext context) =>
        context.AppliedIndicators.Count > 0 ||
        context.BuiltInIndicators.Any(instance =>
            BuiltInIndicatorCatalog.Find(instance.Kind)?.Placement == BuiltInIndicatorPlacement.SeparateWindow);

    private void ShowIndicatorsForActiveChart()
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.IndicatorStack.SetChartSettings(context.Settings);
        bool hasPane = HasSeparateIndicatorPane(context);
        if (context.Host is not null)
        {
            if (hasPane)
                context.Host.AttachIndicators(context.IndicatorStack);
            else
                context.Host.HideIndicators();
            return;
        }

        if (!hasPane)
        {
            PrimaryIndicatorHost.Content = null;
            PrimaryIndicatorHost.Visibility = Visibility.Collapsed;
            PrimaryIndicatorSplitter.Visibility = Visibility.Collapsed;
            PrimaryIndicatorSplitterRow.Height = new GridLength(0);
            PrimaryIndicatorRow.Height = new GridLength(0);
            return;
        }

        bool wasHidden = PrimaryIndicatorHost.Visibility != Visibility.Visible || PrimaryIndicatorRow.Height.Value <= 0;
        RemoveElementFromParent(context.IndicatorStack);
        PrimaryIndicatorHost.Content = context.IndicatorStack;
        PrimaryIndicatorHost.Visibility = Visibility.Visible;
        PrimaryIndicatorSplitter.Visibility = Visibility.Visible;
        PrimaryIndicatorSplitterRow.Height = new GridLength(9);
        if (wasHidden)
            PrimaryIndicatorRow.Height = new GridLength(Math.Max(170, Math.Min(280, _lastToolHeight)));
    }
    private static void RemoveElementFromParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case ContentControl content when ReferenceEquals(content.Content, element): content.Content = null; break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element): decorator.Child = null; break;
        }
    }

}
