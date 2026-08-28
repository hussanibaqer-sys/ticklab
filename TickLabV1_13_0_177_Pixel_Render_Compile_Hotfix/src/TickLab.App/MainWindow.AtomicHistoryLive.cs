using TickLab.Core.Market;

namespace TickLab.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Keeps the complete indexed chart window authoritative when the latest
    /// bridge candle arrives. A stale one-candle runtime list must never replace
    /// a larger valid snapshot already owned by the chart or its context.
    /// </summary>
    private void ReconcileActiveHistoryBeforeLiveMerge(Candle incoming)
    {
        ChartRuntimeContext context = ActiveChartContext;
        if (!string.Equals(context.Symbol, incoming.Symbol, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_requestedSymbol, incoming.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        List<Candle> authoritative = SelectAuthoritativeDisplayHistory(context, includeActiveGlobals: true);
        if (authoritative.Count == 0)
            return;

        if (_displayCandles.Count < authoritative.Count)
            _displayCandles = authoritative.ToList();

        if (IsDirectNative(_activeTimeframe) && _sourceCandles.Count < _displayCandles.Count)
            _sourceCandles = _displayCandles.ToList();

        if (context.DisplayCandles.Count < _displayCandles.Count)
            context.DisplayCandles = _displayCandles.ToList();
        if (IsDirectNative(context.Timeframe) && context.SourceCandles.Count < context.DisplayCandles.Count)
            context.SourceCandles = context.DisplayCandles.ToList();

        EnsureDistinctActiveCandleLists();
        EnsureDistinctContextCandleLists(context);

        if (!SyntheticChartBuilder.IsSynthetic(context.Settings.ChartType) &&
            context.Chart.Candles.Count < _displayCandles.Count)
        {
            context.Chart.ReplaceDataKeepingViewport(_displayCandles);
        }
    }

    /// <summary>
    /// Synchronizes all owners immediately after a successful indexed-history
    /// load. This closes the former gap where the visible chart was full but the
    /// runtime context still contained a stale one-candle source snapshot.
    /// </summary>
    private void CommitLoadedHistoryToActiveContext()
    {
        ChartRuntimeContext context = ActiveChartContext;
        context.Symbol = _requestedSymbol;
        context.Timeframe = _activeTimeframe;
        context.SourceCandles = _sourceCandles.ToList();
        context.DisplayCandles = _displayCandles.ToList();
        context.AllOlderHistoryLoaded = _allOlderHistoryLoaded;
        context.AllNewerHistoryLoaded = _allNewerHistoryLoaded;
        context.CandleRevision++;
        EnsureDistinctActiveCandleLists();
        EnsureDistinctContextCandleLists(context);
    }

    /// <summary>
    /// Repairs a non-active context from whichever same-context owner currently
    /// has the largest valid indexed window before applying a new bridge candle.
    /// </summary>
    private void ReconcileChartContextBeforeLiveMerge(ChartRuntimeContext context)
    {
        List<Candle> authoritative = SelectAuthoritativeDisplayHistory(context, includeActiveGlobals: false);
        if (authoritative.Count == 0)
            return;

        if (context.DisplayCandles.Count < authoritative.Count)
            context.DisplayCandles = authoritative.ToList();

        if (IsDirectNative(context.Timeframe) && context.SourceCandles.Count < context.DisplayCandles.Count)
            context.SourceCandles = context.DisplayCandles.ToList();

        EnsureDistinctContextCandleLists(context);
    }

    /// <summary>
    /// Enforces the invariant that source and display never share one mutable
    /// List instance. The contents may be identical for a native timeframe, but
    /// the owners must remain independent so source-tail repair cannot clear the
    /// visible history.
    /// </summary>
    private void EnsureDistinctActiveCandleLists()
    {
        if (ReferenceEquals(_sourceCandles, _displayCandles))
            _sourceCandles = _displayCandles.ToList();

        ChartRuntimeContext context = ActiveChartContext;
        context.SourceCandles = _sourceCandles;
        context.DisplayCandles = _displayCandles;
    }

    private static void EnsureDistinctContextCandleLists(ChartRuntimeContext context)
    {
        if (ReferenceEquals(context.SourceCandles, context.DisplayCandles))
            context.SourceCandles = context.DisplayCandles.ToList();
    }

    private List<Candle> SelectAuthoritativeDisplayHistory(
        ChartRuntimeContext context,
        bool includeActiveGlobals)
    {
        string displayTimeframe = context.Timeframe.NativeMt5Code ?? context.Timeframe.DisplayText;
        var candidates = new List<IReadOnlyList<Candle>>
        {
            context.DisplayCandles
        };

        if (IsDirectNative(context.Timeframe))
            candidates.Add(context.SourceCandles);

        if (includeActiveGlobals)
        {
            candidates.Add(_displayCandles);
            if (IsDirectNative(context.Timeframe))
                candidates.Add(_sourceCandles);
        }

        // CandleChart.Candles is a rendered synthetic series for Renko/Kagi/etc.
        // It is safe as an authority only for non-synthetic chart visual types.
        if (!SyntheticChartBuilder.IsSynthetic(context.Settings.ChartType))
            candidates.Add(context.Chart.Candles);

        List<Candle> best = new();
        foreach (IReadOnlyList<Candle> candidate in candidates)
        {
            if (!IsSameChartHistory(candidate, context.Symbol))
                continue;

            List<Candle> validated = BuildValidatedCandleSnapshot(
                    candidate,
                    context.Symbol,
                    displayTimeframe)
                .ToList();
            if (validated.Count > best.Count ||
                (validated.Count == best.Count && validated.Count > 0 &&
                 validated[^1].StartUnix > best[^1].StartUnix))
            {
                best = validated;
            }
        }

        return best;
    }

    private static bool IsSameChartHistory(IReadOnlyList<Candle> candles, string symbol)
    {
        if (candles.Count == 0 || string.IsNullOrWhiteSpace(symbol))
            return false;

        Candle first = candles[0];
        Candle last = candles[^1];
        return string.Equals(first.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(last.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
               first.StartUnix <= last.StartUnix;
    }
}
