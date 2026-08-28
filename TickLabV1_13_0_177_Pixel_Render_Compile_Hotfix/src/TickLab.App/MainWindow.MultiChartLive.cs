using System.Globalization;
using TickLab.Core.Market;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private sealed record MultiChartLiveSnapshot(
        DateTime LiveWriteUtc,
        DateTime ClosedWriteUtc,
        Candle? Closed,
        Candle? Live,
        bool IsSecondStream);

    /// <summary>
    /// Projects the one bridge stream into every non-replay chart context for
    /// the same symbol. Each chart owns its own candle lists, so selecting a
    /// pane no longer transfers live-update ownership away from the other panes.
    /// </summary>
    private async Task RefreshAllChartContextsLiveAsync()
    {
        if (_selectedConnector is null)
            return;

        string connectorId = _selectedConnector.ConnectorId;
        MultiChartLiveSnapshot? snapshot = await ReadMultiChartLiveSnapshotAsync(connectorId);
        if (snapshot is not null)
        {
            Candle? identity = snapshot.Live ?? snapshot.Closed;
            if (identity is not null && !string.IsNullOrWhiteSpace(identity.Symbol))
            {
                int serverOffset = _selectedConnector.ServerUtcOffsetMinutes;
                UpdateReplayHiddenLiveState(snapshot, identity, serverOffset);

                ChartRuntimeContext[] targets = _chartContexts.Values
                    .Where(context =>
                        context.PaneId != _activePricePaneId &&
                        !context.Timeframe.IsRawTickChart &&
                        !IsReplayChart(context.PaneId) &&
                        context.AllNewerHistoryLoaded &&
                        !string.IsNullOrWhiteSpace(context.Symbol) &&
                        (context.DisplayCandles.Count > 0 || context.Chart.Candles.Count > 0) &&
                        string.Equals(context.Symbol.Trim(), identity.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (ChartRuntimeContext context in targets)
                {
                    EnsureContextLiveLists(context);
                    int appended = 0;
                    bool changed = false;
                    if (snapshot.Closed is not null &&
                        string.Equals(snapshot.Closed.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        appended += ProjectBridgeCandleIntoContext(
                            context,
                            snapshot.Closed with { IsClosed = true },
                            serverOffset,
                            snapshot.IsSecondStream);
                        changed = true;
                    }
                    if (snapshot.Live is not null &&
                        string.Equals(snapshot.Live.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        appended += ProjectBridgeCandleIntoContext(
                            context,
                            snapshot.Live with { IsClosed = false },
                            serverOffset,
                            snapshot.IsSecondStream);
                        changed = true;
                    }
                    if (!changed)
                        continue;

                    int displayCountBeforeNormalization = context.DisplayCandles.Count;
                    NormalizeContextCandleState(context);
                    TrimContextLiveLists(context);
                    if (appended > 0 || context.DisplayCandles.Count != displayCountBeforeNormalization)
                        context.CandleRevision++;
                    if (ReferenceEquals(context.Chart.Candles, context.DisplayCandles))
                        context.Chart.RefreshData(Math.Max(0, appended));
                    else
                        context.Chart.ReplaceDataKeepingViewport(context.DisplayCandles, Math.Max(0, appended));
                    EvaluateLiveAlerts(context);
                }
            }
        }

        // The latest-only bridge files are responsive but cannot prove that no
        // intermediate candle was missed while the UI was hidden or busy. The
        // rolling M1/seconds windows repair every chart tail independently.
        await RepairAllChartContextsFromRollingSecondsAsync();
    }

    private async Task<MultiChartLiveSnapshot?> ReadMultiChartLiveSnapshotAsync(string connectorId)
    {
        DateTime liveSecondWrite = _bridgeClient.GetLiveSecondLastWriteUtc(connectorId);
        DateTime closedSecondWrite = _bridgeClient.GetClosedSecondLastWriteUtc(connectorId);
        bool secondChanged =
            liveSecondWrite > _lastMultiChartLiveSecondWriteUtc ||
            closedSecondWrite > _lastMultiChartClosedSecondWriteUtc;

        if (secondChanged)
        {
            MultiChartLiveSnapshot snapshot = await Task.Run(() =>
            {
                Candle? closed = closedSecondWrite > _lastMultiChartClosedSecondWriteUtc
                    ? _bridgeClient.ReadClosedSecondCandle(connectorId)
                    : null;
                Candle? live = liveSecondWrite > _lastMultiChartLiveSecondWriteUtc
                    ? _bridgeClient.ReadLiveSecondCandle(connectorId)
                    : null;
                return new MultiChartLiveSnapshot(
                    liveSecondWrite,
                    closedSecondWrite,
                    closed,
                    live,
                    true);
            }, _lifetime.Token);

            _lastMultiChartLiveSecondWriteUtc = liveSecondWrite;
            _lastMultiChartClosedSecondWriteUtc = closedSecondWrite;
            return snapshot;
        }

        // Fallback for bridge installations where the rolling one-second file
        // is not yet available. Native live candles still keep every chart of
        // that same symbol moving; one-second data takes priority when present.
        DateTime liveNativeWrite = _bridgeClient.GetLiveCandleLastWriteUtc(connectorId);
        DateTime closedNativeWrite = _bridgeClient.GetClosedCandleLastWriteUtc(connectorId);
        bool nativeChanged =
            liveNativeWrite > _lastMultiChartLiveNativeWriteUtc ||
            closedNativeWrite > _lastMultiChartClosedNativeWriteUtc;

        if (!nativeChanged)
            return null;

        MultiChartLiveSnapshot nativeSnapshot = await Task.Run(() =>
        {
            Candle? closed = closedNativeWrite > _lastMultiChartClosedNativeWriteUtc
                ? _bridgeClient.ReadClosedCandle(connectorId)
                : null;
            Candle? live = liveNativeWrite > _lastMultiChartLiveNativeWriteUtc
                ? _bridgeClient.ReadLiveCandle(connectorId)
                : null;
            return new MultiChartLiveSnapshot(
                liveNativeWrite,
                closedNativeWrite,
                closed,
                live,
                false);
        }, _lifetime.Token);

        _lastMultiChartLiveNativeWriteUtc = liveNativeWrite;
        _lastMultiChartClosedNativeWriteUtc = closedNativeWrite;
        return nativeSnapshot;
    }

    private void UpdateReplayHiddenLiveState(
        MultiChartLiveSnapshot snapshot,
        Candle identity,
        int serverOffset)
    {
        ReplayRuntime? runtime = _replay;
        if (runtime is null ||
            string.IsNullOrWhiteSpace(runtime.Context.Symbol) ||
            !string.Equals(
                runtime.Context.Symbol.Trim(),
                identity.Symbol.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ChartRuntimeContext context = runtime.Context;
        List<Candle> replaySource = context.SourceCandles;
        List<Candle> replayDisplay = context.DisplayCandles;
        try
        {
            // Live bridge processing never stops during replay. Project the
            // incoming bridge tail into hidden lists while the visible chart
            // remains owned exclusively by the replay engine.
            context.SourceCandles = runtime.HiddenLiveSourceCandles;
            context.DisplayCandles = runtime.HiddenLiveDisplayCandles;

            if (snapshot.Closed is not null &&
                string.Equals(
                    snapshot.Closed.Symbol,
                    context.Symbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                _ = ProjectBridgeCandleIntoContext(
                    context,
                    snapshot.Closed with { IsClosed = true },
                    serverOffset,
                    snapshot.IsSecondStream);
            }

            if (snapshot.Live is not null &&
                string.Equals(
                    snapshot.Live.Symbol,
                    context.Symbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                _ = ProjectBridgeCandleIntoContext(
                    context,
                    snapshot.Live with { IsClosed = false },
                    serverOffset,
                    snapshot.IsSecondStream);
            }

            TrimContextLiveLists(context);
            runtime.HiddenLiveSourceCandles = context.SourceCandles;
            runtime.HiddenLiveDisplayCandles = context.DisplayCandles;
        }
        finally
        {
            context.SourceCandles = replaySource;
            context.DisplayCandles = replayDisplay;
        }
    }

    private void EnsureContextLiveLists(ChartRuntimeContext context)
    {
        ReconcileChartContextBeforeLiveMerge(context);
        EnsureDistinctContextCandleLists(context);

        if (context.DisplayCandles.Count == 0 && context.Chart.Candles.Count > 0)
            context.DisplayCandles = context.Chart.Candles.ToList();

        if (context.SourceCandles.Count == 0 && !context.Timeframe.UsesTickArchive)
            context.SourceCandles = context.DisplayCandles.ToList();
    }

    private static int ProjectBridgeCandleIntoContext(
        ChartRuntimeContext context,
        Candle incoming,
        int serverOffset,
        bool incomingIsSecond)
    {
        if (context.Timeframe.IsRawTickChart)
            return 0;

        if (context.Timeframe.UsesTickArchive && incomingIsSecond)
        {
            _ = UpsertCandleInPlace(context.SourceCandles, incoming);
            if (context.Timeframe.Quantity == 1)
            {
                bool displayAppended = UpsertCandleInPlace(context.DisplayCandles, incoming);
                return displayAppended ? 1 : 0;
            }

            if (context.DisplayCandles.Count == 0)
            {
                context.DisplayCandles = CandleAggregator.Aggregate(
                    context.SourceCandles,
                    context.Timeframe,
                    serverOffset).ToList();
                return context.DisplayCandles.Count > 0 ? 1 : 0;
            }

            int changed = CandleAggregator.ReplaceTailInPlace(
                context.SourceCandles,
                context.DisplayCandles,
                context.Timeframe,
                incoming.StartUnix,
                serverOffset);
            // RefreshData's appended-count parameter must describe display
            // candles only. Counting each new source second would otherwise
            // auto-scroll a 15s/30s/45s chart once per second.
            return Math.Max(0, changed);
        }

        Candle projected = BuildProjectedContextCandle(
            context.DisplayCandles,
            incoming,
            context.Timeframe,
            serverOffset);

        ClosePreviousContextBucket(context.DisplayCandles, projected.StartUnix);
        bool appended = UpsertCandleInPlace(context.DisplayCandles, projected);

        // Native contexts use their requested timeframe as their source list.
        // Keeping the source tail synchronized prevents an immediate stale
        // rollback when the user activates that chart.
        if (IsDirectNative(context.Timeframe))
        {
            ClosePreviousContextBucket(context.SourceCandles, projected.StartUnix);
            _ = UpsertCandleInPlace(context.SourceCandles, projected);
        }

        return appended ? 1 : 0;
    }

    private static Candle BuildProjectedContextCandle(
        IReadOnlyList<Candle> current,
        Candle incoming,
        TimeframeDefinition target,
        int serverOffset)
    {
        long bucketStart = target.GetBucketStartUnix(incoming.StartUnix, serverOffset);
        long bucketEnd = target.GetBucketEndUnix(bucketStart, serverOffset);
        Candle? existing = current.Count > 0 && current[^1].StartUnix == bucketStart
            ? current[^1]
            : null;

        double price = incoming.Close;
        bool bucketClosed = incoming.IsClosed && incoming.EndUnix >= bucketEnd;
        if (existing is not null)
        {
            return existing with
            {
                Digits = incoming.Digits,
                Point = incoming.Point,
                EndUnix = bucketEnd,
                Close = price,
                High = Math.Max(existing.High, Math.Max(incoming.High, price)),
                Low = Math.Min(existing.Low, Math.Min(incoming.Low, price)),
                TickVolume = Math.Max(existing.TickVolume + 1, incoming.TickVolume),
                Spread = incoming.Spread,
                RealVolume = Math.Max(existing.RealVolume, incoming.RealVolume),
                IsClosed = bucketClosed
            };
        }

        return new Candle(
            incoming.Symbol,
            target.NativeMt5Code ?? target.DisplayText,
            incoming.Digits,
            incoming.Point,
            bucketStart,
            bucketEnd,
            DateTimeOffset.FromUnixTimeSeconds(bucketStart)
                .ToUniversalTime()
                .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
            price,
            Math.Max(price, incoming.High),
            Math.Min(price, incoming.Low),
            price,
            Math.Max(1, incoming.TickVolume),
            incoming.Spread,
            incoming.RealVolume,
            bucketClosed);
    }

    private static void ClosePreviousContextBucket(List<Candle> candles, long nextStartUnix)
    {
        if (candles.Count == 0)
            return;

        Candle previous = candles[^1];
        if (previous.StartUnix < nextStartUnix && !previous.IsClosed)
            candles[^1] = previous with { IsClosed = true };
    }

    private static void TrimContextLiveLists(ChartRuntimeContext context)
    {
        TrimContextList(context.DisplayCandles);
        if (!ReferenceEquals(context.SourceCandles, context.DisplayCandles))
            TrimContextList(context.SourceCandles);
    }

    private static void TrimContextList(List<Candle> candles)
    {
        if (candles.Count <= ChartWindowMaximumRecords)
            return;
        candles.RemoveRange(0, candles.Count - ChartWindowMaximumRecords);
    }
}
