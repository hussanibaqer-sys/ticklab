using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Alerts;
using TickLab.Core.Drawing;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl : FrameworkElement
{
    private const double LeftMargin = 12;
    private const double TopMargin = 12;
    private const double RightMargin = 48;
    private const double BottomMargin = 32;
    private const int DefaultVisibleCount = 110;
    private const int MinimumFitVisibleCount = 70;
    private const int MaximumFitVisibleCount = 160;
    private const double PreferredCandleSlotWidth = 10.0;
    private const int DefaultFutureFrames = 10;
    private const int MaximumFutureDragFrames = 1_000_000_000;
    private const double CandleGapPixels = 3.0;
    private const double WheelVisualZoomStep = 1.10;
    private const double MaximumZoomFactor = 10_000.0;

    /// <summary>
    /// Returns the candle/drawing plot in this control's coordinates. The returned
    /// rectangle excludes the right price scale and bottom time scale so overlay UI
    /// (Favorites tabs / quick edit) can stay inside the same protected chart area.
    /// </summary>
    public Rect GetPlotVisualBounds()
    {
        double width = Math.Max(0, ActualWidth - LeftMargin - RightMargin);
        double height = Math.Max(0, ActualHeight - TopMargin - BottomMargin);
        return new Rect(LeftMargin, TopMargin, width, height);
    }
    private const int MaximumHorizontalVisibleCandles = 1_500;
    private const int MinimumBoundaryPrefetchSlots = 256;
    private const int MaximumBoundaryPrefetchSlots = 800;

    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(7, 16, 27));
    private static readonly Brush GridBrush =
        new SolidColorBrush(Color.FromRgb(20, 34, 52));
    private static readonly Brush TextBrush =
        new SolidColorBrush(Color.FromRgb(145, 164, 186));
    private static readonly Brush UpBrush =
        new SolidColorBrush(Color.FromRgb(34, 201, 122));
    private static readonly Brush DownBrush =
        new SolidColorBrush(Color.FromRgb(240, 82, 97));
    private static readonly Brush SelectionDotBrush =
        new SolidColorBrush(Color.FromRgb(41, 98, 255));
    private static readonly Brush LivePriceBrush =
        new SolidColorBrush(Color.FromRgb(41, 98, 255));
    private static readonly Brush HistoryBoundaryBrush =
        new SolidColorBrush(Color.FromRgb(243, 174, 55));
    private static readonly Brush MarkerLabelBrush =
        new SolidColorBrush(Color.FromArgb(205, 16, 27, 43));

    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen UpPen =
        new(
            new SolidColorBrush(
                Color.FromRgb(
                    82,
                    224,
                    153)),
            1);
    private static readonly Pen DownPen =
        new(
            new SolidColorBrush(
                Color.FromRgb(
                    255,
                    115,
                    128)),
            1);
    private static readonly Pen LivePricePen =
        new(
            LivePriceBrush,
            1)
        {
            DashStyle = DashStyles.Dash
        };
    private static readonly Pen HistoryBoundaryPen =
        new(HistoryBoundaryBrush, 1.5)
        {
            DashStyle = DashStyles.Dash
        };
    private static readonly Pen MarkerPen =
        new(new SolidColorBrush(Color.FromRgb(255, 193, 7)), 1.5)
        {
            DashStyle = DashStyles.Dash
        };
    private static readonly Pen CrosshairPen =
        new(
            new SolidColorBrush(
                Color.FromRgb(
                    96,
                    117,
                    141)),
            1)
        {
            DashStyle = DashStyles.Dash
        };

    private static readonly Typeface Typeface =
        new(
            new FontFamily("Segoe UI"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

    private IReadOnlyList<Candle> _sourceCandles =
        Array.Empty<Candle>();
    private IReadOnlyList<Candle> _candles =
        Array.Empty<Candle>();
    private ChartSettings _settings =
        ChartSettings.Default;
    private Candle? _selectedCandle;
    private Point? _mousePosition;
    private double? _externalCrosshairRatio;

    private int _visibleCount =
        DefaultVisibleCount;
    private int _rightOffset;
    private int _horizontalReferenceCount =
        DefaultVisibleCount;

    private bool _verticalAuto = true;
    private double _manualMinimum;
    private double _manualMaximum;
    private double _verticalReferenceSpan;
    private bool _viewportInitialized;
    private string _viewportSymbol = string.Empty;
    private string _viewportTimeframe = string.Empty;

    private DragMode _dragMode;
    private Point _dragStart;
    private int _dragStartVisibleCount;
    private int _dragStartRightOffset;
    private double _latestCandleAnchorRatio = double.NaN;
    private double _dragStartLatestCandleAnchorRatio = double.NaN;
    private double _dragStartMinimum;
    private double _dragStartMaximum;
    private bool _liveButtonHovered;
    private bool _oneShotZoomInMode;
    private bool _olderHistoryRequestPending;
    private bool _newerHistoryRequestPending;
    private bool _canRequestOlderHistory = true;
    private bool _canRequestNewerHistory;
    private IReadOnlyList<ChartTimelineGap> _timelineGaps = Array.Empty<ChartTimelineGap>();
    private IReadOnlyList<CandleMarker> _markers = Array.Empty<CandleMarker>();
    private long? _historicalNavigationAnchorUnix;
    private bool _historicalNavigationAnchorSelected;
    private CandleMarker? _contextMarker;
    private CandleMarker? _interactiveSelectionMarker;
    private CandleMarker? _interactiveReplayEndMarker;
    private bool _markerSelectionMode;
    private bool _interactiveMarkerDragging;
    private bool _interactiveReplayEndDragging;
    private bool _interactiveMarkerPlacementPending;
    private IReadOnlyList<AlertLineOverlay> _alertLines = Array.Empty<AlertLineOverlay>();
    private OrderFlowProfileSnapshot _orderFlowProfile = OrderFlowProfileSnapshot.Empty;
    private string? _draggingAlertLineId;
    private double? _draggingAlertPrice;
    private readonly DispatcherTimer _scrollModeMessageTimer;
    private readonly DispatcherTimer _countdownTimer;
    private string _scrollModeMessage = string.Empty;

    static CandleChartControl()
    {
        Freeze(BackgroundBrush);
        Freeze(GridBrush);
        Freeze(TextBrush);
        Freeze(UpBrush);
        Freeze(DownBrush);
        Freeze(SelectionDotBrush);
        Freeze(LivePriceBrush);
        Freeze(HistoryBoundaryBrush);
        Freeze(MarkerLabelBrush);
        Freeze(GridPen);
        Freeze(UpPen);
        Freeze(DownPen);
        Freeze(LivePricePen);
        Freeze(HistoryBoundaryPen);
        Freeze(MarkerPen);
        Freeze(CrosshairPen);
    }

    public CandleChartControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Cursor = Cursors.Cross;
        InitializeDrawingSystem();
        _scrollModeMessageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _scrollModeMessageTimer.Tick += (_, _) =>
        {
            _scrollModeMessageTimer.Stop();
            _scrollModeMessage = string.Empty;
            InvalidateVisual();
        };
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) =>
        {
            if (Settings.ShowCandleCountdown)
                InvalidateVisual();
        };
        _countdownTimer.Start();
    }

    private static void Freeze(
        Freezable value)
    {
        if (value.CanFreeze)
            value.Freeze();
    }

    public event EventHandler? OlderHistoryRequested;
    public event EventHandler? NewerHistoryRequested;
    public event EventHandler? GoToEarliestRequested;
    public event EventHandler? GoToLatestRequested;
    public event Action<CandleMarker>? MarkerRemoveRequested;
    public event Action<long>? HistoricalNavigationAnchorRemoveRequested;
    public event Action? FindCandleSelectionRemoveRequested;
    public event Action<ChartScrollWheelMode>? ScrollWheelModeChanged;
    public event Action<ChartViewportSnapshot>? ViewportChanged;
    public event Action<ChartVerticalSyncAction>? VerticalSyncAction;
    public event Action<ChartCrosshairSnapshot?>? CrosshairChanged;
    public event Action? RefreshRequested;
    public event Action<CandleMarker>? InteractiveMarkerMoved;
    public event Action<CandleMarker>? InteractiveMarkerPlacementCompleted;
    public event Action<CandleMarker>? InteractiveMarkerRemoveRequested;

    public bool CanRequestOlderHistory
    {
        get => _canRequestOlderHistory;
        set
        {
            if (_canRequestOlderHistory == value)
                return;
            _canRequestOlderHistory = value;
            if (!value)
                _olderHistoryRequestPending = false;
            InvalidateVisual();
        }
    }

    public bool CanRequestNewerHistory
    {
        get => _canRequestNewerHistory;
        set
        {
            if (_canRequestNewerHistory == value)
                return;
            _canRequestNewerHistory = value;
            if (!value)
                _newerHistoryRequestPending = false;
            InvalidateVisual();
        }
    }

    public int VisibleCount => _visibleCount;
    public int RightOffset => _rightOffset;

    public ChartViewportSnapshot? CaptureViewportSnapshot()
    {
        if (!TryCreateLayout(out ChartLayout layout))
            return null;
        return CreateViewportSnapshot(layout);
    }

    public void SetExternalCrosshairRatio(double? ratio)
    {
        _externalCrosshairRatio = ratio.HasValue
            ? Math.Clamp(ratio.Value, 0.0, 1.0)
            : null;
        InvalidateVisual();
    }

    private ChartViewportSnapshot CreateViewportSnapshot(ChartLayout layout) =>
        new(
            layout.FirstIndex,
            layout.LastExclusive,
            layout.TimelineFirst,
            layout.SlotCount,
            layout.VisibleSlots.ToArray(),
            layout.Plot.Left,
            layout.Plot.Width,
            _visibleCount,
            _rightOffset);

    private void PublishViewportChanged()
    {
        if (TryCreateLayout(out ChartLayout layout))
            ViewportChanged?.Invoke(CreateViewportSnapshot(layout));
    }

    private void PublishCrosshair(Point? mouse)
    {
        if (mouse is null || !TryCreateLayout(out ChartLayout layout) || !layout.Plot.Contains(mouse.Value))
        {
            CrosshairChanged?.Invoke(null);
            return;
        }
        double ratio = Math.Clamp((mouse.Value.X - layout.Plot.Left) / Math.Max(1.0, layout.Plot.Width), 0.0, 1.0);
        int? index = HitTestCandle(layout, mouse.Value.X);
        long? timestamp = index.HasValue ? Candles[index.Value].StartUnix : null;
        CrosshairChanged?.Invoke(new ChartCrosshairSnapshot(ratio, timestamp));
    }

    public bool MarkerSelectionMode
    {
        get => _markerSelectionMode;
        set
        {
            _markerSelectionMode = value;
            if (!value)
            {
                _interactiveMarkerDragging = false;
                _interactiveReplayEndDragging = false;
                _interactiveMarkerPlacementPending = false;
            }
            InvalidateVisual();
        }
    }

    public CandleMarker? InteractiveSelectionMarker
    {
        get => _interactiveSelectionMarker;
        set
        {
            _interactiveSelectionMarker = value;
            if (value is null)
            {
                _interactiveMarkerDragging = false;
                _interactiveMarkerPlacementPending = false;
                _interactiveReplayEndMarker = null;
                _interactiveReplayEndDragging = false;
            }
            else if (!value.Source.StartsWith("TickLabReplay", StringComparison.OrdinalIgnoreCase))
            {
                _interactiveReplayEndMarker = null;
                _interactiveReplayEndDragging = false;
            }
            InvalidateVisual();
        }
    }

    public CandleMarker? InteractiveReplayEndMarker
    {
        get => _interactiveReplayEndMarker;
        set
        {
            _interactiveReplayEndMarker = value;
            if (value is null)
                _interactiveReplayEndDragging = false;
            InvalidateVisual();
        }
    }

    public void BeginInteractiveMarkerPlacement(CandleMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        _interactiveSelectionMarker = marker;
        _markerSelectionMode = true;
        _interactiveMarkerDragging = false;
        _interactiveMarkerPlacementPending = true;
        InvalidateVisual();
    }

    public IReadOnlyList<AlertLineOverlay> AlertLines
    {
        get => _alertLines;
        set
        {
            _alertLines = (value ?? Array.Empty<AlertLineOverlay>())
                .Where(item => item.Enabled && double.IsFinite(item.Price))
                .OrderBy(item => item.Price)
                .ToArray();
            if (_draggingAlertLineId is not null && !_alertLines.Any(item => item.AlertId == _draggingAlertLineId))
            {
                _draggingAlertLineId = null;
                _draggingAlertPrice = null;
            }
            InvalidateVisual();
        }
    }

    public OrderFlowProfileSnapshot OrderFlowProfile
    {
        get => _orderFlowProfile;
        set
        {
            _orderFlowProfile = value ?? OrderFlowProfileSnapshot.Empty;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<CandleMarker> Markers
    {
        get => _markers;
        set
        {
            _markers = (value ?? Array.Empty<CandleMarker>())
                .OrderBy(item => item.StartUnix)
                .ToArray();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Exact date/time established by Find Candle. This is intentionally
    /// independent of the timeframe used to create the marker: changing from
    /// M1 to 1s/M5/H1 keeps this market-time pointer visible. The line itself
    /// is rendered in the below-candle layer so it can never cover candle bodies.
    /// </summary>
    public long? HistoricalNavigationAnchorUnix
    {
        get => _historicalNavigationAnchorUnix;
        set
        {
            if (_historicalNavigationAnchorUnix == value)
                return;
            _historicalNavigationAnchorUnix = value;
            _historicalNavigationAnchorSelected = false;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<ChartTimelineGap> TimelineGaps
    {
        get => _timelineGaps;
        set
        {
            _timelineGaps = (value ?? Array.Empty<ChartTimelineGap>())
                .Where(item => item.EndUnix > item.StartUnix && item.SlotCount > 0)
                .OrderBy(item => item.StartUnix)
                .ToArray();
            ClampViewport();
            InvalidateVisual();
        }
    }

    public void CompleteOlderHistoryRequest()
    {
        _olderHistoryRequestPending = false;
    }

    public void CompleteNewerHistoryRequest()
    {
        _newerHistoryRequestPending = false;
    }

    public IReadOnlyList<Candle> Candles
    {
        get => _candles;
        set => SetCandles(
            value,
            resetForContextChange: true,
            appendedCount: 0);
    }

    public ChartViewportState CaptureViewport() =>
        new(
            _visibleCount,
            _rightOffset,
            _verticalAuto,
            _manualMinimum,
            _manualMaximum);

    public void RestoreViewport(ChartViewportState? state)
    {
        if (state is null || _candles.Count == 0)
            return;

        _visibleCount = Math.Clamp(
            state.VisibleCount,
            GetMinimumVisibleCount(),
            GetMaximumVisibleCount());
        _rightOffset = state.RightOffset;
        _verticalAuto = state.VerticalAuto;

        if (!state.VerticalAuto &&
            double.IsFinite(state.ManualMinimum) &&
            double.IsFinite(state.ManualMaximum) &&
            state.ManualMaximum > state.ManualMinimum)
        {
            _manualMinimum = state.ManualMinimum;
            _manualMaximum = state.ManualMaximum;
        }

        ClampViewport();
        UpdateLatestCandleAnchorFromViewport();
        InvalidateVisual();
    }

    public void ReplaceDataKeepingViewport(
        IReadOnlyList<Candle> candles,
        int appendedCount = 0)
    {
        SetCandles(
            candles,
            resetForContextChange: false,
            appendedCount: appendedCount);
    }

    public ChartWindowAnchor? CaptureWindowAnchor()
    {
        if (!TryCreateLayout(out ChartLayout layout) || Candles.Count == 0)
            return null;

        int anchorIndex;
        if (layout.Count > 0)
        {
            anchorIndex = layout.FirstIndex + layout.Count / 2;
        }
        else
        {
            anchorIndex = Math.Clamp(layout.FirstIndex, 0, Candles.Count - 1);
        }

        int anchorSlot = GetCandleTimelineSlot(anchorIndex);
        int visibleSlot = anchorSlot - layout.TimelineFirst;
        return new ChartWindowAnchor(
            Candles[anchorIndex].StartUnix,
            visibleSlot,
            _visibleCount,
            _verticalAuto,
            _manualMinimum,
            _manualMaximum);
    }

    public ChartWindowAnchor? CaptureWindowAnchorAtOrBefore(
        long startUnix,
        bool excludeExact = false)
    {
        if (!TryCreateLayout(out ChartLayout layout) || Candles.Count == 0)
            return null;

        int low = 0;
        int high = Candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Candles[middle].StartUnix <= startUnix)
                low = middle + 1;
            else
                high = middle;
        }

        int anchorIndex = low - 1;
        if (excludeExact &&
            anchorIndex >= 0 &&
            Candles[anchorIndex].StartUnix == startUnix)
        {
            anchorIndex--;
        }

        if (anchorIndex < 0)
            anchorIndex = 0;

        int anchorSlot = GetCandleTimelineSlot(anchorIndex);
        int visibleSlot = anchorSlot - layout.TimelineFirst;
        return new ChartWindowAnchor(
            Candles[anchorIndex].StartUnix,
            visibleSlot,
            _visibleCount,
            _verticalAuto,
            _manualMinimum,
            _manualMaximum);
    }


    public void ReplaceDataPreservingAnchor(
        IReadOnlyList<Candle> candles,
        ChartWindowAnchor? anchor)
    {
        _sourceCandles = candles ?? Array.Empty<Candle>();
        RebuildVisualCandles();
        RefreshSelectionReference();

        if (_candles.Count == 0)
        {
            ClampViewport();
            InvalidateVisual();
            return;
        }

        string newSymbol = _candles[0].Symbol;
        string newTimeframe = _candles[0].Timeframe;
        bool contextChanged =
            !_viewportInitialized ||
            !string.Equals(_viewportSymbol, newSymbol, StringComparison.Ordinal) ||
            !string.Equals(_viewportTimeframe, newTimeframe, StringComparison.Ordinal);

        _viewportInitialized = true;
        _viewportSymbol = newSymbol;
        _viewportTimeframe = newTimeframe;

        if (contextChanged || anchor is null)
        {
            ResetToDefaultView(invalidate: false);
            InvalidateVisual();
            return;
        }

        _visibleCount = Math.Clamp(
            anchor.VisibleCount,
            GetMinimumVisibleCount(),
            GetMaximumVisibleCount());

        int anchorIndex = FindCandleIndex(anchor.StartUnix);
        if (anchorIndex >= 0)
        {
            int anchorTimelineSlot = GetCandleTimelineSlot(anchorIndex);
            int timelineFirst = anchorTimelineSlot - anchor.VisibleSlot;
            int timelineLastExclusive = timelineFirst + _visibleCount;
            _rightOffset = GetTotalTimelineSlots() - timelineLastExclusive;
        }

        _verticalAuto = anchor.VerticalAuto;
        if (!anchor.VerticalAuto &&
            double.IsFinite(anchor.ManualMinimum) &&
            double.IsFinite(anchor.ManualMaximum) &&
            anchor.ManualMaximum > anchor.ManualMinimum)
        {
            _manualMinimum = anchor.ManualMinimum;
            _manualMaximum = anchor.ManualMaximum;
        }

        ClampViewport();
        UpdateLatestCandleAnchorFromViewport();
        InvalidateVisual();
    }

    public void RefreshData(
        int appendedCount = 0)
    {
        bool wasFollowingLatest =
            IsHorizontallyAtLatest();
        int previousVisualCount = _candles.Count;

        if (SyntheticChartBuilder.IsSynthetic(_settings.ChartType))
            RebuildVisualCandles();

        int effectiveAppendedCount = SyntheticChartBuilder.IsSynthetic(_settings.ChartType)
            ? Math.Max(0, _candles.Count - previousVisualCount)
            : appendedCount;

        if (effectiveAppendedCount > 0 &&
            !wasFollowingLatest)
        {
            _rightOffset +=
                effectiveAppendedCount;
        }

        RefreshSelectionReference();
        ClampViewport();
        PublishViewportChanged();
        InvalidateVisual();
    }

    private void SetCandles(
        IReadOnlyList<Candle>? candles,
        bool resetForContextChange,
        int appendedCount)
    {
        bool wasFollowingLatest =
            IsHorizontallyAtLatest();
        int previousVisualCount = _candles.Count;

        _sourceCandles =
            candles ??
            Array.Empty<Candle>();
        RebuildVisualCandles();
        int effectiveAppendedCount = SyntheticChartBuilder.IsSynthetic(_settings.ChartType)
            ? Math.Max(0, _candles.Count - previousVisualCount)
            : appendedCount;

        if (resetForContextChange)
        {
            _olderHistoryRequestPending = false;
            _newerHistoryRequestPending = false;
        }

        RefreshSelectionReference();

        string newSymbol =
            _candles.Count > 0
                ? _candles[0].Symbol
                : string.Empty;

        string newTimeframe =
            _candles.Count > 0
                ? _candles[0].Timeframe
                : string.Empty;

        bool chartContextChanged =
            !_viewportInitialized ||
            !string.Equals(
                _viewportSymbol,
                newSymbol,
                StringComparison.Ordinal) ||
            !string.Equals(
                _viewportTimeframe,
                newTimeframe,
                StringComparison.Ordinal);

        if (_candles.Count > 0 &&
            chartContextChanged &&
            resetForContextChange)
        {
            _viewportInitialized = true;
            _viewportSymbol = newSymbol;
            _viewportTimeframe = newTimeframe;
            ResetToDefaultView(
                invalidate: false);
        }
        else
        {
            if (_candles.Count > 0 &&
                chartContextChanged)
            {
                _viewportInitialized = true;
                _viewportSymbol = newSymbol;
                _viewportTimeframe = newTimeframe;
                ResetToDefaultView(
                    invalidate: false);
            }
            else if (effectiveAppendedCount > 0 &&
                     !wasFollowingLatest)
            {
                _rightOffset +=
                    effectiveAppendedCount;
            }

            ClampViewport();
        }

        PublishViewportChanged();
        InvalidateVisual();
    }

    private void RefreshSelectionReference()
    {
        if (_selectedCandle is null ||
            _candles.Count == 0)
        {
            if (_candles.Count == 0)
                _selectedCandle = null;

            return;
        }

        int index =
            FindCandleIndex(
                _selectedCandle.StartUnix);

        if (index >= 0)
        {
            Candle candidate =
                _candles[index];

            _selectedCandle =
                string.Equals(
                    candidate.Symbol,
                    _selectedCandle.Symbol,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidate.Timeframe,
                    _selectedCandle.Timeframe,
                    StringComparison.Ordinal)
                    ? candidate
                    : null;
        }
        else
        {
            _selectedCandle = null;
        }
    }

    private int FindCandleIndex(
        long startUnix)
    {
        int low = 0;
        int high = _candles.Count - 1;

        while (low <= high)
        {
            int middle =
                low +
                (high - low) /
                2;

            long value =
                _candles[middle].StartUnix;

            if (value == startUnix)
                return middle;

            if (value < startUnix)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return -1;
    }

    public long? NativeHistoryBoundaryUnix { get; set; }

    public string HistoryBoundaryLabel { get; set; } =
        "Native MT5 history begins here";

    public ChartSettings Settings
    {
        get => _settings;
        set
        {
            ChartSettings next = value ?? ChartSettings.Default;
            bool rebuild = next.ChartType != _settings.ChartType ||
                           next.SyntheticBoxSizePoints != _settings.SyntheticBoxSizePoints ||
                           next.RangeBarSizePoints != _settings.RangeBarSizePoints ||
                           next.KagiReversalPoints != _settings.KagiReversalPoints ||
                           next.LineBreakCount != _settings.LineBreakCount ||
                           next.PointAndFigureReversalBoxes != _settings.PointAndFigureReversalBoxes ||
                           next.RenkoReversalBoxes != _settings.RenkoReversalBoxes ||
                           next.TpoBracketMinutes != _settings.TpoBracketMinutes ||
                           next.MarketProfileRows != _settings.MarketProfileRows ||
                           next.ProfileSessionStartHour != _settings.ProfileSessionStartHour ||
                           next.FootprintPriceStepPoints != _settings.FootprintPriceStepPoints ||
                           Math.Abs(next.VolumeProfileValueAreaPercent - _settings.VolumeProfileValueAreaPercent) > 0.0001 ||
                           next.ShowVolumeProfileValueArea != _settings.ShowVolumeProfileValueArea ||
                           next.ShowFootprintDelta != _settings.ShowFootprintDelta;
            _settings = next;
            if (rebuild)
            {
                ChartWindowAnchor? anchor = CaptureWindowAnchor();
                RebuildVisualCandles();
                if (anchor is not null && _candles.Count > 0)
                    ReplaceVisualViewportFromAnchor(anchor);
                else
                    ClampViewport();
                RefreshSelectionReference();
                PublishViewportChanged();
            }
            InvalidateVisual();
        }
    }

    private void RebuildVisualCandles()
    {
        _candles = SyntheticChartBuilder.Build(_sourceCandles, _settings.ChartType, _settings);
    }

    private void ReplaceVisualViewportFromAnchor(ChartWindowAnchor anchor)
    {
        _visibleCount = Math.Clamp(anchor.VisibleCount, GetMinimumVisibleCount(), GetMaximumVisibleCount());
        int anchorIndex = FindNearestCandleIndex(anchor.StartUnix);
        if (anchorIndex >= 0)
        {
            int anchorTimelineSlot = GetCandleTimelineSlot(anchorIndex);
            int timelineFirst = anchorTimelineSlot - anchor.VisibleSlot;
            _rightOffset = GetTotalTimelineSlots() - (timelineFirst + _visibleCount);
        }
        _verticalAuto = anchor.VerticalAuto;
        if (!anchor.VerticalAuto && double.IsFinite(anchor.ManualMinimum) &&
            double.IsFinite(anchor.ManualMaximum) && anchor.ManualMaximum > anchor.ManualMinimum)
        {
            _manualMinimum = anchor.ManualMinimum;
            _manualMaximum = anchor.ManualMaximum;
        }
        ClampViewport();
    }

    private int FindNearestCandleIndex(long startUnix)
    {
        if (_candles.Count == 0)
            return -1;
        int exact = FindCandleIndex(startUnix);
        if (exact >= 0)
            return exact;
        int low = 0;
        int high = _candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_candles[middle].StartUnix < startUnix)
                low = middle + 1;
            else
                high = middle;
        }
        if (low <= 0)
            return 0;
        if (low >= _candles.Count)
            return _candles.Count - 1;
        long leftDistance = Math.Abs(_candles[low - 1].StartUnix - startUnix);
        long rightDistance = Math.Abs(_candles[low].StartUnix - startUnix);
        return leftDistance <= rightDistance ? low - 1 : low;
    }

    public Candle? SelectedCandle =>
        _selectedCandle;

    public int ServerUtcOffsetMinutes { get; set; }

    public event EventHandler<CandleSelectedEventArgs>?
        CandleSelected;
    public event Action? CandleUnmarked;
    public event Action? ChartSettingsRequested;
    public event Action? SaveChartTemplateRequested;
    public event Action? LoadChartTemplateRequested;
    public event Action? DeleteChartTemplateRequested;
    public event Action<double>? PriceAlertRequested;
    public event Action<string, double>? AlertLineMoved;
    public event Action<string>? AlertLineEditRequested;
    public event Action<string>? AlertLineRemoveRequested;
    public Func<IReadOnlyList<System.Windows.Controls.MenuItem>>? HostContextMenuItemsProvider { get; set; }
    public Func<IReadOnlyList<ChartIndicatorMenuEntry>>? IndicatorMenuItemsProvider { get; set; }
    public event Action? IndicatorManagerRequested;
    public event Action? IndicatorAddRequested;
    public event Action<string>? IndicatorEditRequested;
    public event Action<string>? IndicatorRefreshRequested;
    public event Action<string>? IndicatorMoveToWindowRequested;
    public event Action<string>? IndicatorMoveToChartRequested;
    public event Action<string>? IndicatorRemoveRequested;

    public void ClearSelection()
    {
        _selectedCandle = null;
        InvalidateVisual();
    }

    public bool RestoreSelection(
        string symbol,
        string timeframe,
        long startUnix)
    {
        int candleIndex =
            FindCandleIndex(startUnix);

        if (candleIndex < 0)
            return false;

        Candle candle =
            Candles[candleIndex];

        if (!string.Equals(
                candle.Symbol,
                symbol,
                StringComparison.Ordinal) ||
            !string.Equals(
                candle.Timeframe,
                timeframe,
                StringComparison.Ordinal))
        {
            return false;
        }

        _selectedCandle = candle;

        // Restore only the selection highlight. The main chart remains
        // at its default latest position with future space on startup.
        InvalidateVisual();
        return true;
    }

    public bool OneShotZoomInMode => _oneShotZoomInMode;

    public ChartScrollWheelMode ScrollWheelMode => Settings.ScrollWheelMode;

    public void ToggleScrollWheelMode()
    {
        ChartScrollWheelMode nextMode = Settings.ScrollWheelMode == ChartScrollWheelMode.Zoom
            ? ChartScrollWheelMode.Scroll
            : ChartScrollWheelMode.Zoom;
        _settings = Settings with { ScrollWheelMode = nextMode };
        ShowChartScrollWheelModeMessage(nextMode);
        ScrollWheelModeChanged?.Invoke(nextMode);
        InvalidateVisual();
    }

    public void ArmOneShotZoomIn()
    {
        _oneShotZoomInMode = true;
        Cursor = Cursors.Cross;
    }

    public void CancelOneShotZoomIn()
    {
        if (!_oneShotZoomInMode)
            return;
        _oneShotZoomInMode = false;
        Cursor = Cursors.Arrow;
    }

    public void ZoomIn() =>
        ZoomBoth(0.82);

    public void ZoomOut() =>
        ZoomBoth(1.22);

    public void ZoomBoth(
        double factor,
        double horizontalAnchor = 0.5,
        double verticalAnchor = 0.5)
    {
        // Apply both axes as one transaction. Earlier builds published and
        // redrew after horizontal zoom and then again after vertical zoom.
        // At the 1,500-bar horizontal limit that produced a redundant
        // horizontal viewport notification before every vertical-only wheel
        // step, allowing linked panes and persistence callbacks to redraw an
        // intermediate candle range. One combined publish keeps X geometry
        // completely frozen while only the price scale changes.
        bool horizontalChanged = ApplyHorizontalZoomCore(
            factor,
            horizontalAnchor);
        bool verticalChanged = ApplyVerticalZoomCore(
            factor,
            verticalAnchor);

        if (!horizontalChanged && !verticalChanged)
            return;

        if (verticalChanged)
            VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Zoom(factor, verticalAnchor));
        PublishViewportChanged();
        InvalidateVisual();
    }

    public void ZoomHorizontal(
        double factor,
        double anchorRatio = 0.5)
    {
        if (!ApplyHorizontalZoomCore(factor, anchorRatio))
            return;

        PublishViewportChanged();
        InvalidateVisual();
    }

    private bool ApplyHorizontalZoomCore(
        double factor,
        double anchorRatio)
    {
        if (Candles.Count == 0)
            return false;

        if (!TryCreateLayout(
                out ChartLayout layout))
        {
            return false;
        }

        _ = anchorRatio; // Kept for public API and linked-pane compatibility.

        int oldCount =
            layout.SlotCount;

        int newCount =
            Math.Clamp(
                (int)Math.Round(
                    oldCount *
                    factor),
                GetMinimumVisibleCount(),
                GetMaximumVisibleCount());

        newCount = QuantizeDetailedVisibleCount(newCount);

        // Once horizontal zoom reaches either safety limit, vertical-only
        // wheel movement must not recalculate, clamp, publish, or redraw the
        // horizontal viewport at all. This is the exact max-zoom-out glitch
        // path reported by the user.
        if (newCount == oldCount)
            return false;

        // Preserve the actual newest-candle screen position, not the
        // invisible end of the future-space region. Keeping a fixed negative
        // right offset caused all candles to disappear once the visible count
        // became smaller than the future-space count. Scaling the future slots
        // with the zoom count keeps the newest candle stable and guarantees
        // that at least one real candle remains visible.
        double latestAnchorRatio = ResolveLatestCandleAnchorRatio(
            oldCount,
            _rightOffset);

        int zoomedRightOffset = CalculateZoomedRightOffset(
            newCount,
            _rightOffset,
            latestAnchorRatio);

        int oldRightOffset = _rightOffset;
        _visibleCount = newCount;
        _rightOffset = zoomedRightOffset;

        ClampViewport();
        return _visibleCount != oldCount || _rightOffset != oldRightOffset;
    }

    public void ApplyLinkedHorizontalWheel(int delta, double anchorRatio = 0.5)
    {
        if (delta == 0 || Candles.Count == 0)
            return;

        if (Settings.ScrollWheelMode == ChartScrollWheelMode.Scroll)
        {
            int oldRightOffset = _rightOffset;
            int candleShift = Math.Max(1, (int)Math.Round(_visibleCount * 0.12));
            _rightOffset += delta > 0 ? -candleShift : candleShift;
            ClampViewport();
            UpdateLatestCandleAnchorFromViewport();
            PublishViewportChanged();
            InvalidateVisual();

            if (_rightOffset != oldRightOffset)
            {
                RequestHistoryIfNearBoundary(
                    _rightOffset > oldRightOffset
                        ? HistoryBoundaryDirection.Older
                        : HistoryBoundaryDirection.Newer);
            }
            return;
        }

        int oldVisibleCount = _visibleCount;
        double factor = delta > 0 ? 1.0 / WheelVisualZoomStep : WheelVisualZoomStep;
        ZoomHorizontal(factor, anchorRatio);
        if (_visibleCount > oldVisibleCount)
            RequestHistoryIfNearBoundary(HistoryBoundaryDirection.Older);
    }

    public void PanHorizontalBySlots(int slotDelta)
    {
        if (slotDelta == 0 || Candles.Count == 0)
            return;

        int oldRightOffset = _rightOffset;
        _rightOffset += slotDelta;
        ClampViewport();
        UpdateLatestCandleAnchorFromViewport();
        PublishViewportChanged();
        InvalidateVisual();

        if (_rightOffset != oldRightOffset)
        {
            RequestHistoryIfNearBoundary(
                _rightOffset > oldRightOffset
                    ? HistoryBoundaryDirection.Older
                    : HistoryBoundaryDirection.Newer);
        }
    }

    public void ZoomVertical(
        double factor,
        double anchorRatio = 0.5)
    {
        if (!ApplyVerticalZoomCore(factor, anchorRatio))
            return;

        VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Zoom(factor, anchorRatio));
        PublishViewportChanged();
        InvalidateVisual();
    }

    private bool ApplyVerticalZoomCore(
        double factor,
        double anchorRatio)
    {
        if (!TryCreateLayout(
                out ChartLayout layout))
        {
            return false;
        }

        EnsureManualPriceRange(
            layout);

        anchorRatio =
            Math.Clamp(
                anchorRatio,
                0.0,
                1.0);

        double span =
            Math.Max(
                _manualMaximum -
                _manualMinimum,
                MinimumPriceSpan(
                    layout));

        double newSpan =
            ClampVerticalSpan(
                span *
                factor,
                layout);

        double anchorPrice =
            _manualMaximum -
            anchorRatio *
            span;

        double newMaximum =
            anchorPrice +
            anchorRatio *
            newSpan;
        double newMinimum =
            anchorPrice -
            (1.0 -
             anchorRatio) *
            newSpan;

        if (Math.Abs(newMaximum - _manualMaximum) <= 1e-12 &&
            Math.Abs(newMinimum - _manualMinimum) <= 1e-12 &&
            !_verticalAuto)
        {
            return false;
        }

        _manualMaximum = newMaximum;
        _manualMinimum = newMinimum;
        _verticalAuto = false;
        return true;
    }

    public void FitHorizontal()
    {
        _horizontalReferenceCount =
            GetFitVisibleCount();

        _visibleCount =
            _horizontalReferenceCount;

        _rightOffset =
            -GetDefaultFutureSpace();
        UpdateLatestCandleAnchorFromViewport();

        ClampViewport();
        PublishViewportChanged();
        InvalidateVisual();
    }

    public void FitVertical()
    {
        _verticalReferenceSpan = 0;
        _verticalAuto = true;
        VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Reset());
        PublishViewportChanged();
        InvalidateVisual();
    }

    public void FitAll()
    {
        FitHorizontal();
        FitVertical();
    }

    public void GoLive()
    {
        ResetToDefaultView(
            invalidate: true);
    }

    public void GoToEarliest()
    {
        if (Candles.Count == 0)
            return;

        _rightOffset = Math.Max(0, GetTotalTimelineSlots() - _visibleCount);
        UpdateLatestCandleAnchorFromViewport();
        _verticalAuto = true;
        ClampViewport();
        PublishViewportChanged();
        InvalidateVisual();
    }

    public bool GoToTimestamp(long startUnix)
    {
        int index = FindCandleIndex(startUnix);
        if (index < 0)
            return false;

        _selectedCandle = Candles[index];
        BringCandleIntoView(index);
        _verticalAuto = true;
        PublishViewportChanged();
        InvalidateVisual();
        return true;
    }

    public void ResetToLaunchView()
    {
        // Reset only viewport/rendering state. Candles, drawings, selections,
        // indicators and external tools remain attached.
        ResetToDefaultView(invalidate: true);
    }

    private void ResetToDefaultView(
        bool invalidate)
    {
        if (Candles.Count == 0)
        {
            _horizontalReferenceCount =
                GetFitVisibleCount();
            _visibleCount =
                _horizontalReferenceCount;
            _rightOffset =
                -DefaultFutureFrames;
            UpdateLatestCandleAnchorFromViewport();
            _verticalAuto = true;
            _verticalReferenceSpan = 0;
            _manualMinimum = 0;
            _manualMaximum = 0;

            if (invalidate)
                InvalidateVisual();

            return;
        }

        _horizontalReferenceCount =
            GetFitVisibleCount();

        _visibleCount =
            _horizontalReferenceCount;

        _rightOffset =
            -GetDefaultFutureSpace();
        UpdateLatestCandleAnchorFromViewport();

        _verticalAuto = true;
        _verticalReferenceSpan = 0;
        _manualMinimum = 0;
        _manualMaximum = 0;

        ClampViewport();

        if (invalidate)
            InvalidateVisual();
    }

    private int GetDefaultFutureSpace() =>
        GetDefaultFutureSpace(_visibleCount);

    private static int GetDefaultFutureSpace(int visibleCount)
    {
        return Math.Min(
            DefaultFutureFrames,
            Math.Max(
                0,
                visibleCount -
                1));
    }

    private double ResolveLatestCandleAnchorRatio(
        int visibleCount,
        int rightOffset)
    {
        if (rightOffset > 0)
            return double.NaN;

        if (double.IsFinite(_latestCandleAnchorRatio))
            return _latestCandleAnchorRatio;

        return CalculateLatestCandleAnchorRatio(
            visibleCount,
            rightOffset);
    }

    private static double CalculateLatestCandleAnchorRatio(
        int visibleCount,
        int rightOffset)
    {
        visibleCount = Math.Max(1, visibleCount);
        int futureSlots = Math.Max(0, -rightOffset);

        return 1.0 -
            (futureSlots + 0.5) /
            visibleCount;
    }

    private static int CalculateZoomedRightOffset(
        int newVisibleCount,
        int currentRightOffset,
        double latestCandleAnchorRatio)
    {
        if (currentRightOffset > 0 ||
            !double.IsFinite(latestCandleAnchorRatio))
        {
            return currentRightOffset;
        }

        newVisibleCount = Math.Max(1, newVisibleCount);

        int newFutureSlots = (int)Math.Round(
            newVisibleCount *
            (1.0 - latestCandleAnchorRatio) -
            0.5,
            MidpointRounding.AwayFromZero);

        newFutureSlots = Math.Clamp(
            newFutureSlots,
            0,
            MaximumFutureDragFrames);

        return -newFutureSlots;
    }

    private void UpdateLatestCandleAnchorFromViewport()
    {
        _latestCandleAnchorRatio = _rightOffset <= 0
            ? CalculateLatestCandleAnchorRatio(
                _visibleCount,
                _rightOffset)
            : double.NaN;
    }

    private bool IsHorizontallyAtLatest()
    {
        if (Candles.Count == 0)
            return true;

        // Any viewport that includes the newest candle and only future space
        // to its right is still following live data. Exact equality with the
        // default ten-slot gap broke after zooming and made new candles shift
        // the chart as if the user had scrolled into history.
        return _rightOffset <= 0;
    }

    private bool IsDefaultView()
    {
        if (Candles.Count == 0)
            return true;
        if (CanRequestNewerHistory)
            return false;

        return
            _visibleCount ==
                _horizontalReferenceCount &&
            _rightOffset ==
                -GetDefaultFutureSpace() &&
            _verticalAuto;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (HandleDrawingKeyDown(e))
            return;

        if (_rawTickDrawingSurface && _rawTickNavigationTarget is not null)
        {
            if (_rawTickNavigationTarget.HandleExternalKeyDown(e))
                return;
            return;
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) &&
            _markerSelectionMode &&
            _interactiveSelectionMarker is not null &&
            !IsReplayInteractiveMarker(_interactiveSelectionMarker))
        {
            FindCandleSelectionRemoveRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) &&
            _historicalNavigationAnchorSelected &&
            _historicalNavigationAnchorUnix.HasValue)
        {
            long anchorUnix = _historicalNavigationAnchorUnix.Value;
            _historicalNavigationAnchorSelected = false;
            HistoricalNavigationAnchorRemoveRequested?.Invoke(anchorUnix);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Home)
        {
            PushViewportUndoSnapshot();
            GoToEarliestRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End)
        {
            PushViewportUndoSnapshot();
            if (CanRequestNewerHistory)
                GoToLatestRequested?.Invoke(this, EventArgs.Empty);
            else
                GoLive();
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (HandleDrawingTextInput(e.Text))
            e.Handled = true;
    }

    protected override void OnRender(
        DrawingContext drawingContext)
    {
        base.OnRender(
            drawingContext);

        if (_rawTickDrawingSurface)
        {
            // Keep the established drawing surface hit-testable across the whole
            // Tick chart without painting over the raw tick renderer underneath.
            drawingContext.DrawRectangle(Brushes.Transparent, null,
                new Rect(0, 0, ActualWidth, ActualHeight));
            DrawRawTickSharedDrawingSurface(drawingContext);
            return;
        }

        drawingContext.DrawRectangle(
            BrushFrom(Settings.ChartBackgroundColor, Color.FromRgb(7, 16, 27)),
            null,
            new Rect(
                0,
                0,
                ActualWidth,
                ActualHeight));

        if (!TryCreateLayout(
                out ChartLayout layout))
        {
            DrawCenteredMessage(
                drawingContext,
                "Connect to MT5 to load candle history");
            return;
        }

        DrawScaleBackgrounds(drawingContext, layout);

        if (Settings.ShowCandleGrid)
            DrawGrid(
                drawingContext,
                layout);

        // Candles can be intentionally moved outside the visible price
        // range while the user zooms. Clip all bodies and wicks to the
        // plot rectangle so they never cover the toolbar or time scale.
        drawingContext.PushClip(
            new RectangleGeometry(
                layout.Plot));

        // Find/marker vertical strokes are permanent background guides. Paint
        // them before every drawing layer and before candles so the marker is a
        // true chart-background reference and never covers chart content.
        DrawMarkerLines(drawingContext, layout);
        DrawHistoricalNavigationAnchorLine(drawingContext, layout);
        DrawDrawingLayer(drawingContext, layout, TickLab.Core.Drawing.DrawingVisualLayer.BelowCandles);
        DrawReplayInteractiveMarkerLines(drawingContext, layout);

        DrawCandles(
            drawingContext,
            layout);
        DrawBuiltInIndicatorOverlays(drawingContext, layout);

        drawingContext.Pop();

        DrawHistoryBoundary(
            drawingContext,
            layout);

        DrawPriceScale(
            drawingContext,
            layout);

        DrawLivePriceLine(
            drawingContext,
            layout);
        DrawAskPriceLine(drawingContext, layout);
        DrawAlertLines(drawingContext, layout);
        DrawDemoTradeLines(drawingContext, layout);
        DrawSpreadLine(drawingContext, layout);
        DrawCandleCountdown(drawingContext, layout);
        DrawTimeScale(
            drawingContext,
            layout);
        DrawSelectedCandleDot(
            drawingContext,
            layout);
        DrawTimelineGapLabels(
            drawingContext,
            layout);
        DrawMarkerLabels(
            drawingContext,
            layout);
        DrawInteractiveSelectionMarker(drawingContext, layout);

        // Drawing objects live strictly inside the candle plot. The price scale and
        // time scale are UI surfaces, not drawing canvas: lines, fills, labels,
        // selection handles, emojis, Measure and construction previews are clipped
        // here so no drawing folder can paint across either scale boundary.
        drawingContext.PushClip(new RectangleGeometry(layout.Plot));
        DrawDrawingLayer(drawingContext, layout, TickLab.Core.Drawing.DrawingVisualLayer.AboveCandles);
        DrawDrawingLayer(drawingContext, layout, TickLab.Core.Drawing.DrawingVisualLayer.AboveIndicators);
        DrawMeasurementOverlay(drawingContext, layout);
        // Construction anchors are deliberately rendered last so point #1 is visible
        // immediately after the first click and can never be hidden by candles/fills.
        DrawWorkingDrawingOverlay(drawingContext, layout);
        drawingContext.Pop();

        if (Settings.ShowCandleCrosshair && _dragMode == DragMode.None)
        {
            if (_mousePosition is Point mouse)
            {
                DrawCrosshair(drawingContext, layout, mouse);
            }
            else if (_externalCrosshairRatio.HasValue)
            {
                double x = layout.Plot.Left + layout.Plot.Width * _externalCrosshairRatio.Value;
                DrawCrosshair(drawingContext, layout, new Point(x, layout.Plot.Top + layout.Plot.Height / 2.0));
            }
        }

        DrawCursorModeOverlay(drawingContext, layout);

        DrawLatestButton(
            drawingContext,
            layout);
        DrawChartScrollWheelModeMessage(drawingContext, layout);
    }

    protected override void OnMouseMove(
        MouseEventArgs e)
    {
        base.OnMouseMove(e);

        Point mouse =
            e.GetPosition(this);
        _mousePosition = mouse;

        if (_rawTickDrawingSurface)
        {
            bool drawingHandled = TryCreateLayout(out ChartLayout rawDrawingLayout) &&
                HandleDrawingMouseMove(mouse, e, rawDrawingLayout);
            if (!drawingHandled)
                UpdateRawTickNavigation(mouse, e);
            if (drawingHandled)
                e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (TryCreateLayout(out ChartLayout drawingPriorityLayout) &&
            DrawingPointerInputHasPriority(drawingPriorityLayout, mouse) &&
            HandleDrawingMouseMove(mouse, e, drawingPriorityLayout))
        {
            // An armed/active drawing gesture owns pointer movement until it
            // completes; alerts, demo lines and replay markers cannot interrupt it.
            e.Handled = true;
        }
        else if (_draggingDemoTradeLineId is not null && e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out ChartLayout demoTradeLayout) &&
            UpdateDemoTradeLineDrag(mouse, demoTradeLayout))
        {
            e.Handled = true;
        }
        else if (_draggingAlertLineId is not null && e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out ChartLayout alertLayout))
        {
            _draggingAlertPrice = Math.Clamp(
                YToPrice(mouse.Y, alertLayout),
                alertLayout.MinimumPrice,
                alertLayout.MaximumPrice);
            e.Handled = true;
        }
        else if (_interactiveMarkerDragging && e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out ChartLayout markerLayout))
        {
            MoveInteractiveMarkerTo(mouse.X, markerLayout);
            e.Handled = true;
        }
        else if (_interactiveReplayEndDragging && e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out ChartLayout replayEndLayout))
        {
            MoveInteractiveReplayEndMarkerTo(mouse.X, replayEndLayout);
            e.Handled = true;
        }
        else if (TryCreateLayout(out ChartLayout drawingLayout) &&
                 !DrawingPointerInputHasPriority(drawingLayout, mouse) &&
                 HandleDrawingMouseMove(mouse, e, drawingLayout))
        {
            e.Handled = true;
        }
        else if (_dragMode != DragMode.None &&
            e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out ChartLayout layout))
        {
            // Never start history I/O on every mouse-move frame. The final
            // drag direction is evaluated once on mouse-up, which keeps the
            // chart responsive and prevents opposing prefetch tasks from
            // cancelling one another while the user is still dragging.
            ApplyDrag(mouse, layout);
            e.Handled = true;
        }

        bool hover =
            !IsDefaultView() &&
            GetLatestButtonRect()
                .Contains(mouse);

        if (hover !=
            _liveButtonHovered)
        {
            _liveButtonHovered =
                hover;
        }

        if (TryCreateLayout(out ChartLayout demoHistoryHoverLayout))
            UpdateDemoTradeHistoryHover(demoHistoryHoverLayout, mouse);
        else
            CloseDemoTradeHistoryToolTip();

        UpdateCursor(mouse);
        PublishCrosshair(mouse);
        // Crosshair movement is not a viewport change. Publishing a full
        // viewport snapshot here caused every mouse pixel to refresh linked
        // indicator panes, persistence, and virtual-window logic—often twice
        // during a drag. The active drag path publishes only when its actual
        // viewport values change.
        InvalidateVisual();
    }

    protected override void OnMouseLeave(
        MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_rawTickDrawingSurface)
        {
            _mousePosition = null;
            HandleCursorMouseLeave();
            InvalidateVisual();
            return;
        }

        if (_dragMode ==
            DragMode.None)
        {
            _mousePosition = null;
        }

        _liveButtonHovered = false;
        CloseDemoTradeHistoryToolTip();
        HandleCursorMouseLeave();
        PublishCrosshair(null);
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // Drawing interaction gets first refusal on the wheel button. Running this
        // before base input handling prevents chart/window mouse behavior from
        // consuming the middle click before drawing hit-testing can remove the object.
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (TryCreateLayout(out ChartLayout drawingDeleteLayout))
            {
                Point click = e.GetPosition(this);
                DrawingHitInfo? drawingHit = HitTestDrawing(drawingDeleteLayout, click);
                if (drawingHit is DrawingHitInfo hit && !hit.Drawing.IsLocked && !_lockAllDrawings)
                {
                    RemoveDrawingById(hit.Drawing.Id);
                    e.Handled = true;
                    return;
                }
            }

            // Preserve the existing double-middle-click wheel-mode shortcut only
            // when the pointer is not over a removable drawing.
            if (e.ClickCount >= 2)
            {
                ChartScrollWheelMode nextMode = Settings.ScrollWheelMode == ChartScrollWheelMode.Zoom
                    ? ChartScrollWheelMode.Scroll
                    : ChartScrollWheelMode.Zoom;
                _settings = Settings with { ScrollWheelMode = nextMode };
                ShowChartScrollWheelModeMessage(nextMode);
                ScrollWheelModeChanged?.Invoke(nextMode);
                e.Handled = true;
                return;
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        if (CancelActiveDrawingToolOrMeasurement())
        {
            e.Handled = true;
            return;
        }

        CandleMarker? marker = null;
        CandleMarker? interactiveMarker = null;
        CandleMarker? findSelectionMarker = null;
        long? historicalNavigationAnchor = null;
        bool selectedCandleHit = false;
        double? contextPrice = null;
        Point click = e.GetPosition(this);
        if (!_rawTickDrawingSurface && TryCreateLayout(out ChartLayout layout))
        {
            marker = HitTestMarker(layout, click.X);
            interactiveMarker = HitTestReplayInteractiveMarker(layout, click.X);
            CandleMarker? selectionHit = HitTestInteractiveSelectionMarker(layout, click.X);
            if (selectionHit is not null && !IsReplayInteractiveMarker(selectionHit))
                findSelectionMarker = selectionHit;
            historicalNavigationAnchor = HitTestHistoricalNavigationAnchor(layout, click.X);
            selectedCandleHit = IsSelectedCandleHit(layout, click);
            if (layout.Plot.Contains(click))
                contextPrice = YToPrice(click.Y, layout);
        }

        // Existing drawings get first refusal on a right-click. In previous builds
        // an overlapping alert/demo/replay overlay could steal the click and open
        // the generic chart menu even though the drawing was visibly under the pointer.
        if (TryCreateLayout(out ChartLayout priorityDrawingContextLayout) &&
            HitTestDrawing(priorityDrawingContextLayout, click) is not null &&
            TryOpenDrawingContextMenu(e, priorityDrawingContextLayout, click))
        {
            return;
        }

        if (_rawTickDrawingSurface && HandleRawTickFindMarkerRightClick(click))
        {
            e.Handled = true;
            return;
        }

        if (interactiveMarker is not null &&
            IsReplayInteractiveMarker(interactiveMarker))
        {
            CandleMarker replayLine = interactiveMarker;
            var replayLineMenu = new System.Windows.Controls.ContextMenu();
            var removeReplayLine = new System.Windows.Controls.MenuItem { Header = "Remove replay line" };
            removeReplayLine.Click += (_, _) => InteractiveMarkerRemoveRequested?.Invoke(replayLine);
            replayLineMenu.Items.Add(removeReplayLine);
            ContextMenu = replayLineMenu;
            replayLineMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (findSelectionMarker is not null && marker is null)
        {
            var selectionMenu = new System.Windows.Controls.ContextMenu();
            var removeSelection = new System.Windows.Controls.MenuItem
            {
                Header = "Remove Find Candle selection"
            };
            removeSelection.Click += (_, _) => FindCandleSelectionRemoveRequested?.Invoke();
            selectionMenu.Items.Add(removeSelection);
            ApplyFlatChartContextMenuStyle(selectionMenu);
            ContextMenu = selectionMenu;
            selectionMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (!_rawTickDrawingSurface && TryCreateLayout(out ChartLayout alertLineContextLayout) &&
            HitTestAlertLine(alertLineContextLayout, click) is AlertLineOverlay alertLine)
        {
            var alertMenu = new System.Windows.Controls.ContextMenu();
            var editAlert = new System.Windows.Controls.MenuItem { Header = "Edit alert…" };
            editAlert.Click += (_, _) => AlertLineEditRequested?.Invoke(alertLine.AlertId);
            var removeAlert = new System.Windows.Controls.MenuItem { Header = "Remove alert" };
            removeAlert.Click += (_, _) => AlertLineRemoveRequested?.Invoke(alertLine.AlertId);
            alertMenu.Items.Add(editAlert);
            alertMenu.Items.Add(new System.Windows.Controls.Separator());
            alertMenu.Items.Add(removeAlert);
            ContextMenu = alertMenu;
            alertMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (!_rawTickDrawingSurface && TryCreateLayout(out ChartLayout demoLineContextLayout) &&
            TryOpenDemoTradeLineContextMenu(demoLineContextLayout, click))
        {
            e.Handled = true;
            return;
        }

        if (TryCreateLayout(out ChartLayout drawingContextLayout) &&
            TryOpenDrawingContextMenu(e, drawingContextLayout, click))
        {
            return;
        }

        IReadOnlyList<ChartIndicatorMenuEntry> availableIndicatorEntries =
            IndicatorMenuItemsProvider?.Invoke() ?? Array.Empty<ChartIndicatorMenuEntry>();
        if (!_rawTickDrawingSurface && TryCreateLayout(out ChartLayout indicatorHitLayout) &&
            HitTestBuiltInIndicatorOverlay(indicatorHitLayout, click, availableIndicatorEntries) is ChartIndicatorMenuEntry hitIndicator)
        {
            System.Windows.Controls.ContextMenu indicatorMenu = BuildExactIndicatorContextMenu(hitIndicator);
            ContextMenu = indicatorMenu;
            indicatorMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        _contextMarker = marker;
        var menu = new System.Windows.Controls.ContextMenu();
        var refresh = new System.Windows.Controls.MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        menu.Items.Add(refresh);
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Chart Settings…" };
        settingsItem.Click += (_, _) => ChartSettingsRequested?.Invoke();
        menu.Items.Add(settingsItem);
        var addIndicatorItem = new System.Windows.Controls.MenuItem { Header = "Add Indicator…" };
        addIndicatorItem.Click += (_, _) => IndicatorAddRequested?.Invoke();
        menu.Items.Add(addIndicatorItem);

        var indicatorsMenu = new System.Windows.Controls.MenuItem { Header = "Indicators" };
        var openIndicatorList = new System.Windows.Controls.MenuItem { Header = "Indicator List…" };
        openIndicatorList.Click += (_, _) => IndicatorManagerRequested?.Invoke();
        indicatorsMenu.Items.Add(openIndicatorList);
        IReadOnlyList<ChartIndicatorMenuEntry> indicatorEntries = availableIndicatorEntries;
        if (indicatorEntries.Count > 0)
        {
            indicatorsMenu.Items.Add(new System.Windows.Controls.Separator());
            foreach (ChartIndicatorMenuEntry entry in indicatorEntries)
            {
                var indicatorItem = new System.Windows.Controls.MenuItem { Header = $"{entry.DisplayName} — {entry.Placement}" };
                var refreshIndicator = new System.Windows.Controls.MenuItem { Header = "Refresh" };
                refreshIndicator.Click += (_, _) => IndicatorRefreshRequested?.Invoke(entry.Key);
                var editIndicator = new System.Windows.Controls.MenuItem { Header = "Properties…" };
                editIndicator.Click += (_, _) => IndicatorEditRequested?.Invoke(entry.Key);
                var moveWindow = new System.Windows.Controls.MenuItem { Header = "Move to Window…" };
                moveWindow.Click += (_, _) => IndicatorMoveToWindowRequested?.Invoke(entry.Key);
                var moveChart = new System.Windows.Controls.MenuItem { Header = "Move to Chart…" };
                moveChart.Click += (_, _) => IndicatorMoveToChartRequested?.Invoke(entry.Key);
                var removeIndicator = new System.Windows.Controls.MenuItem { Header = "Remove" };
                removeIndicator.Click += (_, _) => IndicatorRemoveRequested?.Invoke(entry.Key);
                indicatorItem.Items.Add(refreshIndicator);
                indicatorItem.Items.Add(editIndicator);
                indicatorItem.Items.Add(moveWindow);
                indicatorItem.Items.Add(moveChart);
                indicatorItem.Items.Add(new System.Windows.Controls.Separator());
                indicatorItem.Items.Add(removeIndicator);
                indicatorsMenu.Items.Add(indicatorItem);
            }
        }
        else
        {
            indicatorsMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "No indicators on this chart",
                IsEnabled = false
            });
        }
        menu.Items.Add(indicatorsMenu);

        var alertItem = new System.Windows.Controls.MenuItem
        {
            Header = contextPrice.HasValue ? $"Add Alert Here…  {contextPrice.Value:G10}" : "Add Alert Here…",
            IsEnabled = contextPrice.HasValue
        };
        alertItem.Click += (_, _) =>
        {
            if (contextPrice.HasValue)
                PriceAlertRequested?.Invoke(contextPrice.Value);
        };
        menu.Items.Add(alertItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        var saveTemplate = new System.Windows.Controls.MenuItem { Header = "Save Template…" };
        saveTemplate.Click += (_, _) => SaveChartTemplateRequested?.Invoke();
        var loadTemplate = new System.Windows.Controls.MenuItem { Header = "Load Template…" };
        loadTemplate.Click += (_, _) => LoadChartTemplateRequested?.Invoke();
        var deleteTemplate = new System.Windows.Controls.MenuItem { Header = "Delete Template…" };
        deleteTemplate.Click += (_, _) => DeleteChartTemplateRequested?.Invoke();
        menu.Items.Add(saveTemplate);
        menu.Items.Add(loadTemplate);
        menu.Items.Add(deleteTemplate);
        AppendDrawingChartContextMenu(menu);

        IReadOnlyList<System.Windows.Controls.MenuItem> hostItems =
            HostContextMenuItemsProvider?.Invoke() ?? Array.Empty<System.Windows.Controls.MenuItem>();
        if (hostItems.Count > 0)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (System.Windows.Controls.MenuItem hostItem in hostItems)
                menu.Items.Add(hostItem);
        }

        if (selectedCandleHit)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            var unmark = new System.Windows.Controls.MenuItem { Header = "Unmark candle" };
            unmark.Click += (_, _) =>
            {
                ClearSelection();
                CandleUnmarked?.Invoke();
            };
            menu.Items.Add(unmark);
        }

        if (marker is not null)
        {
            menu.Items.Add(new System.Windows.Controls.Separator());
            var remove = new System.Windows.Controls.MenuItem { Header = "Remove marker" };
            remove.Click += (_, _) =>
            {
                CandleMarker? selected = _contextMarker;
                _contextMarker = null;
                if (selected is not null)
                    MarkerRemoveRequested?.Invoke(selected);
            };
            menu.Items.Add(remove);
        }
        else if (historicalNavigationAnchor.HasValue)
        {
            // Manual fallback for the exact-time Find Candle guide. If normal
            // Remove Marker ever leaves this guide behind, it remains a real
            // hit-testable/removable chart object instead of an orphaned line.
            menu.Items.Add(new System.Windows.Controls.Separator());
            long anchorUnix = historicalNavigationAnchor.Value;
            var remove = new System.Windows.Controls.MenuItem { Header = "Remove Find Candle marker" };
            remove.Click += (_, _) =>
                HistoricalNavigationAnchorRemoveRequested?.Invoke(anchorUnix);
            menu.Items.Add(remove);
        }

        ApplyFlatChartContextMenuStyle(menu);
        ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void ApplyFlatChartContextMenuStyle(System.Windows.Controls.ContextMenu menu)
    {
        menu.Padding = new Thickness(0);
        if (System.Windows.Application.Current?.TryFindResource("FlatChartContextMenu") is Style menuStyle)
            menu.Style = menuStyle;
        if (System.Windows.Application.Current?.TryFindResource("FlatChartContextMenuItem") is not Style itemStyle)
            return;

        static void ApplyToItems(System.Windows.Controls.ItemCollection items, Style style)
        {
            foreach (object entry in items)
            {
                if (entry is not System.Windows.Controls.MenuItem item)
                    continue;
                item.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                item.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                item.Style = style;
                if (item.Items.Count > 0)
                    ApplyToItems(item.Items, style);
            }
        }

        ApplyToItems(menu.Items, itemStyle);
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        if (!TryCreateLayout(
                out ChartLayout layout))
        {
            return;
        }

        Point mouse =
            e.GetPosition(this);

        if (_rawTickDrawingSurface)
        {
            if (!TryCreateLayout(out ChartLayout rawLayout))
                return;

            if (_oneShotZoomInMode && rawLayout.Plot.Contains(mouse) && _rawTickNavigationTarget is not null)
            {
                _oneShotZoomInMode = false;
                double horizontalAnchor = Math.Clamp((mouse.X - rawLayout.Plot.Left) / Math.Max(1.0, rawLayout.Plot.Width), 0.0, 1.0);
                double verticalAnchor = Math.Clamp((mouse.Y - rawLayout.Plot.Top) / Math.Max(1.0, rawLayout.Plot.Height), 0.0, 1.0);
                _rawTickNavigationTarget.ZoomBoth(0.82, horizontalAnchor, verticalAnchor);
                DrawingToolDefinition? activeAfterZoom = DrawingToolCatalog.Find(_activeDrawingToolId);
                if (activeAfterZoom is not null)
                    UpdateDrawingCursor(activeAfterZoom);
                e.Handled = true;
                return;
            }

            bool rawDrawingObjectUnderPointer = rawLayout.Plot.Contains(mouse) && HitTestDrawing(rawLayout, mouse) is not null;
            if ((DrawingPointerInputHasPriority(rawLayout, mouse) || rawDrawingObjectUnderPointer) &&
                HandleDrawingMouseLeftDown(e, rawLayout, mouse))
                return;

            if (_rawTickNavigationTarget is not null &&
                _rawTickNavigationTarget.HandleExternalFindMarkerMouseDown(mouse))
            {
                e.Handled = true;
                return;
            }

            if (!DrawingPointerInputHasPriority(rawLayout, mouse) &&
                HandleDrawingMouseLeftDown(e, rawLayout, mouse))
                return;

            if (BeginRawTickNavigation(mouse, e.ClickCount))
                e.Handled = true;
            return;
        }

        // TradingView-style left-rail magnifier: arm the tool first, then the
        // next click inside the plot zooms around the exact pointer location.
        // It is intentionally one-shot so chart navigation immediately returns
        // to the user's active drawing/cursor mode afterwards.
        if (_oneShotZoomInMode && layout.Plot.Contains(mouse))
        {
            _oneShotZoomInMode = false;
            double horizontalAnchor = Math.Clamp((mouse.X - layout.Plot.Left) / Math.Max(1.0, layout.Plot.Width), 0.0, 1.0);
            double verticalAnchor = Math.Clamp((mouse.Y - layout.Plot.Top) / Math.Max(1.0, layout.Plot.Height), 0.0, 1.0);
            ZoomBoth(0.82, horizontalAnchor, verticalAnchor);
            DrawingToolDefinition? activeAfterZoom = DrawingToolCatalog.Find(_activeDrawingToolId);
            if (activeAfterZoom is not null)
                UpdateDrawingCursor(activeAfterZoom);
            e.Handled = true;
            return;
        }

        // Scale reset always has first priority. Selecting the chart must never
        // consume the first click of a price/time-scale double-click.
        if (e.ClickCount >= 2)
        {
            if (IsPriceScale(mouse, layout))
            {
                BeginViewportEditHistory();
                FitVertical();
                CompleteViewportEditHistory();
                e.Handled = true;
                return;
            }

            if (IsTimeScale(mouse, layout))
            {
                BeginViewportEditHistory();
                FitHorizontal();
                CompleteViewportEditHistory();
                e.Handled = true;
                return;
            }
        }

        bool drawingObjectUnderPointer = layout.Plot.Contains(mouse) && HitTestDrawing(layout, mouse) is not null;
        if ((DrawingPointerInputHasPriority(layout, mouse) || drawingObjectUnderPointer) &&
            HandleDrawingMouseLeftDown(e, layout, mouse))
        {
            // Critical parity rule: after selecting a drawing tool, its very first
            // plot click belongs to construction. Other chart overlays are tested
            // only when no drawing tool/gesture currently owns the pointer.
            return;
        }

        long? historicalAnchorHit = layout.Plot.Contains(mouse)
            ? HitTestHistoricalNavigationAnchor(layout, mouse.X)
            : null;
        if (historicalAnchorHit.HasValue &&
            !_markerSelectionMode &&
            !DrawingPointerInputHasPriority(layout, mouse))
        {
            _historicalNavigationAnchorSelected = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_historicalNavigationAnchorSelected)
        {
            _historicalNavigationAnchorSelected = false;
            InvalidateVisual();
        }

        if (BeginDemoTradeLineDrag(layout, mouse))
        {
            e.Handled = true;
            return;
        }

        AlertLineOverlay? alertLine = HitTestAlertLine(layout, mouse);
        if (alertLine is not null)
        {
            _draggingAlertLineId = alertLine.AlertId;
            _draggingAlertPrice = alertLine.Price;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_markerSelectionMode && layout.Plot.Contains(mouse))
        {
            CandleMarker? replayHit = HitTestReplayInteractiveMarker(layout, mouse.X);
            if (replayHit is not null && IsReplayEndMarker(replayHit))
            {
                _interactiveReplayEndDragging = true;
                MoveInteractiveReplayEndMarkerTo(mouse.X, layout);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_interactiveSelectionMarker is not null)
            {
                bool replayLine = IsReplayInteractiveMarker(_interactiveSelectionMarker);
                bool placementPending = replayLine && _interactiveMarkerPlacementPending;
                bool canBeginMarkerDrag = !replayLine ||
                                          placementPending ||
                                          HitTestInteractiveSelectionMarker(layout, mouse.X) is not null;
                if (canBeginMarkerDrag)
                {
                    _interactiveMarkerDragging = true;
                    MoveInteractiveMarkerTo(mouse.X, layout);
                    if (placementPending)
                        _interactiveMarkerPlacementPending = false;
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (!IsDefaultView() &&
            GetLatestButtonRect()
                .Contains(mouse))
        {
            if (CanRequestNewerHistory)
            {
                PushViewportUndoSnapshot();
                GoToLatestRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                BeginViewportEditHistory();
                GoLive();
                CompleteViewportEditHistory();
            }
            e.Handled = true;
            return;
        }

        if (!DrawingPointerInputHasPriority(layout, mouse) &&
            HandleDrawingMouseLeftDown(e, layout, mouse))
            return;

        if (e.ClickCount >= 2)
        {
            if (layout.Plot.Contains(
                    mouse))
            {
                int? candleIndex =
                    HitTestCandle(
                        layout,
                        mouse.X);

                if (candleIndex is null)
                    return;

                _selectedCandle =
                    Candles[
                        candleIndex.Value];

                InvalidateVisual();

                CandleSelected?.Invoke(
                    this,
                    new CandleSelectedEventArgs(
                        _selectedCandle));

                e.Handled = true;
                return;
            }
        }

        _dragMode =
            IsPriceScale(
                mouse,
                layout)
                ? DragMode.PriceScale
                : IsTimeScale(
                    mouse,
                    layout)
                    ? DragMode.TimeScale
                    : layout.Plot.Contains(
                        mouse)
                        ? DragMode.Plot
                        : DragMode.None;

        if (_dragMode ==
            DragMode.None)
        {
            return;
        }

        BeginViewportEditHistory();
        EnsureManualPriceRange(
            layout);

        _dragStart = mouse;
        _dragStartVisibleCount =
            _visibleCount;
        _dragStartRightOffset =
            _rightOffset;
        _dragStartLatestCandleAnchorRatio =
            ResolveLatestCandleAnchorRatio(
                _dragStartVisibleCount,
                _dragStartRightOffset);
        _dragStartMinimum =
            _manualMinimum;
        _dragStartMaximum =
            _manualMaximum;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_rawTickDrawingSurface)
        {
            if (TryCreateLayout(out ChartLayout rawDrawingLayout) &&
                HandleDrawingMouseLeftUp(e, rawDrawingLayout, e.GetPosition(this)))
                return;
            if (EndRawTickNavigation())
                e.Handled = true;
            return;
        }

        if (CompleteDemoTradeLineDrag())
        {
            e.Handled = true;
            return;
        }

        if (_draggingAlertLineId is not null)
        {
            string alertId = _draggingAlertLineId;
            double? price = _draggingAlertPrice;
            _draggingAlertLineId = null;
            _draggingAlertPrice = null;
            ReleaseMouseCapture();
            if (price.HasValue && double.IsFinite(price.Value))
                AlertLineMoved?.Invoke(alertId, price.Value);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_interactiveReplayEndDragging)
        {
            CandleMarker? completedMarker = _interactiveReplayEndMarker;
            _interactiveReplayEndDragging = false;
            ReleaseMouseCapture();
            if (completedMarker is not null)
                InteractiveMarkerPlacementCompleted?.Invoke(completedMarker);
            e.Handled = true;
            return;
        }

        if (_interactiveMarkerDragging)
        {
            CandleMarker? completedMarker = _interactiveSelectionMarker;
            _interactiveMarkerDragging = false;
            ReleaseMouseCapture();
            if (completedMarker is not null)
                InteractiveMarkerPlacementCompleted?.Invoke(completedMarker);
            e.Handled = true;
            return;
        }

        if (TryCreateLayout(out ChartLayout drawingLayout) &&
            HandleDrawingMouseLeftUp(e, drawingLayout, e.GetPosition(this)))
        {
            return;
        }

        if (_dragMode !=
            DragMode.None)
        {
            DragMode completedMode = _dragMode;
            _dragMode =
                DragMode.None;
            ReleaseMouseCapture();
            InvalidateVisual();
            CompleteViewportEditHistory();

            if (completedMode == DragMode.Plot &&
                _rightOffset != _dragStartRightOffset)
            {
                RequestHistoryIfNearBoundary(
                    _rightOffset > _dragStartRightOffset
                        ? HistoryBoundaryDirection.Older
                        : HistoryBoundaryDirection.Newer);
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(
        MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_rawTickDrawingSurface && _rawTickNavigationTarget is not null)
        {
            _rawTickNavigationTarget.HandleExternalMouseWheel(e.Delta, e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (!TryCreateLayout(out ChartLayout layout))
            return;

        BeginViewportEditHistory();

        if (Settings.ScrollWheelMode == ChartScrollWheelMode.Scroll)
        {
            int oldRightOffset = _rightOffset;
            int candleShift = Math.Max(
                1,
                (int)Math.Round(_visibleCount * 0.12));

            if (e.Delta > 0)
            {
                // Forward/right toward newer candles, stopping at the
                // normal latest-chart future space.
                _rightOffset = Math.Max(
                    -DefaultFutureFrames,
                    _rightOffset - candleShift);
            }
            else
            {
                // Backward/left toward older history.
                _rightOffset += candleShift;
            }

            ClampViewport();
            UpdateLatestCandleAnchorFromViewport();
            PublishViewportChanged();
            InvalidateVisual();

            if (_rightOffset != oldRightOffset)
            {
                RequestHistoryIfNearBoundary(
                    _rightOffset > oldRightOffset
                        ? HistoryBoundaryDirection.Older
                        : HistoryBoundaryDirection.Newer);
            }
        }
        else
        {
            // Zoom mode always changes horizontal candle density and
            // vertical price scale together until the horizontal limit is
            // reached. Beyond that limit, the same wheel input is strictly
            // vertical and must never start history prefetch or mutate the
            // horizontal virtual window.
            Point mouse = e.GetPosition(this);
            double verticalAnchor = Math.Clamp(
                (mouse.Y - layout.Plot.Top) / Math.Max(1.0, layout.Plot.Height),
                0.0,
                1.0);
            double factor = e.Delta > 0
                ? 1.0 / WheelVisualZoomStep
                : WheelVisualZoomStep;
            int oldVisibleCount = _visibleCount;

            // Horizontal zoom is intentionally right-edge anchored. The
            // mouse position still anchors vertical price zoom only.
            ZoomBoth(factor, 1.0, verticalAnchor);

            // Zooming out can expose older candles on the left. Request only
            // that direction, and only if the horizontal count really grew.
            // At the 1,500-bar limit this branch is a complete no-op.
            if (_visibleCount > oldVisibleCount)
                RequestHistoryIfNearBoundary(HistoryBoundaryDirection.Older);
        }

        CompleteViewportEditHistory();
        e.Handled = true;
    }

    private void ApplyDrag(
        Point mouse,
        ChartLayout layout)
    {
        int previousVisibleCount = _visibleCount;
        int previousRightOffset = _rightOffset;
        bool previousVerticalAuto = _verticalAuto;
        double previousMinimum = _manualMinimum;
        double previousMaximum = _manualMaximum;

        double dx =
            mouse.X -
            _dragStart.X;
        double dy =
            mouse.Y -
            _dragStart.Y;

        switch (_dragMode)
        {
            case DragMode.Plot:
            {
                // TradingView-style free chart movement:
                // horizontal drag moves through history;
                // vertical drag moves the complete price view;
                // neither direction changes the zoom level.
                int horizontalShift =
                    (int)Math.Round(
                        dx /
                        Math.Max(
                            1.0,
                            layout.Plot.Width) *
                        _dragStartVisibleCount);

                _rightOffset =
                    _dragStartRightOffset +
                    horizontalShift;

                ClampViewport();
                UpdateLatestCandleAnchorFromViewport();

                double priceSpan =
                    _dragStartMaximum -
                    _dragStartMinimum;

                double priceShift =
                    dy /
                    Math.Max(
                        1.0,
                        layout.Plot.Height) *
                    priceSpan;

                _verticalAuto = false;
                _manualMinimum =
                    _dragStartMinimum +
                    priceShift;
                _manualMaximum =
                    _dragStartMaximum +
                    priceShift;
                break;
            }

            case DragMode.PriceScale:
            {
                double span =
                    _dragStartMaximum -
                    _dragStartMinimum;
                double factor =
                    Math.Exp(
                        dy /
                        170.0);
                double newSpan =
                    ClampVerticalSpan(
                        span *
                        factor,
                        layout);
                double center =
                    (_dragStartMinimum +
                     _dragStartMaximum) /
                    2.0;

                _verticalAuto = false;
                _manualMinimum =
                    center -
                    newSpan /
                    2.0;
                _manualMaximum =
                    center +
                    newSpan /
                    2.0;
                break;
            }

            case DragMode.TimeScale:
            {
                double factor =
                    Math.Exp(
                        dx /
                        240.0);

                int newCount =
                    Math.Clamp(
                        (int)Math.Round(
                            _dragStartVisibleCount *
                            factor),
                        GetMinimumVisibleCount(),
                        GetMaximumVisibleCount());

                // Time-scale dragging uses the same newest-candle anchor as
                // wheel zoom. Future space scales with the visible count so it
                // can never consume the complete frame and hide every candle.
                _visibleCount =
                    newCount;
                _rightOffset =
                    CalculateZoomedRightOffset(
                        newCount,
                        _dragStartRightOffset,
                        _dragStartLatestCandleAnchorRatio);

                ClampViewport();
                break;
            }
        }

        bool viewportChanged =
            previousVisibleCount != _visibleCount ||
            previousRightOffset != _rightOffset ||
            previousVerticalAuto != _verticalAuto ||
            Math.Abs(previousMinimum - _manualMinimum) > 1e-12 ||
            Math.Abs(previousMaximum - _manualMaximum) > 1e-12;

        if (!viewportChanged)
            return;

        if (_dragMode == DragMode.Plot)
        {
            double previousSpan = Math.Max(1e-15, previousMaximum - previousMinimum);
            double previousCenter = (previousMinimum + previousMaximum) / 2.0;
            double currentCenter = (_manualMinimum + _manualMaximum) / 2.0;
            double shiftRatio = (currentCenter - previousCenter) / previousSpan;
            if (Math.Abs(shiftRatio) > 1e-12)
                VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Pan(shiftRatio));
        }
        else if (_dragMode == DragMode.PriceScale)
        {
            double previousSpan = Math.Max(1e-15, previousMaximum - previousMinimum);
            double currentSpan = Math.Max(1e-15, _manualMaximum - _manualMinimum);
            double factor = currentSpan / previousSpan;
            if (Math.Abs(factor - 1.0) > 1e-12)
                VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Zoom(factor, 0.5));
        }

        PublishViewportChanged();
        InvalidateVisual();
    }

    private void UpdateCursor(
        Point mouse)
    {
        if (!TryCreateLayout(
                out ChartLayout layout))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (IsDemoTradeLineHovered(layout, mouse) ||
            _draggingAlertLineId is not null || HitTestAlertLine(layout, mouse) is not null)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        if (_markerSelectionMode && HitTestReplayInteractiveMarker(layout, mouse.X) is not null)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        TickLab.Core.Drawing.DrawingToolDefinition? activeDrawingTool =
            TickLab.Core.Drawing.DrawingToolCatalog.Find(_activeDrawingToolId);
        if (activeDrawingTool is not null &&
            activeDrawingTool.Id != "cursor-crosshair" &&
            layout.Plot.Contains(mouse) &&
            _dragMode == DragMode.None)
        {
            UpdateDrawingCursor(activeDrawingTool);
            return;
        }

        if (!IsDefaultView() &&
            GetLatestButtonRect()
                .Contains(mouse))
        {
            Cursor = Cursors.Hand;
        }
        else if (IsPriceScale(
                     mouse,
                     layout))
        {
            Cursor = Cursors.SizeNS;
        }
        else if (IsTimeScale(
                     mouse,
                     layout))
        {
            Cursor = Cursors.SizeWE;
        }
        else if (_dragMode ==
                 DragMode.Plot)
        {
            Cursor = Cursors.SizeAll;
        }
        else
        {
            Cursor = Cursors.Cross;
        }
    }

    private void BringCandleIntoView(
        int candleIndex)
    {
        if (Candles.Count == 0)
            return;

        int totalSlots = GetTotalTimelineSlots();
        int candleSlot = GetCandleTimelineSlot(candleIndex);
        int desiredLastExclusive = Math.Min(
            totalSlots,
            candleSlot + Math.Max(1, _visibleCount / 2) + 1);
        desiredLastExclusive = Math.Max(desiredLastExclusive, candleSlot + 1);
        _rightOffset = totalSlots - desiredLastExclusive;
        ClampViewport();
        UpdateLatestCandleAnchorFromViewport();
    }

    private void RequestHistoryIfNearBoundary(
        HistoryBoundaryDirection direction)
    {
        if (Candles.Count == 0)
            return;

        int totalSlots = GetTotalTimelineSlots();
        int timelineLastExclusive = totalSlots - _rightOffset;
        int timelineFirst = timelineLastExclusive - _visibleCount;

        // The previous multiplier grew to 12,000 slots at maximum zoom-out,
        // which covered the complete virtual candle window. A single wheel
        // action could therefore request older and newer pages together; both
        // async loads then cancelled and replaced one another, producing the
        // sticky scroll and right-edge disappear/reappear behaviour. Keep the
        // prefetch distance bounded to less than one 1,600-record page.
        int threshold = Math.Clamp(
            _visibleCount / 2,
            MinimumBoundaryPrefetchSlots,
            MaximumBoundaryPrefetchSlots);
        int firstLoadedCandleSlot = GetCandleTimelineSlot(0);
        int lastLoadedCandleSlotExclusive = GetCandleTimelineSlot(Candles.Count - 1) + 1;

        if (direction == HistoryBoundaryDirection.Older)
        {
            int olderDistance = timelineFirst - firstLoadedCandleSlot;
            if (CanRequestOlderHistory &&
                !_olderHistoryRequestPending &&
                olderDistance <= threshold)
            {
                _olderHistoryRequestPending = true;
                OlderHistoryRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        int newerDistance = lastLoadedCandleSlotExclusive - timelineLastExclusive;
        if (CanRequestNewerHistory &&
            !_newerHistoryRequestPending &&
            newerDistance <= threshold)
        {
            _newerHistoryRequestPending = true;
            NewerHistoryRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClampViewport()
    {
        if (Candles.Count == 0)
        {
            _horizontalReferenceCount =
                GetFitVisibleCount();
            _visibleCount =
                _horizontalReferenceCount;
            _rightOffset =
                -DefaultFutureFrames;
            UpdateLatestCandleAnchorFromViewport();
            return;
        }

        _visibleCount =
            Math.Clamp(
                _visibleCount,
                GetMinimumVisibleCount(),
                GetMaximumVisibleCount());

        _visibleCount = QuantizeDetailedVisibleCount(_visibleCount);

        // Positive offset moves backward through history.
        // Negative offset creates future space after the live candle.
        // The future side is intentionally almost unrestricted so every
        // candle can be dragged completely beyond the left chart border.
        // Do not make the maximum history offset depend on the current
        // zoom count. A count-dependent maximum forced _rightOffset inward
        // whenever the user zoomed out near the earliest loaded candle,
        // which moved the right boundary and made candles disappear and then
        // return. Permit empty space on the left instead; the right boundary
        // remains stable through every zoom level.
        int maximumHistoryOffset =
            Math.Max(
                0,
                GetTotalTimelineSlots() -
                GetMinimumVisibleCount());

        _rightOffset =
            Math.Clamp(
                _rightOffset,
                -MaximumFutureDragFrames,
                maximumHistoryOffset);
    }

    private int QuantizeDetailedVisibleCount(int requestedCount)
    {
        // Keep the requested visible-bar count. Candle geometry now spans the
        // complete fixed plot width and distributes only unavoidable spare
        // physical pixels between slots. Earlier builds forced one integer
        // pitch across the whole frame, leaving a changing unused strip on
        // the left; that made candles disappear and return during zoom even
        // though the chart wall itself stayed fixed.
        return Math.Clamp(
            requestedCount,
            GetMinimumVisibleCount(),
            GetMaximumVisibleCount());
    }


    private static Rect CreateStablePlotRect(double availableWidth, double height)
    {
        // The plot, price scale, time scale, grid and interaction surface stay
        // fixed. Candle slot rounding is distributed inside this rectangle; no
        // unused strip is allowed to move at either chart edge.
        return new Rect(LeftMargin, TopMargin, availableWidth, height);
    }


    private int GetTotalTimelineSlots()
    {
        if (_rawTickDrawingSurface)
            return RawTickDrawingTicks.Count;
        long total = Candles.Count;
        foreach (ChartTimelineGap gap in _timelineGaps)
            total += gap.SlotCount;
        return (int)Math.Clamp(total, 0, int.MaxValue / 2L);
    }

    private int GetCandleTimelineSlot(int candleIndex)
    {
        if (_rawTickDrawingSurface)
            return Math.Clamp(candleIndex, 0, Math.Max(0, RawTickDrawingTicks.Count - 1));
        candleIndex = Math.Clamp(candleIndex, 0, Math.Max(0, Candles.Count - 1));
        long startUnix = Candles[candleIndex].StartUnix;
        long slot = candleIndex;
        foreach (ChartTimelineGap gap in _timelineGaps)
        {
            if (gap.EndUnix <= startUnix)
                slot += gap.SlotCount;
            else
                break;
        }
        return (int)Math.Clamp(slot, 0, int.MaxValue / 2L);
    }

    private int FindFirstCandleAtOrAfterTimelineSlot(int timelineSlot)
    {
        if (_rawTickDrawingSurface)
            return Math.Clamp(timelineSlot, 0, RawTickDrawingTicks.Count);
        int low = 0;
        int high = Candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (GetCandleTimelineSlot(middle) < timelineSlot)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private int GetGapTimelineStartSlot(int gapIndex)
    {
        ChartTimelineGap gap = _timelineGaps[gapIndex];
        int candleCountBefore = 0;
        int low = 0;
        int high = Candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Candles[middle].StartUnix < gap.StartUnix)
                low = middle + 1;
            else
                high = middle;
        }
        candleCountBefore = low;

        long priorSlots = 0;
        for (int index = 0; index < gapIndex; index++)
            priorSlots += _timelineGaps[index].SlotCount;
        return (int)Math.Clamp(candleCountBefore + priorSlots, 0, int.MaxValue / 2L);
    }

    private bool TryCreateLayout(
        out ChartLayout layout)
    {
        layout = default;

        if (_rawTickDrawingSurface)
            return TryCreateRawTickDrawingLayout(out layout);

        if (Candles.Count == 0)
            return false;

        double width =
            ActualWidth -
            LeftMargin -
            RightMargin;
        double height =
            ActualHeight -
            TopMargin -
            BottomMargin;

        if (width < 40 ||
            height < 40)
        {
            return false;
        }

        ClampViewport();

        int totalSlots = GetTotalTimelineSlots();
        int timelineLastExclusive =
            totalSlots -
            _rightOffset;

        int timelineFirst =
            timelineLastExclusive -
            _visibleCount;

        int first = FindFirstCandleAtOrAfterTimelineSlot(timelineFirst);
        int lastExclusive = FindFirstCandleAtOrAfterTimelineSlot(timelineLastExclusive);
        first = Math.Clamp(first, 0, Candles.Count);
        lastExclusive = Math.Clamp(lastExclusive, first, Candles.Count);
        int count = lastExclusive - first;

        int[] visibleSlots = new int[count];
        for (int visibleIndex = 0; visibleIndex < count; visibleIndex++)
        {
            visibleSlots[visibleIndex] =
                GetCandleTimelineSlot(first + visibleIndex) - timelineFirst;
        }

        double minimum = double.MaxValue;
        double maximum = double.MinValue;

        int rangeFirst;
        int rangeLastExclusive;
        if (count > 0)
        {
            rangeFirst = first;
            rangeLastExclusive = lastExclusive;
        }
        else
        {
            int nearest = Math.Clamp(first, 0, Candles.Count - 1);
            int half = Math.Max(1, _horizontalReferenceCount / 2);
            rangeFirst = Math.Max(0, nearest - half);
            rangeLastExclusive = Math.Min(Candles.Count, rangeFirst + Math.Max(1, _horizontalReferenceCount));
            if (rangeLastExclusive <= rangeFirst)
            {
                rangeFirst = Math.Max(0, Candles.Count - 1);
                rangeLastExclusive = Candles.Count;
            }
        }

        for (int index = rangeFirst; index < rangeLastExclusive; index++)
        {
            minimum = Math.Min(minimum, Candles[index].Low);
            maximum = Math.Max(maximum, Candles[index].High);
        }

        foreach (DemoTradeLineOverlay tradeLine in _demoTradeLines.Where(item =>
                     item.IncludeInAutoScale ||
                     (item.IsHistorical && DemoHistoryOverlayOverlapsCandleRange(item, rangeFirst, rangeLastExclusive))))
        {
            double tradePrice = string.Equals(tradeLine.LineId, _draggingDemoTradeLineId, StringComparison.Ordinal)
                ? _draggingDemoTradePrice ?? tradeLine.Price
                : tradeLine.Price;
            if (!double.IsFinite(tradePrice))
                continue;
            minimum = Math.Min(minimum, tradePrice);
            maximum = Math.Max(maximum, tradePrice);
        }

        if (!double.IsFinite(minimum) || !double.IsFinite(maximum))
            return false;

        double range = maximum - minimum;
        double padding = range > 0
            ? range * 0.06
            : Math.Max(Math.Abs(maximum) * 0.0001, 0.00001);

        double displayMinimum = minimum - padding;
        double displayMaximum = maximum + padding;

        if (!_verticalAuto &&
            double.IsFinite(_manualMinimum) &&
            double.IsFinite(_manualMaximum) &&
            _manualMaximum > _manualMinimum)
        {
            displayMinimum = _manualMinimum;
            displayMaximum = _manualMaximum;
        }

        Rect plot = CreateStablePlotRect(width, height);

        layout = new ChartLayout(
            plot,
            timelineFirst,
            first,
            lastExclusive,
            count,
            _visibleCount,
            visibleSlots,
            displayMinimum,
            displayMaximum);

        return true;
    }

    private void EnsureManualPriceRange(
        ChartLayout layout)
    {
        if (!_verticalAuto &&
            _manualMaximum >
            _manualMinimum)
        {
            return;
        }

        _manualMinimum =
            layout.MinimumPrice;
        _manualMaximum =
            layout.MaximumPrice;

        if (_verticalReferenceSpan <= 0)
        {
            _verticalReferenceSpan =
                Math.Max(
                    _manualMaximum -
                    _manualMinimum,
                    0.0000001);
        }
    }

    private int GetFitVisibleCount()
    {
        double plotWidth =
            ActualWidth -
            LeftMargin -
            RightMargin;

        if (!double.IsFinite(plotWidth) ||
            plotWidth <= 0)
        {
            return DefaultVisibleCount;
        }

        return Math.Clamp(
            (int)Math.Round(
                plotWidth /
                PreferredCandleSlotWidth),
            MinimumFitVisibleCount,
            MaximumFitVisibleCount);
    }

    private int GetMinimumVisibleCount()
    {
        int reference =
            Math.Max(
                1,
                _horizontalReferenceCount);

        return Math.Max(
            1,
            (int)Math.Floor(
                reference /
                MaximumZoomFactor));
    }

    private int GetMaximumVisibleCount()
    {
        // Only horizontal candle count is capped. Price-scale zoom remains
        // independent and keeps its existing full range.
        return MaximumHorizontalVisibleCandles;
    }

    private double ClampVerticalSpan(
        double proposedSpan,
        ChartLayout layout)
    {
        if (_verticalReferenceSpan <= 0)
        {
            _verticalReferenceSpan =
                Math.Max(
                    layout.MaximumPrice -
                    layout.MinimumPrice,
                    0.0000001);
        }

        double minimum =
            _verticalReferenceSpan /
            MaximumZoomFactor;
        double maximum =
            _verticalReferenceSpan *
            MaximumZoomFactor;

        return Math.Clamp(
            proposedSpan,
            minimum,
            maximum);
    }

    private static double MinimumPriceSpan(
        ChartLayout layout)
    {
        return Math.Max(
            Math.Abs(
                layout.MaximumPrice -
                layout.MinimumPrice),
            0.0000001);
    }

    private static bool IsPriceScale(
        Point mouse,
        ChartLayout layout) =>
        mouse.X >=
        layout.Plot.Right &&
        mouse.X <=
        layout.Plot.Right +
        RightMargin &&
        mouse.Y >=
        layout.Plot.Top &&
        mouse.Y <=
        layout.Plot.Bottom;

    private static bool IsTimeScale(
        Point mouse,
        ChartLayout layout) =>
        mouse.Y >=
        layout.Plot.Bottom &&
        mouse.Y <=
        layout.Plot.Bottom +
        BottomMargin &&
        mouse.X >=
        layout.Plot.Left &&
        mouse.X <=
        layout.Plot.Right;

    private Rect GetLatestButtonRect()
    {
        return new Rect(
            Math.Max(
                LeftMargin,
                ActualWidth -
                RightMargin -
                34),
            Math.Max(
                TopMargin,
                ActualHeight -
                BottomMargin -
                32),
            25,
            25);
    }

    private void DrawLatestButton(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (IsDefaultView())
            return;

        Rect button =
            GetLatestButtonRect();

        byte alpha =
            _liveButtonHovered
                ? (byte)255
                : (byte)128;

        Color latestColor = ColorFrom(Settings.LatestButtonColor, Color.FromRgb(41, 98, 255));
        Color latestTextColor = ColorFrom(Settings.LatestButtonTextColor, Colors.White);
        var fill = new SolidColorBrush(Color.FromArgb(alpha, latestColor.R, latestColor.G, latestColor.B));
        var border = new Pen(new SolidColorBrush(Color.FromArgb(alpha, latestTextColor.R, latestTextColor.G, latestTextColor.B)), 1);

        drawingContext
            .DrawRoundedRectangle(
                fill,
                border,
                button,
                4,
                4);

        var arrowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(alpha, latestTextColor.R, latestTextColor.G, latestTextColor.B)),
            2);

        double middleY =
            button.Top +
            button.Height /
            2.0;

        drawingContext.DrawLine(
            arrowPen,
            new Point(
                button.Left + 6,
                middleY),
            new Point(
                button.Right - 6,
                middleY));
        drawingContext.DrawLine(
            arrowPen,
            new Point(
                button.Right - 11,
                middleY - 5),
            new Point(
                button.Right - 6,
                middleY));
        drawingContext.DrawLine(
            arrowPen,
            new Point(
                button.Right - 11,
                middleY + 5),
            new Point(
                button.Right - 6,
                middleY));
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        Pen gridPen = CreatePen(
            Settings.GridColor,
            Math.Clamp(Settings.GridThickness, 0.25, 5.0),
            ChartLineStyle.Solid,
            Math.Clamp(Settings.GridOpacity, 0.0, 1.0));
        const int horizontalLines = 6;
        const int verticalLines = 8;

        for (
            int index = 0;
            index <= horizontalLines;
            index++)
        {
            double y =
                layout.Plot.Top +
                layout.Plot.Height *
                index /
                horizontalLines;

            drawingContext.DrawLine(
                gridPen,
                new Point(
                    layout.Plot.Left,
                    y),
                new Point(
                    layout.Plot.Right,
                    y));
        }

        for (
            int index = 0;
            index <= verticalLines;
            index++)
        {
            double x =
                layout.Plot.Left +
                layout.Plot.Width *
                index /
                verticalLines;

            drawingContext.DrawLine(
                gridPen,
                new Point(
                    x,
                    layout.Plot.Top),
                new Point(
                    x,
                    layout.Plot.Bottom));
        }
    }

    private void DrawCandles(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        // Resolve appearance once per frame inside each renderer. Body-based
        // renderers also switch when slotWidth < CandleGapPixels + 1.0 so the
        // 1,500-candle view remains crisp instead of producing blurred bodies.
        switch (Settings.ChartType)
        {
            case ChartVisualType.HollowCandles:
                DrawBodyCandles(drawingContext, layout, hollowBullish: true, volumeWeighted: false);
                break;
            case ChartVisualType.Bars:
                DrawOhlcBars(drawingContext, layout);
                break;
            case ChartVisualType.VolumeCandles:
                DrawBodyCandles(drawingContext, layout, hollowBullish: false, volumeWeighted: true);
                break;
            case ChartVisualType.Line:
                DrawCloseLine(drawingContext, layout, showMarkers: false, stepped: false);
                break;
            case ChartVisualType.LineWithMarkers:
                DrawCloseLine(drawingContext, layout, showMarkers: true, stepped: false);
                break;
            case ChartVisualType.StepLine:
                DrawCloseLine(drawingContext, layout, showMarkers: false, stepped: true);
                break;
            case ChartVisualType.Area:
                DrawAreaChart(drawingContext, layout);
                break;
            case ChartVisualType.HlcArea:
                DrawHlcAreaChart(drawingContext, layout);
                break;
            case ChartVisualType.Baseline:
                DrawBaselineChart(drawingContext, layout);
                break;
            case ChartVisualType.Columns:
                DrawColumnsChart(drawingContext, layout);
                break;
            case ChartVisualType.HighLow:
                DrawHighLowChart(drawingContext, layout);
                break;
            case ChartVisualType.HeikinAshi:
            case ChartVisualType.Renko:
            case ChartVisualType.LineBreak:
            case ChartVisualType.Range:
                DrawBodyCandles(drawingContext, layout, hollowBullish: false, volumeWeighted: false);
                break;
            case ChartVisualType.Kagi:
                DrawKagiChart(drawingContext, layout);
                break;
            case ChartVisualType.PointAndFigure:
                DrawPointAndFigureChart(drawingContext, layout);
                break;
            case ChartVisualType.TimePriceOpportunity:
                DrawTimePriceOpportunityChart(drawingContext, layout);
                break;
            case ChartVisualType.SessionVolumeProfile:
                DrawSessionVolumeProfileChart(drawingContext, layout);
                break;
            case ChartVisualType.VolumeFootprint:
                DrawVolumeFootprintChart(drawingContext, layout);
                break;
            default:
                DrawBodyCandles(drawingContext, layout, hollowBullish: false, volumeWeighted: false);
                break;
        }
    }

    private static double SnapStrokeCoordinate(double value, double thickness)
    {
        int roundedThickness = Math.Max(1, (int)Math.Round(thickness));
        return roundedThickness % 2 == 0
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : Math.Round(value, MidpointRounding.AwayFromZero) + 0.5;
    }

    private double SnapToDeviceStroke(double value, double thickness)
    {
        double scale = Math.Max(0.01, VisualTreeHelper.GetDpi(this).DpiScaleX);
        double pixelThickness = Math.Max(1.0, Math.Round(Math.Max(0.25, thickness) * scale));
        double offset = ((int)pixelThickness & 1) == 1 ? 0.5 : 0.0;
        return (Math.Round(value * scale - offset, MidpointRounding.AwayFromZero) + offset) / scale;
    }

    private Rect SnapRectangleToDevicePixels(Rect source, double strokeThickness)
    {
        double left = SnapToDeviceStroke(source.Left, strokeThickness);
        double top = SnapToDeviceStroke(source.Top, strokeThickness);
        double right = SnapToDeviceStroke(source.Right, strokeThickness);
        double bottom = SnapToDeviceStroke(source.Bottom, strokeThickness);
        if (right <= left)
            right = left + 1.0 / Math.Max(0.01, VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (bottom <= top)
            bottom = top + 1.0 / Math.Max(0.01, VisualTreeHelper.GetDpi(this).DpiScaleY);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void DrawCompressedCandles(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        // Far zoom-out uses a strict two-physical-pixel lattice: one pixel for
        // the candle and one pixel of guaranteed separation. Multiple source
        // candles that fall into the same lattice bucket are combined into one
        // OHLC summary. This prevents neighbouring same-colour candles from
        // visually merging into random thick blocks.
        CandlePixelGrid grid = CreateCandlePixelGrid(layout);
        Brush upBrush = BrushFrom(Settings.UpWickColor, Color.FromRgb(47, 184, 137));
        Brush downBrush = BrushFrom(Settings.DownWickColor, Color.FromRgb(223, 92, 104));
        const int compressedPitchPixels = 2;
        const int compressedWidthPixels = 1;
        int visibleIndex = 0;

        while (visibleIndex < layout.Count)
        {
            int slotIndex = layout.VisibleSlots[visibleIndex];
            double slotCenterPixels =
                grid.PlotLeftPixels +
                grid.RawSlotWidthPixels *
                (slotIndex + 0.5);
            int bucketIndex = Math.Max(
                0,
                (int)Math.Floor(
                    (slotCenterPixels - grid.PlotLeftPixels) /
                    compressedPitchPixels));

            Candle first = Candles[layout.FirstIndex + visibleIndex];
            double high = first.High;
            double low = first.Low;
            double open = first.Open;
            double close = first.Close;
            int next = visibleIndex + 1;

            while (next < layout.Count)
            {
                int nextSlot = layout.VisibleSlots[next];
                double nextCenterPixels =
                    grid.PlotLeftPixels +
                    grid.RawSlotWidthPixels *
                    (nextSlot + 0.5);
                int nextBucket = Math.Max(
                    0,
                    (int)Math.Floor(
                        (nextCenterPixels - grid.PlotLeftPixels) /
                        compressedPitchPixels));
                if (nextBucket != bucketIndex)
                    break;

                Candle candle = Candles[layout.FirstIndex + next];
                high = Math.Max(high, candle.High);
                low = Math.Min(low, candle.Low);
                close = candle.Close;
                next++;
            }

            int pixelColumn =
                grid.PlotLeftPixels +
                bucketIndex * compressedPitchPixels;
            if (pixelColumn >= grid.PlotRightPixels)
                break;

            double centerPixels = pixelColumn + 0.5;
            Brush brush = close >= open ? upBrush : downBrush;
            int highPixels = PriceToPixelY(high, layout, grid);
            int lowPixels = PriceToPixelY(low, layout, grid);
            int openPixels = PriceToPixelY(open, layout, grid);
            int closePixels = PriceToPixelY(close, layout, grid);

            DrawVerticalPixelBar(
                drawingContext,
                brush,
                centerPixels,
                Math.Min(highPixels, lowPixels),
                Math.Max(highPixels, lowPixels) + 1,
                compressedWidthPixels,
                grid);

            int bodyTop = Math.Min(openPixels, closePixels);
            int bodyBottom = Math.Max(openPixels, closePixels) + 1;
            if (bodyBottom - bodyTop < 2)
                bodyBottom = bodyTop + 2;

            DrawVerticalPixelBar(
                drawingContext,
                brush,
                centerPixels,
                bodyTop,
                bodyBottom,
                compressedWidthPixels,
                grid);

            visibleIndex = next;
        }
    }


    private void DrawLivePriceLine(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (!Settings.ShowPriceLine || Candles.Count == 0)
            return;

        int liveSlot = GetCandleTimelineSlot(Candles.Count - 1);
        if (liveSlot < layout.TimelineFirst ||
            liveSlot >= layout.TimelineFirst + layout.SlotCount)
        {
            return;
        }

        Candle liveCandle =
            Candles[^1];

        double livePrice =
            liveCandle.Close;

        double y =
            PriceToY(
                livePrice,
                layout);

        if (y < layout.Plot.Top ||
            y > layout.Plot.Bottom)
        {
            return;
        }

        Pen livePricePen = CreatePen(
            Settings.PriceLineColor,
            Math.Clamp(Settings.PriceLineThickness, 0.25, 8.0),
            Settings.PriceLineStyle);
        Brush livePriceBrush = BrushFrom(Settings.PriceLineColor, Color.FromRgb(41, 98, 255));

        drawingContext.DrawLine(
            livePricePen,
            new Point(
                layout.Plot.Left,
                y),
            new Point(
                layout.Plot.Right,
                y));

        int digits =
            Math.Clamp(
                liveCandle.Digits,
                0,
                10);

        string priceText =
            livePrice.ToString(
                $"F{digits}",
                CultureInfo.InvariantCulture);

        FormattedText text =
            CreateText(
                priceText,
                11,
                BrushFrom(Settings.PriceLineTextColor, Colors.White));

        double ticketHeight =
            text.Height +
            6;

        double ticketTop =
            Math.Clamp(
                y -
                ticketHeight /
                2,
                layout.Plot.Top,
                layout.Plot.Bottom -
                ticketHeight);

        var ticket =
            new Rect(
                layout.Plot.Right +
                2,
                ticketTop,
                RightMargin -
                4,
                ticketHeight);

        drawingContext.DrawRoundedRectangle(
            livePriceBrush,
            null,
            ticket,
            3,
            3);

        drawingContext.DrawText(
            text,
            new Point(
                ticket.Left +
                6,
                ticket.Top +
                3));
    }

    private void DrawHistoryBoundary(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (!NativeHistoryBoundaryUnix.HasValue || Candles.Count == 0)
            return;

        long boundary = NativeHistoryBoundaryUnix.Value;
        int low = 0;
        int high = Candles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Candles[middle].StartUnix < boundary)
                low = middle + 1;
            else
                high = middle;
        }

        int candleIndex = low;
        if (candleIndex < layout.FirstIndex || candleIndex >= layout.LastExclusive)
            return;

        double slotWidth = layout.Plot.Width / layout.SlotCount;
        int visibleIndex = candleIndex - layout.FirstIndex;
        int slotIndex = layout.VisibleSlots[visibleIndex];
        double x = layout.Plot.Left + slotWidth * slotIndex;

        Pen historyBoundaryPen = CreatePen(Settings.HistoryBoundaryColor, 1.5, ChartLineStyle.Dashed);
        Brush historyBoundaryBrush = BrushFrom(Settings.HistoryBoundaryColor, Color.FromRgb(139, 92, 246));
        drawingContext.DrawLine(
            historyBoundaryPen,
            new Point(x, layout.Plot.Top),
            new Point(x, layout.Plot.Bottom));

        FormattedText text = CreateText(
            HistoryBoundaryLabel,
            10,
            historyBoundaryBrush);
        double textX = Math.Clamp(
            x + 5,
            layout.Plot.Left + 4,
            Math.Max(layout.Plot.Left + 4, layout.Plot.Right - text.Width - 4));
        drawingContext.DrawText(text, new Point(textX, layout.Plot.Top + 4));
    }

    private void DrawPriceScale(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        const int labelCount = 6;

        int digits =
            Candles.Count > 0
                ? Candles[^1].Digits
                : 5;

        for (
            int index = 0;
            index <= labelCount;
            index++)
        {
            double ratio =
                index /
                (double)labelCount;

            double price =
                layout.MaximumPrice -
                (layout.MaximumPrice -
                 layout.MinimumPrice) *
                ratio;

            FormattedText text =
                CreateText(
                    price.ToString(
                        $"F{Math.Clamp(digits, 0, 10)}",
                        CultureInfo.InvariantCulture),
                    11,
                    BrushFrom(Settings.PriceScaleTextColor, Color.FromRgb(145, 164, 186)));

            drawingContext.DrawText(
                text,
                new Point(
                    layout.Plot.Right + 4,
                    layout.Plot.Top +
                    layout.Plot.Height *
                    ratio -
                    text.Height /
                    2));
        }
    }

    private void DrawTimeScale(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        int step =
            Math.Max(
                1,
                layout.Count /
                6);

        double slotWidth =
            layout.Plot.Width /
            layout.SlotCount;

        for (
            int visibleIndex = 0;
            visibleIndex <
            layout.Count;
            visibleIndex += step)
        {
            Candle candle =
                Candles[
                    layout.FirstIndex +
                    visibleIndex];

            DateTimeOffset time =
                candle.StartTime
                    .ToUniversalTime();

            string format =
                layout.Count >
                180
                    ? "dd MMM"
                    : "HH:mm";

            FormattedText text =
                CreateText(
                    time.ToString(
                        format,
                        CultureInfo.InvariantCulture),
                    10,
                    BrushFrom(Settings.TimeScaleTextColor, Color.FromRgb(145, 164, 186)));

            int slotIndex =
                layout.VisibleSlots[visibleIndex];

            double centerX =
                layout.Plot.Left +
                slotWidth *
                (slotIndex +
                 0.5);

            drawingContext.DrawText(
                text,
                new Point(
                    centerX -
                    text.Width /
                    2,
                    layout.Plot.Bottom +
                    7));
        }
    }

    private void DrawTimelineGapLabels(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (_timelineGaps.Count == 0)
            return;

        Pen gapPen = CreatePen(Settings.GridColor, Math.Clamp(Settings.GridThickness, 0.25, 5.0), ChartLineStyle.Solid, Math.Clamp(Settings.GridOpacity, 0.0, 1.0));
        Brush gapTextBrush = BrushFrom(Settings.TimeScaleTextColor, Color.FromRgb(145, 164, 186));
        double slotWidth = layout.Plot.Width / layout.SlotCount;
        for (int index = 0; index < _timelineGaps.Count; index++)
        {
            ChartTimelineGap gap = _timelineGaps[index];
            int startSlot = GetGapTimelineStartSlot(index) - layout.TimelineFirst;
            int endSlot = startSlot + gap.SlotCount;
            if (endSlot <= 0 || startSlot >= layout.SlotCount)
                continue;

            double left = layout.Plot.Left + Math.Max(0, startSlot) * slotWidth;
            double right = layout.Plot.Left + Math.Min(layout.SlotCount, endSlot) * slotWidth;
            drawingContext.DrawLine(
                gapPen,
                new Point(left, layout.Plot.Top),
                new Point(left, layout.Plot.Bottom));
            drawingContext.DrawLine(
                gapPen,
                new Point(right, layout.Plot.Top),
                new Point(right, layout.Plot.Bottom));

            if (right - left < 65)
                continue;

            string label = string.IsNullOrWhiteSpace(gap.Label)
                ? "Hidden history"
                : gap.Label;
            FormattedText text = CreateText(label, 10, gapTextBrush);
            double textX = Math.Clamp(
                left + (right - left - text.Width) / 2,
                layout.Plot.Left,
                Math.Max(layout.Plot.Left, layout.Plot.Right - text.Width));
            drawingContext.DrawText(
                text,
                new Point(textX, layout.Plot.Bottom + 7));
        }
    }

    private void DrawCrosshair(
        DrawingContext drawingContext,
        ChartLayout layout,
        Point mouse)
    {
        if (!layout.Plot.Contains(mouse))
            return;

        Pen crosshairPen = CreatePen(
            Settings.CrosshairColor,
            Math.Clamp(Settings.CrosshairThickness, 0.25, 8.0),
            Settings.CrosshairLineStyle);
        double x = mouse.X;
        double y = mouse.Y;
        Candle? candle = null;
        MarketTick? rawTick = null;

        if (_rawTickDrawingSurface && RawTickDrawingTicks.Count > 0)
        {
            int rawIndex = RawTickIndexFromPlotX(mouse.X, layout);
            rawIndex = Math.Clamp(rawIndex, 0, RawTickDrawingTicks.Count - 1);
            rawTick = RawTickDrawingTicks[rawIndex];
            candle = DrawingCandles[rawIndex];
            x = RawTickIndexToX(rawIndex, layout);

            if (TryGetDrawingMagnetSnap(mouse, layout, out int magnetIndex, out double magnetPrice))
            {
                rawIndex = Math.Clamp(magnetIndex, 0, RawTickDrawingTicks.Count - 1);
                rawTick = RawTickDrawingTicks[rawIndex];
                candle = DrawingCandles[rawIndex];
                x = RawTickIndexToX(rawIndex, layout);
                y = PriceToY(magnetPrice, layout);
            }
            else if (Settings.SnapCandleCrosshair)
            {
                y = PriceToY(candle.Close, layout);
            }
        }
        else if (TryGetDrawingMagnetSnap(mouse, layout, out int magnetIndex, out double magnetPrice))
        {
            int visibleIndex = magnetIndex - layout.FirstIndex;
            if (visibleIndex >= 0 && visibleIndex < layout.VisibleSlots.Count)
            {
                int slotIndex = layout.VisibleSlots[visibleIndex];
                double slotWidth = layout.Plot.Width / layout.SlotCount;
                x = layout.Plot.Left + slotWidth * (slotIndex + 0.5);
                candle = Candles[magnetIndex];
                y = PriceToY(magnetPrice, layout);
            }
        }
        else if (Settings.SnapCandleCrosshair)
        {
            int? index = HitTestCandle(layout, mouse.X);
            if (index is not null)
            {
                int visibleIndex = index.Value - layout.FirstIndex;
                int slotIndex = layout.VisibleSlots[visibleIndex];
                double slotWidth = layout.Plot.Width / layout.SlotCount;
                x = layout.Plot.Left + slotWidth * (slotIndex + 0.5);
                candle = Candles[index.Value];
                y = PriceToY(candle.Close, layout);
            }
        }

        string cursorMode = _activeDrawingToolId;
        if (cursorMode == "eraser")
            return;

        // Audited reference semantics:
        // Cross = full chart crosshair; Dot = the same crosshair plus a centre dot;
        // Arrow = plain arrow; Demonstration/Magic retain chart guide lines under their custom cursors.
        bool fullGuide = cursorMode is "cursor-crosshair" or "cursor-dot" or "cursor-demo" or "cursor-magic";
        if (fullGuide)
        {
            drawingContext.DrawLine(crosshairPen, new Point(x, layout.Plot.Top), new Point(x, layout.Plot.Bottom));
            drawingContext.DrawLine(crosshairPen, new Point(layout.Plot.Left, y), new Point(layout.Plot.Right, y));
        }
        if (cursorMode == "cursor-dot")
        {
            Brush dotBrush = crosshairPen.Brush;
            drawingContext.DrawEllipse(dotBrush, null, new Point(x, y), 2.6, 2.6);
        }

        if (!Settings.ShowCrosshairLabels || cursorMode is "cursor-arrow" or "selection")
            return;

        double price = YToPrice(y, layout);
        int digits = candle?.Digits ?? (DrawingCandles.Count > 0 ? DrawingCandles[^1].Digits : 5);
        string priceValue = price.ToString(
            $"F{Math.Clamp(digits, 0, 10)}",
            CultureInfo.InvariantCulture);

        FormattedText priceText = CreateText(
            priceValue,
            11,
            BrushFrom(Settings.CrosshairLabelTextColor, Colors.White));

        var priceBackground = new Rect(
            layout.Plot.Right + 2,
            y - priceText.Height / 2 - 2,
            RightMargin - 4,
            priceText.Height + 4);

        drawingContext.DrawRectangle(
            BrushFrom(Settings.CrosshairLabelBackgroundColor, Color.FromRgb(52, 66, 85)),
            null,
            priceBackground);

        drawingContext.DrawText(
            priceText,
            new Point(layout.Plot.Right + 4, y - priceText.Height / 2));

        if (candle is null)
        {
            if (_rawTickDrawingSurface && RawTickDrawingTicks.Count > 0)
            {
                int rawIndex = RawTickIndexFromPlotX(x, layout);
                rawIndex = Math.Clamp(rawIndex, 0, RawTickDrawingTicks.Count - 1);
                rawTick = RawTickDrawingTicks[rawIndex];
                candle = DrawingCandles[rawIndex];
            }
            else
            {
                int? index = HitTestCandle(layout, x);
                if (index is not null)
                    candle = Candles[index.Value];
            }
        }

        if (candle is not null)
        {
            string timeValue = _rawTickDrawingSurface && rawTick is MarketTick tick
                ? tick.Time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : candle.StartTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            FormattedText timeText = CreateText(
                timeValue,
                10,
                BrushFrom(Settings.CrosshairLabelTextColor, Colors.White));

            double left = Math.Clamp(
                x - timeText.Width / 2 - 4,
                layout.Plot.Left,
                layout.Plot.Right - timeText.Width - 8);

            var timeBackground = new Rect(
                left,
                layout.Plot.Bottom + 2,
                timeText.Width + 8,
                timeText.Height + 4);

            drawingContext.DrawRectangle(
                BrushFrom(Settings.CrosshairLabelBackgroundColor, Color.FromRgb(52, 66, 85)),
                null,
                timeBackground);

            drawingContext.DrawText(
                timeText,
                new Point(left + 4, layout.Plot.Bottom + 4));
        }
    }

    private int? HitTestCandle(
        ChartLayout layout,
        double x)
    {
        if (x <
            layout.Plot.Left ||
            x >
            layout.Plot.Right)
        {
            return null;
        }

        double slotWidth =
            layout.Plot.Width /
            layout.SlotCount;

        int slotIndex =
            (int)(
                (x -
                 layout.Plot.Left) /
                slotWidth);

        int low = 0;
        int high = layout.VisibleSlots.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (layout.VisibleSlots[middle] < slotIndex)
                low = middle + 1;
            else
                high = middle;
        }

        if (low >= layout.VisibleSlots.Count || layout.VisibleSlots[low] != slotIndex)
            return null;

        return layout.FirstIndex + low;
    }

    private static double PriceToY(
        double price,
        ChartLayout layout)
    {
        double range =
            layout.MaximumPrice -
            layout.MinimumPrice;

        double ratio =
            range <= 0
                ? 0.5
                : (layout.MaximumPrice -
                   price) /
                  range;

        return
            layout.Plot.Top +
            ratio *
            layout.Plot.Height;
    }

    private static double YToPrice(
        double y,
        ChartLayout layout)
    {
        double ratio =
            (y -
             layout.Plot.Top) /
            layout.Plot.Height;

        ratio =
            Math.Clamp(
                ratio,
                0,
                1);

        return
            layout.MaximumPrice -
            (layout.MaximumPrice -
             layout.MinimumPrice) *
            ratio;
    }

    private void DrawSelectedCandleDot(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        Candle? selected = _selectedCandle;
        if (selected is null || Candles.Count == 0)
            return;

        int index = FindCandleIndex(selected.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return;

        int visibleIndex = index - layout.FirstIndex;
        double slotWidth = GetSlotWidthDip(layout, layout.VisibleSlots[visibleIndex]);
        double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);

        // Keep the selection independent from price scaling. At 96 DPI,
        // 1 mm is about 3.78 device-independent pixels. A 1-pixel radius
        // produces a maximum 2-pixel dot just above the time labels.
        double y = layout.Plot.Bottom + 3.78;
        drawingContext.DrawEllipse(
            BrushFrom(Settings.SelectedCandleColor, Color.FromRgb(250, 204, 21)),
            null,
            new Point(x, y),
            1.0,
            1.0);
    }

    private bool IsSelectedCandleHit(
        ChartLayout layout,
        Point click)
    {
        Candle? selected = _selectedCandle;
        if (selected is null || Candles.Count == 0)
            return false;

        int index = FindCandleIndex(selected.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return false;

        int visibleIndex = index - layout.FirstIndex;
        double slotWidth = GetSlotWidthDip(layout, layout.VisibleSlots[visibleIndex]);
        double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        double tolerance = Math.Max(5.0, slotWidth * 0.55);

        bool nearTimestamp = Math.Abs(click.X - x) <= tolerance;
        bool insideChartOrTimeScale =
            click.Y >= layout.Plot.Top &&
            click.Y <= layout.Plot.Bottom + BottomMargin;
        return nearTimestamp && insideChartOrTimeScale;
    }

    private void DrawReplayInteractiveMarkerLines(DrawingContext drawingContext, ChartLayout layout)
    {
        DrawReplayInteractiveMarkerLine(
            drawingContext, layout, _interactiveSelectionMarker,
            ColorFrom(Settings.ReplayStartLineColor, Color.FromRgb(250, 204, 21)));
        DrawReplayInteractiveMarkerLine(
            drawingContext, layout, _interactiveReplayEndMarker,
            ColorFrom(Settings.ReplayEndLineColor, Color.FromRgb(239, 68, 68)));
    }

    private void DrawReplayInteractiveMarkerLine(
        DrawingContext drawingContext,
        ChartLayout layout,
        CandleMarker? marker,
        Color color)
    {
        if (marker is null || !IsReplayInteractiveMarker(marker) || Candles.Count == 0)
            return;
        int index = FindCandleIndex(marker.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return;
        int visibleIndex = index - layout.FirstIndex;
        double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        var markerBrush = new SolidColorBrush(color);
        double selectorThickness = IsReplayEndMarker(marker)
            ? Settings.ReplayEndLineThickness
            : Settings.ReplayStartLineThickness;
        var pen = new Pen(markerBrush, Math.Clamp(selectorThickness, 1.0, 6.0))
        {
            DashStyle = DashStyles.Solid,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        double lineX = SnapStrokeCoordinate(x, pen.Thickness);
        string caption = IsReplayEndMarker(marker) ? "END" : "START";
        FormattedText label = CreateText(caption, 9, markerBrush);
        double top = layout.Plot.Top + 1;
        double bottom = Math.Max(layout.Plot.Top + 1, layout.Plot.Bottom - label.Height - 1);
        double lineTop = Math.Min(layout.Plot.Bottom, top + label.Height + 2);
        double lineBottom = Math.Max(layout.Plot.Top, bottom - 2);
        if (lineBottom > lineTop)
            drawingContext.DrawLine(pen, new Point(lineX, lineTop), new Point(lineX, lineBottom));
    }

    private void DrawInteractiveSelectionMarker(DrawingContext drawingContext, ChartLayout layout)
    {
        DrawInteractiveSelectionMarker(
            drawingContext, layout, _interactiveSelectionMarker,
            ColorFrom(Settings.ReplayStartLineColor, Color.FromRgb(250, 204, 21)));
        DrawInteractiveSelectionMarker(
            drawingContext, layout, _interactiveReplayEndMarker,
            ColorFrom(Settings.ReplayEndLineColor, Color.FromRgb(239, 68, 68)));
    }

    private void DrawInteractiveSelectionMarker(
        DrawingContext drawingContext,
        ChartLayout layout,
        CandleMarker? marker,
        Color color)
    {
        if (marker is null || Candles.Count == 0)
            return;
        int index = FindCandleIndex(marker.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return;
        int visibleIndex = index - layout.FirstIndex;
        double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        var markerBrush = new SolidColorBrush(color);
        double selectorThickness = IsReplayEndMarker(marker)
            ? Settings.ReplayEndLineThickness
            : Settings.ReplayStartLineThickness;
        var pen = new Pen(markerBrush, Math.Clamp(selectorThickness, 1.0, 6.0))
        {
            DashStyle = DashStyles.Solid,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        double lineX = SnapStrokeCoordinate(x, pen.Thickness);

        if (IsReplayInteractiveMarker(marker))
        {
            string caption = IsReplayEndMarker(marker) ? "END" : "START";
            FormattedText label = CreateText(caption, 9, markerBrush);
            double left = Math.Clamp(
                lineX - label.Width / 2,
                layout.Plot.Left + 1,
                Math.Max(layout.Plot.Left + 1, layout.Plot.Right - label.Width - 1));
            double top = layout.Plot.Top + 1;
            double bottom = Math.Max(layout.Plot.Top + 1, layout.Plot.Bottom - label.Height - 1);

            // The replay selector stroke is drawn in the below-candle layer.
            // Keep only the tiny START/END captions above candles so they stay
            // readable without the selector covering candle bodies or wicks.
            drawingContext.DrawText(label, new Point(left, top));
            drawingContext.DrawText(label, new Point(left, bottom));
        }
        else
        {
            drawingContext.DrawLine(pen, new Point(lineX, layout.Plot.Top), new Point(lineX, layout.Plot.Bottom));
        }
    }

    private CandleMarker? HitTestInteractiveSelectionMarker(ChartLayout layout, double x)
    {
        CandleMarker? marker = _interactiveSelectionMarker;
        if (marker is null || Candles.Count == 0)
            return null;

        int index = FindCandleIndex(marker.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return null;

        int visibleIndex = index - layout.FirstIndex;
        double markerX = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        double slotWidth = GetSlotWidthDip(layout, layout.VisibleSlots[visibleIndex]);
        double tolerance = Math.Max(7.0, Math.Min(14.0, slotWidth * 0.65));
        return Math.Abs(markerX - x) <= tolerance ? marker : null;
    }

    private CandleMarker? HitTestReplayInteractiveMarker(ChartLayout layout, double x)
    {
        CandleMarker? end = HitTestInteractiveMarker(layout, x, _interactiveReplayEndMarker);
        if (end is not null)
            return end;
        CandleMarker? start = HitTestInteractiveSelectionMarker(layout, x);
        return start is not null && IsReplayInteractiveMarker(start) ? start : null;
    }

    private CandleMarker? HitTestInteractiveMarker(ChartLayout layout, double x, CandleMarker? marker)
    {
        if (marker is null || Candles.Count == 0)
            return null;
        int index = FindCandleIndex(marker.StartUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return null;
        int visibleIndex = index - layout.FirstIndex;
        double markerX = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        double slotWidth = GetSlotWidthDip(layout, layout.VisibleSlots[visibleIndex]);
        double tolerance = Math.Max(7.0, Math.Min(14.0, slotWidth * 0.65));
        return Math.Abs(markerX - x) <= tolerance ? marker : null;
    }

    private static bool IsReplayInteractiveMarker(CandleMarker marker) =>
        marker.Source.StartsWith("TickLabReplay", StringComparison.OrdinalIgnoreCase);

    private static bool IsReplayEndMarker(CandleMarker marker) =>
        string.Equals(marker.Source, "TickLabReplayEnd", StringComparison.OrdinalIgnoreCase);

    private int? HitTestNearestCandle(ChartLayout layout, double x)
    {
        if (layout.Count == 0)
            return null;
        CandlePixelGrid hitGrid = CreateCandlePixelGrid(layout);
        int targetSlot = Math.Clamp(
            (int)Math.Floor((x * hitGrid.ScaleX - hitGrid.PlotLeftPixels) / Math.Max(0.0001, hitGrid.RawSlotWidthPixels)),
            0,
            Math.Max(0, layout.SlotCount - 1));
        int bestVisible = 0;
        int bestDistance = int.MaxValue;
        for (int index = 0; index < layout.VisibleSlots.Count; index++)
        {
            int distance = Math.Abs(layout.VisibleSlots[index] - targetSlot);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestVisible = index;
            }
        }
        return layout.FirstIndex + bestVisible;
    }

    private void MoveInteractiveMarkerTo(double x, ChartLayout layout)
    {
        if (!_markerSelectionMode || _interactiveSelectionMarker is null)
            return;
        int? index = HitTestNearestCandle(layout, x);
        if (!index.HasValue)
            return;
        Candle candle = Candles[index.Value];
        _interactiveSelectionMarker = _interactiveSelectionMarker with
        {
            Symbol = candle.Symbol,
            Timeframe = candle.Timeframe,
            StartUnix = candle.StartUnix
        };
        InteractiveMarkerMoved?.Invoke(_interactiveSelectionMarker);
        InvalidateVisual();
    }

    private void MoveInteractiveReplayEndMarkerTo(double x, ChartLayout layout)
    {
        if (!_markerSelectionMode || _interactiveReplayEndMarker is null)
            return;
        int? index = HitTestNearestCandle(layout, x);
        if (!index.HasValue)
            return;
        Candle candle = Candles[index.Value];
        _interactiveReplayEndMarker = _interactiveReplayEndMarker with
        {
            Symbol = candle.Symbol,
            Timeframe = candle.Timeframe,
            StartUnix = candle.StartUnix
        };
        InteractiveMarkerMoved?.Invoke(_interactiveReplayEndMarker);
        InvalidateVisual();
    }

    private void DrawMarkerLines(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (_markers.Count == 0 || Candles.Count == 0)
            return;

        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        foreach (CandleMarker marker in _markers)
        {
            int index = FindCandleIndex(marker.StartUnix);
            if (index < layout.FirstIndex || index >= layout.LastExclusive)
                continue;

            int visibleIndex = index - layout.FirstIndex;
            int slot = layout.VisibleSlots[visibleIndex];
            double centerX = layout.Plot.Left + slotWidth * (slot + 0.5);
            Pen markerPen = GetMarkerPen(marker);
            drawingContext.DrawLine(
                markerPen,
                new Point(centerX, layout.Plot.Top),
                new Point(centerX, layout.Plot.Bottom));
        }
    }

    private void DrawHistoricalNavigationAnchorLine(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (!_historicalNavigationAnchorUnix.HasValue || Candles.Count == 0)
            return;

        long anchorUnix = _historicalNavigationAnchorUnix.Value;
        int index = FindNearestCandleIndexForHistoricalAnchor(anchorUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return;

        // On the original Find timeframe the persisted marker itself already
        // draws this same background stroke. Avoid double-painting it. On every
        // other timeframe the exact-date/time navigation guide remains visible.
        bool sameVisibleMarkerExists = _markers.Any(marker =>
            (marker.AnchorUnix ?? marker.StartUnix) == anchorUnix &&
            FindCandleIndex(marker.StartUnix) == index);
        if (sameVisibleMarkerExists)
            return;

        int visibleIndex = index - layout.FirstIndex;
        double x = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        var brush = new SolidColorBrush(Color.FromArgb(210, 250, 204, 21));
        if (brush.CanFreeze)
            brush.Freeze();
        var pen = new Pen(brush, _historicalNavigationAnchorSelected ? 4.0 : 2.0)
        {
            DashStyle = DashStyles.Solid,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        if (pen.CanFreeze)
            pen.Freeze();
        double lineX = SnapStrokeCoordinate(x, pen.Thickness);
        drawingContext.DrawLine(
            pen,
            new Point(lineX, layout.Plot.Top),
            new Point(lineX, layout.Plot.Bottom));
    }

    private long? HitTestHistoricalNavigationAnchor(ChartLayout layout, double x)
    {
        if (!_historicalNavigationAnchorUnix.HasValue || Candles.Count == 0)
            return null;

        long anchorUnix = _historicalNavigationAnchorUnix.Value;
        int index = FindNearestCandleIndexForHistoricalAnchor(anchorUnix);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return null;

        int visibleIndex = index - layout.FirstIndex;
        double markerX = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
        double slotWidth = layout.SlotCount > 0
            ? layout.Plot.Width / layout.SlotCount
            : 1.0;
        double tolerance = Math.Max(8.0, slotWidth * 0.75);
        return Math.Abs(markerX - x) <= tolerance
            ? anchorUnix
            : null;
    }

    private int FindNearestCandleIndexForHistoricalAnchor(long anchorUnix)
    {
        int exact = FindCandleIndex(anchorUnix);
        if (exact >= 0)
            return exact;
        if (_candles.Count == 0)
            return -1;

        int low = 0;
        int high = _candles.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (_candles[middle].StartUnix < anchorUnix)
                low = middle + 1;
            else
                high = middle - 1;
        }

        int right = Math.Clamp(low, 0, _candles.Count - 1);
        int left = Math.Clamp(low - 1, 0, _candles.Count - 1);
        long leftDistance = Math.Abs(anchorUnix - _candles[left].StartUnix);
        long rightDistance = Math.Abs(_candles[right].StartUnix - anchorUnix);
        return leftDistance <= rightDistance ? left : right;
    }

    private void DrawMarkerLabels(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (_markers.Count == 0 || Candles.Count == 0)
            return;

        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        foreach (CandleMarker marker in _markers)
        {
            int index = FindCandleIndex(marker.StartUnix);
            if (index < layout.FirstIndex || index >= layout.LastExclusive)
                continue;

            int visibleIndex = index - layout.FirstIndex;
            int slot = layout.VisibleSlots[visibleIndex];
            double centerX = layout.Plot.Left + slotWidth * (slot + 0.5);
            string caption = string.IsNullOrWhiteSpace(marker.Label)
                ? "Marker"
                : marker.Label;
            FormattedText text = CreateText(caption, 10, Brushes.White);
            double labelX = Math.Clamp(
                centerX + 4,
                layout.Plot.Left,
                Math.Max(layout.Plot.Left, layout.Plot.Right - text.Width - 10));
            var labelRect = new Rect(
                labelX - 4,
                layout.Plot.Top + 4,
                text.Width + 8,
                text.Height + 4);
            drawingContext.DrawRoundedRectangle(
                MarkerLabelBrush,
                null,
                labelRect,
                3,
                3);
            drawingContext.DrawText(
                text,
                new Point(labelX, layout.Plot.Top + 6));
        }
    }

    private static Pen GetMarkerPen(CandleMarker marker)
    {
        bool exported = marker.Source.Contains("Export", StringComparison.OrdinalIgnoreCase) ||
                        marker.Label.Contains("Exported", StringComparison.OrdinalIgnoreCase);
        bool selection = marker.Source.Contains("Selection", StringComparison.OrdinalIgnoreCase) ||
                         marker.Label.Contains("Selection", StringComparison.OrdinalIgnoreCase);
        Color color = exported
            ? Color.FromRgb(239, 68, 68)
            : selection
                ? Color.FromRgb(250, 204, 21)
                : Color.FromRgb(250, 204, 21);
        double thickness = exported
            ? 4.0 // Existing red exported line stays unchanged.
            : selection
                ? 1.0
                : 5.0; // Received/imported and local Find markers use maximum emphasis.
        var pen = new Pen(new SolidColorBrush(color), thickness) { DashStyle = DashStyles.Solid };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private CandleMarker? HitTestMarker(ChartLayout layout, double x)
    {
        if (_markers.Count == 0 || Candles.Count == 0)
            return null;

        double slotWidth = layout.SlotCount > 0 ? layout.Plot.Width / layout.SlotCount : 1.0;
        CandleMarker? best = null;
        double bestDistance = Math.Max(8.0, slotWidth * 0.75);

        foreach (CandleMarker marker in _markers)
        {
            int index = FindCandleIndex(marker.StartUnix);
            if (index < layout.FirstIndex || index >= layout.LastExclusive)
                continue;

            int visibleIndex = index - layout.FirstIndex;
            double markerX = GetSlotCenterDip(layout, layout.VisibleSlots[visibleIndex]);
            double distance = Math.Abs(markerX - x);
            if (distance <= bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void ShowChartScrollWheelModeMessage(ChartScrollWheelMode mode)
    {
        _scrollModeMessage = mode == ChartScrollWheelMode.Zoom
            ? "Scroll wheel: Zoom Mode"
            : "Scroll wheel: Scroll Mode";
        _scrollModeMessageTimer.Stop();
        _scrollModeMessageTimer.Start();
        InvalidateVisual();
    }

    private void DrawChartScrollWheelModeMessage(
        DrawingContext drawingContext,
        ChartLayout layout)
    {
        if (string.IsNullOrWhiteSpace(_scrollModeMessage))
            return;

        FormattedText text = CreateText(_scrollModeMessage, 12, BrushFrom(Settings.ChartTextColor, Colors.White));
        const double horizontalPadding = 14;
        const double verticalPadding = 8;
        double width = text.Width + horizontalPadding * 2;
        double height = text.Height + verticalPadding * 2;
        var rect = new Rect(
            layout.Plot.Left + Math.Max(0, (layout.Plot.Width - width) / 2.0),
            layout.Plot.Top + 14,
            width,
            height);
        var fill = new SolidColorBrush(Color.FromArgb(225, 15, 23, 42));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1);
        drawingContext.DrawRoundedRectangle(fill, border, rect, 6, 6);
        drawingContext.DrawText(
            text,
            new Point(rect.Left + horizontalPadding, rect.Top + verticalPadding));
    }

    private void DrawCenteredMessage(
        DrawingContext drawingContext,
        string message)
    {
        FormattedText text =
            CreateText(
                message,
                15,
                BrushFrom(Settings.ChartTextColor, Color.FromRgb(214, 226, 240)));

        drawingContext.DrawText(
            text,
            new Point(
                Math.Max(
                    0,
                    (ActualWidth -
                     text.Width) /
                    2),
                Math.Max(
                    0,
                    (ActualHeight -
                     text.Height) /
                    2)));
    }

    private FormattedText CreateText(
        string text,
        double size,
        Brush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            size,
            brush,
            VisualTreeHelper
                .GetDpi(this)
                .PixelsPerDip);
    }

    private enum HistoryBoundaryDirection
    {
        Older,
        Newer
    }

    private enum DragMode
    {
        None,
        Plot,
        PriceScale,
        TimeScale
    }

    private readonly record struct ChartLayout(
        Rect Plot,
        int TimelineFirst,
        int FirstIndex,
        int LastExclusive,
        int Count,
        int SlotCount,
        IReadOnlyList<int> VisibleSlots,
        double MinimumPrice,
        double MaximumPrice);
    private void DrawScaleBackgrounds(DrawingContext drawingContext, ChartLayout layout)
    {
        drawingContext.DrawRectangle(
            BrushFrom(Settings.PriceScaleBackgroundColor, Color.FromRgb(7, 16, 27)),
            null,
            new Rect(layout.Plot.Right, layout.Plot.Top, RightMargin, layout.Plot.Height));
        drawingContext.DrawRectangle(
            BrushFrom(Settings.TimeScaleBackgroundColor, Color.FromRgb(7, 16, 27)),
            null,
            new Rect(layout.Plot.Left, layout.Plot.Bottom, layout.Plot.Width, BottomMargin));
    }


    private void DrawAskPriceLine(DrawingContext drawingContext, ChartLayout layout)
    {
        if (!Settings.ShowAskPriceLine || Candles.Count == 0)
            return;

        Candle live = Candles[^1];
        double point = live.Point > 0 ? live.Point : Math.Pow(10, -Math.Clamp(live.Digits, 0, 10));
        double askPrice = live.Close + Math.Max(0, live.Spread) * point;
        if (!double.IsFinite(askPrice) || askPrice <= 0)
            return;

        double y = PriceToY(askPrice, layout);
        if (y < layout.Plot.Top || y > layout.Plot.Bottom)
            return;

        Pen pen = CreatePen(
            Settings.AskPriceLineColor,
            Math.Clamp(Settings.AskPriceLineThickness, 0.25, 8.0),
            Settings.AskPriceLineStyle);
        y = SnapStrokeCoordinate(y, pen.Thickness);
        drawingContext.DrawLine(pen, new Point(layout.Plot.Left, y), new Point(layout.Plot.Right, y));

        int digits = Math.Clamp(live.Digits, 0, 10);
        FormattedText text = CreateText(
            askPrice.ToString($"F{digits}", CultureInfo.InvariantCulture),
            10.5,
            BrushFrom(Settings.AskPriceLineTextColor, Colors.Black));
        double height = Math.Max(18, text.Height + 5);
        double width = Math.Min(Math.Max(86, text.Width + 12), Math.Max(86, RightMargin - 2));
        var ticket = new Rect(layout.Plot.Right + 1, Math.Clamp(y - height / 2, layout.Plot.Top, layout.Plot.Bottom - height), width, height);
        drawingContext.DrawRectangle(BrushFrom(Settings.AskPriceLineColor, Color.FromRgb(216, 168, 74)), null, ticket);
        drawingContext.PushClip(new RectangleGeometry(ticket));
        drawingContext.DrawText(text, new Point(ticket.Left + 5, ticket.Top + 2));
        drawingContext.Pop();
    }

    private void DrawSpreadLine(DrawingContext drawingContext, ChartLayout layout)
    {
        if (!Settings.ShowSpreadLine || Candles.Count == 0)
            return;

        Candle live = Candles[^1];
        double spreadPrice = live.Close - Math.Max(0, live.Spread) * Math.Max(live.Point, 0);
        double liveY = PriceToY(live.Close, layout);
        double spreadY = PriceToY(spreadPrice, layout);
        if (spreadY < layout.Plot.Top || spreadY > layout.Plot.Bottom)
            return;

        Brush spreadBrush = BrushFrom(Settings.SpreadLineColor, Color.FromRgb(245, 181, 68));
        if (Settings.ShowSpreadFill && liveY >= layout.Plot.Top && liveY <= layout.Plot.Bottom)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(Settings.SpreadFillOpacity, 0, 1) * 255);
            Color baseColor = ColorFrom(Settings.SpreadLineColor, Color.FromRgb(245, 181, 68));
            Brush fill = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            drawingContext.DrawRectangle(fill, null, new Rect(
                layout.Plot.Left, Math.Min(liveY, spreadY), layout.Plot.Width, Math.Abs(liveY - spreadY)));
        }

        drawingContext.DrawLine(
            CreatePen(Settings.SpreadLineColor, Math.Clamp(Settings.SpreadLineThickness, 0.25, 8.0), Settings.SpreadLineStyle),
            new Point(layout.Plot.Left, spreadY),
            new Point(layout.Plot.Right, spreadY));

        if (Settings.ShowSpreadLabel)
        {
            string label = $"Spread {Math.Max(0, live.Spread)}";
            FormattedText text = CreateText(label, 10, spreadBrush);
            drawingContext.DrawText(text, new Point(Math.Max(layout.Plot.Left + 4, layout.Plot.Right - text.Width - 6), spreadY - text.Height - 2));
        }
    }

    private void DrawCandleCountdown(DrawingContext drawingContext, ChartLayout layout)
    {
        if (!Settings.ShowCandleCountdown || Candles.Count == 0)
            return;

        Candle live = Candles[^1];
        if (live.IsClosed || live.EndUnix <= 0)
            return;

        long remaining = live.EndUnix - Mt5ServerClock.ServerNowUnix(ServerUtcOffsetMinutes);
        string label = remaining > 0
            ? $"{remaining / 3600:00}:{remaining % 3600 / 60:00}:{remaining % 60:00}"
            : "00:00:00";
        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        int liveSlot = GetCandleTimelineSlot(Candles.Count - 1) - layout.TimelineFirst;
        double x = layout.Plot.Left + slotWidth * (liveSlot + 3.5);
        double countdownPrice = live.Close + 100 * Math.Max(live.Point, 0);
        double y = Math.Clamp(PriceToY(countdownPrice, layout), layout.Plot.Top + 2, layout.Plot.Bottom - 18);
        FormattedText text = CreateText(label, 10.5, BrushFrom(Settings.PriceLineColor, Color.FromRgb(41, 98, 255)));
        drawingContext.DrawText(text, new Point(Math.Clamp(x, layout.Plot.Left + 2, layout.Plot.Right - text.Width - 2), y));
    }

    private static Pen CreatePen(string colorText, double thickness, ChartLineStyle style, double opacity = 1.0)
    {
        Color color = ColorFrom(colorText, Colors.White);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
            brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            DashStyle = style switch
            {
                ChartLineStyle.Dotted => DashStyles.Dot,
                ChartLineStyle.Dashed => DashStyles.Dash,
                _ => DashStyles.Solid
            },
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (pen.CanFreeze)
            pen.Freeze();
        return pen;
    }

    private static Brush BrushFrom(string colorText, Color fallback)
    {
        var brush = new SolidColorBrush(ColorFrom(colorText, fallback));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static Color ColorFrom(string? colorText, Color fallback)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString(colorText);
            return converted is Color color ? color : fallback;
        }
        catch
        {
            return fallback;
        }
    }

}

public sealed record ChartViewportSnapshot(
    int FirstIndex,
    int LastExclusive,
    int TimelineFirst,
    int SlotCount,
    IReadOnlyList<int> VisibleSlots,
    double PlotLeft,
    double PlotWidth,
    int VisibleCount,
    int RightOffset);

public enum ChartVerticalSyncActionKind
{
    Zoom,
    Pan,
    Reset
}

public sealed record ChartVerticalSyncAction(
    ChartVerticalSyncActionKind Kind,
    double Amount,
    double AnchorRatio)
{
    public static ChartVerticalSyncAction Zoom(double factor, double anchorRatio) =>
        new(ChartVerticalSyncActionKind.Zoom, factor, Math.Clamp(anchorRatio, 0.0, 1.0));

    public static ChartVerticalSyncAction Pan(double shiftRatio) =>
        new(ChartVerticalSyncActionKind.Pan, shiftRatio, 0.5);

    public static ChartVerticalSyncAction Reset() =>
        new(ChartVerticalSyncActionKind.Reset, 0.0, 0.5);
}

public sealed record ChartIndicatorMenuEntry(string Key, string DisplayName, string Placement);

public sealed record ChartCrosshairSnapshot(double Ratio, long? StartUnix);

public sealed record ChartWindowAnchor(
    long StartUnix,
    int VisibleSlot,
    int VisibleCount,
    bool VerticalAuto,
    double ManualMinimum,
    double ManualMaximum);

public sealed record ChartTimelineGap(
    long StartUnix,
    long EndUnix,
    int SlotCount,
    string Label);
