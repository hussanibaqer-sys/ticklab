using System.Globalization;

namespace TickLab.Core.Market;

public enum TimeframeUnit
{
    Tick,
    Second,
    Minute,
    Hour,
    Day,
    Week,
    Month
}

public sealed record TimeframeDefinition(
    int Quantity,
    TimeframeUnit Unit,
    bool IsBuiltIn,
    string? NativeMt5Code)
{
    private static readonly IReadOnlyList<TimeframeDefinition> BuiltInItems =
        new[]
        {
            new TimeframeDefinition(1, TimeframeUnit.Tick, true, null),
            new TimeframeDefinition(1, TimeframeUnit.Second, true, null),
            new TimeframeDefinition(15, TimeframeUnit.Second, true, null),
            new TimeframeDefinition(30, TimeframeUnit.Second, true, null),
            new TimeframeDefinition(45, TimeframeUnit.Second, true, null),

            new TimeframeDefinition(1, TimeframeUnit.Minute, true, "PERIOD_M1"),
            new TimeframeDefinition(2, TimeframeUnit.Minute, true, "PERIOD_M2"),
            new TimeframeDefinition(3, TimeframeUnit.Minute, true, "PERIOD_M3"),
            new TimeframeDefinition(4, TimeframeUnit.Minute, true, "PERIOD_M4"),
            new TimeframeDefinition(5, TimeframeUnit.Minute, true, "PERIOD_M5"),
            new TimeframeDefinition(10, TimeframeUnit.Minute, true, "PERIOD_M10"),
            new TimeframeDefinition(15, TimeframeUnit.Minute, true, "PERIOD_M15"),
            new TimeframeDefinition(30, TimeframeUnit.Minute, true, "PERIOD_M30"),
            new TimeframeDefinition(45, TimeframeUnit.Minute, true, null),

            new TimeframeDefinition(1, TimeframeUnit.Hour, true, "PERIOD_H1"),
            new TimeframeDefinition(2, TimeframeUnit.Hour, true, "PERIOD_H2"),
            new TimeframeDefinition(3, TimeframeUnit.Hour, true, "PERIOD_H3"),
            new TimeframeDefinition(4, TimeframeUnit.Hour, true, "PERIOD_H4"),
            new TimeframeDefinition(6, TimeframeUnit.Hour, true, "PERIOD_H6"),
            new TimeframeDefinition(8, TimeframeUnit.Hour, true, "PERIOD_H8"),
            new TimeframeDefinition(12, TimeframeUnit.Hour, true, "PERIOD_H12"),

            new TimeframeDefinition(1, TimeframeUnit.Day, true, "PERIOD_D1"),
            new TimeframeDefinition(1, TimeframeUnit.Week, true, "PERIOD_W1"),
            new TimeframeDefinition(1, TimeframeUnit.Month, true, "PERIOD_MN1")
        };

    public static IReadOnlyList<TimeframeDefinition> BuiltIns => BuiltInItems;

    private static readonly IReadOnlyList<string> NativeMt5Items =
        new[]
        {
            "PERIOD_M1", "PERIOD_M2", "PERIOD_M3", "PERIOD_M4",
            "PERIOD_M5", "PERIOD_M6", "PERIOD_M10", "PERIOD_M12",
            "PERIOD_M15", "PERIOD_M20", "PERIOD_M30",
            "PERIOD_H1", "PERIOD_H2", "PERIOD_H3", "PERIOD_H4",
            "PERIOD_H6", "PERIOD_H8", "PERIOD_H12",
            "PERIOD_D1", "PERIOD_W1", "PERIOD_MN1"
        };

    public static IReadOnlyList<string> NativeMt5Timeframes => NativeMt5Items;

    public string Key => $"{Unit}:{Quantity.ToString(CultureInfo.InvariantCulture)}";

    public string DisplayText => Unit switch
    {
        TimeframeUnit.Tick => "Tick",
        TimeframeUnit.Second => $"{Quantity}s",
        TimeframeUnit.Minute => $"{Quantity}m",
        TimeframeUnit.Hour => $"{Quantity}h",
        TimeframeUnit.Day => Quantity == 1 ? "D" : $"{Quantity}D",
        TimeframeUnit.Week => Quantity == 1 ? "W" : $"{Quantity}W",
        TimeframeUnit.Month => Quantity == 1 ? "M" : $"{Quantity}M",
        _ => Quantity.ToString(CultureInfo.InvariantCulture)
    };

    public bool UsesTickArchive => Unit is TimeframeUnit.Tick or TimeframeUnit.Second;

    public bool IsRawTickChart => Unit == TimeframeUnit.Tick;

    public bool IsSynthetic => string.IsNullOrWhiteSpace(NativeMt5Code);

    public long ToApproximateSeconds() => Unit switch
    {
        TimeframeUnit.Tick => 1L,
        TimeframeUnit.Second => Quantity,
        TimeframeUnit.Minute => checked(Quantity * 60L),
        TimeframeUnit.Hour => checked(Quantity * 3_600L),
        TimeframeUnit.Day => checked(Quantity * 86_400L),
        TimeframeUnit.Week => checked(Quantity * 7L * 86_400L),
        TimeframeUnit.Month => checked(Quantity * 30L * 86_400L),
        _ => 60L
    };

    public string SourceMt5Code
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NativeMt5Code))
                return NativeMt5Code;

            // Every synthetic minute-or-larger chart is built from one
            // consistent native M1 stream. Mixing M1 history with live M15/H1
            // source candles corrupts OHLC and freezes the active bucket.
            return "PERIOD_M1";
        }
    }

    public static TimeframeDefinition CreateCustom(
        int quantity,
        TimeframeUnit unit)
    {
        if (unit == TimeframeUnit.Tick)
            throw new ArgumentOutOfRangeException(nameof(unit));

        int maximumQuantity =
            unit == TimeframeUnit.Month
                ? 1_200
                : 100_000;

        if (quantity < 1 || quantity > maximumQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        string? nativeCode = FindNativeCode(quantity, unit);
        return new TimeframeDefinition(quantity, unit, false, nativeCode);
    }

    public static TimeframeDefinition? FindBuiltIn(
        int quantity,
        TimeframeUnit unit) =>
        BuiltInItems.FirstOrDefault(
            item => item.Quantity == quantity && item.Unit == unit);

    public static TimeframeDefinition? FindBuiltInByNativeCode(
        string? nativeCode)
    {
        if (string.IsNullOrWhiteSpace(nativeCode))
            return null;

        return BuiltInItems.FirstOrDefault(
            item => string.Equals(
                item.NativeMt5Code,
                nativeCode.Trim(),
                StringComparison.Ordinal));
    }

    public static TimeframeDefinition FromNativeMt5Code(
        string? nativeCode)
    {
        string normalized = string.IsNullOrWhiteSpace(nativeCode)
            ? "PERIOD_M1"
            : nativeCode.Trim().ToUpperInvariant();

        TimeframeDefinition? builtIn =
            FindBuiltInByNativeCode(normalized);

        if (builtIn is not null)
            return builtIn;

        if (normalized == "PERIOD_D1")
            return CreateCustom(1, TimeframeUnit.Day);

        if (normalized == "PERIOD_W1")
            return CreateCustom(1, TimeframeUnit.Week);

        if (normalized == "PERIOD_MN1")
            return CreateCustom(1, TimeframeUnit.Month);

        if (TryParseNativeQuantity(normalized, "PERIOD_M", out int minutes))
            return CreateCustom(minutes, TimeframeUnit.Minute);

        if (TryParseNativeQuantity(normalized, "PERIOD_H", out int hours))
            return CreateCustom(hours, TimeframeUnit.Hour);

        return FindBuiltIn(1, TimeframeUnit.Minute)!;
    }

    public long GetBucketStartUnix(
        long serverUnixSeconds,
        int serverUtcOffsetMinutes = 0)
    {
        // The bridge sends native MT5 broker-server timestamps. They are
        // already in the clock domain used by MT5 candle boundaries, so an
        // additional UTC-offset shift here would move candles twice.
        _ = serverUtcOffsetMinutes;
        DateTimeOffset timestamp =
            DateTimeOffset.FromUnixTimeSeconds(serverUnixSeconds).ToUniversalTime();

        return Unit switch
        {
            TimeframeUnit.Tick => serverUnixSeconds,
            TimeframeUnit.Second => FloorFixed(serverUnixSeconds, Quantity),
            TimeframeUnit.Minute => FloorFixed(serverUnixSeconds, checked(Quantity * 60L)),
            TimeframeUnit.Hour => FloorFixed(serverUnixSeconds, checked(Quantity * 3_600L)),
            TimeframeUnit.Day => FloorFixed(serverUnixSeconds, checked(Quantity * 86_400L)),
            TimeframeUnit.Week => GetWeekBucketStart(timestamp),
            TimeframeUnit.Month => GetMonthBucketStart(timestamp),
            _ => serverUnixSeconds
        };
    }

    public long GetBucketEndUnix(
        long bucketStartUnix,
        int serverUtcOffsetMinutes = 0)
    {
        if (Unit == TimeframeUnit.Month)
        {
            _ = serverUtcOffsetMinutes;
            return DateTimeOffset
                .FromUnixTimeSeconds(bucketStartUnix)
                .ToUniversalTime()
                .AddMonths(Quantity)
                .ToUnixTimeSeconds();
        }

        long seconds = Unit switch
        {
            TimeframeUnit.Tick => 1L,
            TimeframeUnit.Second => Quantity,
            TimeframeUnit.Minute => checked(Quantity * 60L),
            TimeframeUnit.Hour => checked(Quantity * 3_600L),
            TimeframeUnit.Day => checked(Quantity * 86_400L),
            TimeframeUnit.Week => checked(Quantity * 7L * 86_400L),
            _ => 1L
        };

        return checked(bucketStartUnix + seconds);
    }

    private static long FloorFixed(long value, long size)
    {
        if (size <= 1)
            return value;

        long quotient = value / size;
        long remainder = value % size;

        if (remainder < 0)
            quotient--;

        return quotient * size;
    }

    private long GetWeekBucketStart(DateTimeOffset timestamp)
    {
        DateTimeOffset day = new(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            0,
            0,
            0,
            TimeSpan.Zero);

        int daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;
        DateTimeOffset monday = day.AddDays(-daysSinceMonday);
        DateTimeOffset epochMonday = new(1970, 1, 5, 0, 0, 0, TimeSpan.Zero);
        long weeksSinceEpoch = (long)Math.Floor((monday - epochMonday).TotalDays / 7.0);
        long groupedWeeks = FloorFixed(weeksSinceEpoch, Quantity);
        return epochMonday.AddDays(groupedWeeks * 7L).ToUnixTimeSeconds();
    }

    private long GetMonthBucketStart(DateTimeOffset timestamp)
    {
        int monthsSinceEpoch = checked(
            (timestamp.Year - 1970) * 12 +
            timestamp.Month - 1);
        int groupedMonth = (int)FloorFixed(monthsSinceEpoch, Quantity);
        int yearOffset = Math.DivRem(groupedMonth, 12, out int monthOffset);

        if (monthOffset < 0)
        {
            yearOffset--;
            monthOffset += 12;
        }

        return new DateTimeOffset(
            1970 + yearOffset,
            monthOffset + 1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static string GetMinuteSource(int quantity)
    {
        int[] candidates = { 30, 15, 10, 5, 4, 3, 2, 1 };

        foreach (int candidate in candidates)
        {
            if (quantity % candidate == 0)
                return $"PERIOD_M{candidate}";
        }

        return "PERIOD_M1";
    }

    private static string GetHourSource(int quantity)
    {
        int[] candidates = { 12, 8, 6, 4, 3, 2, 1 };

        foreach (int candidate in candidates)
        {
            if (quantity % candidate == 0)
                return $"PERIOD_H{candidate}";
        }

        return "PERIOD_H1";
    }

    private static bool TryParseNativeQuantity(
        string nativeCode,
        string prefix,
        out int quantity)
    {
        quantity = 0;

        if (!nativeCode.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return int.TryParse(
            nativeCode[prefix.Length..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out quantity) &&
            quantity > 0;
    }

    private static string? FindNativeCode(
        int quantity,
        TimeframeUnit unit)
    {
        string? candidate = unit switch
        {
            TimeframeUnit.Minute => $"PERIOD_M{quantity}",
            TimeframeUnit.Hour => $"PERIOD_H{quantity}",
            TimeframeUnit.Day when quantity == 1 => "PERIOD_D1",
            TimeframeUnit.Week when quantity == 1 => "PERIOD_W1",
            TimeframeUnit.Month when quantity == 1 => "PERIOD_MN1",
            _ => null
        };

        return candidate is not null && NativeMt5Items.Contains(candidate, StringComparer.Ordinal)
            ? candidate
            : null;
    }
}
