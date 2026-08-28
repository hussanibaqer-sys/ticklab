using System.Globalization;
using TickLab.Core.Market;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private DateTime _lastAllChartRecentSecondsWriteUtc = DateTime.MinValue;
    private DateTime _lastAllChartRecentM1WriteUtc = DateTime.MinValue;
    private DateTime _lastAllChartIntegritySweepUtc = DateTime.MinValue;

    private async Task RepairAllChartContextsFromRollingSecondsAsync()
    {
        if (_selectedConnector is null || _isClosing)
            return;

        DateTime now = DateTime.UtcNow;
        string connectorId = _selectedConnector.ConnectorId;
        DateTime secondsWrite = _bridgeClient.GetRecentSecondsLastWriteUtc(connectorId);
        DateTime m1Write = _bridgeClient.GetRecentM1LastWriteUtc(connectorId);
        bool secondsChanged = secondsWrite > _lastAllChartRecentSecondsWriteUtc;
        bool m1Changed = m1Write > _lastAllChartRecentM1WriteUtc;
        if ((!secondsChanged && !m1Changed) &&
            now - _lastAllChartIntegritySweepUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        // Cap this safety sweep so many indicators/charts cannot turn the
        // 100 ms live loop into repeated rolling-window reads.
        if (now - _lastAllChartIntegritySweepUtc < TimeSpan.FromMilliseconds(900))
            return;

        _lastAllChartIntegritySweepUtc = now;
        (IReadOnlyList<Candle> Seconds, IReadOnlyList<Candle> M1) windows =
            await Task.Run(() =>
            {
                IReadOnlyList<Candle> seconds = secondsWrite == DateTime.MinValue
                    ? Array.Empty<Candle>()
                    : BuildValidatedCandleSnapshot(
                        _bridgeClient.ReadRecentSecondCandles(connectorId),
                        expectedSymbol: null,
                        expectedTimeframe: "1s");
                IReadOnlyList<Candle> m1 = m1Write == DateTime.MinValue
                    ? Array.Empty<Candle>()
                    : BuildValidatedCandleSnapshot(
                        _bridgeClient.ReadRecentM1Candles(connectorId),
                        expectedSymbol: null,
                        expectedTimeframe: "PERIOD_M1");
                return (seconds, m1);
            }, _lifetime.Token);

        _lastAllChartRecentSecondsWriteUtc = secondsWrite;
        _lastAllChartRecentM1WriteUtc = m1Write;
        if (windows.Seconds.Count == 0 && windows.M1.Count == 0)
            return;

        int offset = _selectedConnector.ServerUtcOffsetMinutes;
        foreach (ChartRuntimeContext context in _chartContexts.Values.ToArray())
        {
            bool allNewerHistoryLoaded = context.PaneId == _activePricePaneId
                ? _allNewerHistoryLoaded
                : context.AllNewerHistoryLoaded;
            if (IsReplayChart(context.PaneId) || context.Timeframe.IsRawTickChart || !allNewerHistoryLoaded ||
                string.IsNullOrWhiteSpace(context.Symbol))
            {
                continue;
            }

            IReadOnlyList<Candle> sourceWindow = context.Timeframe.UsesTickArchive
                ? windows.Seconds
                : windows.M1;
            if (sourceWindow.Count == 0)
                continue;

            Candle[] symbolWindow = sourceWindow
                .Where(candle => string.Equals(candle.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (symbolWindow.Length == 0)
                continue;

            string sourceTimeframe = context.Timeframe.UsesTickArchive ? "1s" : "PERIOD_M1";
            if (!RepairContextTailFromSource(
                    context,
                    symbolWindow,
                    sourceTimeframe,
                    offset,
                    out int appended,
                    out bool structuralChange))
            {
                continue;
            }

            if (structuralChange)
                context.CandleRevision++;
            context.LastIntegrityRepairUtc = now;
            context.Chart.ReplaceDataKeepingViewport(context.DisplayCandles, Math.Max(0, appended));
            RefreshAppliedIndicatorsForContext(context, force: false);
            RefreshBuiltInIndicatorsForContext(context, force: false);
            EvaluateLiveAlerts(context);
        }
    }

    private bool RepairContextTailFromSource(
        ChartRuntimeContext context,
        IReadOnlyList<Candle> sourceWindow,
        string sourceTimeframe,
        int serverOffset,
        out int appended,
        out bool structuralChange)
    {
        appended = 0;
        structuralChange = false;
        if (context.Timeframe.IsRawTickChart || sourceWindow.Count == 0)
            return false;

        bool active = context.PaneId == _activePricePaneId;
        if (active)
        {
            ReconcileActiveHistoryBeforeLiveMerge(sourceWindow[^1]);
            EnsureDistinctActiveCandleLists();
        }
        else
        {
            ReconcileChartContextBeforeLiveMerge(context);
            EnsureDistinctContextCandleLists(context);
        }

        List<Candle> display = active ? _displayCandles : context.DisplayCandles;
        List<Candle> source = active ? _sourceCandles : context.SourceCandles;
        if (display.Count == 0 && context.Chart.Candles.Count > 0)
            display.AddRange(context.Chart.Candles);

        // A rolling bridge file is only an integrity tail. It is never a chart
        // history source. In particular, do not let a restored/empty chart be
        // initialized as a 30-minute chart window before its indexed local
        // history has loaded.
        if (display.Count == 0)
            return false;

        bool directOneSecond = context.Timeframe.UsesTickArchive && context.Timeframe.Quantity == 1;
        bool directM1 = !context.Timeframe.UsesTickArchive &&
                        context.Timeframe.Unit == TimeframeUnit.Minute &&
                        context.Timeframe.Quantity == 1;
        List<Candle> projected = directOneSecond || directM1
            ? sourceWindow.ToList()
            : CandleAggregator.Aggregate(sourceWindow, context.Timeframe, serverOffset).ToList();

        // A rolling window may start inside the first target bucket. Always
        // discard that partial left edge, even when it is the only projected
        // bucket; replacing a complete H1/D1 candle with 31 minutes is corruption.
        if (projected.Count > 0 && projected[0].StartUnix < sourceWindow[0].StartUnix)
            projected.RemoveAt(0);

        string displayTimeframe = context.Timeframe.NativeMt5Code ?? context.Timeframe.DisplayText;
        projected = BuildValidatedCandleSnapshot(projected, context.Symbol, displayTimeframe).ToList();
        if (projected.Count == 0)
            return false;

        // Preserve every candle that belongs to the indexed chart window. A
        // rolling window can overlap/repair the existing tail or append newer
        // buckets, but it must never insert an earlier rolling prefix and then
        // become the apparent complete history of this chart.
        long loadedHistoryStart = display[0].StartUnix;
        int firstEligibleProjected = LowerBoundByStart(projected, loadedHistoryStart);
        if (firstEligibleProjected > 0)
            projected.RemoveRange(0, firstEligibleProjected);
        if (projected.Count == 0)
            return false;

        long replaceStart = Math.Max(loadedHistoryStart, projected[0].StartUnix);
        int previousCount = display.Count;
        long previousLastStart = display.Count == 0 ? long.MinValue : display[^1].StartUnix;
        bool previousLastClosed = display.Count > 0 && display[^1].IsClosed;
        int displayIndex = LowerBoundByStart(display, replaceStart);
        bool materiallyDifferent = displayIndex >= display.Count ||
            !CandleSequenceMatches(display, displayIndex, projected);
        if (!materiallyDifferent)
            return false;

        if (displayIndex < display.Count)
            display.RemoveRange(displayIndex, display.Count - displayIndex);
        display.AddRange(projected);
        NormalizeCandleListInPlace(display, context.Symbol, displayTimeframe);
        TrimContextList(display);
        appended = Math.Max(0, display.Count - previousCount);
        structuralChange = display.Count != previousCount ||
                           replaceStart < previousLastStart ||
                           (replaceStart == previousLastStart && previousLastClosed);

        List<Candle> safeSource = BuildValidatedCandleSnapshot(
            sourceWindow,
            context.Symbol,
            sourceTimeframe).ToList();
        if (context.Timeframe.UsesTickArchive || !IsDirectNative(context.Timeframe))
        {
            if (safeSource.Count > 0)
            {
                int sourceIndex = LowerBoundByStart(source, safeSource[0].StartUnix);
                if (sourceIndex < source.Count)
                    source.RemoveRange(sourceIndex, source.Count - sourceIndex);
                source.AddRange(safeSource);
                NormalizeCandleListInPlace(source, context.Symbol, sourceTimeframe);
                TrimContextList(source);
            }
        }
        else
        {
            // Native minute-or-larger contexts own candles in their requested
            // timeframe. Do not mix M1 source records into that native list.
            List<Candle> nativeSourceReplacement = display.ToList();
            source.Clear();
            source.AddRange(nativeSourceReplacement);
        }

        context.DisplayCandles = display;
        context.SourceCandles = source;
        context.AllNewerHistoryLoaded = active ? _allNewerHistoryLoaded : context.AllNewerHistoryLoaded;
        if (active)
        {
            _displayCandles = display;
            _sourceCandles = source;
        }
        return true;
    }

    private static bool CandleSequenceMatches(
        IReadOnlyList<Candle> existing,
        int existingIndex,
        IReadOnlyList<Candle> replacement)
    {
        int existingCount = existing.Count - existingIndex;
        if (existingCount != replacement.Count)
            return false;
        for (int index = 0; index < replacement.Count; index++)
        {
            Candle left = existing[existingIndex + index];
            Candle right = replacement[index];
            if (left.StartUnix != right.StartUnix || left.EndUnix != right.EndUnix ||
                left.Open != right.Open || left.High != right.High || left.Low != right.Low ||
                left.Close != right.Close || left.TickVolume != right.TickVolume ||
                left.IsClosed != right.IsClosed)
            {
                return false;
            }
        }
        return true;
    }

    private void NormalizeContextCandleState(ChartRuntimeContext context)
    {
        NormalizeCandleListInPlace(context.DisplayCandles, context.Symbol, context.Timeframe.NativeMt5Code ?? context.Timeframe.DisplayText);
        if (!ReferenceEquals(context.SourceCandles, context.DisplayCandles))
        {
            string sourceTimeframe = context.Timeframe.UsesTickArchive
                ? "1s"
                : context.Timeframe.SourceMt5Code;
            NormalizeCandleListInPlace(context.SourceCandles, context.Symbol, sourceTimeframe);
        }
    }

    private static void NormalizeCandleListInPlace(
        List<Candle> candles,
        string? expectedSymbol,
        string? expectedTimeframe)
    {
        IReadOnlyList<Candle> safe = BuildValidatedCandleSnapshot(candles, expectedSymbol, expectedTimeframe);
        candles.Clear();
        candles.AddRange(safe);
    }

    private static IReadOnlyList<Candle> BuildValidatedCandleSnapshot(
        IEnumerable<Candle> candles,
        string? expectedSymbol,
        string? expectedTimeframe)
    {
        var byStart = new SortedDictionary<long, Candle>();
        foreach (Candle source in candles)
        {
            if (!TryNormalizeCandle(source, expectedSymbol, expectedTimeframe, out Candle normalized))
                continue;
            if (!byStart.TryGetValue(normalized.StartUnix, out Candle? existing) ||
                PreferReplacement(existing, normalized))
            {
                byStart[normalized.StartUnix] = normalized;
            }
        }
        return byStart.Values.ToArray();
    }

    private static bool TryNormalizeCandle(
        Candle source,
        string? expectedSymbol,
        string? expectedTimeframe,
        out Candle normalized)
    {
        normalized = source;
        if (source.StartUnix < 0 || source.EndUnix <= source.StartUnix ||
            !double.IsFinite(source.Open) || !double.IsFinite(source.High) ||
            !double.IsFinite(source.Low) || !double.IsFinite(source.Close))
        {
            return false;
        }

        double high = Math.Max(Math.Max(source.High, source.Low), Math.Max(source.Open, source.Close));
        double low = Math.Min(Math.Min(source.Low, source.High), Math.Min(source.Open, source.Close));
        double point = double.IsFinite(source.Point) && source.Point > 0 ? source.Point : 1e-8;
        string symbol = string.IsNullOrWhiteSpace(expectedSymbol) ? source.Symbol : expectedSymbol;
        string timeframe = string.IsNullOrWhiteSpace(expectedTimeframe) ? source.Timeframe : expectedTimeframe;
        string startText = string.IsNullOrWhiteSpace(source.StartText)
            ? DateTimeOffset.FromUnixTimeSeconds(source.StartUnix).ToUniversalTime()
                .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture)
            : source.StartText;
        normalized = source with
        {
            Symbol = symbol,
            Timeframe = timeframe,
            Digits = Math.Clamp(source.Digits, 0, 10),
            Point = point,
            StartText = startText,
            High = high,
            Low = low,
            TickVolume = Math.Max(0, source.TickVolume),
            Spread = Math.Max(0, source.Spread),
            RealVolume = Math.Max(0, source.RealVolume)
        };
        return true;
    }

    private static bool PreferReplacement(Candle existing, Candle candidate)
    {
        if (candidate.IsClosed != existing.IsClosed)
            return candidate.IsClosed;
        if (candidate.TickVolume != existing.TickVolume)
            return candidate.TickVolume > existing.TickVolume;
        return candidate.EndUnix >= existing.EndUnix;
    }

    private static bool TryCreateSafeIndicatorSnapshot(
        ChartRuntimeContext context,
        out Candle[] candles,
        out int revision,
        out long lastStartUnix)
    {
        revision = context.CandleRevision;
        IReadOnlyList<Candle> source = context.Chart.Candles.Count > 0
            ? context.Chart.Candles
            : context.DisplayCandles;
        candles = BuildValidatedCandleSnapshot(
                source,
                context.Symbol,
                context.Timeframe.NativeMt5Code ?? context.Timeframe.DisplayText)
            .ToArray();
        lastStartUnix = candles.Length == 0 ? 0 : candles[^1].StartUnix;
        return candles.Length > 0;
    }
}
