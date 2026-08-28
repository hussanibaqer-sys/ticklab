namespace TickLab.Core.Market;

public sealed record TickStatistics(
    int Count,
    long FirstTimeMilliseconds,
    long LastTimeMilliseconds,
    double MinimumBid,
    double MaximumBid,
    double MinimumAsk,
    double MaximumAsk,
    double AverageSpread)
{
    public static TickStatistics Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0);
}

public static class TickStatisticsCalculator
{
    public static TickStatistics Calculate(
        IReadOnlyList<MarketTick> ticks)
    {
        if (ticks.Count == 0)
            return TickStatistics.Empty;

        double minimumBid = double.MaxValue;
        double maximumBid = double.MinValue;
        double minimumAsk = double.MaxValue;
        double maximumAsk = double.MinValue;
        double spreadTotal = 0;
        int spreadCount = 0;

        foreach (MarketTick tick in ticks)
        {
            double bid = tick.Bid > 0
                ? tick.Bid
                : tick.DisplayPrice;

            double ask = tick.Ask > 0
                ? tick.Ask
                : tick.DisplayPrice;

            minimumBid = Math.Min(minimumBid, bid);
            maximumBid = Math.Max(maximumBid, bid);
            minimumAsk = Math.Min(minimumAsk, ask);
            maximumAsk = Math.Max(maximumAsk, ask);

            if (tick.Spread > 0)
            {
                spreadTotal += tick.Spread;
                spreadCount++;
            }
        }

        return new TickStatistics(
            ticks.Count,
            ticks[0].TimeMilliseconds,
            ticks[^1].TimeMilliseconds,
            minimumBid,
            maximumBid,
            minimumAsk,
            maximumAsk,
            spreadCount > 0 ? spreadTotal / spreadCount : 0);
    }
}
