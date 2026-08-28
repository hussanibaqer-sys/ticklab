using System.Globalization;
using TickLab.Core.Market;

namespace TickLab.Core.Replay;

public sealed class MarketReplayEngine
{
    private readonly string _symbol;
    private readonly TimeframeDefinition _timeframe;
    private readonly int _digits;
    private readonly double _point;
    private readonly int _serverUtcOffsetMinutes;
    private readonly List<Candle> _candles;
    private ReplayCandleBuilder? _current;
    private readonly Stack<ReplayUndoState> _undo = new();

    public MarketReplayEngine(
        string symbol,
        TimeframeDefinition timeframe,
        int digits,
        double point,
        int serverUtcOffsetMinutes,
        IEnumerable<Candle> completedCandles)
    {
        _symbol = symbol;
        _timeframe = timeframe;
        _digits = Math.Max(0, digits);
        _point = point > 0 ? point : Math.Pow(10, -Math.Max(0, digits));
        _serverUtcOffsetMinutes = serverUtcOffsetMinutes;
        _candles = (completedCandles ?? Array.Empty<Candle>())
            .OrderBy(item => item.StartUnix)
            .GroupBy(item => item.StartUnix)
            .Select(group => group.Last() with { IsClosed = true })
            .ToList();
    }

    public IReadOnlyList<Candle> Candles => _candles;
    public MarketTick? LastTick { get; private set; }
    public bool LastTickClosedCandle { get; private set; }
    public long? CurrentCandleStartUnix => _current?.StartUnix;
    public bool CanUndo => _undo.Count > 0;
    public long? PreviousTickTimeMilliseconds => _undo.Count > 0
        ? _undo.Peek().PreviousLastTick?.TimeMilliseconds
        : null;

    public void Process(MarketTick tick)
    {
        bool previousLastTickClosedCandle = LastTickClosedCandle;
        LastTickClosedCandle = false;
        double price = tick.DisplayPrice;
        if (!double.IsFinite(price) || price <= 0)
            return;

        _undo.Push(new ReplayUndoState(
            _candles.Count,
            _candles.Count > 0 ? _candles[^1] : null,
            _current?.Clone(),
            LastTick,
            previousLastTickClosedCandle,
            tick));

        long bucketStart = _timeframe.GetBucketStartUnix(
            tick.TimeUnix,
            _serverUtcOffsetMinutes);
        long bucketEnd = _timeframe.GetBucketEndUnix(
            bucketStart,
            _serverUtcOffsetMinutes);
        int spread = tick.Bid > 0 && tick.Ask > 0 && _point > 0
            ? Math.Max(0, (int)Math.Round((tick.Ask - tick.Bid) / _point))
            : 0;

        if (_current is null || _current.StartUnix != bucketStart)
        {
            if (_current is not null)
            {
                ReplaceOrAppend(_current.ToCandle(closed: true));
                LastTickClosedCandle = true;
            }

            _current = new ReplayCandleBuilder(
                _symbol,
                _timeframe.DisplayText,
                _digits,
                _point,
                bucketStart,
                bucketEnd,
                price,
                spread,
                tick);
        }
        else
        {
            _current.Add(price, spread, tick);
        }

        ReplaceOrAppend(_current.ToCandle(closed: false));
        LastTick = tick;
    }

    public void CompleteCurrentCandle()
    {
        if (_current is null)
            return;
        ReplaceOrAppend(_current.ToCandle(closed: true));
        _current = null;
        LastTickClosedCandle = true;
    }

    public bool TryUndoLastTick(out MarketTick undoneTick)
    {
        undoneTick = default;
        if (_undo.Count == 0)
            return false;

        ReplayUndoState state = _undo.Pop();
        undoneTick = state.ProcessedTick;

        while (_candles.Count > state.CandleCount)
            _candles.RemoveAt(_candles.Count - 1);

        if (state.CandleCount > 0 && state.PreviousLastCandle is not null)
        {
            if (_candles.Count == state.CandleCount)
                _candles[^1] = state.PreviousLastCandle;
            else if (_candles.Count == state.CandleCount - 1)
                _candles.Add(state.PreviousLastCandle);
        }

        _current = state.PreviousCurrent?.Clone();
        LastTick = state.PreviousLastTick;
        LastTickClosedCandle = state.PreviousLastTickClosedCandle;
        return true;
    }


    private void ReplaceOrAppend(Candle candle)
    {
        if (_candles.Count == 0)
        {
            _candles.Add(candle);
            return;
        }

        if (_candles[^1].StartUnix == candle.StartUnix)
        {
            _candles[^1] = candle;
            return;
        }

        if (_candles[^1].StartUnix < candle.StartUnix)
        {
            _candles.Add(candle);
            return;
        }

        int index = _candles.BinarySearch(
            candle,
            Comparer<Candle>.Create((left, right) => left.StartUnix.CompareTo(right.StartUnix)));
        if (index >= 0)
            _candles[index] = candle;
        else
            _candles.Insert(~index, candle);
    }

    private sealed record ReplayUndoState(
        int CandleCount,
        Candle? PreviousLastCandle,
        ReplayCandleBuilder? PreviousCurrent,
        MarketTick? PreviousLastTick,
        bool PreviousLastTickClosedCandle,
        MarketTick ProcessedTick);

    private sealed class ReplayCandleBuilder
    {
        private readonly string _symbol;
        private readonly string _timeframe;
        private readonly int _digits;
        private readonly double _point;
        private double _open;
        private double _high;
        private double _low;
        private double _close;
        private long _tickVolume;
        private long _realVolume;
        private int _spread;

        public ReplayCandleBuilder(
            string symbol,
            string timeframe,
            int digits,
            double point,
            long startUnix,
            long endUnix,
            double price,
            int spread,
            MarketTick tick)
        {
            _symbol = symbol;
            _timeframe = timeframe;
            _digits = digits;
            _point = point;
            StartUnix = startUnix;
            EndUnix = endUnix;
            _open = _high = _low = _close = price;
            _spread = spread;
            AddVolume(tick);
        }

        public long StartUnix { get; }
        public long EndUnix { get; }

        public void Add(double price, int spread, MarketTick tick)
        {
            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
            _close = price;
            _spread = spread;
            AddVolume(tick);
        }

        private void AddVolume(MarketTick tick)
        {
            _tickVolume++;
            double real = tick.VolumeReal > 0 ? tick.VolumeReal : 0;
            if (real > 0 && double.IsFinite(real))
                _realVolume += Math.Max(0, (long)Math.Round(real));
        }

        public ReplayCandleBuilder Clone()
        {
            var clone = new ReplayCandleBuilder(
                _symbol,
                _timeframe,
                _digits,
                _point,
                StartUnix,
                EndUnix,
                _open,
                _spread,
                default)
            {
                _open = _open,
                _high = _high,
                _low = _low,
                _close = _close,
                _tickVolume = _tickVolume,
                _realVolume = _realVolume,
                _spread = _spread
            };
            return clone;
        }

        public Candle ToCandle(bool closed) =>
            new(
                _symbol,
                _timeframe,
                _digits,
                _point,
                StartUnix,
                EndUnix,
                Mt5ServerClock.ToDisplayTime(StartUnix)
                    .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
                _open,
                _high,
                _low,
                _close,
                _tickVolume,
                _spread,
                _realVolume,
                closed);
    }
}
