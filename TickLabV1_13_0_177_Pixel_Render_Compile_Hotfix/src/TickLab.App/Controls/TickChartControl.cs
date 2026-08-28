using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class TickChartControl : FrameworkElement
{
    private const double LeftMargin = 12;
    private const double TopMargin = 30;
    private const double RightMargin = 48;
    private const double BottomMargin = 32;
    // WPF device-independent units are 1/96 inch. Keep about three inches
    // of clean breathing room to the right of the newest raw tick.
    private const double DefaultRightBlankSpace = 288.0;
    private const int MinimumVisibleTicks = 1;
    // User-facing raw Tick Chart safety ceiling: never squeeze more than
    // 5,000 individual ticks into the visible chart frame. History remains
    // available through normal horizontal panning/paging.
    private const int MaximumVisibleTicks = 5000;
    private const double WheelZoomStep = 1.08;
    private const double MaximumZoomFactor = 100.0;

    private static readonly Brush MidBrush =
        new SolidColorBrush(Color.FromRgb(225, 230, 238));
    private static readonly Pen MidPen = new(MidBrush, 1.1)
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

    private IReadOnlyList<MarketTick> _ticks = Array.Empty<MarketTick>();
    private ChartSettings _settings = ChartSettings.Default;
    private Point? _mousePosition;

    private int _replayVisibleCount;
    private int _viewStart;
    private int _viewCount;
    private int _horizontalReferenceCount;
    private bool _horizontalAuto = true;
    private bool _verticalAuto = true;
    private double _manualMinimum;
    private double _manualMaximum;
    private double _verticalReferenceSpan;

    private DragMode _dragMode;
    private Point _dragStart;
    private int _dragStartViewStart;
    private int _dragStartViewCount;
    private double _dragStartMinimum;
    private double _dragStartMaximum;
    private bool _liveButtonHovered;
    private bool _olderHistoryRequestPending;
    private bool _newerHistoryRequestPending;

    // Raw Tick rendering is substantially denser than candle rendering. Cache the
    // expensive viewport price scan and the static Bid/Ask/Mid drawing so pointer
    // movement / drawing-overlay invalidation does not reconstruct thousands of
    // tick segments when the underlying market geometry has not changed.
    private int _tickDataVersion;
    // Changes only when existing tick indexes can move (full replacement/prepend).
    // Pure live/newer appends keep historical indexes stable, which lets the
    // drawing overlay safely retain its visual cache while new ticks arrive.
    private int _tickIndexOriginVersion;
    private TickLayout? _cachedLayout;
    private TickLayoutCacheKey? _cachedLayoutKey;
    private DrawingGroup? _cachedSeriesDrawing;
    private TickSeriesCacheKey? _cachedSeriesKey;

    // Interactive pan/zoom is intentionally rendered from a retained overscanned
    // series snapshot and an affine transform. Rebuilding thousands of raw-tick
    // segments on every display frame is the main reason Tick Chart felt less
    // smooth than Candle Chart. The exact series is rebuilt only after interaction
    // settles, while the final viewport/timestamps remain exact.
    private DrawingGroup? _interactionSeriesDrawing;
    private TickLayout? _interactionBaseLayout;
    private bool _fastSeriesInteraction;
    private int _interactionDataVersion = -1;
    private readonly DispatcherTimer _wheelInteractionSettleTimer;
    private bool _viewChangedScheduled;

    // Freeze/cache theme resources once per Settings change. Raw Tick used to parse
    // colors and allocate brushes/pens repeatedly inside OnRender.
    private Brush _tickBackgroundBrush = Brushes.Black;
    private Brush _tickTextBrush = Brushes.LightGray;
    private Brush _tickBidBrush = Brushes.DodgerBlue;
    private Brush _tickAskBrush = Brushes.IndianRed;
    private Brush _priceScaleTextBrush = Brushes.LightGray;
    private Brush _timeScaleTextBrush = Brushes.LightGray;
    private Brush _priceScaleBackgroundBrush = Brushes.Black;
    private Brush _timeScaleBackgroundBrush = Brushes.Black;
    private Pen _tickBidPen = new(Brushes.DodgerBlue, 1);
    private Pen _tickAskPen = new(Brushes.IndianRed, 1);
    private Pen _tickGridPen = new(Brushes.DimGray, 1);

    public TickChartControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = Cursors.Cross;
        RefreshStyleCache();

        _wheelInteractionSettleTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(110)
        };
        _wheelInteractionSettleTimer.Tick += (_, _) =>
        {
            _wheelInteractionSettleTimer.Stop();
            EndFastSeriesInteraction();
        };
    }

    public IReadOnlyList<MarketTick> Ticks
    {
        get => _ticks;
        set
        {
            _ticks = value ?? Array.Empty<MarketTick>();
            _replayVisibleCount = _ticks.Count;
            _tickDataVersion++;
            _tickIndexOriginVersion++;
            InvalidateTickRenderCaches();
            FitAll();
        }
    }

    public ChartSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? ChartSettings.Default;
            RefreshStyleCache();
            InvalidateTickRenderCaches();
            InvalidateVisual();
        }
    }

    public int ReplayVisibleCount => EffectiveTickCount;

    // Live raw-tick refreshes are deferred while the user is actively panning
    // or zooming. Otherwise a new tick invalidates the retained interaction
    // snapshot mid-gesture and forces expensive geometry rebuilds.
    public bool IsViewportInteractionActive =>
        _dragMode != DragMode.None || _fastSeriesInteraction;

    // Symbol precision is supplied by MainWindow from MT5/native candle metadata
    // so Tick Measure reports pips using the same point/digit convention as the
    // normal candle chart instead of guessing from the absolute price level.
    public int PriceDigits { get; set; } = 5;
    public double PointSize { get; set; } = 0.00001;

    public bool IsAtLatest
    {
        get
        {
            if (IsHistoricalWindow)
                return false;

            int count = EffectiveTickCount;
            if (count == 0)
                return true;

            EnsureHorizontalViewport();
            return _viewStart + _viewCount >= count;
        }
    }

    public event EventHandler? ViewChanged;
    public event EventHandler? OlderHistoryRequested;
    public event EventHandler? NewerHistoryRequested;
    public event EventHandler? GoToLatestRequested;

    public bool CanRequestOlderHistory { get; set; }
    public bool CanRequestNewerHistory { get; set; }

    // A direct historical Tick Find is not the live edge even if the target
    // happens to sit at the end of the currently loaded local window. This
    // keeps the live right-side breathing space hidden and keeps the Live/End
    // action available until MainWindow reloads the true latest raw ticks.
    public bool IsHistoricalWindow { get; set; }

    public int FirstVisibleIndex
    {
        get
        {
            EnsureHorizontalViewport();
            return _viewStart;
        }
    }

    public int VisibleTickCount
    {
        get
        {
            EnsureHorizontalViewport();
            return _viewCount;
        }
    }

    // Tick Find must change only the navigation target, not the user's zoom.
    // Capture visible density and vertical scale before a historical result
    // replaces the local tick window.
    public TickFindViewportState CaptureFindViewportState()
    {
        int visibleCount = EffectiveTickCount > 0 ? VisibleTickCount : 0;
        double verticalSpan = 0.0;
        if (TryCreateLayout(out TickLayout layout))
            verticalSpan = Math.Max(0.0, layout.MaximumPrice - layout.MinimumPrice);
        return new TickFindViewportState(visibleCount, _verticalAuto, verticalSpan);
    }

    public TickDrawingViewportSnapshot? CaptureDrawingViewport()
    {
        if (!TryCreateLayout(out TickLayout layout))
            return null;
        return new TickDrawingViewportSnapshot(
            layout.Plot,
            layout.DataRight,
            layout.FirstIndex,
            layout.LastExclusive,
            layout.Count,
            layout.MinimumPrice,
            layout.MaximumPrice,
            _tickDataVersion,
            _tickIndexOriginVersion);
    }

    public void CompleteOlderHistoryRequest() => _olderHistoryRequestPending = false;
    public void CompleteNewerHistoryRequest() => _newerHistoryRequestPending = false;

    public void ReplaceTicksKeepingViewport(
        IReadOnlyList<MarketTick> ticks,
        int prependedCount = 0,
        bool followLatest = false)
    {
        int previousCount = EffectiveTickCount;
        bool wasAtLatest = IsAtLatest;
        _ticks = ticks ?? Array.Empty<MarketTick>();
        _replayVisibleCount = _ticks.Count;
        _tickDataVersion++;
        if (prependedCount > 0)
            _tickIndexOriginVersion++;
        InvalidateTickRenderCaches();

        if (_horizontalAuto)
        {
            _horizontalReferenceCount = Math.Max(_horizontalReferenceCount, _ticks.Count);
            _viewCount = GetMaximumVisibleCount(_ticks.Count);
            _viewStart = Math.Max(0, _ticks.Count - _viewCount);
        }
        else
        {
            if (prependedCount > 0)
                _viewStart += prependedCount;
            EnsureHorizontalViewport();
            if ((followLatest || wasAtLatest) && _ticks.Count >= previousCount)
                _viewStart = Math.Max(0, _ticks.Count - _viewCount);
        }

        InvalidateVisual();
        RaiseViewChanged();
    }

    public void SetReplayPosition(
        int visibleCount,
        bool followLatest = true)
    {
        int previousCount = EffectiveTickCount;
        bool wasAtLatest = IsAtLatest;

        _replayVisibleCount = Math.Clamp(
            visibleCount,
            0,
            Ticks.Count);

        if (_horizontalAuto)
        {
            FitHorizontal();
        }
        else
        {
            EnsureHorizontalViewport();

            if (followLatest || wasAtLatest || EffectiveTickCount < previousCount)
                GoToLatest();
        }

        if (_verticalAuto)
            InvalidateVisual();

        RaiseViewChanged();
    }

    public void ZoomIn() => ZoomBoth(1.0 / WheelZoomStep);
    public void ZoomOut() => ZoomBoth(WheelZoomStep);

    public void ZoomBoth(
        double factor,
        double horizontalAnchor = 0.5,
        double verticalAnchor = 0.5)
    {
        ZoomHorizontal(factor, horizontalAnchor);
        ZoomVertical(factor, verticalAnchor);
    }

    public void ZoomHorizontal(
        double factor,
        double anchorRatio = 0.5)
    {
        int effectiveCount = EffectiveTickCount;
        if (effectiveCount < MinimumVisibleTicks)
            return;

        EnsureHorizontalViewport();

        anchorRatio = Math.Clamp(anchorRatio, 0.0, 1.0);
        int oldCount = _viewCount;
        int newCount = Math.Clamp(
            (int)Math.Round(oldCount * factor),
            GetMinimumVisibleCount(effectiveCount),
            GetMaximumVisibleCount(effectiveCount));

        double anchorIndex =
            _viewStart + (oldCount - 1) * anchorRatio;

        int newStart = (int)Math.Round(
            anchorIndex - (newCount - 1) * anchorRatio);

        _horizontalAuto = false;
        _viewCount = newCount;
        _viewStart = Math.Clamp(
            newStart,
            0,
            Math.Max(0, effectiveCount - newCount));

        InvalidateVisual();
        RaiseViewChanged();
    }

    public void ZoomVertical(
        double factor,
        double anchorRatio = 0.5)
    {
        if (!TryCreateLayout(out TickLayout layout))
            return;

        EnsureManualPriceRange(layout);

        anchorRatio = Math.Clamp(anchorRatio, 0.0, 1.0);
        double span = Math.Max(
            _manualMaximum - _manualMinimum,
            MinimumPriceSpan(layout));

        double newSpan = ClampVerticalSpan(
            span * factor,
            layout);

        double anchorPrice =
            _manualMaximum - anchorRatio * span;

        _manualMaximum =
            anchorPrice + anchorRatio * newSpan;
        _manualMinimum =
            anchorPrice - (1.0 - anchorRatio) * newSpan;

        _verticalAuto = false;
        InvalidateVisual();
        RaiseViewChanged();
    }

    public void FitHorizontal()
    {
        int count = EffectiveTickCount;
        _horizontalReferenceCount = count;
        _horizontalAuto = true;
        _viewCount = GetMaximumVisibleCount(count);
        _viewStart = Math.Max(0, count - _viewCount);
        InvalidateVisual();
        RaiseViewChanged();
    }

    public void FitVertical()
    {
        _verticalReferenceSpan = 0;
        _verticalAuto = true;
        InvalidateVisual();
        RaiseViewChanged();
    }

    public void FitAll()
    {
        int count = EffectiveTickCount;
        _horizontalReferenceCount = count;
        _verticalReferenceSpan = 0;
        _horizontalAuto = true;
        _verticalAuto = true;
        _viewCount = GetMaximumVisibleCount(count);
        _viewStart = Math.Max(0, count - _viewCount);
        InvalidateVisual();
        RaiseViewChanged();
    }

    public void ShowLatest(int visibleCount)
    {
        int count = EffectiveTickCount;
        if (count <= 0)
            return;
        _horizontalAuto = false;
        _viewCount = Math.Clamp(visibleCount, GetMinimumVisibleCount(count), GetMaximumVisibleCount(count));
        _viewStart = Math.Max(0, count - _viewCount);
        _horizontalReferenceCount = Math.Max(_horizontalReferenceCount, _viewCount);
        _verticalAuto = true;
        InvalidateVisual();
        RaiseViewChanged();
    }

    public bool ShowTimestamp(long timeMilliseconds, int visibleCount = 1600)
    {
        int count = EffectiveTickCount;
        if (count <= 0)
            return false;

        int index = FindTickIndexByTimestamp(timeMilliseconds);
        if (index < 0)
            return false;

        int wanted = Math.Clamp(
            visibleCount,
            GetMinimumVisibleCount(count),
            GetMaximumVisibleCount(count));
        _horizontalAuto = false;
        _viewCount = wanted;
        _viewStart = Math.Clamp(
            index - wanted / 2,
            0,
            Math.Max(0, count - wanted));
        _verticalAuto = true;
        InvalidateVisual();
        RaiseViewChanged();
        return true;
    }

    public bool ShowTimestampPreservingViewport(
        long timeMilliseconds,
        TickFindViewportState previousViewport,
        int fallbackVisibleCount = 1600)
    {
        int count = EffectiveTickCount;
        if (count <= 0)
            return false;

        int index = FindTickIndexByTimestamp(timeMilliseconds);
        if (index < 0)
            return false;

        int requestedVisible = previousViewport.VisibleCount > 0
            ? previousViewport.VisibleCount
            : fallbackVisibleCount;
        int wanted = Math.Clamp(
            requestedVisible,
            GetMinimumVisibleCount(count),
            GetMaximumVisibleCount(count));

        _horizontalAuto = false;
        _viewCount = wanted;
        _viewStart = Math.Clamp(
            index - wanted / 2,
            0,
            Math.Max(0, count - wanted));

        if (!previousViewport.VerticalAuto && previousViewport.VerticalSpan > 0.0)
        {
            double centerPrice = GetSeriesValue(Ticks[index], useAsk: false, useMid: true);
            double halfSpan = previousViewport.VerticalSpan / 2.0;
            _verticalAuto = false;
            _manualMinimum = centerPrice - halfSpan;
            _manualMaximum = centerPrice + halfSpan;
        }
        else
        {
            _verticalAuto = true;
        }

        InvalidateVisual();
        RaiseViewChanged();
        return true;
    }

    public void GoToLatest()
    {
        int count = EffectiveTickCount;
        if (count == 0)
            return;

        EnsureHorizontalViewport();
        _viewStart = Math.Max(0, count - _viewCount);
        _horizontalAuto = false;
        InvalidateVisual();
        RaiseViewChanged();
    }

    // Navigation entry points used by the transparent shared drawing surface.
    // They execute the exact Raw Tick viewport math that this control normally
    // uses, so placing the real drawing engine above it never changes scrolling
    // or the 5,000-visible-tick zoom ceiling.
    public void HandleExternalMouseWheel(int delta, Point mouse)
    {
        if (!TryCreateLayout(out TickLayout layout))
            return;

        BeginFastSeriesInteraction(layout);
        ArmWheelInteractionSettle();

        if (Settings.ScrollWheelMode == ChartScrollWheelMode.Scroll)
        {
            EnsureHorizontalViewport();
            int count = EffectiveTickCount;
            int oldStart = _viewStart;
            double wheelSteps = Math.Max(0.25, Math.Abs(delta) / 120.0);
            int tickShift = Math.Max(1, (int)Math.Round(_viewCount * 0.12 * wheelSteps));
            _horizontalAuto = false;
            _viewStart += delta > 0 ? tickShift : -tickShift;
            _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, count - _viewCount));
            if (_viewStart != oldStart)
            {
                InvalidateVisual();
                RaiseViewChanged();
                if (_viewStart < oldStart)
                    RequestOlderHistoryIfNeeded(force: false);
                else if (_viewStart > oldStart)
                    RequestNewerHistoryIfNeeded(force: false);
            }
            else if (delta < 0)
            {
                RequestOlderHistoryIfNeeded(force: true);
            }
            else if (delta > 0)
            {
                RequestNewerHistoryIfNeeded(force: true);
            }
            return;
        }

        double factor = Math.Pow(WheelZoomStep, -delta / 120.0);
        if (IsPriceScale(mouse, layout))
        {
            double verticalAnchor = (mouse.Y - layout.Plot.Top) / layout.Plot.Height;
            ZoomVertical(factor, verticalAnchor);
        }
        else if (IsTimeScale(mouse, layout))
        {
            double horizontalAnchor = (mouse.X - layout.Plot.Left) / layout.Plot.Width;
            ZoomHorizontal(factor, horizontalAnchor);
        }
        else if (layout.Plot.Contains(mouse))
        {
            double horizontalAnchor = (mouse.X - layout.Plot.Left) / layout.Plot.Width;
            double verticalAnchor = (mouse.Y - layout.Plot.Top) / layout.Plot.Height;
            ZoomBoth(factor, horizontalAnchor, verticalAnchor);
        }
    }

    public bool HandleExternalKeyDown(KeyEventArgs e)
    {
        if (HandleExternalFindMarkerKeyDown(e))
            return true;
        if (e.Key == Key.End)
        {
            if (GoToLatestRequested is not null)
                GoToLatestRequested.Invoke(this, EventArgs.Empty);
            else
                GoToLatest();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Home)
        {
            EnsureHorizontalViewport();
            _horizontalAuto = false;
            _viewStart = 0;
            InvalidateVisual();
            RequestOlderHistoryIfNeeded(force: true);
            RaiseViewChanged();
            e.Handled = true;
            return true;
        }
        return false;
    }

    public bool BeginExternalPointerInteraction(Point mouse, int clickCount)
    {
        if (!TryCreateLayout(out TickLayout layout))
            return false;

        if (!IsAtLatest && GetLatestButtonRect().Contains(mouse))
        {
            if (GoToLatestRequested is not null)
                GoToLatestRequested.Invoke(this, EventArgs.Empty);
            else
                GoToLatest();
            return true;
        }

        if (clickCount >= 2)
        {
            if (IsPriceScale(mouse, layout))
            {
                FitVertical();
                return true;
            }
            if (IsTimeScale(mouse, layout))
            {
                FitHorizontal();
                return true;
            }
            if (layout.Plot.Contains(mouse))
            {
                FitAll();
                return true;
            }
        }

        _dragMode = IsPriceScale(mouse, layout)
            ? DragMode.PriceScale
            : IsTimeScale(mouse, layout)
                ? DragMode.TimeScale
                : layout.Plot.Contains(mouse) ? DragMode.Plot : DragMode.None;
        if (_dragMode == DragMode.None)
            return false;

        EnsureHorizontalViewport();
        EnsureManualPriceRange(layout);
        BeginFastSeriesInteraction(layout);
        _dragStart = mouse;
        _dragStartViewStart = _viewStart;
        _dragStartViewCount = _viewCount;
        _dragStartMinimum = _manualMinimum;
        _dragStartMaximum = _manualMaximum;
        return true;
    }

    public bool UpdateExternalPointerInteraction(Point mouse, bool leftButtonPressed)
    {
        _mousePosition = mouse;

        // Crosshair/drawing pointer visuals live on the shared drawing overlay. Do
        // not repaint the dense raw-tick renderer just because the pointer moved.
        // The only non-drag pointer visual owned here is the Live-button hover.
        bool hover = GetLatestButtonRect().Contains(mouse) && !IsAtLatest;
        bool hoverChanged = hover != _liveButtonHovered;
        _liveButtonHovered = hover;

        if (_dragMode == DragMode.None || !leftButtonPressed)
        {
            if (hoverChanged)
                InvalidateVisual();
            return false;
        }

        if (!TryCreateLayout(out TickLayout layout))
            return false;

        ApplyDrag(mouse, layout);
        return true;
    }

    public bool EndExternalPointerInteraction()
    {
        if (_dragMode == DragMode.None)
            return false;
        _dragMode = DragMode.None;
        EndFastSeriesInteraction();
        return true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(
            TickBackgroundBrush,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (!TryCreateLayout(out TickLayout layout))
        {
            DrawCenteredMessage(
                drawingContext,
                "No saved raw ticks are available for this chart period");
            return;
        }

        DrawScaleBackgrounds(drawingContext, layout);

        if (Settings.ShowTickGrid)
            DrawGrid(drawingContext, layout);

        DrawLegend(drawingContext);

        // Keep Bid, Ask, Mid, and point markers inside the chart plot,
        // even after an extreme manual price zoom.
        drawingContext.PushClip(
            new RectangleGeometry(
                layout.Plot));

        DrawCachedTickSeries(drawingContext, layout);

        drawingContext.Pop();

        // The Raw Tick renderer owns only its Tick-specific Find marker.
        // Crosshair, Measure and every user drawing are rendered by the existing
        // CandleChartControl drawing engine in transparent Tick-surface mode.
        drawingContext.PushClip(new RectangleGeometry(layout.Plot));
        DrawFindTickMarker(drawingContext, layout);
        drawingContext.Pop();

        DrawLatestPriceLines(drawingContext, layout);
        DrawPriceScale(drawingContext, layout);
        DrawTimeScale(drawingContext, layout);
        DrawLatestButton(drawingContext, layout);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HandleExternalFindMarkerKeyDown(e))
            return;
        if (e.Key == Key.End)
        {
            if (GoToLatestRequested is not null)
                GoToLatestRequested.Invoke(this, EventArgs.Empty);
            else
                GoToLatest();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Home)
        {
            EnsureHorizontalViewport();
            _horizontalAuto = false;
            _viewStart = 0;
            InvalidateVisual();
            RequestOlderHistoryIfNeeded(force: true);
            RaiseViewChanged();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point mouse = e.GetPosition(this);
        _mousePosition = mouse;

        bool renderedByDrag = false;
        if (_dragMode != DragMode.None &&
            e.LeftButton == MouseButtonState.Pressed &&
            TryCreateLayout(out TickLayout layout))
        {
            ApplyDrag(mouse, layout);
            renderedByDrag = true;
            e.Handled = true;
        }

        bool hover = GetLatestButtonRect().Contains(mouse) && !IsAtLatest;
        bool hoverChanged = hover != _liveButtonHovered;
        _liveButtonHovered = hover;

        UpdateCursor(mouse);
        if (!renderedByDrag && hoverChanged)
            InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_dragMode == DragMode.None)
            _mousePosition = null;

        bool hoverChanged = _liveButtonHovered;
        _liveButtonHovered = false;
        if (hoverChanged)
            InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        if (!TryCreateLayout(out TickLayout layout))
            return;

        Point mouse = e.GetPosition(this);

        if (!IsAtLatest && GetLatestButtonRect().Contains(mouse))
        {
            if (GoToLatestRequested is not null)
                GoToLatestRequested.Invoke(this, EventArgs.Empty);
            else
                GoToLatest();
            e.Handled = true;
            return;
        }

        if (HandleExternalFindMarkerMouseDown(mouse))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            if (IsPriceScale(mouse, layout))
            {
                FitVertical();
                e.Handled = true;
                return;
            }

            if (IsTimeScale(mouse, layout))
            {
                FitHorizontal();
                e.Handled = true;
                return;
            }

            if (layout.Plot.Contains(mouse))
            {
                FitAll();
                e.Handled = true;
                return;
            }
        }

        _dragMode =
            IsPriceScale(mouse, layout)
                ? DragMode.PriceScale
                : IsTimeScale(mouse, layout)
                    ? DragMode.TimeScale
                    : layout.Plot.Contains(mouse)
                        ? DragMode.Plot
                        : DragMode.None;

        if (_dragMode == DragMode.None)
            return;

        EnsureHorizontalViewport();
        EnsureManualPriceRange(layout);
        BeginFastSeriesInteraction(layout);

        _dragStart = mouse;
        _dragStartViewStart = _viewStart;
        _dragStartViewCount = _viewCount;
        _dragStartMinimum = _manualMinimum;
        _dragStartMaximum = _manualMaximum;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            ReleaseMouseCapture();
            EndFastSeriesInteraction();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!TryCreateLayout(out TickLayout layout))
            return;

        BeginFastSeriesInteraction(layout);
        ArmWheelInteractionSettle();

        // Match the regular candle chart's configured wheel behavior. In
        // Scroll mode the visible tick count stays fixed and the wheel pans
        // through history; it must never masquerade as horizontal zoom-out.
        if (Settings.ScrollWheelMode == ChartScrollWheelMode.Scroll)
        {
            EnsureHorizontalViewport();
            int count = EffectiveTickCount;
            int oldStart = _viewStart;
            double wheelSteps = Math.Max(0.25, Math.Abs(e.Delta) / 120.0);
            int tickShift = Math.Max(
                1,
                (int)Math.Round(_viewCount * 0.12 * wheelSteps));

            _horizontalAuto = false;
            _viewStart += e.Delta > 0 ? tickShift : -tickShift;
            _viewStart = Math.Clamp(
                _viewStart,
                0,
                Math.Max(0, count - _viewCount));

            if (_viewStart != oldStart)
            {
                InvalidateVisual();
                ScheduleViewChanged();
                if (_viewStart < oldStart)
                    RequestOlderHistoryIfNeeded(force: false);
                else if (_viewStart > oldStart)
                    RequestNewerHistoryIfNeeded(force: false);
            }
            else if (e.Delta < 0)
            {
                // At the oldest loaded edge, ask for the next history page
                // without changing the zoom density.
                RequestOlderHistoryIfNeeded(force: true);
            }
            else if (e.Delta > 0)
            {
                // Historical Tick Find windows page forward too. The found
                // tick is a navigation anchor, never an artificial right edge.
                RequestNewerHistoryIfNeeded(force: true);
            }

            e.Handled = true;
            return;
        }

        Point mouse = e.GetPosition(this);
        // Smaller proportional steps make both axes feel continuous and also
        // respect high-resolution wheel/touchpad deltas.
        double factor = Math.Pow(WheelZoomStep, -e.Delta / 120.0);

        if (IsPriceScale(mouse, layout))
        {
            double verticalAnchor =
                (mouse.Y - layout.Plot.Top) / layout.Plot.Height;
            ZoomVertical(factor, verticalAnchor);
        }
        else if (IsTimeScale(mouse, layout))
        {
            double horizontalAnchor =
                (mouse.X - layout.Plot.Left) / layout.Plot.Width;
            ZoomHorizontal(factor, horizontalAnchor);
        }
        else if (layout.Plot.Contains(mouse))
        {
            double horizontalAnchor =
                (mouse.X - layout.Plot.Left) / layout.Plot.Width;
            double verticalAnchor =
                (mouse.Y - layout.Plot.Top) / layout.Plot.Height;

            ZoomBoth(
                factor,
                horizontalAnchor,
                verticalAnchor);
        }

        e.Handled = true;
    }

    private void ApplyDrag(
        Point mouse,
        TickLayout layout)
    {
        double dx = mouse.X - _dragStart.X;
        double dy = mouse.Y - _dragStart.Y;

        int previousViewStart = _viewStart;
        int previousViewCount = _viewCount;
        bool previousHorizontalAuto = _horizontalAuto;
        bool previousVerticalAuto = _verticalAuto;
        double previousMinimum = _manualMinimum;
        double previousMaximum = _manualMaximum;

        switch (_dragMode)
        {
            case DragMode.Plot:
            {
                // TradingView-style free tick-chart movement:
                // drag horizontally through tick history and vertically
                // through price space while preserving both zoom levels.
                int count = EffectiveTickCount;
                int shift = (int)Math.Round(
                    -dx / Math.Max(1.0, layout.Plot.Width) *
                    _dragStartViewCount);

                _horizontalAuto = false;
                _viewCount = Math.Clamp(
                    _dragStartViewCount,
                    MinimumVisibleTicks,
                    Math.Max(MinimumVisibleTicks, count));
                _viewStart = Math.Clamp(
                    _dragStartViewStart + shift,
                    0,
                    Math.Max(0, count - _viewCount));

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
                    _dragStartMaximum - _dragStartMinimum;
                double factor = Math.Exp(dy / 240.0);
                double newSpan = ClampVerticalSpan(
                    span * factor,
                    layout);
                double center =
                    (_dragStartMinimum + _dragStartMaximum) / 2.0;

                _verticalAuto = false;
                _manualMinimum = center - newSpan / 2.0;
                _manualMaximum = center + newSpan / 2.0;
                break;
            }

            case DragMode.TimeScale:
            {
                int count = EffectiveTickCount;
                double factor = Math.Exp(dx / 360.0);
                int newCount = Math.Clamp(
                    (int)Math.Round(
                        _dragStartViewCount * factor),
                    GetMinimumVisibleCount(count),
                    GetMaximumVisibleCount(count));

                double center =
                    _dragStartViewStart +
                    (_dragStartViewCount - 1) / 2.0;

                _horizontalAuto = false;
                _viewCount = newCount;
                _viewStart = Math.Clamp(
                    (int)Math.Round(
                        center - (newCount - 1) / 2.0),
                    0,
                    Math.Max(0, count - newCount));
                break;
            }
        }

        if (previousViewStart == _viewStart &&
            previousViewCount == _viewCount &&
            previousHorizontalAuto == _horizontalAuto &&
            previousVerticalAuto == _verticalAuto &&
            previousMinimum.Equals(_manualMinimum) &&
            previousMaximum.Equals(_manualMaximum))
        {
            return;
        }

        InvalidateVisual();
        RaiseViewChanged();
    }

    private void UpdateCursor(Point mouse)
    {
        if (!TryCreateLayout(out TickLayout layout))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (!IsAtLatest && GetLatestButtonRect().Contains(mouse))
            Cursor = Cursors.Hand;
        else if (IsPriceScale(mouse, layout))
            Cursor = Cursors.SizeNS;
        else if (IsTimeScale(mouse, layout))
            Cursor = Cursors.SizeWE;
        else if (_dragMode == DragMode.Plot)
            Cursor = Cursors.SizeAll;
        else
            Cursor = Cursors.Cross;
    }

    private int EffectiveTickCount =>
        Math.Clamp(
            _replayVisibleCount,
            0,
            Ticks.Count);

    private void EnsureHorizontalViewport()
    {
        int count = EffectiveTickCount;

        if (count <= 0)
        {
            _viewStart = 0;
            _viewCount = 0;
            return;
        }

        if (_horizontalAuto)
        {
            _viewCount = GetMaximumVisibleCount(count);
            _viewStart = Math.Max(0, count - _viewCount);
            return;
        }

        _viewCount = Math.Clamp(
            _viewCount <= 0 ? count : _viewCount,
            GetMinimumVisibleCount(count),
            GetMaximumVisibleCount(count));

        _viewStart = Math.Clamp(
            _viewStart,
            0,
            Math.Max(0, count - _viewCount));
    }

    private bool TryCreateLayout(out TickLayout layout)
    {
        layout = default;

        int effectiveCount = EffectiveTickCount;
        if (effectiveCount == 0)
            return false;

        double width = ActualWidth - LeftMargin - RightMargin;
        double height = ActualHeight - TopMargin - BottomMargin;

        if (width < 40 || height < 40)
            return false;

        EnsureHorizontalViewport();

        int first = _viewStart;
        int lastExclusive = Math.Min(
            effectiveCount,
            first + _viewCount);

        if (lastExclusive <= first)
            return false;

        bool atLatest = !IsHistoricalWindow && lastExclusive >= effectiveCount;
        var cacheKey = new TickLayoutCacheKey(
            _tickDataVersion,
            effectiveCount,
            first,
            lastExclusive,
            width,
            height,
            _verticalAuto,
            _manualMinimum,
            _manualMaximum,
            Settings.ShowBidLine,
            Settings.ShowAskLine,
            Settings.ShowMidLine,
            IsHistoricalWindow,
            atLatest);

        if (_cachedLayoutKey is TickLayoutCacheKey existingKey &&
            existingKey.Equals(cacheKey) &&
            _cachedLayout is TickLayout existingLayout)
        {
            layout = existingLayout;
            return true;
        }

        double autoMinimum;
        double autoMaximum;
        if (!_verticalAuto &&
            double.IsFinite(_manualMinimum) &&
            double.IsFinite(_manualMaximum) &&
            _manualMaximum > _manualMinimum)
        {
            // During manual pan/scale interaction the price range is already
            // authoritative. Do not rescan up to 5,000 Bid/Ask ticks just to
            // throw that result away on every frame.
            autoMinimum = _manualMinimum;
            autoMaximum = _manualMaximum;
        }
        else
        {
            double minimum = double.MaxValue;
            double maximum = double.MinValue;

            for (int index = first; index < lastExclusive; index++)
            {
                MarketTick tick = Ticks[index];
                double bid = GetBid(tick);
                double ask = GetAsk(tick);

                if (Settings.ShowBidLine)
                {
                    minimum = Math.Min(minimum, bid);
                    maximum = Math.Max(maximum, bid);
                }

                if (Settings.ShowAskLine)
                {
                    minimum = Math.Min(minimum, ask);
                    maximum = Math.Max(maximum, ask);
                }

                if (Settings.ShowMidLine)
                {
                    double mid = (bid + ask) / 2.0;
                    minimum = Math.Min(minimum, mid);
                    maximum = Math.Max(maximum, mid);
                }
            }

            if (minimum == double.MaxValue || maximum == double.MinValue)
            {
                for (int index = first; index < lastExclusive; index++)
                {
                    minimum = Math.Min(minimum, Ticks[index].DisplayPrice);
                    maximum = Math.Max(maximum, Ticks[index].DisplayPrice);
                }
            }

            double range = maximum - minimum;
            double padding = range > 0
                ? range * 0.08
                : Math.Max(Math.Abs(maximum) * 0.0001, 0.00001);
            autoMinimum = minimum - padding;
            autoMaximum = maximum + padding;
        }

        long firstTime = Ticks[first].TimeMilliseconds;
        long lastTime = Ticks[lastExclusive - 1].TimeMilliseconds;

        Rect plot = new(
            LeftMargin,
            TopMargin,
            width,
            height);
        // Future/right breathing room belongs only to the live edge. The
        // instant the user pans into history, reclaim the complete plot width
        // so an accidental scroll cannot leave a persistent empty right gap.
        double rightBlank = atLatest
            ? Math.Min(
                DefaultRightBlankSpace,
                Math.Max(0.0, plot.Width * 0.45))
            : 0.0;

        layout = new TickLayout(
            plot,
            Math.Max(plot.Left + 1.0, plot.Right - rightBlank),
            first,
            lastExclusive,
            lastExclusive - first,
            autoMinimum,
            autoMaximum,
            firstTime,
            lastTime);

        _cachedLayoutKey = cacheKey;
        _cachedLayout = layout;
        return true;
    }

    private void InvalidateTickRenderCaches()
    {
        _cachedLayout = null;
        _cachedLayoutKey = null;
        _cachedSeriesDrawing = null;
        _cachedSeriesKey = null;
        _interactionSeriesDrawing = null;
        _interactionBaseLayout = null;
        _interactionDataVersion = -1;
        _fastSeriesInteraction = false;
    }

    private void EnsureManualPriceRange(
        TickLayout layout)
    {
        if (!_verticalAuto &&
            _manualMaximum > _manualMinimum)
        {
            return;
        }

        _manualMinimum = layout.MinimumPrice;
        _manualMaximum = layout.MaximumPrice;

        if (_verticalReferenceSpan <= 0)
        {
            _verticalReferenceSpan =
                Math.Max(
                    _manualMaximum -
                    _manualMinimum,
                    0.0000001);
        }
    }

    private static int GetMinimumVisibleCount(
        int availableCount) =>
        availableCount > 0 ? 1 : 0;

    private static int GetMaximumVisibleCount(
        int availableCount) =>
        availableCount > 0
            ? Math.Min(MaximumVisibleTicks, availableCount)
            : 0;

    private double ClampVerticalSpan(
        double proposedSpan,
        TickLayout layout)
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
        TickLayout layout)
    {
        return Math.Max(
            Math.Abs(
                layout.MaximumPrice -
                layout.MinimumPrice),
            0.0000001);
    }

    private static bool IsPriceScale(
        Point mouse,
        TickLayout layout) =>
        mouse.X >= layout.Plot.Right &&
        mouse.X <= layout.Plot.Right + RightMargin &&
        mouse.Y >= layout.Plot.Top &&
        mouse.Y <= layout.Plot.Bottom;

    private static bool IsTimeScale(
        Point mouse,
        TickLayout layout) =>
        mouse.Y >= layout.Plot.Bottom &&
        mouse.Y <= layout.Plot.Bottom + BottomMargin &&
        mouse.X >= layout.Plot.Left &&
        mouse.X <= layout.Plot.Right;

    private Rect GetLatestButtonRect()
    {
        return new Rect(
            Math.Max(
                LeftMargin,
                ActualWidth - RightMargin - 34),
            Math.Max(
                TopMargin,
                ActualHeight - BottomMargin - 32),
            25,
            25);
    }

    private Brush TickBackgroundBrush => _tickBackgroundBrush;
    private Brush TickTextBrush => _tickTextBrush;
    private Brush TickBidBrush => _tickBidBrush;
    private Brush TickAskBrush => _tickAskBrush;
    private Pen TickBidPen => _tickBidPen;
    private Pen TickAskPen => _tickAskPen;
    private Pen TickGridPen => _tickGridPen;

    private void RefreshStyleCache()
    {
        _tickBackgroundBrush = CreateFrozenBrush(Settings.ChartBackgroundColor, Color.FromRgb(8, 14, 22));
        _tickTextBrush = CreateFrozenBrush(Settings.ChartTextColor, Color.FromRgb(190, 202, 216));
        _tickBidBrush = CreateFrozenBrush(Settings.TickBidColor, Color.FromRgb(45, 126, 247));
        _tickAskBrush = CreateFrozenBrush(Settings.TickAskColor, Color.FromRgb(239, 81, 80));
        _priceScaleTextBrush = CreateFrozenBrush(Settings.PriceScaleTextColor, Color.FromRgb(145, 164, 186));
        _timeScaleTextBrush = CreateFrozenBrush(Settings.TimeScaleTextColor, Color.FromRgb(145, 164, 186));
        _priceScaleBackgroundBrush = CreateFrozenBrush(Settings.PriceScaleBackgroundColor, Color.FromRgb(7, 16, 27));
        _timeScaleBackgroundBrush = CreateFrozenBrush(Settings.TimeScaleBackgroundColor, Color.FromRgb(7, 16, 27));
        _tickBidPen = CreateFrozenPen(_tickBidBrush, Math.Clamp(Settings.TickBidThickness, 0.25, 8.0));
        _tickAskPen = CreateFrozenPen(_tickAskBrush, Math.Clamp(Settings.TickAskThickness, 0.25, 8.0));
        Brush gridBrush = CreateFrozenBrush(Settings.GridColor, Color.FromRgb(29, 42, 56), Math.Clamp(Settings.GridOpacity, 0.0, 1.0));
        _tickGridPen = CreateFrozenPen(gridBrush, Math.Clamp(Settings.GridThickness, 0.25, 5.0));
    }

    private static Brush CreateFrozenBrush(string? value, Color fallback, double opacity = 1.0)
    {
        Color color = fallback;
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
                color = parsed;
        }
        catch { }
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze)
            pen.Freeze();
        return pen;
    }

    private void DrawLatestButton(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        if (IsAtLatest)
            return;

        Rect button = GetLatestButtonRect();
        byte alpha =
            _liveButtonHovered
                ? (byte)255
                : (byte)128;

        var fill = new SolidColorBrush(
            Color.FromArgb(
                alpha,
                41,
                98,
                255));
        var border = new Pen(
            new SolidColorBrush(
                Color.FromArgb(
                    alpha,
                    160,
                    190,
                    255)),
            1);

        drawingContext.DrawRoundedRectangle(
            fill,
            border,
            button,
            4,
            4);

        var arrowPen = new Pen(
            new SolidColorBrush(
                Color.FromArgb(
                    alpha,
                    255,
                    255,
                    255)),
            2);

        double middleY = button.Top + button.Height / 2.0;
        drawingContext.DrawLine(
            arrowPen,
            new Point(button.Left + 6, middleY),
            new Point(button.Right - 6, middleY));
        drawingContext.DrawLine(
            arrowPen,
            new Point(button.Right - 11, middleY - 5),
            new Point(button.Right - 6, middleY));
        drawingContext.DrawLine(
            arrowPen,
            new Point(button.Right - 11, middleY + 5),
            new Point(button.Right - 6, middleY));
    }


    private void DrawScaleBackgrounds(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        drawingContext.DrawRectangle(
            _priceScaleBackgroundBrush,
            null,
            new Rect(layout.Plot.Right, layout.Plot.Top, RightMargin, layout.Plot.Height));
        drawingContext.DrawRectangle(
            _timeScaleBackgroundBrush,
            null,
            new Rect(layout.Plot.Left, layout.Plot.Bottom, layout.Plot.Width, BottomMargin));
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        const int horizontalLines = 6;
        const int verticalLines = 8;

        for (int index = 0; index <= horizontalLines; index++)
        {
            double y =
                layout.Plot.Top +
                layout.Plot.Height *
                index /
                horizontalLines;

            drawingContext.DrawLine(
                TickGridPen,
                new Point(layout.Plot.Left, y),
                new Point(layout.Plot.Right, y));
        }

        for (int index = 0; index <= verticalLines; index++)
        {
            double x =
                layout.Plot.Left +
                layout.Plot.Width *
                index /
                verticalLines;

            drawingContext.DrawLine(
                TickGridPen,
                new Point(x, layout.Plot.Top),
                new Point(x, layout.Plot.Bottom));
        }
    }

    private void DrawCachedTickSeries(DrawingContext drawingContext, TickLayout layout)
    {
        // During active pan/zoom draw the retained overscanned market series with
        // a cheap affine transform. This is the key smoothness path: WPF no longer
        // reconstructs Bid/Ask geometry for every mouse pixel / wheel notch.
        if (_fastSeriesInteraction &&
            _interactionSeriesDrawing is not null &&
            _interactionBaseLayout is TickLayout baseLayout)
        {
            Matrix matrix = CreateInteractionMatrix(baseLayout, layout);
            drawingContext.PushTransform(new MatrixTransform(matrix));
            drawingContext.DrawDrawing(_interactionSeriesDrawing);
            drawingContext.Pop();
            return;
        }

        var key = new TickSeriesCacheKey(
            _tickDataVersion,
            layout.FirstIndex,
            layout.LastExclusive,
            layout.Plot.Width,
            layout.Plot.Height,
            layout.DataRight,
            layout.MinimumPrice,
            layout.MaximumPrice,
            Settings.ShowBidLine,
            Settings.ShowAskLine,
            Settings.ShowMidLine,
            Settings.ShowTickPoints,
            Settings.TickBidColor ?? string.Empty,
            Settings.TickAskColor ?? string.Empty,
            Settings.TickBidThickness,
            Settings.TickAskThickness);

        if (_cachedSeriesKey is not TickSeriesCacheKey existingKey ||
            !existingKey.Equals(key) ||
            _cachedSeriesDrawing is null)
        {
            _cachedSeriesDrawing = BuildSeriesDrawing(
                layout,
                layout.FirstIndex,
                layout.LastExclusive,
                includePoints: true,
                interactionOptimized: false);
            _cachedSeriesKey = key;
        }

        drawingContext.DrawDrawing(_cachedSeriesDrawing);
    }

    private DrawingGroup BuildSeriesDrawing(
        TickLayout layout,
        int rangeFirst,
        int rangeLastExclusive,
        bool includePoints,
        bool interactionOptimized = false)
    {
        rangeFirst = Math.Clamp(rangeFirst, 0, EffectiveTickCount);
        rangeLastExclusive = Math.Clamp(rangeLastExclusive, rangeFirst, EffectiveTickCount);
        var group = new DrawingGroup();
        using (DrawingContext cacheContext = group.Open())
        {
            if (Settings.ShowBidLine)
                DrawSeriesRange(cacheContext, layout, false, _tickBidPen, _tickBidBrush, rangeFirst, rangeLastExclusive, includePoints, interactionOptimized);
            if (Settings.ShowAskLine)
                DrawSeriesRange(cacheContext, layout, true, _tickAskPen, _tickAskBrush, rangeFirst, rangeLastExclusive, includePoints, interactionOptimized);
            if (Settings.ShowMidLine)
                DrawMidSeriesRange(cacheContext, layout, rangeFirst, rangeLastExclusive, interactionOptimized);
        }
        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private void BeginFastSeriesInteraction(TickLayout layout)
    {
        if (_fastSeriesInteraction &&
            _interactionSeriesDrawing is not null &&
            _interactionBaseLayout is not null &&
            _interactionDataVersion == _tickDataVersion)
        {
            return;
        }

        // Keep one viewport of overscan on each side. Most drags therefore reveal
        // already-rendered ticks instead of blank edges while the series is moved.
        int overscan = Math.Max(256, layout.Count);
        int first = Math.Max(0, layout.FirstIndex - overscan);
        int last = Math.Min(EffectiveTickCount, layout.LastExclusive + overscan);
        _interactionSeriesDrawing = BuildSeriesDrawing(
            layout,
            first,
            last,
            includePoints: Settings.ShowTickPoints,
            interactionOptimized: true);
        _interactionBaseLayout = layout;
        _interactionDataVersion = _tickDataVersion;
        _fastSeriesInteraction = true;
    }

    private void ArmWheelInteractionSettle()
    {
        _wheelInteractionSettleTimer.Stop();
        _wheelInteractionSettleTimer.Start();
    }

    private void EndFastSeriesInteraction()
    {
        _wheelInteractionSettleTimer.Stop();
        if (!_fastSeriesInteraction && _interactionSeriesDrawing is null)
            return;
        _fastSeriesInteraction = false;
        _interactionSeriesDrawing = null;
        _interactionBaseLayout = null;
        _interactionDataVersion = -1;
        _cachedSeriesDrawing = null;
        _cachedSeriesKey = null;
        InvalidateVisual();
        ScheduleViewChanged();
    }

    private static Matrix CreateInteractionMatrix(TickLayout source, TickLayout target)
    {
        double sourceStepX = source.DataWidth / Math.Max(1, source.Count - 1);
        double targetStepX = target.DataWidth / Math.Max(1, target.Count - 1);
        double scaleX = targetStepX / Math.Max(0.0000001, sourceStepX);
        double offsetX = target.Plot.Left +
            (source.FirstIndex - target.FirstIndex) * targetStepX -
            scaleX * source.Plot.Left;

        double sourceRange = Math.Max(0.0000000001, source.MaximumPrice - source.MinimumPrice);
        double targetRange = Math.Max(0.0000000001, target.MaximumPrice - target.MinimumPrice);
        double scaleY = sourceRange / targetRange *
            target.Plot.Height / Math.Max(0.0000001, source.Plot.Height);
        double offsetY = target.Plot.Top +
            (target.MaximumPrice - source.MaximumPrice) * target.Plot.Height / targetRange -
            scaleY * source.Plot.Top;

        return new Matrix(scaleX, 0, 0, scaleY, offsetX, offsetY);
    }

    private void DrawSeriesRange(
        DrawingContext drawingContext,
        TickLayout layout,
        bool useAsk,
        Pen pen,
        Brush pointBrush,
        int rangeFirst,
        int rangeLastExclusive,
        bool includePoints,
        bool interactionOptimized = false)
    {
        if (rangeLastExclusive <= rangeFirst)
            return;

        StreamGeometry geometry = BuildSeriesGeometryRange(
            layout, useAsk, false, rangeFirst, rangeLastExclusive, interactionOptimized);
        drawingContext.DrawGeometry(null, pen, geometry);

        if (!includePoints || !Settings.ShowTickPoints)
            return;

        double radius = layout.Count > 5000 ? 1 : 1.8;
        int pointFirst = Math.Max(rangeFirst, layout.FirstIndex);
        int pointLast = Math.Min(rangeLastExclusive, layout.LastExclusive);
        int pointCount = Math.Max(0, pointLast - pointFirst);
        int pointStep = interactionOptimized
            ? Math.Max(1, (int)Math.Ceiling(pointCount / 1000.0))
            : 1;
        for (int index = pointFirst; index < pointLast; index += pointStep)
        {
            Point point = GetPoint(Ticks[index], index, useAsk, layout);
            drawingContext.DrawEllipse(pointBrush, null, point, radius, radius);
        }
    }

    private void DrawMidSeriesRange(
        DrawingContext drawingContext,
        TickLayout layout,
        int rangeFirst,
        int rangeLastExclusive,
        bool interactionOptimized = false)
    {
        if (rangeLastExclusive <= rangeFirst)
            return;
        StreamGeometry geometry = BuildSeriesGeometryRange(
            layout, false, true, rangeFirst, rangeLastExclusive, interactionOptimized);
        drawingContext.DrawGeometry(null, MidPen, geometry);
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        TickLayout layout,
        bool useAsk,
        Pen pen,
        Brush pointBrush)
    {
        if (layout.Count == 0)
            return;

        StreamGeometry geometry = BuildSeriesGeometry(layout, useAsk, useMid: false);
        drawingContext.DrawGeometry(null, pen, geometry);

        if (!Settings.ShowTickPoints)
            return;

        // Point markers are an explicit user option. Keep them exact, but because
        // the whole drawing group is cached they are rebuilt only when the tick
        // data/viewport changes, never for ordinary crosshair/pointer movement.
        double radius = layout.Count > 5000 ? 1 : 1.8;
        for (int index = layout.FirstIndex; index < layout.LastExclusive; index++)
        {
            Point point = GetPoint(Ticks[index], index, useAsk, layout);
            drawingContext.DrawEllipse(pointBrush, null, point, radius, radius);
        }
    }

    private void DrawMidSeries(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        if (layout.Count == 0)
            return;

        StreamGeometry geometry = BuildSeriesGeometry(layout, useAsk: false, useMid: true);
        drawingContext.DrawGeometry(null, MidPen, geometry);
    }

    private StreamGeometry BuildSeriesGeometry(
        TickLayout layout,
        bool useAsk,
        bool useMid) =>
        BuildSeriesGeometryRange(layout, useAsk, useMid, layout.FirstIndex, layout.LastExclusive);

    private StreamGeometry BuildSeriesGeometryRange(
        TickLayout layout,
        bool useAsk,
        bool useMid,
        int rangeFirst,
        int rangeLastExclusive,
        bool interactionOptimized = false)
    {
        var geometry = new StreamGeometry();
        if (rangeLastExclusive <= rangeFirst)
            return geometry;

        using (StreamGeometryContext context = geometry.Open())
        {
            Point firstPoint = GetSeriesPoint(rangeFirst, layout, useAsk, useMid);
            context.BeginFigure(firstPoint, false, false);

            int rangeCount = rangeLastExclusive - rangeFirst;
            int exactThreshold = Math.Max(900, (int)Math.Ceiling(layout.DataWidth * 2.0));
            if (rangeCount <= exactThreshold)
            {
                for (int index = rangeFirst + 1; index < rangeLastExclusive; index++)
                    context.LineTo(GetSeriesPoint(index, layout, useAsk, useMid), true, false);
            }
            else
            {
                // The target bucket count scales with the overscanned width, keeping
                // extrema while bounding WPF segment count during retained rendering.
                double widthMultiplier = Math.Max(1.0, rangeCount / (double)Math.Max(1, layout.Count));
                // During an active gesture the series is only a temporary retained
                // snapshot. Preserve first/min/max/last per bucket, but use fewer
                // buckets so WPF rasterizes far fewer line segments each frame.
                // Once the gesture settles, the normal-density cached geometry is
                // rebuilt immediately, so final chart fidelity is unchanged.
                double bucketDivisor = interactionOptimized ? 5.0 : 2.0;
                int minimumBuckets = interactionOptimized ? 180 : 240;
                int targetBuckets = Math.Max(
                    minimumBuckets,
                    (int)Math.Ceiling(layout.DataWidth * widthMultiplier / bucketDivisor));
                int bucketSize = Math.Max(2, (int)Math.Ceiling(rangeCount / (double)targetBuckets));
                Span<int> candidates = stackalloc int[4];
                int lastAppended = rangeFirst;

                for (int bucketStart = rangeFirst + 1; bucketStart < rangeLastExclusive; bucketStart += bucketSize)
                {
                    int bucketEnd = Math.Min(rangeLastExclusive, bucketStart + bucketSize);
                    int minimumIndex = bucketStart;
                    int maximumIndex = bucketStart;
                    double minimumValue = GetSeriesValue(Ticks[bucketStart], useAsk, useMid);
                    double maximumValue = minimumValue;

                    for (int index = bucketStart + 1; index < bucketEnd; index++)
                    {
                        double value = GetSeriesValue(Ticks[index], useAsk, useMid);
                        if (value < minimumValue) { minimumValue = value; minimumIndex = index; }
                        if (value > maximumValue) { maximumValue = value; maximumIndex = index; }
                    }

                    candidates[0] = bucketStart;
                    candidates[1] = minimumIndex;
                    candidates[2] = maximumIndex;
                    candidates[3] = bucketEnd - 1;
                    for (int outer = 1; outer < candidates.Length; outer++)
                    {
                        int value = candidates[outer];
                        int inner = outer - 1;
                        while (inner >= 0 && candidates[inner] > value)
                        {
                            candidates[inner + 1] = candidates[inner];
                            inner--;
                        }
                        candidates[inner + 1] = value;
                    }

                    for (int candidate = 0; candidate < candidates.Length; candidate++)
                    {
                        int index = candidates[candidate];
                        if (index <= lastAppended)
                            continue;
                        context.LineTo(GetSeriesPoint(index, layout, useAsk, useMid), true, false);
                        lastAppended = index;
                    }
                }

                if (lastAppended < rangeLastExclusive - 1)
                    context.LineTo(GetSeriesPoint(rangeLastExclusive - 1, layout, useAsk, useMid), true, false);
            }
        }
        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }

    private Point GetSeriesPoint(
        int index,
        TickLayout layout,
        bool useAsk,
        bool useMid)
    {
        MarketTick tick = Ticks[index];
        double value = GetSeriesValue(tick, useAsk, useMid);
        return new Point(IndexToX(tick, index, layout), PriceToY(value, layout));
    }

    private static double GetSeriesValue(MarketTick tick, bool useAsk, bool useMid)
    {
        if (useMid)
            return (GetBid(tick) + GetAsk(tick)) / 2.0;
        return useAsk ? GetAsk(tick) : GetBid(tick);
    }

    private void DrawLegend(DrawingContext drawingContext)
    {
        double x = LeftMargin;

        if (Settings.ShowBidLine)
        {
            drawingContext.DrawLine(
                TickBidPen,
                new Point(x, 17),
                new Point(x + 20, 17));
            drawingContext.DrawText(
                CreateText("Bid", 11, TickTextBrush),
                new Point(x + 25, 9));
            x += 70;
        }

        if (Settings.ShowAskLine)
        {
            drawingContext.DrawLine(
                TickAskPen,
                new Point(x, 17),
                new Point(x + 20, 17));
            drawingContext.DrawText(
                CreateText("Ask", 11, TickTextBrush),
                new Point(x + 25, 9));
            x += 70;
        }

        if (Settings.ShowMidLine)
        {
            drawingContext.DrawLine(
                MidPen,
                new Point(x, 17),
                new Point(x + 20, 17));
            drawingContext.DrawText(
                CreateText("Mid", 11, TickTextBrush),
                new Point(x + 25, 9));
        }
    }

    private void DrawLatestPriceLines(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        if (layout.LastExclusive <= layout.FirstIndex)
            return;

        MarketTick tick = Ticks[layout.LastExclusive - 1];
        int digits = GuessDigits();

        if (Settings.ShowBidLine)
            DrawLatestPriceLine(drawingContext, layout, GetBid(tick), TickBidBrush, digits);
        if (Settings.ShowAskLine)
            DrawLatestPriceLine(drawingContext, layout, GetAsk(tick), TickAskBrush, digits);
    }

    private void DrawLatestPriceLine(
        DrawingContext drawingContext,
        TickLayout layout,
        double price,
        Brush brush,
        int digits)
    {
        double y = PriceToY(price, layout);
        if (y < layout.Plot.Top || y > layout.Plot.Bottom)
            return;
        var pen = new Pen(brush, 1.0) { DashStyle = DashStyles.Dash };
        drawingContext.DrawLine(
            pen,
            new Point(layout.Plot.Left, y),
            new Point(layout.Plot.Right, y));

        FormattedText text = CreateText(
            price.ToString($"F{digits}", CultureInfo.InvariantCulture),
            9.0,
            Brushes.White);
        double ticketWidth = Math.Min(RightMargin - 2, text.Width + 8);
        Rect ticket = new(
            layout.Plot.Right + 1,
            y - (text.Height + 6) / 2,
            Math.Max(1, ticketWidth),
            text.Height + 6);
        drawingContext.DrawRectangle(brush, null, ticket);
        drawingContext.DrawText(
            text,
            new Point(ticket.Left + 4, ticket.Top + 3));
    }

    private void DrawPriceScale(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        const int labelCount = 6;
        int digits = GuessDigits();

        for (int index = 0; index <= labelCount; index++)
        {
            double ratio =
                index /
                (double)labelCount;

            double price =
                layout.MaximumPrice -
                (layout.MaximumPrice -
                 layout.MinimumPrice) *
                ratio;

            FormattedText text = CreateText(
                price.ToString(
                    $"F{digits}",
                    CultureInfo.InvariantCulture),
                11,
                _priceScaleTextBrush);

            drawingContext.DrawText(
                text,
                new Point(
                    layout.Plot.Right + 7,
                    layout.Plot.Top +
                    layout.Plot.Height *
                    ratio -
                    text.Height / 2));
        }
    }

    private void DrawTimeScale(
        DrawingContext drawingContext,
        TickLayout layout)
    {
        const int labelCount = 6;

        for (int index = 0; index <= labelCount; index++)
        {
            double ratio =
                index /
                (double)labelCount;

            int tickIndex = Math.Clamp(
                layout.FirstIndex +
                (int)Math.Round(ratio * Math.Max(0, layout.Count - 1)),
                layout.FirstIndex,
                layout.LastExclusive - 1);
            long time = Ticks[tickIndex].TimeMilliseconds;

            TimeSpan visibleSpan = TimeSpan.FromMilliseconds(Math.Max(0, layout.LastTime - layout.FirstTime));
            string format = visibleSpan.TotalDays >= 2
                ? "yyyy-MM-dd HH:mm"
                : visibleSpan.TotalHours >= 1
                    ? "MM-dd HH:mm:ss"
                    : visibleSpan.TotalMinutes >= 1
                        ? "HH:mm:ss"
                        : "HH:mm:ss.fff";
            string value = DateTimeOffset
                .FromUnixTimeMilliseconds(time)
                .ToUniversalTime()
                .ToString(format, CultureInfo.InvariantCulture);

            FormattedText text =
                CreateText(
                    value,
                    10,
                    _timeScaleTextBrush);

            double x =
                layout.Plot.Left +
                layout.DataWidth *
                ratio;

            drawingContext.DrawText(
                text,
                new Point(
                    Math.Clamp(
                        x - text.Width / 2,
                        layout.Plot.Left,
                        layout.Plot.Right -
                        text.Width),
                    layout.Plot.Bottom + 7));
        }
    }

    private void DrawCrosshair(
        DrawingContext drawingContext,
        TickLayout layout,
        Point mouse)
    {
        if (!layout.Plot.Contains(mouse))
            return;

        int tickIndex =
            FindNearestTickIndex(
                layout,
                mouse.X);

        MarketTick tick =
            Ticks[tickIndex];

        Point point =
            GetPoint(
                tick,
                tickIndex,
                false,
                layout);

        drawingContext.DrawLine(
            CrosshairPen,
            new Point(
                point.X,
                layout.Plot.Top),
            new Point(
                point.X,
                layout.Plot.Bottom));

        drawingContext.DrawLine(
            CrosshairPen,
            new Point(
                layout.Plot.Left,
                mouse.Y),
            new Point(
                layout.Plot.Right,
                mouse.Y));

        string numberFormat =
            $"F{GuessDigits()}";

        string details =
            $"{tick.Time.ToUniversalTime():HH:mm:ss.fff}   " +
            $"Bid {GetBid(tick).ToString(numberFormat, CultureInfo.InvariantCulture)}   " +
            $"Ask {GetAsk(tick).ToString(numberFormat, CultureInfo.InvariantCulture)}";

        FormattedText text =
            CreateText(
                details,
                11,
                Brushes.White);

        double left = Math.Clamp(
            point.X - text.Width / 2 - 5,
            layout.Plot.Left,
            layout.Plot.Right -
            text.Width -
            10);

        drawingContext.DrawRectangle(
            Brushes.DimGray,
            null,
            new Rect(
                left,
                layout.Plot.Top + 4,
                text.Width + 10,
                text.Height + 6));

        drawingContext.DrawText(
            text,
            new Point(
                left + 5,
                layout.Plot.Top + 7));
    }

    private Point GetMidPoint(
        MarketTick tick,
        int index,
        TickLayout layout)
    {
        double bid = GetBid(tick);
        double ask = GetAsk(tick);
        double value = (bid + ask) / 2.0;

        return new Point(
            IndexToX(tick, index, layout),
            PriceToY(value, layout));
    }

    private Point GetPoint(
        MarketTick tick,
        int index,
        bool useAsk,
        TickLayout layout)
    {
        double value =
            useAsk
                ? GetAsk(tick)
                : GetBid(tick);

        return new Point(
            IndexToX(tick, index, layout),
            PriceToY(value, layout));
    }

    private static double IndexToX(
        MarketTick tick,
        int index,
        TickLayout layout)
    {
        // A raw Tick Chart is sequence-based, not elapsed-time based. Every tick
        // occupies one horizontal slot. Market closures or quiet milliseconds must
        // never create giant visual holes or compress dense bursts into one pixel.
        int visibleIndex = index - layout.FirstIndex;
        return
            layout.Plot.Left +
            visibleIndex /
            (double)Math.Max(1, layout.Count - 1) *
            layout.DataWidth;
    }


    // Shared nearest-timestamp lookup used by viewport navigation and the
    // Find Tick marker. The loaded raw-tick sequence is time ordered, so a
    // binary search avoids any linear scan even for large historical pages.
    private int FindTickIndexByTimestamp(long timeMilliseconds)
    {
        int count = EffectiveTickCount;
        if (count <= 0)
            return -1;

        int low = 0;
        int high = count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_ticks[middle].TimeMilliseconds < timeMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }

        int index = Math.Clamp(low, 0, count - 1);
        if (index > 0)
        {
            long previousDistance = Math.Abs(_ticks[index - 1].TimeMilliseconds - timeMilliseconds);
            long currentDistance = Math.Abs(_ticks[index].TimeMilliseconds - timeMilliseconds);
            if (previousDistance <= currentDistance)
                index--;
        }

        return index;
    }

    private int FindNearestTickIndex(
        TickLayout layout,
        double x)
    {
        double ratio =
            (Math.Min(x, layout.DataRight) - layout.Plot.Left) /
            Math.Max(1.0, layout.DataWidth);
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        int offset = (int)Math.Round(ratio * Math.Max(0, layout.Count - 1));
        return Math.Clamp(layout.FirstIndex + offset, layout.FirstIndex, layout.LastExclusive - 1);
    }

    private static double GetBid(MarketTick tick) =>
        tick.Bid > 0
            ? tick.Bid
            : tick.DisplayPrice;

    private static double GetAsk(MarketTick tick) =>
        tick.Ask > 0
            ? tick.Ask
            : tick.DisplayPrice;

    private static double PriceToY(
        double price,
        TickLayout layout)
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

    private int GuessDigits() => Math.Clamp(PriceDigits, 0, 10);

    private void DrawCenteredMessage(
        DrawingContext drawingContext,
        string message)
    {
        FormattedText text =
            CreateText(
                message,
                15,
                TickTextBrush);

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

    private void RaiseViewChanged() => ScheduleViewChanged();

    private void ScheduleViewChanged()
    {
        RequestOlderHistoryIfNeeded(force: false);
        RequestNewerHistoryIfNeeded(force: false);
        if (_viewChangedScheduled)
            return;
        _viewChangedScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _viewChangedScheduled = false;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void RequestOlderHistoryIfNeeded(bool force)
    {
        if (!CanRequestOlderHistory || _olderHistoryRequestPending || EffectiveTickCount == 0)
            return;
        EnsureHorizontalViewport();
        int threshold = Math.Max(12, Math.Max(1, _viewCount / 8));
        if (!force && _viewStart > threshold)
            return;
        _olderHistoryRequestPending = true;
        OlderHistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestNewerHistoryIfNeeded(bool force)
    {
        if (!CanRequestNewerHistory || _newerHistoryRequestPending || EffectiveTickCount == 0)
            return;
        EnsureHorizontalViewport();
        int maximumStart = Math.Max(0, EffectiveTickCount - _viewCount);
        int remaining = maximumStart - _viewStart;
        int threshold = Math.Max(12, Math.Max(1, _viewCount / 8));
        if (!force && remaining > threshold)
            return;
        _newerHistoryRequestPending = true;
        NewerHistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private readonly record struct TickLayoutCacheKey(
        int DataVersion,
        int EffectiveCount,
        int FirstIndex,
        int LastExclusive,
        double Width,
        double Height,
        bool VerticalAuto,
        double ManualMinimum,
        double ManualMaximum,
        bool ShowBid,
        bool ShowAsk,
        bool ShowMid,
        bool HistoricalWindow,
        bool AtLatest);

    private readonly record struct TickSeriesCacheKey(
        int DataVersion,
        int FirstIndex,
        int LastExclusive,
        double PlotWidth,
        double PlotHeight,
        double DataRight,
        double MinimumPrice,
        double MaximumPrice,
        bool ShowBid,
        bool ShowAsk,
        bool ShowMid,
        bool ShowPoints,
        string BidColor,
        string AskColor,
        double BidThickness,
        double AskThickness);

    private enum DragMode
    {
        None,
        Plot,
        PriceScale,
        TimeScale
    }

    private readonly record struct TickLayout(
        Rect Plot,
        double DataRight,
        int FirstIndex,
        int LastExclusive,
        int Count,
        double MinimumPrice,
        double MaximumPrice,
        long FirstTime,
        long LastTime)
    {
        public double DataWidth => Math.Max(1.0, DataRight - Plot.Left);
    }
}

public readonly record struct TickFindViewportState(
    int VisibleCount,
    bool VerticalAuto,
    double VerticalSpan);

public sealed record TickDrawingViewportSnapshot(
    Rect Plot,
    double DataRight,
    int FirstIndex,
    int LastExclusive,
    int Count,
    double MinimumPrice,
    double MaximumPrice,
    int DataVersion,
    int IndexOriginVersion);
