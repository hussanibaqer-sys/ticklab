namespace TickLab.Core.Market;

public sealed record FootprintPriceLevel(
    double Price,
    double BidVolume,
    double AskVolume,
    long Trades)
{
    public double TotalVolume => BidVolume + AskVolume;
    public double Delta => AskVolume - BidVolume;
}

public sealed record FootprintCandleProfile(
    long CandleStartUnix,
    IReadOnlyList<FootprintPriceLevel> Levels,
    double MaximumLevelVolume,
    double TotalBidVolume,
    double TotalAskVolume)
{
    public double Delta => TotalAskVolume - TotalBidVolume;
}

public sealed record VolumeProfileLevel(
    double Price,
    double Volume,
    long Trades);

public sealed record SessionVolumeProfileData(
    long SessionStartUnix,
    long SessionEndUnix,
    IReadOnlyList<VolumeProfileLevel> Levels,
    double PointOfControlPrice,
    double ValueAreaLow,
    double ValueAreaHigh,
    double MaximumLevelVolume,
    double TotalVolume);

public sealed record OrderFlowProfileSnapshot(
    long StartMilliseconds,
    long EndMilliseconds,
    double PriceStep,
    IReadOnlyDictionary<long, FootprintCandleProfile> Footprints,
    IReadOnlyList<SessionVolumeProfileData> Sessions,
    bool HasRealVolume,
    int TickCount,
    string StatusMessage)
{
    public static OrderFlowProfileSnapshot Empty { get; } = new(
        0,
        0,
        0,
        new Dictionary<long, FootprintCandleProfile>(),
        Array.Empty<SessionVolumeProfileData>(),
        false,
        0,
        string.Empty);
}
