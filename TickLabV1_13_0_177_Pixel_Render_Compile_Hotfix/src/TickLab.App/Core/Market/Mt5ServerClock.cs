namespace TickLab.Core.Market;

public static class Mt5ServerClock
{
    // MQL5 datetime values are broker/server wall-clock values represented as
    // seconds from the 1970 epoch. TickLab preserves those values unchanged.
    public static long UtcToServerUnix(
        DateTimeOffset utcTime,
        int serverUtcOffsetMinutes) =>
        checked(utcTime.ToUnixTimeSeconds() + serverUtcOffsetMinutes * 60L);

    public static long UtcMillisecondsToServerMilliseconds(
        long utcMilliseconds,
        int serverUtcOffsetMinutes) =>
        checked(utcMilliseconds + serverUtcOffsetMinutes * 60_000L);

    public static long ServerUnixToUtcMilliseconds(
        long serverUnix,
        int serverUtcOffsetMinutes,
        int safetyMarginMinutes = 0)
    {
        long milliseconds = checked(
            (serverUnix - serverUtcOffsetMinutes * 60L) * 1000L);
        long safety = Math.Max(0, safetyMarginMinutes) * 60_000L;
        return Math.Max(1, milliseconds - safety);
    }

    public static long ServerNowUnix(int serverUtcOffsetMinutes) =>
        UtcToServerUnix(DateTimeOffset.UtcNow, serverUtcOffsetMinutes);

    public static DateTimeOffset ToDisplayTime(long serverUnix) =>
        DateTimeOffset.FromUnixTimeSeconds(serverUnix).ToUniversalTime();
}
