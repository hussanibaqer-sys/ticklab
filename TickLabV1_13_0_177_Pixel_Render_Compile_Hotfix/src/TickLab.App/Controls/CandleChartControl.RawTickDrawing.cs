using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Drawing;
using TickLab.Core.Market;

namespace TickLab.Desktop.Controls;

/// <summary>
/// Adapts the existing CandleChartControl drawing engine to the Raw Tick renderer.
/// This is deliberately an adapter, not a second drawing engine: ChartDrawing,
/// tool construction, quick edit, settings, inspector, persistence, lock/hide,
/// middle-click delete, one-select/one-draw and undo/redo remain the same code paths.
/// </summary>
public sealed partial class CandleChartControl
{
    private bool _rawTickDrawingSurface;
    private TickChartControl? _rawTickNavigationTarget;
    private string _rawTickDrawingSymbol = string.Empty;
    private int _rawTickDrawingDigits = 5;
    private double _rawTickDrawingPoint = 0.00001;
    private RawTickCandleView? _rawTickCandleView;
    private int[] _rawTickVisibleSlotsCache = Array.Empty<int>();

    // Completed Tick drawings are expensive because some parity tools inspect raw
    // market data and create substantial WPF geometry/text. Cache each completed
    // object's visual independently. Pointer/crosshair movement then redraws only
    // cached DrawingGroups plus the active preview/selection overlays.
    private readonly Dictionary<string, RawTickDrawingVisualCacheEntry> _rawTickDrawingVisualCache =
        new(StringComparer.Ordinal);
    private bool _rawTickDrawingDataAccessObserved;

    public bool IsRawTickDrawingSurface => _rawTickDrawingSurface;

    private IReadOnlyList<MarketTick> RawTickDrawingTicks =>
        _rawTickNavigationTarget?.Ticks ?? Array.Empty<MarketTick>();

    // Drawing.cs uses this collection instead of the normal Candles collection.
    // In ordinary candle mode it is exactly the original Candles list. In Tick
    // mode it is a lazy compatibility view over raw ticks, so no second object
    // store and no duplicate drawing model is introduced.
    private IReadOnlyList<Candle> DrawingCandles =>
        _rawTickDrawingSurface
            ? (_rawTickCandleView ??= new RawTickCandleView(this))
            : Candles;

    public void EnableRawTickDrawingSurface(
        TickChartControl navigationTarget,
        string symbol,
        int digits,
        double point)
    {
        _rawTickNavigationTarget = navigationTarget;
        _rawTickDrawingSymbol = symbol ?? string.Empty;
        _rawTickDrawingDigits = Math.Clamp(digits, 0, 10);
        _rawTickDrawingPoint = point > 0
            ? point
            : Math.Pow(10.0, -_rawTickDrawingDigits);
        _rawTickDrawingSurface = true;
        // Recreate the compatibility view whenever Tick mode is (re)bound so a
        // symbol/digits/point change can never reuse synthetic candles carrying
        // metadata from the previous binding.
        _rawTickCandleView = new RawTickCandleView(this);
        _rawTickDrawingVisualCache.Clear();
        // Tick drawings use the same timeframe-aware visibility/settings path.
        // Do not mutate the candle chart's stored data/gaps; this overlay is only
        // an alternate coordinate surface over the same drawing engine.
        InvalidateVisual();
    }

    public void DisableRawTickDrawingSurface()
    {
        if (!_rawTickDrawingSurface)
            return;
        _rawTickDrawingSurface = false;
        _rawTickNavigationTarget = null;
        _rawTickCandleView = null;
        _rawTickDrawingVisualCache.Clear();
        _mousePosition = null;
        _dragMode = DragMode.None;
        InvalidateVisual();
    }

    public void RefreshRawTickDrawingSurface()
    {
        if (_rawTickDrawingSurface)
            InvalidateVisual();
    }

    private TickDrawingViewportSnapshot? RawTickDrawingViewport =>
        _rawTickDrawingSurface
            ? _rawTickNavigationTarget?.CaptureDrawingViewport()
            : null;

    private bool TryCreateRawTickDrawingLayout(out ChartLayout layout)
    {
        layout = default;
        TickDrawingViewportSnapshot? snapshot = RawTickDrawingViewport;
        if (snapshot is null || snapshot.Count <= 0 || RawTickDrawingTicks.Count == 0)
            return false;

        double dataWidth = Math.Max(1.0, snapshot.DataRight - snapshot.Plot.Left);
        double slotWidth = dataWidth / Math.Max(1, snapshot.Count);
        double futureWidth = Math.Max(0.0, snapshot.Plot.Right - snapshot.DataRight);
        int futureSlots = (int)Math.Round(futureWidth / Math.Max(0.0001, slotWidth));
        int slotCount = Math.Max(1, snapshot.Count + Math.Max(0, futureSlots));
        IReadOnlyList<int> visibleSlots = GetRawTickVisibleSlots(snapshot.Count);

        layout = new ChartLayout(
            snapshot.Plot,
            snapshot.FirstIndex,
            snapshot.FirstIndex,
            snapshot.LastExclusive,
            snapshot.Count,
            slotCount,
            visibleSlots,
            snapshot.MinimumPrice,
            snapshot.MaximumPrice);
        return true;
    }

    private IReadOnlyList<int> GetRawTickVisibleSlots(int count)
    {
        if (_rawTickVisibleSlotsCache.Length != count)
        {
            _rawTickVisibleSlotsCache = new int[count];
            for (int index = 0; index < count; index++)
                _rawTickVisibleSlotsCache[index] = index;
        }
        return _rawTickVisibleSlotsCache;
    }

    private long DrawingAnchorMilliseconds(DrawingAnchor anchor) =>
        anchor.StartMilliseconds ?? checked(anchor.StartUnix * 1000L);

    private static DrawingAnchor WithDrawingAnchorTime(DrawingAnchor target, DrawingAnchor source) =>
        target with
        {
            StartUnix = source.StartUnix,
            StartMilliseconds = source.StartMilliseconds
        };

    private DrawingAnchor CreateRawTickAnchorAtIndex(int index, double price)
    {
        IReadOnlyList<MarketTick> ticks = RawTickDrawingTicks;
        if (ticks.Count == 0)
            return new DrawingAnchor(0, price);
        index = Math.Clamp(index, 0, ticks.Count - 1);
        MarketTick tick = ticks[index];
        return new DrawingAnchor(tick.TimeUnix, price)
        {
            StartMilliseconds = tick.TimeMilliseconds
        };
    }

    private DrawingAnchor CreateDrawingAnchorAtIndex(int index, double price)
    {
        if (_rawTickDrawingSurface)
            return CreateRawTickAnchorAtIndex(index, price);
        if (Candles.Count == 0)
            return new DrawingAnchor(0, price);
        index = Math.Clamp(index, 0, Candles.Count - 1);
        return new DrawingAnchor(Candles[index].StartUnix, price);
    }

    private int FindNearestRawTickIndex(long milliseconds)
    {
        IReadOnlyList<MarketTick> ticks = RawTickDrawingTicks;
        if (ticks.Count == 0)
            return -1;
        int low = 0;
        int high = ticks.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ticks[middle].TimeMilliseconds < milliseconds)
                low = middle + 1;
            else
                high = middle;
        }
        if (low <= 0)
            return 0;
        if (low >= ticks.Count)
            return ticks.Count - 1;
        long before = ticks[low - 1].TimeMilliseconds;
        long after = ticks[low].TimeMilliseconds;
        return Math.Abs(milliseconds - before) <= Math.Abs(after - milliseconds)
            ? low - 1
            : low;
    }

    private int FindNearestDrawingCandleIndex(long startUnix) =>
        _rawTickDrawingSurface
            ? FindNearestRawTickIndex(checked(startUnix * 1000L))
            : FindNearestCandleIndex(startUnix);

    private int FindNearestDrawingCandleIndex(DrawingAnchor anchor) =>
        _rawTickDrawingSurface
            ? FindNearestRawTickIndex(DrawingAnchorMilliseconds(anchor))
            : FindNearestCandleIndex(anchor.StartUnix);

    private long DrawingPointMilliseconds(int index)
    {
        if (_rawTickDrawingSurface)
        {
            IReadOnlyList<MarketTick> ticks = RawTickDrawingTicks;
            if (ticks.Count == 0)
                return 0;
            return ticks[Math.Clamp(index, 0, ticks.Count - 1)].TimeMilliseconds;
        }
        IReadOnlyList<Candle> candles = Candles;
        if (candles.Count == 0)
            return 0;
        return checked(candles[Math.Clamp(index, 0, candles.Count - 1)].StartUnix * 1000L);
    }

    private double RawTickTimestampToTimelineSlot(long milliseconds)
    {
        int index = FindNearestRawTickIndex(milliseconds);
        return index < 0 ? 0.0 : index;
    }

    private int RawTickIndexFromPlotX(double x, ChartLayout layout)
    {
        IReadOnlyList<MarketTick> ticks = RawTickDrawingTicks;
        TickDrawingViewportSnapshot? snapshot = RawTickDrawingViewport;
        if (ticks.Count == 0 || snapshot is null || snapshot.Count <= 0)
            return 0;

        double dataWidth = Math.Max(1.0, snapshot.DataRight - snapshot.Plot.Left);
        double ratio = (Math.Min(x, snapshot.DataRight) - snapshot.Plot.Left) / dataWidth;
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        int offset = (int)Math.Round(ratio * Math.Max(0, snapshot.Count - 1));
        return Math.Clamp(snapshot.FirstIndex + offset, 0, ticks.Count - 1);
    }

    private double RawTickIndexToX(int index, ChartLayout layout)
    {
        TickDrawingViewportSnapshot? snapshot = RawTickDrawingViewport;
        if (snapshot is null || snapshot.Count <= 0)
            return layout.Plot.Left;
        int clamped = Math.Clamp(index, snapshot.FirstIndex, Math.Max(snapshot.FirstIndex, snapshot.LastExclusive - 1));
        int visibleIndex = clamped - snapshot.FirstIndex;
        double dataWidth = Math.Max(1.0, snapshot.DataRight - snapshot.Plot.Left);
        return snapshot.Plot.Left +
            visibleIndex / (double)Math.Max(1, snapshot.Count - 1) * dataWidth;
    }

    private static string FormatRawTickMeasurementDuration(long totalMilliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, totalMilliseconds));
        if (value.TotalDays >= 1)
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        if (value.TotalSeconds >= 1)
            return value.Milliseconds > 0
                ? $"{value.Seconds}.{value.Milliseconds:000}s"
                : $"{value.Seconds}s";
        return $"{value.TotalMilliseconds:0}ms";
    }

    private DrawingAnchor ShiftRawTickAnchorBySlots(DrawingAnchor anchor, int slotDelta, double priceDelta)
    {
        int index = FindNearestRawTickIndex(DrawingAnchorMilliseconds(anchor));
        if (index < 0)
            return anchor with { Price = anchor.Price + priceDelta };
        int shifted = Math.Clamp(index + slotDelta, 0, Math.Max(0, RawTickDrawingTicks.Count - 1));
        DrawingAnchor moved = CreateRawTickAnchorAtIndex(shifted, anchor.Price + priceDelta);
        return moved with { IndicatorValue = anchor.IndicatorValue };
    }

    private void DrawRawTickSharedDrawingSurface(System.Windows.Media.DrawingContext drawingContext)
    {
        if (!TryCreateRawTickDrawingLayout(out ChartLayout layout))
            return;

        TickDrawingViewportSnapshot? snapshot = RawTickDrawingViewport;
        if (snapshot is null)
            return;

        bool retainedInteraction = IsRawTickRetainedVisualInteractionActive();
        int settingsHash = Settings.GetHashCode();
        PruneRawTickDrawingVisualCache();
        ChartDrawing[] visibleDrawings = VisibleDrawings().ToArray();

        drawingContext.PushClip(new RectangleGeometry(layout.Plot));
        // The normal CandleChart drawing engine remains authoritative. Only its
        // completed-object presentation is retained here; construction, selection,
        // Measure, hit-testing, settings, inspector, persistence and undo/redo keep
        // using the exact same shared engine/state.
        DrawRawTickCompletedDrawingLayer(
            drawingContext, layout, snapshot, visibleDrawings, DrawingVisualLayer.BelowCandles, retainedInteraction, settingsHash);
        DrawRawTickCompletedDrawingLayer(
            drawingContext, layout, snapshot, visibleDrawings, DrawingVisualLayer.AboveCandles, retainedInteraction, settingsHash);
        // Preserve the original shared-engine z-order exactly: construction preview
        // and selection handles belong to the AboveCandles pass, before the
        // AboveIndicators layer.
        DrawRawTickDynamicDrawingOverlay(drawingContext, layout);
        DrawRawTickCompletedDrawingLayer(
            drawingContext, layout, snapshot, visibleDrawings, DrawingVisualLayer.AboveIndicators, retainedInteraction, settingsHash);
        DrawMeasurementOverlay(drawingContext, layout);
        DrawWorkingDrawingOverlay(drawingContext, layout);
        drawingContext.Pop();

        if ((_activeDrawingToolId == "cursor-crosshair" || Settings.ShowCandleCrosshair) &&
            _mousePosition is Point mouse && _dragMode == DragMode.None)
        {
            DrawCrosshair(drawingContext, layout, mouse);
        }
        DrawCursorModeOverlay(drawingContext, layout);
    }

    private bool IsRawTickRetainedVisualInteractionActive() =>
        (_rawTickNavigationTarget?.IsViewportInteractionActive ?? false) ||
        _workingDrawing is not null ||
        _drawingDragMode != DrawingDragMode.None ||
        _measureDragging ||
        _freehandDrawing ||
        _demonstrationCursorDrawing ||
        _drawingSelectionBoxStart.HasValue;


    private void DrawRawTickCompletedDrawingLayer(
        DrawingContext drawingContext,
        ChartLayout layout,
        TickDrawingViewportSnapshot currentViewport,
        IReadOnlyList<ChartDrawing> visibleDrawings,
        DrawingVisualLayer layer,
        bool retainedInteraction,
        int settingsHash)
    {
        foreach (ChartDrawing drawing in visibleDrawings.Where(item => item.VisualLayer == layer))
        {
            // Every completed drawing keeps exact current-data semantics once the
            // chart is settled. During an active gesture, data-version changes are
            // intentionally deferred until the gesture ends so live ticks cannot
            // force all cached objects to rebuild under the pointer.
            int dataVersion = currentViewport.DataVersion;

            bool valid = _rawTickDrawingVisualCache.TryGetValue(
                drawing.Id, out RawTickDrawingVisualCacheEntry? cached) &&
                ReferenceEquals(cached.DrawingIdentity, drawing) &&
                cached.SettingsHash == settingsHash &&
                cached.IndexOriginVersion == currentViewport.IndexOriginVersion &&
                (retainedInteraction || !cached.DependsOnTickData || cached.DataVersion == dataVersion);

            // Outside an active gesture, always rebase a cached visual onto the
            // exact settled viewport. During the gesture, reuse its previous base
            // and transform it cheaply; this mirrors the retained Tick market path.
            if (valid && !retainedInteraction && cached is not null &&
                !RawTickViewportGeometryEquals(cached.BaseViewport, currentViewport))
            {
                valid = false;
            }

            if (!valid || cached is null)
            {
                DrawingGroup visual = BuildRawTickCompletedDrawingVisual(
                    drawing, layout, out bool dependsOnTickData);
                cached = new RawTickDrawingVisualCacheEntry(
                    drawing,
                    settingsHash,
                    dataVersion,
                    currentViewport.IndexOriginVersion,
                    currentViewport,
                    dependsOnTickData,
                    visual);
                _rawTickDrawingVisualCache[drawing.Id] = cached;
            }

            if (retainedInteraction &&
                !RawTickViewportGeometryEquals(cached.BaseViewport, currentViewport))
            {
                Matrix transform = CreateRawTickDrawingInteractionMatrix(cached.BaseViewport, currentViewport);
                drawingContext.PushTransform(new MatrixTransform(transform));
                drawingContext.DrawDrawing(cached.Visual);
                drawingContext.Pop();
            }
            else
            {
                drawingContext.DrawDrawing(cached.Visual);
            }
        }
    }

    private DrawingGroup BuildRawTickCompletedDrawingVisual(
        ChartDrawing drawing,
        ChartLayout layout,
        out bool dependsOnTickData)
    {
        // Detect data-dependent tools automatically instead of maintaining a brittle
        // hand-written tool list. RawTickCandleView marks any Count/index/enumeration
        // access made by the mature renderer. Simple geometric drawings can then
        // remain retained across append-only live ticks, while VWAP/regression/
        // volume/profile/position tools still rebuild when their source data changes.
        _rawTickDrawingDataAccessObserved = false;
        var group = new DrawingGroup();
        using (DrawingContext cacheContext = group.Open())
            DrawOneDrawing(cacheContext, layout, drawing, preview: false);
        dependsOnTickData = _rawTickDrawingDataAccessObserved;
        _rawTickDrawingDataAccessObserved = false;
        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private void DrawRawTickDynamicDrawingOverlay(DrawingContext drawingContext, ChartLayout layout)
    {
        if (_workingDrawing is not null)
        {
            ChartDrawing preview = _workingDrawing;
            if (_previewDrawingAnchor is not null && !_freehandDrawing)
                preview = preview with { Anchors = preview.Anchors.Append(_previewDrawingAnchor).ToArray() };
            DrawOneDrawing(drawingContext, layout, preview, preview: true);
        }

        DrawDrawingSelection(drawingContext, layout);
        if (_drawingSelectionBox.HasValue)
        {
            var boxPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 96, 165, 250)), 1)
            {
                DashStyle = DashStyles.Dash
            };
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(32, 96, 165, 250)),
                boxPen,
                _drawingSelectionBox.Value);
        }
    }

    private void PruneRawTickDrawingVisualCache()
    {
        if (_rawTickDrawingVisualCache.Count == 0 ||
            _rawTickDrawingVisualCache.Count <= _drawings.Count)
        {
            return;
        }
        var liveIds = new HashSet<string>(_drawings.Select(item => item.Id), StringComparer.Ordinal);
        foreach (string staleId in _rawTickDrawingVisualCache.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            _rawTickDrawingVisualCache.Remove(staleId);
    }

    private static bool RawTickViewportGeometryEquals(
        TickDrawingViewportSnapshot left,
        TickDrawingViewportSnapshot right) =>
        left.Plot.Equals(right.Plot) &&
        left.DataRight.Equals(right.DataRight) &&
        left.FirstIndex == right.FirstIndex &&
        left.LastExclusive == right.LastExclusive &&
        left.Count == right.Count &&
        left.MinimumPrice.Equals(right.MinimumPrice) &&
        left.MaximumPrice.Equals(right.MaximumPrice);

    private static Matrix CreateRawTickDrawingInteractionMatrix(
        TickDrawingViewportSnapshot source,
        TickDrawingViewportSnapshot target)
    {
        double sourceWidth = Math.Max(1.0, source.DataRight - source.Plot.Left);
        double targetWidth = Math.Max(1.0, target.DataRight - target.Plot.Left);
        double sourceStepX = sourceWidth / Math.Max(1, source.Count - 1);
        double targetStepX = targetWidth / Math.Max(1, target.Count - 1);
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

    private sealed record RawTickDrawingVisualCacheEntry(
        ChartDrawing DrawingIdentity,
        int SettingsHash,
        int DataVersion,
        int IndexOriginVersion,
        TickDrawingViewportSnapshot BaseViewport,
        bool DependsOnTickData,
        DrawingGroup Visual);

    private bool BeginRawTickNavigation(Point mouse, int clickCount)
    {
        if (!_rawTickDrawingSurface || _rawTickNavigationTarget is null)
            return false;
        bool started = _rawTickNavigationTarget.BeginExternalPointerInteraction(mouse, clickCount);
        if (started)
            CaptureMouse();
        return started;
    }

    private bool UpdateRawTickNavigation(Point mouse, MouseEventArgs e)
    {
        if (!_rawTickDrawingSurface || _rawTickNavigationTarget is null)
            return false;
        return _rawTickNavigationTarget.UpdateExternalPointerInteraction(
            mouse,
            e.LeftButton == MouseButtonState.Pressed);
    }

    private bool EndRawTickNavigation()
    {
        if (!_rawTickDrawingSurface || _rawTickNavigationTarget is null)
            return false;
        bool ended = _rawTickNavigationTarget.EndExternalPointerInteraction();
        if (ended && IsMouseCaptured)
            ReleaseMouseCapture();
        return ended;
    }

    private bool HandleRawTickFindMarkerRightClick(Point mouse)
    {
        if (!_rawTickDrawingSurface || _rawTickNavigationTarget is null)
            return false;
        return _rawTickNavigationTarget.HandleExternalFindMarkerRightClick(mouse, this);
    }

    private sealed class RawTickCandleView : IReadOnlyList<Candle>
    {
        private readonly CandleChartControl _owner;
        private IReadOnlyList<MarketTick>? _cachedSource;
        private Candle?[] _cache = Array.Empty<Candle?>();

        public RawTickCandleView(CandleChartControl owner) => _owner = owner;
        public int Count
        {
            get
            {
                _owner._rawTickDrawingDataAccessObserved = true;
                return _owner.RawTickDrawingTicks.Count;
            }
        }

        public Candle this[int index]
        {
            get
            {
                _owner._rawTickDrawingDataAccessObserved = true;
                IReadOnlyList<MarketTick> ticks = _owner.RawTickDrawingTicks;
                if (index < 0 || index >= ticks.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                EnsureCache(ticks);
                Candle? cached = _cache[index];
                if (cached is not null)
                    return cached;

                MarketTick tick = ticks[index];
                double bid = tick.Bid > 0 ? tick.Bid : tick.DisplayPrice;
                double ask = tick.Ask > 0 ? tick.Ask : bid;
                double price = tick.DisplayPrice;
                double high = Math.Max(price, Math.Max(bid, ask));
                double low = Math.Min(price, Math.Min(bid, ask));
                int spreadPoints = _owner._rawTickDrawingPoint > 0
                    ? (int)Math.Round(Math.Max(0.0, ask - bid) / _owner._rawTickDrawingPoint)
                    : 0;
                cached = new Candle(
                    _owner._rawTickDrawingSymbol,
                    "Tick",
                    _owner._rawTickDrawingDigits,
                    _owner._rawTickDrawingPoint,
                    tick.TimeUnix,
                    tick.TimeUnix,
                    tick.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    price,
                    high,
                    low,
                    price,
                    1,
                    spreadPoints,
                    0,
                    true);
                _cache[index] = cached;
                return cached;
            }
        }

        private void EnsureCache(IReadOnlyList<MarketTick> ticks)
        {
            if (!ReferenceEquals(_cachedSource, ticks))
            {
                _cachedSource = ticks;
                _cache = new Candle?[ticks.Count];
                return;
            }

            if (_cache.Length == ticks.Count)
                return;

            if (ticks.Count > _cache.Length)
            {
                // Raw Tick live/newer paging is append-only on the same List, so
                // preserve all already materialized compatibility candles. Older
                // paging replaces the List instance and therefore takes the reset
                // branch above, preventing index-shift corruption.
                Array.Resize(ref _cache, ticks.Count);
            }
            else
            {
                _cache = new Candle?[ticks.Count];
            }
        }

        public IEnumerator<Candle> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
                yield return this[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
