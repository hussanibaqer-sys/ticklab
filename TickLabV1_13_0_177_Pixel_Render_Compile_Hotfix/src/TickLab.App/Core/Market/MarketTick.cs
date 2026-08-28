namespace TickLab.Core.Market;

public readonly record struct MarketTick(
    long TimeMilliseconds,
    long TimeUnix,
    double Bid,
    double Ask,
    double Last,
    double Volume,
    uint Flags,
    double VolumeReal)
{
    public double DisplayPrice =>
        Bid > 0
            ? Bid
            : Last > 0
                ? Last
                : Ask;

    public double Spread =>
        Bid > 0 && Ask > 0
            ? Ask - Bid
            : 0;

    public DateTimeOffset Time =>
        DateTimeOffset.FromUnixTimeMilliseconds(TimeMilliseconds);
}
