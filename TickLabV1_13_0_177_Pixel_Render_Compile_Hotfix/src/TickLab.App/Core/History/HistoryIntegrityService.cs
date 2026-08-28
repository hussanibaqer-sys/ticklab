using TickLab.Core.Market;

namespace TickLab.Core.History;

public enum HistorySourceKind
{
    NativeMt5,
    TickLabSaved,
    ExternalTicks,
    ExternalM1,
    Unavailable
}

public sealed record HistorySourceBoundary(
    long StartUnix,
    HistorySourceKind Source,
    string Label);

public sealed record HistoryIntegrityIssue(
    long StartUnix,
    string Code,
    string Message,
    bool IsRepairable);

public sealed record HistoryIntegrityReport(
    long CheckedRecords,
    long ValidRecords,
    IReadOnlyList<HistoryIntegrityIssue> Issues)
{
    public bool Passed => Issues.Count == 0;
}

public static class HistoryIntegrityService
{
    public static HistoryIntegrityReport ValidateCandles(
        IEnumerable<Candle> candles,
        string expectedSymbol,
        string expectedTimeframe)
    {
        var issues = new List<HistoryIntegrityIssue>();
        long checkedRecords = 0;
        long validRecords = 0;
        long previousStart = long.MinValue;

        foreach (Candle candle in candles)
        {
            checkedRecords++;
            bool valid = true;

            if (!string.Equals(candle.Symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "symbol_mismatch",
                    $"Expected {expectedSymbol}, received {candle.Symbol}.",
                    false));
                valid = false;
            }

            if (!string.Equals(candle.Timeframe, expectedTimeframe, StringComparison.Ordinal))
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "timeframe_mismatch",
                    $"Expected {expectedTimeframe}, received {candle.Timeframe}.",
                    false));
                valid = false;
            }

            if (candle.StartUnix <= previousStart)
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    candle.StartUnix == previousStart ? "duplicate" : "out_of_order",
                    "Candle timestamps must be strictly increasing.",
                    true));
                valid = false;
            }

            if (candle.EndUnix <= candle.StartUnix)
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "invalid_time_range",
                    "Candle end time is not after its start time.",
                    true));
                valid = false;
            }

            if (!IsFinite(candle.Open) || !IsFinite(candle.High) ||
                !IsFinite(candle.Low) || !IsFinite(candle.Close) ||
                candle.Point <= 0)
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "invalid_price",
                    "Candle contains a non-finite price or invalid point size.",
                    true));
                valid = false;
            }
            else if (candle.High < candle.Low ||
                     candle.Open < candle.Low || candle.Open > candle.High ||
                     candle.Close < candle.Low || candle.Close > candle.High)
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "invalid_ohlc",
                    "Open and Close must be inside the High-Low range.",
                    true));
                valid = false;
            }

            if (candle.TickVolume < 0 || candle.RealVolume < 0 || candle.Spread < 0)
            {
                issues.Add(new HistoryIntegrityIssue(
                    candle.StartUnix,
                    "invalid_volume_or_spread",
                    "Volume and spread values cannot be negative.",
                    true));
                valid = false;
            }

            if (valid)
                validRecords++;

            previousStart = Math.Max(previousStart, candle.StartUnix);
        }

        return new HistoryIntegrityReport(checkedRecords, validRecords, issues);
    }

    public static IReadOnlyList<Candle> MergeWithPriority(
        IEnumerable<Candle> fallback,
        IEnumerable<Candle> authoritative)
    {
        var merged = new SortedDictionary<long, Candle>();

        foreach (Candle candle in fallback)
            merged[candle.StartUnix] = candle;

        foreach (Candle candle in authoritative)
            merged[candle.StartUnix] = candle;

        return merged.Values.ToArray();
    }

    public static IReadOnlyList<Candle> MergeTimeframeWithNativePriority(
        IEnumerable<Candle> generated,
        IEnumerable<Candle> native,
        TimeframeDefinition timeframe,
        int serverUtcOffsetMinutes)
    {
        // Native MT5 candles own every bucket they cover. Grouping by the
        // canonical bucket prevents a Sunday/Monday W1 timestamp difference
        // from displaying a generated weekly candle beside its native twin.
        var buckets = new SortedDictionary<long, Candle>();
        foreach (Candle candle in generated)
        {
            long bucket = GetCanonicalMergeBucket(candle, timeframe, serverUtcOffsetMinutes);
            buckets[bucket] = candle;
        }
        foreach (Candle candle in native.OrderBy(item => item.StartUnix))
        {
            long bucket = GetCanonicalMergeBucket(candle, timeframe, serverUtcOffsetMinutes);
            buckets[bucket] = candle;
        }
        return buckets.Values.OrderBy(item => item.StartUnix).ToArray();
    }

    private static long GetCanonicalMergeBucket(
        Candle candle,
        TimeframeDefinition timeframe,
        int serverUtcOffsetMinutes)
    {
        // Some brokers expose W1 starts a few hours before the Monday clock
        // boundary while generated W1 candles begin exactly at Monday 00:00.
        // Classifying the weekly candle from a point 36 hours inside it makes
        // both representations resolve to the same broker week without
        // changing the native candle timestamp that is finally displayed.
        long classificationUnix = timeframe.Unit == TimeframeUnit.Week
            ? checked(candle.StartUnix + 36L * 3_600L)
            : candle.StartUnix;
        return timeframe.GetBucketStartUnix(classificationUnix, serverUtcOffsetMinutes);
    }

    public static long? FindFirstNativeBoundary(
        IReadOnlyList<Candle> nativeCandles)
    {
        return nativeCandles.Count == 0
            ? null
            : nativeCandles.Min(candle => candle.StartUnix);
    }


    public static bool CandlePriceAndTimeMatches(
        Candle left,
        Candle right)
    {
        if (left.StartUnix != right.StartUnix || left.EndUnix != right.EndUnix)
            return false;

        double tolerance = Math.Max(left.Point, right.Point) * 0.1;
        return NearlyEqual(left.Open, right.Open, tolerance) &&
               NearlyEqual(left.High, right.High, tolerance) &&
               NearlyEqual(left.Low, right.Low, tolerance) &&
               NearlyEqual(left.Close, right.Close, tolerance);
    }

    public static bool CandleMatches(
        Candle left,
        Candle right)
    {
        if (left.StartUnix != right.StartUnix || left.EndUnix != right.EndUnix)
            return false;

        double tolerance = Math.Max(left.Point, right.Point) * 0.1;
        return NearlyEqual(left.Open, right.Open, tolerance) &&
               NearlyEqual(left.High, right.High, tolerance) &&
               NearlyEqual(left.Low, right.Low, tolerance) &&
               NearlyEqual(left.Close, right.Close, tolerance) &&
               left.TickVolume == right.TickVolume &&
               left.RealVolume == right.RealVolume &&
               left.Spread == right.Spread;
    }

    private static bool NearlyEqual(double left, double right, double tolerance) =>
        Math.Abs(left - right) <= tolerance;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
