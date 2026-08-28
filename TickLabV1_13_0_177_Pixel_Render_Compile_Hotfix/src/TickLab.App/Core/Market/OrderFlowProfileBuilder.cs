using TickLab.Core.Settings;

namespace TickLab.Core.Market;

public static class OrderFlowProfileBuilder
{
    private const uint TickFlagBuy = 32;
    private const uint TickFlagSell = 64;

    public static OrderFlowProfileSnapshot Build(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<MarketTick> ticks,
        ChartSettings settings,
        int serverUtcOffsetMinutes)
    {
        if (candles.Count == 0 || ticks.Count == 0)
            return OrderFlowProfileSnapshot.Empty with { StatusMessage = "No saved tick data is available for this chart range." };

        double point = Math.Max(candles[0].Point, 0.00000001);
        double priceStep = Math.Max(point, point * Math.Clamp(settings.FootprintPriceStepPoints, 1, 1_000_000));
        var footprintBuilders = new Dictionary<long, FootprintBuilder>();
        var sessionBuilders = new Dictionary<long, SessionBuilder>();
        int candleIndex = 0;
        double priorTradePrice = 0;
        TradeSide priorSide = TradeSide.Buy;
        int realVolumeTicks = 0;
        long startMs = long.MaxValue;
        long endMs = long.MinValue;

        foreach (MarketTick tick in ticks.OrderBy(item => item.TimeMilliseconds))
        {
            while (candleIndex < candles.Count && tick.TimeUnix >= candles[candleIndex].EndUnix)
                candleIndex++;
            if (candleIndex >= candles.Count)
                break;

            Candle candle = candles[candleIndex];
            if (tick.TimeUnix < candle.StartUnix)
                continue;

            double volume = tick.VolumeReal;
            if (!double.IsFinite(volume) || volume <= 0)
                continue;

            double price = tick.Last > 0 ? tick.Last : tick.DisplayPrice;
            if (!double.IsFinite(price) || price <= 0)
                continue;

            TradeSide side = ResolveTradeSide(tick, price, priorTradePrice, priorSide, point);
            priorTradePrice = price;
            priorSide = side;
            realVolumeTicks++;
            startMs = Math.Min(startMs, tick.TimeMilliseconds);
            endMs = Math.Max(endMs, tick.TimeMilliseconds);

            double bucketPrice = RoundToStep(price, priceStep);
            if (!footprintBuilders.TryGetValue(candle.StartUnix, out FootprintBuilder? footprint))
            {
                footprint = new FootprintBuilder(candle.StartUnix);
                footprintBuilders[candle.StartUnix] = footprint;
            }
            footprint.Add(bucketPrice, volume, side);

            long sessionStart = GetSessionStartUnix(
                tick.TimeMilliseconds,
                serverUtcOffsetMinutes,
                Math.Clamp(settings.ProfileSessionStartHour, 0, 23));
            if (!sessionBuilders.TryGetValue(sessionStart, out SessionBuilder? session))
            {
                session = new SessionBuilder(sessionStart, sessionStart + 86_400);
                sessionBuilders[sessionStart] = session;
            }
            session.Add(bucketPrice, volume);
        }

        if (realVolumeTicks == 0)
        {
            return OrderFlowProfileSnapshot.Empty with
            {
                StatusMessage = "This chart requires real trade volume, but the connected broker is not providing it."
            };
        }

        IReadOnlyDictionary<long, FootprintCandleProfile> footprints = footprintBuilders
            .ToDictionary(item => item.Key, item => item.Value.Build());
        IReadOnlyList<SessionVolumeProfileData> sessions = sessionBuilders
            .OrderBy(item => item.Key)
            .Select(item => item.Value.Build(Math.Clamp(settings.VolumeProfileValueAreaPercent, 1.0, 99.0)))
            .ToArray();

        return new OrderFlowProfileSnapshot(
            startMs == long.MaxValue ? 0 : startMs,
            endMs == long.MinValue ? 0 : endMs,
            priceStep,
            footprints,
            sessions,
            true,
            realVolumeTicks,
            $"Loaded {realVolumeTicks:N0} real-volume ticks.");
    }

    private static TradeSide ResolveTradeSide(
        MarketTick tick,
        double price,
        double previousPrice,
        TradeSide previousSide,
        double point)
    {
        bool buyFlag = (tick.Flags & TickFlagBuy) != 0;
        bool sellFlag = (tick.Flags & TickFlagSell) != 0;
        if (buyFlag && !sellFlag)
            return TradeSide.Buy;
        if (sellFlag && !buyFlag)
            return TradeSide.Sell;

        double tolerance = Math.Max(point * 0.1, 0.000000001);
        if (tick.Ask > 0 && price >= tick.Ask - tolerance)
            return TradeSide.Buy;
        if (tick.Bid > 0 && price <= tick.Bid + tolerance)
            return TradeSide.Sell;
        if (previousPrice > 0)
        {
            if (price > previousPrice + tolerance)
                return TradeSide.Buy;
            if (price < previousPrice - tolerance)
                return TradeSide.Sell;
        }
        return previousSide;
    }

    private static double RoundToStep(double value, double step) =>
        Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    private static long GetSessionStartUnix(
        long timeMilliseconds,
        int serverUtcOffsetMinutes,
        int sessionStartHour)
    {
        TimeSpan offset = TimeSpan.FromMinutes(Math.Clamp(serverUtcOffsetMinutes, -14 * 60, 14 * 60));
        DateTimeOffset local = DateTimeOffset.FromUnixTimeMilliseconds(timeMilliseconds).ToOffset(offset);
        DateOnly sessionDate = DateOnly.FromDateTime(local.DateTime);
        if (local.Hour < sessionStartHour)
            sessionDate = sessionDate.AddDays(-1);
        DateTime localStart = sessionDate.ToDateTime(new TimeOnly(sessionStartHour, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(localStart, offset).ToUnixTimeSeconds();
    }

    private enum TradeSide
    {
        Buy,
        Sell
    }

    private sealed class FootprintBuilder
    {
        private readonly long _candleStartUnix;
        private readonly Dictionary<double, MutableFootprintLevel> _levels = new();

        public FootprintBuilder(long candleStartUnix) => _candleStartUnix = candleStartUnix;

        public void Add(double price, double volume, TradeSide side)
        {
            if (!_levels.TryGetValue(price, out MutableFootprintLevel? level))
            {
                level = new MutableFootprintLevel(price);
                _levels[price] = level;
            }
            if (side == TradeSide.Buy)
                level.AskVolume += volume;
            else
                level.BidVolume += volume;
            level.Trades++;
        }

        public FootprintCandleProfile Build()
        {
            FootprintPriceLevel[] levels = _levels.Values
                .OrderByDescending(item => item.Price)
                .Select(item => new FootprintPriceLevel(item.Price, item.BidVolume, item.AskVolume, item.Trades))
                .ToArray();
            return new FootprintCandleProfile(
                _candleStartUnix,
                levels,
                levels.Length == 0 ? 0 : levels.Max(item => item.TotalVolume),
                levels.Sum(item => item.BidVolume),
                levels.Sum(item => item.AskVolume));
        }
    }

    private sealed class MutableFootprintLevel
    {
        public MutableFootprintLevel(double price) => Price = price;
        public double Price { get; }
        public double BidVolume { get; set; }
        public double AskVolume { get; set; }
        public long Trades { get; set; }
    }

    private sealed class SessionBuilder
    {
        private readonly Dictionary<double, MutableVolumeLevel> _levels = new();

        public SessionBuilder(long sessionStartUnix, long sessionEndUnix)
        {
            SessionStartUnix = sessionStartUnix;
            SessionEndUnix = sessionEndUnix;
        }

        public long SessionStartUnix { get; }
        public long SessionEndUnix { get; }

        public void Add(double price, double volume)
        {
            if (!_levels.TryGetValue(price, out MutableVolumeLevel? level))
            {
                level = new MutableVolumeLevel(price);
                _levels[price] = level;
            }
            level.Volume += volume;
            level.Trades++;
        }

        public SessionVolumeProfileData Build(double valueAreaPercent)
        {
            VolumeProfileLevel[] levels = _levels.Values
                .OrderBy(item => item.Price)
                .Select(item => new VolumeProfileLevel(item.Price, item.Volume, item.Trades))
                .ToArray();
            if (levels.Length == 0)
            {
                return new SessionVolumeProfileData(
                    SessionStartUnix,
                    SessionEndUnix,
                    levels,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            double total = levels.Sum(item => item.Volume);
            VolumeProfileLevel poc = levels.MaxBy(item => item.Volume)!;
            double target = total * valueAreaPercent / 100.0;
            double accumulated = 0;
            double valueLow = poc.Price;
            double valueHigh = poc.Price;
            foreach (VolumeProfileLevel level in levels.OrderByDescending(item => item.Volume))
            {
                if (accumulated >= target)
                    break;
                accumulated += level.Volume;
                valueLow = Math.Min(valueLow, level.Price);
                valueHigh = Math.Max(valueHigh, level.Price);
            }

            return new SessionVolumeProfileData(
                SessionStartUnix,
                SessionEndUnix,
                levels,
                poc.Price,
                valueLow,
                valueHigh,
                poc.Volume,
                total);
        }
    }

    private sealed class MutableVolumeLevel
    {
        public MutableVolumeLevel(double price) => Price = price;
        public double Price { get; }
        public double Volume { get; set; }
        public long Trades { get; set; }
    }
}
