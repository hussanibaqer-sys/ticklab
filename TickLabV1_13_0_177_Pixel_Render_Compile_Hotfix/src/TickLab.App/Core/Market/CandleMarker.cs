using System.Text.Json.Serialization;

namespace TickLab.Core.Market;

public sealed record CandleMarker(
    string Id,
    string Symbol,
    string Timeframe,
    long StartUnix,
    string Label,
    string Source,
    long CreatedUnix,
    long? AnchorUnix = null)
{
    public DateTimeOffset StartTime => DateTimeOffset.FromUnixTimeSeconds(StartUnix);

    // Find Candle can begin on a larger timeframe candle while preserving the
    // exact typed date/time for later cross-timeframe inspection. Older/MT5
    // markers simply fall back to their candle start.
    [JsonIgnore]
    public long NavigationUnix => AnchorUnix ?? StartUnix;

    [JsonIgnore]
    public DateTimeOffset NavigationTime => DateTimeOffset.FromUnixTimeSeconds(NavigationUnix);
}

public sealed record CandleMarkerTransfer(
    string Id,
    string Action,
    string Symbol,
    string Timeframe,
    long StartUnix,
    string Source,
    long CreatedUnix,
    string Label);
