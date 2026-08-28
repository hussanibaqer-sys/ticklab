namespace TickLab.Gateway.FileBridge;

public sealed record Mt5ConnectorSummary(
    string ConnectorId,
    string ConnectorName,
    string BridgeVersion,
    string Broker,
    string Server,
    int TerminalBuild,
    long AccountLogin,
    string Symbol,
    string Timeframe,
    int Digits,
    double Point,
    bool TerminalConnected,
    bool AccountConnected,
    long UpdatedUnix,
    long DataActivityUnix,
    bool HasDataAvailable,
    int ServerUtcOffsetMinutes = 0,
    double TickSize = 0)
{
    public const string BridgeV240 =
        "2.4.0-projection-live-history";

    public const string BridgeV290 =
        "2.9.0-dual-channel";

    public const string BridgeV300 =
        "3.0.0";

    // V300 preserves the proven dual-heartbeat connector while separating
    // permanent raw-tick capture from permanent native timeframe candles.
    public bool IsCompatibleBridge =>
        !string.IsNullOrWhiteSpace(BridgeVersion) &&
        BridgeVersion.StartsWith(BridgeV300, StringComparison.OrdinalIgnoreCase);

    public bool SupportsRequestedHistory => IsCompatibleBridge;

    public bool UsesAutomaticHistoryExport => false;

    // A bridge is active only when its heartbeat is genuinely live.
    // Old candles/history files must never make a stale connector appear online.
    public bool IsHeartbeatFresh => IsRecent(UpdatedUnix, 45);

    public bool IsDataActivityFresh => IsRecent(DataActivityUnix, 20);

    public bool IsBridgeOnline =>
        TerminalConnected && IsHeartbeatFresh;

    public bool IsConnected => IsCompatibleBridge && IsBridgeOnline;

    public bool CanConnect => IsConnected;

    private static bool IsRecent(long unixTime, long maximumAgeSeconds)
    {
        if (unixTime <= 0)
            return false;

        long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixTime;
        return age >= -30 && age <= maximumAgeSeconds;
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(ConnectorName) ? ConnectorId : ConnectorName;

    public string ConnectionState =>
        !IsCompatibleBridge
            ? $"UNSUPPORTED {BridgeVersion}"
            : IsBridgeOnline
                ? "Connected — live heartbeat"
                : "Offline — stale heartbeat";
}
