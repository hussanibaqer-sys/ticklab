namespace TickLab.Core.Alerts;

public enum AlertConditionType
{
    PriceAbove = 0,
    PriceBelow = 1,
    PriceCrossesUp = 2,
    PriceCrossesDown = 3,
    SpreadAbove = 4,
    CandleOpened = 5,
    CandleClosed = 6,
    DrawingCross = 7,
    IndicatorAbove = 8,
    IndicatorBelow = 9,
    IndicatorCrossesUp = 10,
    IndicatorCrossesDown = 11,
    PriceTouches = 12
}

public enum AlertPriceSource
{
    Bid,
    Ask,
    Last,
    Close
}

public enum AlertFrequency
{
    Once,
    OncePerCandle,
    OncePerCandleClose,
    Repeating
}

public sealed record AlertRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New alert";
    public bool Enabled { get; init; } = true;
    public int ChartId { get; init; } = 1;
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public AlertConditionType Condition { get; init; } = AlertConditionType.PriceCrossesUp;
    public AlertPriceSource PriceSource { get; init; } = AlertPriceSource.Bid;
    public double Threshold { get; init; }
    public string LineColor { get; init; } = "#F5B83E";
    public double LineThickness { get; init; } = 1.25;
    public string DrawingId { get; init; } = string.Empty;
    public string IndicatorKey { get; init; } = string.Empty;
    public AlertFrequency Frequency { get; init; } = AlertFrequency.Once;
    public bool PlaySound { get; init; } = true;
    public bool ShowDesktopPopup { get; init; } = true;
    public long CreatedUnix { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long? LastTriggeredUnix { get; init; }
    public long? LastTriggeredCandleUnix { get; init; }
    public bool HasTriggered { get; init; }
    public string LastMessage { get; init; } = string.Empty;
}

public sealed record AlertDocument
{
    public IReadOnlyList<AlertRule> Rules { get; init; } = Array.Empty<AlertRule>();
    public IReadOnlyList<AlertLogEntry> Log { get; init; } = Array.Empty<AlertLogEntry>();
}

public sealed record AlertLogEntry(
    string Id,
    string AlertId,
    string AlertName,
    string Symbol,
    string Timeframe,
    long TriggeredUnix,
    string Message);
