using System.Text.Json.Serialization;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

internal sealed class ConnectionDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("connector_name")]
    public string ConnectorName { get; set; } = string.Empty;

    [JsonPropertyName("bridge_version")]
    public string BridgeVersion { get; set; } = string.Empty;

    [JsonPropertyName("broker")]
    public string Broker { get; set; } = string.Empty;

    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("terminal_build")]
    public int TerminalBuild { get; set; }

    [JsonPropertyName("account_login")]
    public long AccountLogin { get; set; }
}

internal sealed class HeartbeatDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("bridge_version")]
    public string BridgeVersion { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("digits")]
    public int Digits { get; set; }

    [JsonPropertyName("point")]
    public double Point { get; set; }

    [JsonPropertyName("tick_size")]
    public double TickSize { get; set; }

    [JsonPropertyName("server_utc_offset_minutes")]
    public int ServerUtcOffsetMinutes { get; set; }

    [JsonPropertyName("terminal_connected")]
    public bool TerminalConnected { get; set; }

    [JsonPropertyName("account_connected")]
    public bool AccountConnected { get; set; }

    [JsonPropertyName("updated_unix")]
    public long UpdatedUnix { get; set; }
}

internal sealed class HistoryStatusDocument
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("synchronized")]
    public bool Synchronized { get; set; }

    [JsonPropertyName("exported_bars")]
    public int ExportedBars { get; set; }

    [JsonPropertyName("first_bar_unix")]
    public long FirstBarUnix { get; set; }

    [JsonPropertyName("latest_bar_unix")]
    public long LatestBarUnix { get; set; }

    [JsonPropertyName("server_first_unix")]
    public long ServerFirstUnix { get; set; }

    [JsonPropertyName("series_first_unix")]
    public long SeriesFirstUnix { get; set; }

    [JsonPropertyName("terminal_first_unix")]
    public long TerminalFirstUnix { get; set; }

    [JsonPropertyName("target_first_unix")]
    public long TargetFirstUnix { get; set; }

    [JsonPropertyName("available_first_unix")]
    public long AvailableFirstUnix { get; set; }

    [JsonPropertyName("native_range_complete")]
    public bool NativeRangeComplete { get; set; }

    [JsonPropertyName("native_range_partial")]
    public bool NativeRangePartial { get; set; }

    [JsonPropertyName("coverage_reason")]
    public string CoverageReason { get; set; } = string.Empty;

    [JsonPropertyName("last_error_code")]
    public int LastErrorCode { get; set; }

    [JsonPropertyName("history_sync_complete")]
    public bool HistorySyncComplete { get; set; }

    [JsonPropertyName("limited_by_max_bars")]
    public bool LimitedByMaxBars { get; set; }

    [JsonPropertyName("terminal_max_bars")]
    public int TerminalMaxBars { get; set; }

    [JsonPropertyName("target_total_bars")]
    public int TargetTotalBars { get; set; }

    [JsonPropertyName("progress_percent")]
    public double ProgressPercent { get; set; }

    [JsonPropertyName("current_bar_unix")]
    public long CurrentBarUnix { get; set; }

    [JsonPropertyName("current_block_start_unix")]
    public long CurrentBlockStartUnix { get; set; }

    [JsonPropertyName("current_block_end_unix")]
    public long CurrentBlockEndUnix { get; set; }

    [JsonPropertyName("speed_bars_per_second")]
    public double SpeedBarsPerSecond { get; set; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("failure_code")]
    public string FailureCode { get; set; } = string.Empty;

    [JsonPropertyName("failure_stage")]
    public string FailureStage { get; set; } = string.Empty;

    [JsonPropertyName("failure_expected_bars")]
    public int FailureExpectedBars { get; set; }

    [JsonPropertyName("failure_actual_bars")]
    public int FailureActualBars { get; set; }

    [JsonPropertyName("failure_expected_first_unix")]
    public long FailureExpectedFirstUnix { get; set; }

    [JsonPropertyName("failure_actual_first_unix")]
    public long FailureActualFirstUnix { get; set; }

    [JsonPropertyName("failure_expected_latest_unix")]
    public long FailureExpectedLatestUnix { get; set; }

    [JsonPropertyName("failure_actual_latest_unix")]
    public long FailureActualLatestUnix { get; set; }

    [JsonPropertyName("failure_file_path")]
    public string FailureFilePath { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("updated_unix")]
    public long UpdatedUnix { get; set; }
}

internal sealed class SymbolListRequestDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 2;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("requested_unix")]
    public long RequestedUnix { get; set; }
}

internal sealed class ChartSelectionRequestDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 2;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("requested_unix")]
    public long RequestedUnix { get; set; }
}


internal sealed class HistoryRequestDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 2;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("include_ticks")]
    public int IncludeTicks { get; set; }

    [JsonPropertyName("include_candles")]
    public int IncludeCandles { get; set; } = 1;

    [JsonPropertyName("minimum_tick_msc")]
    public long MinimumTickMilliseconds { get; set; }

    [JsonPropertyName("minimum_candle_unix")]
    public long MinimumCandleUnix { get; set; }

    [JsonPropertyName("requested_unix")]
    public long RequestedUnix { get; set; }
}

internal sealed class HistoryControlRequestDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 2;

    [JsonPropertyName("control_id")]
    public string ControlId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("requested_unix")]
    public long RequestedUnix { get; set; }
}

internal sealed class TickRequestDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 2;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("start_msc")]
    public long StartMilliseconds { get; set; }

    [JsonPropertyName("end_msc")]
    public long EndMilliseconds { get; set; }

    [JsonPropertyName("requested_unix")]
    public long RequestedUnix { get; set; }
}

internal sealed class TickSelectionDocument
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string ConnectorId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("start_msc")]
    public long StartMilliseconds { get; set; }

    [JsonPropertyName("end_msc")]
    public long EndMilliseconds { get; set; }

    [JsonPropertyName("tick_count")]
    public int TickCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("error_code")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("ticks_file")]
    public string TicksFile { get; set; } = "ticks.csv";

    [JsonPropertyName("completed_unix")]
    public long CompletedUnix { get; set; }
}

public sealed record TickResponseResult(
    bool IsComplete,
    bool IsSuccess,
    string Message,
    int ErrorCode,
    IReadOnlyList<MarketTick> Ticks)
{
    public static TickResponseResult Pending { get; } =
        new(false, false, string.Empty, 0, Array.Empty<MarketTick>());
}
