using System.Globalization;
using System.Text;

namespace TickLab.Core.Diagnostics;

public sealed record TickLabErrorReport(
    string ReportId,
    DateTimeOffset OccurredUtc,
    TickLabErrorSeverity Severity,
    string Operation,
    string Stage,
    string Message,
    string TechnicalDetails,
    string SuggestedAction,
    string? Symbol,
    string? Timeframe,
    string? ConnectorId,
    string? RequestId,
    string? FilePath,
    long? BlockStartUnix,
    long? BlockEndUnix,
    int? ExpectedRecords,
    int? ActualRecords,
    long? ExpectedFirstUnix,
    long? ActualFirstUnix,
    long? ExpectedLatestUnix,
    long? ActualLatestUnix,
    int? Mt5ErrorCode,
    IReadOnlyDictionary<string, string> AdditionalData)
{
    public string ToDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TickLab error: {ReportId}");
        builder.AppendLine($"Severity: {Severity}");
        builder.AppendLine($"Time (UTC): {OccurredUtc:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Operation: {Operation}");
        builder.AppendLine($"Stage: {Stage}");
        builder.AppendLine($"Message: {Message}");
        Append(builder, "Suggested action", SuggestedAction);
        Append(builder, "Symbol", Symbol);
        Append(builder, "Timeframe", Timeframe);
        Append(builder, "Connector", ConnectorId);
        Append(builder, "Request", RequestId);
        Append(builder, "File", FilePath);
        AppendUnix(builder, "Block start", BlockStartUnix);
        AppendUnix(builder, "Block end", BlockEndUnix);
        Append(builder, "Expected records", ExpectedRecords);
        Append(builder, "Actual records", ActualRecords);
        AppendUnix(builder, "Expected first", ExpectedFirstUnix);
        AppendUnix(builder, "Actual first", ActualFirstUnix);
        AppendUnix(builder, "Expected latest", ExpectedLatestUnix);
        AppendUnix(builder, "Actual latest", ActualLatestUnix);
        Append(builder, "MT5 error code", Mt5ErrorCode);

        foreach ((string key, string value) in AdditionalData.OrderBy(item => item.Key, StringComparer.Ordinal))
            Append(builder, key, value);

        if (!string.IsNullOrWhiteSpace(TechnicalDetails))
        {
            builder.AppendLine();
            builder.AppendLine("Technical details:");
            builder.AppendLine(TechnicalDetails);
        }

        return builder.ToString().TrimEnd();
    }

    private static void Append(StringBuilder builder, string label, object? value)
    {
        if (value is null)
            return;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
            builder.AppendLine($"{label}: {text}");
    }

    private static void AppendUnix(StringBuilder builder, string label, long? unix)
    {
        if (unix is null || unix <= 0)
            return;
        string rendered;
        try
        {
            rendered = $"{unix} ({DateTimeOffset.FromUnixTimeSeconds(unix.Value):yyyy-MM-dd HH:mm:ss} UTC)";
        }
        catch (ArgumentOutOfRangeException)
        {
            rendered = unix.Value.ToString(CultureInfo.InvariantCulture);
        }
        builder.AppendLine($"{label}: {rendered}");
    }
}
