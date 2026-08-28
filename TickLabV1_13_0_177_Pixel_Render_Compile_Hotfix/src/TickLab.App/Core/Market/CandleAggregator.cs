using System.Globalization;

namespace TickLab.Core.Market;

public static class CandleAggregator
{
    public static IReadOnlyList<Candle> Aggregate(
        IReadOnlyList<Candle> source,
        TimeframeDefinition target,
        int serverUtcOffsetMinutes = 0)
    {
        if (source.Count == 0)
            return Array.Empty<Candle>();

        var result = new List<Candle>();
        CandleAccumulator? accumulator = null;

        foreach (Candle candle in source)
        {
            long bucketStart = target.GetBucketStartUnix(candle.StartUnix, serverUtcOffsetMinutes);
            long bucketEnd = target.GetBucketEndUnix(bucketStart, serverUtcOffsetMinutes);

            if (accumulator is null || accumulator.StartUnix != bucketStart)
            {
                if (accumulator is not null)
                    result.Add(accumulator.ToCandle(target));

                accumulator = new CandleAccumulator(
                    candle,
                    bucketStart,
                    bucketEnd);
            }
            else
            {
                accumulator.Add(candle);
            }
        }

        if (accumulator is not null)
            result.Add(accumulator.ToCandle(target));

        return result;
    }

    public static IReadOnlyList<Candle> ReplaceTail(
        IReadOnlyList<Candle> source,
        IReadOnlyList<Candle> existingAggregated,
        TimeframeDefinition target,
        long changedSourceStartUnix,
        int serverUtcOffsetMinutes = 0)
    {
        if (source.Count == 0)
            return Array.Empty<Candle>();

        long bucketStart = target.GetBucketStartUnix(changedSourceStartUnix, serverUtcOffsetMinutes);
        int sourceIndex = LowerBound(source, bucketStart);
        int existingIndex = LowerBound(existingAggregated, bucketStart);

        var result = new List<Candle>(
            existingIndex + Math.Max(1, source.Count - sourceIndex));

        for (int index = 0; index < existingIndex; index++)
            result.Add(existingAggregated[index]);

        IReadOnlyList<Candle> tail = Aggregate(
            new CandleSlice(source, sourceIndex),
            target,
            serverUtcOffsetMinutes);

        result.AddRange(tail);
        return result;
    }

    public static int ReplaceTailInPlace(
        IReadOnlyList<Candle> source,
        List<Candle> existingAggregated,
        TimeframeDefinition target,
        long changedSourceStartUnix,
        int serverUtcOffsetMinutes = 0)
    {
        if (source.Count == 0)
        {
            int removed = existingAggregated.Count;
            existingAggregated.Clear();
            return -removed;
        }

        long bucketStart =
            target.GetBucketStartUnix(changedSourceStartUnix, serverUtcOffsetMinutes);
        int sourceIndex =
            LowerBound(source, bucketStart);
        int existingIndex =
            LowerBound(existingAggregated, bucketStart);
        int previousCount =
            existingAggregated.Count;

        if (existingIndex < existingAggregated.Count)
        {
            existingAggregated.RemoveRange(
                existingIndex,
                existingAggregated.Count - existingIndex);
        }

        IReadOnlyList<Candle> tail =
            Aggregate(
                new CandleSlice(source, sourceIndex),
                target,
                serverUtcOffsetMinutes);

        existingAggregated.AddRange(tail);
        return existingAggregated.Count - previousCount;
    }

    private static int LowerBound(
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

    private sealed class CandleAccumulator
    {
        private readonly string _symbol;
        private readonly int _digits;
        private readonly double _point;
        private double _open;
        private double _high;
        private double _low;
        private double _close;
        private long _tickVolume;
        private int _spread;
        private long _realVolume;
        private bool _allClosed;

        public CandleAccumulator(
            Candle first,
            long startUnix,
            long endUnix)
        {
            _symbol = first.Symbol;
            _digits = first.Digits;
            _point = first.Point;
            StartUnix = startUnix;
            EndUnix = endUnix;
            _open = first.Open;
            _high = first.High;
            _low = first.Low;
            _close = first.Close;
            _tickVolume = first.TickVolume;
            _spread = first.Spread;
            _realVolume = first.RealVolume;
            _allClosed = first.IsClosed;
        }

        public long StartUnix { get; }
        public long EndUnix { get; }

        public void Add(Candle candle)
        {
            _high = Math.Max(_high, candle.High);
            _low = Math.Min(_low, candle.Low);
            _close = candle.Close;
            _tickVolume += candle.TickVolume;
            _spread = candle.Spread;
            _realVolume += candle.RealVolume;
            _allClosed &= candle.IsClosed;
        }

        public Candle ToCandle(TimeframeDefinition target)
        {
            bool isClosed = _allClosed;

            string startText = DateTimeOffset
                .FromUnixTimeSeconds(StartUnix)
                .ToUniversalTime()
                .ToString(
                    "yyyy.MM.dd HH:mm:ss",
                    CultureInfo.InvariantCulture);

            return new Candle(
                _symbol,
                target.DisplayText,
                _digits,
                _point,
                StartUnix,
                EndUnix,
                startText,
                _open,
                _high,
                _low,
                _close,
                _tickVolume,
                _spread,
                _realVolume,
                isClosed);
        }
    }

    private sealed class CandleSlice : IReadOnlyList<Candle>
    {
        private readonly IReadOnlyList<Candle> _source;
        private readonly int _offset;

        public CandleSlice(
            IReadOnlyList<Candle> source,
            int offset)
        {
            _source = source;
            _offset = Math.Clamp(offset, 0, source.Count);
        }

        public int Count => _source.Count - _offset;

        public Candle this[int index] => _source[_offset + index];

        public IEnumerator<Candle> GetEnumerator()
        {
            for (int index = _offset; index < _source.Count; index++)
                yield return _source[index];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
