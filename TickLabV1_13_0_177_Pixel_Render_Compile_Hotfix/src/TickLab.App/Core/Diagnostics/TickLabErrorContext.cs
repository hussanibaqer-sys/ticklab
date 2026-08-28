namespace TickLab.Core.Diagnostics;

public sealed record TickLabErrorContext(
    string Operation,
    string Stage,
    string SuggestedAction,
    string? ErrorCode = null,
    string? Symbol = null,
    string? Timeframe = null,
    string? ConnectorId = null,
    string? RequestId = null,
    string? FilePath = null,
    long? BlockStartUnix = null,
    long? BlockEndUnix = null,
    int? ExpectedRecords = null,
    int? ActualRecords = null,
    long? ExpectedFirstUnix = null,
    long? ActualFirstUnix = null,
    long? ExpectedLatestUnix = null,
    long? ActualLatestUnix = null,
    int? Mt5ErrorCode = null,
    IReadOnlyDictionary<string, string>? AdditionalData = null);
