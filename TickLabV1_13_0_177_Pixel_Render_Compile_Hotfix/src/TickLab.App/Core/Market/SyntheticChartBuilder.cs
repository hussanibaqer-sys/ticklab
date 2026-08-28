using TickLab.Core.Settings;

namespace TickLab.Core.Market;

public static class SyntheticChartBuilder
{
    public static IReadOnlyList<Candle> Build(
        IReadOnlyList<Candle>? source,
        ChartVisualType chartType,
        ChartSettings settings)
    {
        source ??= Array.Empty<Candle>();
        if (source.Count == 0)
            return Array.Empty<Candle>();

        return chartType switch
        {
            ChartVisualType.HeikinAshi => BuildHeikinAshi(source),
            ChartVisualType.Renko => BuildRenko(source, settings),
            ChartVisualType.LineBreak => BuildLineBreak(source, settings),
            ChartVisualType.Kagi => BuildKagi(source, settings),
            ChartVisualType.PointAndFigure => BuildPointAndFigure(source, settings),
            ChartVisualType.Range => BuildRange(source, settings),
            _ => source
        };
    }

    public static bool IsSynthetic(ChartVisualType chartType) => chartType is
        ChartVisualType.HeikinAshi or
        ChartVisualType.Renko or
        ChartVisualType.LineBreak or
        ChartVisualType.Kagi or
        ChartVisualType.PointAndFigure or
        ChartVisualType.Range;

    private static IReadOnlyList<Candle> BuildHeikinAshi(IReadOnlyList<Candle> source)
    {
        var result = new List<Candle>(source.Count);
        double previousOpen = (source[0].Open + source[0].Close) / 2.0;
        double previousClose = (source[0].Open + source[0].High + source[0].Low + source[0].Close) / 4.0;

        for (int index = 0; index < source.Count; index++)
        {
            Candle item = source[index];
            double close = (item.Open + item.High + item.Low + item.Close) / 4.0;
            double open = index == 0
                ? (item.Open + item.Close) / 2.0
                : (previousOpen + previousClose) / 2.0;
            double high = Math.Max(item.High, Math.Max(open, close));
            double low = Math.Min(item.Low, Math.Min(open, close));
            result.Add(Clone(item, item.StartUnix, open, high, low, close, item.TickVolume, item.RealVolume));
            previousOpen = open;
            previousClose = close;
        }

        return result;
    }

    private static IReadOnlyList<Candle> BuildRenko(IReadOnlyList<Candle> source, ChartSettings settings)
    {
        double box = ResolveSize(source, settings.SyntheticBoxSizePoints);
        int reversalBoxes = Math.Clamp(settings.RenkoReversalBoxes, 1, 10);
        var result = new List<Candle>();
        double lastClose = RoundToBox(source[0].Close, box);
        int direction = 0;
        long timestamp = source[0].StartUnix - 1;

        foreach (Candle item in source)
        {
            double price = item.Close;
            if (direction >= 0)
            {
                while (price >= lastClose + box)
                {
                    double open = lastClose;
                    double close = open + box;
                    timestamp = NextTimestamp(timestamp, item.StartUnix);
                    result.Add(Clone(item, timestamp, open, close, open, close, item.TickVolume, item.RealVolume));
                    lastClose = close;
                    direction = 1;
                }
                if (direction == 0)
                {
                    while (price <= lastClose - box)
                    {
                        double open = lastClose;
                        double close = open - box;
                        timestamp = NextTimestamp(timestamp, item.StartUnix);
                        result.Add(Clone(item, timestamp, open, open, close, close, item.TickVolume, item.RealVolume));
                        lastClose = close;
                        direction = -1;
                    }
                }
                else if (price <= lastClose - box * reversalBoxes)
                {
                    while (price <= lastClose - box)
                    {
                        double open = lastClose;
                        double close = open - box;
                        timestamp = NextTimestamp(timestamp, item.StartUnix);
                        result.Add(Clone(item, timestamp, open, open, close, close, item.TickVolume, item.RealVolume));
                        lastClose = close;
                        direction = -1;
                    }
                }
            }
            else
            {
                while (price <= lastClose - box)
                {
                    double open = lastClose;
                    double close = open - box;
                    timestamp = NextTimestamp(timestamp, item.StartUnix);
                    result.Add(Clone(item, timestamp, open, open, close, close, item.TickVolume, item.RealVolume));
                    lastClose = close;
                }
                if (price >= lastClose + box * reversalBoxes)
                {
                    while (price >= lastClose + box)
                    {
                        double open = lastClose;
                        double close = open + box;
                        timestamp = NextTimestamp(timestamp, item.StartUnix);
                        result.Add(Clone(item, timestamp, open, close, open, close, item.TickVolume, item.RealVolume));
                        lastClose = close;
                        direction = 1;
                    }
                }
            }
        }

        return result.Count > 0 ? result : source.Take(1).ToArray();
    }

    private static IReadOnlyList<Candle> BuildRange(IReadOnlyList<Candle> source, ChartSettings settings)
    {
        double range = ResolveSize(source, settings.RangeBarSizePoints);
        var result = new List<Candle>();
        double open = source[0].Open;
        long timestamp = source[0].StartUnix - 1;

        foreach (Candle item in source)
        {
            double price = item.Close;
            while (Math.Abs(price - open) >= range)
            {
                bool up = price > open;
                double close = open + (up ? range : -range);
                timestamp = NextTimestamp(timestamp, item.StartUnix);
                result.Add(Clone(
                    item,
                    timestamp,
                    open,
                    Math.Max(open, close),
                    Math.Min(open, close),
                    close,
                    item.TickVolume,
                    item.RealVolume));
                open = close;
            }
        }

        return result.Count > 0 ? result : source.Take(1).ToArray();
    }

    private static IReadOnlyList<Candle> BuildLineBreak(IReadOnlyList<Candle> source, ChartSettings settings)
    {
        int breakCount = Math.Clamp(settings.LineBreakCount, 1, 10);
        var result = new List<Candle>();
        Candle first = source[0];
        double firstClose = first.Close;
        double firstOpen = first.Open;
        long timestamp = first.StartUnix;
        result.Add(Clone(first, timestamp, firstOpen, Math.Max(firstOpen, firstClose), Math.Min(firstOpen, firstClose), firstClose, first.TickVolume, first.RealVolume));

        foreach (Candle item in source.Skip(1))
        {
            int start = Math.Max(0, result.Count - breakCount);
            double recentHigh = result.Skip(start).Max(line => Math.Max(line.Open, line.Close));
            double recentLow = result.Skip(start).Min(line => Math.Min(line.Open, line.Close));
            double price = item.Close;
            double previousClose = result[^1].Close;
            if (price > recentHigh)
            {
                timestamp = NextTimestamp(timestamp, item.StartUnix);
                result.Add(Clone(item, timestamp, previousClose, price, Math.Min(previousClose, price), price, item.TickVolume, item.RealVolume));
            }
            else if (price < recentLow)
            {
                timestamp = NextTimestamp(timestamp, item.StartUnix);
                result.Add(Clone(item, timestamp, previousClose, Math.Max(previousClose, price), price, price, item.TickVolume, item.RealVolume));
            }
        }

        return result;
    }

    private static IReadOnlyList<Candle> BuildKagi(IReadOnlyList<Candle> source, ChartSettings settings)
    {
        double reversal = ResolveSize(source, settings.KagiReversalPoints);
        var result = new List<Candle>();
        double segmentOpen = source[0].Close;
        double extreme = segmentOpen;
        int direction = 0;
        Candle segmentSource = source[0];
        long timestamp = source[0].StartUnix - 1;

        foreach (Candle item in source.Skip(1))
        {
            double price = item.Close;
            if (direction == 0)
            {
                if (Math.Abs(price - segmentOpen) < reversal)
                    continue;
                direction = price > segmentOpen ? 1 : -1;
                extreme = price;
                segmentSource = item;
                continue;
            }

            if (direction > 0)
            {
                if (price > extreme)
                {
                    extreme = price;
                    segmentSource = item;
                }
                else if (price <= extreme - reversal)
                {
                    timestamp = NextTimestamp(timestamp, segmentSource.StartUnix);
                    result.Add(Clone(segmentSource, timestamp, segmentOpen, extreme, segmentOpen, extreme, segmentSource.TickVolume, segmentSource.RealVolume));
                    segmentOpen = extreme;
                    extreme = price;
                    segmentSource = item;
                    direction = -1;
                }
            }
            else
            {
                if (price < extreme)
                {
                    extreme = price;
                    segmentSource = item;
                }
                else if (price >= extreme + reversal)
                {
                    timestamp = NextTimestamp(timestamp, segmentSource.StartUnix);
                    result.Add(Clone(segmentSource, timestamp, segmentOpen, segmentOpen, extreme, extreme, segmentSource.TickVolume, segmentSource.RealVolume));
                    segmentOpen = extreme;
                    extreme = price;
                    segmentSource = item;
                    direction = 1;
                }
            }
        }

        if (direction != 0)
        {
            timestamp = NextTimestamp(timestamp, segmentSource.StartUnix);
            result.Add(Clone(
                segmentSource,
                timestamp,
                segmentOpen,
                Math.Max(segmentOpen, extreme),
                Math.Min(segmentOpen, extreme),
                extreme,
                segmentSource.TickVolume,
                segmentSource.RealVolume));
        }

        return result.Count > 0 ? result : source.Take(1).ToArray();
    }

    private static IReadOnlyList<Candle> BuildPointAndFigure(IReadOnlyList<Candle> source, ChartSettings settings)
    {
        double box = ResolveSize(source, settings.SyntheticBoxSizePoints);
        int reversalBoxes = Math.Clamp(settings.PointAndFigureReversalBoxes, 1, 10);
        var result = new List<Candle>();
        double anchor = RoundToBox(source[0].Close, box);
        double high = anchor;
        double low = anchor;
        int direction = 0;
        Candle columnSource = source[0];
        long timestamp = source[0].StartUnix - 1;

        foreach (Candle item in source.Skip(1))
        {
            double price = item.Close;
            if (direction == 0)
            {
                if (price >= anchor + box)
                {
                    direction = 1;
                    high = anchor + Math.Floor((price - anchor) / box) * box;
                    low = anchor;
                    columnSource = item;
                }
                else if (price <= anchor - box)
                {
                    direction = -1;
                    low = anchor - Math.Floor((anchor - price) / box) * box;
                    high = anchor;
                    columnSource = item;
                }
                continue;
            }

            if (direction > 0)
            {
                if (price >= high + box)
                {
                    high += Math.Floor((price - high) / box) * box;
                    columnSource = item;
                }
                else if (price <= high - reversalBoxes * box)
                {
                    timestamp = NextTimestamp(timestamp, columnSource.StartUnix);
                    result.Add(Clone(columnSource, timestamp, low, high, low, high, columnSource.TickVolume, columnSource.RealVolume));
                    direction = -1;
                    double newHigh = high - box;
                    double steps = Math.Max(reversalBoxes, Math.Floor((newHigh - price) / box) + 1);
                    high = newHigh;
                    low = high - (steps - 1) * box;
                    columnSource = item;
                }
            }
            else
            {
                if (price <= low - box)
                {
                    low -= Math.Floor((low - price) / box) * box;
                    columnSource = item;
                }
                else if (price >= low + reversalBoxes * box)
                {
                    timestamp = NextTimestamp(timestamp, columnSource.StartUnix);
                    result.Add(Clone(columnSource, timestamp, high, high, low, low, columnSource.TickVolume, columnSource.RealVolume));
                    direction = 1;
                    double newLow = low + box;
                    double steps = Math.Max(reversalBoxes, Math.Floor((price - newLow) / box) + 1);
                    low = newLow;
                    high = low + (steps - 1) * box;
                    columnSource = item;
                }
            }
        }

        if (direction != 0)
        {
            timestamp = NextTimestamp(timestamp, columnSource.StartUnix);
            result.Add(direction > 0
                ? Clone(columnSource, timestamp, low, high, low, high, columnSource.TickVolume, columnSource.RealVolume)
                : Clone(columnSource, timestamp, high, high, low, low, columnSource.TickVolume, columnSource.RealVolume));
        }

        return result.Count > 0 ? result : source.Take(1).ToArray();
    }

    private static double ResolveSize(IReadOnlyList<Candle> source, int points)
    {
        double point = source.FirstOrDefault()?.Point ?? 0.00001;
        if (!double.IsFinite(point) || point <= 0)
            point = 0.00001;
        return Math.Max(point, point * Math.Clamp(points, 1, 1_000_000));
    }

    private static double RoundToBox(double price, double box) =>
        Math.Round(price / box, MidpointRounding.AwayFromZero) * box;

    private static long NextTimestamp(long previous, long requested) =>
        Math.Max(previous + 1, requested);

    private static Candle Clone(
        Candle source,
        long startUnix,
        double open,
        double high,
        double low,
        double close,
        long tickVolume,
        long realVolume) =>
        new(
            source.Symbol,
            source.Timeframe,
            source.Digits,
            source.Point,
            startUnix,
            Math.Max(startUnix + 1, source.EndUnix),
            DateTimeOffset.FromUnixTimeSeconds(startUnix).ToString("yyyy-MM-dd HH:mm:ss"),
            open,
            Math.Max(high, Math.Max(open, close)),
            Math.Min(low, Math.Min(open, close)),
            close,
            tickVolume,
            source.Spread,
            realVolume,
            source.IsClosed);
}
