using System.Windows;
using System.Windows.Controls;
using TickLab.Core.Market;
using TickLab.Desktop.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private const int RawTickInitialVisibleCount = 1600;
    private const int RawTickReadLimit = 150_000;

    private void WireRawTickChartControl(ChartRuntimeContext context)
    {
        context.TickChart.Settings = context.Settings;
        context.TickChart.PreviewMouseLeftButtonDown += (_, _) => ActivateWorkspacePane(context.PaneId);
        context.TickChart.ViewChanged += (_, _) => context.Chart.RefreshRawTickDrawingSurface();
        context.TickChart.GoToLatestRequested += async (_, _) =>
        {
            if (context.HistoricalNavigationAnchorUnix.HasValue)
            {
                context.HistoricalNavigationAnchorUnix = null;
                context.HistoricalNavigationAnchorSymbol = string.Empty;
                context.Chart.HistoricalNavigationAnchorUnix = null;
                context.TickChart.IsHistoricalWindow = false;
                context.TickChart.ClearFindMarker();
                await LoadRawTickChartAsync(context, resetViewport: true, cancellationToken: _lifetime.Token);
                await RefreshRawTickChartLiveAsync(context, force: true);
            }
            else
            {
                context.TickChart.GoToLatest();
            }
        };
        context.TickChart.OlderHistoryRequested += async (_, _) =>
        {
            try
            {
                await LoadOlderRawTicksAsync(context);
            }
            finally
            {
                context.TickChart.CompleteOlderHistoryRequest();
            }
        };
        context.TickChart.NewerHistoryRequested += async (_, _) =>
        {
            try
            {
                await LoadNewerRawTicksAsync(context);
            }
            finally
            {
                context.TickChart.CompleteNewerHistoryRequest();
            }
        };
    }

    private void SetRawTickMode(ChartRuntimeContext context, bool enabled)
    {
        context.TickChart.Settings = context.Settings;
        if (enabled)
        {
            ConfigureRawTickPriceMetadata(context);
            context.Chart.EnableRawTickDrawingSurface(
                context.TickChart,
                context.Symbol,
                context.TickChart.PriceDigits,
                context.TickChart.PointSize);
        }
        else
        {
            context.Chart.DisableRawTickDrawingSurface();
        }

        if (context.Host is not null)
        {
            context.Host.SetRawTickMode(enabled);
        }
        else
        {
            PrimaryTickChart.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            // In Tick mode this is not a second chart renderer: it is the same
            // established drawing engine running transparently above Raw Tick.
            PrimaryCandleChart.Visibility = Visibility.Visible;
            Panel.SetZIndex(PrimaryTickChart, 0);
            Panel.SetZIndex(PrimaryCandleChart, enabled ? 1 : 0);
            if (enabled)
            {
                PrimaryIndicatorHost.Visibility = Visibility.Collapsed;
                PrimaryIndicatorSplitter.Visibility = Visibility.Collapsed;
                PrimaryIndicatorSplitterRow.Height = new GridLength(0);
                PrimaryIndicatorRow.Height = new GridLength(0);
            }
        }

        if (ReferenceEquals(context, ActiveChartContext))
        {
            ChartCountLabelText.Text = enabled ? "TICKS " : "CANDLES ";
            CandleCountText.Text = enabled
                ? context.TickHistory.Count.ToString("N0")
                : context.DisplayCandles.Count.ToString("N0");
            ChartOhlcText.Text = enabled
                ? "Raw Bid / Ask ticks · drag/zoom scales · scroll left for history"
                : ChartOhlcText.Text;
            if (!enabled)
                ShowIndicatorsForActiveChart();
            UpdateChartTypeButton();
        }
    }


    private void ConfigureRawTickPriceMetadata(ChartRuntimeContext context)
    {
        var liveSymbol = _availableSymbols
            .FirstOrDefault(item => string.Equals(item.Name, context.Symbol, StringComparison.OrdinalIgnoreCase));
        Candle? candle = context.DisplayCandles.LastOrDefault() ?? context.SourceCandles.LastOrDefault();
        int digits = liveSymbol?.Digits
            ?? candle?.Digits
            ?? (_selectedConnector is not null &&
                string.Equals(_selectedConnector.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase)
                    ? _selectedConnector.Digits
                    : 5);
        double point = candle is { Point: > 0 } validCandle
            ? validCandle.Point
            : _selectedConnector is not null &&
              string.Equals(_selectedConnector.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase) &&
              _selectedConnector.Point > 0
                ? _selectedConnector.Point
                : Math.Pow(10.0, -Math.Clamp(digits, 0, 10));
        context.TickChart.PriceDigits = Math.Clamp(digits, 0, 10);
        context.TickChart.PointSize = point > 0 ? point : Math.Pow(10.0, -Math.Clamp(digits, 0, 10));
    }

    private async Task LoadRawTickChartAsync(
        ChartRuntimeContext context,
        bool resetViewport,
        CancellationToken cancellationToken)
    {
        if (_selectedConnector is null || string.IsNullOrWhiteSpace(context.Symbol))
            return;

        string connectorId = _selectedConnector.ConnectorId;
        int serverOffset = _selectedConnector.ServerUtcOffsetMinutes;
        string symbol = context.Symbol;
        CanonicalTickCoverage coverage = await Task.Run(
            () => _historyStore.GetTickCoverageForReplay(connectorId, symbol, cancellationToken),
            CancellationToken.None);

        if (!coverage.HasData)
        {
            // Keep launch fast: synchronize only recent/live raw sources here.
            // Older completed snapshots are read directly on demand when the user
            // pans left, so a large saved history never blocks Tick Chart launch.
            await Task.Run(() => _historyStore.SyncTickArchives(
                connectorId,
                symbol,
                cancellationToken,
                includeHistorical: false,
                serverUtcOffsetMinutes: serverOffset), CancellationToken.None);
            coverage = await Task.Run(
                () => _historyStore.GetTickCoverageForReplay(connectorId, symbol, cancellationToken),
                CancellationToken.None);
        }

        cancellationToken.ThrowIfCancellationRequested();

        MarketTick[] ticks;
        if (coverage.HasData)
        {
            // Read the newest N ticks, not the first N ticks inside an arbitrary time
            // interval. This removes the old ~30-minute projection ceiling and ensures
            // the visible page is contiguous with the live edge regardless of tick rate.
            long endExclusive = coverage.LatestTimeMilliseconds == long.MaxValue
                ? long.MaxValue
                : coverage.LatestTimeMilliseconds + 1;
            CanonicalTickReadResult read = await Task.Run(
                () => _historyStore.ReadTicksBeforeForReplay(
                    connectorId,
                    symbol,
                    endExclusive,
                    RawTickReadLimit,
                    cancellationToken,
                    coverage.EarliestTimeMilliseconds),
                CancellationToken.None);

            ticks = read.Ticks
                .OrderBy(item => item.TimeMilliseconds)
                .ToArray();
        }
        else
        {
            // The permanent raw CSV snapshots are authoritative saved history too.
            // If the canonical cache is missing/short, launch the Tick Chart directly
            // from the newest intact ticks_history_* segment instead of showing an
            // empty chart or forcing a destructive rebuild.
            CanonicalTickCoverage rawCoverage = await Task.Run(
                () => _historyStore.GetBridgeHistoricalTickSourceCoverage(
                    connectorId,
                    symbol,
                    cancellationToken),
                CancellationToken.None);
            if (!rawCoverage.HasData)
            {
                context.TickHistory.Clear();
                context.TickAllNewerLoaded = true;
                context.TickChart.CanRequestOlderHistory = false;
                context.TickChart.CanRequestNewerHistory = false;
                context.TickChart.Ticks = Array.Empty<MarketTick>();
                StatusText.Text = $"No saved raw ticks are available for {symbol}.";
                return;
            }

            long rawEndExclusive = rawCoverage.LatestTimeMilliseconds == long.MaxValue
                ? long.MaxValue
                : rawCoverage.LatestTimeMilliseconds + 1;
            long span = 30L * 60L * 1000L;
            ticks = Array.Empty<MarketTick>();
            for (int attempt = 0; attempt < 5 && ticks.Length == 0; attempt++)
            {
                long rawStart = Math.Max(0L, rawEndExclusive - span);
                CanonicalTickReadResult rawRead = await Task.Run(
                    () => _historyStore.ReadBridgeTicksBeforeForReplayFast(
                        connectorId,
                        symbol,
                        rawStart,
                        rawEndExclusive,
                        RawTickReadLimit,
                        serverOffset,
                        cancellationToken,
                        allowFullIndexRebuild: attempt >= 4),
                    CancellationToken.None);
                ticks = rawRead.Ticks
                    .OrderBy(item => item.TimeMilliseconds)
                    .ToArray();
                span = Math.Min(7L * 24L * 60L * 60L * 1000L, span * 4L);
            }
        }
        context.TickHistory = ticks.ToList();
        context.LastRawTickMilliseconds = ticks.Length == 0 ? 0 : ticks[^1].TimeMilliseconds;
        // Canonical coverage may contain only recent data while completed
        // ticks_history_*.csv snapshots hold much older intact history. Keep the
        // older-history door open; the paging routine probes those raw snapshots
        // directly when the canonical edge is reached.
        context.TickAllOlderLoaded = ticks.Length == 0;
        context.TickAllNewerLoaded = true;
        context.TickChart.CanRequestOlderHistory = false;
        context.TickChart.CanRequestNewerHistory = false;
        context.TickChart.Settings = context.Settings;
        if (!context.HistoricalNavigationAnchorUnix.HasValue)
            context.TickChart.IsHistoricalWindow = false;
        context.TickChart.Ticks = context.TickHistory;
        if (resetViewport && ticks.Length > 0)
            context.TickChart.ShowLatest(Math.Min(RawTickInitialVisibleCount, ticks.Length));
        context.TickChart.CanRequestOlderHistory = ticks.Length > 0;
        context.TickChart.CanRequestNewerHistory = false;
        SetRawTickMode(context, true);
        StatusText.Text = ticks.Length == 0
            ? $"No raw ticks found for {symbol}."
            : $"Raw Tick chart: {symbol} · {ticks.Length:N0} contiguous ticks loaded. Scroll left for older saved ticks.";
    }

    private async Task LoadOlderRawTicksAsync(ChartRuntimeContext context)
    {
        if (_selectedConnector is null || context.TickAllOlderLoaded ||
            !context.Timeframe.IsRawTickChart || string.IsNullOrWhiteSpace(context.Symbol) || context.TickHistory.Count == 0)
        {
            return;
        }

        if (context.TickHistoryLoadRunning)
        {
            // Fast scrolling may request another page while the current disk read
            // is still completing. Remember that intent instead of silently
            // dropping it; the pending request is drained immediately afterward.
            context.TickOlderHistoryRequestPending = true;
            return;
        }

        context.TickHistoryLoadRunning = true;
        try
        {
            string connectorId = _selectedConnector.ConnectorId;
            int serverOffset = _selectedConnector.ServerUtcOffsetMinutes;
            string symbol = context.Symbol;
            long endExclusive = context.TickHistory[0].TimeMilliseconds;
            CanonicalTickCoverage coverage = await Task.Run(
                () => _historyStore.GetTickCoverageForReplay(connectorId, symbol, _lifetime.Token),
                CancellationToken.None);

            MarketTick[] older = Array.Empty<MarketTick>();

            // First take the immediately preceding canonical ticks. Reverse paging
            // guarantees the record limit cannot create a missing middle.
            if (coverage.HasData && endExclusive > coverage.EarliestTimeMilliseconds)
            {
                CanonicalTickReadResult canonical = await Task.Run(
                    () => _historyStore.ReadTicksBeforeForReplay(
                        connectorId,
                        symbol,
                        endExclusive,
                        RawTickReadLimit,
                        _lifetime.Token,
                        coverage.EarliestTimeMilliseconds),
                    CancellationToken.None);
                older = canonical.Ticks
                    .Where(item => item.TimeMilliseconds < endExclusive)
                    .OrderBy(item => item.TimeMilliseconds)
                    .ToArray();
            }

            // If the canonical archive has reached its edge, read the completed MT5
            // history snapshots directly. This is the important fallback for the
            // user's intact history files that have not yet been projected into the
            // canonical _ticks archive.
            if (older.Length == 0)
            {
                long span = 30L * 60L * 1000L;
                for (int attempt = 0; attempt < 6 && older.Length == 0; attempt++)
                {
                    long startMilliseconds = Math.Max(0L, endExclusive - span);
                    CanonicalTickReadResult raw = await Task.Run(
                        () => _historyStore.ReadBridgeTicksBeforeForReplayFast(
                            connectorId,
                            symbol,
                            startMilliseconds,
                            endExclusive,
                            RawTickReadLimit,
                            serverOffset,
                            _lifetime.Token,
                            allowFullIndexRebuild: attempt >= 5),
                        CancellationToken.None);
                    older = raw.Ticks
                        .Where(item => item.TimeMilliseconds < endExclusive)
                        .OrderBy(item => item.TimeMilliseconds)
                        .ToArray();

                    // Cross overnight/weekend market gaps without forcing a full
                    // archive scan on normal scrolling.
                    span = Math.Min(7L * 24L * 60L * 60L * 1000L, span * 4L);
                }
            }

            if (older.Length == 0)
            {
                // One final filename-coverage check is only performed at the true
                // edge, never during launch. It distinguishes "no older history"
                // from an intact raw source that merely sits across a long gap.
                CanonicalTickCoverage rawCoverage = await Task.Run(
                    () => _historyStore.GetBridgeHistoricalTickSourceCoverage(
                        connectorId,
                        symbol,
                        _lifetime.Token),
                    CancellationToken.None);
                context.TickAllOlderLoaded = !rawCoverage.HasData ||
                    rawCoverage.EarliestTimeMilliseconds >= endExclusive;
                context.TickChart.CanRequestOlderHistory = !context.TickAllOlderLoaded;
                return;
            }

            var combined = new List<MarketTick>(older.Length + context.TickHistory.Count);
            combined.AddRange(older);
            combined.AddRange(context.TickHistory);
            context.TickHistory = combined;
            context.TickAllOlderLoaded = false;
            context.TickChart.CanRequestOlderHistory = true;
            context.TickChart.ReplaceTicksKeepingViewport(context.TickHistory, older.Length, followLatest: false);
            if (ReferenceEquals(context, ActiveChartContext))
                CandleCountText.Text = context.TickHistory.Count.ToString("N0");
            StatusText.Text = $"Raw Tick chart: loaded {older.Length:N0} contiguous older ticks for {symbol}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Older raw ticks could not be loaded: {exception.Message}";
        }
        finally
        {
            context.TickHistoryLoadRunning = false;
            DrainPendingRawTickHistoryRequests(context);
        }
    }

    private async Task FindRawTickAsync(MarkerDraft draft)
    {
        Mt5ConnectorSummary? selectedConnector = _selectedConnector;
        if (selectedConnector is null)
        {
            _markerWindow?.SetStatus("No MT5/history connection is selected for Tick Find.");
            return;
        }

        TimeframeDefinition tickTimeframe =
            TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Tick)!;
        string symbol = draft.Symbol.Trim();
        long requestedUnix = draft.RequestedUnix ?? draft.StartUnix;
        long requestedMilliseconds = checked(requestedUnix * 1000L);
        var previousTickViewport =
            ActiveChartContext.TickChart.CaptureFindViewportState();

        // Find first, navigate second — same user-visible contract as normal
        // Find Candle. Do not switch the active chart into an empty Tick window
        // while the historical lookup is still unresolved.
        _markerWindow?.SetStatus("Finding stored tick at the requested server time…");

        MarketTick[] ticks = Array.Empty<MarketTick>();
        CanonicalTickCoverage canonicalCoverage = await Task.Run(
            () => _historyStore.GetTickCoverageForReplay(
                selectedConnector.ConnectorId,
                symbol,
                _lifetime.Token),
            CancellationToken.None);

        long[] halfWindows =
        {
            5_000L,
            30_000L,
            2L * 60L * 1000L,
            10L * 60L * 1000L,
            60L * 60L * 1000L
        };

        for (int attempt = 0; attempt < halfWindows.Length && ticks.Length == 0; attempt++)
        {
            long halfWindow = halfWindows[attempt];
            long sourceStart = Math.Max(0L, requestedMilliseconds - halfWindow);
            long sourceEnd = requestedMilliseconds > long.MaxValue - halfWindow - 1000L
                ? long.MaxValue
                : requestedMilliseconds + halfWindow + 1000L;
            if (sourceEnd <= sourceStart)
                continue;

            // First use the non-persistent targeted/raw lookup. v147 guarantees
            // this cannot poison the complete index used by the protected 1s chart.
            CanonicalTickReadResult read = await Task.Run(
                () => _historyStore.ReadBridgeTicksForReplayFast(
                    selectedConnector.ConnectorId,
                    symbol,
                    sourceStart,
                    sourceEnd,
                    maximumRecords: RawTickReadLimit,
                    serverUtcOffsetMinutes: selectedConnector.ServerUtcOffsetMinutes,
                    cancellationToken: _lifetime.Token,
                    takeLatest: false,
                    allowFullIndexRebuild: false),
                CancellationToken.None);

            // Canonical cache is a second source, not a prerequisite for Tick Find.
            if (read.Ticks.Count == 0 && canonicalCoverage.HasData)
            {
                long archiveStart = Math.Max(canonicalCoverage.EarliestTimeMilliseconds, sourceStart);
                long archiveEnd = Math.Min(canonicalCoverage.LatestTimeMilliseconds + 1, sourceEnd);
                if (archiveEnd > archiveStart)
                {
                    read = await Task.Run(
                        () => _historyStore.ReadTicksForReplay(
                            selectedConnector.ConnectorId,
                            symbol,
                            archiveStart,
                            archiveEnd,
                            RawTickReadLimit,
                            _lifetime.Token),
                        CancellationToken.None);
                }
            }

            ticks = read.Ticks
                .OrderBy(item => item.TimeMilliseconds)
                .ToArray();
        }

        if (ticks.Length == 0)
        {
            // Reliability fallback: force one complete filename-index discovery,
            // then keep the actual data reads target-local. Never use a huge
            // one-hour forward read that could hit the record cap before reaching
            // the requested second on a dense symbol.
            foreach (long halfWindow in halfWindows)
            {
                long sourceStart = Math.Max(0L, requestedMilliseconds - halfWindow);
                long sourceEnd = requestedMilliseconds > long.MaxValue - halfWindow - 1000L
                    ? long.MaxValue
                    : requestedMilliseconds + halfWindow + 1000L;
                CanonicalTickReadResult reliable = await Task.Run(
                    () => _historyStore.ReadBridgeTicksForReplayFast(
                        selectedConnector.ConnectorId,
                        symbol,
                        sourceStart,
                        sourceEnd,
                        maximumRecords: RawTickReadLimit,
                        serverUtcOffsetMinutes: selectedConnector.ServerUtcOffsetMinutes,
                        cancellationToken: _lifetime.Token,
                        takeLatest: false,
                        allowFullIndexRebuild: true),
                    CancellationToken.None);
                ticks = reliable.Ticks
                    .OrderBy(item => item.TimeMilliseconds)
                    .ToArray();
                if (ticks.Length > 0)
                    break;
            }
        }

        if (ticks.Length == 0)
        {
            _markerWindow?.SetStatus(
                "No stored raw tick was found around that server date/time. The chart was not changed.");
            return;
        }

        MarketTick found = ticks
            .FirstOrDefault(item => item.TimeMilliseconds >= requestedMilliseconds &&
                                    item.TimeMilliseconds < requestedMilliseconds + 1000L);
        if (found == default)
            found = ticks.MinBy(item => Math.Abs(item.TimeMilliseconds - requestedMilliseconds));
        if (found == default)
        {
            _markerWindow?.SetStatus("Tick Find could not resolve a stored tick near that time.");
            return;
        }

        int requestedVisibleCount = previousTickViewport.VisibleCount > 0
            ? previousTickViewport.VisibleCount
            : RawTickInitialVisibleCount;
        ticks = await BuildTickFindPresentationWindowAsync(
            selectedConnector,
            symbol,
            found,
            ticks,
            requestedVisibleCount);

        // Only after a valid result exists do we open the Raw Tick chart.
        if (!ActiveChartContext.Timeframe.IsRawTickChart ||
            !string.Equals(ActiveChartContext.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        {
            await SafeSelectChartAsync(symbol, tickTimeframe);
        }

        ChartRuntimeContext context = ActiveChartContext;
        if (!context.Timeframe.IsRawTickChart ||
            !string.Equals(context.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        {
            _markerWindow?.SetStatus("Tick found, but TickLab could not open the Raw Tick chart.");
            return;
        }

        context.TickHistory = ticks.ToList();
        context.LastRawTickMilliseconds = ticks[^1].TimeMilliseconds;
        context.TickAllOlderLoaded = false;
        context.TickAllNewerLoaded = false;
        context.TickChart.Settings = context.Settings;
        context.TickChart.Ticks = context.TickHistory;
        context.TickChart.CanRequestOlderHistory = true;
        context.TickChart.CanRequestNewerHistory = true;
        context.HistoricalNavigationAnchorUnix = requestedUnix;
        context.HistoricalNavigationAnchorSymbol = symbol;
        context.Chart.HistoricalNavigationAnchorUnix = requestedUnix;
        context.TickChart.IsHistoricalWindow = true;
        context.TickChart.SetFindMarker(found.TimeMilliseconds);
        context.TickChart.ShowTimestampPreservingViewport(
            found.TimeMilliseconds,
            previousTickViewport,
            Math.Min(RawTickInitialVisibleCount, ticks.Length));
        SetRawTickMode(context, true);
        context.Chart.RefreshRawTickDrawingSurface();
        CandleCountText.Text = context.TickHistory.Count.ToString("N0");

        string foundTime = DateTimeOffset
            .FromUnixTimeMilliseconds(found.TimeMilliseconds)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        _markerWindow?.SetStatus($"Tick found, centered and marked: {symbol} {foundTime} server time.");
        StatusText.Text = $"Tick Find: {symbol} {foundTime} · historical tick window.";
    }

    private async Task<MarketTick[]> BuildTickFindPresentationWindowAsync(
        Mt5ConnectorSummary connector,
        string symbol,
        MarketTick found,
        IReadOnlyList<MarketTick> searchTicks,
        int visibleCount)
    {
        int sideTarget = Math.Clamp(Math.Max(visibleCount * 3, 5000), 5000, 20_000);
        int localSideTarget = Math.Clamp(
            Math.Max(visibleCount, RawTickInitialVisibleCount),
            RawTickInitialVisibleCount,
            5000);
        long targetMilliseconds = found.TimeMilliseconds;
        string connectorId = connector.ConnectorId;
        int serverOffset = connector.ServerUtcOffsetMinutes;

        // IMPORTANT: the target-local search result is authoritative immediately
        // around the marker.  The old presentation builder discarded these local
        // ticks and replaced each side with the canonical archive.  A partially
        // populated canonical quarter could still return thousands of records
        // while ending hours away from the target, producing one artificial giant
        // line directly beside the found tick.  Keep the local neighborhood and
        // extend it from the same raw-history source first; canonical is fallback
        // only when a side has no target-local/raw data at all.
        MarketTick[] older = searchTicks
            .Where(item => item.TimeMilliseconds < targetMilliseconds)
            .OrderBy(item => item.TimeMilliseconds)
            .TakeLast(localSideTarget)
            .ToArray();
        MarketTick[] newer = searchTicks
            .Where(item => item.TimeMilliseconds > targetMilliseconds)
            .OrderBy(item => item.TimeMilliseconds)
            .Take(localSideTarget)
            .ToArray();

        // Grow the older side from raw bridge history.  A candidate is accepted
        // only when it preserves (or improves) the tick nearest the marker, so a
        // stale/far-away indexed block can never replace a valid local seam.
        if (older.Length < localSideTarget)
        {
            long span = 30L * 60L * 1000L;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                long start = Math.Max(0L, targetMilliseconds - span);
                CanonicalTickReadResult raw = await Task.Run(
                    () => _historyStore.ReadBridgeTicksBeforeForReplayFast(
                        connectorId,
                        symbol,
                        start,
                        targetMilliseconds,
                        localSideTarget,
                        serverOffset,
                        _lifetime.Token,
                        allowFullIndexRebuild: false),
                    CancellationToken.None);
                MarketTick[] candidate = raw.Ticks
                    .Where(item => item.TimeMilliseconds < targetMilliseconds)
                    .OrderBy(item => item.TimeMilliseconds)
                    .TakeLast(localSideTarget)
                    .ToArray();

                if (candidate.Length > 0 &&
                    (older.Length == 0 ||
                     candidate[^1].TimeMilliseconds >= older[^1].TimeMilliseconds))
                {
                    MarketTick[] merged = candidate
                        .Concat(older)
                        .Distinct()
                        .OrderBy(item => item.TimeMilliseconds)
                        .TakeLast(localSideTarget)
                        .ToArray();
                    if (merged.Length > older.Length ||
                        merged[^1].TimeMilliseconds >= older[^1].TimeMilliseconds)
                    {
                        older = merged;
                    }
                }

                if (older.Length >= localSideTarget)
                    break;
                span = Math.Min(7L * 24L * 60L * 60L * 1000L, span * 4L);
            }
        }

        long newerStart = targetMilliseconds == long.MaxValue
            ? long.MaxValue
            : targetMilliseconds + 1L;
        if (newer.Length < localSideTarget && newerStart < long.MaxValue)
        {
            long span = 30L * 60L * 1000L;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                long end = targetMilliseconds > long.MaxValue - span
                    ? long.MaxValue
                    : targetMilliseconds + span;
                if (end <= newerStart)
                    break;

                CanonicalTickReadResult raw = await Task.Run(
                    () => _historyStore.ReadBridgeTicksForReplayFast(
                        connectorId,
                        symbol,
                        newerStart,
                        end,
                        localSideTarget,
                        serverOffset,
                        _lifetime.Token,
                        takeLatest: false,
                        allowFullIndexRebuild: false),
                    CancellationToken.None);
                MarketTick[] candidate = raw.Ticks
                    .Where(item => item.TimeMilliseconds > targetMilliseconds)
                    .OrderBy(item => item.TimeMilliseconds)
                    .Take(localSideTarget)
                    .ToArray();

                if (candidate.Length > 0 &&
                    (newer.Length == 0 ||
                     candidate[0].TimeMilliseconds <= newer[0].TimeMilliseconds))
                {
                    MarketTick[] merged = newer
                        .Concat(candidate)
                        .Distinct()
                        .OrderBy(item => item.TimeMilliseconds)
                        .Take(localSideTarget)
                        .ToArray();
                    if (merged.Length > newer.Length ||
                        merged[0].TimeMilliseconds <= newer[0].TimeMilliseconds)
                    {
                        newer = merged;
                    }
                }

                if (newer.Length >= localSideTarget)
                    break;
                span = Math.Min(7L * 24L * 60L * 60L * 1000L, span * 4L);
            }
        }

        // Canonical data is a fallback only for a side for which the target-local
        // raw source returned nothing.  Never splice a far-away canonical block
        // onto an already valid local neighborhood beside the marker.
        if (older.Length == 0 || newer.Length == 0)
        {
            CanonicalTickCoverage canonicalCoverage = await Task.Run(
                () => _historyStore.GetTickCoverageForReplay(
                    connectorId,
                    symbol,
                    _lifetime.Token),
                CancellationToken.None);

            if (older.Length == 0 &&
                canonicalCoverage.HasData &&
                targetMilliseconds > canonicalCoverage.EarliestTimeMilliseconds)
            {
                CanonicalTickReadResult read = await Task.Run(
                    () => _historyStore.ReadTicksBeforeForReplay(
                        connectorId,
                        symbol,
                        targetMilliseconds,
                        sideTarget,
                        _lifetime.Token,
                        canonicalCoverage.EarliestTimeMilliseconds),
                    CancellationToken.None);
                older = read.Ticks
                    .Where(item => item.TimeMilliseconds < targetMilliseconds)
                    .OrderBy(item => item.TimeMilliseconds)
                    .TakeLast(sideTarget)
                    .ToArray();
            }

            if (newer.Length == 0 &&
                newerStart < long.MaxValue &&
                canonicalCoverage.HasData &&
                newerStart <= canonicalCoverage.LatestTimeMilliseconds)
            {
                CanonicalTickReadResult read = await Task.Run(
                    () => _historyStore.ReadTicksForReplay(
                        connectorId,
                        symbol,
                        newerStart,
                        endMilliseconds: null,
                        maximumRecords: sideTarget,
                        cancellationToken: _lifetime.Token),
                    CancellationToken.None);
                newer = read.Ticks
                    .Where(item => item.TimeMilliseconds > targetMilliseconds)
                    .OrderBy(item => item.TimeMilliseconds)
                    .Take(sideTarget)
                    .ToArray();
            }
        }

        MarketTick[] sameTimestamp = searchTicks
            .Where(item => item.TimeMilliseconds == targetMilliseconds)
            .ToArray();
        if (sameTimestamp.Length == 0)
            sameTimestamp = new[] { found };

        return older
            .Concat(sameTimestamp)
            .Concat(newer)
            .Distinct()
            .OrderBy(item => item.TimeMilliseconds)
            .ToArray();
    }

    private async Task LoadNewerRawTicksAsync(ChartRuntimeContext context)
    {
        if (_selectedConnector is null || context.TickAllNewerLoaded ||
            !context.Timeframe.IsRawTickChart || string.IsNullOrWhiteSpace(context.Symbol) || context.TickHistory.Count == 0)
        {
            return;
        }

        if (context.TickHistoryLoadRunning)
        {
            context.TickNewerHistoryRequestPending = true;
            return;
        }

        context.TickHistoryLoadRunning = true;
        try
        {
            string connectorId = _selectedConnector.ConnectorId;
            int serverOffset = _selectedConnector.ServerUtcOffsetMinutes;
            string symbol = context.Symbol;
            long currentLast = context.TickHistory[^1].TimeMilliseconds;
            long startMilliseconds = currentLast == long.MaxValue ? long.MaxValue : currentLast + 1L;
            if (startMilliseconds == long.MaxValue)
            {
                context.TickAllNewerLoaded = true;
                context.TickChart.CanRequestNewerHistory = false;
                return;
            }

            MarketTick[] newer = Array.Empty<MarketTick>();
            CanonicalTickCoverage coverage = await Task.Run(
                () => _historyStore.GetTickCoverageForReplay(connectorId, symbol, _lifetime.Token),
                CancellationToken.None);

            if (coverage.HasData && startMilliseconds <= coverage.LatestTimeMilliseconds)
            {
                CanonicalTickReadResult canonical = await Task.Run(
                    () => _historyStore.ReadTicksForReplay(
                        connectorId,
                        symbol,
                        startMilliseconds,
                        endMilliseconds: null,
                        maximumRecords: RawTickReadLimit,
                        cancellationToken: _lifetime.Token),
                    CancellationToken.None);
                newer = canonical.Ticks
                    .Where(item => item.TimeMilliseconds > currentLast)
                    .OrderBy(item => item.TimeMilliseconds)
                    .ToArray();
            }

            if (newer.Length == 0)
            {
                long span = 30L * 60L * 1000L;
                for (int attempt = 0; attempt < 6 && newer.Length == 0; attempt++)
                {
                    long endMilliseconds = currentLast > long.MaxValue - span
                        ? long.MaxValue
                        : currentLast + span;
                    CanonicalTickReadResult raw = await Task.Run(
                        () => _historyStore.ReadBridgeTicksForReplayFast(
                            connectorId,
                            symbol,
                            startMilliseconds,
                            endMilliseconds,
                            RawTickReadLimit,
                            serverOffset,
                            _lifetime.Token,
                            takeLatest: false,
                            allowFullIndexRebuild: attempt >= 5),
                        CancellationToken.None);
                    newer = raw.Ticks
                        .Where(item => item.TimeMilliseconds > currentLast)
                        .OrderBy(item => item.TimeMilliseconds)
                        .ToArray();
                    span = Math.Min(7L * 24L * 60L * 60L * 1000L, span * 4L);
                }
            }

            if (newer.Length == 0)
            {
                CanonicalTickCoverage rawCoverage = await Task.Run(
                    () => _historyStore.GetBridgeHistoricalTickSourceCoverage(
                        connectorId,
                        symbol,
                        _lifetime.Token),
                    CancellationToken.None);
                long latestAvailable = coverage.HasData ? coverage.LatestTimeMilliseconds : 0L;
                if (rawCoverage.HasData)
                    latestAvailable = Math.Max(latestAvailable, rawCoverage.LatestTimeMilliseconds);
                context.TickAllNewerLoaded = latestAvailable <= currentLast;
                context.TickChart.CanRequestNewerHistory = !context.TickAllNewerLoaded;
                return;
            }

            // The newer reader is strictly start-after-currentLast and already
            // ordered, so appending is correct. Do not Distinct+sort the entire
            // accumulated TickHistory on every page; that became an O(N log N)
            // UI-thread freeze after scrolling through large histories.
            context.TickHistory.AddRange(newer);
            context.LastRawTickMilliseconds = context.TickHistory[^1].TimeMilliseconds;
            context.TickAllNewerLoaded = false;
            context.TickChart.CanRequestNewerHistory = true;
            context.TickChart.ReplaceTicksKeepingViewport(context.TickHistory, prependedCount: 0, followLatest: false);
            if (ReferenceEquals(context, ActiveChartContext))
                CandleCountText.Text = context.TickHistory.Count.ToString("N0");
            StatusText.Text = $"Raw Tick chart: loaded {newer.Length:N0} contiguous newer ticks for {symbol}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Newer raw ticks could not be loaded: {exception.Message}";
        }
        finally
        {
            context.TickHistoryLoadRunning = false;
            DrainPendingRawTickHistoryRequests(context);
        }
    }

    private void DrainPendingRawTickHistoryRequests(ChartRuntimeContext context)
    {
        if (_isClosing || !context.Timeframe.IsRawTickChart)
        {
            context.TickOlderHistoryRequestPending = false;
            context.TickNewerHistoryRequestPending = false;
            return;
        }

        bool loadOlder = context.TickOlderHistoryRequestPending;
        bool loadNewer = context.TickNewerHistoryRequestPending;
        context.TickOlderHistoryRequestPending = false;
        context.TickNewerHistoryRequestPending = false;

        if (loadOlder && !context.TickAllOlderLoaded)
            _ = LoadOlderRawTicksAsync(context);
        if (loadNewer && !context.TickAllNewerLoaded)
            _ = LoadNewerRawTicksAsync(context);
    }

    private async Task RefreshRawTickChartLiveAsync(ChartRuntimeContext context, bool force = false)
    {
        if (_selectedConnector is null || !context.Timeframe.IsRawTickChart || string.IsNullOrWhiteSpace(context.Symbol))
            return;
        if (context.HistoricalNavigationAnchorUnix.HasValue)
            return;
        if (!force && context.TickChart.IsViewportInteractionActive)
            return;
        DateTime now = DateTime.UtcNow;
        if (!force && now - context.LastRawTickRefreshUtc < TimeSpan.FromMilliseconds(175))
            return;
        context.LastRawTickRefreshUtc = now;

        string connectorId = _selectedConnector.ConnectorId;
        long minimum = Math.Max(0, context.LastRawTickMilliseconds - 1);
        IReadOnlyList<MarketTick> incoming = await Task.Run(
            () => _bridgeClient.ReadLiveRawTicksSince(
                connectorId,
                context.Symbol,
                minimum,
                maximumRecords: 50_000),
            CancellationToken.None);
        if (incoming.Count == 0)
            return;

        bool wasAtLatest = context.TickChart.IsAtLatest;
        List<MarketTick> additions = GetRawTickAdditions(context.TickHistory, incoming, context.LastRawTickMilliseconds);
        if (additions.Count == 0)
            return;
        context.TickHistory.AddRange(additions);
        context.LastRawTickMilliseconds = Math.Max(context.LastRawTickMilliseconds, additions[^1].TimeMilliseconds);
        context.TickChart.ReplaceTicksKeepingViewport(context.TickHistory, prependedCount: 0, followLatest: wasAtLatest);
        if (ReferenceEquals(context, ActiveChartContext))
            CandleCountText.Text = context.TickHistory.Count.ToString("N0");
    }

    private static List<MarketTick> GetRawTickAdditions(
        IReadOnlyList<MarketTick> existing,
        IReadOnlyList<MarketTick> incoming,
        long lastMilliseconds)
    {
        var result = new List<MarketTick>();
        if (incoming.Count == 0)
            return result;

        // The live CSV may append multiple identical ticks at the same
        // millisecond. Compare multiplicities at the overlap timestamp so raw
        // ticks are neither lost nor duplicated when polling the growing file.
        // Existing TickHistory can contain hundreds of thousands of ticks. The
        // previous LINQ Where() walked the entire list on every ~175 ms live poll
        // even though only the final 1 ms overlap can possibly matter. Binary-seek
        // that tiny tail instead; duplicate/multiplicity semantics stay identical.
        var existingCounts = new Dictionary<MarketTick, int>();
        long overlapStart = lastMilliseconds - 1;
        int low = 0;
        int high = existing.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (existing[middle].TimeMilliseconds < overlapStart)
                low = middle + 1;
            else
                high = middle;
        }
        for (int index = low; index < existing.Count; index++)
        {
            MarketTick tick = existing[index];
            existingCounts.TryGetValue(tick, out int count);
            existingCounts[tick] = count + 1;
        }

        bool incomingAlreadyOrdered = true;
        for (int index = 1; index < incoming.Count; index++)
        {
            if (incoming[index].TimeMilliseconds < incoming[index - 1].TimeMilliseconds)
            {
                incomingAlreadyOrdered = false;
                break;
            }
        }
        IEnumerable<MarketTick> orderedIncoming = incomingAlreadyOrdered
            ? incoming
            : incoming.OrderBy(item => item.TimeMilliseconds);

        var seenIncoming = new Dictionary<MarketTick, int>();
        foreach (MarketTick tick in orderedIncoming)
        {
            if (tick.TimeMilliseconds > lastMilliseconds)
            {
                result.Add(tick);
                continue;
            }
            if (tick.TimeMilliseconds < lastMilliseconds - 1)
                continue;
            seenIncoming.TryGetValue(tick, out int seen);
            seen++;
            seenIncoming[tick] = seen;
            existingCounts.TryGetValue(tick, out int already);
            if (seen > already)
                result.Add(tick);
        }
        return result;
    }

    private async Task RefreshAllRawTickContextsLiveAsync()
    {
        ChartRuntimeContext[] targets = _chartContexts.Values
            .Where(context => context.Timeframe.IsRawTickChart && !IsReplayChart(context.PaneId))
            .ToArray();
        foreach (ChartRuntimeContext context in targets)
            await RefreshRawTickChartLiveAsync(context);
    }
}
