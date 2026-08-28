namespace TickLab.Core.Market;

public sealed record Candle(
    string Symbol,
    string Timeframe,
    int Digits,
    double Point,
    long StartUnix,
    long EndUnix,
    string StartText,
    double Open,
    double High,
    double Low,
    double Close,
    long TickVolume,
    int Spread,
    long RealVolume,
    bool IsClosed)
{
    public bool IsBullish => Close >= Open;

    public DateTimeOffset StartTime =>
        DateTimeOffset.FromUnixTimeSeconds(StartUnix);

    public DateTimeOffset EndTime =>
        DateTimeOffset.FromUnixTimeSeconds(EndUnix);
}
