using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Diagnostics;
using TickLab.Core.Drawing;
using TickLab.Core.History;
using TickLab.Core.Market;
using TickLab.Core.Settings;
using TickLab.Core.Scripting;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Core;
using TickLab.Desktop.Settings;
using TickLab.Desktop.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop;

public partial class MainWindow : Window
{
    private const string MainChartId = "main-chart-1";
    private const int MaximumChartBufferRecords = 8_000;
    private const int ChartWindowMaximumRecords = 12_000;
    private const int ChartWindowPageRecords = 1_600;
    // 1-second history uses a deeper read-ahead buffer while preserving the
    // normal visual-page size. One backward request prepares three pages, so
    // manual history scrolling does not stop every ~1,600 seconds to redraw.
    private const int SecondChartHistoryPageRecords = ChartWindowPageRecords * 3;
    private const int ChartWindowSearchMultiplier = 3;
    private const double HistoryImportWorkShare = 0.82;
    private const int M1ChartLaunchPreviewRecords = 2_400;
    private const int MinuteChartLaunchPreviewRecords = 800;
    // Second-based candles use the same virtual-page discipline as normal
    // candle charts: one bounded page around the 1,500-candle visual ceiling,
    // then older/newer pages are streamed on demand. Never bootstrap thousands
    // of seconds from the raw-tick archive into the visual chart at once.
    private const int SecondChartLaunchPreviewRecords = ChartWindowPageRecords;
    private const int HigherChartLaunchPreviewRecords = 600;
    private static readonly TimeSpan ChartLaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TickArchiveFinalizeTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ConnectorOfflineGrace =
        TimeSpan.FromSeconds(12);

    private readonly Mt5FileBridgeClient _bridgeClient = new();
    private readonly PersistentHistoryStore _historyStore = new();
    private readonly TemporaryHistoryStore _temporaryHistoryStore = new();
    private readonly ExternalHistoryStore _externalHistoryStore = new();
    private readonly DataRangeCoordinator _dataRangeCoordinator = new();
    private readonly SettingsStore _settingsStore = new();
    private MarkerExchangeService? _markerExchange;
    private readonly DispatcherTimer _liveTimer;
    private readonly DispatcherTimer _maintenanceTimer;
    private readonly DispatcherTimer _workspaceSaveTimer;
    private readonly DispatcherTimer _markerTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<TimeframeDefinition> _customTimeframes = new();
    private readonly object _pendingHistoryWriteSync = new();
    private readonly Dictionary<string, PendingHistoryWrite> _pendingHistoryWrites =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _automaticHistoryRequests =
        new(StringComparer.Ordinal);
    private readonly object _automaticHistorySync = new();

    private UserPreferences _preferences = new();
    private Mt5ConnectorSummary? _selectedConnector;
    private IReadOnlyList<Mt5SymbolInfo> _availableSymbols = Array.Empty<Mt5SymbolInfo>();
    private readonly HashSet<string> _marketFavouriteSymbols = new(StringComparer.OrdinalIgnoreCase);
    private string _marketFilter = "All";
    private bool _marketFilterDragging;
    private Point _marketFilterDragStart;
    private double _marketFilterDragStartOffset;
    private static readonly string MarketFavouriteFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TickLab",
        "symbol-favourites.json");
    private List<Candle> _sourceCandles = new();
    private List<Candle> _displayCandles = new();
    private TimeframeDefinition _activeTimeframe =
        TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
    private HistoryLoadSelection _historySelection;
    private string _requestedSymbol = string.Empty;
    private string _sourceTimeframe = "PERIOD_M1";
    private int _selectionGeneration;
    private CancellationTokenSource? _selectionCancellation;
    private DateTime _lastLiveWriteUtc = DateTime.MinValue;
    private DateTime _lastChartBootstrapWriteUtc = DateTime.MinValue;
    private DateTime _lastClosedWriteUtc = DateTime.MinValue;
    private DateTime _lastSymbolsWriteUtc = DateTime.MinValue;
    private DateTime _lastTickArchiveWriteUtc = DateTime.MinValue;
    private DateTime _lastRecentSecondsWriteUtc = DateTime.MinValue;
    private DateTime _lastLiveSecondWriteUtc = DateTime.MinValue;
    private DateTime _lastClosedSecondWriteUtc = DateTime.MinValue;
    private DateTime _lastMultiChartLiveSecondWriteUtc = DateTime.MinValue;
    private DateTime _lastMultiChartClosedSecondWriteUtc = DateTime.MinValue;
    private DateTime _lastMultiChartLiveNativeWriteUtc = DateTime.MinValue;
    private DateTime _lastMultiChartClosedNativeWriteUtc = DateTime.MinValue;
    private bool _liveRefreshRunning;
    private bool _maintenanceRunning;
    private bool _isClosing;
    private Candle? _selectedCandle;
    private bool _historyFlushRunning;
    private bool _historyOperationRunning;
    private bool _liveIntegrityRunning;
    private DateTime _lastTickIntegrityWriteUtc = DateTime.MinValue;
    private DateTime _lastNativeClosedIntegrityWriteUtc = DateTime.MinValue;
    private DateTime _lastLiveIntegrityCheckUtc = DateTime.MinValue;
    private bool _goToEarliestLoadRunning;
    private bool _goToLatestLoadRunning;
    private bool _allOlderHistoryLoaded;
    private bool _allNewerHistoryLoaded = true;
    private bool _activeInstrumentSaving;
    private bool _startupRefreshQueued;
    private bool _bridgeWasAvailable;
    private DateTime _lastHealthyConnectorObservationUtc = DateTime.MinValue;
    private int _consecutiveConnectorFailures;
    private DateTime _lastAutoConnectAttemptUtc = DateTime.MinValue;
    private DetachedToolWindow? _detachedToolWindow;
    private DetachedChartWindow? _detachedHostedToolWindow;
    private readonly List<DetachedChartWindow> _detachedChartWindows = new();
    private DetachedChartWindow? _activeDetachedChartWindow;
    private HistoryImportProgressWindow? _historyProgressWindow;
    private CandleMarkerWindow? _markerWindow;
    private bool _findCandleInProgress;
    private TickScriptEditorWindow? _scriptEditorWindow;
    private IndicatorsWindow? _indicatorsWindow;
    private DrawingObjectTreeWindow? _drawingObjectTreeWindow;
    private Window? _activeDrawingSettingsWindow;
    private DrawingFavoritesWindow? _drawingFavoritesWindow;
    private TimeframeFavoritesWindow? _timeframeFavoritesWindow;
    private readonly List<string> _favoriteTimeframeKeys = new();
    private string _inlineFavoriteDragId = string.Empty;
    private Point _inlineFavoriteDragStart;
    private bool _drawingToolbarCollapsed;
    // Keeps each toolbox folder on the last tool chosen from that folder.
    // RecentDrawingToolIds seeds this after restart; this cache makes the behavior
    // deterministic for every category during the current session.
    private readonly Dictionary<DrawingToolCategory, string> _lastDrawingToolByCategory = new();
    private DrawingToolCategory? _openDrawingCategory;
    private double _drawingPaletteExpandedWidth = 260.0;
    private bool _rightWorkspaceCollapsed = true;
    private double _rightWorkspaceExpandedWidth = 310.0;
    private bool _rightWorkspaceHandleDragging;
    private bool _rightWorkspaceHandleMoved;
    private double _rightWorkspaceHandleStartX;
    private double _rightWorkspaceHandleStartWidth;
    private ChartDrawing? _quickEditDrawing;
    private CandleChartControl? _quickEditChart;
    private ChartDrawing? _quickEditOriginal;
    private ChartDrawing? _quickEditPreview;
    private bool _suppressQuickEdit;
    private bool _quickEditBarDragging;
    private bool _quickEditBarManualPosition;
    private Point _quickEditBarDragStart;
    private Thickness _quickEditBarDragStartMargin;
    private string _quickEditBarDrawingId = string.Empty;
    private readonly DispatcherTimer _quickEditCommitTimer;
    private DateTime _lastIndicatorRefreshUtc = DateTime.MinValue;
    private CancellationTokenSource? _chartNavigationCancellation;
    private int _chartNavigationGeneration;
    private string _activeHistoryRequestId = string.Empty;
    private bool _historyRestartAllRequested;
    private bool _historyChartLaunchRunning;
    private bool _pendingChartLaunchRetryRunning;
    private string _historyOperationChartTimeframe = string.Empty;
    private string _lastHistoryPauseErrorSignature = string.Empty;
    private TickLabErrorContext? _activeHistoryErrorContext;
    private readonly Dictionary<string, PendingChartLaunch> _pendingChartLaunches =
        new(StringComparer.Ordinal);
    private double _historyOverallBasePercent;
    private double _historyOverallScalePercent = 100;
    private double _lastToolHeight = 340;
    private readonly List<CandleMarker> _candleMarkers = new();
    private bool _receiveMarkers;
    private bool _markerPollRunning;
    private bool _markerExchangeInitialized;
    private bool _workspaceDirty = true;
    private bool _workspaceSaveRunning;
    private string _lastWorkspaceSaveErrorSignature = string.Empty;
    private bool _ownedDrawingWindowsReady;
    private bool _favoritesOpenDeferred;


    public MainWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) => QueueFavoritesTabsPosition();
        SizeChanged += (_, _) => QueueFavoritesTabsPosition();
        StateChanged += (_, _) => QueueFavoritesTabsPosition();
        LoadMarketFavourites();
        BuildMarketFilterButtons();
        InitializeChartContextSystem();
        InitializeInlineCodeEditor();

        _quickEditCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _quickEditCommitTimer.Tick += (_, _) => CommitQuickDrawingEdit();

        _preferences = _settingsStore.Load();
        _bridgeClient.SetManualBridgeFolder(
            string.IsNullOrWhiteSpace(_preferences.BridgeFolderOverride)
                ? null
                : _preferences.BridgeFolderOverride);
        _historySelection = new HistoryLoadSelection(
            _preferences.HistoryDisplayMode,
            _preferences.SelectedHistorySegments);

        LoadSavedTimeframes();
        _favoriteTimeframeKeys.Clear();
        foreach (string key in _preferences.FavoriteTimeframeKeys ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(key) && !_favoriteTimeframeKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                _favoriteTimeframeKeys.Add(key);
        }
        RemoveMissingTimeframeFavorites();
        RestoreActiveTimeframe();
        ApplicationThemeManager.Apply(_preferences.ApplicationTheme);
        ApplyTopToolbarIcons();
        ChartRuntimeContext startupContext = GetChartContext(1);
        startupContext.Settings = _preferences.Chart;
        ApplyChartSettings(startupContext.Settings);
        if (_preferences.DrawingDocuments.Count > 0)
            CandleChart.ImportDrawingWorkspaceJson(_preferences.DrawingDocuments[0]);
        _receiveMarkers = _preferences.ReceiveMarkers;
        _drawingToolbarCollapsed = _preferences.DrawingToolbarCollapsed;
        BuildTimeframeButtons();
        UpdateChartTypeButton();
        BuildDrawingToolbar();
        InitializeWorkspaceSystem();
        InitializeAlertsAndReplay();
        InitializeDemoTrading();
        // Start the main PANEL collapsed on every application launch, matching
        // the Code Editor and Demo Trading handle-only startup behavior.
        _rightWorkspaceCollapsed = true;
        ApplyRightWorkspaceCollapsedState();
        RefreshChartWindowDock();

        RefreshInlineDrawingFavorites();
        RefreshInlineDrawingObjectTree();

        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _liveTimer.Tick += LiveTimer_Tick;

        _maintenanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _maintenanceTimer.Tick += MaintenanceTimer_Tick;

        _workspaceSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _workspaceSaveTimer.Tick += WorkspaceSaveTimer_Tick;

        _markerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _markerTimer.Tick += async (_, _) => await PollIncomingMarkersAsync();

        BridgePathText.Text = _historyStore.RootPath;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowBounds();
        ApplyDrawingToolbarCollapsedState();
        _ownedDrawingWindowsReady = true;
        RestoreDrawingWorkspaceWindows();
        RestoreTimeframeFavoritesWindow();
        RestoreWorkspaceWindowsAfterLoad();
        RefreshChartWindowDock();
        InitializeMarkerExchangeSafely();
        _liveTimer.Start();
        _maintenanceTimer.Start();
        _workspaceSaveTimer.Start();
        if (_markerExchangeInitialized)
            _markerTimer.Start();
        await TryReconnectLastConnectorAsync();
        RestoreAppliedIndicators();
        RestoreAppliedBuiltInIndicators();
    }

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DrawingCategoryPaletteBorder.Visibility != Visibility.Visible ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // Keep the palette open only while the user works inside it or
        // drags its divider. Any click elsewhere closes it immediately. A
        // category-button click may then open the newly requested folder.
        if (IsWithinVisualTree(source, DrawingCategoryPaletteBorder) ||
            IsWithinVisualTree(source, DrawingPaletteSplitter))
        {
            return;
        }

        CloseDrawingCategoryPalette();
    }

    private static bool IsWithinVisualTree(DependencyObject? source, DependencyObject container)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, container))
                return true;

            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                current = VisualTreeHelper.GetParent(current);
            else if (current is FrameworkContentElement contentElement)
                current = contentElement.Parent;
            else
                current = LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox)
            return;

        Key drawingHotKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            string? drawingToolId = drawingHotKey switch
            {
                Key.T when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "trend-line",
                Key.H when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "horizontal-line",
                Key.J when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "horizontal-ray",
                Key.V when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "vertical-line",
                Key.C when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "cross-line",
                Key.F when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "fib-retracement",
                Key.R when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "rectangle",
                _ => null
            };

            if (drawingToolId is not null)
            {
                ActivateDrawingToolFromUi(drawingToolId);
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.F)
            {
                foreach (CandleChartControl chart in _chartContexts.Values
                             .Select(item => item.Chart)
                             .Distinct())
                {
                    chart.CommitEditHistoryCheckpoint();
                }
                StatusText.Text = "Undo history locked here on all charts. Earlier changes cannot be undone.";
                e.Handled = true;
                return;
            }

            if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? historyContext))
            {
                if (e.Key == Key.Z)
                {
                    if (historyContext.Chart.CanUndoEditHistory)
                    {
                        historyContext.Chart.UndoDrawingChange();
                        StatusText.Text = "Undo.";
                    }
                    else
                    {
                        StatusText.Text = "Nothing to undo before the current checkpoint.";
                    }
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.R)
                {
                    if (historyContext.Chart.CanRedoEditHistory)
                    {
                        historyContext.Chart.RedoDrawingChange();
                        StatusText.Text = "Redo.";
                    }
                    else
                    {
                        StatusText.Text = "Nothing to redo.";
                    }
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Escape)
        {
            ClearWorkspacePartitionSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Home)
        {
            if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            {
                context.Chart.PushViewportUndoSnapshot();
                CandleChart_GoToEarliestRequested(context.Chart, EventArgs.Empty);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End)
        {
            if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            {
                context.Chart.PushViewportUndoSnapshot();
                CandleChart_GoToLatestRequested(context.Chart, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _ownedDrawingWindowsReady = false;
        _liveTimer.Stop();
        _maintenanceTimer.Stop();
        _workspaceSaveTimer.Stop();
        _markerTimer.Stop();
        _quickEditCommitTimer.Stop();
        ShutdownDemoTrading();
        ShutdownScreenRecorder();
        CloseAlertsAndReplayForShutdown();
        CommitQuickDrawingEdit();
        _selectionCancellation?.Cancel();
        _chartNavigationCancellation?.Cancel();
        foreach (ChartRuntimeContext context in _chartContexts.Values.ToArray())
            ResetContextHistoryPaging(context);
        FlushPendingHistoryWritesSynchronously();
        _lifetime.Cancel();
        SaveWorkspaceSynchronously();
        CloseWorkspaceWindowsForApplicationExit();

        foreach (DetachedChartWindow chartWindow in _detachedChartWindows.ToArray())
            chartWindow.Close();
        _detachedChartWindows.Clear();
        _activeDetachedChartWindow = null;

        if (_drawingFavoritesWindow is not null)
        {
            _drawingFavoritesWindow.Close();
            _drawingFavoritesWindow = null;
        }
        if (_timeframeFavoritesWindow is not null)
        {
            _timeframeFavoritesWindow.Close();
            _timeframeFavoritesWindow = null;
        }

        if (_detachedToolWindow is not null)
        {
            _detachedToolWindow.Close();
            _detachedToolWindow = null;
        }

        if (_historyProgressWindow is not null)
        {
            _historyProgressWindow.CloseAfterOperation();
            _historyProgressWindow = null;
        }

        if (_markerWindow is not null)
        {
            _markerWindow.CloseForShutdown();
            _markerWindow = null;
        }

        if (_scriptEditorWindow is not null)
        {
            _scriptEditorWindow.CloseForShutdown();
            _scriptEditorWindow = null;
        }

        if (_indicatorsWindow is not null)
        {
            _indicatorsWindow.CloseForShutdown();
            _indicatorsWindow = null;
        }
    }

    private async void ConnectionsButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenConnectionsWorkflowAsync();
    }

    private async Task OpenConnectionsWorkflowAsync()
    {
        bool reopen;
        do
        {
            reopen = false;
            var window = new ConnectionsWindow(
                _bridgeClient,
                _requestedSymbol,
                _activeTimeframe.DisplayText)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            if (window.SelectedConnector is not null &&
                (_selectedConnector is null ||
                 !string.Equals(_selectedConnector.ConnectorId, window.SelectedConnector.ConnectorId, StringComparison.Ordinal)))
            {
                await ConnectAsync(window.SelectedConnector);
            }

            switch (window.RequestedAction)
            {
                case ConnectionWindowAction.Connect:
                    break;
                case ConnectionWindowAction.ImportHistory:
                    await OpenHistoryOperationOptionsAsync(refresh: false);
                    break;
                case ConnectionWindowAction.RefreshHistory:
                    await OpenHistoryOperationOptionsAsync(refresh: true);
                    break;
                case ConnectionWindowAction.OpenCandleHistory:
                    reopen = await OpenCandleHistoryWindowAsync();
                    break;
                case ConnectionWindowAction.OpenTickHistory:
                    reopen = await OpenTickHistoryWindowAsync();
                    break;
            }
        }
        while (reopen);
    }

    private void InitializeMarkerExchangeSafely()
    {
        if (_markerExchangeInitialized)
            return;

        try
        {
            var markerExchange = new MarkerExchangeService();
            _markerExchange = markerExchange;
            _candleMarkers.Clear();
            _candleMarkers.AddRange(markerExchange.LoadMarkers());
            _markerExchangeInitialized = true;
            RefreshChartMarkers();
            if (IsLoaded && !_markerTimer.IsEnabled)
                _markerTimer.Start();
        }
        catch (Exception exception)
        {
            _markerExchange = null;
            _markerExchangeInitialized = false;
            StatusText.Text = "TickLab opened normally. Candle-marker storage is temporarily unavailable.";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Candle marker exchange",
                    "initialize_after_main_window_loaded",
                    "TickLab remains usable. Open Markers later to retry initialization and check Common Files permissions.",
                    ErrorCode: "TL-MARKER-INIT"),
                TickLabErrorSeverity.Warning,
                this,
                showPopup: false);
        }
    }

    private bool EnsureMarkerExchange()
    {
        if (!_markerExchangeInitialized || _markerExchange is null)
            InitializeMarkerExchangeSafely();
        return _markerExchangeInitialized && _markerExchange is not null;
    }

    private void MarkersButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureMarkerExchange();

        CandleMarkerWindow markerWindow;
        CandleMarkerWindow? existingMarkerWindow = _markerWindow;
        if (existingMarkerWindow is null)
        {
            markerWindow = new CandleMarkerWindow(_receiveMarkers)
            {
                Owner = this
            };
            _markerWindow = markerWindow;
            markerWindow.ReceiveChanged += enabled =>
            {
                _receiveMarkers = enabled;
                _preferences = _preferences with { ReceiveMarkers = enabled };
                markerWindow.SetStatus(enabled
                    ? "Receive ON — queued MT5 marker events will be processed."
                    : "Receive OFF — incoming MT5 marker events remain queued.");
                if (enabled)
                    _ = PollIncomingMarkersAsync();
            };
            markerWindow.FindRequested += draft => _ = FindCandleAsync(draft);
            markerWindow.MarkModeChanged += (enabled, draft) => _ = SetMarkerSelectionModeAsync(enabled, draft);
            markerWindow.ExportRequested += draft => _ = CreateMarkerAsync(draft, sendToMt5: true);
            markerWindow.GoToRequested += marker => _ = GoToMarkerAsync(marker);
            markerWindow.RemoveRequested += RemoveMarker;
            markerWindow.ClearExportedRequested += ClearExportedMarkers;
        }
        else
        {
            markerWindow = existingMarkerWindow;
        }

        string chartTimeframe = GetMarkerTimeframeCode(_activeTimeframe);
        Candle? latestChartCandle = _displayCandles.Count > 0 ? _displayCandles[^1] : null;
        string[] lastUsedSymbols =
        {
            _requestedSymbol,
            _preferences.LastChartSymbol,
            _preferences.LastSelectedSymbol
        };
        Mt5ConnectorSummary? selectedConnector = _selectedConnector;
        IEnumerable<string> savedInstrumentSymbols = selectedConnector is null
            ? Array.Empty<string>()
            : _historyStore.GetSavedInstruments(selectedConnector.ConnectorId)
                .Select(item => item.Symbol);
        string[] historySymbols = _historyStore.GetDatasets()
            .Select(item => item.Symbol)
            .Concat(savedInstrumentSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] mt5Symbols = _availableSymbols
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        markerWindow.SetChartContext(
            _requestedSymbol,
            chartTimeframe,
            _selectedCandle,
            latestChartCandle,
            lastUsedSymbols,
            historySymbols,
            mt5Symbols);
        markerWindow.SetMarkers(_candleMarkers);
        if (!_markerExchangeInitialized)
            markerWindow.SetStatus("Marker storage could not initialize. Check MT5 Common Files permissions, then close and reopen this window.");
        markerWindow.Show();
        markerWindow.Activate();
    }

    private async Task SetMarkerSelectionModeAsync(bool enabled, MarkerDraft? draft)
    {
        if (!enabled)
        {
            CandleChart.MarkerSelectionMode = false;
            CandleChart.InteractiveSelectionMarker = null;
            _markerWindow?.SetMarkMode(false);
            return;
        }

        if (draft is null)
            return;

        var selection = new CandleMarker(
            "TL_SELECTION_" + Guid.NewGuid().ToString("N"),
            draft.Symbol,
            draft.Timeframe,
            draft.StartUnix,
            "Selection",
            "TickLabSelection",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (!string.Equals(draft.Symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(draft.Timeframe, GetMarkerTimeframeCode(_activeTimeframe), StringComparison.OrdinalIgnoreCase) ||
            !_displayCandles.Any(item => item.StartUnix == draft.StartUnix))
        {
            await GoToMarkerAsync(selection);
        }

        CandleChart.InteractiveSelectionMarker = selection;
        CandleChart.MarkerSelectionMode = true;
        CandleChart.GoToTimestamp(selection.StartUnix);
        Candle? candle = _displayCandles.FirstOrDefault(item => item.StartUnix == selection.StartUnix);
        if (candle is not null)
            _markerWindow?.SetInteractiveMarker(candle);
        _markerWindow?.SetStatus("Mark mode ON. Drag the thin solid yellow line to select a candle, then click Export.");
    }

    private void CandleChart_InteractiveMarkerMoved(CandleMarker marker)
    {
        HandleReplayMarkerMoved(marker);
        Candle? candle = _displayCandles.FirstOrDefault(item => item.StartUnix == marker.StartUnix);
        if (candle is not null)
            _markerWindow?.SetInteractiveMarker(candle);
    }

    private void CandleChart_InteractiveMarkerPlacementCompleted(CandleMarker marker)
    {
        HandleReplayMarkerPlacementCompleted(marker);
    }

    private void ClearExportedMarkers()
    {
        CandleMarker[] exported = _candleMarkers
            .Where(item => item.Source.Contains("Export", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (CandleMarker marker in exported)
        {
            _candleMarkers.Remove(marker);
            try { _markerExchange?.SendRemoveToMt5(marker); } catch { }
        }
        SaveAndRefreshMarkers();
        _markerWindow?.SetStatus($"Removed {exported.Length:N0} exported marker(s). Other markers were kept.");
    }

    private async Task FindCandleAsync(MarkerDraft draft)
    {
        if (_findCandleInProgress)
        {
            _markerWindow?.SetStatus("Find is already running. Finish the current candle/tick search before starting another.");
            return;
        }

        _findCandleInProgress = true;
        _markerWindow?.SetFindingState(true);
        _markerWindow?.SetStatus(string.Equals(draft.Timeframe, "Tick", StringComparison.OrdinalIgnoreCase)
            ? "Finding tick…"
            : "Finding candle…");
        try
        {
            if (string.Equals(draft.Timeframe, "Tick", StringComparison.OrdinalIgnoreCase))
            {
                await FindRawTickAsync(draft);
                return;
            }

            await CreateMarkerAsync(draft, sendToMt5: false);
            CandleMarker? marker = _candleMarkers.FirstOrDefault(item =>
                string.Equals(item.Symbol, draft.Symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Timeframe, draft.Timeframe, StringComparison.OrdinalIgnoreCase) &&
                item.StartUnix == draft.StartUnix);
            if (marker is null)
                return;

            _markerWindow?.SetStatus("Candle found. Loading and centering it on the chart…");
            await GoToMarkerAsync(marker);
        }
        finally
        {
            _findCandleInProgress = false;
            _markerWindow?.SetFindingState(false);
        }
    }

    private static string GetMarkerTimeframeCode(TimeframeDefinition timeframe) =>
        timeframe.Unit == TimeframeUnit.Second
            ? timeframe.DisplayText
            : timeframe.NativeMt5Code ?? timeframe.DisplayText;

    private bool TryResolveMarkerTimeframe(
        string? raw,
        out TimeframeDefinition timeframe)
    {
        timeframe = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string value = raw.Trim();
        TimeframeDefinition? resolved = GetAllTimeframes().FirstOrDefault(item =>
            !item.IsRawTickChart &&
            (string.Equals(item.DisplayText, value, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(item.NativeMt5Code) &&
              string.Equals(item.NativeMt5Code, value, StringComparison.OrdinalIgnoreCase))));
        if (resolved is not null)
        {
            timeframe = resolved;
            return true;
        }

        if (!TimeframeDefinition.NativeMt5Timeframes.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        timeframe = TimeframeDefinition.FromNativeMt5Code(value);
        return true;
    }

    private async Task<bool> CenterActiveChartOnHistoricalTimestampAsync(
        ChartRuntimeContext context,
        string symbol,
        TimeframeDefinition timeframe,
        long anchorUnix,
        string description)
    {
        Mt5ConnectorSummary? selectedConnector = _selectedConnector;
        if (selectedConnector is null || timeframe.IsRawTickChart || _isClosing)
            return false;

        int generation = _selectionGeneration;
        long targetStartUnix = timeframe.GetBucketStartUnix(
            anchorUnix,
            selectedConnector.ServerUtcOffsetMinutes);
        int pageSize = Math.Max(
            ChartWindowPageRecords,
            GetChartLaunchPreviewRecords(timeframe));
        long beforeUnix = CalculateForwardSearchBoundary(
            targetStartUnix,
            timeframe,
            Math.Max(2, pageSize / 2));

        LocalChartResult result = await Task.Run(
            () => BuildLocalChartResult(
                selectedConnector.ConnectorId,
                selectedConnector,
                symbol,
                timeframe,
                HistoryLoadSelection.All,
                pageSize,
                beforeUnix,
                _lifetime.Token,
                anchorUnix),
            CancellationToken.None);

        if (_isClosing || generation != _selectionGeneration ||
            !ReferenceEquals(context, ActiveChartContext) ||
            !string.Equals(_requestedSymbol, symbol, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_activeTimeframe.Key, timeframe.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        List<Candle> display = result.Display
            .OrderBy(item => item.StartUnix)
            .ToList();
        if (display.Count == 0)
            return false;

        Candle? target = display.FirstOrDefault(item => item.StartUnix == targetStartUnix);
        target ??= display
            .Where(item => item.StartUnix <= anchorUnix)
            .LastOrDefault();
        if (target is null)
            return false;

        _sourceCandles = result.Source.ToList();
        _displayCandles = display;
        _allOlderHistoryLoaded =
            result.BoundaryUnix.HasValue &&
            _displayCandles[0].StartUnix <= result.BoundaryUnix.Value;

        // A historical Find Candle window is deliberately detached from the
        // live edge. Live capture continues in the bridge/permanent archive,
        // but changing timeframe must not append the current market onto this
        // old window and drag the viewport back to today.
        _allNewerHistoryLoaded = false;
        CommitLoadedHistoryToActiveContext();

        context.HistoricalNavigationAnchorUnix = anchorUnix;
        context.HistoricalNavigationAnchorSymbol = symbol;
        context.Chart.HistoricalNavigationAnchorUnix = anchorUnix;
        context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
        context.AllNewerHistoryLoaded = false;

        CandleChart.TimelineGaps = BuildChartTimelineGaps(
            selectedConnector.ConnectorId,
            symbol,
            timeframe);
        CandleChart.NativeHistoryBoundaryUnix = result.BoundaryUnix;
        CandleChart.HistoryBoundaryLabel = result.BoundaryLabel;
        CandleChart.CompleteOlderHistoryRequest();
        CandleChart.CompleteNewerHistoryRequest();
        CandleChart.Candles = _displayCandles;
        SyncDetachedChartWindows();
        RefreshChartMarkers();
        UpdateChartPagingAvailability();

        bool centered = CandleChart.GoToTimestamp(target.StartUnix);
        UpdateChartUi(description);
        return centered;
    }

    private async Task CreateMarkerAsync(MarkerDraft draft, bool sendToMt5)
    {
        if (!EnsureMarkerExchange())
        {
            _markerWindow?.SetStatus("Marker export is unavailable because the shared marker folder could not be opened.");
            return;
        }

        MarkerExchangeService? markerExchange = _markerExchange;
        if (markerExchange is null)
        {
            _markerWindow?.SetStatus("Marker export is unavailable because the shared marker folder could not be opened.");
            return;
        }

        try
        {
            if (!TryResolveMarkerTimeframe(draft.Timeframe, out TimeframeDefinition markerTimeframe))
            {
                _markerWindow?.SetStatus("That Find Candle timeframe is not supported by this TickLab chart.");
                return;
            }

            if (sendToMt5 && markerTimeframe.Unit == TimeframeUnit.Second)
            {
                _markerWindow?.SetStatus("Second timeframes are TickLab-local. Find/Mark works here; MT5 export requires a native MT5 timeframe.");
                return;
            }

            string activeMarkerTimeframe = GetMarkerTimeframeCode(_activeTimeframe);
            bool exactCurrentCandle = string.Equals(draft.Symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(draft.Timeframe, activeMarkerTimeframe, StringComparison.OrdinalIgnoreCase) &&
                _displayCandles.Any(item => item.StartUnix == draft.StartUnix);

            if (!exactCurrentCandle)
            {
                Mt5ConnectorSummary? selectedConnector = _selectedConnector;
                if (selectedConnector is null)
                {
                    _markerWindow?.SetStatus("The exact candle is not open on the chart and no matching MT5/history connection is selected.");
                    return;
                }

                long requestedAnchorUnix = draft.RequestedUnix ?? draft.StartUnix;
                long requestedBucketStart = markerTimeframe.GetBucketStartUnix(
                    requestedAnchorUnix,
                    selectedConnector.ServerUtcOffsetMinutes);

                // Seconds history is virtual: a candle does not have to exist as
                // a pre-generated object before Find Candle is pressed. Build a
                // full normal chart page around the requested time directly from
                // stored raw ticks. ReadSecondCandlesOnDemand will index only the
                // raw-tick snapshots required for this historical window.
                int validationRecords = markerTimeframe.Unit == TimeframeUnit.Second
                    ? ChartWindowPageRecords
                    : 8;
                int forwardBuckets = markerTimeframe.Unit == TimeframeUnit.Second
                    ? Math.Max(2, validationRecords / 2)
                    : 4;
                long validationBefore = CalculateForwardSearchBoundary(
                    requestedBucketStart,
                    markerTimeframe,
                    forwardBuckets);
                LocalChartResult nearby = await Task.Run(
                    () => BuildLocalChartResult(
                        selectedConnector.ConnectorId,
                        selectedConnector,
                        draft.Symbol,
                        markerTimeframe,
                        HistoryLoadSelection.All,
                        validationRecords,
                        validationBefore,
                        _lifetime.Token,
                        markerTimeframe.Unit == TimeframeUnit.Second
                            ? requestedAnchorUnix
                            : null),
                    CancellationToken.None);
                bool exactBucketAvailable = nearby.Display.Any(item =>
                    item.StartUnix == requestedBucketStart);
                if (markerTimeframe.Unit == TimeframeUnit.Second)
                {
                    // Find Candle on a seconds chart is a date/time navigation
                    // tool first. A quiet second can legitimately contain zero
                    // ticks, so do not reject the historical pointer merely
                    // because that exact bucket has no candle. The surrounding
                    // raw-tick page only needs to span the requested time; the
                    // vertical historical anchor remains exact.
                    bool surroundingWindowAvailable = nearby.Display.Count > 0 &&
                        nearby.Display[0].StartUnix <= requestedAnchorUnix &&
                        nearby.Display[^1].StartUnix >= requestedAnchorUnix;
                    if (!exactBucketAvailable && !surroundingWindowAvailable)
                    {
                        _markerWindow?.SetStatus(
                            "No stored raw-tick history spans that requested date/time. Refresh Tick Data if MT5 still provides that older range.");
                        return;
                    }
                }
                else if (!exactBucketAvailable)
                {
                    _markerWindow?.SetStatus(
                        "That exact symbol, timeframe and broker-server candle time is not available in saved TickLab history.");
                    return;
                }
            }

            int existingIndex = _candleMarkers.FindIndex(item =>
                string.Equals(item.Symbol, draft.Symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Timeframe, draft.Timeframe, StringComparison.OrdinalIgnoreCase) &&
                item.StartUnix == draft.StartUnix);
            string markerId = existingIndex >= 0
                ? _candleMarkers[existingIndex].Id
                : Guid.NewGuid().ToString("N");
            var marker = new CandleMarker(
                markerId,
                draft.Symbol,
                draft.Timeframe,
                draft.StartUnix,
                string.IsNullOrWhiteSpace(draft.Label)
                    ? sendToMt5 ? "Exported candle" : "TickLab marker"
                    : draft.Label,
                sendToMt5 ? "TickLabExported" : "TickLab",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                sendToMt5 ? null : draft.RequestedUnix ?? draft.StartUnix);

            // Export is transactional: keep the yellow selection visible until
            // the shared MT5 queue write succeeds. A failed export must never
            // leave a misleading red marker behind.
            if (sendToMt5)
                markerExchange.SendAddToMt5(marker);

            if (existingIndex >= 0)
                _candleMarkers[existingIndex] = marker;
            else
                _candleMarkers.Add(marker);

            markerExchange.SaveMarkers(_candleMarkers);
            RefreshChartMarkers();
            _markerWindow?.SetMarkers(_candleMarkers);

            if (sendToMt5)
            {
                CandleChart.MarkerSelectionMode = false;
                CandleChart.InteractiveSelectionMarker = null;
                _markerWindow?.SetMarkMode(false);
                _markerWindow?.SetStatus("Export succeeded. The yellow selection line became a thick red exported marker.");
            }
            else
            {
                _markerWindow?.SetStatus("Exact candle marked in TickLab.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Candle marker exchange",
                    sendToMt5 ? "send_marker_to_mt5" : "mark_in_ticklab",
                    sendToMt5
                        ? "Copy diagnostics and confirm the MT5 Common Files TickLab folder is writable."
                        : "Copy diagnostics and confirm the requested candle exists in TickLab history.",
                    ErrorCode: sendToMt5 ? "TL-MARKER-SEND" : "TL-MARKER-LOCAL",
                    Symbol: draft.Symbol,
                    Timeframe: draft.Timeframe,
                    BlockStartUnix: draft.StartUnix),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private async Task PollIncomingMarkersAsync()
    {
        if (!_receiveMarkers || _markerPollRunning || _isClosing ||
            !_markerExchangeInitialized)
        {
            return;
        }

        MarkerExchangeService? exchange = _markerExchange;
        if (exchange is null)
            return;

        _markerPollRunning = true;
        try
        {
            IReadOnlyList<CandleMarkerTransfer> transfers = await Task.Run(
                exchange.ReadPendingFromMt5,
                _lifetime.Token);
            if (transfers.Count == 0)
                return;

            foreach (CandleMarkerTransfer transfer in transfers)
            {
                if (string.Equals(transfer.Action, "remove", StringComparison.OrdinalIgnoreCase))
                {
                    _candleMarkers.RemoveAll(item => string.Equals(item.Id, transfer.Id, StringComparison.Ordinal));
                    continue;
                }

                if (!string.Equals(transfer.Action, "add", StringComparison.OrdinalIgnoreCase))
                    continue;

                var marker = new CandleMarker(
                    transfer.Id,
                    transfer.Symbol,
                    transfer.Timeframe,
                    transfer.StartUnix,
                    string.IsNullOrWhiteSpace(transfer.Label) ? "MT5 marker" : transfer.Label,
                    "MT5Received",
                    transfer.CreatedUnix);
                int existing = _candleMarkers.FindIndex(item =>
                    string.Equals(item.Id, marker.Id, StringComparison.Ordinal));
                if (existing >= 0)
                    _candleMarkers[existing] = marker;
                else
                    _candleMarkers.Add(marker);
            }

            SaveAndRefreshMarkers();
            _markerWindow?.SetStatus($"Received {transfers.Count:N0} queued marker event(s) from MT5.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Candle marker exchange",
                    "receive_markers_from_mt5",
                    "Incoming events remain queued. Copy diagnostics, then switch Receive ON again.",
                    ErrorCode: "TL-MARKER-RECEIVE"),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            _markerPollRunning = false;
        }
    }

    private void RemoveMarker(CandleMarker marker)
    {
        _candleMarkers.RemoveAll(item => string.Equals(item.Id, marker.Id, StringComparison.Ordinal));

        // Find Candle can paint three related pieces: the persisted marker, the
        // exact-time navigation guide, and the thin interactive selection line.
        // Remove Marker must clear all of them so no ghost line survives.
        long navigationAnchorUnix = marker.AnchorUnix ?? marker.StartUnix;
        bool clearedInteractiveSelection = false;
        foreach (ChartRuntimeContext context in _chartContexts.Values)
        {
            if (!string.Equals(context.Symbol, marker.Symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (context.HistoricalNavigationAnchorUnix == navigationAnchorUnix)
            {
                context.HistoricalNavigationAnchorUnix = null;
                context.HistoricalNavigationAnchorSymbol = string.Empty;
                context.Chart.HistoricalNavigationAnchorUnix = null;
            }

            // The thin yellow Find Candle selector is a third, independent
            // visual layer. Clear it when it points at the same persisted
            // marker so Remove Marker cannot leave a selectable-looking ghost.
            CandleMarker? interactive = context.Chart.InteractiveSelectionMarker;
            if (interactive is not null &&
                !interactive.Source.StartsWith("TickLabReplay", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(interactive.Symbol, marker.Symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(interactive.Timeframe, marker.Timeframe, StringComparison.OrdinalIgnoreCase) &&
                interactive.StartUnix == marker.StartUnix)
            {
                context.Chart.InteractiveSelectionMarker = null;
                context.Chart.MarkerSelectionMode = false;
                clearedInteractiveSelection = true;
            }
        }

        RefreshChartMarkers();
        _markerWindow?.SetMarkers(_candleMarkers);
        if (clearedInteractiveSelection)
            _markerWindow?.SetMarkMode(false);
        SaveWorkspace();

        if (!EnsureMarkerExchange())
        {
            _markerWindow?.SetStatus("Marker removed from TickLab, but MT5 synchronization is unavailable.");
            return;
        }

        MarkerExchangeService? markerExchange = _markerExchange;
        if (markerExchange is null)
        {
            _markerWindow?.SetStatus("Marker removed from TickLab, but MT5 synchronization is unavailable.");
            return;
        }

        try
        {
            markerExchange.SaveMarkers(_candleMarkers);
            markerExchange.SendRemoveToMt5(marker);
            _markerWindow?.SetStatus("Marker removed from TickLab. Removal was exported to MT5.");
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Candle marker exchange",
                    "remove_marker",
                    "The marker was removed from TickLab, but MT5 synchronization failed. Copy diagnostics and retry from the marker window.",
                    ErrorCode: "TL-MARKER-REMOVE",
                    Symbol: marker.Symbol,
                    Timeframe: marker.Timeframe,
                    BlockStartUnix: marker.StartUnix),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private void RemoveFindCandleSelection(CandleChartControl chart)
    {
        CandleMarker? selection = chart.InteractiveSelectionMarker;
        if (selection is null ||
            selection.Source.StartsWith("TickLabReplay", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        chart.InteractiveSelectionMarker = null;
        chart.MarkerSelectionMode = false;
        if (ReferenceEquals(chart, CandleChart))
            _markerWindow?.SetMarkMode(false);
        StatusText.Text = "Find Candle selection marker removed.";
    }

    private void RemoveHistoricalNavigationAnchor(
        CandleChartControl chart,
        long anchorUnix)
    {
        ChartRuntimeContext context = FindChartContext(chart);
        if (context.HistoricalNavigationAnchorUnix != anchorUnix &&
            chart.HistoricalNavigationAnchorUnix != anchorUnix)
        {
            return;
        }

        // If a persisted Find Candle marker still exists at this exact anchor,
        // remove it through the normal marker path as well. This makes the
        // manual fallback a complete cleanup rather than merely hiding a line.
        CandleMarker[] matchingMarkers = _candleMarkers
            .Where(item =>
                string.Equals(item.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase) &&
                (item.AnchorUnix ?? item.StartUnix) == anchorUnix)
            .ToArray();
        if (matchingMarkers.Length > 0)
        {
            foreach (CandleMarker marker in matchingMarkers)
                RemoveMarker(marker);
            return;
        }

        context.HistoricalNavigationAnchorUnix = null;
        context.HistoricalNavigationAnchorSymbol = string.Empty;
        chart.HistoricalNavigationAnchorUnix = null;
        SaveWorkspace();
        StatusText.Text = "Find Candle marker guide removed.";
    }

    private void SaveAndRefreshMarkers()
    {
        _markerExchange?.SaveMarkers(_candleMarkers);
        RefreshChartMarkers();
        _markerWindow?.SetMarkers(_candleMarkers);
    }

    private void RefreshChartMarkers()
    {
        string timeframe = GetMarkerTimeframeCode(_activeTimeframe);
        CandleChart.Markers = _candleMarkers
            .Where(item =>
                string.Equals(item.Symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private async Task GoToMarkerAsync(CandleMarker marker)
    {
        Mt5ConnectorSummary? selectedConnector = _selectedConnector;
        if (selectedConnector is null)
        {
            _markerWindow?.SetStatus("Connect the matching MT5 history first.");
            return;
        }

        if (!TryResolveMarkerTimeframe(marker.Timeframe, out TimeframeDefinition timeframe))
        {
            _markerWindow?.SetStatus("That marker timeframe is not supported by this TickLab chart.");
            return;
        }

        try
        {
            long navigationAnchorUnix = marker.AnchorUnix ?? marker.StartUnix;
            long targetStartUnix = timeframe.GetBucketStartUnix(
                navigationAnchorUnix,
                selectedConnector.ServerUtcOffsetMinutes);
            ChartRuntimeContext context = ActiveChartContext;

            if (string.Equals(marker.Symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(timeframe.Key, _activeTimeframe.Key, StringComparison.OrdinalIgnoreCase) &&
                CandleChart.GoToTimestamp(targetStartUnix))
            {
                context.HistoricalNavigationAnchorUnix = navigationAnchorUnix;
                context.HistoricalNavigationAnchorSymbol = marker.Symbol;
                context.Chart.HistoricalNavigationAnchorUnix = navigationAnchorUnix;
                // A successful Find Candle explicitly detaches this pane from
                // the live tail. Live capture continues in storage, but the
                // historical viewport stays stable until End / Go Live.
                context.AllNewerHistoryLoaded = false;
                _allNewerHistoryLoaded = false;
                UpdateChartPagingAvailability(context);
                _markerWindow?.SetStatus("Marker opened. Changing timeframe will stay around this exact historical date/time.");
                return;
            }

            _selectionGeneration++;
            _requestedSymbol = marker.Symbol;
            _activeTimeframe = timeframe;
            _sourceTimeframe = timeframe.SourceMt5Code;
            context.Symbol = marker.Symbol;
            context.Timeframe = timeframe;
            if (!timeframe.IsRawTickChart)
                context.LastCandleTimeframe = timeframe;
            _selectedCandle = null;
            _allOlderHistoryLoaded = false;
            _allNewerHistoryLoaded = false;

            SetChartIdentityUi();
            BuildTimeframeButtons();

            bool centered = await CenterActiveChartOnHistoricalTimestampAsync(
                context,
                marker.Symbol,
                timeframe,
                navigationAnchorUnix,
                "marker-centered virtual chart window");
            if (!centered)
            {
                _markerWindow?.SetStatus("The exact marked candle is not available in saved TickLab history.");
                return;
            }

            RefreshAlertLines();
            RefreshDemoTradeLines();
            SaveWorkspace();
            _markerWindow?.SetStatus("Marker opened. Timeframe changes now preserve the exact marked date/time; the original Find timeframe is only the initial lookup context.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Candle marker navigation",
                    "go_to_marker",
                    "Confirm the symbol/timeframe history is saved, then retry Go to Marker.",
                    ErrorCode: "TL-MARKER-GOTO",
                    Symbol: marker.Symbol,
                    Timeframe: marker.Timeframe,
                    ConnectorId: selectedConnector.ConnectorId,
                    BlockStartUnix: marker.StartUnix),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshEntireTickLabAsync();
    }

    private async Task RefreshEntireTickLabAsync()
    {
        if (_isClosing)
            return;

        StatusText.Text = "Refreshing TickLab connections, chart, indicators, markers and workspace…";
        (int _, CancellationToken navigationToken) = BeginChartNavigation();
        try
        {
            await RefreshConnectorStateAsync();
            await RefreshSymbolsAsync(force: true);
            await RefreshActiveChartAsync(resetViewport: false, navigationToken);
            if (_receiveMarkers)
                await PollIncomingMarkersAsync();
            RefreshChartMarkers();
            RefreshAllAppliedIndicators(force: true);
            SaveWorkspace();
            StatusText.Text = "TickLab refresh completed. Saved history and attached tools were preserved.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Refresh was replaced by a newer chart request.";
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "TickLab refresh",
                    "refresh_entire_application",
                    "Saved history remains unchanged. Retry Refresh or restart only the desktop app.",
                    ErrorCode: "TL-REFRESH-ALL",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private async Task RefreshActiveChartAsync(
        bool resetViewport,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_requestedSymbol) || _selectedConnector is null)
        {
            StatusText.Text = "Select a chart instrument first.";
            return;
        }

        CancellationToken token = cancellationToken.CanBeCanceled
            ? cancellationToken
            : _lifetime.Token;

        ChartRuntimeContext activeContext = ActiveChartContext;
        long? historicalAnchor =
            activeContext.HistoricalNavigationAnchorUnix.HasValue &&
            string.Equals(
                activeContext.HistoricalNavigationAnchorSymbol,
                _requestedSymbol,
                StringComparison.OrdinalIgnoreCase)
                ? activeContext.HistoricalNavigationAnchorUnix
                : null;

        // Refresh must not silently destroy a Find Candle investigation. When
        // an exact historical date/time anchor is active, rebuild the bounded
        // window around that same timestamp instead of loading the live tail.
        if (historicalAnchor.HasValue && !_activeTimeframe.IsRawTickChart)
        {
            bool centered = await CenterActiveChartOnHistoricalTimestampAsync(
                activeContext,
                _requestedSymbol,
                _activeTimeframe,
                historicalAnchor.Value,
                "refreshed historical Find Candle window");
            if (centered)
            {
                RefreshAllAppliedIndicators(force: true);
                StatusText.Text = $"Historical chart refreshed around the Find Candle date/time: {_requestedSymbol} {_activeTimeframe.DisplayText}.";
                return;
            }
        }

        ChartViewportState viewport = CandleChart.CaptureViewport();
        if (_activeTimeframe.IsRawTickChart)
        {
            StatusText.Text = $"Refreshing {_requestedSymbol} raw Tick chart…";
            await LoadRawTickChartAsync(ActiveChartContext, resetViewport, token);
            await RefreshRawTickChartLiveAsync(ActiveChartContext, force: true);
            return;
        }
        StatusText.Text = $"Refreshing {_requestedSymbol} {_activeTimeframe.DisplayText} chart…";
        _lastLiveWriteUtc = DateTime.MinValue;
        _lastClosedWriteUtc = DateTime.MinValue;
        _lastChartBootstrapWriteUtc = DateTime.MinValue;

        await LoadLocalChartAsync(
            _selectionGeneration,
            _requestedSymbol,
            _activeTimeframe,
            _historySelection,
            token,
            GetChartLaunchPreviewRecords(_activeTimeframe));

        if (resetViewport)
            CandleChart.ResetToLaunchView();
        else
            CandleChart.RestoreViewport(viewport);

        if (_activeTimeframe.UsesTickArchive)
            await RefreshSecondChartLiveAsync();
        else
            await RefreshNativeLiveAsync(force: true);

        RefreshAllAppliedIndicators(force: true);
        StatusText.Text = $"Chart refreshed: {_requestedSymbol} {_activeTimeframe.DisplayText}.";
    }

    private void CandleChart_ScrollWheelModeChanged(CandleChartControl chart, ChartScrollWheelMode mode)
    {
        ChartRuntimeContext context = FindChartContext(chart);
        ChartSettings settings = context.Settings with { ScrollWheelMode = mode };
        context.Settings = settings;
        chart.Settings = settings;
        context.TickChart.Settings = settings;
        SaveWorkspace();
        StatusText.Text = mode == ChartScrollWheelMode.Zoom
            ? $"Chart {context.PaneId} scroll wheel: Zoom Mode."
            : $"Chart {context.PaneId} scroll wheel: Scroll Mode.";
        UpdateDrawingToolbarState();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenChartSettingsForSelectedChart();

    private void OpenChartSettingsForSelectedChart()
    {
        int[] availableChartIds = _workspacePaneRegistry.Values
            .Where(item => item.Kind == WorkspacePaneKind.PriceChart)
            .Select(item => item.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (availableChartIds.Length == 0)
        {
            StatusText.Text = "Open a chart before using Chart Settings.";
            return;
        }
        int paneId = availableChartIds.Contains(_activePricePaneId)
            ? _activePricePaneId
            : availableChartIds[0];
        ChartRuntimeContext context = GetChartContext(paneId);
        ChartSettings original = context.Settings;
        string originalTheme = _preferences.ApplicationTheme;
        var window = new ChartSettingsWindow(
            paneId,
            context.Symbol,
            context.Timeframe.DisplayText,
            original,
            originalTheme,
            availableChartIds)
        {
            Owner = this
        };
        window.PreviewChanged += settings =>
        {
            context.Chart.Settings = settings;
            context.TickChart.Settings = settings;
            context.IndicatorStack.SetChartSettings(settings);
        };
        window.ThemePreviewChanged += ApplyApplicationThemePreview;

        if (window.ShowDialog() == true)
        {
            context.Settings = window.Settings;
            context.Chart.Settings = window.Settings;
            context.TickChart.Settings = window.Settings;
            context.IndicatorStack.SetChartSettings(window.Settings);
            foreach (int targetId in window.CopyTargetChartIds)
            {
                if (!_chartContexts.TryGetValue(targetId, out ChartRuntimeContext? target))
                    continue;
                target.Settings = window.Settings;
                target.Chart.Settings = window.Settings;
                target.TickChart.Settings = window.Settings;
                target.IndicatorStack.SetChartSettings(window.Settings);
            }
            _preferences = _preferences with
            {
                ApplicationTheme = window.ApplicationTheme,
                Chart = window.UseAsDefaultForFutureCharts ? window.Settings : _preferences.Chart
            };
            ApplyApplicationThemePreview(window.ApplicationTheme);
            SaveWorkspace();
        }
        else
        {
            context.Chart.Settings = original;
            context.TickChart.Settings = original;
            context.IndicatorStack.SetChartSettings(original);
            ApplyApplicationThemePreview(originalTheme);
        }
    }

    private void ApplyApplicationThemePreview(string theme)
    {
        ApplicationThemeManager.Apply(theme);

        // Vector icons are created with the brush that exists at construction time.
        // Rebuild the visual-only toolbar content immediately after a live theme swap
        // so the rail never keeps the previous theme's icon brush until a tool click.
        ApplyTopToolbarIcons(theme);
        UpdateChartTypeButton();
        BuildTimeframeButtons();
        BuildDrawingToolbar();
        ApplyDrawingToolbarCollapsedState();

        if (_quickEditDrawing is not null && DrawingQuickEditBar.Visibility == Visibility.Visible)
            ShowDrawingQuickEditor(_quickEditDrawing);
    }

    private void ApplyTopToolbarIcons(string? appliedTheme = null)
    {
        Brush iconBrush = TryFindResource("TextBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(226, 232, 240));
        NewChartButton.Content = TopBarIconFactory.CreateAction("new-chart", 28.5, iconBrush);
        LayoutButton.Content = TopBarIconFactory.CreateAction("layout", 28.5, iconBrush);
        IndicatorsTopButton.Content = TopBarIconFactory.CreateAction("indicators", 28.5, iconBrush);
        AlertsButton.Content = TopBarIconFactory.CreateAction("alerts", 28.5, iconBrush);
        ReplayButton.Content = TopBarIconFactory.CreateAction("replay", 28.5, iconBrush);
        ConnectionsTopButton.Content = TopBarIconFactory.CreateAction("connections", 28.5, iconBrush);
        RefreshTopButton.Content = TopBarIconFactory.CreateAction("refresh", 27, iconBrush);
        MarkersTopButton.Content = TopBarIconFactory.CreateAction("markers", 27, iconBrush);
        bool light = string.Equals(appliedTheme ?? _preferences.ApplicationTheme, "Light", StringComparison.OrdinalIgnoreCase);
        ThemeButton.Content = TopBarIconFactory.CreateAction(light ? "theme-light" : "theme-dark", 27, iconBrush);
        SettingsTopButton.Content = TopBarIconFactory.CreateAction("settings", 27, iconBrush);
        OpenFolderTopButton.Content = TopBarIconFactory.CreateAction("folder", 27, iconBrush);
        CodePanelButton.Content = TopBarIconFactory.CreateAction("code", 27, iconBrush);
        RecorderButton.Content = TopBarIconFactory.CreateAction("record", 27, iconBrush);
        ScreenshotButton.Content = TopBarIconFactory.CreateAction("screenshot", 27, iconBrush);
        RestoreToolButton.Content = TopBarIconFactory.CreateAction("restore", 27, iconBrush);
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        string next = string.Equals(_preferences.ApplicationTheme, "Light", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        _preferences = _preferences with { ApplicationTheme = next };
        ApplyApplicationThemePreview(next);
        SaveWorkspace();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = _selectedConnector is null
            ? _historyStore.RootPath
            : _historyStore.RootPath;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private async void SymbolSearchBorder_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await OpenSymbolPickerAsync();

    private async void OpenSymbolSelectorButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await OpenSymbolPickerAsync();

    private async Task OpenSymbolPickerAsync()
    {
        Mt5SymbolInfo? selectedSymbol = await ShowSymbolPickerForSelectionAsync();
        LoadMarketFavourites();
        RefreshMarketWorkspace();
        if (selectedSymbol is not null)
            await SafeSelectChartAsync(selectedSymbol.Name, _activeTimeframe);
    }

    private async Task<Mt5SymbolInfo?> ShowSymbolPickerForSelectionAsync()
    {
        if (_selectedConnector is null)
        {
            StatusText.Text = "Connect TickLab to MT5 first.";
            return null;
        }

        await RefreshSymbolsAsync(force: true);

        var picker = new SymbolPickerWindow(_availableSymbols, _requestedSymbol)
        {
            Owner = this
        };

        picker.RefreshRequested += async (_, _) =>
        {
            await RefreshSymbolsAsync(force: true);
            picker.ReplaceSymbols(_availableSymbols);
        };

        return picker.ShowDialog() == true ? picker.SelectedSymbol : null;
    }

    private void IndicatorsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_indicatorsWindow is null)
        {
            var window = new IndicatorsWindow { Owner = this };
            window.ApplyRequested += ApplyTickScriptIndicatorFromSelection;
            window.BuiltInApplyRequested += ApplyBuiltInIndicatorFromSelection;
            window.AppliedEditRequested += key => EditIndicatorByKey(ActiveChartContext, key);
            window.AppliedRemoveRequested += key => RemoveIndicatorByKey(ActiveChartContext, key);
            window.AppliedRouteRequested += (key, action) => RouteIndicatorByKey(ActiveChartContext, key, action);
            window.OpenEditorRequested += () => CodeEditorButton_Click(this, new RoutedEventArgs());
            _indicatorsWindow = window;
        }
        _indicatorsWindow.Refresh();
        RefreshIndicatorsWindowAppliedList();
        _indicatorsWindow.Show();
        _indicatorsWindow.Activate();
    }

    private void CandleChart_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TickLab.TickScriptIndicator"))
            return;
        string? sourcePath = e.Data.GetData("TickLab.TickScriptIndicator") as string;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;
        TickScriptEntry? entry = new TickScriptStore().GetIndicators().FirstOrDefault(item =>
            string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            ApplyIndicatorToActiveChart(entry);
    }

    private void RestoreAppliedIndicators()
    {
        IReadOnlyList<AppliedTickScriptIndicatorPreference> preferences =
            _preferences.AppliedTickScriptIndicators ?? Array.Empty<AppliedTickScriptIndicatorPreference>();
        if (preferences.Count == 0 && _preferences.AppliedIndicatorSourcePaths.Count > 0)
        {
            preferences = _preferences.AppliedIndicatorSourcePaths
                .Select(path => new AppliedTickScriptIndicatorPreference
                {
                    SourcePath = path,
                    Appearance = TickScriptIndicatorAppearance.Default
                })
                .ToArray();
        }
        RestoreTickScriptIndicatorsForContext(ActiveChartContext, preferences, evaluateImmediately: true);
    }

    private void RestoreTickScriptIndicatorsForContext(
        ChartRuntimeContext context,
        IEnumerable<AppliedTickScriptIndicatorPreference>? preferences,
        bool evaluateImmediately)
    {
        AppliedTickScriptIndicatorPreference[] requested = preferences?
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.SourcePath))
            .ToArray() ?? Array.Empty<AppliedTickScriptIndicatorPreference>();
        if (requested.Length == 0)
            return;

        IReadOnlyList<TickScriptEntry> available = new TickScriptStore().GetIndicators();
        foreach (AppliedTickScriptIndicatorPreference preference in requested)
        {
            TickScriptEntry? entry = available.FirstOrDefault(item =>
                string.Equals(item.SourcePath, preference.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            int existing = context.AppliedIndicators.FindIndex(item =>
                string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                context.AppliedIndicators[existing] = entry;
            else
                context.AppliedIndicators.Add(entry);
            context.IndicatorAppearances[entry.SourcePath] =
                (preference.Appearance ?? TickScriptIndicatorAppearance.Default) with { };
        }

        if (evaluateImmediately)
            RefreshAppliedIndicatorsForContext(context, force: true);
    }

    private TickScriptIndicatorAppearance GetTickScriptAppearance(
        ChartRuntimeContext context,
        string sourcePath)
    {
        if (context.IndicatorAppearances.TryGetValue(sourcePath, out TickScriptIndicatorAppearance? appearance) && appearance is not null)
            return appearance;
        TickScriptIndicatorAppearance created = TickScriptIndicatorAppearance.Default with { };
        context.IndicatorAppearances[sourcePath] = created;
        return created;
    }

    private static IReadOnlyList<AppliedTickScriptIndicatorPreference> CaptureTickScriptIndicatorPreferences(
        ChartRuntimeContext context) =>
        context.AppliedIndicators
            .Select(entry => new AppliedTickScriptIndicatorPreference
            {
                SourcePath = entry.SourcePath,
                Appearance = context.IndicatorAppearances.TryGetValue(entry.SourcePath, out TickScriptIndicatorAppearance? appearance) && appearance is not null
                    ? appearance with { }
                    : TickScriptIndicatorAppearance.Default with { }
            })
            .ToArray();

    private void ApplyIndicatorToActiveChart(TickScriptEntry entry) =>
        ApplyIndicatorToContext(ActiveChartContext, entry, appearance: null);

    private void ApplyIndicatorToContext(
        ChartRuntimeContext context,
        TickScriptEntry entry,
        TickScriptIndicatorAppearance? appearance)
    {
        try
        {
            int existing = context.AppliedIndicators.FindIndex(item =>
                string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                context.AppliedIndicators[existing] = entry;
            else
                context.AppliedIndicators.Add(entry);

            if (appearance is not null)
                context.IndicatorAppearances[entry.SourcePath] = appearance with { };
            else
                _ = GetTickScriptAppearance(context, entry.SourcePath);

            RefreshAppliedIndicatorsForContext(context, force: true);
            if (ReferenceEquals(context, ActiveChartContext))
            {
                ShowIndicatorsForActiveChart();
                RefreshIndicatorsWindowAppliedList();
                _indicatorsWindow?.Hide();
                StatusText.Text = $"Applied {entry.Name}. Indicator panes share the chart time axis while keeping their own vertical scale.";
            }
            SaveWorkspace();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Indicator could not be applied: {exception.Message}";
        }
    }

    private void EditTickScriptIndicatorProperties(ChartRuntimeContext context, TickScriptEntry entry)
    {
        TickScriptIndicatorAppearance current = GetTickScriptAppearance(context, entry.SourcePath);
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions(context.PaneId, context.PaneId, true);
        var settings = new TickScriptIndicatorSettingsWindow(entry, current, options) { Owner = this };
        bool? accepted = settings.ShowDialog();
        if (settings.OpenCodeEditorRequested)
        {
            OpenIndicatorInEditor(entry);
            return;
        }
        if (accepted != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;

        if (placement.PlaceAddress.PriceChartPaneId != context.PaneId)
        {
            TickScriptIndicatorAppearance moved = settings.Result with { LinkedGroupId = string.Empty };
            if (placement.PlaceAddress.PriceChartPaneId is int targetPaneId &&
                _chartContexts.TryGetValue(targetPaneId, out ChartRuntimeContext? targetChart))
            {
                if (!TryCopyTickScriptIndicatorToChart(targetChart, entry, moved))
                    return;
                RemoveAppliedIndicator(context, entry);
                StatusText.Text = $"Moved {entry.Name} to Chart {targetChart.PaneId}.";
                SaveWorkspace();
                return;
            }

            if (!TryCreateTickScriptIndicatorWorkspace(entry, moved, placement, out _))
                return;
            RemoveAppliedIndicator(context, entry);
            StatusText.Text = $"Moved {entry.Name} to Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}.";
            SaveWorkspace();
            return;
        }

        TickScriptIndicatorAppearance updated = settings.Result with { LinkedGroupId = current.LinkedGroupId };
        ChartRuntimeContext[] targets = string.IsNullOrWhiteSpace(current.LinkedGroupId)
            ? new[] { context }
            : _chartContexts.Values
                .Where(candidate => candidate.IndicatorAppearances.Values.Any(appearance =>
                    string.Equals(appearance.LinkedGroupId, current.LinkedGroupId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        foreach (ChartRuntimeContext target in targets)
        {
            TickScriptEntry? targetEntry = target.AppliedIndicators.FirstOrDefault(candidate =>
                string.Equals(candidate.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (targetEntry is null)
                continue;
            target.IndicatorAppearances[targetEntry.SourcePath] = updated with { };
            if (target.IndicatorResults.TryGetValue(targetEntry.SourcePath, out TickScriptIndicatorResult? result) && result is not null)
                target.IndicatorStack.UpdateResult(targetEntry, result, updated);
            target.IndicatorStack.SetViewport(target.Chart.CaptureViewportSnapshot());
        }
        if (ReferenceEquals(context, ActiveChartContext))
        {
            ShowIndicatorsForActiveChart();
            RefreshIndicatorsWindowAppliedList();
            StatusText.Text = $"Updated colours and display properties for {entry.Name}.";
        }
        SaveWorkspace();
    }

    private void RefreshAppliedIndicator(TickScriptEntry entry, bool force = false) =>
        RefreshAppliedIndicator(ActiveChartContext, entry, force);

    private void RefreshAppliedIndicator(ChartRuntimeContext context, TickScriptEntry entry, bool force = false)
    {
        if (!context.AppliedIndicators.Any(item =>
                string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase)))
            return;
        RefreshAppliedIndicatorsForContext(context, force: force);
    }

    private void RefreshAllAppliedIndicators(bool force = false)
    {
        RefreshAllBuiltInIndicators(force);
        foreach (ChartRuntimeContext context in _chartContexts.Values.ToArray())
            RefreshAppliedIndicatorsForContext(context, force);
        RefreshAllIndependentIndicatorWorkspaces(force);
    }

    private void RefreshAppliedIndicatorsForContext(ChartRuntimeContext context, bool force)
    {
        if (context.AppliedIndicators.Count == 0)
        {
            context.IndicatorResults.Clear();
            return;
        }
        if (_isClosing || context.Chart.Candles.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        if (!force && now - context.LastIndicatorRefreshUtc < TimeSpan.FromMilliseconds(500))
        {
            context.IndicatorRefreshPending = true;
            return;
        }
        if (context.IndicatorRefreshRunning)
        {
            context.IndicatorRefreshPending = true;
            return;
        }
        _ = RefreshAppliedIndicatorsForContextAsync(context);
    }

    private async Task RefreshAppliedIndicatorsForContextAsync(ChartRuntimeContext context)
    {
        if (context.IndicatorRefreshRunning || context.AppliedIndicators.Count == 0 || context.Chart.Candles.Count == 0)
            return;

        context.IndicatorRefreshRunning = true;
        context.IndicatorRefreshPending = false;
        context.LastIndicatorRefreshUtc = DateTime.UtcNow;

        int paneId = context.PaneId;
        int identityGeneration = context.IdentityGeneration;
        string symbol = context.Symbol;
        string timeframeKey = context.Timeframe.Key;
        if (!TryCreateSafeIndicatorSnapshot(context, out Candle[] candles, out int candleRevision, out long lastCandleStartUnix))
        {
            context.IndicatorRefreshRunning = false;
            return;
        }
        TickScriptEntry[] entries = context.AppliedIndicators.ToArray();

        try
        {
            Dictionary<string, TickScriptIndicatorResult> results = await Task.Run(() =>
            {
                var store = new TickScriptStore();
                var calculated = new Dictionary<string, TickScriptIndicatorResult>(StringComparer.OrdinalIgnoreCase);
                foreach (TickScriptEntry entry in entries)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    string source = store.LoadSource(entry);
                    calculated[entry.SourcePath] = TickScriptIndicatorRuntime.Evaluate(entry.Name, source, candles);
                }
                return calculated;
            }, _lifetime.Token);

            if (_isClosing || !_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? liveContext) ||
                !ReferenceEquals(liveContext, context) || liveContext.IdentityGeneration != identityGeneration ||
                !string.Equals(liveContext.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveContext.Timeframe.Key, timeframeKey, StringComparison.OrdinalIgnoreCase) ||
                liveContext.CandleRevision != candleRevision ||
                liveContext.Chart.Candles.Count == 0 ||
                liveContext.Chart.Candles[^1].StartUnix != lastCandleStartUnix)
            {
                return;
            }

            foreach (TickScriptEntry entry in entries)
            {
                if (!results.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? result) || result is null)
                    continue;
                liveContext.IndicatorResults[entry.SourcePath] = result;
                TickScriptIndicatorAppearance appearance = GetTickScriptAppearance(liveContext, entry.SourcePath);
                liveContext.IndicatorStack.UpdateResult(entry, result, appearance);
            }
            liveContext.IndicatorStack.SetViewport(liveContext.Chart.CaptureViewportSnapshot());
            if (ReferenceEquals(liveContext, ActiveChartContext))
            {
                ShowIndicatorsForActiveChart();
                RefreshIndicatorsWindowAppliedList();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Indicator calculation failed: {exception.Message}";
        }
        finally
        {
            context.IndicatorRefreshRunning = false;
            if (context.IndicatorRefreshPending && !_isClosing)
            {
                context.IndicatorRefreshPending = false;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => RefreshAppliedIndicatorsForContext(context, force: false)));
            }
        }
    }

    private void OpenIndicatorInEditor(TickScriptEntry entry)
    {
        if (_scriptEditorWindow is null)
        {
            _scriptEditorWindow = new TickScriptEditorWindow { Owner = this };
            _scriptEditorWindow.Closed += (_, _) => _scriptEditorWindow = null;
        }
        _scriptEditorWindow.OpenEntry(entry);
    }

    private IReadOnlyList<double> GetOverlayIndicatorSnapValues(int candleIndex) =>
        GetOverlayIndicatorSnapValues(CandleChart, candleIndex);

    private IReadOnlyList<double> GetOverlayIndicatorSnapValues(CandleChartControl chart, int candleIndex)
    {
        if (candleIndex < 0)
            return Array.Empty<double>();

        ChartRuntimeContext context = FindChartContext(chart);
        IEnumerable<double> custom = context.IndicatorResults.Values
            .Where(result => result.Overlay && candleIndex < result.Values.Count)
            .Select(result => result.Values[candleIndex])
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value);
        return custom
            .Concat(chart.GetBuiltInIndicatorValuesAt(candleIndex))
            .ToArray();
    }

    private void RemoveAppliedIndicator(TickScriptEntry entry) =>
        RemoveAppliedIndicator(ActiveChartContext, entry);

    private void RemoveAppliedIndicator(ChartRuntimeContext context, TickScriptEntry entry)
    {
        context.AppliedIndicators.RemoveAll(item =>
            string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
        context.IndicatorResults.Remove(entry.SourcePath);
        context.IndicatorAppearances.Remove(entry.SourcePath);
        context.IndicatorStack.Remove(entry);
        if (ReferenceEquals(context, ActiveChartContext))
        {
            ShowIndicatorsForActiveChart();
            RefreshIndicatorsWindowAppliedList();
            StatusText.Text = $"Removed {entry.Name} from the active chart.";
        }
        SaveWorkspace();
    }

    private void ToolPartitionBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
            return;

        var menu = new ContextMenu();
        var refreshTool = new MenuItem { Header = "Refresh" };
        refreshTool.Click += (_, _) =>
        {
            if (_appliedIndicatorEntries.Count > 0)
                RefreshAllAppliedIndicators(force: true);
            StatusText.Text = "Attached chart tool refreshed.";
        };
        var refreshAll = new MenuItem { Header = "Refresh TickLab" };
        refreshAll.Click += async (_, _) => await RefreshEntireTickLabAsync();
        menu.Items.Add(refreshTool);
        menu.Items.Add(new Separator());
        menu.Items.Add(refreshAll);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CodeEditorButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCodeEditorPanel(true);
    }

    private void PlannedFeatureButton_Click(object sender, RoutedEventArgs e)
    {
        string feature = (sender as FrameworkElement)?.Tag?.ToString()
                         ?? (sender as ContentControl)?.Content?.ToString()
                         ?? "Feature";
        StatusText.Text = $"{feature} will be enabled after the data core passes stability testing.";
    }

    private async void MakeTimeframeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new TimeframeBuilderWindow { Owner = this };
        if (window.ShowDialog() != true)
            return;

        TimeframeDefinition? selectedTimeframe = window.SelectedTimeframe;
        if (selectedTimeframe is null)
            return;

        TimeframeDefinition created = selectedTimeframe;
        TimeframeDefinition? existing = GetAllTimeframes()
            .FirstOrDefault(item => item.Key == created.Key);

        if (existing is null)
        {
            _customTimeframes.Add(created);
            existing = created;
        }

        await SelectTimeframeForActiveChartAsync(existing);
        SaveTimeframePreferences();
        BuildTimeframeButtons();

        StatusText.Text = $"Generated and opened {existing.DisplayText}.";
    }

    private void NewChartButton_Click(object sender, RoutedEventArgs e)
    {
        CreateWorkspaceChart();
    }

    private void MainChartDockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workspacePages.Count > 0)
            SwitchToWorkspace(_activeWorkspaceId > 0 ? _activeWorkspaceId : _workspacePages.Keys.Min());
    }

    private void CreateDetachedChartWindow()
    {
        CreateWorkspaceChart();
    }

    private void PositionDetachedWindow(Window window)
    {
        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        double offset = 34.0 * ((_detachedChartWindows.Count % 6) + 1);
        double requestedLeft = double.IsFinite(Left) ? Left + offset : virtualLeft + 80 + offset;
        double requestedTop = double.IsFinite(Top) ? Top + offset : virtualTop + 60 + offset;
        window.Left = Math.Clamp(requestedLeft, virtualLeft + 8, Math.Max(virtualLeft + 8, virtualRight - window.Width - 8));
        window.Top = Math.Clamp(requestedTop, virtualTop + 8, Math.Max(virtualTop + 8, virtualBottom - window.Height - 8));
    }

    private void SyncDetachedChartWindows()
    {
        if (_isClosing)
            return;

        SyncWorkspaceChartPanes();
        foreach (DetachedChartWindow chartWindow in _detachedChartWindows.ToArray())
        {
            if (chartWindow.IsLoaded)
                UpdateDetachedChartWindow(chartWindow);
        }
        RefreshWorkspaceTabs();
    }

    private void UpdateDetachedChartWindow(DetachedChartWindow chartWindow)
    {
        chartWindow.UpdateChart(
            _requestedSymbol,
            _activeTimeframe.DisplayText,
            _displayCandles,
            CandleChart.TimelineGaps,
            _candleMarkers,
            CandleChart.DemoTradeLines,
            CandleChart.Settings,
            _selectedConnector?.ServerUtcOffsetMinutes ?? 0,
            CandleChart.NativeHistoryBoundaryUnix,
            CandleChart.HistoryBoundaryLabel);
    }

    private void RefreshChartWindowDock()
    {
        RefreshWorkspaceTabs();
    }

    private void RestoreToolButton_Click(object sender, RoutedEventArgs e) =>
        ShowDockedTool();

    private void MinimizeToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (ToolPartitionRow.ActualHeight > 0)
            _lastToolHeight = ToolPartitionRow.ActualHeight;
        ToolPartitionBorder.Visibility = Visibility.Collapsed;
        ToolPartitionSplitter.Visibility = Visibility.Collapsed;
        ToolPartitionRow.Height = new GridLength(0);
        ToolSplitterRow.Height = new GridLength(0);
        RestoreToolButton.Visibility = Visibility.Visible;
    }

    private void DetachToolButton_Click(object sender, RoutedEventArgs e)
    {
        FrameworkElement? activeTool = DockedToolContent.Content as FrameworkElement;
        if (activeTool is null)
            return;

        string title = string.IsNullOrWhiteSpace(ToolPartitionTitleText.Text)
            ? "Indicator / Tool"
            : ToolPartitionTitleText.Text;
        DockedToolContent.Content = null;
        ToolPartitionBorder.Visibility = Visibility.Collapsed;
        ToolPartitionSplitter.Visibility = Visibility.Collapsed;
        ToolPartitionRow.Height = new GridLength(0);
        ToolSplitterRow.Height = new GridLength(0);
        RestoreToolButton.Visibility = Visibility.Visible;

        int paneId = AllocateLowestPaneId();
        WorkspacePaneKind kind = title.Contains("indicator", StringComparison.OrdinalIgnoreCase)
            ? WorkspacePaneKind.Indicator
            : WorkspacePaneKind.Tool;
        var pane = new WorkspacePaneHandle(paneId, kind, title, activeTool);
        _workspacePaneRegistry[paneId] = pane;
        OpenFloatingPane(pane);
        StatusText.Text = $"Detached {title} into numbered window {paneId}.";
    }

    private void CloseToolButton_Click(object sender, RoutedEventArgs e)
    {
        _detachedHostedToolWindow?.Close();
        _detachedHostedToolWindow = null;
        _detachedToolWindow?.Close();
        _detachedToolWindow = null;
        if (ReferenceEquals(DockedToolContent.Content, _indicatorPaneStack))
            _appliedIndicatorEntries.Clear();
        CloseToolPartition();
    }

    private void CloseToolPartition()
    {
        DockedToolContent.Content = null;
        ToolHeaderRow.Height = new GridLength(36);
        ToolPartitionBorder.Visibility = Visibility.Collapsed;
        ToolPartitionSplitter.Visibility = Visibility.Collapsed;
        ToolPartitionRow.Height = new GridLength(0);
        ToolSplitterRow.Height = new GridLength(0);
        RestoreToolButton.Visibility = Visibility.Collapsed;
    }

    private void DockDetachedTool()
    {
        if (_detachedToolWindow is null)
            return;

        _detachedToolWindow.ReleaseContent();
        _detachedToolWindow.Close();
        _detachedToolWindow = null;
        ShowDockedTool();
    }

    private void ShowDockedTool()
    {
        if (_detachedHostedToolWindow is not null)
        {
            DetachedChartWindow window = _detachedHostedToolWindow;
            FrameworkElement? released = window.ReleaseHostedContent();
            _detachedHostedToolWindow = null;
            if (released is not null)
                DockedToolContent.Content = released;
            window.Close();
        }

        if (_detachedToolWindow is not null)
        {
            DockDetachedTool();
            return;
        }

        ShowIndicatorsForActiveChart();
    }

    private async void LiveTimer_Tick(object? sender, EventArgs e)
    {
        // V300 recording and desktop live chart reading continue during the
        // complete V305 history operation. Only the short final chart-data
        // replacement uses _historyChartLaunchRunning to prevent two UI writers.
        if (_selectedConnector is null || _liveRefreshRunning || _isClosing ||
            _historyChartLaunchRunning)
            return;

        _liveRefreshRunning = true;
        try
        {
            // Replay freezes only its own pane. Every other open price chart must
            // continue receiving the same-symbol bridge stream independently.
            if (!IsReplayChart(_activePricePaneId))
            {
                if (_activeTimeframe.IsRawTickChart)
                    await RefreshRawTickChartLiveAsync(ActiveChartContext);
                else if (_activeTimeframe.UsesTickArchive)
                    await RefreshSecondChartLiveAsync();
                else
                    await RefreshNativeLiveAsync();
            }

            await RefreshAllChartContextsLiveAsync();
            await RefreshAllRawTickContextsLiveAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Live update paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Live chart update",
                    "read_or_render_live_data",
                    "Copy diagnostics. Confirm both bridges are online, then use Refresh.",
                    ErrorCode: "TL-LIVE-UPDATE",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            _liveRefreshRunning = false;
            SyncDetachedChartWindows();
            RefreshMarketWorkspace(throttle: true);
        }
    }

    private async void MaintenanceTimer_Tick(object? sender, EventArgs e)
    {
        if (_maintenanceRunning || _isClosing)
            return;

        _maintenanceRunning = true;
        try
        {
            if (_selectedConnector is null)
            {
                if (DateTime.UtcNow - _lastAutoConnectAttemptUtc >= TimeSpan.FromSeconds(3))
                    await TryReconnectLastConnectorAsync();

                if (_selectedConnector is null)
                    return;
            }

            await RefreshConnectorStateAsync();
            await RefreshSymbolsAsync(force: false);

            if (_selectedConnector is not null &&
                !string.IsNullOrWhiteSpace(_requestedSymbol))
            {
                QueueAutomaticNativeHistoryLoad(
                    _selectedConnector.ConnectorId,
                    _requestedSymbol,
                    _activeTimeframe);
            }

            await RunLiveIntegrityAgentAsync();
            await FlushPendingHistoryWritesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Background maintenance paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Background maintenance",
                    "connector_integrity_or_flush",
                    "Copy diagnostics. Keep MT5 open and verify the bridge folder is writable.",
                    ErrorCode: "TL-CORE-MAINT",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            _maintenanceRunning = false;
        }
    }

    private async Task RunLiveIntegrityAgentAsync()
    {
        if (_liveIntegrityRunning || _selectedConnector is null ||
            !_activeInstrumentSaving || _historyOperationRunning ||
            string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            return;
        }

        if (DateTime.UtcNow - _lastLiveIntegrityCheckUtc < TimeSpan.FromSeconds(30))
            return;

        string connectorId = _selectedConnector.ConnectorId;
        string symbol = _requestedSymbol;
        int offset = _selectedConnector.ServerUtcOffsetMinutes;
        DateTime tickWrite = _bridgeClient.GetTickArchiveLastWriteUtc(connectorId);
        DateTime nativeWrite = _bridgeClient.GetAllNativeClosedLastWriteUtc(connectorId);

        bool ticksChanged = tickWrite > _lastTickIntegrityWriteUtc;
        bool candlesChanged = nativeWrite > _lastNativeClosedIntegrityWriteUtc;
        if (!ticksChanged && !candlesChanged)
            return;

        _liveIntegrityRunning = true;
        _lastLiveIntegrityCheckUtc = DateTime.UtcNow;

        try
        {
            using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            operationTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            CancellationToken operationToken = operationTimeout.Token;

            await using IAsyncDisposable lease = await _dataRangeCoordinator.AcquireAsync(
                connectorId,
                symbol,
                "data",
                operationToken);

            (int SavedCandles, bool TicksSynced) result = await Task.Run(() =>
            {
                bool ticksSynced = false;
                if (ticksChanged)
                {
                    _historyStore.SyncTickArchives(
                        connectorId,
                        symbol,
                        operationToken,
                        includeHistorical: false,
                        serverUtcOffsetMinutes: offset);
                    ticksSynced = true;
                }

                int savedCandles = 0;
                if (candlesChanged)
                {
                    long serverNow = Mt5ServerClock.ServerNowUnix(offset);
                    IReadOnlyList<Candle> closedNative = _bridgeClient
                        .ReadAllNativeClosedCandles(connectorId)
                        .Where(candle =>
                            candle.IsClosed &&
                            candle.EndUnix <= serverNow &&
                            string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                            TimeframeDefinition.NativeMt5Timeframes.Contains(
                                candle.Timeframe,
                                StringComparer.Ordinal))
                        .OrderBy(candle => candle.StartUnix)
                        .ToArray();

                    foreach (IGrouping<string, Candle> timeframeGroup in
                             closedNative.GroupBy(candle => candle.Timeframe, StringComparer.Ordinal))
                    {
                        Candle? latestSaved = _historyStore.ReadCandles(
                                connectorId,
                                symbol,
                                timeframeGroup.Key,
                                HistoryLoadSelection.All,
                                1)
                            .LastOrDefault();

                        foreach (Candle candle in timeframeGroup.OrderBy(item => item.StartUnix))
                        {
                            if (latestSaved is not null && candle.StartUnix < latestSaved.StartUnix)
                                continue;
                            if (latestSaved is not null &&
                                candle.StartUnix == latestSaved.StartUnix &&
                                HistoryIntegrityService.CandleMatches(latestSaved, candle))
                            {
                                continue;
                            }

                            _historyStore.UpsertLiveCandle(connectorId, candle, offset);
                            latestSaved = candle;
                            savedCandles++;
                        }
                    }
                }

                return (savedCandles, ticksSynced);
            }, operationToken);

            if (ticksChanged)
                _lastTickIntegrityWriteUtc = tickWrite;
            if (candlesChanged)
                _lastNativeClosedIntegrityWriteUtc = nativeWrite;

            if (result.SavedCandles > 0)
            {
                StatusText.Text =
                    $"Permanent history updated: {result.SavedCandles:N0} newly closed native timeframe candle(s); live ticks remain synchronized.";
            }
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusText.Text = "Live archive maintenance timed out safely and will retry later.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Permanent history update paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Permanent history integrity",
                    "live_archive_sync",
                    "Copy diagnostics. Do not delete history; retry Refresh History after checking disk access.",
                    ErrorCode: "TL-HIST-LIVE-SYNC",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            _liveIntegrityRunning = false;
        }
    }

    private async Task TryReconnectLastConnectorAsync()
    {
        _lastAutoConnectAttemptUtc = DateTime.UtcNow;

        IReadOnlyList<Mt5ConnectorSummary> connectors = await Task.Run(
            () => _bridgeClient.DiscoverConnectors()
                .Where(item => item.CanConnect)
                .OrderByDescending(item => item.UpdatedUnix)
                .ToArray(),
            _lifetime.Token);

        Mt5ConnectorSummary? connector = null;

        if (!string.IsNullOrWhiteSpace(_preferences.LastConnectorId))
        {
            connector = connectors.FirstOrDefault(item => string.Equals(
                item.ConnectorId,
                _preferences.LastConnectorId,
                StringComparison.Ordinal));
        }

        // First launch, a changed account, or a rebuilt bridge must still
        // auto-connect. Prefer the freshest live V300 heartbeat instead of
        // requiring a connector ID saved by an older TickLab version.
        connector ??= connectors.FirstOrDefault();

        if (connector?.IsCompatibleBridge != true)
        {
            StatusText.Text = "Waiting for a live V300 bridge heartbeat. Auto-connect is active.";
            return;
        }

        await ConnectAsync(connector);
    }

    private async Task ConnectAsync(Mt5ConnectorSummary connector)
    {
        _bridgeClient.ActivateConnector(connector);

        if (!connector.IsCompatibleBridge)
        {
            MessageBox.Show(
                this,
                "Attach the supported TickLab V300 Live and History bridges.",
                "Supported bridge required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _selectedConnector = connector;
        _bridgeWasAvailable = connector.IsConnected;
        _lastHealthyConnectorObservationUtc = connector.IsConnected
            ? DateTime.UtcNow
            : DateTime.MinValue;
        _consecutiveConnectorFailures = 0;
        _preferences = _preferences with { LastConnectorId = connector.ConnectorId };
        ConnectionText.Text = $"MT5 Connected • Live  ·  {connector.DisplayName}";
        BrokerText.Text = $"{connector.Broker}\n{connector.Server}";
        ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(38, 194, 129));
        MarketStateText.Text = "Connected";
        MarketStateText.Foreground = new SolidColorBrush(Color.FromRgb(38, 194, 129));

        await RefreshSymbolsAsync(force: true);

        string symbol = !string.IsNullOrWhiteSpace(_preferences.LastChartSymbol)
            ? _preferences.LastChartSymbol
            : connector.Symbol;

        if (_availableSymbols.Count > 0 && !_availableSymbols.Any(item =>
                string.Equals(item.Name, symbol, StringComparison.OrdinalIgnoreCase)))
        {
            symbol = connector.Symbol;
        }

        await SafeSelectChartAsync(symbol, _activeTimeframe);

        bool hasEnabledHistory = _historyStore.GetSavedInstruments(connector.ConnectorId)
            .Any(item => item.Enabled);
        if (!_startupRefreshQueued && hasEnabledHistory)
        {
            _startupRefreshQueued = true;
            _ = RunStartupRefreshAsync(symbol);
        }
    }

    private async Task RunStartupRefreshAsync(string symbol)
    {
        try
        {
            await Task.Delay(750, _lifetime.Token);
            Mt5ConnectorSummary? connector = _selectedConnector;
            if (connector is null)
                return;

            string connectorId = connector.ConnectorId;
            bool activeEnabled = _historyStore.GetSavedInstruments(connectorId)
                .Any(item => item.Enabled &&
                    string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (!activeEnabled)
                return;

            StatusText.Text = "Checking permanent local history before requesting MT5 data…";
            IReadOnlyList<HistoryImportPhase> phases = await Task.Run(
                () => BuildStartupRepairPhases(connectorId, symbol),
                _lifetime.Token);

            if (phases.Count == 0)
            {
                StatusText.Text = "Permanent history is up to date. No startup import was required.";
                return;
            }

            bool fullRecovery = phases.Any(phase =>
                !phase.MinimumStartUnix.HasValue &&
                (phase.ImportCandles || phase.IncludeTicks));
            string mode = fullRecovery
                ? "Full recovery is required for one or more missing datasets."
                : "Only missing, damaged or recent tail ranges will be checked.";

            StatusText.Text = $"Startup history check prepared. {mode}";
            await ExecuteHistoryOperationAsync(
                "Startup gap check",
                "refresh",
                phases,
                syncTicks: phases.Any(phase => phase.IncludeTicks),
                successMessage: fullRecovery
                    ? $"Startup recovery completed for {symbol}. Existing good history was preserved."
                    : $"Startup gap check completed for {symbol}. Only targeted ranges were merged.",
                operationSymbol: symbol);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Startup history check paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Startup history check",
                    "startup_gap_only_recovery",
                    "Saved history remains unchanged. Retry Refresh History after both bridges are online.",
                    ErrorCode: "TL-HIST-STARTUP-GAP",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private IReadOnlyList<HistoryImportPhase> BuildStartupRepairPhases(
        string connectorId,
        string symbol)
    {
        var phases = new List<HistoryImportPhase>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (string nativeCode in TimeframeDefinition.NativeMt5Timeframes)
        {
            NativeCandleFileSummary? local =
                _historyStore.GetNativeCandleFile(connectorId, symbol, nativeCode);
            long? minimumStart = null;
            string description;
            if (local is null ||
                local.RecordCount <= 0 ||
                !string.Equals(local.Status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                description = "startup recovery for missing or unusable native history";
            }
            else
            {
                TimeframeDefinition timeframe =
                    TimeframeDefinition.FromNativeMt5Code(nativeCode);
                long seconds = Math.Max(60, timeframe.ToApproximateSeconds());
                long overlap = Math.Max(86_400L, checked(seconds * 8L));
                minimumStart = Math.Max(local.EarliestUnix, local.LatestUnix - overlap);
                description = "startup recent-tail comparison; preserve complete local archive";
            }

            phases.Add(new HistoryImportPhase(
                nativeCode,
                false,
                description,
                true,
                minimumStart,
                null));
        }

        // The permanent M1 quarter folders are the source for every generated
        // timeframe. A damaged folder is repaired from its earliest affected
        // boundary instead of restarting the complete archive.
        HistorySegmentSummary[] m1Segments = _historyStore
            .GetSegments(connectorId, symbol, "PERIOD_M1")
            .OrderBy(item => item.EarliestUnix)
            .ToArray();
        HistorySegmentSummary? damagedM1 = m1Segments
            .FirstOrDefault(item =>
                !string.Equals(item.Status, "Healthy", StringComparison.OrdinalIgnoreCase));
        if (damagedM1 is not null)
        {
            phases.RemoveAll(phase =>
                string.Equals(phase.Timeframe, "PERIOD_M1", StringComparison.Ordinal));
            phases.Insert(0, new HistoryImportPhase(
                "PERIOD_M1",
                false,
                "startup targeted repair from first damaged permanent M1 quarter",
                true,
                damagedM1.EarliestUnix > 0 ? damagedM1.EarliestUnix : null,
                null));
        }

        // Catch up raw ticks from a small overlap before the newest permanent
        // M1 candle. The tick archive itself decides whether this is a tail
        // comparison, a targeted damaged-quarter repair, or a valid full
        // recovery because no permanent tick data exists at all.
        TickHistoryFolderSummary[] tickFolders = _historyStore
            .GetTickHistoryFolders(connectorId, symbol)
            .OrderBy(item => item.StartUnix)
            .ToArray();
        TickHistoryFolderSummary? damagedTicks = tickFolders.FirstOrDefault(item =>
            item.SizeBytes <= 0 ||
            !string.Equals(item.Status, "OK", StringComparison.OrdinalIgnoreCase));
        HistoryDatasetSummary? m1Dataset = _historyStore.GetDatasets(connectorId)
            .FirstOrDefault(item =>
                string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Timeframe, "PERIOD_M1", StringComparison.Ordinal));

        long? tickMinimum;
        string tickDescription;
        if (tickFolders.Length == 0)
        {
            tickMinimum = null;
            tickDescription = "startup full raw-tick recovery because permanent tick history is missing";
        }
        else if (damagedTicks is not null)
        {
            tickMinimum = damagedTicks.StartUnix > 0 ? damagedTicks.StartUnix : null;
            tickDescription = "startup targeted raw-tick repair from first damaged quarter";
        }
        else
        {
            tickMinimum = m1Dataset is { RecordCount: > 0, LatestUnix: > 0 }
                ? Math.Max(0, Math.Min(now, m1Dataset.LatestUnix) - 86_400L)
                : Math.Max(0, tickFolders[^1].StartUnix);
            tickDescription = "startup raw-tick tail comparison";
        }

        phases.Add(new HistoryImportPhase(
            "PERIOD_M1",
            true,
            tickDescription,
            true,
            tickMinimum,
            null,
            ImportCandles: false,
            ProgressLabel: "RAW_TICKS"));

        return phases;
    }

    private async Task SafeSelectChartAsync(
        string symbol,
        TimeframeDefinition timeframe)
    {
        if (IsReplayChart(_activePricePaneId))
            StopReplay(restoreChart: true);

        try
        {
            await SelectChartAsync(symbol, timeframe);
        }
        catch (OperationCanceledException)
        {
            // A newer symbol/timeframe selection superseded this one.
        }
    }

    private async Task SelectChartAsync(
        string symbol,
        TimeframeDefinition timeframe)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(symbol))
            return;

        _chartNavigationCancellation?.Cancel();
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token = _selectionCancellation.Token;
        int generation = ++_selectionGeneration;

        _requestedSymbol = symbol.Trim();
        ChartRuntimeContext activeContext = ActiveChartContext;
        if (!string.IsNullOrWhiteSpace(activeContext.HistoricalNavigationAnchorSymbol) &&
            !string.Equals(
                activeContext.HistoricalNavigationAnchorSymbol,
                _requestedSymbol,
                StringComparison.OrdinalIgnoreCase))
        {
            activeContext.HistoricalNavigationAnchorUnix = null;
            activeContext.HistoricalNavigationAnchorSymbol = string.Empty;
            activeContext.Chart.HistoricalNavigationAnchorUnix = null;
        }
        _activeInstrumentSaving = _historyStore.IsInstrumentSaving(
            _selectedConnector.ConnectorId,
            _requestedSymbol);
        _activeTimeframe = timeframe;
        _sourceTimeframe = timeframe.SourceMt5Code;
        ActiveChartContext.Symbol = _requestedSymbol;
        ActiveChartContext.Timeframe = timeframe;
        if (!timeframe.IsRawTickChart)
            ActiveChartContext.LastCandleTimeframe = timeframe;
        SetRawTickMode(ActiveChartContext, timeframe.IsRawTickChart);
        _lastLiveWriteUtc = DateTime.MinValue;
        _lastChartBootstrapWriteUtc = DateTime.MinValue;
        _lastClosedWriteUtc = DateTime.MinValue;
        _lastTickArchiveWriteUtc = DateTime.MinValue;
        _lastRecentSecondsWriteUtc = DateTime.MinValue;
        _lastLiveSecondWriteUtc = DateTime.MinValue;
        _lastClosedSecondWriteUtc = DateTime.MinValue;
        _lastMultiChartLiveSecondWriteUtc = DateTime.MinValue;
        _lastMultiChartClosedSecondWriteUtc = DateTime.MinValue;
        _lastMultiChartLiveNativeWriteUtc = DateTime.MinValue;
        _lastMultiChartClosedNativeWriteUtc = DateTime.MinValue;
        _selectedCandle = null;
        _allOlderHistoryLoaded = false;
        _allNewerHistoryLoaded = true;
        CandleChart.CanRequestOlderHistory = false;
        CandleChart.CanRequestNewerHistory = false;

        SetChartIdentityUi();
        SaveTimeframePreferences();
        BuildTimeframeButtons();

        CandleChart.ClearSelection();
        CandleChart.Candles = Array.Empty<Candle>();
        SyncDetachedChartWindows();
        _sourceCandles = new List<Candle>();
        _displayCandles = new List<Candle>();
        CandleCountText.Text = "0";
        StatusText.Text = $"Opening saved {_requestedSymbol} {_activeTimeframe.DisplayText} history...";

        Task localLoad = timeframe.IsRawTickChart
            ? LoadRawTickChartAsync(ActiveChartContext, resetViewport: true, cancellationToken: token)
            : LoadLocalChartAsync(
                generation,
                _requestedSymbol,
                timeframe,
                _historySelection,
                token,
                GetChartLaunchPreviewRecords(timeframe));

        bool attached = string.Equals(
                            _selectedConnector.Symbol,
                            _requestedSymbol,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            NormalizeTimeframe(_selectedConnector.Timeframe),
                            _sourceTimeframe,
                            StringComparison.Ordinal);

        if (!attached)
        {
            _bridgeClient.SendChartSelectionRequest(
                _selectedConnector.ConnectorId,
                _requestedSymbol,
                _sourceTimeframe);

            StatusText.Text = $"Changing MT5 data source to {_requestedSymbol} {_sourceTimeframe}...";
            await WaitForChartSelectionAsync(generation, token);
        }

        await localLoad;

        if (timeframe.IsRawTickChart)
        {
            await RefreshRawTickChartLiveAsync(ActiveChartContext, force: true);
        }
        else if (timeframe.UsesTickArchive)
        {
            await RefreshSecondChartLiveAsync();
        }
        else
        {
            await MergeNativeBootstrapAsync(generation, _requestedSymbol, timeframe, token);
            await RefreshNativeLiveAsync(force: true);
            QueueAutomaticNativeHistoryLoad(
                _selectedConnector.ConnectorId,
                _requestedSymbol,
                timeframe);
        }

        _preferences = _preferences with
        {
            LastChartSymbol = _requestedSymbol,
            LastChartTimeframe = _activeTimeframe.DisplayText,
            LastActiveTimeframeKey = _activeTimeframe.Key
        };
        SaveWorkspace();
    }

    private async Task WaitForChartSelectionAsync(
        int generation,
        CancellationToken token)
    {
        if (_selectedConnector is null)
            return;

        string connectorId = _selectedConnector.ConnectorId;
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            if (token.IsCancellationRequested || generation != _selectionGeneration)
                return;

            // A superseded chart selection is normal. Delay and connector
            // polling stop cooperatively so no cancellation exception reaches
            // Visual Studio's first-chance exception debugger.
            await Task.Delay(150);
            if (token.IsCancellationRequested || generation != _selectionGeneration)
                return;

            Mt5ConnectorSummary? current = await Task.Run(
                () => _bridgeClient.FindConnector(connectorId),
                CancellationToken.None);

            if (token.IsCancellationRequested || generation != _selectionGeneration)
                return;

            if (current is not null)
                _selectedConnector = current;

            if (current is not null &&
                string.Equals(current.Symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeTimeframe(current.Timeframe), _sourceTimeframe, StringComparison.Ordinal))
            {
                StatusText.Text = $"MT5 source changed to {_requestedSymbol} {_sourceTimeframe}.";
                return;
            }
        }

        StatusText.Text = "MT5 did not confirm the chart change. The saved chart remains usable.";
    }

    private async Task LoadLocalChartAsync(
        int generation,
        string symbol,
        TimeframeDefinition timeframe,
        HistoryLoadSelection selection,
        CancellationToken token,
        int maximumRecords = MaximumChartBufferRecords)
    {
        if (_selectedConnector is null ||
            token.IsCancellationRequested ||
            generation != _selectionGeneration)
        {
            return;
        }

        string connectorId = _selectedConnector.ConnectorId;
        Mt5ConnectorSummary connector = _selectedConnector;

        // Chart-selection cancellation is routine. Do not give Task.Run the
        // token because a token canceled before scheduling creates a canceled
        // Task and Visual Studio reports it as OperationCanceledException.
        // The archive reader below polls the token cooperatively instead.
        // Seconds are candle charts, not bulk tick-history viewers. Keep every
        // full reload (launch, Refresh, End, history visibility changes) on the
        // same bounded virtual-page contract used by the normal candle chart.
        // Older/newer navigation grows only the rolling virtual buffer.
        int effectiveMaximumRecords = timeframe.Unit == TimeframeUnit.Second
            ? Math.Min(Math.Max(1, maximumRecords), SecondChartLaunchPreviewRecords)
            : Math.Max(1, maximumRecords);

        LocalChartResult result = await Task.Run(
            () => BuildLocalChartResult(
                connectorId,
                connector,
                symbol,
                timeframe,
                selection,
                effectiveMaximumRecords,
                beforeUnix: null,
                token: token),
            CancellationToken.None);

        if (generation != _selectionGeneration || token.IsCancellationRequested)
            return;

        // Source and display are separate mutable owners. Never retain a shared
        // list returned by a fast history path; a later source clear/repair must
        // not erase the visible chart history.
        _sourceCandles = result.Source.ToList();
        _displayCandles = result.Display.ToList();
        _allOlderHistoryLoaded =
            _displayCandles.Count == 0 ||
            (result.BoundaryUnix.HasValue &&
             _displayCandles[0].StartUnix <= result.BoundaryUnix.Value);
        _allNewerHistoryLoaded = true;
        CommitLoadedHistoryToActiveContext();
        CandleChart.TimelineGaps = BuildChartTimelineGaps(
            connectorId,
            symbol,
            timeframe);
        CandleChart.NativeHistoryBoundaryUnix = result.BoundaryUnix;
        CandleChart.HistoryBoundaryLabel = result.BoundaryLabel;
        CandleChart.CompleteOlderHistoryRequest();
        CandleChart.CompleteNewerHistoryRequest();
        UpdateChartPagingAvailability();

        if (_displayCandles.Count > 0)
        {
            CandleChart.Candles = _displayCandles;
            SyncDetachedChartWindows();
            CandleChart.RestoreViewport(_preferences.Viewport);
            UpdateChartUi(result.Description);
        }
        else
        {
            // Never leave a previously selected timeframe visible after the
            // active history source has been hidden or deleted.
            CandleChart.Candles = Array.Empty<Candle>();
            SyncDetachedChartWindows();
            StatusText.Text = result.Description.Contains("hidden", StringComparison.OrdinalIgnoreCase)
                ? result.Description
                : $"No saved {_requestedSymbol} {_activeTimeframe.DisplayText} history. Use MT5 Connections → Import History.";
            PriceChangeText.Text = "Live data will appear when MT5 is connected.";
        }
    }

    private async Task ReloadActiveChartAfterHistoryVisibilityChangeAsync()
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
            return;

        StatusText.Text = "Applying saved-history display settings...";
        await LoadLocalChartAsync(
            _selectionGeneration,
            _requestedSymbol,
            _activeTimeframe,
            _historySelection,
            _lifetime.Token);
    }

    private IReadOnlyList<ChartTimelineGap> BuildChartTimelineGaps(
        string connectorId,
        string symbol,
        TimeframeDefinition timeframe)
    {
        if (!timeframe.UsesTickArchive)
            return Array.Empty<ChartTimelineGap>();

        long duration = Math.Max(1, timeframe.ToApproximateSeconds());
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gaps = new List<ChartTimelineGap>();
        foreach (HiddenHistoryRange range in _historyStore.GetHiddenTickHistoryRanges(connectorId, symbol))
        {
            long endUnix = Math.Min(range.EndUnix, nowUnix);
            if (endUnix <= range.StartUnix)
                continue;

            long slots = (endUnix - range.StartUnix + duration - 1) / duration;
            int slotCount = (int)Math.Clamp(slots, 1, 1_000_000_000L);
            string label = $"Hidden {DateTimeOffset.FromUnixTimeSeconds(range.StartUnix):yyyy-MM-dd} → {DateTimeOffset.FromUnixTimeSeconds(endUnix - 1):yyyy-MM-dd}";
            gaps.Add(new ChartTimelineGap(
                range.StartUnix,
                endUnix,
                slotCount,
                label));
        }
        return gaps;
    }

    private static bool IsInsideHiddenHistoryRange(
        long startUnix,
        IReadOnlyList<HiddenHistoryRange> ranges)
    {
        foreach (HiddenHistoryRange range in ranges)
        {
            if (startUnix < range.StartUnix)
                return false;
            if (startUnix < range.EndUnix)
                return true;
        }
        return false;
    }

    private (long? EarliestUnix, long? LatestUnix) GetSavedSecondChartBoundaries(
        string connectorId,
        string symbol,
        CancellationToken cancellationToken = default,
        bool includeBridgeHistoricalSourceCoverage = true)
    {
        long? earliest = null;
        long? latest = null;
        IReadOnlyList<HiddenHistoryRange> hiddenRanges =
            _historyStore.GetHiddenTickHistoryRanges(connectorId, symbol);

        // Canonical raw ticks are the authoritative permanent source for every
        // second chart. Coverage reads the actual first/last stored tick rather
        // than the first candle of the currently requested 1,600-candle page.
        CanonicalTickCoverage permanentCoverage =
            _historyStore.GetTickCoverageForReplay(connectorId, symbol);
        if (permanentCoverage.HasData)
        {
            long permanentEarliest = permanentCoverage.EarliestTimeMilliseconds / 1000L;
            long permanentLatest = permanentCoverage.LatestTimeMilliseconds / 1000L;
            if (!IsInsideHiddenHistoryRange(permanentEarliest, hiddenRanges))
                earliest = permanentEarliest;
            if (!IsInsideHiddenHistoryRange(permanentLatest, hiddenRanges))
                latest = permanentLatest;
        }

        // Historical bridge tick snapshots may exist before they have been
        // indexed into ticks.tlt. Normal launch/paging can include their global
        // filename-level coverage. A focused Find Candle request must NOT do a
        // recursive full-history coverage scan after it already found the target;
        // that old boundary lookup was enough to turn a direct one-month lookup
        // back into a multi-minute operation.
        if (includeBridgeHistoricalSourceCoverage)
        {
            CanonicalTickCoverage bridgeCoverage =
                _historyStore.GetBridgeHistoricalTickSourceCoverage(connectorId, symbol, cancellationToken);
            if (bridgeCoverage.HasData)
            {
                long bridgeEarliest = bridgeCoverage.EarliestTimeMilliseconds / 1000L;
                long bridgeLatest = bridgeCoverage.LatestTimeMilliseconds / 1000L;
                if (!IsInsideHiddenHistoryRange(bridgeEarliest, hiddenRanges))
                {
                    earliest = !earliest.HasValue
                        ? bridgeEarliest
                        : Math.Min(earliest.Value, bridgeEarliest);
                }
                if (!IsInsideHiddenHistoryRange(bridgeLatest, hiddenRanges))
                {
                    latest = !latest.HasValue
                        ? bridgeLatest
                        : Math.Max(latest.Value, bridgeLatest);
                }
            }
        }

        // External raw-tick datasets participate in the same second-candle
        // projection path, so include their real manifest coverage as well.
        foreach (ExternalDatasetManifest dataset in
                 _externalHistoryStore.GetDatasets(connectorId, symbol))
        {
            if (!dataset.Enabled ||
                dataset.Kind != ExternalDataKind.RawTicks ||
                dataset.AcceptedRecords <= 0)
            {
                continue;
            }

            if (dataset.EarliestUnix > 0 &&
                !IsInsideHiddenHistoryRange(dataset.EarliestUnix, hiddenRanges))
            {
                earliest = !earliest.HasValue
                    ? dataset.EarliestUnix
                    : Math.Min(earliest.Value, dataset.EarliestUnix);
            }
            if (dataset.LatestUnix > 0 &&
                !IsInsideHiddenHistoryRange(dataset.LatestUnix, hiddenRanges))
            {
                latest = !latest.HasValue
                    ? dataset.LatestUnix
                    : Math.Max(latest.Value, dataset.LatestUnix);
            }
        }

        return (earliest, latest);
    }

    private LocalChartResult BuildLocalChartResult(
        string connectorId,
        Mt5ConnectorSummary connector,
        string symbol,
        TimeframeDefinition timeframe,
        HistoryLoadSelection selection,
        int maximumRecords,
        long? beforeUnix,
        CancellationToken token,
        long? focusUnix = null)
    {
        if (token.IsCancellationRequested)
        {
            return new LocalChartResult(
                new List<Candle>(),
                new List<Candle>(),
                "Chart load canceled by a newer request.",
                null,
                string.Empty);
        }

        if (timeframe.UsesTickArchive)
        {
            IReadOnlyList<HiddenHistoryRange> hiddenRanges =
                _historyStore.GetHiddenTickHistoryRanges(connectorId, symbol);
            IReadOnlySet<string> hiddenKeys = hiddenRanges
                .Select(item => item.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<Candle> externalSeconds = _externalHistoryStore.ReadSecondCandles(
                    connectorId, symbol, timeframe, maximumRecords, beforeUnix)
                .Where(candle => !IsInsideHiddenHistoryRange(candle.StartUnix, hiddenRanges))
                .ToArray();
            IReadOnlyList<Candle> savedSeconds = _historyStore.ReadSecondCandlesOnDemand(
                connectorId,
                symbol,
                connector.Digits,
                connector.Point,
                timeframe,
                maximumRecords,
                connector.ServerUtcOffsetMinutes,
                token,
                beforeUnix,
                hiddenKeys,
                focusUnix);

            List<Candle> seconds = HistoryIntegrityService
                .MergeWithPriority(externalSeconds, savedSeconds)
                .Where(candle => !IsInsideHiddenHistoryRange(candle.StartUnix, hiddenRanges))
                .TakeLast(maximumRecords)
                .ToList();
            // Do NOT use the first candle of this requested page as the archive
            // boundary. That made every 1s/15s/30s/45s launch immediately claim
            // "all older history loaded". Resolve the real raw-tick coverage on
            // the initial page; subsequent pages keep using the boundary already
            // stored on the chart.
            // Resolve the actual oldest stored raw-tick boundary on every
            // seconds-page request. Older virtual pages must never lose the
            // archive boundary merely because they are not the launch page.
            long? savedBoundary =
                GetSavedSecondChartBoundaries(
                    connectorId,
                    symbol,
                    token,
                    includeBridgeHistoricalSourceCoverage: !focusUnix.HasValue).EarliestUnix;

            return new LocalChartResult(
                seconds.ToList(),
                seconds.ToList(),
                savedSeconds.Count > 0
                    ? externalSeconds.Count > 0
                        ? "saved MT5 ticks with external tick fallback"
                        : "saved MT5 ticks — generated on demand"
                    : externalSeconds.Count > 0
                        ? "external raw ticks — generated on demand"
                        : "second history unavailable because raw ticks are missing",
                savedBoundary,
                "Saved MT5 tick history begins here");
        }

        string? requestedNativeCode = timeframe.NativeMt5Code;
        if (!string.IsNullOrWhiteSpace(requestedNativeCode) &&
            !_historyStore.IsNativeCandleVisible(connectorId, symbol, requestedNativeCode))
        {
            return new LocalChartResult(
                new List<Candle>(),
                new List<Candle>(),
                $"{timeframe.DisplayText} candle history is hidden in All TF Candle History",
                null,
                "Hidden candle history");
        }

        string? nativeCode = requestedNativeCode;
        IReadOnlyList<Candle> permanentNative = string.IsNullOrWhiteSpace(nativeCode)
            ? Array.Empty<Candle>()
            : beforeUnix.HasValue
                ? _historyStore.ReadCandlesBefore(
                    connectorId, symbol, nativeCode!, beforeUnix.Value, HistoryLoadSelection.All, maximumRecords)
                : _historyStore.ReadCandles(
                    connectorId, symbol, nativeCode!, HistoryLoadSelection.All, maximumRecords);
        IReadOnlyList<Candle> legacyTemporaryNative = string.IsNullOrWhiteSpace(nativeCode)
            ? Array.Empty<Candle>()
            : beforeUnix.HasValue
                ? _temporaryHistoryStore.ReadCandlesBefore(
                    connectorId, symbol, nativeCode!, beforeUnix.Value, maximumRecords)
                : _temporaryHistoryStore.ReadCandles(
                    connectorId, symbol, nativeCode!, maximumRecords);
        IReadOnlyList<Candle> native = HistoryIntegrityService.MergeWithPriority(
            legacyTemporaryNative,
            permanentNative);
        long? archiveNativeBoundary = string.IsNullOrWhiteSpace(nativeCode)
            ? null
            : _historyStore.GetNativeCandleFile(connectorId, symbol, nativeCode!)
                ?.EarliestUnix;

        // Fast path: an exact native MT5 page completely satisfies the active
        // virtual chart window. Do not read or aggregate a much larger M1
        // fallback in this common case. This is the main MT5-style performance
        // rule: read only the indexed page that can actually be displayed.
        if (!string.IsNullOrWhiteSpace(nativeCode) &&
            native.Count >= maximumRecords)
        {
            long? nativeBoundary = archiveNativeBoundary ??
                HistoryIntegrityService.FindFirstNativeBoundary(native);
            List<Candle> exactPage = native.TakeLast(maximumRecords).ToList();
            return new LocalChartResult(
                exactPage.ToList(),
                exactPage.ToList(),
                "exact native MT5 virtual chart page",
                nativeBoundary,
                $"Native MT5 {timeframe.DisplayText} history begins here");
        }

        bool m1Visible = _historyStore.IsNativeCandleVisible(
            connectorId,
            symbol,
            "PERIOD_M1");

        // A larger chart needs many more M1 source records than displayed
        // target candles. The previous one-to-one limit (300,000 M1 rows for
        // 300,000 H1 rows) silently restricted H1 reconstruction to only a
        // few months. Scale the source read by the target/source ratio while
        // keeping a bounded memory ceiling.
        int m1SourceLimit = CalculateM1FallbackRecordLimit(timeframe, maximumRecords);
        IReadOnlyList<Candle> externalM1 = m1Visible
            ? _externalHistoryStore.ReadM1Candles(
                connectorId, symbol, m1SourceLimit, beforeUnix)
            : Array.Empty<Candle>();
        // Generated/custom timeframes must page across the complete permanent M1
        // archive. The History display filter is a file-management preference and
        // must never silently cap M45 or another generated chart to three months.
        HistoryLoadSelection m1Selection = string.IsNullOrWhiteSpace(nativeCode)
            ? HistoryLoadSelection.All
            : selection;
        IReadOnlyList<Candle> savedM1 = !m1Visible
            ? Array.Empty<Candle>()
            : beforeUnix.HasValue
                ? _historyStore.ReadCandlesBefore(
                    connectorId, symbol, "PERIOD_M1", beforeUnix.Value, m1Selection, m1SourceLimit)
                : _historyStore.ReadCandles(
                    connectorId, symbol, "PERIOD_M1", m1Selection, m1SourceLimit);
        long? m1ArchiveBoundary = _historyStore
            .GetNativeCandleFile(connectorId, symbol, "PERIOD_M1")
            ?.EarliestUnix;

        List<Candle> combinedM1 = HistoryIntegrityService
            .MergeWithPriority(externalM1, savedM1)
            .TakeLast(m1SourceLimit)
            .ToList();

        if (timeframe.Unit == TimeframeUnit.Minute && timeframe.Quantity == 1)
        {
            List<Candle> displayM1 = HistoryIntegrityService
                .MergeWithPriority(combinedM1, native)
                .TakeLast(maximumRecords)
                .ToList();
            long? nativeM1Boundary = archiveNativeBoundary ??
                HistoryIntegrityService.FindFirstNativeBoundary(native);

            return new LocalChartResult(
                native.ToList(),
                displayM1,
                native.Count > 0
                    ? combinedM1.Count > 0
                        ? "native MT5 M1 with permanent/external M1 fallback"
                        : "native MT5 M1 permanent candle history"
                    : savedM1.Count > 0
                        ? externalM1.Count > 0
                            ? "permanent native M1 with external M1 fallback"
                            : "permanent native M1 history"
                        : externalM1.Count > 0
                            ? "external M1 history"
                            : "M1 history unavailable",
                nativeM1Boundary,
                "Native MT5 M1 history begins here");
        }

        List<Candle> generated = CandleAggregator
            .Aggregate(combinedM1, timeframe, connector.ServerUtcOffsetMinutes)
            .Where(candle => !beforeUnix.HasValue || candle.StartUnix < beforeUnix.Value)
            .TakeLast(maximumRecords)
            .ToList();

        RemovePartialLeadingBucket(
            generated,
            combinedM1,
            timeframe,
            connector.ServerUtcOffsetMinutes);

        if (!string.IsNullOrWhiteSpace(nativeCode))
        {
            List<Candle> displayNative = HistoryIntegrityService
                .MergeTimeframeWithNativePriority(
                    generated,
                    native,
                    timeframe,
                    connector.ServerUtcOffsetMinutes)
                .TakeLast(maximumRecords)
                .ToList();
            long? nativeBoundary = archiveNativeBoundary ??
                HistoryIntegrityService.FindFirstNativeBoundary(native);

            return new LocalChartResult(
                native.ToList(),
                displayNative,
                native.Count > 0
                    ? generated.Count > 0
                        ? "native MT5 history with permanent/external M1 fallback"
                        : "native MT5 permanent history"
                    : generated.Count > 0
                        ? "generated temporarily from permanent/external M1"
                        : "history unavailable",
                nativeBoundary,
                "Native MT5 history begins here");
        }

        return new LocalChartResult(
            combinedM1,
            generated,
            generated.Count > 0
                ? "synthetic timeframe generated from the complete indexed native M1 stream"
                : "history unavailable",
            m1ArchiveBoundary ?? (combinedM1.Count > 0 ? combinedM1[0].StartUnix : null),
            "Permanent native M1 history begins here");
    }

    private static int CalculateM1FallbackRecordLimit(
        TimeframeDefinition target,
        int maximumTargetRecords)
    {
        maximumTargetRecords = Math.Max(1, maximumTargetRecords);
        if (target.Unit == TimeframeUnit.Minute && target.Quantity == 1)
            return maximumTargetRecords;

        long targetSeconds = Math.Max(60L, target.ToApproximateSeconds());
        long ratio = Math.Max(1L, (targetSeconds + 59L) / 60L);
        long required = checked((long)maximumTargetRecords * ratio + ratio * 4L);

        // Roughly seven years of continuously traded M1 data. Larger ranges
        // continue through the existing older-history paging mechanism.
        const int maximumM1SourceRecords = 2_500_000;
        return (int)Math.Clamp(required, maximumTargetRecords, maximumM1SourceRecords);
    }

    private static void RemovePartialLeadingBucket(
        List<Candle> generated,
        IReadOnlyList<Candle> source,
        TimeframeDefinition timeframe,
        int serverUtcOffsetMinutes)
    {
        if (generated.Count == 0 || source.Count == 0)
            return;

        long expectedBucketStart = timeframe.GetBucketStartUnix(
            source[0].StartUnix,
            serverUtcOffsetMinutes);

        if (source[0].StartUnix > expectedBucketStart &&
            generated[0].StartUnix == expectedBucketStart)
        {
            generated.RemoveAt(0);
        }
    }

    private void UpdateChartPagingAvailability()
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.DisplayCandles = _displayCandles.ToList();
        context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
        context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        UpdateChartPagingAvailability(context);
    }

    private void UpdateChartPagingAvailability(ChartRuntimeContext context)
    {
        bool hasChart = (context.DisplayCandles.Count > 0 || context.Chart.Candles.Count > 0) && !_isClosing;
        bool active = ReferenceEquals(context, ActiveChartContext);
        context.Chart.CanRequestOlderHistory =
            hasChart && !context.AllOlderHistoryLoaded && !context.OlderHistoryLoadRunning &&
            !(active && _goToEarliestLoadRunning);
        context.Chart.CanRequestNewerHistory =
            hasChart && !context.AllNewerHistoryLoaded && !context.NewerHistoryLoadRunning &&
            !(active && _goToLatestLoadRunning);
    }

    private (long? EarliestUnix, long? LatestUnix) GetSavedChartBoundaries(
        string connectorId,
        string symbol,
        TimeframeDefinition timeframe) =>
        GetSavedChartBoundaries(connectorId, symbol, timeframe, CandleChart);

    private (long? EarliestUnix, long? LatestUnix) GetSavedChartBoundaries(
        string connectorId,
        string symbol,
        TimeframeDefinition timeframe,
        CandleChartControl chart)
    {
        if (string.IsNullOrWhiteSpace(timeframe.NativeMt5Code))
            return (chart.NativeHistoryBoundaryUnix, null);

        NativeCandleFileSummary? summary = _historyStore.GetNativeCandleFile(
            connectorId,
            symbol,
            timeframe.NativeMt5Code!);

        return summary is null
            ? (chart.NativeHistoryBoundaryUnix, null)
            : (summary.EarliestUnix, summary.LatestUnix);
    }

    private static long CalculateForwardSearchBoundary(
        long startUnix,
        TimeframeDefinition timeframe,
        int recordCount)
    {
        long duration = Math.Max(1L, timeframe.ToApproximateSeconds());
        long distance;
        try
        {
            distance = checked(duration * Math.Max(1L, recordCount));
        }
        catch (OverflowException)
        {
            distance = long.MaxValue / 4;
        }

        return startUnix > long.MaxValue - distance
            ? long.MaxValue
            : startUnix + distance;
    }

    private static (int RemovedLeft, int RemovedRight) TrimVirtualWindowAroundAnchor(
        List<Candle> candles,
        int maximumRecords,
        ChartWindowAnchor? anchor,
        bool preferRight)
    {
        int excess = Math.Max(0, candles.Count - Math.Max(1, maximumRecords));
        if (excess == 0)
            return (0, 0);

        int anchorIndex = anchor is null
            ? (preferRight ? 0 : candles.Count - 1)
            : candles.FindIndex(candle => candle.StartUnix == anchor.StartUnix);
        if (anchorIndex < 0)
            anchorIndex = preferRight ? 0 : candles.Count - 1;

        int availableLeft = anchorIndex;
        int availableRight = candles.Count - anchorIndex - 1;
        int removedLeft = 0;
        int removedRight = 0;

        if (preferRight)
        {
            removedRight = Math.Min(excess, availableRight);
            removedLeft = Math.Min(excess - removedRight, availableLeft);
        }
        else
        {
            removedLeft = Math.Min(excess, availableLeft);
            removedRight = Math.Min(excess - removedLeft, availableRight);
        }

        if (removedRight > 0)
            candles.RemoveRange(candles.Count - removedRight, removedRight);
        if (removedLeft > 0)
            candles.RemoveRange(0, removedLeft);

        return (removedLeft, removedRight);
    }

    private (int Generation, CancellationToken Token) BeginChartNavigation()
    {
        CancellationTokenSource? previous = _chartNavigationCancellation;
        _chartNavigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        previous?.Cancel();
        previous?.Dispose();
        _goToEarliestLoadRunning = false;
        _goToLatestLoadRunning = false;
        return (++_chartNavigationGeneration, _chartNavigationCancellation.Token);
    }

    private async void CandleChart_OlderHistoryRequested(
        object? sender,
        EventArgs e)
    {
        CandleChartControl chart = sender as CandleChartControl ?? CandleChart;
        ChartRuntimeContext context = FindChartContext(chart);
        if (IsReplayChart(context.PaneId))
        {
            chart.CompleteOlderHistoryRequest();
            UpdateChartPagingAvailability(context);
            StatusText.Text = "Replay chart is isolated. End replay before loading older candles.";
            return;
        }
        if (ReferenceEquals(context, ActiveChartContext))
        {
            context.DisplayCandles = _displayCandles.ToList();
            context.SourceCandles = _sourceCandles.ToList();
            context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
            context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        }
        if (context.DisplayCandles.Count == 0 && chart.Candles.Count > 0)
            context.DisplayCandles = chart.Candles.ToList();

        if (context.AllOlderHistoryLoaded || _selectedConnector is null ||
            context.DisplayCandles.Count == 0 || _isClosing)
        {
            chart.CompleteOlderHistoryRequest();
            UpdateChartPagingAvailability(context);
            return;
        }

        // Cancel the previous same-direction request, but let its own finally block
        // dispose its source after the background read has completely unwound.
        context.OlderHistoryLoadCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        context.OlderHistoryLoadCancellation = cancellation;
        int requestGeneration = ++context.OlderHistoryLoadGeneration;
        int identityGeneration = context.IdentityGeneration;
        context.OlderHistoryLoadRunning = true;
        UpdateChartPagingAvailability(context);

        Mt5ConnectorSummary connector = _selectedConnector;
        string connectorId = connector.ConnectorId;
        string symbol = context.Symbol;
        TimeframeDefinition timeframe = context.Timeframe;
        HistoryLoadSelection selection = _historySelection;
        long beforeUnix = context.DisplayCandles[0].StartUnix;
        int pageSize = timeframe.Unit == TimeframeUnit.Second && timeframe.Quantity == 1
            ? SecondChartHistoryPageRecords
            : ChartWindowPageRecords;

        try
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Preloading older {symbol} {timeframe.DisplayText} candles...";

            LocalChartResult page = await Task.Run(
                () => BuildLocalChartResult(
                    connectorId,
                    connector,
                    symbol,
                    timeframe,
                    selection,
                    pageSize,
                    beforeUnix,
                    cancellation.Token),
                cancellation.Token);

            if (_isClosing || cancellation.IsCancellationRequested ||
                !_chartContexts.TryGetValue(context.PaneId, out ChartRuntimeContext? liveContext) ||
                !ReferenceEquals(liveContext, context) ||
                liveContext.IdentityGeneration != identityGeneration ||
                liveContext.OlderHistoryLoadGeneration != requestGeneration ||
                !string.Equals(liveContext.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveContext.Timeframe.Key, timeframe.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<Candle> olderDisplay = page.Display
                .Where(candle => candle.StartUnix < beforeUnix)
                .TakeLast(pageSize)
                .ToList();
            if (olderDisplay.Count == 0)
            {
                long? actualBoundary =
                    page.BoundaryUnix ?? chart.NativeHistoryBoundaryUnix;

                // For seconds charts, an empty page is not proof that history
                // ended. Raw-tick snapshots can live in another connector root,
                // can still be waiting for on-demand indexing, or can contain a
                // market-closure gap. Only declare the beginning of history when
                // the current cursor has actually reached the oldest stored tick.
                bool reachedActualBeginning =
                    timeframe.Unit != TimeframeUnit.Second ||
                    !actualBoundary.HasValue ||
                    beforeUnix <= actualBoundary.Value;

                context.AllOlderHistoryLoaded = reachedActualBeginning;
                if (ReferenceEquals(context, ActiveChartContext))
                {
                    _allOlderHistoryLoaded = reachedActualBeginning;
                    StatusText.Text = reachedActualBeginning
                        ? "Beginning of the selected saved history reached."
                        : "Older raw ticks still exist. Continue scrolling left to load the next seconds page.";
                }
                return;
            }

            ChartWindowAnchor? anchor = chart.CaptureWindowAnchor();
            List<Candle> merged = HistoryIntegrityService
                .MergeWithPriority(olderDisplay, context.DisplayCandles)
                .ToList();

            (int removedFromLeft, int removedFromRight) = TrimVirtualWindowAroundAnchor(
                merged,
                ChartWindowMaximumRecords,
                anchor,
                preferRight: true);
            if (removedFromRight > 0)
                context.AllNewerHistoryLoaded = false;
            if (removedFromLeft > 0)
                context.AllOlderHistoryLoaded = false;

            context.DisplayCandles = merged;
            if (!chart.NativeHistoryBoundaryUnix.HasValue && page.BoundaryUnix.HasValue)
            {
                chart.NativeHistoryBoundaryUnix = page.BoundaryUnix;
                chart.HistoryBoundaryLabel = page.BoundaryLabel;
            }

            context.AllOlderHistoryLoaded =
                removedFromLeft == 0 && page.BoundaryUnix.HasValue &&
                context.DisplayCandles[0].StartUnix <= page.BoundaryUnix.Value;

            chart.ReplaceDataPreservingAnchor(context.DisplayCandles, anchor);
            RefreshAppliedIndicatorsForContext(context, force: true);
            RefreshBuiltInIndicatorsForContext(context, force: true);

            if (ReferenceEquals(context, ActiveChartContext))
            {
                _displayCandles = context.DisplayCandles.ToList();
                _allOlderHistoryLoaded = context.AllOlderHistoryLoaded;
                _allNewerHistoryLoaded = context.AllNewerHistoryLoaded;
                SyncDetachedChartWindows();
                UpdateChartUi(
                    $"virtual chart window — added {olderDisplay.Count:N0} older candles" +
                    (removedFromRight > 0
                        ? $", released {removedFromRight:N0} far-right candles from memory"
                        : removedFromLeft > 0
                            ? $", released {removedFromLeft:N0} far-left candles from memory"
                            : string.Empty));
            }
            SaveWorkspace();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Older history preload paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Chart virtual history navigation",
                    "preload_older_window",
                    "Copy diagnostics. Saved history is unchanged; retry scrolling left or use Refresh.",
                    ErrorCode: "TL-CHART-WINDOW-OLDER",
                    Symbol: symbol,
                    Timeframe: timeframe.SourceMt5Code,
                    ConnectorId: connectorId),
                TickLabErrorSeverity.Error,
                this,
                showPopup: ReferenceEquals(context, ActiveChartContext));
        }
        finally
        {
            bool stillOwnsRequest =
                context.OlderHistoryLoadGeneration == requestGeneration &&
                ReferenceEquals(context.OlderHistoryLoadCancellation, cancellation);
            if (stillOwnsRequest)
            {
                context.OlderHistoryLoadRunning = false;
                context.OlderHistoryLoadCancellation = null;
                chart.CompleteOlderHistoryRequest();
                UpdateChartPagingAvailability(context);
            }
            cancellation.Dispose();
        }
    }

    private async void CandleChart_NewerHistoryRequested(
        object? sender,
        EventArgs e)
    {
        CandleChartControl chart = sender as CandleChartControl ?? CandleChart;
        ChartRuntimeContext context = FindChartContext(chart);
        if (IsReplayChart(context.PaneId))
        {
            chart.CompleteNewerHistoryRequest();
            UpdateChartPagingAvailability(context);
            StatusText.Text = "Replay chart is isolated. End replay before loading newer candles.";
            return;
        }
        if (ReferenceEquals(context, ActiveChartContext))
        {
            context.DisplayCandles = _displayCandles.ToList();
            context.SourceCandles = _sourceCandles.ToList();
            context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
            context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        }
        if (context.DisplayCandles.Count == 0 && chart.Candles.Count > 0)
            context.DisplayCandles = chart.Candles.ToList();

        if (context.AllNewerHistoryLoaded || _selectedConnector is null ||
            context.DisplayCandles.Count == 0 || _isClosing)
        {
            chart.CompleteNewerHistoryRequest();
            UpdateChartPagingAvailability(context);
            return;
        }

        // Cancel the previous same-direction request, but let its own finally block
        // dispose its source after the background read has completely unwound.
        context.NewerHistoryLoadCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        context.NewerHistoryLoadCancellation = cancellation;
        int requestGeneration = ++context.NewerHistoryLoadGeneration;
        int identityGeneration = context.IdentityGeneration;
        context.NewerHistoryLoadRunning = true;
        UpdateChartPagingAvailability(context);

        Mt5ConnectorSummary connector = _selectedConnector;
        string connectorId = connector.ConnectorId;
        string symbol = context.Symbol;
        TimeframeDefinition timeframe = context.Timeframe;
        HistoryLoadSelection selection = _historySelection;
        long afterUnix = context.DisplayCandles[^1].StartUnix;
        int searchRecords = ChartWindowPageRecords * ChartWindowSearchMultiplier;
        long beforeUnix = CalculateForwardSearchBoundary(afterUnix, timeframe, searchRecords);
        long? newerFocusUnix = timeframe.Unit == TimeframeUnit.Second
            ? CalculateForwardSearchBoundary(afterUnix, timeframe, ChartWindowPageRecords)
            : null;

        try
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Preloading newer {symbol} {timeframe.DisplayText} candles...";

            LocalChartResult page = await Task.Run(
                () => BuildLocalChartResult(
                    connectorId,
                    connector,
                    symbol,
                    timeframe,
                    selection,
                    searchRecords,
                    beforeUnix,
                    cancellation.Token,
                    newerFocusUnix),
                cancellation.Token);

            if (_isClosing || cancellation.IsCancellationRequested ||
                !_chartContexts.TryGetValue(context.PaneId, out ChartRuntimeContext? liveContext) ||
                !ReferenceEquals(liveContext, context) ||
                liveContext.IdentityGeneration != identityGeneration ||
                liveContext.NewerHistoryLoadGeneration != requestGeneration ||
                !string.Equals(liveContext.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveContext.Timeframe.Key, timeframe.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<Candle> newerDisplay = page.Display
                .Where(candle => candle.StartUnix > afterUnix)
                .OrderBy(candle => candle.StartUnix)
                .Take(ChartWindowPageRecords)
                .ToList();
            if (newerDisplay.Count == 0)
            {
                context.AllNewerHistoryLoaded = true;
                if (ReferenceEquals(context, ActiveChartContext))
                {
                    _allNewerHistoryLoaded = true;
                    StatusText.Text = "Latest saved candle reached. Historical Find Candle date/time anchor remains active until End / Go Live.";
                }
                return;
            }

            ChartWindowAnchor? anchor = chart.CaptureWindowAnchor();
            List<Candle> merged = HistoryIntegrityService
                .MergeWithPriority(context.DisplayCandles, newerDisplay)
                .ToList();

            (int removedFromLeft, int removedFromRight) = TrimVirtualWindowAroundAnchor(
                merged,
                ChartWindowMaximumRecords,
                anchor,
                preferRight: false);
            if (removedFromLeft > 0)
                context.AllOlderHistoryLoaded = false;
            if (removedFromRight > 0)
                context.AllNewerHistoryLoaded = false;

            context.DisplayCandles = merged;
            (long? _, long? latestUnix) = GetSavedChartBoundaries(
                connectorId,
                symbol,
                timeframe,
                chart);
            context.AllNewerHistoryLoaded =
                removedFromRight == 0 &&
                (latestUnix.HasValue
                    ? context.DisplayCandles[^1].StartUnix >= latestUnix.Value
                    : newerDisplay.Count < ChartWindowPageRecords);
            if (context.AllNewerHistoryLoaded)
            {
                // Reaching the newest saved page by scrolling does not destroy
                // the Find Candle time pointer. Only the explicit End / Go Live
                // command releases the historical navigation anchor.
                if (IsDirectNative(timeframe) || timeframe.UsesTickArchive)
                    context.SourceCandles = context.DisplayCandles.ToList();
            }

            chart.ReplaceDataPreservingAnchor(context.DisplayCandles, anchor);
            RefreshAppliedIndicatorsForContext(context, force: true);
            RefreshBuiltInIndicatorsForContext(context, force: true);

            if (ReferenceEquals(context, ActiveChartContext))
            {
                _displayCandles = context.DisplayCandles.ToList();
                _sourceCandles = context.SourceCandles.Count > 0
                    ? context.SourceCandles.ToList()
                    : _displayCandles.ToList();
                _allOlderHistoryLoaded = context.AllOlderHistoryLoaded;
                _allNewerHistoryLoaded = context.AllNewerHistoryLoaded;
                SyncDetachedChartWindows();
                UpdateChartUi(
                    $"virtual chart window — added {newerDisplay.Count:N0} newer candles" +
                    (removedFromLeft > 0
                        ? $", released {removedFromLeft:N0} far-left candles from memory"
                        : removedFromRight > 0
                            ? $", released {removedFromRight:N0} far-right candles from memory"
                            : string.Empty));
            }
            SaveWorkspace();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(context, ActiveChartContext))
                StatusText.Text = $"Newer history preload paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Chart virtual history navigation",
                    "preload_newer_window",
                    "Copy diagnostics. Saved history is unchanged; retry scrolling right or press End to return to live.",
                    ErrorCode: "TL-CHART-WINDOW-NEWER",
                    Symbol: symbol,
                    Timeframe: timeframe.SourceMt5Code,
                    ConnectorId: connectorId),
                TickLabErrorSeverity.Error,
                this,
                showPopup: ReferenceEquals(context, ActiveChartContext));
        }
        finally
        {
            bool stillOwnsRequest =
                context.NewerHistoryLoadGeneration == requestGeneration &&
                ReferenceEquals(context.NewerHistoryLoadCancellation, cancellation);
            if (stillOwnsRequest)
            {
                context.NewerHistoryLoadRunning = false;
                context.NewerHistoryLoadCancellation = null;
                chart.CompleteNewerHistoryRequest();
                UpdateChartPagingAvailability(context);
            }
            cancellation.Dispose();
        }
    }

    private async void CandleChart_GoToEarliestRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is CandleChartControl requestedChart && !ReferenceEquals(requestedChart, CandleChart))
            ActivateChartControl(requestedChart);

        if (IsReplayChart(_activePricePaneId))
        {
            StatusText.Text = "End replay before opening the earliest chart window.";
            return;
        }

        if (_selectedConnector is null ||
            _displayCandles.Count == 0 ||
            _isClosing)
        {
            return;
        }

        (int navigationGeneration, CancellationToken navigationToken) = BeginChartNavigation();
        _goToEarliestLoadRunning = true;
        CandleChart.GoToEarliest();
        int generation = _selectionGeneration;
        Mt5ConnectorSummary connector = _selectedConnector;
        string connectorId = connector.ConnectorId;
        string symbol = _requestedSymbol;
        TimeframeDefinition timeframe = _activeTimeframe;

        try
        {
            StatusText.Text = $"Home: opening earliest saved {symbol} {timeframe.DisplayText} window...";
            List<Candle> earliestWindow;

            int earliestTargetRecords = Math.Max(
                GetChartLaunchPreviewRecords(timeframe),
                ChartWindowPageRecords);
            if (!string.IsNullOrWhiteSpace(timeframe.NativeMt5Code))
            {
                IReadOnlyList<Candle> first = await Task.Run(
                    () => _historyStore.ReadFirstCandles(
                        connectorId,
                        symbol,
                        timeframe.NativeMt5Code!,
                        earliestTargetRecords),
                    navigationToken);
                earliestWindow = first.ToList();
            }
            else if (!timeframe.UsesTickArchive)
            {
                // Home on a generated timeframe reads the earliest indexed M1
                // page directly instead of scanning a giant backward window.
                int sourceRecords = CalculateM1FallbackRecordLimit(
                    timeframe,
                    earliestTargetRecords);
                IReadOnlyList<Candle> firstM1 = await Task.Run(
                    () => _historyStore.ReadFirstCandles(
                        connectorId,
                        symbol,
                        "PERIOD_M1",
                        sourceRecords),
                    navigationToken);
                earliestWindow = CandleAggregator
                    .Aggregate(firstM1, timeframe, connector.ServerUtcOffsetMinutes)
                    .OrderBy(candle => candle.StartUnix)
                    .Take(earliestTargetRecords)
                    .ToList();
                RemovePartialLeadingBucket(
                    earliestWindow,
                    firstM1,
                    timeframe,
                    connector.ServerUtcOffsetMinutes);
            }
            else
            {
                long earliestBoundary =
                    CandleChart.NativeHistoryBoundaryUnix ??
                    _displayCandles[0].StartUnix;
                int searchRecords = earliestTargetRecords * ChartWindowSearchMultiplier;
                long beforeUnix = CalculateForwardSearchBoundary(
                    earliestBoundary,
                    timeframe,
                    searchRecords);
                LocalChartResult page = await Task.Run(
                    () => BuildLocalChartResult(
                        connectorId,
                        connector,
                        symbol,
                        timeframe,
                        HistoryLoadSelection.All,
                        searchRecords,
                        beforeUnix,
                        navigationToken),
                    navigationToken);
                earliestWindow = page.Display
                    .Where(candle => candle.StartUnix >= earliestBoundary)
                    .OrderBy(candle => candle.StartUnix)
                    .Take(earliestTargetRecords)
                    .ToList();
            }

            if (generation != _selectionGeneration || navigationGeneration != _chartNavigationGeneration || _isClosing)
                return;
            if (earliestWindow.Count == 0)
                throw new InvalidDataException("The earliest saved chart window is empty.");

            _displayCandles = earliestWindow;
            _sourceCandles = earliestWindow.ToList();
            _allOlderHistoryLoaded = true;
            var savedBoundaries = GetSavedChartBoundaries(
                connectorId,
                symbol,
                timeframe);
            long? latestUnix = savedBoundaries.LatestUnix;
            _allNewerHistoryLoaded = latestUnix.HasValue &&
                _displayCandles[^1].StartUnix >= latestUnix.Value;

            CandleChart.ReplaceDataKeepingViewport(_displayCandles);
            SyncDetachedChartWindows();
            CandleChart.GoToEarliest();
            UpdateChartUi(
                $"earliest virtual chart window — {_displayCandles.Count:N0} candles in memory");
            StatusText.Text =
                $"Beginning reached: {_displayCandles[0].StartTime:yyyy-MM-dd HH:mm}. Scroll right to stream newer candles from disk.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Home history jump paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Chart virtual history navigation",
                    "go_to_earliest_window",
                    "Copy diagnostics. The archive is unchanged; retry Home or inspect Candle History.",
                    ErrorCode: "TL-CHART-WINDOW-HOME",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            if (navigationGeneration == _chartNavigationGeneration)
                _goToEarliestLoadRunning = false;
            CandleChart.CompleteOlderHistoryRequest();
            CandleChart.CompleteNewerHistoryRequest();
            UpdateChartPagingAvailability();
        }
    }

    private async void CandleChart_GoToLatestRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is CandleChartControl requestedChart && !ReferenceEquals(requestedChart, CandleChart))
            ActivateChartControl(requestedChart);

        if (IsReplayChart(_activePricePaneId))
        {
            StatusText.Text = "End replay before returning this chart to the latest window.";
            return;
        }

        if (_selectedConnector is null ||
            _isClosing)
        {
            return;
        }

        ChartRuntimeContext context = ActiveChartContext;
        context.HistoricalNavigationAnchorUnix = null;
        context.HistoricalNavigationAnchorSymbol = string.Empty;
        context.Chart.HistoricalNavigationAnchorUnix = null;

        (int navigationGeneration, CancellationToken navigationToken) = BeginChartNavigation();
        _goToLatestLoadRunning = true;
        try
        {
            StatusText.Text = $"End: opening latest saved {_requestedSymbol} {_activeTimeframe.DisplayText} window...";
            await LoadLocalChartAsync(
                _selectionGeneration,
                _requestedSymbol,
                _activeTimeframe,
                _historySelection,
                navigationToken,
                MaximumChartBufferRecords);
            if (navigationGeneration != _chartNavigationGeneration || _isClosing)
                return;
            CandleChart.GoLive();
            StatusText.Text = "Latest chart window loaded. Live updates remain active.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Chart virtual history navigation",
                    "go_to_latest_window",
                    "Copy diagnostics. Saved history is unchanged; retry End or use Refresh.",
                    ErrorCode: "TL-CHART-WINDOW-END",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            if (navigationGeneration == _chartNavigationGeneration)
                _goToLatestLoadRunning = false;
            CandleChart.CompleteOlderHistoryRequest();
            CandleChart.CompleteNewerHistoryRequest();
            UpdateChartPagingAvailability();
        }
    }

    private async Task MergeNativeBootstrapAsync(
        int generation,
        string symbol,
        TimeframeDefinition timeframe,
        CancellationToken token)
    {
        if (_selectedConnector is null ||
            timeframe.UsesTickArchive ||
            string.IsNullOrWhiteSpace(timeframe.NativeMt5Code))
        {
            return;
        }

        string connectorId = _selectedConnector.ConnectorId;
        string nativeCode = timeframe.NativeMt5Code!;
        IReadOnlyList<Candle> bootstrap = Array.Empty<Candle>();
        DateTime write = DateTime.MinValue;

        // The live heartbeat can confirm a source change a few milliseconds
        // before the atomic bootstrap file is replaced. Wait briefly for the
        // matching native snapshot instead of reading the previous timeframe.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            token.ThrowIfCancellationRequested();
            write = _bridgeClient.GetChartBootstrapLastWriteUtc(connectorId);
            bootstrap = await Task.Run(
                () => _bridgeClient.ReadChartBootstrapCandles(connectorId)
                    .Where(candle =>
                        string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candle.Timeframe, nativeCode, StringComparison.Ordinal))
                    .OrderBy(candle => candle.StartUnix)
                    .GroupBy(candle => candle.StartUnix)
                    .Select(group => group.Last())
                    .ToArray(),
                token);

            if (bootstrap.Count > 0)
                break;

            await Task.Delay(100, token);
        }

        if (generation != _selectionGeneration || bootstrap.Count == 0)
            return;

        int previousCount = _displayCandles.Count;
        foreach (Candle candle in bootstrap)
        {
            UpsertCandleInPlace(_sourceCandles, candle);
            UpsertCandleInPlace(_displayCandles, candle);
        }

        int appended = Math.Max(0, _displayCandles.Count - previousCount);
        CandleChart.ReplaceDataKeepingViewport(_displayCandles, appended);
        SyncDetachedChartWindows();
        CandleChart.NativeHistoryBoundaryUnix = bootstrap.Min(candle => candle.StartUnix);
        CandleChart.HistoryBoundaryLabel = "Exact native MT5 chart bootstrap begins here";
        _lastChartBootstrapWriteUtc = write;
        UpdateChartUi($"{bootstrap.Count:N0} exact native MT5 bootstrap candles");
    }

    private void QueueAutomaticNativeHistoryLoad(
        string connectorId,
        string symbol,
        TimeframeDefinition timeframe)
    {
        if (_isClosing ||
            timeframe.UsesTickArchive ||
            string.IsNullOrWhiteSpace(timeframe.NativeMt5Code) ||
            !_bridgeClient.IsHistoryWorkerOnline(connectorId))
        {
            return;
        }

        string nativeCode = timeframe.NativeMt5Code!;
        if (_historyStore.ReadCandles(
                connectorId,
                symbol,
                nativeCode,
                HistoryLoadSelection.All,
                2).Count > 0)
        {
            return;
        }

        // A missing selected chart must never start in the middle of the native
        // timeframe queue. One automatic request always owns the complete
        // M1-to-Monthly sequence for this symbol.
        string key = string.Join("|", connectorId, symbol, "ALL_NATIVE_TIMEFRAMES");
        lock (_automaticHistorySync)
        {
            if (!_automaticHistoryRequests.Add(key))
                return;
        }

        _ = RunAutomaticNativeHistoryLoadAsync(
            key,
            connectorId,
            symbol,
            nativeCode);
    }

    private async Task RunAutomaticNativeHistoryLoadAsync(
        string key,
        string connectorId,
        string symbol,
        string nativeCode)
    {
        try
        {
            if (_historyOperationRunning ||
                _selectedConnector is null ||
                !string.Equals(
                    _selectedConnector.ConnectorId,
                    connectorId,
                    StringComparison.Ordinal))
            {
                return;
            }

            HistoryImportPhase[] phases = TimeframeDefinition.NativeMt5Timeframes
                .Select(code => new HistoryImportPhase(
                    code,
                    false,
                    "automatic permanent native MT5 history — sequential M1 to Monthly",
                    true,
                    null,
                    null))
                .ToArray();

            await ExecuteHistoryOperationAsync(
                "Automatic history",
                "import",
                phases,
                syncTicks: false,
                successMessage: "All native MT5 candle timeframes loaded sequentially from M1 to Monthly.",
                operationSymbol: symbol,
                showFailureDialog: true);

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Automatic native history paused: {exception.Message}";
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Automatic native history",
                    "queue_native_timeframes",
                    "Copy diagnostics. Retry only the reported timeframe or stage.",
                    ErrorCode: "TL-HIST-AUTO",
                    Symbol: symbol,
                    Timeframe: _sourceTimeframe,
                    ConnectorId: _selectedConnector?.ConnectorId),
                TickLabErrorSeverity.Error,
                this);
        }
        finally
        {
            lock (_automaticHistorySync)
                _automaticHistoryRequests.Remove(key);
        }
    }

    private async Task RefreshNativeLiveAsync(bool force = false)
    {
        if (_selectedConnector is null || _activeTimeframe.UsesTickArchive)
            return;

        int generation = _selectionGeneration;
        string connectorId = _selectedConnector.ConnectorId;
        string symbol = _requestedSymbol;
        string timeframe = _sourceTimeframe;

        DateTime liveWrite = _bridgeClient.GetLiveCandleLastWriteUtc(connectorId);
        DateTime closedWrite = _bridgeClient.GetClosedCandleLastWriteUtc(connectorId);

        if (!force &&
            liveWrite <= _lastLiveWriteUtc &&
            closedWrite <= _lastClosedWriteUtc)
        {
            return;
        }

        NativeLiveResult result = await Task.Run(() =>
        {
            Candle? live = force || liveWrite > _lastLiveWriteUtc
                ? _bridgeClient.ReadLiveCandle(connectorId)
                : null;

            Candle? closed = force || closedWrite > _lastClosedWriteUtc
                ? _bridgeClient.ReadClosedCandle(connectorId)
                : null;

            return new NativeLiveResult(liveWrite, closedWrite, live, closed);
        }, _lifetime.Token);

        if (generation != _selectionGeneration)
            return;

        if ((result.ClosedWriteUtc > _lastClosedWriteUtc || force) &&
            result.ClosedCandle is not null)
        {
            if (MatchesSource(result.ClosedCandle, symbol, timeframe))
            {
                ApplySourceCandle(
                    result.ClosedCandle with { IsClosed = true },
                    persist: false);
            }

            _lastClosedWriteUtc = result.ClosedWriteUtc;
        }

        if (result.LiveCandle is not null &&
            MatchesSource(result.LiveCandle, symbol, timeframe))
        {
            ApplySourceCandle(result.LiveCandle, persist: false);
            _lastLiveWriteUtc = result.LiveWriteUtc;
        }
    }

    private async Task RefreshSecondChartLiveAsync()
    {
        if (_selectedConnector is null)
            return;

        int generation = _selectionGeneration;
        string connectorId = _selectedConnector.ConnectorId;
        string symbol = _requestedSymbol;
        DateTime liveWrite = _bridgeClient.GetLiveSecondLastWriteUtc(connectorId);
        DateTime closedWrite = _bridgeClient.GetClosedSecondLastWriteUtc(connectorId);

        if (liveWrite <= _lastLiveSecondWriteUtc &&
            closedWrite <= _lastClosedSecondWriteUtc)
        {
            return;
        }

        (Candle? Closed, Candle? Live) result = await Task.Run(() =>
        {
            Candle? closed = closedWrite > _lastClosedSecondWriteUtc
                ? _bridgeClient.ReadClosedSecondCandle(connectorId)
                : null;
            Candle? live = liveWrite > _lastLiveSecondWriteUtc
                ? _bridgeClient.ReadLiveSecondCandle(connectorId)
                : null;
            return (closed, live);
        }, _lifetime.Token);

        if (generation != _selectionGeneration)
            return;

        long latestSourceStart = _sourceCandles.Count == 0
            ? 0
            : _sourceCandles[^1].StartUnix;
        long newestIncoming = Math.Max(
            result.Closed?.StartUnix ?? 0,
            result.Live?.StartUnix ?? 0);

        // If the UI was paused long enough to miss one or more closed-second
        // files, recover only then from the rolling snapshot. Normal live
        // operation never rebuilds hundreds of candles per tick.
        if (newestIncoming > 0 &&
            latestSourceStart > 0 &&
            newestIncoming > latestSourceStart + 1)
        {
            _lastRecentSecondsWriteUtc = DateTime.MinValue;
            await RefreshRecentSecondProjectionAsync(updateSecondSource: true);
        }

        if (result.Closed is not null &&
            MatchesSource(result.Closed, symbol, "1s"))
        {
            ApplySourceCandle(
                result.Closed with { IsClosed = true },
                persist: false);
        }

        if (result.Live is not null &&
            MatchesSource(result.Live, symbol, "1s"))
        {
            ApplySourceCandle(
                result.Live with { IsClosed = false },
                persist: false);
        }

        if (closedWrite > _lastClosedSecondWriteUtc)
            _lastClosedSecondWriteUtc = closedWrite;
        if (liveWrite > _lastLiveSecondWriteUtc)
            _lastLiveSecondWriteUtc = liveWrite;
    }

    private async Task RefreshRecentSecondProjectionAsync(bool updateSecondSource)
    {
        if (_selectedConnector is null)
            return;

        DateTime write = _bridgeClient.GetRecentSecondsLastWriteUtc(
            _selectedConnector.ConnectorId);

        if (write <= _lastRecentSecondsWriteUtc)
            return;

        int generation = _selectionGeneration;
        Mt5ConnectorSummary connector = _selectedConnector;
        TimeframeDefinition timeframe = _activeTimeframe;
        string symbol = _requestedSymbol;

        IReadOnlyList<Candle> oneSecond = await Task.Run(
            () => _bridgeClient.ReadRecentSecondCandles(connector.ConnectorId)
                .Where(candle =>
                    string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candle.Timeframe, "1s", StringComparison.Ordinal))
                .OrderBy(candle => candle.StartUnix)
                .GroupBy(candle => candle.StartUnix)
                .Select(group => group.Last())
                .ToArray(),
            _lifetime.Token);

        if (generation != _selectionGeneration || oneSecond.Count == 0)
            return;

        if (!_allNewerHistoryLoaded)
        {
            // A historical virtual window is open. The bridges and permanent
            // stores continue capturing live data, but the visible historical
            // page must not be joined to a far-future live tail.
            _lastRecentSecondsWriteUtc = write;
            return;
        }

        IReadOnlyList<HiddenHistoryRange> hiddenRanges =
            _historyStore.GetHiddenTickHistoryRanges(connector.ConnectorId, symbol);
        oneSecond = oneSecond
            .Where(candle => !IsInsideHiddenHistoryRange(candle.StartUnix, hiddenRanges))
            .ToArray();
        CandleChart.TimelineGaps = BuildChartTimelineGaps(
            connector.ConnectorId,
            symbol,
            timeframe);
        if (oneSecond.Count == 0)
            return;

        List<Candle> projected = timeframe.Unit == TimeframeUnit.Second &&
                                 timeframe.Quantity == 1
            ? oneSecond.ToList()
            : CandleAggregator.Aggregate(
                    oneSecond,
                    timeframe,
                    connector.ServerUtcOffsetMinutes)
                .ToList();

        // The rolling bridge window can begin halfway through the first
        // requested bucket. Never replace a complete saved candle with that
        // partial left-edge bucket. All later buckets have full source coverage.
        if (projected.Count > 1 &&
            projected[0].StartUnix < oneSecond[0].StartUnix)
        {
            projected.RemoveAt(0);
        }

        if (projected.Count == 0)
            return;

        if (!updateSecondSource && !string.IsNullOrWhiteSpace(timeframe.NativeMt5Code))
        {
            for (int index = 0; index < projected.Count; index++)
                projected[index] = projected[index] with { Timeframe = _sourceTimeframe };
        }

        EnsureDistinctActiveCandleLists();
        List<Candle> target = updateSecondSource
            ? _sourceCandles
            : _displayCandles;
        int previousCount = target.Count;
        ReplaceRecentTail(target, projected);
        int appended = Math.Max(0, target.Count - previousCount);
        if (target.Count > ChartWindowMaximumRecords)
        {
            target.RemoveRange(0, target.Count - ChartWindowMaximumRecords);
        }

        if (updateSecondSource)
            _displayCandles = _sourceCandles.ToList();

        CandleChart.ReplaceDataKeepingViewport(_displayCandles, appended);
        SyncDetachedChartWindows();
        _lastRecentSecondsWriteUtc = write;
        UpdateChartUi(
            updateSecondSource
                ? "exact rolling MT5 1-second stream"
                : "isolated live tick projection while history refreshes");
    }

    private static void ReplaceRecentTail(
        List<Candle> destination,
        IReadOnlyList<Candle> recent)
    {
        if (recent.Count == 0)
            return;

        int replaceIndex = LowerBoundByStart(destination, recent[0].StartUnix);
        if (replaceIndex < destination.Count)
        {
            destination.RemoveRange(
                replaceIndex,
                destination.Count - replaceIndex);
        }

        foreach (Candle candle in recent)
        {
            if (destination.Count > 0 &&
                destination[^1].StartUnix == candle.StartUnix)
            {
                destination[^1] = candle;
            }
            else if (destination.Count == 0 ||
                     destination[^1].StartUnix < candle.StartUnix)
            {
                destination.Add(candle);
            }
        }
    }

    private bool ApplySecondCandle(Candle candle)
    {
        if (_sourceCandles.Count == 0)
        {
            _sourceCandles.Add(candle);
            return true;
        }

        Candle last = _sourceCandles[^1];
        if (candle.StartUnix == last.StartUnix)
        {
            _sourceCandles[^1] = candle;
            return false;
        }

        if (candle.StartUnix > last.StartUnix)
        {
            _sourceCandles.Add(candle);
            return true;
        }

        int index = _sourceCandles.BinarySearch(candle, CandleStartComparer.Instance);
        if (index >= 0)
            _sourceCandles[index] = candle;

        return false;
    }

    private void ApplySourceCandle(Candle candle, bool persist)
    {
        ReconcileActiveHistoryBeforeLiveMerge(candle);
        EnsureDistinctActiveCandleLists();

        if (_activeTimeframe.UsesTickArchive &&
            _selectedConnector is not null &&
            IsInsideHiddenHistoryRange(
                candle.StartUnix,
                _historyStore.GetHiddenTickHistoryRanges(
                    _selectedConnector.ConnectorId,
                    _requestedSymbol)))
        {
            CandleChart.TimelineGaps = BuildChartTimelineGaps(
                _selectedConnector.ConnectorId,
                _requestedSymbol,
                _activeTimeframe);
            return;
        }

        if (!_allNewerHistoryLoaded)
        {
            // Keep the historical window stable while live capture continues
            // in the unchanged MT5 bridges and permanent archive. End or the
            // blue live button reloads the newest indexed page immediately.
            if (persist && _selectedConnector is not null &&
                _activeInstrumentSaving)
            {
                QueueCandlePersistence(candle);
            }
            return;
        }

        bool structuralCandleChange = UpsertCandleInPlace(_sourceCandles, candle);

        if (IsDirectNative(_activeTimeframe))
        {
            bool displayAppended = UpsertCandleInPlace(_displayCandles, candle);
            structuralCandleChange |= displayAppended;
            if (_displayCandles.Count > ChartWindowMaximumRecords)
            {
                int remove = _displayCandles.Count - ChartWindowMaximumRecords;
                _displayCandles.RemoveRange(0, remove);
                if (!ReferenceEquals(_sourceCandles, _displayCandles) &&
                    _sourceCandles.Count > ChartWindowMaximumRecords)
                {
                    _sourceCandles.RemoveRange(
                        0,
                        _sourceCandles.Count - ChartWindowMaximumRecords);
                }
            }

            // Native MT5 candles are already in the requested timeframe.
            // Never aggregate them a second time. The live native candle
            // replaces any M1-generated fallback at the same timestamp.
            if (ReferenceEquals(CandleChart.Candles, _displayCandles))
                CandleChart.RefreshData(displayAppended ? 1 : 0);
            else
                CandleChart.ReplaceDataKeepingViewport(
                    _displayCandles,
                    displayAppended ? 1 : 0);
        }
        else
        {
            if (_displayCandles.Count == 0)
            {
                _displayCandles = CandleAggregator.Aggregate(
                    _sourceCandles,
                    _activeTimeframe,
                    _selectedConnector?.ServerUtcOffsetMinutes ?? 0).ToList();
                RemovePartialLeadingBucket(
                    _displayCandles,
                    _sourceCandles,
                    _activeTimeframe,
                    _selectedConnector?.ServerUtcOffsetMinutes ?? 0);
                CandleChart.ReplaceDataKeepingViewport(_displayCandles);
                structuralCandleChange = true;
                SyncDetachedChartWindows();
            }
            else
            {
                int changed = CandleAggregator.ReplaceTailInPlace(
                    _sourceCandles,
                    _displayCandles,
                    _activeTimeframe,
                    candle.StartUnix,
                    _selectedConnector?.ServerUtcOffsetMinutes ?? 0);
                CandleChart.RefreshData(Math.Max(0, changed));
                structuralCandleChange |= changed > 0;
            }
        }

        ChartRuntimeContext liveContext = ActiveChartContext;
        liveContext.SourceCandles = _sourceCandles;
        liveContext.DisplayCandles = _displayCandles;
        if (structuralCandleChange)
            liveContext.CandleRevision++;

        UpdateChartUi(
            IsDirectNative(_activeTimeframe)
                ? "exact native MT5 live candle"
                : "live candle generated from one native M1 stream");

        if (persist && _selectedConnector is not null &&
            _activeInstrumentSaving)
        {
            QueueCandlePersistence(candle);
        }
    }

    private static bool UpsertCandleInPlace(
        List<Candle> destination,
        Candle candle)
    {
        if (destination.Count == 0)
        {
            destination.Add(candle);
            return true;
        }

        Candle last = destination[^1];
        if (candle.StartUnix == last.StartUnix)
        {
            destination[^1] = candle;
            return false;
        }

        if (candle.StartUnix > last.StartUnix)
        {
            destination.Add(candle);
            return true;
        }

        int index = destination.BinarySearch(
            candle,
            CandleStartComparer.Instance);
        if (index >= 0)
        {
            destination[index] = candle;
            return false;
        }

        int insertionIndex = ~index;
        destination.Insert(insertionIndex, candle);
        return insertionIndex == destination.Count - 1;
    }

    private void QueueCandlePersistence(Candle candle)
    {
        if (_selectedConnector is null ||
            !string.Equals(candle.Timeframe, "PERIOD_M1", StringComparison.Ordinal))
        {
            return;
        }

        string connectorId = _selectedConnector.ConnectorId;
        string key = string.Join(
            "|",
            connectorId,
            candle.Symbol,
            candle.Timeframe,
            candle.StartUnix.ToString(CultureInfo.InvariantCulture));

        lock (_pendingHistoryWriteSync)
        {
            _pendingHistoryWrites[key] =
                new PendingHistoryWrite(connectorId, candle);
        }
    }

    private async Task FlushPendingHistoryWritesAsync()
    {
        if (_historyFlushRunning || _isClosing)
            return;

        PendingHistoryWrite[] pending;
        lock (_pendingHistoryWriteSync)
        {
            pending = _pendingHistoryWrites.Values.ToArray();
            _pendingHistoryWrites.Clear();
        }

        if (pending.Length == 0)
            return;

        _historyFlushRunning = true;
        try
        {
            int serverOffset = _selectedConnector?.ServerUtcOffsetMinutes ?? 0;
            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            writeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            CancellationToken writeToken = writeTimeout.Token;

            await Task.Run(() =>
            {
                foreach (PendingHistoryWrite item in pending)
                {
                    writeToken.ThrowIfCancellationRequested();
                    _historyStore.UpsertLiveCandle(
                        item.ConnectorId,
                        item.Candle,
                        serverOffset);
                }
            }, writeToken);
        }
        catch (OperationCanceledException)
        {
            RequeueHistoryWrites(pending);
            if (!_lifetime.IsCancellationRequested)
                StatusText.Text = "History save timed out safely; pending candles were requeued.";
        }
        catch (IOException exception)
        {
            RequeueHistoryWrites(pending);
            ReportPendingHistoryWriteFailure(exception, "write_or_replace_file");
        }
        catch (UnauthorizedAccessException exception)
        {
            RequeueHistoryWrites(pending);
            ReportPendingHistoryWriteFailure(exception, "folder_permission");
        }
        catch (Exception exception)
        {
            RequeueHistoryWrites(pending);
            ReportPendingHistoryWriteFailure(exception, "flush_pending_history");
        }
        finally
        {
            _historyFlushRunning = false;
        }
    }

    private void ReportPendingHistoryWriteFailure(Exception exception, string stage)
    {
        StatusText.Text = $"History save paused: {exception.Message}";
        TickLabErrorEngine.Report(
            exception,
            new TickLabErrorContext(
                "Permanent history save",
                stage,
                "The unsaved rows were requeued. Copy diagnostics, check disk access, then keep TickLab running or use Refresh History.",
                ErrorCode: "TL-HIST-WRITE",
                Symbol: _requestedSymbol,
                Timeframe: _sourceTimeframe,
                ConnectorId: _selectedConnector?.ConnectorId),
            TickLabErrorSeverity.Error,
            this);
    }

    private void FlushPendingHistoryWritesSynchronously()
    {
        PendingHistoryWrite[] pending;
        lock (_pendingHistoryWriteSync)
        {
            pending = _pendingHistoryWrites.Values.ToArray();
            _pendingHistoryWrites.Clear();
        }

        foreach (PendingHistoryWrite item in pending)
        {
            try
            {
                _historyStore.UpsertLiveCandle(
                        item.ConnectorId,
                        item.Candle,
                        _selectedConnector?.ServerUtcOffsetMinutes ?? 0);
            }
            catch (Exception exception)
            {
                TickLabErrorEngine.Report(
                    exception,
                    new TickLabErrorContext(
                        "Shutdown history flush",
                        "save_live_candle",
                        "The error was logged. Reopen TickLab and use Refresh History to repair the final unsaved range.",
                        ErrorCode: "TL-HIST-SHUTDOWN",
                        Symbol: item.Candle.Symbol,
                        Timeframe: item.Candle.Timeframe,
                        ConnectorId: item.ConnectorId),
                    TickLabErrorSeverity.Warning,
                    this,
                    showPopup: false);
            }
        }

        if (_selectedConnector is not null &&
            _activeInstrumentSaving &&
            !string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            try
            {
                _historyStore.SyncTickArchives(
                    _selectedConnector.ConnectorId,
                    _requestedSymbol,
                    serverUtcOffsetMinutes: _selectedConnector.ServerUtcOffsetMinutes);
            }
            catch (Exception exception)
            {
                TickLabErrorEngine.Report(
                    exception,
                    new TickLabErrorContext(
                        "Shutdown tick flush",
                        "sync_tick_archive",
                        "The error was logged. Reopen TickLab and use Refresh History to repair the final tick range.",
                        ErrorCode: "TL-TICK-SHUTDOWN",
                        Symbol: _requestedSymbol,
                        ConnectorId: _selectedConnector?.ConnectorId),
                    TickLabErrorSeverity.Warning,
                    this,
                    showPopup: false);
            }
        }
    }

    private void RequeueHistoryWrites(IEnumerable<PendingHistoryWrite> items)
    {
        lock (_pendingHistoryWriteSync)
        {
            foreach (PendingHistoryWrite item in items)
            {
                string key = string.Join(
                    "|",
                    item.ConnectorId,
                    item.Candle.Symbol,
                    item.Candle.Timeframe,
                    item.Candle.StartUnix.ToString(CultureInfo.InvariantCulture));
                _pendingHistoryWrites[key] = item;
            }
        }
    }

    private async Task OpenHistoryOperationOptionsAsync(bool refresh)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            StatusText.Text = "Connect MT5 and select a chart instrument first.";
            return;
        }

        var window = new HistoryOperationOptionsWindow(refresh, _activeTimeframe.SourceMt5Code)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
            return;

        long? minimumStartUnix = null;
        if (window.StartDate.HasValue)
        {
            DateTime unspecified = DateTime.SpecifyKind(window.StartDate.Value, DateTimeKind.Unspecified);
            long wallClockUnix = new DateTimeOffset(unspecified, TimeSpan.Zero).ToUnixTimeSeconds();
            minimumStartUnix = wallClockUnix;
        }

        switch (window.Choice)
        {
            case HistoryOperationChoice.ImportAll:
                await ImportAllHistoryAsync();
                return;
            case HistoryOperationChoice.RefreshAll:
                await RefreshAllCandleHistoryAsync();
                return;
            case HistoryOperationChoice.ManageCandleFiles:
                await OpenCandleHistoryWindowAsync();
                return;
            case HistoryOperationChoice.RebuildGeneratedTimeframe:
                StatusText.Text = $"Rebuilding {_activeTimeframe.DisplayText} from saved M1…";
                _sourceCandles.Clear();
                _displayCandles.Clear();
                await LoadLocalChartAsync(
                    _selectionGeneration,
                    _requestedSymbol,
                    _activeTimeframe,
                    HistoryLoadSelection.All,
                    _lifetime.Token,
                    GetChartLaunchPreviewRecords(_activeTimeframe));
                RefreshAllAppliedIndicators(force: true);
                StatusText.Text = $"{_activeTimeframe.DisplayText} rebuilt from saved M1 without deleting history.";
                return;
        }

        string timeframe = window.Choice switch
        {
            HistoryOperationChoice.ImportSpecificTimeframe or HistoryOperationChoice.RefreshSpecificTimeframe => window.SelectedTimeframe,
            HistoryOperationChoice.RefreshCurrentTimeframeSource => _activeTimeframe.SourceMt5Code,
            _ => "PERIOD_M1"
        };

        bool ticksOnly = window.Choice is HistoryOperationChoice.ImportTicksOnly or HistoryOperationChoice.RefreshTicksOnly;
        bool verifyAndRepair = window.Choice == HistoryOperationChoice.VerifyAndRepair;
        bool import = !refresh;
        var phases = new List<HistoryImportPhase>();

        if (verifyAndRepair)
        {
            phases.AddRange(TimeframeDefinition.NativeMt5Timeframes.Select(code => new HistoryImportPhase(
                code, false, "verify and repair native candles", true, minimumStartUnix, null)));
            phases.Add(new HistoryImportPhase(
                "PERIOD_M1", true, "verify and repair raw ticks", true, minimumStartUnix, null,
                ImportCandles: false, ProgressLabel: "RAW_TICKS"));
        }
        else if (ticksOnly)
        {
            phases.Add(new HistoryImportPhase(
                "PERIOD_M1", true, import ? "import raw ticks only" : "refresh raw ticks only", true, minimumStartUnix, null,
                ImportCandles: false, ProgressLabel: "RAW_TICKS"));
        }
        else
        {
            phases.Add(new HistoryImportPhase(
                timeframe, false, import ? "import selected candle timeframe" : "refresh selected candle timeframe",
                true, minimumStartUnix, null));
        }

        string operation = verifyAndRepair
            ? "Verify and repair history"
            : import
                ? "Import selected history"
                : "Refresh selected history";
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Run {operation.ToLowerInvariant()} for {_requestedSymbol}? Existing good history will be preserved.",
            operation,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        await ExecuteHistoryOperationAsync(
            operation,
            import ? "import" : "refresh",
            phases,
            syncTicks: phases.Any(item => item.IncludeTicks),
            successMessage: verifyAndRepair
                ? "History verification and targeted repair completed."
                : $"{operation} completed for {timeframe}.");
    }

    private Task ImportOrRefreshHistoryAsync(bool refresh) =>
        refresh
            ? RefreshAllCandleHistoryAsync()
            : ImportAllHistoryAsync();

    private async Task ImportAllHistoryAsync()
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            StatusText.Text = "Connect MT5 and select a chart instrument first.";
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Import every available native MT5 candle timeframe from M1 to Monthly for {_requestedSymbol}, plus all raw ticks still available from the broker?",
            "Import History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        var phases = TimeframeDefinition.NativeMt5Timeframes
            .Select(code => new HistoryImportPhase(
                code,
                false,
                "permanent native MT5 candles — oldest to newest",
                true,
                null,
                null))
            .Append(new HistoryImportPhase(
                "PERIOD_M1",
                true,
                "available raw MT5 ticks after all candle timeframes",
                true,
                null,
                null,
                ImportCandles: false,
                ProgressLabel: "RAW_TICKS"))
            .ToArray();

        await ExecuteHistoryOperationAsync(
            "Import",
            "import",
            phases,
            syncTicks: true,
            successMessage: "Imported permanent native candles for every MT5 timeframe and synchronized available raw ticks.");
    }

    private async Task RefreshAllCandleHistoryAsync(string? symbolOverride = null)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            StatusText.Text = "Connect MT5 and select a chart instrument first.";
            return;
        }

        string operationSymbol = string.IsNullOrWhiteSpace(symbolOverride)
            ? _requestedSymbol
            : symbolOverride.Trim();

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Quick-scan every permanently saved native timeframe for {operationSymbol} against MT5, then add missing candles, replace incorrect candles and remove duplicates?",
            "Refresh All Candle Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        HistoryImportPhase[] phases = TimeframeDefinition.NativeMt5Timeframes
            .Select(code => new HistoryImportPhase(
                code,
                false,
                "permanent native MT5 candle verification",
                true,
                null,
                null))
            .ToArray();

        await ExecuteHistoryOperationAsync(
            "Refresh candles",
            "refresh",
            phases,
            syncTicks: false,
            successMessage: "All permanent native candle timeframes were scanned, aligned and repaired.",
            operationSymbol: operationSymbol);
    }

    private async Task RefreshSavedHistoryAsync(
        bool recheckLatest60Days,
        string? symbolOverride = null)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
        {
            StatusText.Text = "Connect MT5 and select an instrument first.";
            return;
        }

        string operationSymbol = string.IsNullOrWhiteSpace(symbolOverride)
            ? _requestedSymbol
            : symbolOverride.Trim();

        int offset = _selectedConnector.ServerUtcOffsetMinutes;
        string? onlySegment = recheckLatest60Days
            ? null
            : PersistentHistoryStore.GetCurrentSegmentKey(offset);
        long? minimumStartUnix = recheckLatest60Days
            ? Mt5ServerClock.UtcToServerUnix(DateTimeOffset.UtcNow.AddDays(-60), offset)
            : PersistentHistoryStore.GetSegmentStartUnix(onlySegment!);

        string actionName = recheckLatest60Days
            ? "Recheck latest 60 days"
            : "Refresh current saving folder";

        MessageBoxResult answer = MessageBox.Show(
            this,
            recheckLatest60Days
                ? $"Recheck and repair the latest 60 days of permanent raw ticks for {operationSymbol}? This may cross into the previous three-month folder."
                : $"Refresh only the current three-month permanent tick folder for {operationSymbol}? Older saved folders will remain untouched.",
            actionName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        var phase = new HistoryImportPhase(
            "PERIOD_M1",
            true,
            recheckLatest60Days
                ? "permanent raw ticks — latest 60 days"
                : "permanent raw ticks — current folder",
            true,
            minimumStartUnix,
            onlySegment,
            ImportCandles: false,
            ProgressLabel: "RAW_TICKS");

        await ExecuteHistoryOperationAsync(
            actionName,
            "refresh",
            new[] { phase },
            syncTicks: true,
            successMessage: recheckLatest60Days
                ? "Latest 60 days of permanent history rechecked and repaired."
                : "Current permanent saving folder refreshed without deleting older history.",
            operationSymbol: operationSymbol);
    }

    private async Task ExecuteHistoryOperationAsync(
        string operation,
        string bridgeAction,
        IReadOnlyList<HistoryImportPhase> phases,
        bool syncTicks,
        string successMessage,
        string? operationSymbol = null,
        bool showFailureDialog = true)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(_requestedSymbol))
            return;

        if (_historyOperationRunning)
        {
            StatusText.Text = "A history operation is already running.";
            _historyProgressWindow?.Show();
            _historyProgressWindow?.Activate();
            return;
        }

        string connectorId = _selectedConnector.ConnectorId;
        string chartSymbol = _requestedSymbol;
        TimeframeDefinition chartTimeframe = _activeTimeframe;
        string chartSourceTimeframe = _sourceTimeframe;
        string symbol = string.IsNullOrWhiteSpace(operationSymbol)
            ? chartSymbol
            : operationSymbol.Trim();

        await using IAsyncDisposable lease = await _dataRangeCoordinator.AcquireAsync(
            connectorId,
            symbol,
            "data",
            _lifetime.Token);

        if (_historyOperationRunning)
        {
            StatusText.Text = "A history operation is already running.";
            return;
        }

        _historyOperationRunning = true;
        _historyRestartAllRequested = false;
        _lastHistoryPauseErrorSignature = string.Empty;
        _activeHistoryErrorContext = null;
        _historyOperationChartTimeframe = chartSourceTimeframe;
        ShowHistoryImportProgressWindow(operation, symbol, phases.Count);
        string? historyCompletedMessage = null;
        int historyPendingLaunches = 0;
        bool historySucceeded = false;

        try
        {
            if (phases.Any(phase => phase.SavePermanent))
            {
                _historyStore.SetInstrumentSaving(connectorId, symbol, true);
                _activeInstrumentSaving = true;
            }

            long importedRecords = 0;
            bool restart;
            do
            {
                restart = false;
                _historyRestartAllRequested = false;
                importedRecords = 0;

                // V1.8.3 removed the separate 0-to-100 boundary pre-scan.
                // Each timeframe now discovers its own maximum reachable MT5
                // range and immediately imports it. This prevents the loading
                // window from appearing to finish every timeframe in seconds
                // and then returning to M1 at 5%.
                _historyOverallBasePercent = 0;
                _historyOverallScalePercent = 100;

                for (int index = 0; index < phases.Count; index++)
                {
                    if (_historyRestartAllRequested)
                    {
                        restart = true;
                        break;
                    }

                    HistoryImportPhase phase = phases[index];
                    string progressLabel = phase.ProgressLabel ?? phase.Timeframe;
                    _lastHistoryPauseErrorSignature = string.Empty;
                    _activeHistoryErrorContext = null;
                    _historyProgressWindow?.BeginPhase(progressLabel);
                    StatusText.Text =
                        $"{operation} {symbol}: {index + 1}/{phases.Count}  •  {progressLabel}  •  {phase.Description}";

                    try
                    {
                        HistoryImportResult result = await ImportNativeDatasetAsync(
                            connectorId,
                            symbol,
                            phase,
                            bridgeAction,
                            index + 1,
                            phases.Count,
                            _lifetime.Token);

                        importedRecords += result.ImportedRecords;
                        _historyProgressWindow?.MarkPhaseCompleted(progressLabel);
                    }
                    catch (HistoryRestartRequestedException)
                    {
                        restart = true;
                        break;
                    }
                }

                if (restart)
                {
                    if (!string.IsNullOrWhiteSpace(_activeHistoryRequestId))
                    {
                        _bridgeClient.SendHistoryControl(
                            connectorId,
                            _activeHistoryRequestId,
                            "cancel");
                    }
                    _activeHistoryRequestId = string.Empty;
                    _historyProgressWindow?.ResetForRestart();
                    StatusText.Text = $"Restarting {symbol} import from M1. Correct saved candles are being retained.";
                    await Task.Delay(500, _lifetime.Token);
                }
            }
            while (restart);

            bool tickArchiveFinalizeDeferred = false;
            if (syncTicks)
            {
                HistoryImportPhase? tickPhase = phases.LastOrDefault(phase => phase.IncludeTicks);
                TimeSpan finalizeBudget = operation.StartsWith(
                    "Startup",
                    StringComparison.OrdinalIgnoreCase)
                    ? TimeSpan.FromSeconds(15)
                    : TickArchiveFinalizeTimeout;
                using var tickFinalizeStop =
                    CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                CancellationToken tickFinalizeToken = tickFinalizeStop.Token;
                StatusText.Text =
                    "MT5 tick download completed. Finalizing the local canonical tick archive...";
                _historyProgressWindow?.UpdateChartLaunch(
                    tickPhase?.ProgressLabel ?? "Tick history",
                    70,
                    99,
                    "Finalizing local tick archive",
                    $"TickLab is indexing only tick source files that overlap the requested range. Startup gets a {finalizeBudget.TotalSeconds:0}-second foreground budget; completed files are checkpointed and any remaining work resumes later without repeating them.");

                Task finalizeTask = Task.Run(
                    () => _historyStore.SyncTickArchives(
                        connectorId,
                        symbol,
                        tickFinalizeToken,
                        includeHistorical: true,
                        serverUtcOffsetMinutes: _selectedConnector?.ServerUtcOffsetMinutes ?? 0,
                        minimumStartUnix: tickPhase?.MinimumStartUnix,
                        onlySegmentKey: tickPhase?.OnlySegmentKey),
                    CancellationToken.None);
                _ = finalizeTask.ContinueWith(
                    static completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

                DateTime finalizeStartedUtc = DateTime.UtcNow;
                DateTime finalizeDeadlineUtc = finalizeStartedUtc + finalizeBudget;
                while (!finalizeTask.IsCompleted &&
                       DateTime.UtcNow < finalizeDeadlineUtc &&
                       !_lifetime.IsCancellationRequested)
                {
                    await Task.WhenAny(
                        finalizeTask,
                        Task.Delay(750, CancellationToken.None));
                    if (finalizeTask.IsCompleted)
                        break;

                    double elapsedRatio = Math.Clamp(
                        (DateTime.UtcNow - finalizeStartedUtc).TotalMilliseconds /
                        Math.Max(1, finalizeBudget.TotalMilliseconds),
                        0,
                        1);
                    double stagePercent = 70 + elapsedRatio * 18;
                    _historyProgressWindow?.UpdateChartLaunch(
                        tickPhase?.ProgressLabel ?? "Tick history",
                        stagePercent,
                        Math.Min(98, stagePercent),
                        "Verifying and indexing saved ticks",
                        $"Range-filtered tick archive heartbeat • {(DateTime.UtcNow - finalizeStartedUtc):mm\\:ss} elapsed • completed source files are checkpointed.");
                }

                if (finalizeTask.IsCompleted)
                {
                    await finalizeTask;
                }
                else
                {
                    tickArchiveFinalizeDeferred = true;
                    tickFinalizeStop.Cancel();

                    // Give the current small source file a moment to observe the
                    // cooperative stop. Do not throw or hold chart launch.
                    await Task.WhenAny(
                        finalizeTask,
                        Task.Delay(1500, CancellationToken.None));

                    // The bridge export is already complete and safe. Resume any
                    // unfinished canonical indexing after the foreground task exits,
                    // without holding the import window or chart launch hostage.
                    _ = finalizeTask.ContinueWith(
                        _ => ContinueTickArchiveIndexingInBackground(
                            connectorId,
                            symbol,
                            _selectedConnector?.ServerUtcOffsetMinutes ?? 0,
                            tickPhase?.MinimumStartUnix,
                            tickPhase?.OnlySegmentKey),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    StatusText.Text =
                        "Raw tick history is safe. Remaining replay indexing continues in the background from completed-file checkpoints; chart launch is released immediately.";
                }
            }

            int pendingLaunches = _pendingChartLaunches.Count;
            string tickFinalizeNote = tickArchiveFinalizeDeferred
                ? " Raw ticks are safe; remaining local tick indexing was deferred after the foreground budget and completed-file checkpoints were preserved."
                : string.Empty;
            string completedMessage = pendingLaunches == 0
                ? $"{successMessage}  •  {importedRecords:N0} candles processed.{tickFinalizeNote}"
                : $"{successMessage}  •  {importedRecords:N0} candles processed. {pendingLaunches:N0} chart launch(es) can be retried without repeating MT5 imports.{tickFinalizeNote}";
            historyCompletedMessage = completedMessage;
            historyPendingLaunches = pendingLaunches;
            historySucceeded = true;
            StatusText.Text = completedMessage;
            _historyProgressWindow?.SetCompleted(
                completedMessage + " Chart restoration and any remaining replay indexing continue separately.",
                pendingLaunches);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "History operation cancelled.";
        }
        catch (Exception exception)
        {
            TickLabErrorContext context = _activeHistoryErrorContext ?? new TickLabErrorContext(
                "History operation",
                "desktop_import_pipeline",
                "Copy the diagnostics. Retry only the failed block or stage; do not delete verified permanent history.",
                ErrorCode: "TL-HIST-DESKTOP",
                Symbol: symbol,
                Timeframe: _sourceTimeframe,
                ConnectorId: connectorId,
                RequestId: _activeHistoryRequestId);
            TickLabErrorReport report = TickLabErrorEngine.Report(
                exception,
                context,
                TickLabErrorSeverity.Error,
                this,
                showPopup: showFailureDialog);
            _historyProgressWindow?.SetFailed($"{report.ReportId}: {exception.Message}");
            StatusText.Text = showFailureDialog
                ? $"History operation paused: {report.ReportId}"
                : $"Automatic history paused: {report.ReportId} — {exception.Message}";
        }
        finally
        {
            _activeHistoryRequestId = string.Empty;
            _historyChartLaunchRunning = false;
            _historyOperationChartTimeframe = string.Empty;
            _requestedSymbol = chartSymbol;
            _activeTimeframe = chartTimeframe;
            _sourceTimeframe = chartSourceTimeframe;
            SetChartIdentityUi();
            BuildTimeframeButtons();

            // Release the import gate before chart reload. The live timer can now
            // continue reading V300 immediately; only the short chart replacement
            // below is protected by _historyChartLaunchRunning.
            _historyOperationRunning = false;
            if (!_isClosing && !_lifetime.IsCancellationRequested)
            {
                using var reloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                reloadTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                CancellationToken reloadToken = reloadTimeout.Token;
                _historyChartLaunchRunning = true;

                try
                {
                    if (historySucceeded)
                    {
                        _historyProgressWindow?.UpdateChartLaunch(
                            _activeTimeframe.DisplayText,
                            86,
                            98,
                            "Building chart working window",
                            "Verified files are safe. TickLab is loading only the visible chart frame and restoring live updates with a bounded watchdog.");
                    }
                    _lastLiveWriteUtc = DateTime.MinValue;
                    _lastClosedWriteUtc = DateTime.MinValue;
                    _lastTickArchiveWriteUtc = DateTime.MinValue;
                    _lastRecentSecondsWriteUtc = DateTime.MinValue;
                    _lastLiveSecondWriteUtc = DateTime.MinValue;
                    _lastClosedSecondWriteUtc = DateTime.MinValue;

                    int previewRecords = GetChartLaunchPreviewRecords(_activeTimeframe);
                    Task reloadTask = _activeTimeframe.IsRawTickChart
                        ? LoadRawTickChartAsync(ActiveChartContext, resetViewport: true, cancellationToken: reloadToken)
                        : LoadLocalChartAsync(
                            _selectionGeneration,
                            _requestedSymbol,
                            _activeTimeframe,
                            _historySelection,
                            reloadToken,
                            previewRecords);
                    await reloadTask.WaitAsync(reloadToken);
                    if (historySucceeded)
                    {
                        _historyProgressWindow?.UpdateChartLaunch(
                            _activeTimeframe.DisplayText,
                            94,
                            99,
                            "Restoring live chart",
                            "The saved chart frame is visible. TickLab is reconnecting the latest live candle without repeating history import.");
                    }

                    if (_activeTimeframe.IsRawTickChart)
                        await RefreshRawTickChartLiveAsync(ActiveChartContext, force: true).WaitAsync(reloadToken);
                    else if (_activeTimeframe.UsesTickArchive)
                        await RefreshSecondChartLiveAsync().WaitAsync(reloadToken);
                    else
                        await RefreshNativeLiveAsync(force: true).WaitAsync(reloadToken);

                    if (!_activeTimeframe.IsRawTickChart)
                        CandleChart.GoLive();
                    if (historySucceeded)
                    {
                        StatusText.Text = "History completed. The saved chart is loaded and live updates resumed.";
                        string completed = historyCompletedMessage ?? StatusText.Text;
                        _historyProgressWindow?.UpdateChartLaunch(
                            _activeTimeframe.DisplayText,
                            100,
                            100,
                            "Chart loaded and live",
                            "Save, verify, commit, chart index and live restoration all completed. No full re-import was repeated.");
                        _historyProgressWindow?.SetCompleted(completed, historyPendingLaunches);
                    }
                    else
                    {
                        StatusText.Text += " The previous saved chart was restored and live updates resumed.";
                    }
                }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
                {
                    StatusText.Text =
                        "History completed safely. Chart reload exceeded 20 seconds and was released; live updates remain enabled. Use Refresh later without repeating the import.";
                    if (historySucceeded)
                    {
                        _historyProgressWindow?.UpdateChartLaunch(
                            _activeTimeframe.DisplayText,
                            100,
                            100,
                            "History safe — chart reload deferred",
                            StatusText.Text);
                        _historyProgressWindow?.SetCompleted(historyCompletedMessage ?? StatusText.Text, historyPendingLaunches);
                    }
                }
                catch (Exception exception)
                {
                    StatusText.Text = $"History completed safely, but chart reload was deferred: {exception.Message}";
                    if (historySucceeded)
                    {
                        _historyProgressWindow?.UpdateChartLaunch(
                            _activeTimeframe.DisplayText,
                            100,
                            100,
                            "History safe — chart reload deferred",
                            StatusText.Text);
                        _historyProgressWindow?.SetCompleted(historyCompletedMessage ?? StatusText.Text, historyPendingLaunches);
                    }
                    TickLabErrorEngine.Report(
                        exception,
                        new TickLabErrorContext(
                            "Chart reload",
                            "bounded_reload_after_history_operation",
                            "Permanent history is safe and live reading was released. Use Refresh; do not repeat the MT5 import.",
                            ErrorCode: "TL-CHART-RELOAD-DEFERRED",
                            Symbol: _requestedSymbol,
                            Timeframe: _sourceTimeframe,
                            ConnectorId: _selectedConnector?.ConnectorId),
                        TickLabErrorSeverity.Warning,
                        this,
                        showPopup: false);
                }
                finally
                {
                    _historyChartLaunchRunning = false;
                }
            }
        }
    }

    private void ContinueTickArchiveIndexingInBackground(
        string connectorId,
        string symbol,
        int serverUtcOffsetMinutes,
        long? minimumStartUnix,
        string? onlySegmentKey)
    {
        if (_isClosing)
            return;

        Task background = Task.Run(() => _historyStore.SyncTickArchives(
            connectorId,
            symbol,
            CancellationToken.None,
            includeHistorical: true,
            serverUtcOffsetMinutes: serverUtcOffsetMinutes,
            minimumStartUnix: minimumStartUnix,
            onlySegmentKey: onlySegmentKey));
        _ = background.ContinueWith(
            completed =>
            {
                Exception? error = completed.Exception?.GetBaseException();
                if (error is not null)
                {
                    TickLabErrorEngine.Report(
                        error,
                        new TickLabErrorContext(
                            "Tick archive background indexing",
                            "background_canonical_tick_index",
                            "Raw bridge tick files remain safe. Replay can retry the selected quarter on demand.",
                            ErrorCode: "TL-TICK-INDEX-BACKGROUND",
                            Symbol: symbol,
                            ConnectorId: connectorId),
                        TickLabErrorSeverity.Warning,
                        owner: null,
                        showPopup: false);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void ShowHistoryImportProgressWindow(
        string operation,
        string symbol,
        int phaseCount)
    {
        if (_historyProgressWindow is not null)
        {
            _historyProgressWindow.CloseAfterOperation();
            _historyProgressWindow = null;
        }

        var window = new HistoryImportProgressWindow(operation, symbol, phaseCount)
        {
            Owner = this
        };
        window.QuickRefreshRequested += (_, _) =>
        {
            if (_selectedConnector is null || string.IsNullOrWhiteSpace(_activeHistoryRequestId))
                return;
            _bridgeClient.SendHistoryControl(
                _selectedConnector.ConnectorId,
                _activeHistoryRequestId,
                "quick_refresh");
            StatusText.Text = "Retry Current Stage sent. Completed blocks are preserved.";
        };
        window.RetryChartLaunchRequested += async (_, _) =>
        {
            await RetryNextPendingChartLaunchAsync();
        };
        window.CompletionAcknowledged += (_, _) =>
        {
            if (ReferenceEquals(_historyProgressWindow, window))
                _historyProgressWindow = null;
        };
        window.PauseChanged += paused =>
        {
            if (_selectedConnector is null || string.IsNullOrWhiteSpace(_activeHistoryRequestId))
                return;
            _bridgeClient.SendHistoryControl(
                _selectedConnector.ConnectorId,
                _activeHistoryRequestId,
                paused ? "pause" : "resume");
            StatusText.Text = paused
                ? "History import paused. Live tick recording continues."
                : "History import resumed from the same timestamp block.";
        };
        window.RestartAllRequested += (_, _) =>
        {
            _historyRestartAllRequested = true;
            _pendingChartLaunches.Clear();
            _historyProgressWindow?.SetPendingChartLaunchCount(0);
            if (_selectedConnector is not null &&
                !string.IsNullOrWhiteSpace(_activeHistoryRequestId))
            {
                _bridgeClient.SendHistoryControl(
                    _selectedConnector.ConnectorId,
                    _activeHistoryRequestId,
                    "cancel");
            }
        };
        _historyProgressWindow = window;
        window.Show();
    }

    private Task<HistoryImportResult> ImportNativeDatasetAsync(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        string action,
        int phaseNumber,
        int phaseCount,
        CancellationToken token) =>
        ImportNativeDatasetAttemptAsync(
            connectorId,
            symbol,
            phase,
            action,
            phaseNumber,
            phaseCount,
            token);

    private async Task<HistoryImportResult> ImportNativeDatasetAttemptAsync(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        string action,
        int phaseNumber,
        int phaseCount,
        CancellationToken token)
    {
        Mt5ConnectorSummary? bridge = await Task.Run(
            () => _bridgeClient.FindConnector(connectorId),
            token);

        bool requestDriven = bridge?.SupportsRequestedHistory == true;
        if (requestDriven && !_bridgeClient.IsHistoryWorkerOnline(connectorId))
        {
            throw new InvalidOperationException(
                "TickLab History Worker V3.5.0 is offline. Attach TickLabHistoryBridge_V305 to a second MT5 chart.");
        }

        long minimumTickMilliseconds = phase.IncludeTicks && phase.MinimumStartUnix.HasValue
            ? Mt5ServerClock.ServerUnixToUtcMilliseconds(
                phase.MinimumStartUnix.Value,
                bridge?.ServerUtcOffsetMinutes
                    ?? _selectedConnector?.ServerUtcOffsetMinutes
                    ?? 0,
                safetyMarginMinutes: 180)
            : 0;
        string requestId = requestDriven
            ? _bridgeClient.SendHistoryRequest(
                connectorId,
                action,
                symbol,
                phase.Timeframe,
                phase.IncludeTicks,
                minimumTickMilliseconds,
                phase.MinimumStartUnix ?? 0,
                includeCandles: phase.ImportCandles)
            : string.Empty;
        _activeHistoryRequestId = requestId;

        Mt5HistoryStatus? finalStatus = null;
        HistoryImportResult? committedResult = null;
        bool desktopCommitSent = false;
        DateTime lastStatusUtc = DateTime.UtcNow;
        long lastProgressBars = -1;
        string progressLabel = phase.ProgressLabel ?? phase.Timeframe;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (_historyRestartAllRequested)
                throw new HistoryRestartRequestedException();

            await Task.Delay(250, token);

            Mt5HistoryStatus? status = await Task.Run(
                () => _bridgeClient.ReadHistoryStatus(connectorId),
                token);

            bool identityMatches = status is not null &&
                string.Equals(status.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    NormalizeTimeframe(status.Timeframe),
                    phase.Timeframe,
                    StringComparison.Ordinal);

            bool requestMatches = !requestDriven ||
                (status is not null &&
                 string.Equals(status.RequestId, requestId, StringComparison.Ordinal));

            if (!identityMatches || !requestMatches)
            {
                if (DateTime.UtcNow - lastStatusUtc > TimeSpan.FromSeconds(10))
                {
                    double overallWaiting = _historyOverallBasePercent +
                        _historyOverallScalePercent * (phaseNumber - 1) / phaseCount;
                    _historyProgressWindow?.UpdateProgress(
                        progressLabel,
                        "waiting_for_mt5",
                        0,
                        overallWaiting,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        "Waiting for the History Bridge to acknowledge this timeframe request. Live tick recording is unaffected.");
                }
                continue;
            }

            lastStatusUtc = DateTime.UtcNow;
            double phasePercent = Math.Clamp(status!.ProgressPercent, 0, 100);
            double importFraction = phase.ImportCandles
                ? HistoryImportWorkShare * phasePercent / 100.0
                : phasePercent / 100.0;
            double overallPercent = _historyOverallBasePercent +
                _historyOverallScalePercent *
                ((phaseNumber - 1) + importFraction) / phaseCount;
            long currentUnix = status.CurrentBarUnix > 0
                ? status.CurrentBarUnix
                : status.CurrentBlockStartUnix;

            _historyProgressWindow?.UpdateProgress(
                progressLabel,
                status.Status,
                phasePercent,
                overallPercent,
                currentUnix,
                status.ExportedBars,
                status.TargetTotalBars,
                status.SpeedBarsPerSecond,
                status.CurrentBlockStartUnix,
                status.CurrentBlockEndUnix,
                status.RetryCount,
                status.Message,
                status.ServerFirstUnix,
                status.AvailableFirstUnix,
                status.NativeRangePartial,
                status.CoverageReason,
                status.LastErrorCode,
                status.FailureCode,
                status.FailureStage,
                status.FailureExpectedBars,
                status.FailureActualBars,
                status.FailureExpectedFirstUnix,
                status.FailureActualFirstUnix,
                status.FailureExpectedLatestUnix,
                status.FailureActualLatestUnix,
                status.FailureFilePath);

            StatusText.Text =
                $"History {phaseNumber}/{phaseCount}: {progressLabel}  •  {status.Status}  •  {status.ExportedBars:N0} candles  •  {phasePercent:0.0}%";

            if (status.ExportedBars != lastProgressBars)
            {
                lastProgressBars = status.ExportedBars;
                lastStatusUtc = DateTime.UtcNow;
            }

            if (string.Equals(status.Status, "cancelled", StringComparison.OrdinalIgnoreCase) &&
                _historyRestartAllRequested)
            {
                throw new HistoryRestartRequestedException();
            }

            if (string.Equals(status.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                _activeHistoryErrorContext = BuildHistoryErrorContext(
                    connectorId, requestId, symbol, progressLabel, status,
                    "The MT5 History Worker stopped this request. Correct the reported condition, then restart only this timeframe.");
                throw new InvalidDataException(
                    string.IsNullOrWhiteSpace(status.Message)
                        ? $"MT5 could not import {progressLabel}."
                        : status.Message);
            }

            if (string.Equals(status.Status, "stuck_block", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.Status, "verification_failed", StringComparison.OrdinalIgnoreCase))
            {
                _activeHistoryErrorContext = BuildHistoryErrorContext(
                    connectorId, requestId, symbol, progressLabel, status,
                    string.Equals(status.Status, "verification_failed", StringComparison.OrdinalIgnoreCase)
                        ? "Press Retry Current Stage once. It retries only final verification/publish and must not reset completed candle blocks. Copy diagnostics if the same error returns."
                        : "Press Retry Current Stage once. It retries only the current timestamp block and must preserve all earlier completed blocks.");
                string signature = string.Join('|',
                    status.Status, status.FailureCode, status.FailureStage,
                    status.ExportedBars, status.CurrentBlockStartUnix, status.CurrentBlockEndUnix, status.Message);
                if (!string.Equals(signature, _lastHistoryPauseErrorSignature, StringComparison.Ordinal))
                {
                    _lastHistoryPauseErrorSignature = signature;
                    TickLabErrorEngine.ReportMessage(
                        string.IsNullOrWhiteSpace(status.Message)
                            ? $"{progressLabel} paused at {status.Status}."
                            : status.Message,
                        _activeHistoryErrorContext,
                        TickLabErrorSeverity.Error,
                        this,
                        showPopup: true);
                }

                // Stay inside this request. Retry Current Stage retries only the exact
                // failed stage; it cannot delete the saved 0-to-100 checkpoint.
                continue;
            }

            _lastHistoryPauseErrorSignature = string.Empty;
            _activeHistoryErrorContext = null;

            if (string.Equals(status.Status, "awaiting_desktop_commit", StringComparison.OrdinalIgnoreCase) &&
                phase.ImportCandles &&
                !desktopCommitSent)
            {
                // Saving and boundary verification are the authoritative commit gate.
                // Chart rendering is deliberately NOT part of the bridge acknowledgement:
                // a UI/cache/preview problem must never hold M1 at 100% or stop M2.
                committedResult ??= await CommitPublishedCandleSnapshotAsync(
                    connectorId,
                    symbol,
                    phase,
                    action,
                    status,
                    bridge,
                    phaseNumber,
                    phaseCount,
                    progressLabel,
                    token);

                if (requestDriven)
                {
                    _bridgeClient.SendHistoryControl(
                        connectorId,
                        requestId,
                        "commit_ack");
                }
                desktopCommitSent = true;
                StatusText.Text =
                    $"{progressLabel}: permanently saved and verified. MT5 is released to continue.";

                if (string.Equals(
                        phase.Timeframe,
                        _historyOperationChartTimeframe,
                        StringComparison.Ordinal))
                {
                    QueueIndependentChartLaunch(
                        connectorId, symbol, phase, status, phaseNumber, phaseCount,
                        progressLabel, token);
                }
                else
                {
                    _historyProgressWindow?.UpdateChartLaunch(
                        progressLabel,
                        70,
                        GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 70),
                        "Permanent archive verified",
                        "This timeframe is safely stored. TickLab keeps the existing single main chart and immediately continues the import queue.");
                }

                continue;
            }

            if (string.Equals(status.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                finalStatus = status;
                break;
            }
        }

        _activeHistoryRequestId = string.Empty;

        if (finalStatus is null)
        {
            throw new InvalidDataException(
                $"MT5 did not produce a verified {progressLabel} history result.");
        }

        if (!phase.ImportCandles)
        {
            return new HistoryImportResult(
                true,
                "MT5 tick history request completed; candle snapshot intentionally ignored.",
                symbol,
                phase.Timeframe,
                0,
                _historyStore.GetDatasetSizeBytes(connectorId, symbol));
        }

        // Backward compatibility for an older worker that returns ready directly.
        if (committedResult is null)
        {
            committedResult = await CommitPublishedCandleSnapshotAsync(
                connectorId,
                symbol,
                phase,
                action,
                finalStatus,
                bridge,
                phaseNumber,
                phaseCount,
                progressLabel,
                token);
            if (string.Equals(
                    phase.Timeframe,
                    _historyOperationChartTimeframe,
                    StringComparison.Ordinal))
            {
                QueueIndependentChartLaunch(
                    connectorId, symbol, phase, finalStatus, phaseNumber, phaseCount,
                    progressLabel, token);
            }
        }

        _historyStore.SetNativeAvailabilityBoundary(
            connectorId,
            symbol,
            phase.Timeframe,
            finalStatus.FirstBarUnix);

        return committedResult;
    }

    private async Task<HistoryImportResult> CommitPublishedCandleSnapshotAsync(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        string action,
        Mt5HistoryStatus status,
        Mt5ConnectorSummary? bridge,
        int phaseNumber,
        int phaseCount,
        string progressLabel,
        CancellationToken token)
    {
        if (status.ExportedBars <= 0 ||
            status.FirstBarUnix <= 0 ||
            status.LatestBarUnix < status.FirstBarUnix)
        {
            throw new InvalidDataException(
                $"MT5 reached 100% for {progressLabel} without a valid first-to-latest candle range. The timeframe was paused instead of restarted.");
        }

        NativeCandleFileSummary? existingArchive = phase.SavePermanent
            ? _historyStore
                .GetNativeCandleFiles(connectorId, symbol)
                .FirstOrDefault(item =>
                    string.Equals(item.Timeframe, phase.Timeframe, StringComparison.Ordinal))
            : null;
        bool alreadyCommitted = string.Equals(action, "import", StringComparison.OrdinalIgnoreCase) &&
            existingArchive is not null &&
            string.Equals(existingArchive.Status, "OK", StringComparison.OrdinalIgnoreCase) &&
            existingArchive.RecordCount >= status.ExportedBars &&
            existingArchive.EarliestUnix <= status.FirstBarUnix &&
            existingArchive.LatestUnix >= status.LatestBarUnix;
        if (alreadyCommitted)
        {
            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                70,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 70),
                "Permanent archive already verified",
                $"The complete {progressLabel} MT5 range is already safely stored. TickLab is skipping the duplicate disk rewrite and launching it directly on the chart.");
            return new HistoryImportResult(
                true,
                "Existing permanent archive already covers the complete MT5 export.",
                symbol,
                phase.Timeframe,
                0,
                existingArchive!.SizeBytes);
        }

        double launchStartOverall = GetHistoryOverallPercent(
            phaseNumber,
            phaseCount,
            HistoryImportWorkShare);
        string publishedSnapshotPath = Path.Combine(
            _bridgeClient.GetConnectorFolder(connectorId),
            "candles.csv");
        _activeHistoryErrorContext = new TickLabErrorContext(
            "History final commit",
            "wait_for_published_snapshot",
            "The bridge export is preserved. Copy diagnostics, confirm the published file is accessible, then use Retry Current Stage.",
            ErrorCode: "TL-HIST-PUBLISHED-FILE",
            Symbol: symbol,
            Timeframe: progressLabel,
            ConnectorId: connectorId,
            RequestId: status.RequestId,
            FilePath: publishedSnapshotPath,
            ExpectedRecords: status.ExportedBars,
            ExpectedFirstUnix: status.FirstBarUnix,
            ExpectedLatestUnix: status.LatestBarUnix);
        _historyProgressWindow?.UpdateChartLaunch(
            progressLabel,
            0,
            launchStartOverall,
            "Waiting for published file",
            "MT5 import reached 100%. TickLab is waiting for the complete published snapshot before saving it.");

        await WaitForPublishedCandleSnapshotAsync(connectorId, token);
        _historyProgressWindow?.UpdateChartLaunch(
            progressLabel,
            5,
            GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 5),
            "Saving permanent candles",
            "The published MT5 file is stable. TickLab is streaming it directly to the permanent archive without loading the complete file into chart memory.");

        int serverOffset = bridge?.ServerUtcOffsetMinutes
            ?? _selectedConnector?.ServerUtcOffsetMinutes
            ?? 0;

        var saveProgress = new Progress<HistoryImportProgress>(progress =>
        {
            double ratio = progress.TotalRecords > 0
                ? Math.Clamp((double)progress.ImportedRecords / progress.TotalRecords, 0, 1)
                : 0;
            double launchPercent = 5 + ratio * 55;
            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                launchPercent,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, launchPercent),
                "Saving permanent candles",
                $"Saved {progress.ImportedRecords:N0} / {Math.Max(progress.TotalRecords, progress.ImportedRecords):N0} candles to the permanent {progressLabel} archive.");
        });

        _activeHistoryErrorContext = new TickLabErrorContext(
            "History final commit",
            "save_permanent_archive",
            "The MT5 export remains available. Copy diagnostics, check disk access, then retry this final stage without restarting M1.",
            ErrorCode: "TL-HIST-PERMANENT-SAVE",
            Symbol: symbol,
            Timeframe: progressLabel,
            ConnectorId: connectorId,
            RequestId: status.RequestId,
            FilePath: publishedSnapshotPath,
            ExpectedRecords: status.ExportedBars,
            ExpectedFirstUnix: status.FirstBarUnix,
            ExpectedLatestUnix: status.LatestBarUnix);

        HistoryImportResult result;
        if (phase.SavePermanent)
        {
            result = await Task.Run(
                () => _historyStore.ImportCandleStream(
                    connectorId,
                    ValidatePublishedCandleStream(
                        _bridgeClient.EnumerateCandles(connectorId),
                        symbol,
                        phase.Timeframe,
                        status.ExportedBars,
                        status.FirstBarUnix,
                        status.LatestBarUnix),
                    symbol,
                    phase.Timeframe,
                    token,
                    saveProgress,
                    expectedRecords: status.ExportedBars,
                    serverUtcOffsetMinutes: serverOffset,
                    minimumStartUnix: phase.MinimumStartUnix,
                    onlySegmentKey: phase.OnlySegmentKey),
                token);
        }
        else
        {
            IReadOnlyList<Candle> snapshot = await Task.Run(
                () => _bridgeClient.ReadCandles(connectorId),
                token);
            result = await Task.Run(
                () => ConvertTemporaryImport(
                    connectorId,
                    snapshot,
                    symbol,
                    phase,
                    status,
                    token),
                token);
        }

        if (!result.Success)
            throw new InvalidDataException(result.Message);

        _historyProgressWindow?.UpdateChartLaunch(
            progressLabel,
            65,
            GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 65),
            "Verifying permanent archive",
            "Permanent write completed. TickLab is confirming record count and first/latest timestamp boundaries.");

        if (phase.SavePermanent)
        {
            _activeHistoryErrorContext = new TickLabErrorContext(
                "History final commit",
                "verify_permanent_archive",
                "Do not repeat the MT5 download. Copy diagnostics and retry only permanent verification after checking the saved archive file.",
                ErrorCode: "TL-HIST-PERMANENT-VERIFY",
                Symbol: symbol,
                Timeframe: progressLabel,
                ConnectorId: connectorId,
                RequestId: status.RequestId,
                ExpectedRecords: status.ExportedBars,
                ExpectedFirstUnix: status.FirstBarUnix,
                ExpectedLatestUnix: status.LatestBarUnix);
            NativeCandleFileSummary? saved = _historyStore
                .GetNativeCandleFiles(connectorId, symbol)
                .FirstOrDefault(item =>
                    string.Equals(item.Timeframe, phase.Timeframe, StringComparison.Ordinal));

            bool archiveVerified = saved is not null &&
                string.Equals(saved.Status, "OK", StringComparison.OrdinalIgnoreCase) &&
                saved.RecordCount >= status.ExportedBars &&
                saved.EarliestUnix <= status.FirstBarUnix &&
                saved.LatestUnix >= status.LatestBarUnix;
            if (!archiveVerified)
            {
                throw new InvalidDataException(
                    $"{progressLabel} was read from MT5 but the permanent archive boundary verification failed. The bridge remains on this timeframe; saved valid records were not deleted.");
            }
        }

        _activeHistoryErrorContext = null;
        _historyProgressWindow?.UpdateChartLaunch(
            progressLabel,
            70,
            GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 70),
            "Permanent archive verified",
            "The saved file is valid. TickLab will now create a lightweight chart working window instead of drawing the complete multi-year archive at once.");
        return result;
    }

    private void QueueIndependentChartLaunch(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        Mt5HistoryStatus status,
        int phaseNumber,
        int phaseCount,
        string progressLabel,
        CancellationToken token)
    {
        // Never launch or replace the main chart while the multi-timeframe import
        // queue is still running. The verified archive is safe at this point.
        // A single bounded reload runs after the complete operation releases its
        // data-range lease, so chart rendering can never hold MT5 history at the
        // final stage or prevent live updates from resuming.
        _historyProgressWindow?.UpdateChartLaunch(
            progressLabel,
            70,
            GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 70),
            "Permanent archive verified",
            "The timeframe is safely stored. TickLab will load the active chart once after the complete import finishes; the MT5 queue and live channel continue now.");
    }

    private async Task LaunchCommittedTimeframeOnChartAsync(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        Mt5HistoryStatus status,
        int phaseNumber,
        int phaseCount,
        string progressLabel,
        CancellationToken token)
    {
        if (!phase.ImportCandles)
            return;

        using var launchTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        launchTimeout.CancelAfter(ChartLaunchTimeout);
        CancellationToken launchToken = launchTimeout.Token;
        _historyChartLaunchRunning = true;

        try
        {
            TimeframeDefinition timeframe = TimeframeDefinition.FromNativeMt5Code(phase.Timeframe);
            int previewRecords = GetChartLaunchPreviewRecords(timeframe);

            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                74,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 74),
                "Building chart index",
                $"Preparing random-access metadata for {progressLabel}. Only the newest {previewRecords:N0} candles will enter chart memory; older candles remain available through scrolling and Home.");

            Task<NativeCandleFileSummary?> summaryTask = Task.Run(
                () => _historyStore
                    .GetNativeCandleFiles(connectorId, symbol)
                    .FirstOrDefault(item =>
                        string.Equals(item.Timeframe, phase.Timeframe, StringComparison.Ordinal)));
            NativeCandleFileSummary? summary = await summaryTask.WaitAsync(launchToken);
            if (summary is null ||
                summary.RecordCount <= 0 ||
                summary.EarliestUnix > status.FirstBarUnix ||
                summary.LatestUnix < status.LatestBarUnix)
            {
                throw new InvalidDataException(
                    $"The {progressLabel} archive index does not cover the imported MT5 range.");
            }

            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                82,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 82),
                "Loading chart working window",
                $"Loading a {previewRecords:N0}-candle preview from the permanent archive. The complete {summary.RecordCount:N0}-candle file stays indexed on disk.");

            Task<IReadOnlyList<Candle>> previewTask = Task.Run(
                () => _historyStore.ReadCandles(
                    connectorId,
                    symbol,
                    phase.Timeframe,
                    HistoryLoadSelection.All,
                    previewRecords));
            IReadOnlyList<Candle> preview = await previewTask.WaitAsync(launchToken);

            _requestedSymbol = symbol;
            _activeTimeframe = timeframe;
            _sourceTimeframe = timeframe.SourceMt5Code;
            ++_selectionGeneration;
            SetChartIdentityUi();
            BuildTimeframeButtons();
            CandleChart.ClearSelection();
            _sourceCandles = preview.ToList();
            _displayCandles = preview.ToList();
            _allOlderHistoryLoaded =
                _displayCandles.Count == 0 ||
                _displayCandles[0].StartUnix <= summary.EarliestUnix;
            _allNewerHistoryLoaded =
                _displayCandles.Count > 0 &&
                _displayCandles[^1].StartUnix >= summary.LatestUnix;
            CandleChart.TimelineGaps = Array.Empty<ChartTimelineGap>();
            CandleChart.NativeHistoryBoundaryUnix = summary.EarliestUnix;
            CandleChart.HistoryBoundaryLabel = $"Native MT5 {progressLabel} history begins here";
            CandleChart.Candles = _displayCandles;
            SyncDetachedChartWindows();
            CandleChart.ResetToLaunchView();
            UpdateChartPagingAvailability();
            UpdateChartUi($"permanent native MT5 {progressLabel} launch preview");

            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                94,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 94),
                "Confirming chart launch",
                "The chart working window is rendered. TickLab is confirming timeline order, latest saved candle and lazy older-history access.");

            if (_displayCandles.Count == 0)
                throw new InvalidDataException($"{progressLabel} saved correctly but produced an empty chart working window.");
            for (int index = 1; index < _displayCandles.Count; index++)
            {
                if (_displayCandles[index].StartUnix <= _displayCandles[index - 1].StartUnix)
                {
                    throw new InvalidDataException(
                        $"{progressLabel} chart working window is duplicated or out of order.");
                }
            }
            if (_displayCandles[^1].StartUnix < status.LatestBarUnix)
            {
                throw new InvalidDataException(
                    $"{progressLabel} chart stopped at {_displayCandles[^1].StartTime:yyyy-MM-dd HH:mm} instead of the imported latest candle.");
            }

            _historyProgressWindow?.UpdateChartLaunch(
                progressLabel,
                100,
                GetHistoryLaunchOverallPercent(phaseNumber, phaseCount, 100),
                "Launched and verified",
                $"{progressLabel} is saved, indexed and visible on the chart. The history queue was already released after permanent verification.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"{progressLabel} chart launch exceeded the 30-second safety limit.",
                launchToken);
        }
        finally
        {
            _historyChartLaunchRunning = false;
        }
    }

    private static string ChartLaunchKey(string connectorId, string symbol, string timeframe) =>
        $"{connectorId}|{symbol.ToUpperInvariant()}|{NormalizeTimeframe(timeframe)}";

    private void RegisterPendingChartLaunch(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        Mt5HistoryStatus status,
        int phaseNumber,
        int phaseCount,
        string progressLabel,
        string reason)
    {
        string key = ChartLaunchKey(connectorId, symbol, phase.Timeframe);
        _pendingChartLaunches[key] = new PendingChartLaunch(
            connectorId, symbol, phase, status, phaseNumber, phaseCount, progressLabel, reason);
        _historyProgressWindow?.SetChartLaunchDeferred(
            progressLabel,
            $"{progressLabel} was permanently saved and the history queue is continuing. Chart launch did not finish: {reason} Use Retry Chart Launch; MT5 import will not repeat.",
            _pendingChartLaunches.Count);
        StatusText.Text =
            $"{progressLabel} history is safe. Chart launch was deferred; continuing to the next timeframe.";
    }

    private void ClearPendingChartLaunch(string connectorId, string symbol, string timeframe)
    {
        _pendingChartLaunches.Remove(ChartLaunchKey(connectorId, symbol, timeframe));
        _historyProgressWindow?.SetPendingChartLaunchCount(_pendingChartLaunches.Count);
    }

    private async Task RetryNextPendingChartLaunchAsync()
    {
        if (_pendingChartLaunchRetryRunning)
        {
            StatusText.Text = "A chart-launch retry is already running.";
            return;
        }

        PendingChartLaunch? pending = _pendingChartLaunches.Values.FirstOrDefault();
        if (pending is null)
        {
            StatusText.Text = "There is no failed chart launch to retry.";
            _historyProgressWindow?.SetPendingChartLaunchCount(0);
            return;
        }

        _pendingChartLaunchRetryRunning = true;
        try
        {
            _historyProgressWindow?.UpdateChartLaunch(
                pending.ProgressLabel,
                0,
                GetHistoryLaunchOverallPercent(pending.PhaseNumber, pending.PhaseCount, 0),
                "Retrying chart launch",
                "Using the already verified permanent archive. No MT5 history will be downloaded or rewritten.");

            await LaunchCommittedTimeframeOnChartAsync(
                pending.ConnectorId,
                pending.Symbol,
                pending.Phase,
                pending.Status,
                pending.PhaseNumber,
                pending.PhaseCount,
                pending.ProgressLabel,
                _lifetime.Token);

            ClearPendingChartLaunch(
                pending.ConnectorId, pending.Symbol, pending.Phase.Timeframe);
            StatusText.Text = $"{pending.ProgressLabel} chart launch succeeded from saved history.";
            _historyProgressWindow?.SetChartLaunchRetrySucceeded(
                pending.ProgressLabel, _pendingChartLaunches.Count);
        }
        catch (Exception exception)
        {
            ReportChartLaunchFailure(
                pending.ConnectorId, pending.Symbol, pending.Phase, pending.Status,
                pending.ProgressLabel, exception);
            string key = ChartLaunchKey(
                pending.ConnectorId, pending.Symbol, pending.Phase.Timeframe);
            _pendingChartLaunches[key] = pending with { Reason = exception.Message };
            _historyProgressWindow?.SetChartLaunchDeferred(
                pending.ProgressLabel,
                $"Chart launch still failed: {exception.Message} Permanent candles remain safe and imports are not affected.",
                _pendingChartLaunches.Count);
            StatusText.Text = $"{pending.ProgressLabel} chart launch retry failed: {exception.Message}";
        }
        finally
        {
            _pendingChartLaunchRetryRunning = false;
        }
    }

    private TickLabErrorContext BuildHistoryErrorContext(
        string connectorId,
        string requestId,
        string symbol,
        string progressLabel,
        Mt5HistoryStatus status,
        string suggestedAction)
    {
        var additional = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bridge status"] = status.Status,
            ["Bridge message"] = status.Message,
            ["Series synchronized"] = status.Synchronized.ToString(),
            ["Progress percent"] = status.ProgressPercent.ToString("0.000", CultureInfo.InvariantCulture),
            ["Exported bars"] = status.ExportedBars.ToString(CultureInfo.InvariantCulture),
            ["Target bars"] = status.TargetTotalBars.ToString(CultureInfo.InvariantCulture),
            ["Retry count"] = status.RetryCount.ToString(CultureInfo.InvariantCulture),
            ["Server first unix"] = status.ServerFirstUnix.ToString(CultureInfo.InvariantCulture),
            ["Available first unix"] = status.AvailableFirstUnix.ToString(CultureInfo.InvariantCulture),
            ["Terminal max bars"] = status.TerminalMaxBars.ToString(CultureInfo.InvariantCulture),
            ["Native range partial"] = status.NativeRangePartial.ToString(),
            ["Coverage reason"] = status.CoverageReason
        };

        return new TickLabErrorContext(
            "MT5 history import",
            string.IsNullOrWhiteSpace(status.FailureStage)
                ? status.Status
                : status.FailureStage,
            suggestedAction,
            ErrorCode: string.IsNullOrWhiteSpace(status.FailureCode)
                ? "TL-HIST-BRIDGE"
                : status.FailureCode,
            Symbol: symbol,
            Timeframe: progressLabel,
            ConnectorId: connectorId,
            RequestId: requestId,
            FilePath: status.FailureFilePath,
            BlockStartUnix: status.CurrentBlockStartUnix,
            BlockEndUnix: status.CurrentBlockEndUnix,
            ExpectedRecords: status.FailureExpectedBars > 0
                ? status.FailureExpectedBars
                : status.TargetTotalBars,
            ActualRecords: status.FailureActualBars > 0
                ? status.FailureActualBars
                : status.ExportedBars,
            ExpectedFirstUnix: status.FailureExpectedFirstUnix > 0
                ? status.FailureExpectedFirstUnix
                : status.AvailableFirstUnix,
            ActualFirstUnix: status.FailureActualFirstUnix > 0
                ? status.FailureActualFirstUnix
                : status.FirstBarUnix,
            ExpectedLatestUnix: status.FailureExpectedLatestUnix > 0
                ? status.FailureExpectedLatestUnix
                : status.LatestBarUnix,
            ActualLatestUnix: status.FailureActualLatestUnix > 0
                ? status.FailureActualLatestUnix
                : status.CurrentBarUnix,
            Mt5ErrorCode: status.LastErrorCode == 0 ? null : status.LastErrorCode,
            AdditionalData: additional);
    }

    private void ReportChartLaunchFailure(
        string connectorId,
        string symbol,
        HistoryImportPhase phase,
        Mt5HistoryStatus status,
        string progressLabel,
        Exception exception)
    {
        TickLabErrorEngine.Report(
            exception,
            new TickLabErrorContext(
                "Chart launch",
                "load_verified_history_preview",
                "Permanent history is safe. Use Retry Chart Launch; do not repeat the MT5 import.",
                ErrorCode: "TL-CHART-LAUNCH",
                Symbol: symbol,
                Timeframe: progressLabel,
                ConnectorId: connectorId,
                RequestId: status.RequestId,
                ExpectedRecords: status.ExportedBars,
                ExpectedFirstUnix: status.FirstBarUnix,
                ExpectedLatestUnix: status.LatestBarUnix,
                AdditionalData: new Dictionary<string, string>
                {
                    ["Native timeframe"] = phase.Timeframe,
                    ["History state"] = status.Status
                }),
            TickLabErrorSeverity.Error,
            this);
    }

    private async Task WaitForPublishedCandleSnapshotAsync(
        string connectorId,
        CancellationToken token)
    {
        long previousLength = -1;
        int stableObservations = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            long length = await Task.Run(
                () => _bridgeClient.GetCandlesFileLength(connectorId),
                token);
            if (length > 100 && length == previousLength)
                stableObservations++;
            else
                stableObservations = 0;

            if (stableObservations >= 2)
                return;

            previousLength = length;
            await Task.Delay(200, token);
        }

        throw new IOException(
            "The History Bridge reported 100%, but its published candle file did not become stable within eight seconds.");
    }

    private static IEnumerable<Candle> ValidatePublishedCandleStream(
        IEnumerable<Candle> source,
        string symbol,
        string timeframe,
        int expectedCount,
        long expectedFirstUnix,
        long expectedLatestUnix)
    {
        int count = 0;
        long first = 0;
        long latest = 0;
        long previous = long.MinValue;

        foreach (Candle candle in source)
        {
            if (!string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candle.Timeframe, timeframe, StringComparison.Ordinal) ||
                !candle.IsClosed)
            {
                throw new InvalidDataException(
                    "The published MT5 snapshot contains a different symbol, timeframe or an unclosed candle.");
            }
            if (candle.StartUnix <= previous)
                throw new InvalidDataException("The published MT5 snapshot is duplicated or out of order.");

            count++;
            first = count == 1 ? candle.StartUnix : first;
            latest = candle.StartUnix;
            previous = candle.StartUnix;
            yield return candle;
        }

        if (count != expectedCount || first != expectedFirstUnix || latest != expectedLatestUnix)
        {
            throw new InvalidDataException(
                $"Published MT5 snapshot verification failed: read {count:N0}/{expectedCount:N0}, first {first}, latest {latest}.");
        }
    }

    private double GetHistoryOverallPercent(
        int phaseNumber,
        int phaseCount,
        double completedPhaseFraction) =>
        _historyOverallBasePercent +
        _historyOverallScalePercent *
        ((phaseNumber - 1) + Math.Clamp(completedPhaseFraction, 0, 1)) /
        Math.Max(1, phaseCount);

    private double GetHistoryLaunchOverallPercent(
        int phaseNumber,
        int phaseCount,
        double launchPercent)
    {
        double launchFraction = Math.Clamp(launchPercent, 0, 100) / 100.0;
        double phaseFraction = HistoryImportWorkShare +
            (1.0 - HistoryImportWorkShare) * launchFraction;
        return GetHistoryOverallPercent(phaseNumber, phaseCount, phaseFraction);
    }

    private static int GetChartLaunchPreviewRecords(TimeframeDefinition timeframe)
    {
        if (timeframe.Unit == TimeframeUnit.Second)
            return SecondChartLaunchPreviewRecords;
        if (timeframe.Unit == TimeframeUnit.Minute && timeframe.Quantity == 1)
            return M1ChartLaunchPreviewRecords;
        if (timeframe.Unit == TimeframeUnit.Minute)
            return MinuteChartLaunchPreviewRecords;
        return HigherChartLaunchPreviewRecords;
    }

    private HistoryImportResult ConvertTemporaryImport(
        string connectorId,
        IReadOnlyList<Candle> snapshot,
        string symbol,
        HistoryImportPhase phase,
        Mt5HistoryStatus status,
        CancellationToken token)
    {
        TemporaryHistoryImportResult temporary =
            _temporaryHistoryStore.ReplaceSnapshot(
                connectorId,
                snapshot,
                symbol,
                phase.Timeframe,
                token,
                status.ExportedBars);

        return new HistoryImportResult(
            temporary.Success,
            temporary.Issues.Count == 0
                ? temporary.Message
                : $"{temporary.Message} {temporary.Issues.Count:N0} invalid records were rejected.",
            temporary.Symbol,
            temporary.Timeframe,
            temporary.ImportedRecords,
            0);
    }

    private async Task SelectBridgeSourceAsync(
        string connectorId,
        string symbol,
        string timeframe,
        CancellationToken token)
    {
        Mt5ConnectorSummary? current = await Task.Run(
            () => _bridgeClient.FindConnector(connectorId),
            token);

        if (current is not null &&
            string.Equals(current.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                NormalizeTimeframe(current.Timeframe),
                timeframe,
                StringComparison.Ordinal))
        {
            _selectedConnector = current;
            return;
        }

        _bridgeClient.SendChartSelectionRequest(
            connectorId,
            symbol,
            timeframe);

        DateTime deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(150, token);

            current = await Task.Run(
                () => _bridgeClient.FindConnector(connectorId),
                token);

            if (current is null)
                continue;

            _selectedConnector = current;
            if (string.Equals(current.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    NormalizeTimeframe(current.Timeframe),
                    timeframe,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new TimeoutException(
            $"MT5 did not switch to {symbol} {timeframe}. Make sure AutoTrading and the TickLab bridge are running.");
    }

    private async Task EnsureSourceAttachedAsync()
    {
        if (_selectedConnector is null)
            return;

        bool attached = string.Equals(
                            _selectedConnector.Symbol,
                            _requestedSymbol,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            NormalizeTimeframe(_selectedConnector.Timeframe),
                            _sourceTimeframe,
                            StringComparison.Ordinal);

        if (attached)
            return;

        _bridgeClient.SendChartSelectionRequest(
            _selectedConnector.ConnectorId,
            _requestedSymbol,
            _sourceTimeframe);
        await WaitForChartSelectionAsync(_selectionGeneration, _lifetime.Token);
    }

    private async Task<bool> OpenCandleHistoryWindowAsync()
    {
        string connectorId = _selectedConnector?.ConnectorId
            ?? _preferences.LastConnectorId;
        if (!Mt5Paths.IsValidConnectorId(connectorId))
        {
            StatusText.Text = "No saved TickLab connector history was found.";
            return false;
        }

        _historyStore.RescanPortableHistory(connectorId);
        string[] symbols = _historyStore.GetSavedInstruments(connectorId)
            .Select(item => item.Symbol)
            .Append(_requestedSymbol)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var window = new CandleHistoryWindow(
            _historyStore,
            connectorId,
            symbols,
            _requestedSymbol)
        {
            Owner = this
        };

        bool? result = window.ShowDialog();
        if (result == true && window.RequestedAction == CandleHistoryWindowAction.RefreshAll)
            await RefreshAllCandleHistoryAsync(window.OperationSymbol);
        if (window.VisibilityChanged)
            await ReloadActiveChartAfterHistoryVisibilityChangeAsync();
        return window.ReturnToConnections;
    }

    private async Task<bool> OpenTickHistoryWindowAsync()
    {
        string connectorId = _selectedConnector?.ConnectorId
            ?? _preferences.LastConnectorId;
        if (!Mt5Paths.IsValidConnectorId(connectorId))
        {
            StatusText.Text = "No saved TickLab connector history was found.";
            return false;
        }

        _historyStore.RescanPortableHistory(connectorId);
        string[] symbols = _historyStore.GetSavedInstruments(connectorId)
            .Select(item => item.Symbol)
            .Append(_requestedSymbol)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var window = new TickHistoryWindow(
            _historyStore,
            connectorId,
            symbols,
            _requestedSymbol)
        {
            Owner = this
        };

        bool? result = window.ShowDialog();
        if (result == true)
        {
            switch (window.RequestedAction)
            {
                case TickHistoryWindowAction.RefreshTicks:
                    await RefreshTickHistoryAsync(window.OperationSymbol, window.OperationSegmentKey);
                    break;
                case TickHistoryWindowAction.BuildCandles:
                    await BuildCandlesFromTicksAsync(connectorId, window.OperationSymbol, window.OperationSegmentKey);
                    break;
            }
        }
        if (window.VisibilityChanged)
            await ReloadActiveChartAfterHistoryVisibilityChangeAsync();
        return window.ReturnToConnections;
    }

    private async Task RefreshTickHistoryAsync(string symbol, string? segmentKey)
    {
        if (_selectedConnector is null)
            return;

        long? minimumStart = null;
        string? onlySegment = null;
        if (!string.IsNullOrWhiteSpace(segmentKey))
        {
            minimumStart = PersistentHistoryStore.GetSegmentStartUnix(segmentKey);
            onlySegment = segmentKey;
        }
        else
        {
            TickHistoryFolderSummary? earliest = _historyStore
                .GetTickHistoryFolders(_selectedConnector.ConnectorId, symbol)
                .OrderBy(item => item.StartUnix)
                .FirstOrDefault();
            minimumStart = earliest?.StartUnix;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(segmentKey)
                ? $"Compare all locally saved {symbol} tick folders with every matching tick MT5 still provides, then repair missing or incorrect data?"
                : $"Compare {symbol} tick folder {segmentKey} with MT5 and repair every available missing or incorrect tick?",
            "Refresh Tick Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        var phase = new HistoryImportPhase(
            "PERIOD_M1",
            true,
            string.IsNullOrWhiteSpace(segmentKey) ? "all saved tick folders" : $"tick folder {segmentKey}",
            false,
            minimumStart,
            onlySegment,
            ImportCandles: false);

        await ExecuteHistoryOperationAsync(
            "Refresh ticks",
            "refresh",
            new[] { phase },
            syncTicks: true,
            successMessage: "Tick history was compared with available MT5 timestamp, price, volume and flag data and repaired.",
            operationSymbol: symbol);
    }

    private async Task BuildCandlesFromTicksAsync(
        string connectorId,
        string symbol,
        string? segmentKey)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(segmentKey))
        {
            StatusText.Text = "Select one three-month tick folder first.";
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Build only missing M1-to-Monthly candle slots from {symbol} ticks in {segmentKey}? Valid native MT5 candles will never be replaced.",
            "Build Candles from Ticks",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        NativeCandleFileSummary? savedMetadata = _historyStore
            .GetNativeCandleFiles(connectorId, symbol)
            .FirstOrDefault(item => item.Digits > 0 && item.Point > 0);
        Mt5SymbolInfo? liveSymbol = _availableSymbols
            .FirstOrDefault(item => string.Equals(item.Name, symbol, StringComparison.OrdinalIgnoreCase));
        int digits = liveSymbol?.Digits
            ?? savedMetadata?.Digits
            ?? (_selectedConnector is not null &&
                string.Equals(_selectedConnector.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                    ? _selectedConnector.Digits
                    : 8);
        double point = savedMetadata is { Point: > 0 } validMetadata
            ? validMetadata.Point
            : _selectedConnector is not null &&
              string.Equals(_selectedConnector.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
              _selectedConnector.Point > 0
                ? _selectedConnector.Point
                : Math.Pow(10, -Math.Max(0, digits));
        int serverOffset = _selectedConnector is not null &&
                           string.Equals(_selectedConnector.ConnectorId, connectorId, StringComparison.Ordinal)
            ? _selectedConnector.ServerUtcOffsetMinutes
            : 0;

        try
        {
            StatusText.Text = $"Building missing candles from {symbol} {segmentKey} ticks...";
            TickProjectionResult result = await Task.Run(() =>
                _historyStore.BuildMissingNativeCandlesFromTicks(
                    connectorId,
                    symbol,
                    segmentKey,
                    digits,
                    point,
                    serverOffset,
                    _lifetime.Token),
                _lifetime.Token);
            StatusText.Text = result.Message;
            if (string.Equals(symbol, _requestedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                await LoadLocalChartAsync(
                    _selectionGeneration,
                    _requestedSymbol,
                    _activeTimeframe,
                    _historySelection,
                    _lifetime.Token);
            }
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Tick projection",
                    "build_missing_candles",
                    "Copy diagnostics. Keep the raw tick folder unchanged and retry the selected segment.",
                    ErrorCode: "TL-TICK-PROJECT",
                    Symbol: symbol,
                    ConnectorId: connectorId,
                    FilePath: segmentKey),
                TickLabErrorSeverity.Error,
                this);
        }
    }

    private async void OpenHistoryWindow()
    {
        if (_selectedConnector is null)
        {
            StatusText.Text = "Select an MT5 connection first.";
            return;
        }

        var window = new HistoryManagementWindow(
            _historyStore,
            _externalHistoryStore,
            _selectedConnector,
            _availableSymbols,
            _historySelection.Mode,
            _historySelection.SegmentKeys ?? Array.Empty<string>())
        {
            Owner = this
        };

        bool? historyResult = window.ShowDialog();
        _activeInstrumentSaving = _historyStore.IsInstrumentSaving(
            _selectedConnector.ConnectorId,
            _requestedSymbol);

        if (window.ExternalDataChanged)
        {
            await LoadLocalChartAsync(
                _selectionGeneration,
                _requestedSymbol,
                _activeTimeframe,
                _historySelection,
                _lifetime.Token);
        }

        if (historyResult != true)
            return;

        switch (window.RequestedAction)
        {
            case HistoryManagementAction.RefreshSavedHistory:
                await RefreshSavedHistoryAsync(false, window.OperationSymbol);
                return;
            case HistoryManagementAction.RecheckLatest60Days:
                await RefreshSavedHistoryAsync(true, window.OperationSymbol);
                return;
            case HistoryManagementAction.ApplyDisplay:
                break;
            default:
                return;
        }

        if (window.SelectedDisplay is not null)
        {
            _historySelection = window.SelectedDisplay;
            _preferences = _preferences with
            {
                HistoryDisplayMode = _historySelection.Mode,
                SelectedHistorySegments = _historySelection.SegmentKeys ?? Array.Empty<string>()
            };
            SaveWorkspace();
            await LoadLocalChartAsync(
                _selectionGeneration,
                _requestedSymbol,
                _activeTimeframe,
                _historySelection,
                _lifetime.Token);
        }
    }

    private async Task RefreshConnectorStateAsync()
    {
        if (_selectedConnector is null)
            return;

        string connectorId = _selectedConnector.ConnectorId;
        Mt5ConnectorSummary? current = await Task.Run(
            () => _bridgeClient.FindConnector(connectorId),
            _lifetime.Token);

        bool healthy = current?.IsConnected == true;
        if (healthy)
        {
            bool reconnected = !_bridgeWasAvailable;
            _lastHealthyConnectorObservationUtc = DateTime.UtcNow;
            _consecutiveConnectorFailures = 0;
            _bridgeWasAvailable = true;
            _selectedConnector = current;
            ConnectionText.Text = $"MT5 Connected • Live  ·  {current!.DisplayName}";
            MarketStateText.Text = "Connected";
            MarketStateText.Foreground = new SolidColorBrush(Color.FromRgb(38, 194, 129));
            ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(38, 194, 129));

            if (reconnected && !_historyOperationRunning &&
                !string.IsNullOrWhiteSpace(_requestedSymbol))
            {
                _ = RunStartupRefreshAsync(_requestedSymbol);
            }

            return;
        }

        _consecutiveConnectorFailures++;
        bool insideGrace =
            _lastHealthyConnectorObservationUtc != DateTime.MinValue &&
            DateTime.UtcNow - _lastHealthyConnectorObservationUtc <
                ConnectorOfflineGrace;

        // Do not let one malformed read, a file swap, antivirus delay, or an
        // older bridge overwrite make the visible status flash. Offline is a
        // stable state transition only after several misses and the local
        // grace period has elapsed.
        if (insideGrace || _consecutiveConnectorFailures < 3)
            return;

        _bridgeWasAvailable = false;
        MarketStateText.Text = "Bridge unavailable";
        MarketStateText.Foreground = new SolidColorBrush(Color.FromRgb(103, 116, 135));
        ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(103, 116, 135));
        ConnectionText.Text = "MT5 Disconnected";
    }

    private async Task RefreshSymbolsAsync(bool force)
    {
        if (_selectedConnector is null)
            return;

        string connectorId = _selectedConnector.ConnectorId;
        DateTime write = _bridgeClient.GetSymbolsLastWriteUtc(connectorId);
        if (!force && write <= _lastSymbolsWriteUtc)
            return;

        if (force)
        {
            // Symbol refresh is opportunistic. A short MT5/antivirus sharing
            // lock must not abort connection or raise a maintenance error; the
            // last valid symbols.psv remains usable and the next timer retries.
            try
            {
                _bridgeClient.RequestSymbolsRefresh(connectorId);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(150, _lifetime.Token);
        }

        IReadOnlyList<Mt5SymbolInfo> symbols = await Task.Run(
            () => _bridgeClient.ReadSymbols(connectorId),
            _lifetime.Token);

        if (symbols.Count == 0)
            return;

        _availableSymbols = symbols;
        _lastSymbolsWriteUtc = _bridgeClient.GetSymbolsLastWriteUtc(connectorId);
        RefreshMarketWorkspace();
    }


    private Brush DrawingUiBrush(string resourceKey, Color fallback) =>
        Application.Current?.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

    private void BuildDrawingToolbar()
    {
        if (DrawingCategoryButtonsPanel is null)
            return;

        Brush railBrush = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        DrawingCategoryButtonsPanel.Children.Clear();
        foreach (DrawingToolCategory category in Enum.GetValues<DrawingToolCategory>())
        {
            DrawingToolDefinition shortcut = ResolveCategoryShortcut(category);
            DrawingCategoryButtonsPanel.Children.Add(CreateCategorySplitButton(category, shortcut, railBrush));
        }

        DrawingBrushButton.Content = DrawingToolIconFactory.CreateToolIcon(DrawingToolCatalog.Find("brush")!, 30, railBrush);
        DrawingFavoritesButton.Content = DrawingToolIconFactory.CreateActionIcon("favorites", 30, railBrush);
        DrawingMeasureButton.Content = DrawingToolIconFactory.CreateActionIcon("measure", 30, railBrush);
        DrawingZoomButton.Content = DrawingToolIconFactory.CreateActionIcon("zoom", 30, railBrush);
        DrawingMagnetButton.Content = DrawingToolIconFactory.CreateActionIcon("magnet", 30, railBrush);
        StayDrawingModeButton.Content = DrawingToolIconFactory.CreateActionIcon("stay", 30, railBrush);
        DrawingLockButton.Content = DrawingToolIconFactory.CreateActionIcon("lock", 30, railBrush);
        DrawingHideButton.Content = DrawingToolIconFactory.CreateActionIcon("hide", 30, railBrush);
        DrawingObjectTreeButton.Content = DrawingToolIconFactory.CreateActionIcon("tree", 30, railBrush);
        DrawingSyncButton.Content = DrawingToolIconFactory.CreateActionIcon("sync", 30, railBrush);
        DrawingUndoButton.Content = DrawingToolIconFactory.CreateActionIcon("undo", 30, railBrush);
        DrawingRedoButton.Content = DrawingToolIconFactory.CreateActionIcon("redo", 30, railBrush);
        DrawingRemoveButton.Content = DrawingToolIconFactory.CreateActionIcon("delete", 30, railBrush);
        DrawingToolbarScrollUpButton.Content = DrawingToolIconFactory.CreateActionIcon("chevron-up", 22.5, railBrush);
        DrawingToolbarScrollDownButton.Content = DrawingToolIconFactory.CreateActionIcon("chevron-down", 22.5, railBrush);
        DrawingToolbarCollapseButton.Content = DrawingToolIconFactory.CreateActionIcon("collapse", 24, railBrush);
        UpdateDrawingToolbarState();
        RefreshDrawingFavoritesWindow();

        // A category window opens only when its rail icon is clicked.
        // It now lives directly beside the rail instead of inside the right workspace.
        if (_openDrawingCategory is not null &&
            DrawingCategoryPaletteBorder.Visibility == Visibility.Visible)
        {
            RebuildDrawingCategoryPalette();
        }
    }

    private DrawingToolDefinition ResolveCategoryShortcut(DrawingToolCategory category)
    {
        if (_lastDrawingToolByCategory.TryGetValue(category, out string? rememberedId))
        {
            DrawingToolDefinition? remembered = DrawingToolCatalog.Find(rememberedId);
            if (remembered?.Category == category)
                return remembered;
        }

        DrawingToolDefinition? active = DrawingToolCatalog.Find(CandleChart.ActiveDrawingToolId);
        if (active?.Category == category)
        {
            _lastDrawingToolByCategory[category] = active.Id;
            return active;
        }

        foreach (string id in CandleChart.RecentDrawingToolIds)
        {
            DrawingToolDefinition? recent = DrawingToolCatalog.Find(id);
            if (recent?.Category == category)
            {
                _lastDrawingToolByCategory[category] = recent.Id;
                return recent;
            }
        }

        string preferredId = category switch
        {
            DrawingToolCategory.Cursor => "cursor-crosshair",
            DrawingToolCategory.TrendLine => "trend-line",
            DrawingToolCategory.FibonacciGann => "fib-retracement",
            DrawingToolCategory.Pattern => "xabcd-pattern",
            DrawingToolCategory.PredictionMeasurement => "long-position",
            DrawingToolCategory.Geometry => "brush",
            DrawingToolCategory.Annotation => "text",
            DrawingToolCategory.IconsMedia => "emojis",
            _ => string.Empty
        };
        DrawingToolDefinition fallback = DrawingToolCatalog.Find(preferredId) ?? DrawingToolCatalog.InCategory(category).First();
        _lastDrawingToolByCategory[category] = fallback.Id;
        return fallback;
    }

    private void RememberCategoryShortcut(string toolId)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(toolId);
        if (tool is not null)
            _lastDrawingToolByCategory[tool.Category] = tool.Id;
    }

    private UIElement CreateCategorySplitButton(
        DrawingToolCategory category,
        DrawingToolDefinition shortcut,
        Brush railBrush)
    {
        var host = new Grid
        {
            Width = 44.5,
            Height = 38,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            ToolTip = $"{shortcut.DisplayName} — {DrawingToolCatalog.CategoryName(category)}"
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8.5) });

        var primary = new Button
        {
            Style = (Style)FindResource("DrawingRailButton"),
            Width = 36,
            Height = 38,
            MinHeight = 38,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = DrawingToolIconFactory.CreateToolIcon(shortcut, 30, railBrush),
            Tag = string.Equals(CandleChart.ActiveDrawingToolId, shortcut.Id, StringComparison.OrdinalIgnoreCase) ? "Active" : null,
            ToolTip = shortcut.DisplayName
        };
        primary.Click += (_, _) =>
        {
            if (ActivateDrawingToolFromUi(shortcut.Id))
                CloseDrawingCategoryPalette();
        };
        primary.PreviewMouseRightButtonUp += (_, e) =>
        {
            ShowDrawingToolFavoritesContextMenu(primary, shortcut);
            e.Handled = true;
        };

        var flyout = new Button
        {
            Style = (Style)FindResource("DrawingRailButton"),
            Width = 8.5,
            Height = 38,
            MinHeight = 38,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = railBrush,
            Content = DrawingToolIconFactory.CreateActionIcon("chevron-right", 8, railBrush),
            Tag = _openDrawingCategory == category ? "Active" : null,
            Visibility = Visibility.Hidden,
            ToolTip = $"Open {DrawingToolCatalog.CategoryName(category)} folder",
            Effect = null,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        flyout.Click += (_, _) => OpenDrawingCategoryPalette(category);
        host.MouseEnter += (_, _) => flyout.Visibility = Visibility.Visible;
        host.MouseLeave += (_, _) => flyout.Visibility = Visibility.Hidden;
        Grid.SetColumn(flyout, 1);
        host.Children.Add(primary);
        host.Children.Add(flyout);
        return host;
    }

    private UIElement CreateReferenceTrendLineCategoryButton(
        DrawingToolCategory category,
        DrawingToolDefinition shortcut,
        Brush railBrush)
    {
        var button = new Button
        {
            Style = (Style)FindResource("DrawingRailButton"),
            Width = 46,
            Height = 38,
            MinHeight = 38,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = DrawingToolIconFactory.CreateToolIcon(shortcut, 30, railBrush),
            Tag = _openDrawingCategory == category ? "Active" : null,
            ToolTip = DrawingToolCatalog.CategoryName(category)
        };
        button.Click += (_, _) =>
        {
            if (_openDrawingCategory == category && DrawingCategoryPaletteBorder.Visibility == Visibility.Visible)
                CloseDrawingCategoryPalette();
            else
                OpenDrawingCategoryPalette(category);
        };
        button.PreviewMouseRightButtonUp += (_, e) =>
        {
            ShowDrawingToolFavoritesContextMenu(button, shortcut);
            e.Handled = true;
        };
        return button;
    }

    private void OpenDrawingCategoryPalette(DrawingToolCategory category)
    {
        _brushPaletteOpen = false;
        _openDrawingCategory = category;
        DrawingCategoryPaletteTitle.Text = DrawingToolCatalog.CategoryName(category);
        DrawingCategoryPaletteIconHost.Child = DrawingToolIconFactory.CreateCategoryIcon(
            category, 20, DrawingUiBrush("AccentBrightBrush", Color.FromRgb(112, 183, 255)));
        DrawingCategoryPaletteSearchBox.Text = string.Empty;
        ConfigureDrawingCategoryPaletteAppearance(category);

        // TradingView-style flyout: overlay the chart beside the rail instead of
        // resizing the chart whenever a drawing folder is opened.
        DrawingPaletteColumn.Width = new GridLength(0.0);
        DrawingPaletteSplitterColumn.Width = new GridLength(0.0);
        DrawingCategoryPaletteBorder.Width = category == DrawingToolCategory.Cursor ? 258.0 : category == DrawingToolCategory.TrendLine ? 276.0 : category == DrawingToolCategory.FibonacciGann ? 292.0 : 265.0;
        DrawingCategoryPaletteBorder.Visibility = Visibility.Visible;
        DrawingPaletteSplitter.Visibility = Visibility.Collapsed;

        RebuildDrawingCategoryPalette();
        PositionDrawingCategoryPalette(category);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => PositionDrawingCategoryPalette(category)));
        UpdateDrawingToolbarState();
    }

    private void ConfigureDrawingCategoryPaletteAppearance(DrawingToolCategory category)
    {
        bool referenceCursor = category == DrawingToolCategory.Cursor;
        bool referenceLines = category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann;
        if (referenceCursor || referenceLines)
        {
            DrawingCategoryPaletteBorder.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
            DrawingCategoryPaletteBorder.BorderBrush = DrawingUiBrush("BorderBrush", Color.FromRgb(51, 65, 85));
            DrawingCategoryPaletteBorder.BorderThickness = new Thickness(1);
            DrawingCategoryPaletteBorder.CornerRadius = new CornerRadius(3);
            DrawingCategoryPaletteBorder.MaxHeight = referenceCursor ? 310 : 690;
            DrawingCategoryPaletteScrollViewer.Margin = new Thickness(0, referenceCursor ? 2 : 4, 0, referenceCursor ? 2 : 5);
            DrawingCategoryPaletteScrollViewer.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
            return;
        }

        DrawingCategoryPaletteBorder.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
        DrawingCategoryPaletteBorder.BorderBrush = DrawingUiBrush("BorderStrongBrush", Color.FromRgb(52, 65, 81));
        DrawingCategoryPaletteBorder.BorderThickness = new Thickness(1);
        DrawingCategoryPaletteBorder.CornerRadius = new CornerRadius(4);
        DrawingCategoryPaletteBorder.MaxHeight = 720;
        DrawingCategoryPaletteScrollViewer.Margin = new Thickness(4);
        DrawingCategoryPaletteScrollViewer.Background = Brushes.Transparent;
    }

    private void PositionDrawingCategoryPalette(DrawingToolCategory category)
    {
        // Flyouts originate beside the rail button that opened them, then move upward
        // only as much as needed to remain completely inside the TickLab frame.
        int categoryIndex = Math.Max(0, Array.IndexOf(Enum.GetValues<DrawingToolCategory>(), category));
        double desiredTop = 4.0 + categoryIndex * 38.0;
        FrameworkElement? host = DrawingCategoryPaletteBorder.Parent as FrameworkElement;
        double availableHeight = host?.ActualHeight > 1 ? host.ActualHeight : ActualHeight;
        double flyoutHeight = DrawingCategoryPaletteBorder.ActualHeight > 1
            ? DrawingCategoryPaletteBorder.ActualHeight
            : Math.Min(DrawingCategoryPaletteBorder.MaxHeight, Math.Max(260.0, availableHeight - 12.0));
        double maxTop = Math.Max(4.0, availableHeight - flyoutHeight - 5.0);
        double top = Math.Clamp(desiredTop, 4.0, maxTop);
        DrawingCategoryPaletteBorder.Margin = new Thickness(4, top, 0, 5);
        DrawingCategoryPaletteBorder.MaxHeight = Math.Max(180.0, availableHeight - top - 5.0);
    }

    private void CloseDrawingCategoryPalette()
    {
        _openDrawingCategory = null;
        _brushPaletteOpen = false;
        DrawingCategoryPaletteBorder.Visibility = Visibility.Collapsed;
        DrawingPaletteSplitter.Visibility = Visibility.Collapsed;
        DrawingPaletteColumn.Width = new GridLength(0.0);
        DrawingPaletteSplitterColumn.Width = new GridLength(0.0);
        UpdateDrawingToolbarState();
    }

    private void RebuildDrawingCategoryPalette()
    {
        if (_brushPaletteOpen)
        {
            RebuildBrushPalette();
            return;
        }

        DrawingCategoryPaletteRowsPanel.Children.Clear();
        if (_openDrawingCategory is not DrawingToolCategory category)
            return;

        string query = DrawingCategoryPaletteSearchBox.Text.Trim();
        IEnumerable<DrawingToolDefinition> tools = DrawingToolCatalog.InCategory(category);
        if (category == DrawingToolCategory.Cursor)
        {
            RebuildReferenceCursorPalette(tools);
            return;
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            tools = tools.Where(item =>
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        string? activeSection = null;
        foreach (DrawingToolDefinition tool in tools)
        {
            string section = DrawingPaletteSectionName(tool);
            if (!string.Equals(activeSection, section, StringComparison.Ordinal))
            {
                activeSection = section;
                DrawingCategoryPaletteRowsPanel.Children.Add(CreateDrawingPaletteSectionHeader(section));
            }
            DrawingCategoryPaletteRowsPanel.Children.Add(CreateDrawingPaletteRow(tool));
        }
    }

    private void RebuildReferenceCursorPalette(IEnumerable<DrawingToolDefinition> source)
    {
        DrawingCategoryPaletteRowsPanel.Children.Clear();
        string[] orderedIds = { "cursor-crosshair", "cursor-dot", "cursor-arrow", "cursor-demo", "cursor-magic", "eraser" };
        Dictionary<string, DrawingToolDefinition> byId = source.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedIds.Length; i++)
        {
            if (!byId.TryGetValue(orderedIds[i], out DrawingToolDefinition? tool))
                continue;
            if (i == 5)
                DrawingCategoryPaletteRowsPanel.Children.Add(CreateReferenceCursorSeparator());
            DrawingCategoryPaletteRowsPanel.Children.Add(CreateReferenceCursorPaletteRow(tool));
        }
        DrawingCategoryPaletteRowsPanel.Children.Add(CreateReferenceCursorSeparator());
        DrawingCategoryPaletteRowsPanel.Children.Add(CreateReferenceCursorTooltipToggle());
    }

    private UIElement CreateReferenceCursorSeparator() => new Border
    {
        Height = 1,
        Margin = new Thickness(12, 5, 12, 5),
        Background = DrawingUiBrush("BorderBrush", Color.FromRgb(51, 65, 85)),
        SnapsToDevicePixels = true
    };

    private UIElement CreateReferenceCursorPaletteRow(DrawingToolDefinition tool)
    {
        bool active = string.Equals(CandleChart.ActiveDrawingToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
        bool favorite = CandleChart.IsDrawingFavorite(tool.Id);
        Brush panel = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
        Brush text = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        Brush hover = DrawingUiBrush("ControlHoverBrush", Color.FromRgb(23, 31, 43));
        Brush selected = DrawingUiBrush("SelectionBrush", Color.FromRgb(36, 50, 71));
        Brush selectedText = DrawingUiBrush("SelectionTextBrush", Color.FromRgb(248, 250, 252));
        var row = new Border
        {
            Height = 35,
            Margin = new Thickness(5, 0, 5, 0),
            Padding = new Thickness(9, 0, 5, 0),
            CornerRadius = new CornerRadius(2),
            Background = active ? selected : panel,
            Cursor = Cursors.Hand,
            Tag = tool.Id,
            SnapsToDevicePixels = true
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var iconHost = new Border
        {
            Width = 23, Height = 23, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
            Child = DrawingToolIconFactory.CreateToolIcon(tool, 18, active ? selectedText : text)
        };
        grid.Children.Add(iconHost);

        var name = new TextBlock
        {
            Text = tool.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? selectedText : text,
            FontSize = 12.8,
            FontWeight = FontWeights.Normal
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var star = new Button
        {
            Width = 25, Height = 25, MinHeight = 25, Padding = new Thickness(0), Margin = new Thickness(0),
            Content = favorite ? "★" : "☆", FontSize = 16,
            Foreground = active ? selectedText : text,
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            ToolTip = favorite ? "Remove from Favorites" : "Add to Favorites",
            Visibility = favorite ? Visibility.Visible : Visibility.Hidden,
            Tag = tool.Id
        };
        star.Click += (_, e) =>
        {
            bool added = CandleChart.ToggleDrawingFavorite(tool.Id);
            RebuildDrawingCategoryPalette();
            RefreshDrawingFavoritesWindow();
            if (added) OpenDrawingFavoritesWindow(forceShow: true);
            e.Handled = true;
        };
        Grid.SetColumn(star, 2);
        grid.Children.Add(star);
        row.Child = grid;

        void SetHover(bool isHover)
        {
            bool isActive = string.Equals(CandleChart.ActiveDrawingToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
            Brush stateText = isActive ? selectedText : text;
            row.Background = isActive ? selected : isHover ? hover : panel;
            name.Foreground = stateText;
            iconHost.Child = DrawingToolIconFactory.CreateToolIcon(tool, 18, stateText);
            star.Foreground = stateText;
            star.Visibility = isHover || CandleChart.IsDrawingFavorite(tool.Id) ? Visibility.Visible : Visibility.Hidden;
        }
        row.MouseEnter += (_, _) => SetHover(true);
        row.MouseLeave += (_, _) => SetHover(false);
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
            if (ActivateDrawingToolFromUi(tool.Id))
            {
                CloseDrawingCategoryPalette();
                e.Handled = true;
            }
        };
        row.PreviewMouseRightButtonUp += (_, e) =>
        {
            ShowDrawingToolFavoritesContextMenu(row, tool);
            e.Handled = true;
        };
        return row;
    }

    private UIElement CreateReferenceCursorTooltipToggle()
    {
        var host = new Grid { Height = 42, Margin = new Thickness(10, 0, 8, 1) };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
        host.Children.Add(new TextBlock
        {
            Text = "Values tooltip on long press",
            Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240)),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        });

        var track = new Border
        {
            Width = 38, Height = 20, CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Padding = new Thickness(2),
            Tag = CandleChart.CursorValuesTooltipOnLongPress
        };
        var thumb = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        track.Child = thumb;
        void RefreshToggle()
        {
            bool enabled = CandleChart.CursorValuesTooltipOnLongPress;
            track.Tag = enabled;
            track.Background = enabled ? new SolidColorBrush(Color.FromRgb(20, 20, 22)) : new SolidColorBrush(Color.FromRgb(185, 188, 194));
            thumb.HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        RefreshToggle();
        track.MouseLeftButtonUp += (_, e) =>
        {
            bool enabled = !CandleChart.CursorValuesTooltipOnLongPress;
            foreach (ChartRuntimeContext context in _chartContexts.Values)
                context.Chart.SetCursorValuesTooltipOnLongPress(enabled);
            RefreshToggle();
            SaveWorkspace();
            e.Handled = true;
        };
        Grid.SetColumn(track, 1);
        host.Children.Add(track);
        return host;
    }

    private static string DrawingPaletteSectionName(DrawingToolDefinition tool) => tool.Category switch
    {
        DrawingToolCategory.Cursor => tool.Id is "eraser" or "selection" ? "ERASER & SELECTION" : "CURSORS",
        DrawingToolCategory.TrendLine => tool.Id switch
        {
            "parallel-channel" or "regression-trend" or "flat-top-bottom" or "disjoint-channel" => "CHANNELS",
            "pitchfork" or "schiff-pitchfork" or "modified-schiff-pitchfork" or "inside-pitchfork" => "PITCHFORKS",
            _ => "LINES"
        },
        DrawingToolCategory.FibonacciGann => tool.Id switch
        {
            "gann-box" or "gann-square-fixed" or "gann-square" or "gann-fan" => "GANN",
            _ => "FIBONACCI"
        },
        DrawingToolCategory.Pattern => tool.Id switch
        {
            "elliott-impulse" or "elliott-triangle" or "elliott-triple-combo" or "elliott-correction" or "elliott-double-combo" => "ELLIOTT WAVES",
            "cyclic-lines" or "time-cycles" or "sine-line" => "CYCLES",
            _ => "PATTERNS"
        },
        DrawingToolCategory.PredictionMeasurement => tool.Id switch
        {
            "anchored-vwap" or "fixed-volume-profile" or "anchored-volume-profile" => "VOLUME BASED",
            "date-range" or "price-range" or "date-price-range" => "MEASURES",
            _ => "FORECASTING"
        },
        DrawingToolCategory.Geometry => tool.Id switch
        {
            "pen" or "brush" or "highlighter" => "BRUSHES",
            "arrow" or "arrow-marker" or "arrow-mark-left" or "arrow-mark-right" or "arrow-mark-up" or "arrow-mark-down" => "ARROWS",
            _ => "SHAPES"
        },
        DrawingToolCategory.Annotation => tool.Id is "image" or "post" or "idea" ? "CONTENT" : "TEXT AND NOTES",
        DrawingToolCategory.IconsMedia => "MEDIA",
        _ => DrawingToolCatalog.CategoryName(tool.Category).ToUpperInvariant()
    };

    private UIElement CreateDrawingPaletteSectionHeader(string section)
    {
        bool referenceLines = _openDrawingCategory is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann;
        return new TextBlock
        {
            Text = section,
            Margin = referenceLines ? new Thickness(14, 9, 8, 4) : new Thickness(8, 7, 5, 3),
            Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(113, 131, 155)),
            FontSize = referenceLines ? 9.0 : 9.5,
            FontWeight = referenceLines ? FontWeights.Medium : FontWeights.SemiBold
        };
    }

    private static string DrawingShortcut(DrawingToolDefinition tool) => tool.Id switch
    {
        "trend-line" => "Alt + T",
        "horizontal-line" => "Alt + H",
        "horizontal-ray" => "Alt + J",
        "vertical-line" => "Alt + V",
        "cross-line" => "Alt + C",
        "fib-retracement" => "Alt + F",
        "rectangle" => "Alt + Shift + R",
        _ => string.Empty
    };

    private UIElement CreateDrawingPaletteRow(DrawingToolDefinition tool)
    {
        if (_openDrawingCategory is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann)
            return CreateReferenceTrendLinePaletteRow(tool);

        bool active = string.Equals(CandleChart.ActiveDrawingToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
        var row = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(6, 0, 2, 0),
            Background = active
                ? DrawingUiBrush("AccentSoftBrush", Color.FromRgb(16, 44, 79))
                : Brushes.Transparent,
            BorderBrush = active
                ? DrawingUiBrush("AccentBrightBrush", Color.FromRgb(47, 128, 237))
                : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = tool.Id
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var iconHost = new Border
        {
            Width = 23,
            Height = 23,
            Background = Brushes.Transparent,
            Child = DrawingToolIconFactory.CreateToolIcon(tool, 18, DrawingUiBrush("TextBrush", Color.FromRgb(199, 212, 229)))
        };
        grid.Children.Add(iconHost);

        var text = new TextBlock
        {
            Text = tool.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 5, 0),
            Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240)),
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        string shortcut = DrawingShortcut(tool);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            actions.Children.Add(new TextBlock
            {
                Text = shortcut, Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,3,0)
            });
        }
        bool favorite = CandleChart.IsDrawingFavorite(tool.Id);
        var star = new Button
        {
            Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(0), Margin = new Thickness(0),
            Content = DrawingToolIconFactory.CreateActionIcon(favorite ? "star-filled" : "favorites", 15,
                favorite ? DrawingUiBrush("WarningBrush", Color.FromRgb(245, 158, 11)) : DrawingUiBrush("MutedTextBrush", Color.FromRgb(100, 116, 139))),
            Foreground = favorite ? DrawingUiBrush("WarningBrush", Color.FromRgb(245, 158, 11)) : DrawingUiBrush("MutedTextBrush", Color.FromRgb(100, 116, 139)),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Tag = tool.Id,
            ToolTip = favorite ? "Remove from favorites" : "Add to favorites",
            Visibility = favorite ? Visibility.Visible : Visibility.Hidden
        };
        star.Click += (_, e) =>
        {
            bool added = CandleChart.ToggleDrawingFavorite(tool.Id);
            RebuildDrawingCategoryPalette();
            RefreshDrawingFavoritesWindow();
            if (added) OpenDrawingFavoritesWindow(forceShow: true);
            e.Handled = true;
        };
        actions.Children.Add(star);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        row.Child = grid;
        row.MouseEnter += (_, _) =>
        {
            row.Background = DrawingUiBrush("PanelAltBrush", Color.FromRgb(18, 37, 60));
            if (!favorite)
                star.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = active
                ? DrawingUiBrush("AccentSoftBrush", Color.FromRgb(16, 44, 79))
                : Brushes.Transparent;
            if (!favorite)
                star.Visibility = Visibility.Hidden;
        };
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
            bool selected = ActivateDrawingToolFromUi(tool.Id);
            if (selected)
            {
                CloseDrawingCategoryPalette();
                e.Handled = true;
            }
            else RebuildDrawingCategoryPalette();
        };
        row.PreviewMouseRightButtonUp += (_, e) =>
        {
            ShowDrawingToolFavoritesContextMenu(row, tool);
            e.Handled = true;
        };
        return row;
    }

    private UIElement CreateReferenceTrendLinePaletteRow(DrawingToolDefinition tool)
    {
        bool active = string.Equals(CandleChart.ActiveDrawingToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
        bool favorite = CandleChart.IsDrawingFavorite(tool.Id);
        Brush panel = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
        Brush text = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        Brush muted = DrawingUiBrush("MutedTextBrush", Color.FromRgb(148, 163, 184));
        Brush hover = DrawingUiBrush("ControlHoverBrush", Color.FromRgb(23, 31, 43));
        Brush selected = DrawingUiBrush("SelectionBrush", Color.FromRgb(36, 50, 71));
        Brush selectedText = DrawingUiBrush("SelectionTextBrush", Color.FromRgb(248, 250, 252));

        var row = new Border
        {
            Height = 32,
            Margin = new Thickness(0),
            Padding = new Thickness(12, 0, 7, 0),
            Background = active ? selected : panel,
            Cursor = Cursors.Hand,
            Tag = tool.Id,
            SnapsToDevicePixels = true
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });

        var iconHost = new Border
        {
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Child = DrawingToolIconFactory.CreateToolIcon(tool, 18, active ? selectedText : text)
        };
        grid.Children.Add(iconHost);

        var name = new TextBlock
        {
            Text = tool.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? selectedText : text,
            FontSize = 12.8,
            FontWeight = FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        string shortcut = DrawingShortcut(tool);
        var shortcutText = new TextBlock
        {
            Text = shortcut,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = active ? selectedText : muted,
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(shortcutText, 2);
        grid.Children.Add(shortcutText);

        var star = new Button
        {
            Width = 23, Height = 23, MinHeight = 23, Padding = new Thickness(0), Margin = new Thickness(0),
            Content = favorite ? "★" : "☆", FontSize = 16,
            Foreground = active ? selectedText : text, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            Tag = tool.Id, ToolTip = favorite ? "Remove from Favorites" : "Add to Favorites",
            Visibility = favorite ? Visibility.Visible : Visibility.Hidden
        };
        star.Click += (_, e) =>
        {
            bool added = CandleChart.ToggleDrawingFavorite(tool.Id);
            RebuildDrawingCategoryPalette();
            RefreshDrawingFavoritesWindow();
            if (added) OpenDrawingFavoritesWindow(forceShow: true);
            e.Handled = true;
        };
        Grid.SetColumn(star, 3);
        grid.Children.Add(star);
        row.Child = grid;

        void SetState(bool isHover)
        {
            bool isActive = string.Equals(CandleChart.ActiveDrawingToolId, tool.Id, StringComparison.OrdinalIgnoreCase);
            Brush stateText = isActive ? selectedText : text;
            row.Background = isActive ? selected : isHover ? hover : panel;
            name.Foreground = stateText;
            shortcutText.Foreground = isActive ? selectedText : muted;
            iconHost.Child = DrawingToolIconFactory.CreateToolIcon(tool, 18, stateText);
            star.Foreground = stateText;
            star.Visibility = isHover || CandleChart.IsDrawingFavorite(tool.Id) ? Visibility.Visible : Visibility.Hidden;
        }
        row.MouseEnter += (_, _) => SetState(true);
        row.MouseLeave += (_, _) => SetState(false);
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
            if (ActivateDrawingToolFromUi(tool.Id))
            {
                CloseDrawingCategoryPalette();
                e.Handled = true;
            }
        };
        row.PreviewMouseRightButtonUp += (_, e) =>
        {
            ShowDrawingToolFavoritesContextMenu(row, tool);
            e.Handled = true;
        };
        return row;
    }

    private void ShowDrawingToolFavoritesContextMenu(FrameworkElement target, DrawingToolDefinition tool)
    {
        bool favorite = CandleChart.IsDrawingFavorite(tool.Id);
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.MousePoint
        };
        var toggle = new MenuItem
        {
            Header = favorite ? "Remove from Favorites" : "Add to Favorites"
        };
        toggle.Click += (_, _) =>
        {
            bool nowFavorite = CandleChart.ToggleDrawingFavorite(tool.Id);
            RebuildDrawingCategoryPalette();
            RefreshDrawingFavoritesWindow();
            if (nowFavorite)
                SetDrawingFavoritesProjectionVisible(true);
        };
        menu.Items.Add(toggle);
        if (tool.Category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann)
            StyleReferenceLineContextMenu(menu);
        menu.IsOpen = true;
    }

    private bool ActivateDrawingToolFromUi(string toolId)
    {
        if (string.Equals(toolId, "image", StringComparison.OrdinalIgnoreCase))
            return OpenDrawingImagePicker();
        if (toolId is "icons" or "stickers" or "emojis")
            return OpenDrawingMediaPicker(toolId);
        RememberCategoryShortcut(toolId);
        SetDrawingToolForAllCharts(toolId);
        return true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void WorkspacePartitionGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DrawingCategoryPaletteBorder.Visibility == Visibility.Visible)
            CloseDrawingCategoryPalette();
    }

    private void DrawingCategoryPaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RebuildDrawingCategoryPalette();

    private void CloseDrawingCategoryPaletteButton_Click(object sender, RoutedEventArgs e) =>
        CloseDrawingCategoryPalette();

    private bool OpenDrawingMediaPicker(string toolId)
    {
        // The reference media picker behaves like a chart flyout, not like a modal
        // application dialog. Keep the chart interactive and let the picker close
        // itself after an item is chosen or when focus moves elsewhere.
        var picker = new DrawingMediaPickerWindow(toolId, ActiveChartContext.Chart)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        picker.MediaSelected += (selectedToolId, symbol) =>
        {
            foreach (ChartRuntimeContext context in _chartContexts.Values)
                context.Chart.SetNextDrawingMediaSymbol(selectedToolId, symbol);
            RememberCategoryShortcut(selectedToolId);
            SetDrawingToolForAllCharts(selectedToolId);
            BuildDrawingToolbar();
        };
        picker.Show();
        return true;
    }

    private bool OpenDrawingImagePicker()
    {
        var picker = new DrawingImagePickerWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedImagePath))
            return false;

        CandleChartControl target = ActiveChartContext.Chart;
        if (!target.PlaceImageDrawing(picker.SelectedImagePath, picker.SelectedOpacity, picker.SelectedAspectRatio))
        {
            StatusText.Text = "Image could not be placed because the active chart is not ready.";
            return false;
        }
        ActivateChartControl(target);
        RememberCategoryShortcut("image");
        BuildDrawingToolbar();
        return true;
    }

    private void OpenDrawingFavoritesWindow(bool forceShow = true)
    {
        if (_isClosing)
            return;

        // BuildDrawingToolbar runs while MainWindow is still inside its constructor.
        // An owned WPF window must not be created/shown until the owner has completed
        // Loaded and has a presentation source. Defer the request instead of letting
        // Window construction escape as InvalidOperationException during startup.
        if (!_ownedDrawingWindowsReady || !IsLoaded || PresentationSource.FromVisual(this) is null)
        {
            if (forceShow && !_favoritesOpenDeferred)
            {
                _favoritesOpenDeferred = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    _favoritesOpenDeferred = false;
                    if (!_isClosing && _ownedDrawingWindowsReady && IsLoaded)
                        OpenDrawingFavoritesWindow(forceShow: true);
                }));
            }
            return;
        }

        if (_drawingFavoritesWindow is null)
        {
            var window = new DrawingFavoritesWindow();
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            _drawingFavoritesWindow = window;
            window.ToolSelected += id => ActivateDrawingToolFromUi(id);
            window.RemoveRequested += id =>
            {
                if (CandleChart.IsDrawingFavorite(id))
                    CandleChart.ToggleDrawingFavorite(id);
                RefreshDrawingFavoritesWindow();
            };
            window.MoveRequested += (sourceId, targetId) =>
            {
                CandleChart.MoveDrawingFavorite(sourceId, targetId);
                RefreshDrawingFavoritesWindow();
            };
            window.CompactChanged += _ => SaveWorkspace();
            window.IsVisibleChanged += (_, _) =>
            {
                UpdateDrawingToolbarState();
                if (!_isClosing)
                    SaveWorkspace();
            };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_drawingFavoritesWindow, window))
                    _drawingFavoritesWindow = null;
            };

            window.Left = double.IsFinite(_preferences.DrawingFavoritesWindowLeft)
                ? _preferences.DrawingFavoritesWindowLeft
                : Left + 86;
            window.Top = double.IsFinite(_preferences.DrawingFavoritesWindowTop)
                ? _preferences.DrawingFavoritesWindowTop
                : Top + 92;
            window.SetCompact(_preferences.DrawingFavoritesWindowCompact);
            window.EnsureVisible(this);
        }

        RefreshDrawingFavoritesWindow();
        DrawingFavoritesWindow? favoritesWindow = _drawingFavoritesWindow;
        if (favoritesWindow is null)
            return;
        favoritesWindow.EnsureVisible(this);
        if (forceShow && !favoritesWindow.IsVisible)
            favoritesWindow.Show();
        if (forceShow)
        {
            favoritesWindow.EnsureVisible(this);
            favoritesWindow.Activate();
            QueueFavoritesTabsPosition();
        }
    }

    private void RestoreDrawingWorkspaceWindows()
    {
        RefreshInlineDrawingFavorites();
        if (CandleChart.FavoriteDrawingToolIds.Count > 0 && _preferences.DrawingFavoritesWindowVisible)
            OpenDrawingFavoritesWindow(forceShow: true);
    }

    private void RefreshDrawingFavoritesWindow()
    {
        if (_isClosing)
            return;

        RefreshInlineDrawingFavorites();

        if (CandleChart.FavoriteDrawingToolIds.Count == 0)
        {
            _drawingFavoritesWindow?.Hide();
            return;
        }

        _drawingFavoritesWindow?.SetTools(CandleChart.FavoriteDrawingToolIds);
        QueueFavoritesTabsPosition();
    }


    private void RefreshInlineDrawingFavorites()
    {
        if (InlineDrawingFavoritesPanel is null)
            return;

        InlineDrawingFavoritesPanel.Children.Clear();
        IReadOnlyList<string> ids = CandleChart.FavoriteDrawingToolIds;
        if (ids.Count == 0)
        {
            InlineDrawingFavoritesPanel.Children.Add(new TextBlock
            {
                Text = "Star any drawing tool to place it here",
                Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(113, 131, 155)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11
            });
            return;
        }

        foreach (string id in ids)
        {
            DrawingToolDefinition? tool = DrawingToolCatalog.Find(id);
            if (tool is null)
                continue;
            var button = new Button
            {
                Width = 42, Height = 36, MinHeight = 36, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 5, 0),
                Background = DrawingUiBrush("PanelAltBrush", Color.FromRgb(16, 28, 46)),
                BorderBrush = DrawingUiBrush("BorderBrush", Color.FromRgb(45, 62, 86)),
                Foreground = DrawingUiBrush("TextBrush", Colors.White), Tag = tool.Id,
                ToolTip = $"{tool.DisplayName} — drag to reorder; right-click to remove",
                Content = DrawingToolIconFactory.CreateToolIcon(tool, 20, DrawingUiBrush("TextBrush", Colors.White)),
                AllowDrop = true
            };
            button.Click += (_, _) => ActivateDrawingToolFromUi(tool.Id);
            button.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _inlineFavoriteDragId = tool.Id;
                _inlineFavoriteDragStart = e.GetPosition(InlineDrawingFavoritesPanel);
            };
            button.PreviewMouseMove += (sender, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_inlineFavoriteDragId))
                    return;
                Point p = e.GetPosition(InlineDrawingFavoritesPanel);
                if (Math.Abs(p.X - _inlineFavoriteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance)
                    return;
                DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("TickLabInlineDrawingFavorite", _inlineFavoriteDragId), DragDropEffects.Move);
            };
            button.Drop += (_, e) =>
            {
                if (!e.Data.GetDataPresent("TickLabInlineDrawingFavorite")) return;
                string source = e.Data.GetData("TickLabInlineDrawingFavorite") as string ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, tool.Id, StringComparison.OrdinalIgnoreCase))
                    CandleChart.MoveDrawingFavorite(source, tool.Id);
                RefreshDrawingFavoritesWindow();
                e.Handled = true;
            };
            button.PreviewMouseRightButtonUp += (_, e) =>
            {
                if (CandleChart.IsDrawingFavorite(tool.Id)) CandleChart.ToggleDrawingFavorite(tool.Id);
                RefreshDrawingFavoritesWindow();
                e.Handled = true;
            };
            InlineDrawingFavoritesPanel.Children.Add(button);
        }
    }

    private void SetDrawingFavoritesProjectionVisible(bool visible)
    {
        if (visible)
        {
            if (CandleChart.FavoriteDrawingToolIds.Count == 0)
            {
                StatusText.Text = "Right-click any drawing tool and choose Add to Favorites first.";
                return;
            }
            OpenDrawingFavoritesWindow(forceShow: true);
            _drawingFavoritesWindow?.EnsureVisible(this);
            StatusText.Text = "Drawing Favorites tabs are on.";
        }
        else
        {
            _drawingFavoritesWindow?.Hide();
            StatusText.Text = "Drawing Favorites tabs are off.";
        }
        UpdateDrawingToolbarState();
        SaveWorkspace();
    }

    private bool FavoritesTabsVisible =>
        _drawingFavoritesWindow?.IsVisible == true || _timeframeFavoritesWindow?.IsVisible == true;

    private void SetFavoritesTabsVisible(bool visible)
    {
        if (!visible)
        {
            _drawingFavoritesWindow?.Hide();
            _timeframeFavoritesWindow?.Hide();
            StatusText.Text = "Favorites tabs are off.";
            UpdateDrawingToolbarState();
            SaveWorkspace();
            return;
        }

        bool hasDrawingFavorites = CandleChart.FavoriteDrawingToolIds.Count > 0;
        bool hasTimeframeFavorites = GetFavoriteTimeframes().Count > 0;
        if (!hasDrawingFavorites && !hasTimeframeFavorites)
        {
            StatusText.Text = "Add a favorite drawing tool or timeframe first.";
            return;
        }

        if (hasDrawingFavorites)
            OpenDrawingFavoritesWindow(forceShow: true);
        else
            _drawingFavoritesWindow?.Hide();

        if (hasTimeframeFavorites)
            OpenTimeframeFavoritesWindow(forceShow: true);
        else
            _timeframeFavoritesWindow?.Hide();

        QueueFavoritesTabsPosition();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(PositionFavoritesTabsAtActiveChartBottom));
        StatusText.Text = "Favorites tabs are on — drawing tools and timeframes returned to their bottom-center home.";
        UpdateDrawingToolbarState();
        SaveWorkspace();
    }

    private void QueueFavoritesTabsPosition()
    {
        if (!_ownedDrawingWindowsReady || _isClosing || !IsLoaded)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionFavoritesTabsAtActiveChartBottom));
    }

    private static double WindowDisplayWidth(Window window) =>
        double.IsFinite(window.Width) && window.Width > 1
            ? Math.Max(window.MinWidth, window.Width)
            : Math.Max(window.MinWidth, window.ActualWidth);

    private static double WindowDisplayHeight(Window window) =>
        double.IsFinite(window.Height) && window.Height > 1
            ? Math.Max(window.MinHeight, window.Height)
            : Math.Max(window.MinHeight, window.ActualHeight);

    private static Point ControlPointToScreenDip(Visual visual, Point point)
    {
        Point device = visual.PointToScreen(point);
        PresentationSource? source = PresentationSource.FromVisual(visual);
        return source?.CompositionTarget is null
            ? device
            : source.CompositionTarget.TransformFromDevice.Transform(device);
    }

    private void PositionFavoritesTabsAtActiveChartBottom()
    {
        if (_isClosing || !IsLoaded)
            return;

        CandleChartControl chart = ActiveChartContext.Chart;
        if (!chart.IsVisible || chart.ActualWidth <= 1 || chart.ActualHeight <= 1)
            return;

        Rect plot = chart.GetPlotVisualBounds();
        if (plot.Width <= 1 || plot.Height <= 1)
            return;

        Point plotLeftBottom;
        Point plotRightBottom;
        Point plotLeftTop;
        try
        {
            plotLeftBottom = ControlPointToScreenDip(chart, plot.BottomLeft);
            plotRightBottom = ControlPointToScreenDip(chart, plot.BottomRight);
            plotLeftTop = ControlPointToScreenDip(chart, plot.TopLeft);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        double left = Math.Min(plotLeftBottom.X, plotRightBottom.X);
        double right = Math.Max(plotLeftBottom.X, plotRightBottom.X);
        double plotBottom = Math.Min(plotLeftBottom.Y, plotRightBottom.Y);
        double plotTop = plotLeftTop.Y;
        double availableWidth = Math.Max(1, right - left);
        const double edge = 8.0;
        const double gap = 6.0;
        double hostWidth = Math.Max(70, availableWidth - edge * 2);

        DrawingFavoritesWindow? drawing = _drawingFavoritesWindow?.IsVisible == true ? _drawingFavoritesWindow : null;
        TimeframeFavoritesWindow? timeframe = _timeframeFavoritesWindow?.IsVisible == true ? _timeframeFavoritesWindow : null;
        if (drawing is null && timeframe is null)
            return;

        drawing?.SetMaximumHostWidth(hostWidth);
        timeframe?.SetMaximumHostWidth(hostWidth);

        double drawingWidth = drawing is null ? 0 : Math.Min(WindowDisplayWidth(drawing), hostWidth);
        double timeframeWidth = timeframe is null ? 0 : Math.Min(WindowDisplayWidth(timeframe), hostWidth);
        double drawingHeight = drawing is null ? 0 : WindowDisplayHeight(drawing);
        double timeframeHeight = timeframe is null ? 0 : WindowDisplayHeight(timeframe);

        // Permanent home: bottom-center of the active plot, one bar above the other.
        // Timeframes occupy the lower row and drawing favorites the row above it.
        double cursorBottom = plotBottom - edge;
        if (timeframe is not null)
        {
            double y = Math.Max(plotTop + edge, cursorBottom - timeframeHeight);
            timeframe.Left = left + Math.Max(edge, (availableWidth - timeframeWidth) / 2.0);
            timeframe.Top = y;
            timeframe.EnsureVisible(this);
            cursorBottom = y - gap;
        }

        if (drawing is not null)
        {
            double y = Math.Max(plotTop + edge, cursorBottom - drawingHeight);
            drawing.Left = left + Math.Max(edge, (availableWidth - drawingWidth) / 2.0);
            drawing.Top = y;
            drawing.EnsureVisible(this);
        }
    }

    private void UpdateDrawingToolbarState()
    {
Brush railBrush = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        DrawingMagnetButton.Content = DrawingToolIconFactory.CreateActionIcon("magnet", 33, railBrush);
        DrawingMagnetButton.ToolTip =
            $"Magnet: {CandleChart.DrawingMagnetMode}. Hold Ctrl to temporarily reverse it; when magnet is off, Ctrl activates Strong magnet.";
        bool magnetActive = CandleChart.DrawingMagnetMode != DrawingMagnetMode.Off;
        DrawingMagnetButton.Tag = magnetActive ? "Active" : null;
        DrawingMagnetButton.Background = magnetActive
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;

        bool measureActive = ActiveChartContext.Chart.MeasureModeActive;
        DrawingMeasureButton.Tag = measureActive ? "Active" : null;
        DrawingMeasureButton.Background = measureActive
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;

        CandleChartControl wheelModeChart = ActiveChartContext.Chart;
        bool wheelZoomMode = wheelModeChart.ScrollWheelMode == ChartScrollWheelMode.Zoom;
        Brush wheelBrush = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        DrawingZoomButton.Content = DrawingToolIconFactory.CreateActionIcon(wheelZoomMode ? "zoom" : "scroll", 30, wheelBrush);
        DrawingZoomButton.ToolTip = wheelZoomMode
            ? "Mouse wheel: Zoom mode — click to switch to horizontal Scroll mode"
            : "Mouse wheel: Scroll mode — click to switch to Zoom mode";
        DrawingZoomButton.Tag = wheelZoomMode ? "Zoom" : "Scroll";
        DrawingZoomButton.Background = wheelZoomMode
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;

        bool syncActive = CandleChart.DefaultDrawingSyncMode != DrawingSyncMode.CurrentChart;
        DrawingSyncButton.Tag = syncActive ? "Active" : null;
        DrawingSyncButton.ToolTip = $"Drawing sync: {CandleChart.DefaultDrawingSyncMode}. Click to cycle; right-click for all modes.";
        DrawingSyncButton.Background = syncActive
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;

        StayDrawingModeButton.Tag = CandleChart.StayInDrawingMode ? "Active" : null;
        StayDrawingModeButton.Background = CandleChart.StayInDrawingMode
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;
        bool lockAllDrawings = ActiveChartContext.Chart.LockAllDrawings;
        DrawingLockButton.Tag = lockAllDrawings ? "Active" : null;
        DrawingLockButton.Background = lockAllDrawings
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;
        bool hideAllDrawings = ActiveChartContext.Chart.HideAllDrawings;
        DrawingHideButton.Tag = hideAllDrawings ? "Active" : null;
        DrawingHideButton.Background = hideAllDrawings
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;
        string activeDrawingId = ActiveChartContext.Chart.ActiveDrawingToolId;
        bool brushActive = activeDrawingId is "pen" or "brush" or "highlighter";
        DrawingBrushButton.Tag = brushActive ? "Active" : null;
        DrawingBrushButton.Background = brushActive
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;

        bool favoritesVisible = FavoritesTabsVisible;
        DrawingFavoritesButton.Tag = favoritesVisible ? "Active" : null;
        DrawingFavoritesButton.ToolTip = favoritesVisible
            ? "Favorites Tabs: On — click to hide drawing + timeframe favorites"
            : "Favorites Tabs: Off — click to show drawing + timeframe favorites above the time scale";
        DrawingFavoritesButton.Background = favoritesVisible
            ? DrawingUiBrush("PanelAltBrush", Color.FromRgb(20, 20, 20))
            : Brushes.Transparent;
    }

    private void DrawingFavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshInlineDrawingFavorites();
        SetFavoritesTabsVisible(!FavoritesTabsVisible);
    }

    private void DrawingMeasureButton_Click(object sender, RoutedEventArgs e)
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Chart.SetMeasureMode(!context.Chart.MeasureModeActive);
        UpdateDrawingToolbarState();
    }

    private void DrawingMeasureButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = DrawingMeasureButton,
            Placement = PlacementMode.MousePoint
        };
        var edit = new MenuItem { Header = "Edit" };
        edit.Click += (_, _) => OpenMeasureStyleEditor(ActiveChartContext.Chart);
        menu.Items.Add(edit);
        StyleReferenceLineContextMenu(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OpenMeasureStyleEditor(CandleChartControl chart)
    {
        var editor = new Window
        {
            Owner = this,
            Title = "Measure — Edit",
            Width = 330,
            Height = 190,
            MinWidth = 330,
            MinHeight = 190,
            MaxWidth = 330,
            MaxHeight = 190,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = DrawingUiBrush("WindowBrush", Color.FromRgb(9, 14, 22))
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var colorLabel = new TextBlock
        {
            Text = "Colour",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = DrawingUiBrush("TextBrush", Colors.White)
        };
        var colorButton = new Button
        {
            Width = 74,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = QuickColorBrush(chart.MeasureLineColor, Color.FromRgb(56, 189, 248)),
            ToolTip = "Measure cross and rectangle colour"
        };
        Grid.SetColumn(colorButton, 1);
        colorRow.Children.Add(colorLabel);
        colorRow.Children.Add(colorButton);
        Grid.SetRow(colorRow, 0);
        root.Children.Add(colorRow);

        var opacityPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var opacityHeader = new DockPanel();
        var opacityValue = new TextBlock
        {
            Text = $"{chart.MeasureOpacity * 100:0}%",
            Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(opacityValue, Dock.Right);
        opacityHeader.Children.Add(opacityValue);
        opacityHeader.Children.Add(new TextBlock
        {
            Text = "Transparency / opacity",
            Foreground = DrawingUiBrush("TextBrush", Colors.White)
        });
        var opacitySlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = chart.MeasureOpacity,
            TickFrequency = 0.01,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 7, 0, 0)
        };
        opacitySlider.ValueChanged += (_, args) =>
        {
            opacityValue.Text = $"{args.NewValue * 100:0}%";
            chart.SetMeasureStyle(chart.MeasureLineColor, args.NewValue);
        };
        opacityPanel.Children.Add(opacityHeader);
        opacityPanel.Children.Add(opacitySlider);
        Grid.SetRow(opacityPanel, 1);
        root.Children.Add(opacityPanel);

        colorButton.Click += (_, _) =>
        {
            string original = chart.MeasureLineColor;
            var picker = new DrawingColorPickerWindow(original) { Owner = editor };
            picker.ColorPreviewChanged += color =>
            {
                chart.SetMeasureStyle(color, chart.MeasureOpacity);
                colorButton.Background = QuickColorBrush(color, Color.FromRgb(56, 189, 248));
            };
            bool accepted = picker.ShowDialog() == true;
            string final = accepted ? picker.SelectedColor : original;
            chart.SetMeasureStyle(final, chart.MeasureOpacity);
            colorButton.Background = QuickColorBrush(final, Color.FromRgb(56, 189, 248));
        };

        var close = new Button
        {
            Content = "Close",
            Width = 80,
            Height = 30,
            MinHeight = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(10, 4, 10, 4)
        };
        close.Click += (_, _) => editor.Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);

        editor.Content = root;
        editor.Show();
    }

    private void DrawingZoomButton_Click(object sender, RoutedEventArgs e)
    {
        CandleChartControl chart = ActiveChartContext.Chart;
        chart.ToggleScrollWheelMode();
        ActivateChartControl(chart);
        UpdateDrawingToolbarState();
    }

    private void DrawingSyncButton_Click(object sender, RoutedEventArgs e)
    {
        DrawingSyncMode next = CandleChart.DefaultDrawingSyncMode switch
        {
            DrawingSyncMode.CurrentChart => DrawingSyncMode.SameSymbol,
            DrawingSyncMode.SameSymbol => DrawingSyncMode.SameSymbolAndTimeframe,
            DrawingSyncMode.SameSymbolAndTimeframe => DrawingSyncMode.CurrentLayout,
            DrawingSyncMode.CurrentLayout => DrawingSyncMode.Global,
            _ => DrawingSyncMode.CurrentChart
        };
        CandleChart.SetDefaultDrawingSyncMode(next);
        UpdateDrawingToolbarState();
        SaveWorkspace();
    }

    private void DrawingSyncButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = DrawingSyncButton };
        foreach (DrawingSyncMode mode in Enum.GetValues<DrawingSyncMode>())
        {
            DrawingSyncMode captured = mode;
            var item = new MenuItem
            {
                Header = mode switch
                {
                    DrawingSyncMode.CurrentChart => "No sync — current chart only",
                    DrawingSyncMode.SameSymbol => "Sync on charts with the same symbol",
                    DrawingSyncMode.SameSymbolAndTimeframe => "Sync on same symbol and timeframe",
                    DrawingSyncMode.CurrentLayout => "Sync in current layout",
                    DrawingSyncMode.Global => "Global sync in TickLab workspace",
                    _ => mode.ToString()
                },
                IsCheckable = true,
                IsChecked = CandleChart.DefaultDrawingSyncMode == mode
            };
            item.Click += (_, _) =>
            {
                CandleChart.SetDefaultDrawingSyncMode(captured);
                UpdateDrawingToolbarState();
                SaveWorkspace();
            };
            menu.Items.Add(item);
        }
        DrawingSyncButton.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DrawingMagnetButton_Click(object sender, RoutedEventArgs e)
    {
        DrawingMagnetMode next = CandleChart.DrawingMagnetMode switch
        {
            DrawingMagnetMode.Off => DrawingMagnetMode.Weak,
            DrawingMagnetMode.Weak => DrawingMagnetMode.Strong,
            _ => DrawingMagnetMode.Off
        };
        CandleChart.SetDrawingMagnetMode(next);
        UpdateDrawingToolbarState();
    }

    private void DrawingMagnetButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = DrawingMagnetButton };
        foreach (DrawingMagnetMode mode in Enum.GetValues<DrawingMagnetMode>())
        {
            DrawingMagnetMode captured = mode;
            var item = new MenuItem
            {
                Header = mode switch
                {
                    DrawingMagnetMode.Off => "Magnet off",
                    DrawingMagnetMode.Weak => "Weak magnet — snap when close",
                    DrawingMagnetMode.Strong => "Strong magnet — always snap",
                    _ => mode.ToString()
                },
                IsCheckable = true,
                IsChecked = CandleChart.DrawingMagnetMode == mode
            };
            item.Click += (_, _) =>
            {
                CandleChart.SetDrawingMagnetMode(captured);
                UpdateDrawingToolbarState();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var snapIndicators = new MenuItem
        {
            Header = "Snap to indicator plots",
            IsCheckable = true,
            IsChecked = CandleChart.SnapDrawingsToIndicators
        };
        snapIndicators.Click += (_, _) => CandleChart.SetSnapDrawingsToIndicators(snapIndicators.IsChecked);
        menu.Items.Add(snapIndicators);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Ctrl temporarily reverses the current magnet state",
            IsEnabled = false
        });
        DrawingMagnetButton.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void StayDrawingModeButton_Click(object sender, RoutedEventArgs e)
    {
        CandleChart.SetStayInDrawingMode(!CandleChart.StayInDrawingMode);
        UpdateDrawingToolbarState();
    }

    private void DrawingLockButton_Click(object sender, RoutedEventArgs e)
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Chart.SetLockAllDrawings(!context.Chart.LockAllDrawings);
        UpdateDrawingToolbarState();
    }

    private void DrawingHideButton_Click(object sender, RoutedEventArgs e)
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Chart.SetHideAllDrawings(!context.Chart.HideAllDrawings);
        UpdateDrawingToolbarState();
    }

    private void DrawingObjectTreeButton_Click(object sender, RoutedEventArgs e) => OpenDrawingObjectTree();
    private void DrawingUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            context.Chart.UndoDrawingChange();
    }

    private void DrawingRedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            context.Chart.RedoDrawingChange();
    }

    private void DrawingRemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Chart.RemoveSelectedDrawings();
    }

    private void DrawingToolbarScrollUpButton_Click(object sender, RoutedEventArgs e) =>
        DrawingToolbarScrollViewer.ScrollToVerticalOffset(
            Math.Max(0.0, DrawingToolbarScrollViewer.VerticalOffset - 160.0));

    private void DrawingToolbarScrollDownButton_Click(object sender, RoutedEventArgs e) =>
        DrawingToolbarScrollViewer.ScrollToVerticalOffset(
            DrawingToolbarScrollViewer.VerticalOffset + 160.0);

    private void DrawingToolbarCollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _drawingToolbarCollapsed = !_drawingToolbarCollapsed;
        if (_drawingToolbarCollapsed)
            CloseDrawingCategoryPalette();
        ApplyDrawingToolbarCollapsedState();
        SaveWorkspace();
    }

    private void ApplyDrawingToolbarCollapsedState()
    {
        DrawingToolbarColumn.Width = new GridLength(_drawingToolbarCollapsed ? 14 : 53);
        Visibility railVisibility = _drawingToolbarCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;

        // The audited rail has no extra scroll arrows; the icon strip itself remains scrollable
        // by wheel/touch when the window is unusually short.
        DrawingToolbarScrollUpButton.Visibility = Visibility.Collapsed;
        DrawingToolbarScrollViewer.Visibility = railVisibility;
        DrawingToolbarScrollDownButton.Visibility = Visibility.Collapsed;
        Brush railBrush = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        DrawingToolbarCollapseButton.Content = DrawingToolIconFactory.CreateActionIcon(
            _drawingToolbarCollapsed ? "chevron-right" : "collapse", _drawingToolbarCollapsed ? 16.5 : 24, railBrush);
        DrawingToolbarCollapseButton.ToolTip = _drawingToolbarCollapsed
            ? "Expand drawing toolbar"
            : "Collapse drawing toolbar";
        DrawingToolbarCollapseButton.Width = _drawingToolbarCollapsed ? 16 : 36;
        DrawingToolbarCollapseButton.Margin = _drawingToolbarCollapsed
            ? new Thickness(0, 3, 0, 5)
            : new Thickness(8.5, 3, 0, 5);
    }

    private void RightWorkspaceToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rightWorkspaceHandleMoved)
            return;
        _rightWorkspaceCollapsed = !_rightWorkspaceCollapsed;
        ApplyRightWorkspaceCollapsedState();
        SaveWorkspace();
    }

    private void RightWorkspaceHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, RightWorkspaceToggleButton) || e.ChangedButton != MouseButton.Left)
            return;

        CancelOtherRightHandleInteractions(RightWorkspaceToggleButton);
        _rightWorkspaceHandleDragging = true;
        _rightWorkspaceHandleMoved = false;
        _rightWorkspaceHandleStartX = e.GetPosition(this).X;
        _rightWorkspaceHandleStartWidth = _rightWorkspaceCollapsed
            ? 0.0
            : Math.Max(0.0, RightWorkspaceColumn.ActualWidth);
    }

    private void RightWorkspaceHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_rightWorkspaceHandleDragging || sender is not UIElement handle)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishRightWorkspaceHandleInteraction(handle, allowClickToggle: false);
            return;
        }

        double delta = e.GetPosition(this).X - _rightWorkspaceHandleStartX;
        if (!_rightWorkspaceHandleMoved && Math.Abs(delta) >= SystemParameters.MinimumHorizontalDragDistance)
        {
            _rightWorkspaceHandleMoved = true;
            Mouse.Capture(handle, CaptureMode.Element);
        }

        if (!_rightWorkspaceHandleMoved)
            return;

        double requestedWidth = Math.Clamp(
            _rightWorkspaceHandleStartWidth - delta,
            0.0,
            620.0);
        SetRightWorkspaceDragWidth(requestedWidth);
        e.Handled = true;
    }

    private void RightWorkspaceHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rightWorkspaceHandleDragging || sender is not UIElement handle || e.ChangedButton != MouseButton.Left)
            return;

        if (_rightWorkspaceHandleMoved)
        {
            FinishRightWorkspaceHandleInteraction(handle, allowClickToggle: false);
            e.Handled = true;
        }
        else
        {
            _rightWorkspaceHandleDragging = false;
        }
    }

    private void RightWorkspaceHandle_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_rightWorkspaceHandleDragging)
            return;

        FinishRightWorkspaceHandleInteraction(sender as UIElement, allowClickToggle: false);
    }

    private void RightWorkspaceHandle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
            return;

        _rightWorkspaceCollapsed = !_rightWorkspaceCollapsed;
        ApplyRightWorkspaceCollapsedState();
        SaveWorkspace();
        e.Handled = true;
    }

    private void FinishRightWorkspaceHandleInteraction(UIElement? handle, bool allowClickToggle)
    {
        bool moved = _rightWorkspaceHandleMoved;
        _rightWorkspaceHandleDragging = false;
        _rightWorkspaceHandleMoved = false;
        if (handle?.IsMouseCaptured == true)
            Mouse.Capture(null);

        if (!moved)
        {
            if (allowClickToggle)
            {
                _rightWorkspaceCollapsed = !_rightWorkspaceCollapsed;
                ApplyRightWorkspaceCollapsedState();
                SaveWorkspace();
            }
            return;
        }

        if (RightWorkspaceColumn.ActualWidth < 72.0)
        {
            _rightWorkspaceCollapsed = true;
        }
        else
        {
            _rightWorkspaceCollapsed = false;
            _rightWorkspaceExpandedWidth = Math.Clamp(
                RightWorkspaceColumn.ActualWidth,
                140.0,
                620.0);
        }
        ApplyRightWorkspaceCollapsedState();
        SaveWorkspace();
    }

    private void SetRightWorkspaceDragWidth(double width)
    {
        if (width < 40.0)
        {
            _rightWorkspaceCollapsed = true;
            RightWorkspaceBorder.Visibility = Visibility.Collapsed;
            RightWorkspaceColumn.Width = new GridLength(0.0);
            RightWorkspaceSplitter.IsEnabled = false;
            RightWorkspaceToggleButton.ToolTip = "Click to expand; drag left to open or resize";
            return;
        }

        _rightWorkspaceCollapsed = false;
        RightWorkspaceBorder.Visibility = Visibility.Visible;
        RightWorkspaceColumn.Width = new GridLength(Math.Clamp(width, 40.0, 620.0));
        RightWorkspaceSplitter.IsEnabled = true;
        RightWorkspaceToggleButton.ToolTip = "Click to collapse; drag left or right to resize";
    }

    private void ApplyRightWorkspaceCollapsedState()
    {
        if (_rightWorkspaceCollapsed)
        {
            if (RightWorkspaceColumn.ActualWidth >= 140.0)
            {
                _rightWorkspaceExpandedWidth = Math.Clamp(
                    RightWorkspaceColumn.ActualWidth,
                    140.0,
                    620.0);
            }

            RightWorkspaceBorder.Visibility = Visibility.Collapsed;
            RightWorkspaceColumn.Width = new GridLength(0.0);
            RightWorkspaceSplitter.IsEnabled = false;
            RightWorkspaceToggleButton.ToolTip = "Click to expand; drag left to open or resize";
            return;
        }

        RightWorkspaceColumn.Width = new GridLength(
            Math.Clamp(_rightWorkspaceExpandedWidth, 140.0, 620.0));
        RightWorkspaceBorder.Visibility = Visibility.Visible;
        RightWorkspaceSplitter.IsEnabled = true;
        RightWorkspaceToggleButton.ToolTip = "Click to collapse; drag left or right to resize";
    }

    private void ShowDrawingQuickEditor(ChartDrawing? drawing)
    {
        if (_quickEditOriginal is not null &&
            (drawing is null || !string.Equals(_quickEditOriginal.Id, drawing.Id, StringComparison.Ordinal)))
        {
            CommitQuickDrawingEdit();
        }

        string nextQuickId = drawing?.Id ?? string.Empty;
        if (!string.Equals(_quickEditBarDrawingId, nextQuickId, StringComparison.Ordinal))
        {
            _quickEditBarDrawingId = nextQuickId;
            _quickEditBarManualPosition = false;
            _quickEditBarDragging = false;
        }
        _quickEditDrawing = drawing;
        _quickEditPreview = drawing;
        RefreshInlineDrawingInspector(drawing);
        if (drawing is null)
        {
            DrawingQuickEditBar.Visibility = Visibility.Collapsed;
            return;
        }

        DrawingToolDefinition? tool = DrawingToolCatalog.Find(drawing.ToolId);
        bool referenceLineTool = tool?.Category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann;
        bool predictionMeasurementTool = tool?.Category == DrawingToolCategory.PredictionMeasurement;
        bool longShortPositionTool = drawing.ToolId is "long-position" or "short-position";
        bool annotationParityTool = drawing.ToolId is "text" or "note" or "price-note" or "pin" or "table" or "callout" or "comment" or "price-label" or "signpost" or "flag-mark";
        bool annotationEditableText = drawing.ToolId is "text" or "note" or "pin" or "callout" or "comment" or "signpost";
        bool annotationSupportsAnchor = drawing.ToolId is "text" or "pin" or "table";
        bool annotationFontSizeQuick = drawing.ToolId is "text" or "pin" or "callout" or "comment" or "price-label" or "signpost";
        _suppressQuickEdit = true;
        try
        {
            QuickLineWidthBox.ItemsSource = new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0 };
            QuickLineWidthBox.SelectedItem = ((double[])QuickLineWidthBox.ItemsSource)
                .OrderBy(value => Math.Abs(value - drawing.Style.LineWidth))
                .First();
            QuickLineStyleBox.ItemsSource = Enum.GetValues<DrawingLineStyle>();
            QuickLineStyleBox.SelectedItem = drawing.Style.LineStyle;
            QuickFontSizeBox.ItemsSource = new[] { 8.0, 10.0, 11.0, 12.0, 13.0, 14.0, 16.0, 18.0, 20.0, 24.0, 28.0, 32.0, 40.0, 48.0 };
            QuickFontSizeBox.SelectedItem = ((double[])QuickFontSizeBox.ItemsSource)
                .OrderBy(value => Math.Abs(value - drawing.Style.FontSize))
                .First();
            QuickOpacitySlider.Value = Math.Clamp(drawing.Style.Opacity, 0, 1);
            QuickFillOpacitySlider.Value = Math.Clamp(drawing.Style.FillOpacity, 0, 1);
            QuickFillToggle.IsChecked = drawing.Style.FillOpacity > 0.0001;
            bool supportsFill = tool?.SupportsFill == true;
            bool supportsText = tool?.SupportsText == true;
            bool isMedia = tool?.Geometry is DrawingGeometryKind.Image or DrawingGeometryKind.Icon;
            // Keep selection controls deliberately small.  The first audited folder uses
            // the same compact white contextual strip visible in the reference video.
            QuickGripText.Visibility = Visibility.Visible;
            QuickCloneButton.Visibility = Visibility.Collapsed;
            QuickTableColumnButton.Visibility = drawing.ToolId == "table" ? Visibility.Visible : Visibility.Collapsed;
            QuickTableRowButton.Visibility = drawing.ToolId == "table" ? Visibility.Visible : Visibility.Collapsed;
            QuickTextButton.Visibility = referenceLineTool && supportsText && drawing.ToolId != "trend-angle" ? Visibility.Visible : Visibility.Collapsed;
            QuickTextColorButton.Visibility = annotationParityTool && drawing.ToolId != "flag-mark" ? Visibility.Visible : Visibility.Collapsed;
            QuickFontSizeBox.Visibility = annotationFontSizeQuick ? Visibility.Visible : Visibility.Collapsed;
            QuickAnchorButton.Visibility = annotationSupportsAnchor ? Visibility.Visible : Visibility.Collapsed;
            QuickVisualOrderButton.Visibility = Visibility.Collapsed;
            QuickAlertButton.Visibility = referenceLineTool ? Visibility.Visible : Visibility.Collapsed;
            QuickMoreButton.Visibility = referenceLineTool ? Visibility.Visible : Visibility.Collapsed;

            QuickLineColorButton.Visibility = isMedia ? Visibility.Collapsed : Visibility.Visible;
            QuickLineWidthBox.Visibility = isMedia || annotationParityTool ? Visibility.Collapsed : Visibility.Visible;
            QuickLineStyleBox.Visibility = isMedia || annotationParityTool ? Visibility.Collapsed : Visibility.Visible;
            QuickOpacitySlider.Visibility = referenceLineTool || predictionMeasurementTool ? Visibility.Visible : Visibility.Collapsed;
            bool geometryFillControls = drawing.ToolId == "arc";
            bool annotationFillControls = annotationParityTool && supportsFill && drawing.ToolId != "flag-mark";
            QuickFillToggle.Visibility = supportsFill && !longShortPositionTool && (predictionMeasurementTool || geometryFillControls || annotationFillControls) ? Visibility.Visible : Visibility.Collapsed;
            QuickFillOpacitySlider.Visibility = supportsFill && !longShortPositionTool && (predictionMeasurementTool || geometryFillControls || annotationFillControls) ? Visibility.Visible : Visibility.Collapsed;
            QuickFillColorButton.Visibility = !referenceLineTool && !isMedia && supportsFill && !longShortPositionTool ? Visibility.Visible : Visibility.Collapsed;
            QuickBackgroundColorButton.Visibility = !annotationParityTool && !referenceLineTool && supportsText && !isMedia ? Visibility.Visible : Visibility.Collapsed;
            QuickTextBox.Visibility = annotationEditableText ? Visibility.Visible : (!annotationParityTool && !referenceLineTool && supportsText && !isMedia ? Visibility.Visible : Visibility.Collapsed);
            QuickTextBox.Text = annotationEditableText ? drawing.Text : (drawing.ToolId == "table" ? string.Empty : drawing.Text);
            string anchorMode = drawing.TextOptions.TryGetValue("Anchor", out string? storedAnchor) && !string.IsNullOrWhiteSpace(storedAnchor) ? storedAnchor : "Auto";
            QuickAnchorButton.ToolTip = $"Anchor position: {anchorMode} (click to cycle)";

            Brush quickIconBrush = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
            QuickLockButton.Content = DrawingToolIconFactory.CreateActionIcon(drawing.IsLocked ? "unlock" : "lock", 16, quickIconBrush);
            QuickLockButton.ToolTip = drawing.IsLocked ? "Unlock drawing" : "Lock drawing";
            QuickTemplateButton.Content = DrawingToolIconFactory.CreateActionIcon("template", 16, quickIconBrush);
            QuickSettingsButton.Content = DrawingToolIconFactory.CreateActionIcon("settings", 16, quickIconBrush);
            QuickDeleteButton.Content = DrawingToolIconFactory.CreateActionIcon("delete", 16, quickIconBrush);
            QuickAlertButton.Content = DrawingToolIconFactory.CreateActionIcon("alert", 15, quickIconBrush);
            QuickMoreButton.Content = DrawingToolIconFactory.CreateActionIcon("more", 15, quickIconBrush);
            QuickCloseButton.Content = DrawingToolIconFactory.CreateActionIcon("close", 14, quickIconBrush);

            if (referenceLineTool)
            {
                // Keep the toolbar surface explicitly theme-bound. ClearValue()
                // removes the DynamicResource declared in XAML and leaves the bar
                // transparent, which is why the quick-edit background disappeared.
                DrawingQuickEditBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                DrawingQuickEditBar.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
                DrawingQuickEditBar.CornerRadius = new CornerRadius(5);
                // Do not stamp local theme colours onto quick-edit controls.
                // DynamicResource styles own normal/hover/selected colours so
                // switching themes can never leave white text on white UI.
                QuickLineWidthBox.ClearValue(Control.BackgroundProperty);
                QuickLineWidthBox.ClearValue(Control.ForegroundProperty);
                QuickLineWidthBox.ClearValue(Control.BorderBrushProperty);
                QuickLineStyleBox.ClearValue(Control.BackgroundProperty);
                QuickLineStyleBox.ClearValue(Control.ForegroundProperty);
                QuickLineStyleBox.ClearValue(Control.BorderBrushProperty);
                ApplyQuickEditComboSystemColours(QuickLineWidthBox);
                ApplyQuickEditComboSystemColours(QuickLineStyleBox);
                foreach (Button button in new[] { QuickTemplateButton, QuickTextButton, QuickSettingsButton, QuickAlertButton, QuickLockButton, QuickDeleteButton, QuickMoreButton })
                {
                    button.ClearValue(Control.BackgroundProperty);
                    button.ClearValue(Control.BorderBrushProperty);
                    button.ClearValue(Control.ForegroundProperty);
                }
                QuickTemplateButton.Visibility = Visibility.Visible;
                QuickSettingsButton.Visibility = Visibility.Visible;
                QuickCloseButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Keep the toolbar surface explicitly theme-bound. ClearValue()
                // removes the DynamicResource declared in XAML and leaves the bar
                // transparent, which is why the quick-edit background disappeared.
                DrawingQuickEditBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                DrawingQuickEditBar.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
                DrawingQuickEditBar.CornerRadius = new CornerRadius(5);
                QuickSettingsButton.Visibility = Visibility.Visible;
                QuickCloseButton.Visibility = Visibility.Visible;
            }

            if (referenceLineTool)
            {
                QuickLineColorButton.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
                QuickLineColorButton.Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
                QuickLineColorButton.BorderBrush = DrawingUiBrush("BorderStrongBrush", Color.FromRgb(51, 65, 85));
                QuickLineColorButton.BorderThickness = new Thickness(1);
                QuickLineColorButton.Content = CreateQuickLineColourSwatch(drawing.Style.LineColor);
            }
            else
            {
                QuickLineColorButton.Background = QuickColorBrush(drawing.Style.LineColor, Colors.DodgerBlue);
                QuickLineColorButton.BorderThickness = new Thickness(1);
            }
            QuickFillColorButton.Background = QuickColorBrush(drawing.Style.FillColor, Colors.DodgerBlue);
            QuickBackgroundColorButton.Background = QuickColorBrush(drawing.Style.BackgroundColor, Color.FromRgb(15, 23, 42));
            QuickTextColorButton.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
            QuickTextColorButton.Foreground = QuickColorBrush(drawing.Style.TextColor, Colors.White);
            QuickTextColorButton.BorderBrush = QuickColorBrush(drawing.Style.TextColor, Colors.White);
            QuickAnchorButton.Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
            DrawingQuickEditBar.Visibility = Visibility.Visible;
            if (!_quickEditBarManualPosition)
            {
                PositionDrawingQuickEditor();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionDrawingQuickEditor));
            }
        }
        finally
        {
            _suppressQuickEdit = false;
        }
    }

    private void QuickGripText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DrawingQuickEditBar.Visibility != Visibility.Visible)
            return;
        _quickEditBarDragging = true;
        _quickEditBarManualPosition = true;
        _quickEditBarDragStart = e.GetPosition(MainChartPaneRoot);
        _quickEditBarDragStartMargin = DrawingQuickEditBar.Margin;
        QuickGripText.CaptureMouse();
        e.Handled = true;
    }

    private void QuickGripText_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_quickEditBarDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(MainChartPaneRoot);
        Vector delta = current - _quickEditBarDragStart;
        DrawingQuickEditBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = DrawingQuickEditBar.ActualWidth > 1 ? DrawingQuickEditBar.ActualWidth : DrawingQuickEditBar.DesiredSize.Width;
        double height = DrawingQuickEditBar.ActualHeight > 1 ? DrawingQuickEditBar.ActualHeight : DrawingQuickEditBar.DesiredSize.Height;
        double hostWidth = Math.Max(1, MainChartPaneRoot.ActualWidth);
        double hostHeight = Math.Max(1, MainChartPaneRoot.ActualHeight);
        const double edge = 4.0;
        double x = Math.Clamp(_quickEditBarDragStartMargin.Left + delta.X, edge, Math.Max(edge, hostWidth - width - edge));
        double y = Math.Clamp(_quickEditBarDragStartMargin.Top + delta.Y, edge, Math.Max(edge, hostHeight - height - edge));
        DrawingQuickEditBar.Margin = new Thickness(x, y, 0, 0);
        e.Handled = true;
    }

    private void QuickGripText_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_quickEditBarDragging)
            return;
        _quickEditBarDragging = false;
        QuickGripText.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ApplyQuickEditComboSystemColours(ComboBox combo)
    {
        // WPF's default ComboBox template still consults SystemColors for the
        // selected/hovered text in a few visual states.  Override those keys at
        // the control scope so a selected Pixels/Style item can never become
        // white-on-white even when Windows theme resources differ from TickLab.
        Brush selection = DrawingUiBrush("SelectionBrush", Color.FromRgb(36, 50, 71));
        Brush selectionText = DrawingUiBrush("SelectionTextBrush", Color.FromRgb(248, 250, 252));
        Brush menu = DrawingUiBrush("MenuBrush", Color.FromRgb(16, 16, 16));
        Brush text = DrawingUiBrush("TextBrush", Color.FromRgb(242, 242, 242));

        combo.Resources[SystemColors.HighlightBrushKey] = selection;
        combo.Resources[SystemColors.HighlightTextBrushKey] = selectionText;
        combo.Resources[SystemColors.WindowBrushKey] = menu;
        combo.Resources[SystemColors.WindowTextBrushKey] = text;
        combo.Resources[SystemColors.ControlBrushKey] = menu;
        combo.Resources[SystemColors.ControlTextBrushKey] = text;
    }

    private void PositionDrawingQuickEditor()
    {
        if (_quickEditBarManualPosition || DrawingQuickEditBar.Visibility != Visibility.Visible || _quickEditDrawing is null)
            return;

        CandleChartControl target = _quickEditChart ?? ActiveChartContext.Chart;
        Rect plot = target.GetPlotVisualBounds();
        if (plot.Width <= 1 || plot.Height <= 1)
            return;

        try
        {
            // Global rule: every completed/reselected drawing opens its contextual
            // toolbar at the top-center of the active chart, independent of where
            // the object itself is placed. Keep it strictly inside the protected
            // plot so it can never sit on the price or time scales.
            Point plotTopLeftScreen = target.PointToScreen(plot.TopLeft);
            Point plotTopRightScreen = target.PointToScreen(plot.TopRight);
            Point plotTopLeft = MainChartPaneRoot.PointFromScreen(plotTopLeftScreen);
            Point plotTopRight = MainChartPaneRoot.PointFromScreen(plotTopRightScreen);

            double plotLeft = Math.Min(plotTopLeft.X, plotTopRight.X);
            double plotRight = Math.Max(plotTopLeft.X, plotTopRight.X);
            DrawingQuickEditBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double barWidth = DrawingQuickEditBar.ActualWidth > 1 ? DrawingQuickEditBar.ActualWidth : DrawingQuickEditBar.DesiredSize.Width;
            double barHeight = DrawingQuickEditBar.ActualHeight > 1 ? DrawingQuickEditBar.ActualHeight : DrawingQuickEditBar.DesiredSize.Height;
            const double edge = 7.0;

            double x = plotLeft + (plotRight - plotLeft - barWidth) / 2.0;
            double y = Math.Min(plotTopLeft.Y, plotTopRight.Y) + edge;
            double hostWidth = Math.Max(1, MainChartPaneRoot.ActualWidth);
            double hostHeight = Math.Max(1, MainChartPaneRoot.ActualHeight);
            x = Math.Clamp(x, edge, Math.Max(edge, hostWidth - barWidth - edge));
            y = Math.Clamp(y, edge, Math.Max(edge, hostHeight - barHeight - edge));
            DrawingQuickEditBar.Margin = new Thickness(x, y, 0, 0);
        }
        catch (InvalidOperationException)
        {
            DrawingQuickEditBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double barWidth = DrawingQuickEditBar.ActualWidth > 1 ? DrawingQuickEditBar.ActualWidth : DrawingQuickEditBar.DesiredSize.Width;
            double hostWidth = Math.Max(1, MainChartPaneRoot.ActualWidth);
            DrawingQuickEditBar.Margin = new Thickness(Math.Max(7, (hostWidth - barWidth) / 2.0), 7, 0, 0);
        }
    }

    private void QueueQuickDrawingEdit(Func<ChartDrawing, ChartDrawing> change, bool commitImmediately = false)
    {
        if (_suppressQuickEdit || _quickEditDrawing is null)
            return;

        _quickEditOriginal ??= _quickEditDrawing;
        ChartDrawing current = _quickEditPreview ?? _quickEditDrawing;
        ChartDrawing updated = change(current) with { UpdatedAt = DateTimeOffset.UtcNow };
        _quickEditPreview = updated;
        _quickEditDrawing = updated;
        RefreshInlineDrawingInspector(updated);
        (_quickEditChart ?? CandleChart).PreviewDrawing(updated);

        _quickEditCommitTimer.Stop();
        if (commitImmediately)
            CommitQuickDrawingEdit();
        else
            _quickEditCommitTimer.Start();
    }

    private void CommitQuickDrawingEdit()
    {
        _quickEditCommitTimer.Stop();
        if (_quickEditOriginal is not ChartDrawing original || _quickEditPreview is not ChartDrawing preview)
            return;

        _quickEditOriginal = null;
        _quickEditPreview = preview;
        CandleChartControl target = _quickEditChart ?? CandleChart;
        target.PreviewDrawing(original);
        target.UpdateDrawing(preview);
        _quickEditDrawing = preview;
    }

    private void QuickLineColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickEditDrawing is null)
            return;
        string originalColor = _quickEditDrawing.Style.LineColor;
        var picker = new DrawingColorPickerWindow(originalColor) { Owner = this };
        picker.ColorPreviewChanged += color =>
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { LineColor = color } });
        bool accepted = picker.ShowDialog() == true;
        string finalColor = accepted ? picker.SelectedColor : originalColor;
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { LineColor = finalColor } }, commitImmediately: true);
        bool referenceLine = DrawingToolCatalog.Find(_quickEditDrawing?.ToolId)?.Category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann;
        if (referenceLine)
        {
            QuickLineColorButton.Background = DrawingUiBrush("PanelBrush", Color.FromRgb(12, 18, 28));
            QuickLineColorButton.Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
            QuickLineColorButton.BorderBrush = DrawingUiBrush("BorderStrongBrush", Color.FromRgb(51, 65, 85));
            QuickLineColorButton.BorderThickness = new Thickness(1);
            QuickLineColorButton.Content = CreateQuickLineColourSwatch(finalColor);
        }
        else
        {
            QuickLineColorButton.Background = QuickColorBrush(finalColor, Colors.DodgerBlue);
        }
    }

    private void QuickFillColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickEditDrawing is null)
            return;
        string originalColor = _quickEditDrawing.Style.FillColor;
        var picker = new DrawingColorPickerWindow(originalColor) { Owner = this };
        picker.ColorPreviewChanged += color =>
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { FillColor = color } });
        bool accepted = picker.ShowDialog() == true;
        string finalColor = accepted ? picker.SelectedColor : originalColor;
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { FillColor = finalColor } }, commitImmediately: true);
        QuickFillColorButton.Background = QuickColorBrush(finalColor, Colors.DodgerBlue);
    }

    private void QuickBackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickEditDrawing is null)
            return;
        string originalColor = _quickEditDrawing.Style.BackgroundColor;
        var picker = new DrawingColorPickerWindow(originalColor) { Owner = this };
        picker.ColorPreviewChanged += color =>
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { BackgroundColor = color } });
        bool accepted = picker.ShowDialog() == true;
        string finalColor = accepted ? picker.SelectedColor : originalColor;
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { BackgroundColor = finalColor } }, commitImmediately: true);
        QuickBackgroundColorButton.Background = QuickColorBrush(finalColor, Color.FromRgb(15, 23, 42));
    }

    private void QuickTextColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickEditDrawing is null)
            return;
        string originalColor = _quickEditDrawing.Style.TextColor;
        var picker = new DrawingColorPickerWindow(originalColor) { Owner = this };
        picker.ColorPreviewChanged += color =>
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { TextColor = color } });
        bool accepted = picker.ShowDialog() == true;
        string finalColor = accepted ? picker.SelectedColor : originalColor;
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { TextColor = finalColor } }, commitImmediately: true);
        QuickTextColorButton.Foreground = QuickColorBrush(finalColor, Colors.White);
        QuickTextColorButton.BorderBrush = QuickColorBrush(finalColor, Colors.White);
    }

    private void QuickFontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickFontSizeBox.SelectedItem is double size)
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { FontSize = size } }, commitImmediately: true);
    }

    private void QuickAnchorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickEditDrawing is null)
            return;
        string[] modes = { "Auto", "Top", "Right", "Bottom", "Left" };
        string current = _quickEditDrawing.TextOptions.TryGetValue("Anchor", out string? stored) && !string.IsNullOrWhiteSpace(stored)
            ? stored
            : "Auto";
        int currentIndex = Array.FindIndex(modes, value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
        string next = modes[(Math.Max(-1, currentIndex) + 1) % modes.Length];
        QueueQuickDrawingEdit(item =>
        {
            var options = item.TextOptions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            options["Anchor"] = next;
            return item with { TextOptions = options };
        }, commitImmediately: true);
        QuickAnchorButton.ToolTip = $"Anchor position: {next} (click to cycle)";
    }

    private void QuickTableColumnButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        CandleChartControl target = _quickEditChart ?? CandleChart;
        target.AddSelectedTableColumn();
        ShowDrawingQuickEditor(target.SelectedDrawing);
    }

    private void QuickTableRowButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        CandleChartControl target = _quickEditChart ?? CandleChart;
        target.AddSelectedTableRow();
        ShowDrawingQuickEditor(target.SelectedDrawing);
    }

    private void QuickLineWidthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickLineWidthBox.SelectedItem is double width)
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { LineWidth = width } }, commitImmediately: true);
    }

    private void QuickLineStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickLineStyleBox.SelectedItem is DrawingLineStyle style)
            QueueQuickDrawingEdit(item => item with { Style = item.Style with { LineStyle = style } }, commitImmediately: true);
    }

    private void QuickOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        // Slider direction is intentionally Transparent (left/0) -> Solid (right/1).
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { Opacity = Math.Clamp(e.NewValue, 0, 1) } });

    private void QuickFillOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { FillOpacity = Math.Clamp(e.NewValue, 0, 1) } });

    private void QuickFillToggle_Changed(object sender, RoutedEventArgs e)
    {
        double opacity = QuickFillToggle.IsChecked == true
            ? Math.Max(0.12, QuickFillOpacitySlider.Value)
            : 0;
        _suppressQuickEdit = true;
        QuickFillOpacitySlider.Value = opacity;
        _suppressQuickEdit = false;
        QueueQuickDrawingEdit(item => item with { Style = item.Style with { FillOpacity = opacity } }, commitImmediately: true);
    }

    private void QuickEditSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CommitQuickDrawingEdit();

    private void QuickTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        QueueQuickDrawingEdit(item => item with { Text = QuickTextBox.Text });

    private void QuickLockButton_Click(object sender, RoutedEventArgs e)
    {
        QueueQuickDrawingEdit(item => item with { IsLocked = !item.IsLocked }, commitImmediately: true);
        if (_quickEditDrawing is not null)
        {
            DrawingToolDefinition? tool = DrawingToolCatalog.Find(_quickEditDrawing.ToolId);
            Brush iconBrush = tool?.Category == DrawingToolCategory.TrendLine
                ? new SolidColorBrush(Color.FromRgb(47, 50, 56))
                : DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
            QuickLockButton.Content = DrawingToolIconFactory.CreateActionIcon(_quickEditDrawing.IsLocked ? "unlock" : "lock", 16, iconBrush);
            QuickLockButton.ToolTip = _quickEditDrawing.IsLocked ? "Unlock drawing" : "Lock drawing";
        }
    }

    private void QuickCloneButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is null)
            return;
        CandleChartControl target = _quickEditChart ?? CandleChart;
        target.CloneDrawingById(_quickEditDrawing.Id);
        ShowDrawingQuickEditor(target.SelectedDrawing);
    }

    private void QuickTextButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is not null)
            OpenDrawingSettings(_quickEditDrawing, "Text");
    }

    private void QuickVisualOrderButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is null)
            return;
        CandleChartControl target = _quickEditChart ?? CandleChart;
        string id = _quickEditDrawing.Id;
        var menu = new ContextMenu { PlacementTarget = QuickVisualOrderButton };
        MenuItem Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Add("Bring to front", () => target.BringDrawingToFront(id));
        Add("Bring forward", () => target.MoveDrawingLayer(id, 1));
        Add("Send backward", () => target.MoveDrawingLayer(id, -1));
        Add("Send to back", () => target.SendDrawingToBack(id));
        StyleReferenceLineContextMenu(menu);
        menu.IsOpen = true;
    }

    private void QuickAlertButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is not null)
            (_quickEditChart ?? CandleChart).RequestDrawingAlertById(_quickEditDrawing.Id);
    }

    private void QuickMoreButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is null)
            return;
        CandleChartControl target = _quickEditChart ?? CandleChart;
        ChartDrawing drawing = _quickEditDrawing;
        var menu = new ContextMenu { PlacementTarget = QuickMoreButton };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        var order = new MenuItem { Header = "Visual order" };
        order.Items.Add(Item("Bring to front", () => target.BringDrawingToFront(drawing.Id)));
        order.Items.Add(Item("Bring forward", () => target.MoveDrawingLayer(drawing.Id, 1)));
        order.Items.Add(Item("Send backward", () => target.MoveDrawingLayer(drawing.Id, -1)));
        order.Items.Add(Item("Send to back", () => target.SendDrawingToBack(drawing.Id)));
        menu.Items.Add(order);

        var intervals = new MenuItem { Header = "Visibility on intervals" };
        intervals.Items.Add(Item("Current interval and above", () => target.SetDrawingVisibilityCurrentAndAbove(drawing.Id)));
        intervals.Items.Add(Item("Current interval and below", () => target.SetDrawingVisibilityCurrentAndBelow(drawing.Id)));
        intervals.Items.Add(Item("Current interval only", () => target.SetDrawingVisibilityCurrentIntervalOnly(drawing.Id)));
        intervals.Items.Add(Item("All intervals", () => target.SetDrawingVisibilityAllIntervals(drawing.Id)));
        menu.Items.Add(intervals);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Clone", () => target.CloneDrawingById(drawing.Id)));
        menu.Items.Add(Item("Copy", () => target.CopyDrawingById(drawing.Id)));
        menu.Items.Add(Item(drawing.IsHidden ? "Show" : "Hide", () => target.ToggleDrawingHidden(drawing.Id)));
        menu.Items.Add(Item(drawing.VisualLayer == DrawingVisualLayer.BelowCandles ? "Remove from background" : "Place as background", () =>
        {
            ChartDrawing updated = drawing with
            {
                VisualLayer = drawing.VisualLayer == DrawingVisualLayer.BelowCandles
                    ? DrawingVisualLayer.AboveCandles
                    : DrawingVisualLayer.BelowCandles
            };
            target.UpdateDrawing(updated);
            ShowDrawingQuickEditor(updated);
        }));
        StyleReferenceLineContextMenu(menu);
        menu.IsOpen = true;
    }

    private void StyleReferenceLineContextMenu(ContextMenu menu)
    {
        Brush foreground = DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240));
        Brush border = DrawingUiBrush("BorderBrush", Color.FromRgb(51, 65, 85));
        Brush panel = DrawingUiBrush("MenuBrush", Color.FromRgb(16, 16, 16));
        menu.Background = panel;
        menu.Foreground = foreground;
        menu.BorderBrush = border;
        menu.BorderThickness = new Thickness(1);
        menu.Padding = new Thickness(3);

        void StyleItems(ItemCollection items)
        {
            foreach (object entry in items)
            {
                if (entry is not MenuItem item)
                    continue;
                // Do not pin local foreground/background values here.  Local
                // values override WPF hover/selected style triggers and caused
                // light highlight + light text in the quick-edit menus.
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.BorderBrush = Brushes.Transparent;
                item.Padding = new Thickness(10, 5, 10, 5);
                if (item.Items.Count > 0) StyleItems(item.Items);
            }
        }
        StyleItems(menu.Items);
    }

    private void QuickTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        ChartDrawing? drawing = _quickEditDrawing;
        if (drawing is null)
            return;

        CandleChartControl targetChart = _quickEditChart ?? CandleChart;
        var menu = new ContextMenu { PlacementTarget = QuickTemplateButton };
        var save = new MenuItem { Header = "Save current style as template" };
        save.Click += (_, _) =>
        {
            string name = $"{drawing.DisplayName} {DateTime.Now:HHmmss}";
            targetChart.SaveDrawingTemplate(drawing.Id, name, makeDefault: false);
            StatusText.Text = $"Template '{name}' saved.";
        };
        menu.Items.Add(save);

        var saveDefault = new MenuItem { Header = "Save and make default" };
        saveDefault.Click += (_, _) =>
        {
            string name = $"{drawing.DisplayName} default";
            targetChart.SaveDrawingTemplate(drawing.Id, name, makeDefault: true);
            StatusText.Text = $"Default template '{name}' saved.";
        };
        menu.Items.Add(saveDefault);

        DrawingTemplate[] templates = targetChart.DrawingTemplates
            .Where(item => item.ToolId == drawing.ToolId)
            .ToArray();
        if (templates.Length > 0)
        {
            menu.Items.Add(new Separator());
            foreach (DrawingTemplate template in templates)
            {
                DrawingTemplate captured = template;
                var item = new MenuItem { Header = $"Load: {captured.Name}" };
                item.Click += (_, _) =>
                {
                    targetChart.ApplyDrawingTemplate(drawing.Id, captured.Id);
                    ChartDrawing? refreshed = targetChart.ChartDrawings.FirstOrDefault(value => value.Id == drawing.Id);
                    ShowDrawingQuickEditor(refreshed);
                };
                menu.Items.Add(item);
            }
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Template manager and full settings…" };
        manage.Click += (_, _) => OpenDrawingSettings(drawing);
        menu.Items.Add(manage);
        if (DrawingToolCatalog.Find(drawing.ToolId)?.Category == DrawingToolCategory.TrendLine)
            StyleReferenceLineContextMenu(menu);
        menu.IsOpen = true;
    }

    private void QuickSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is not null)
            OpenDrawingSettings(_quickEditDrawing);
    }

    private void QuickDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        if (_quickEditDrawing is null)
            return;

        string id = _quickEditDrawing.Id;
        CandleChartControl? target = null;

        // The quick bar can remain open while chart focus/context changes. Always
        // resolve the chart that actually owns this drawing instead of falling back
        // to the primary CandleChart and silently deleting nothing.
        if (_quickEditChart is not null &&
            _quickEditChart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
        {
            target = _quickEditChart;
        }
        else if (ActiveChartContext.Chart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
        {
            target = ActiveChartContext.Chart;
        }
        else
        {
            target = _chartContexts.Values
                .Select(context => context.Chart)
                .FirstOrDefault(chart => chart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)));
        }

        target ??= CandleChart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            ? CandleChart
            : null;
        if (target is null)
            return;

        // Respect drawing/Lock-All protection exactly as before. If unlocked, the
        // explicit quick-menu Remove must remove the actual owning object.
        target.RemoveDrawingById(id);
        if (target.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
            return;

        DrawingQuickEditBar.Visibility = Visibility.Collapsed;
        _quickEditDrawing = null;
        _quickEditPreview = null;
        _quickEditOriginal = null;
        _quickEditChart = null;
    }

    private void CloseQuickEditButton_Click(object sender, RoutedEventArgs e)
    {
        CommitQuickDrawingEdit();
        DrawingQuickEditBar.Visibility = Visibility.Collapsed;
    }

    private void RefreshInlineDrawingInspector(ChartDrawing? drawing)
    {
        if (InlineInspectorLevelsPanel is null)
            return;

        InlineInspectorLevelsPanel.Children.Clear();
        if (drawing is null)
        {
            InlineInspectorTitleText.Text = "No drawing selected";
            InlineInspectorMetaText.Text = "Select a drawing to inspect style and levels";
            InlineInspectorLineSwatch.Background = QuickColorBrush("#2F80ED", Colors.DodgerBlue);
            InlineInspectorFillSwatch.Background = QuickColorBrush("#102C4F", Color.FromRgb(16, 44, 79));
            InlineInspectorLevelsPanel.Children.Add(new TextBlock
            {
                Text = "Line, fill, opacity, text, coordinates and templates appear here after selection.",
                Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(101, 120, 143)),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return;
        }

        InlineInspectorTitleText.Text = string.IsNullOrWhiteSpace(drawing.Name)
            ? drawing.DisplayName
            : drawing.Name;
        InlineInspectorMetaText.Text = $"{drawing.Style.LineWidth:0.#} px · {drawing.Style.Opacity * 100:0}% line · {drawing.Style.FillOpacity * 100:0}% fill";
        InlineInspectorLineSwatch.Background = QuickColorBrush(drawing.Style.LineColor, Colors.DodgerBlue);
        InlineInspectorFillSwatch.Background = QuickColorBrush(drawing.Style.FillColor, Color.FromRgb(16, 44, 79));

        DrawingLevel[] levels = (drawing.Levels.Count > 0
                ? drawing.Levels
                : DrawingToolCatalog.Find(drawing.ToolId)?.SupportsLevels == true
                    ? DrawingToolCatalog.DefaultFibonacciLevels()
                    : Array.Empty<DrawingLevel>())
            .Where(level => level.Enabled)
            .Take(7)
            .ToArray();

        if (levels.Length == 0)
        {
            InlineInspectorLevelsPanel.Children.Add(new TextBlock
            {
                Text = drawing.IsLocked
                    ? "Locked drawing · click Settings for full controls"
                    : "Live controls are available on the chart toolbar",
                Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(126, 145, 168)),
                FontSize = 9.5,
                Margin = new Thickness(0, 3, 0, 0)
            });
            return;
        }

        foreach (DrawingLevel level in levels)
        {
            var row = new Grid { Height = 21, Margin = new Thickness(0, 0, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

            row.Children.Add(new Border
            {
                Width = 14,
                Height = 3,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = QuickColorBrush(level.Color, Color.FromRgb(148, 163, 184))
            });
            var fill = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = QuickColorBrush(
                    string.IsNullOrWhiteSpace(level.FillColor) ? drawing.Style.FillColor : level.FillColor,
                    Color.FromRgb(51, 65, 85)),
                Opacity = level.FillOpacity >= 0 ? Math.Clamp(level.FillOpacity * 3.2, 0.28, 0.9) : 0.45
            };
            Grid.SetColumn(fill, 1);
            row.Children.Add(fill);

            var label = new TextBlock
            {
                Text = level.Label,
                Foreground = DrawingUiBrush("TextBrush", Color.FromRgb(205, 218, 233)),
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 2);
            row.Children.Add(label);

            var value = new TextBlock
            {
                Text = level.Value.ToString("0.###", CultureInfo.InvariantCulture),
                Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(112, 132, 155)),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 3);
            row.Children.Add(value);
            InlineInspectorLevelsPanel.Children.Add(row);
        }
    }

    private static UIElement CreateQuickLineColourSwatch(string colorText)
    {
        var host = new Grid { Width = 21, Height = 18, Background = Brushes.Transparent };
        host.Children.Add(new Border
        {
            Width = 19,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = QuickColorBrush(colorText, Colors.DodgerBlue),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return host;
    }

    private static Brush QuickColorBrush(string colorText, Color fallback)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString(colorText);
            if (converted is Color color)
                return new SolidColorBrush(color);
        }
        catch
        {
            // Invalid persisted colour is rendered with the safe fallback.
        }
        return new SolidColorBrush(fallback);
    }

    private void OpenDrawingSettings(ChartDrawing drawing, string initialTab = "Style")
    {
        CommitQuickDrawingEdit();

        // Drawing settings are intentionally modeless.  The reference keeps the chart
        // interactive while a settings panel is open, and WPF ShowDialog() disables the
        // owner window which made TickLab look completely frozen.
        if (_activeDrawingSettingsWindow is not null)
        {
            if (_activeDrawingSettingsWindow.WindowState == WindowState.Minimized)
                _activeDrawingSettingsWindow.WindowState = WindowState.Normal;
            _activeDrawingSettingsWindow.Activate();
            return;
        }

        CandleChartControl targetChart = _quickEditChart ?? ActiveChartContext.Chart;
        if (!targetChart.ChartDrawings.Any(item => string.Equals(item.Id, drawing.Id, StringComparison.Ordinal)))
        {
            targetChart = _chartContexts.Values
                .Select(context => context.Chart)
                .FirstOrDefault(chart => chart.ChartDrawings.Any(item => string.Equals(item.Id, drawing.Id, StringComparison.Ordinal)))
                ?? CandleChart;
        }
        _quickEditChart = targetChart;
        ChartDrawing original = drawing;
        DrawingToolDefinition? definition = DrawingToolCatalog.Find(original.ToolId);

        // Parity folders use dedicated live settings instead of the older one-size-fits-all
        // dialog.  Their surfaces follow the active TickLab theme while preserving the
        // reference tool-specific controls and construction behaviour.
        if (definition?.Category == DrawingToolCategory.TrendLine)
        {
            var lineWindow = new TradingViewLineSettingsWindow(
                original, targetChart.DrawingTemplates, targetChart, initialTab) { Owner = this };
            lineWindow.PreviewChanged += targetChart.PreviewDrawing;
            _activeDrawingSettingsWindow = lineWindow;
            lineWindow.Closed += (_, _) =>
            {
                _activeDrawingSettingsWindow = null;

                // Always restore the exact original first.  OK then creates one clean undo
                // step; Cancel/X leaves the chart exactly as it was before live preview.
                targetChart.PreviewDrawing(original);
                if (!lineWindow.WasAccepted)
                {
                    ShowDrawingQuickEditor(original);
                    StatusText.Text = "Drawing settings cancelled.";
                    return;
                }

                targetChart.UpdateDrawing(lineWindow.UpdatedDrawing);
                StatusText.Text = $"{original.DisplayName} settings applied.";
                ShowDrawingQuickEditor(lineWindow.UpdatedDrawing);
                RefreshDrawingObjectTree();
            };
            lineWindow.Show();
            lineWindow.Activate();
            StatusText.Text = $"Editing {original.DisplayName} settings — chart remains interactive.";
            return;
        }

        if (definition?.Category == DrawingToolCategory.FibonacciGann)
        {
            var fibWindow = new TradingViewFibGannSettingsWindow(original, targetChart.DrawingTemplates) { Owner = this };
            fibWindow.PreviewChanged += targetChart.PreviewDrawing;
            _activeDrawingSettingsWindow = fibWindow;
            fibWindow.Closed += (_, _) =>
            {
                _activeDrawingSettingsWindow = null;
                targetChart.PreviewDrawing(original);
                if (!fibWindow.WasAccepted)
                {
                    ShowDrawingQuickEditor(original);
                    StatusText.Text = "Drawing settings cancelled.";
                    return;
                }

                targetChart.UpdateDrawing(fibWindow.UpdatedDrawing);
                foreach (string templateId in fibWindow.TemplateIdsToDelete)
                    targetChart.DeleteDrawingTemplate(templateId);
                if (!string.IsNullOrWhiteSpace(fibWindow.TemplateIdToMakeDefault))
                    targetChart.SetDefaultDrawingTemplate(fibWindow.TemplateIdToMakeDefault);
                if (fibWindow.SaveAsTemplate)
                    targetChart.SaveDrawingTemplate(fibWindow.UpdatedDrawing.Id, fibWindow.TemplateName, fibWindow.MakeDefaultTemplate);
                ShowDrawingQuickEditor(fibWindow.UpdatedDrawing);
                RefreshDrawingObjectTree();
                StatusText.Text = $"{original.DisplayName} settings applied.";
            };
            fibWindow.Show();
            fibWindow.Activate();
            StatusText.Text = $"Editing {original.DisplayName} settings — chart remains interactive.";
            return;
        }

        var window = new DrawingSettingsWindow(original, targetChart.DrawingTemplates, targetChart.Candles) { Owner = this };
        window.PreviewChanged += targetChart.PreviewDrawing;
        _activeDrawingSettingsWindow = window;
        window.Closed += (_, _) =>
        {
            _activeDrawingSettingsWindow = null;

            // Always restore the exact original first. Apply then creates one clean undo step;
            // Cancel leaves the chart exactly as it was before live preview began.
            targetChart.PreviewDrawing(original);
            if (!window.WasAccepted)
            {
                ShowDrawingQuickEditor(original);
                StatusText.Text = "Drawing settings cancelled.";
                return;
            }

            targetChart.UpdateDrawing(window.UpdatedDrawing);
            foreach (string templateId in window.TemplateIdsToDelete)
                targetChart.DeleteDrawingTemplate(templateId);
            if (!string.IsNullOrWhiteSpace(window.TemplateIdToMakeDefault))
                targetChart.SetDefaultDrawingTemplate(window.TemplateIdToMakeDefault);
            if (window.SaveAsTemplate)
            {
                targetChart.SaveDrawingTemplate(window.UpdatedDrawing.Id, window.TemplateName, window.MakeDefaultTemplate);
                StatusText.Text = "Drawing updated and template saved.";
            }
            else
            {
                StatusText.Text = "Drawing settings applied.";
            }
            ShowDrawingQuickEditor(window.UpdatedDrawing);
            RefreshDrawingObjectTree();
        };
        window.Show();
        window.Activate();
        StatusText.Text = $"Editing {original.DisplayName} settings — chart remains interactive.";
    }

    private CandleChartControl DrawingChartForId(string id)
    {
        CandleChartControl active = ActiveChartContext.Chart;
        if (active.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
            return active;
        return _chartContexts.Values
            .Select(context => context.Chart)
            .Distinct()
            .FirstOrDefault(chart => chart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
            ?? active;
    }

    private void RemoveDrawingFromInspectorEverywhere(string id)
    {
        foreach (CandleChartControl chart in _chartContexts.Values.Select(context => context.Chart).Distinct().ToArray())
        {
            if (chart.ChartDrawings.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
                chart.RemoveDrawingById(id, overrideLock: true);
        }
        RefreshDrawingObjectTree();
    }

    private void OpenDrawingObjectTree()
    {
        if (_drawingObjectTreeWindow is null)
        {
            _drawingObjectTreeWindow = new DrawingObjectTreeWindow { Owner = this };
            _drawingObjectTreeWindow.Closed += (_, _) => _drawingObjectTreeWindow = null;
            _drawingObjectTreeWindow.SelectRequested += id => DrawingChartForId(id).SelectDrawingById(id);
            _drawingObjectTreeWindow.ToggleHiddenRequested += id => { DrawingChartForId(id).ToggleDrawingHidden(id); RefreshDrawingObjectTree(); };
            _drawingObjectTreeWindow.ToggleLockedRequested += id => { DrawingChartForId(id).ToggleDrawingLocked(id); RefreshDrawingObjectTree(); };
            _drawingObjectTreeWindow.BringFrontRequested += id => { DrawingChartForId(id).BringDrawingToFront(id); RefreshDrawingObjectTree(); };
            _drawingObjectTreeWindow.SendBackRequested += id => { DrawingChartForId(id).SendDrawingToBack(id); RefreshDrawingObjectTree(); };
            _drawingObjectTreeWindow.SettingsRequested += id =>
            {
                CandleChartControl chart = DrawingChartForId(id);
                ChartDrawing? drawing = chart.ChartDrawings.FirstOrDefault(item => item.Id == id);
                if (drawing is not null) OpenDrawingSettings(drawing);
            };
            _drawingObjectTreeWindow.RemoveRequested += RemoveDrawingFromInspectorEverywhere;
            _drawingObjectTreeWindow.RefreshRequested += RefreshDrawingObjectTree;
        }
        RefreshDrawingObjectTree();
        if (!_drawingObjectTreeWindow.IsVisible)
            _drawingObjectTreeWindow.Show();
        else
            _drawingObjectTreeWindow.Activate();
    }

    private void RefreshDrawingObjectTree()
    {
        _drawingObjectTreeWindow?.SetDrawings(ActiveChartContext.Chart.ChartDrawings);
        RefreshInlineDrawingObjectTree();
    }

    private void RefreshInlineDrawingObjectTree()
    {
        if (InlineDrawingObjectTreePanel is null)
            return;

        InlineDrawingObjectTreePanel.Children.Clear();
        CandleChartControl inspectorChart = ActiveChartContext.Chart;
        IReadOnlyList<ChartDrawing> drawings = inspectorChart.ChartDrawings
            .OrderByDescending(item => item.ZIndex)
            .ThenByDescending(item => item.UpdatedAt)
            .ToArray();

        if (drawings.Count == 0)
        {
            InlineDrawingObjectTreePanel.Children.Add(new TextBlock
            {
                Text = "No drawings on this chart",
                Foreground = DrawingUiBrush("MutedTextBrush", Color.FromRgb(113, 131, 155)),
                Margin = new Thickness(8, 16, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (ChartDrawing drawing in drawings)
        {
            var row = new Border
            {
                Height = 36,
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(6, 2, 4, 2),
                CornerRadius = new CornerRadius(4),
                Background = inspectorChart.SelectedDrawingIds.Contains(drawing.Id)
                    ? DrawingUiBrush("SelectionBrush", Color.FromRgb(36, 50, 71))
                    : DrawingUiBrush("PanelBrush", Color.FromRgb(13, 26, 43)),
                BorderBrush = DrawingUiBrush("BorderBrush", Color.FromRgb(34, 54, 80)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });

            DrawingToolDefinition? tool = DrawingToolCatalog.Find(drawing.ToolId);
            var icon = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
                Background = DrawingUiBrush("PanelAltBrush", Color.FromRgb(16, 35, 59)),
                Child = tool is null ? null : DrawingToolIconFactory.CreateToolIcon(tool, 15,
                    inspectorChart.SelectedDrawingIds.Contains(drawing.Id)
                        ? DrawingUiBrush("SelectionTextBrush", Color.FromRgb(248, 250, 252))
                        : DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240)))
            };
            grid.Children.Add(icon);
            string title = !string.IsNullOrWhiteSpace(drawing.Name) ? drawing.Name : drawing.DisplayName;
            var label = new TextBlock
            {
                Text = title, Foreground = inspectorChart.SelectedDrawingIds.Contains(drawing.Id)
                    ? DrawingUiBrush("SelectionTextBrush", Color.FromRgb(248, 250, 252))
                    : DrawingUiBrush("TextBrush", Color.FromRgb(214, 226, 240)),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(5, 0, 4, 0), FontSize = 11
            };
            Grid.SetColumn(label, 1); grid.Children.Add(label);

            var eye = new Button
            {
                Width = 25, Height = 25, MinHeight = 25, Padding = new Thickness(0), Margin = new Thickness(0),
                Content = drawing.IsHidden ? "○" : "◉", ToolTip = drawing.IsHidden ? "Show" : "Hide",
                Foreground = drawing.IsHidden
                    ? DrawingUiBrush("MutedTextBrush", Color.FromRgb(100, 116, 139))
                    : DrawingUiBrush("TextBrush", Color.FromRgb(226, 232, 240)),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent
            };
            eye.Click += (_, e) => { inspectorChart.ToggleDrawingHidden(drawing.Id); RefreshDrawingObjectTree(); e.Handled = true; };
            Grid.SetColumn(eye, 2); grid.Children.Add(eye);

            var lockButton = new Button
            {
                Width = 25, Height = 25, MinHeight = 25, Padding = new Thickness(0), Margin = new Thickness(0),
                Content = drawing.IsLocked ? "●" : "○", ToolTip = drawing.IsLocked ? "Unlock" : "Lock",
                Foreground = drawing.IsLocked
                    ? DrawingUiBrush("AccentBrightBrush", Color.FromRgb(96, 165, 250))
                    : DrawingUiBrush("MutedTextBrush", Color.FromRgb(148, 163, 184)),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent
            };
            lockButton.Click += (_, e) => { inspectorChart.ToggleDrawingLocked(drawing.Id); RefreshDrawingObjectTree(); e.Handled = true; };
            Grid.SetColumn(lockButton, 3); grid.Children.Add(lockButton);

            var deleteButton = new Button
            {
                Width = 25, Height = 25, MinHeight = 25, Padding = new Thickness(0), Margin = new Thickness(0),
                Content = "×", ToolTip = "Remove drawing",
                Foreground = DrawingUiBrush("DangerBrush", Color.FromRgb(239, 68, 68)),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, FontSize = 17
            };
            deleteButton.Click += (_, e) => { RemoveDrawingFromInspectorEverywhere(drawing.Id); e.Handled = true; };
            Grid.SetColumn(deleteButton, 4); grid.Children.Add(deleteButton);
            row.Child = grid;
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
                inspectorChart.SelectDrawingById(drawing.Id);
                RefreshDrawingObjectTree();
            };
            row.MouseRightButtonUp += (_, e) =>
            {
                var menu = new ContextMenu();
                var settings = new MenuItem { Header = "Settings" };
                settings.Click += (_, _) => OpenDrawingSettings(drawing);
                var front = new MenuItem { Header = "Bring to front" };
                front.Click += (_, _) => { inspectorChart.BringDrawingToFront(drawing.Id); RefreshDrawingObjectTree(); };
                var back = new MenuItem { Header = "Send to back" };
                back.Click += (_, _) => { inspectorChart.SendDrawingToBack(drawing.Id); RefreshDrawingObjectTree(); };
                var remove = new MenuItem { Header = "Remove" };
                remove.Click += (_, _) => RemoveDrawingFromInspectorEverywhere(drawing.Id);
                menu.Items.Add(settings); menu.Items.Add(front); menu.Items.Add(back); menu.Items.Add(new Separator()); menu.Items.Add(remove);
                menu.PlacementTarget = row; menu.IsOpen = true; e.Handled = true;
            };
            InlineDrawingObjectTreePanel.Children.Add(row);
        }
    }

    private void CandleChart_CandleSelected(object? sender, CandleSelectedEventArgs e)
    {
        _selectedCandle = e.Candle;
        _markerWindow?.SetSelectedCandle(e.Candle);
        StatusText.Text =
            $"Candle marked at {e.Candle.StartTime:yyyy-MM-dd HH:mm:ss}. " +
            "Right-click its tiny time-axis dot and choose Unmark to remove it.";
    }

    private void CandleChart_CandleUnmarked()
    {
        _selectedCandle = null;
        _markerWindow?.SetSelectedCandle(null);
        StatusText.Text = "Candle mark removed.";
    }

    private void BuildTimeframeButtons()
    {
        TimeframeButtonsPanel.Children.Clear();

        foreach (TimeframeDefinition timeframe in GetAllTimeframes().Where(item => !item.IsRawTickChart))
        {
            bool active = timeframe.Key == _activeTimeframe.Key;
            var button = new Button
            {
                Style = (Style)FindResource("TimeframeChipButton"),
                Content = timeframe.DisplayText,
                Tag = timeframe,
                MinWidth = timeframe.DisplayText.Length > 3 ? 58.5 : 46.5,
                Height = 42,
                Margin = new Thickness(0, 0, 3, 0),
                Padding = new Thickness(10.5, 3, 10.5, 3),
                Foreground = active
                    ? (TryFindResource("TextBrush") as Brush ?? Brushes.White)
                    : (TryFindResource("MutedTextBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(126, 145, 168))),
                FontSize = 15.75,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                // Selected timeframe uses only a subtle shade of the current theme surface.
                // No blue marker / blue border is used.
                Background = active
                    ? (TryFindResource("PanelAltBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(16, 16, 16)))
                    : Brushes.Transparent,
                BorderBrush = active
                    ? (TryFindResource("BorderStrongBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(69, 69, 69)))
                    : Brushes.Transparent,
                BorderThickness = active ? new Thickness(0, 0, 0, 2) : new Thickness(0),
                ToolTip = timeframe.IsBuiltIn
                    ? "Permanent TickLab timeframe"
                    : "Custom timeframe — right-click to remove"
            };

            button.Click += async (_, _) => await SelectTimeframeForActiveChartAsync(timeframe);

            var menu = new ContextMenu();
            bool isFavorite = IsFavoriteTimeframe(timeframe.Key);
            var favorite = new MenuItem
            {
                Header = isFavorite
                    ? $"Remove {timeframe.DisplayText} from Favorites"
                    : $"Add {timeframe.DisplayText} to Favorites"
            };
            favorite.Click += (_, _) => ToggleTimeframeFavorite(timeframe);
            menu.Items.Add(favorite);
            if (_favoriteTimeframeKeys.Count > 0)
            {
                var showFavorites = new MenuItem { Header = "Show Timeframe Favorites Bar" };
                showFavorites.Click += (_, _) => OpenTimeframeFavoritesWindow(forceShow: true);
                menu.Items.Add(showFavorites);
            }
            if (!timeframe.IsBuiltIn)
            {
                menu.Items.Add(new Separator());
                var remove = new MenuItem { Header = $"Remove {timeframe.DisplayText}" };
                remove.Click += (_, _) => RemoveCustomTimeframe(timeframe);
                menu.Items.Add(remove);
            }
            button.ContextMenu = menu;

            TimeframeButtonsPanel.Children.Add(button);
            if (active)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(button.BringIntoView));
        }
    }

    private void TimeframeScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;
        double delta = e.Delta > 0 ? -96 : 96;
        viewer.ScrollToHorizontalOffset(Math.Clamp(viewer.HorizontalOffset + delta, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private IEnumerable<TimeframeDefinition> GetAllTimeframes() =>
        TimeframeDefinition.BuiltIns
            .Concat(_customTimeframes)
            .GroupBy(item => item.Key)
            .Select(group => group.First())
            // Built-in and custom timeframes share one chronological selector.
            // A newly-created 16h therefore sits after 12h and before D,
            // instead of being appended after Monthly.
            .OrderBy(item => item.ToApproximateSeconds())
            .ThenBy(item => item.Unit == TimeframeUnit.Tick ? 0 : 1)
            .ThenBy(item => item.Quantity);

    private void RemoveCustomTimeframe(TimeframeDefinition timeframe)
    {
        if (timeframe.IsBuiltIn)
            return;

        _customTimeframes.RemoveAll(item => item.Key == timeframe.Key);
        _favoriteTimeframeKeys.RemoveAll(key => string.Equals(key, timeframe.Key, StringComparison.OrdinalIgnoreCase));
        RefreshTimeframeFavoritesWindow();
        if (_activeTimeframe.Key == timeframe.Key)
            _activeTimeframe = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
        SaveTimeframePreferences();
        BuildTimeframeButtons();
    }

    private void LoadSavedTimeframes()
    {
        _customTimeframes.Clear();
        foreach (CustomTimeframePreference saved in _preferences.CustomTimeframes)
        {
            try
            {
                TimeframeDefinition timeframe = TimeframeDefinition.CreateCustom(saved.Quantity, saved.Unit);
                if (!timeframe.IsBuiltIn && _customTimeframes.All(item => item.Key != timeframe.Key))
                    _customTimeframes.Add(timeframe);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }

    private void RestoreActiveTimeframe()
    {
        TimeframeDefinition? restored = GetAllTimeframes().FirstOrDefault(item =>
            string.Equals(item.Key, _preferences.LastActiveTimeframeKey, StringComparison.Ordinal));
        if (restored is not null)
            _activeTimeframe = restored;
    }

    private void SaveTimeframePreferences()
    {
        _preferences = _preferences with
        {
            LastActiveTimeframeKey = _activeTimeframe.Key,
            FavoriteTimeframeKeys = GetFavoriteTimeframes().Select(item => item.Key).ToArray(),
            CustomTimeframes = _customTimeframes
                .OrderBy(item => item.ToApproximateSeconds())
                .ThenBy(item => item.Unit == TimeframeUnit.Tick ? 0 : 1)
                .ThenBy(item => item.Quantity)
                .Select(item => new CustomTimeframePreference(item.Quantity, item.Unit))
                .ToArray()
        };
        SaveWorkspace();
    }

    private bool IsFavoriteTimeframe(string key) =>
        _favoriteTimeframeKeys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));

    private void ToggleTimeframeFavorite(TimeframeDefinition timeframe)
    {
        int index = _favoriteTimeframeKeys.FindIndex(item => string.Equals(item, timeframe.Key, StringComparison.OrdinalIgnoreCase));
        bool added = index < 0;
        if (added)
            _favoriteTimeframeKeys.Add(timeframe.Key);
        else
            _favoriteTimeframeKeys.RemoveAt(index);

        SaveTimeframePreferences();
        BuildTimeframeButtons();
        RefreshTimeframeFavoritesWindow();
        if (added)
            OpenTimeframeFavoritesWindow(forceShow: true);
        StatusText.Text = added
            ? $"{timeframe.DisplayText} added to timeframe Favorites."
            : $"{timeframe.DisplayText} removed from timeframe Favorites.";
    }

    private void RemoveMissingTimeframeFavorites()
    {
        HashSet<string> available = GetAllTimeframes().Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _favoriteTimeframeKeys.RemoveAll(key => !available.Contains(key));
    }

    private IReadOnlyList<TimeframeDefinition> GetFavoriteTimeframes()
    {
        Dictionary<string, TimeframeDefinition> available = GetAllTimeframes()
            .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var favorites = new List<TimeframeDefinition>();
        foreach (string key in _favoriteTimeframeKeys)
        {
            if (available.TryGetValue(key, out TimeframeDefinition? timeframe) && timeframe is not null)
                favorites.Add(timeframe);
        }

        return favorites
            .OrderBy(timeframe => timeframe.ToApproximateSeconds())
            .ThenBy(timeframe => timeframe.Unit == TimeframeUnit.Tick ? 0 : 1)
            .ThenBy(timeframe => timeframe.Quantity)
            .ToArray();
    }

    private void MoveTimeframeFavorite(string sourceKey, string targetKey)
    {
        int sourceIndex = _favoriteTimeframeKeys.FindIndex(item => string.Equals(item, sourceKey, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            return;
        string source = _favoriteTimeframeKeys[sourceIndex];
        _favoriteTimeframeKeys.RemoveAt(sourceIndex);
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            _favoriteTimeframeKeys.Add(source);
        }
        else
        {
            int targetIndex = _favoriteTimeframeKeys.FindIndex(item => string.Equals(item, targetKey, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
                _favoriteTimeframeKeys.Add(source);
            else
                _favoriteTimeframeKeys.Insert(targetIndex, source);
        }
        SaveTimeframePreferences();
        RefreshTimeframeFavoritesWindow();
    }

    private void RestoreTimeframeFavoritesWindow()
    {
        RemoveMissingTimeframeFavorites();
        if (_favoriteTimeframeKeys.Count > 0 && _preferences.TimeframeFavoritesWindowVisible)
            OpenTimeframeFavoritesWindow(forceShow: true);
    }

    private void OpenTimeframeFavoritesWindow(bool forceShow = true)
    {
        if (_isClosing || !IsLoaded)
            return;

        if (_timeframeFavoritesWindow is null)
        {
            var window = new TimeframeFavoritesWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            _timeframeFavoritesWindow = window;
            window.TimeframeSelected += key =>
            {
                TimeframeDefinition? timeframe = GetAllTimeframes().FirstOrDefault(item =>
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (timeframe is not null)
                    Dispatcher.BeginInvoke(new Action(async () => await SelectTimeframeForActiveChartAsync(timeframe)));
            };
            window.RemoveRequested += key =>
            {
                _favoriteTimeframeKeys.RemoveAll(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
                SaveTimeframePreferences();
                BuildTimeframeButtons();
                RefreshTimeframeFavoritesWindow();
            };
            window.CompactChanged += _ => SaveWorkspace();
            window.IsVisibleChanged += (_, _) =>
            {
                UpdateDrawingToolbarState();
                if (!_isClosing)
                    SaveWorkspace();
            };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_timeframeFavoritesWindow, window))
                    _timeframeFavoritesWindow = null;
            };
            window.Left = double.IsFinite(_preferences.TimeframeFavoritesWindowLeft)
                ? _preferences.TimeframeFavoritesWindowLeft
                : Left + 220;
            window.Top = double.IsFinite(_preferences.TimeframeFavoritesWindowTop)
                ? _preferences.TimeframeFavoritesWindowTop
                : Top + 92;
            window.SetCompact(_preferences.TimeframeFavoritesWindowCompact);
            window.EnsureVisible(this);
        }

        RefreshTimeframeFavoritesWindow();
        if (_timeframeFavoritesWindow is null)
            return;
        _timeframeFavoritesWindow.EnsureVisible(this);
        if (forceShow && !_timeframeFavoritesWindow.IsVisible)
            _timeframeFavoritesWindow.Show();
        if (forceShow)
        {
            _timeframeFavoritesWindow.Activate();
            QueueFavoritesTabsPosition();
        }
    }

    private void RefreshTimeframeFavoritesWindow()
    {
        if (_isClosing)
            return;
        RemoveMissingTimeframeFavorites();
        if (_favoriteTimeframeKeys.Count == 0)
        {
            _timeframeFavoritesWindow?.Hide();
            return;
        }
        _timeframeFavoritesWindow?.SetTimeframes(GetFavoriteTimeframes());
        QueueFavoritesTabsPosition();
    }

    private void ApplyChartSettings(ChartSettings settings)
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Settings = settings;
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        context.IndicatorStack.SetChartSettings(context.Settings);
        ApplyIndicatorWorkspaceChartSettings(context);
        SyncDetachedChartWindows();
    }

    private void SetChartIdentityUi()
    {
        TopSymbolText.Text = string.IsNullOrWhiteSpace(_requestedSymbol)
            ? "Search instruments"
            : _requestedSymbol;
        TopTimeframeText.Text = _activeTimeframe.DisplayText;
        ChartTitleText.Text = string.IsNullOrWhiteSpace(_requestedSymbol)
            ? (_activeTimeframe.IsRawTickChart ? "RAW TICK CHART" : "MAIN CANDLE CHART")
            : $"{_requestedSymbol}  •  {_activeTimeframe.DisplayText}";
        SymbolDetailsTitle.Text = string.IsNullOrWhiteSpace(_requestedSymbol)
            ? "Market Overview"
            : _requestedSymbol;
        SymbolDescriptionText.Text = _activeTimeframe.IsRawTickChart
            ? "Source: saved raw ticks + live MT5 Bid/Ask"
            : $"Source: {_sourceTimeframe}";
        RefreshChartMarkers();
    }

    private void UpdateChartUi(string sourceDescription)
    {
        if (_displayCandles.Count == 0)
            return;

        Candle last = _displayCandles[^1];
        int digits = Math.Clamp(last.Digits, 0, 10);
        string format = $"F{digits}";
        LastPriceText.Text = last.Close.ToString(format, CultureInfo.InvariantCulture);
        CandleCountText.Text = _displayCandles.Count.ToString("N0", CultureInfo.InvariantCulture);
        ChartOhlcText.Text =
            $"O {last.Open.ToString(format, CultureInfo.InvariantCulture)}   " +
            $"H {last.High.ToString(format, CultureInfo.InvariantCulture)}   " +
            $"L {last.Low.ToString(format, CultureInfo.InvariantCulture)}   " +
            $"C {last.Close.ToString(format, CultureInfo.InvariantCulture)}   " +
            $"TV {last.TickVolume:N0}";

        double change = last.Open == 0 ? 0 : (last.Close - last.Open) / last.Open * 100;
        PriceChangeText.Text = $"{change:+0.###;-0.###;0}%  •  {sourceDescription}";
        // Connection status is owned only by RefreshConnectorStateAsync.
        // Chart rendering must never overwrite it with a second competing
        // state such as LIVE, which previously caused visible flicker.
        StatusText.Text = $"{last.Symbol} {_activeTimeframe.DisplayText}  •  {sourceDescription}";
        RefreshAllAppliedIndicators();
        EvaluateLiveAlerts(ActiveChartContext);
    }

    private void SaveWorkspace()
    {
        _workspaceDirty = true;
    }

    private async void WorkspaceSaveTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || !IsLoaded || !_workspaceDirty || _workspaceSaveRunning)
            return;

        _workspaceSaveRunning = true;
        try
        {
            UserPreferences snapshot = CaptureWorkspacePreferences();
            _workspaceDirty = false;
            await Task.Run(() => _settingsStore.Save(snapshot), _lifetime.Token);
            _lastWorkspaceSaveErrorSignature = string.Empty;
        }
        catch (OperationCanceledException)
        {
            _workspaceDirty = true;
        }
        catch (Exception exception)
        {
            _workspaceDirty = true;
            string signature = exception.GetType().FullName + "|" + exception.Message;
            if (!string.Equals(signature, _lastWorkspaceSaveErrorSignature, StringComparison.Ordinal))
            {
                _lastWorkspaceSaveErrorSignature = signature;
                StatusText.Text = $"Workspace save paused: {exception.Message}";
                TickLabErrorEngine.Report(
                    exception,
                    new TickLabErrorContext(
                        "Workspace persistence",
                        "save_user_preferences_background",
                        "TickLab remains usable. The background save will retry automatically.",
                        ErrorCode: "TL-SETTINGS-SAVE-BG",
                        Symbol: _requestedSymbol,
                        Timeframe: _sourceTimeframe),
                    TickLabErrorSeverity.Warning,
                    this,
                    showPopup: false);
            }
        }
        finally
        {
            _workspaceSaveRunning = false;
        }
    }

    private UserPreferences CaptureWorkspacePreferences()
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _preferences = _preferences with
        {
            LastConnectorId = _selectedConnector?.ConnectorId ?? _preferences.LastConnectorId,
            BridgeFolderOverride = _bridgeClient.ManualConnectionsRoot ?? string.Empty,
            LastChartSymbol = _requestedSymbol,
            LastChartTimeframe = _activeTimeframe.DisplayText,
            LastActiveTimeframeKey = _activeTimeframe.Key,
            Viewport = CandleChart.CaptureViewport(),
            ReceiveMarkers = _receiveMarkers,
            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            HistoryDisplayMode = _historySelection.Mode,
            SelectedHistorySegments = _historySelection.SegmentKeys ?? Array.Empty<string>(),
            AppliedIndicatorSourcePaths = GetChartContext(1).AppliedIndicators
                .Select(item => item.SourcePath)
                .ToArray(),
            AppliedTickScriptIndicators = CaptureTickScriptIndicatorPreferences(GetChartContext(1)),
            AppliedBuiltInIndicators = CloneBuiltInIndicators(GetChartContext(1).BuiltInIndicators),
            DrawingDocuments = new[] { CandleChart.ExportDrawingWorkspaceJson() },
            DrawingToolbarCollapsed = _drawingToolbarCollapsed,
            DrawingFavoritesWindowVisible = _drawingFavoritesWindow?.IsVisible == true,
            DrawingFavoritesWindowCompact = _drawingFavoritesWindow?.IsCompact ?? _preferences.DrawingFavoritesWindowCompact,
            DrawingFavoritesWindowLeft = _drawingFavoritesWindow?.Left ?? _preferences.DrawingFavoritesWindowLeft,
            DrawingFavoritesWindowTop = _drawingFavoritesWindow?.Top ?? _preferences.DrawingFavoritesWindowTop,
            DrawingFavoritesWindowWidth = _drawingFavoritesWindow?.Width ?? _preferences.DrawingFavoritesWindowWidth,
            DrawingFavoritesWindowHeight = _drawingFavoritesWindow?.Height ?? _preferences.DrawingFavoritesWindowHeight,
            FavoriteTimeframeKeys = GetFavoriteTimeframes().Select(item => item.Key).ToArray(),
            TimeframeFavoritesWindowVisible = _timeframeFavoritesWindow?.IsVisible == true,
            TimeframeFavoritesWindowCompact = _timeframeFavoritesWindow?.IsCompact ?? _preferences.TimeframeFavoritesWindowCompact,
            TimeframeFavoritesWindowLeft = _timeframeFavoritesWindow?.Left ?? _preferences.TimeframeFavoritesWindowLeft,
            TimeframeFavoritesWindowTop = _timeframeFavoritesWindow?.Top ?? _preferences.TimeframeFavoritesWindowTop,
            TimeframeFavoritesWindowWidth = _timeframeFavoritesWindow?.Width ?? _preferences.TimeframeFavoritesWindowWidth,
            TimeframeFavoritesWindowHeight = _timeframeFavoritesWindow?.Height ?? _preferences.TimeframeFavoritesWindowHeight,
            WorkspaceStateInitialized = true,
            ActiveWorkspaceId = _activeWorkspaceId,
            PreferredWorkspaceLayout = _preferredWorkspaceLayout,
            Workspaces = CaptureWorkspacePagePreferences(),
            FloatingPanes = CaptureFloatingPanePreferences()
        };

        return _preferences;
    }

    private void SaveWorkspaceSynchronously()
    {
        if (!IsLoaded)
            return;

        try
        {
            UserPreferences snapshot = CaptureWorkspacePreferences();
            _settingsStore.Save(snapshot);
            _workspaceDirty = false;
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "Workspace persistence",
                    "save_user_preferences_on_close",
                    "TickLab is closing. Existing saved workspace data remains intact.",
                    ErrorCode: "TL-SETTINGS-SAVE-CLOSE",
                    Symbol: _requestedSymbol,
                    Timeframe: _sourceTimeframe),
                TickLabErrorSeverity.Warning,
                this,
                showPopup: false);
        }
    }

    private void RestoreWindowBounds()
    {
        Rect work = SystemParameters.WorkArea;
        double width = Math.Clamp(_preferences.WindowWidth, MinWidth, Math.Max(MinWidth, work.Width - 48));
        double height = Math.Clamp(_preferences.WindowHeight, MinHeight, Math.Max(MinHeight, work.Height - 48));
        double left = double.IsFinite(_preferences.WindowLeft)
            ? _preferences.WindowLeft
            : work.Left + (work.Width - width) / 2;
        double top = double.IsFinite(_preferences.WindowTop)
            ? _preferences.WindowTop
            : work.Top + (work.Height - height) / 2;

        left = Math.Clamp(left, work.Left + 24, Math.Max(work.Left + 24, work.Right - width - 24));
        top = Math.Clamp(top, work.Top + 24, Math.Max(work.Top + 24, work.Bottom - height - 24));

        WindowState = WindowState.Normal;
        Width = width;
        Height = height;
        Left = left;
        Top = top;

        if (_preferences.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private static bool IsDirectNative(TimeframeDefinition timeframe) =>
        !timeframe.UsesTickArchive &&
        !string.IsNullOrWhiteSpace(timeframe.NativeMt5Code);

    private static bool MatchesSource(Candle candle, string symbol, string timeframe) =>
        string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candle.Timeframe, timeframe, StringComparison.Ordinal);

    private static string NormalizeTimeframe(string? timeframe) =>
        string.IsNullOrWhiteSpace(timeframe) ? "PERIOD_M1" : timeframe.Trim();

    private static int LowerBoundByStart(
        IReadOnlyList<Candle> candles,
        long startUnix)
    {
        int low = 0;
        int high = candles.Count;

        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (candles[middle].StartUnix < startUnix)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int CountAppended(
        IReadOnlyList<Candle> previous,
        IReadOnlyList<Candle> current)
    {
        if (previous.Count == 0 || current.Count == 0)
            return 0;
        long previousLast = previous[^1].StartUnix;
        int count = 0;
        for (int index = current.Count - 1; index >= 0 && current[index].StartUnix > previousLast; index--)
            count++;
        return count;
    }

    private DateTime _lastMarketWorkspaceRefreshUtc = DateTime.MinValue;


    private static bool IsWithinMarketFilterButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void MarketFilterScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;
        double delta = e.Delta > 0 ? -110.0 : 110.0;
        viewer.ScrollToHorizontalOffset(Math.Clamp(viewer.HorizontalOffset + delta, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private void MarketFilterScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer viewer ||
            IsWithinMarketFilterButton(e.OriginalSource as DependencyObject))
            return;
        _marketFilterDragging = true;
        _marketFilterDragStart = e.GetPosition(viewer);
        _marketFilterDragStartOffset = viewer.HorizontalOffset;
        viewer.CaptureMouse();
        e.Handled = true;
    }

    private void MarketFilterScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_marketFilterDragging || sender is not ScrollViewer viewer || e.LeftButton != MouseButtonState.Pressed)
            return;
        double dx = e.GetPosition(viewer).X - _marketFilterDragStart.X;
        viewer.ScrollToHorizontalOffset(Math.Clamp(_marketFilterDragStartOffset - dx, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private void MarketFilterScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        EndMarketFilterDrag(sender as ScrollViewer);

    private void MarketFilterScrollViewer_LostMouseCapture(object sender, MouseEventArgs e) =>
        _marketFilterDragging = false;

    private void EndMarketFilterDrag(ScrollViewer? viewer)
    {
        if (!_marketFilterDragging)
            return;
        _marketFilterDragging = false;
        if (viewer?.IsMouseCaptured == true)
            viewer.ReleaseMouseCapture();
    }

    private void BuildMarketFilterButtons()
    {
        if (MarketFilterPanel is null)
            return;

        MarketFilterPanel.Children.Clear();
        foreach (string filter in InstrumentCategoryClassifier.Filters)
        {
            var button = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = filter,
                Tag = filter,
                Height = 28,
                MinWidth = 62,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 5, 0),
                IsChecked = string.Equals(filter, _marketFilter, StringComparison.OrdinalIgnoreCase)
            };
            button.Click += MarketFilterButton_Click;
            MarketFilterPanel.Children.Add(button);
        }
    }

    private void MarketFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton selected ||
            selected.Tag is not string filter)
            return;

        _marketFilter = filter;
        foreach (System.Windows.Controls.Primitives.ToggleButton button in MarketFilterPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>())
            button.IsChecked = ReferenceEquals(button, selected);
        RefreshMarketWorkspace();
    }

    private void MarketSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshMarketWorkspace();

    private void RefreshMarketWorkspace(bool throttle = false)
    {
        if (WatchlistGrid is null || MarketSearchBox is null)
            return;
        if (throttle && DateTime.UtcNow - _lastMarketWorkspaceRefreshUtc < TimeSpan.FromMilliseconds(750))
            return;
        _lastMarketWorkspaceRefreshUtc = DateTime.UtcNow;

        string query = MarketSearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<Mt5SymbolInfo> source = _availableSymbols;

        if (string.Equals(_marketFilter, "Favorites", StringComparison.OrdinalIgnoreCase))
            source = source.Where(item => _marketFavouriteSymbols.Contains(item.Name));
        else if (!string.Equals(_marketFilter, "All", StringComparison.OrdinalIgnoreCase))
            source = source.Where(item => string.Equals(InstrumentCategoryClassifier.Classify(item), _marketFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        string? selectedSymbol = (WatchlistGrid.SelectedItem as WatchlistRow)?.Symbol;
        WatchlistRow[] rows = source
            .OrderBy(item => _marketFavouriteSymbols.Contains(item.Name) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildWatchlistRow)
            .ToArray();
        WatchlistGrid.ItemsSource = rows;

        if (!string.IsNullOrWhiteSpace(selectedSymbol))
        {
            WatchlistRow? selected = rows.FirstOrDefault(item => string.Equals(item.Symbol, selectedSymbol, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                WatchlistGrid.SelectedItem = selected;
        }
    }

    private WatchlistRow BuildWatchlistRow(Mt5SymbolInfo symbol)
    {
        string last = "—";
        string spread = "—";

        ChartRuntimeContext? context = _chartContexts.Values
            .Where(item => string.Equals(item.Symbol, symbol.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.LastRawTickRefreshUtc)
            .FirstOrDefault();

        if (context is not null)
        {
            if (context.TickHistory.Count > 0)
            {
                MarketTick tick = context.TickHistory[^1];
                double shown = tick.Bid > 0 ? tick.Bid : tick.Last;
                if (shown > 0)
                    last = shown.ToString($"F{Math.Clamp(symbol.Digits, 0, 10)}", CultureInfo.InvariantCulture);
                if (tick.Bid > 0 && tick.Ask > 0)
                {
                    double point = Math.Pow(10, -Math.Clamp(symbol.Digits, 0, 10));
                    spread = point > 0
                        ? ((tick.Ask - tick.Bid) / point).ToString("0.##", CultureInfo.InvariantCulture)
                        : (tick.Ask - tick.Bid).ToString("G", CultureInfo.InvariantCulture);
                }
            }
            else if (context.DisplayCandles.Count > 0)
            {
                Candle candle = context.DisplayCandles[^1];
                last = candle.Close.ToString($"F{Math.Clamp(symbol.Digits, 0, 10)}", CultureInfo.InvariantCulture);
                spread = candle.Spread.ToString(CultureInfo.InvariantCulture);
            }
        }

        return new WatchlistRow(
            symbol.Name,
            last,
            spread,
            _marketFavouriteSymbols.Contains(symbol.Name) ? "★" : "☆",
            InstrumentCategoryClassifier.Classify(symbol));
    }

    private void MarketFavourite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string symbol } || string.IsNullOrWhiteSpace(symbol))
            return;

        if (!_marketFavouriteSymbols.Add(symbol))
            _marketFavouriteSymbols.Remove(symbol);
        SaveMarketFavourites();
        RefreshMarketWorkspace();
        e.Handled = true;
    }

    private void WatchlistGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WatchlistGrid.SelectedItem is not WatchlistRow row)
            return;

        Mt5SymbolInfo? symbol = _availableSymbols.FirstOrDefault(item =>
            string.Equals(item.Name, row.Symbol, StringComparison.OrdinalIgnoreCase));
        SymbolDetailsTitle.Text = row.Symbol;
        SymbolDescriptionText.Text = symbol?.Description ?? string.Empty;
        LastPriceText.Text = row.Last;
        PriceChangeText.Text = row.Spread == "—" ? "Spread —" : $"Spread {row.Spread} points";
    }

    private void WatchlistGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WatchlistGrid.SelectedItem is not WatchlistRow row)
            return;
        Mt5SymbolInfo? symbol = _availableSymbols.FirstOrDefault(item =>
            string.Equals(item.Name, row.Symbol, StringComparison.OrdinalIgnoreCase));
        if (symbol is null)
            return;
        _requestedSymbol = symbol.Name;
        TopSymbolText.Text = symbol.Name;
        _ = SafeSelectChartAsync(symbol.Name, _activeTimeframe);
    }

    private void LoadMarketFavourites()
    {
        try
        {
            _marketFavouriteSymbols.Clear();
            if (!File.Exists(MarketFavouriteFilePath))
                return;
            string[]? saved = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(MarketFavouriteFilePath));
            if (saved is null)
                return;
            foreach (string symbol in saved.Where(item => !string.IsNullOrWhiteSpace(item)))
                _marketFavouriteSymbols.Add(symbol.Trim());
        }
        catch
        {
            _marketFavouriteSymbols.Clear();
        }
    }

    private void SaveMarketFavourites()
    {
        try
        {
            string? directory = Path.GetDirectoryName(MarketFavouriteFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                MarketFavouriteFilePath,
                System.Text.Json.JsonSerializer.Serialize(
                    _marketFavouriteSymbols.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed record LocalChartResult(
        List<Candle> Source,
        List<Candle> Display,
        string Description,
        long? BoundaryUnix,
        string BoundaryLabel);

    private sealed record NativeLiveResult(
        DateTime LiveWriteUtc,
        DateTime ClosedWriteUtc,
        Candle? LiveCandle,
        Candle? ClosedCandle);

    private sealed record WatchlistRow(string Symbol, string Last, string Spread, string StarGlyph, string Category);

    private sealed record PendingHistoryWrite(
        string ConnectorId,
        Candle Candle);

    private sealed record HistoryImportPhase(
        string Timeframe,
        bool IncludeTicks,
        string Description,
        bool SavePermanent,
        long? MinimumStartUnix,
        string? OnlySegmentKey,
        bool ImportCandles = true,
        string? ProgressLabel = null);

    private sealed record PendingChartLaunch(
        string ConnectorId,
        string Symbol,
        HistoryImportPhase Phase,
        Mt5HistoryStatus Status,
        int PhaseNumber,
        int PhaseCount,
        string ProgressLabel,
        string Reason);

    private sealed class HistoryRestartRequestedException : Exception { }

    private sealed class CandleStartComparer : IComparer<Candle>
    {
        public static CandleStartComparer Instance { get; } = new();
        public int Compare(Candle? x, Candle? y) =>
            (x?.StartUnix ?? 0).CompareTo(y?.StartUnix ?? 0);
    }
}
