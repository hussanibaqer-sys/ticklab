using System.Collections.Concurrent;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class Mt5FileBridgeClient
{
    private readonly TickArchiveCandleCache _tickArchiveCache = new();
    private readonly object _connectorFolderSync = new();
    private readonly Dictionary<string, string> _connectorFolders =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mt5ConnectorSummary> _lastGoodConnectors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastGoodHistoryWorkerUtc =
        new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    // Request files have fixed bridge names. Serialize writers per target so a
    // maintenance refresh cannot collide with a manual chart/history request.
    private static readonly ConcurrentDictionary<string, object> AtomicWriteLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public string ConnectionsRootPath =>
        Mt5Paths.GetConnectionsRoot();

    public string? ManualConnectionsRoot =>
        Mt5Paths.ManualConnectionsRoot;

    public bool SetManualBridgeFolder(string? selectedPath)
    {
        bool accepted = Mt5Paths.SetManualBridgeFolder(selectedPath);
        if (accepted)
        {
            lock (_connectorFolderSync)
            {
                _connectorFolders.Clear();
                _lastGoodConnectors.Clear();
                _lastGoodHistoryWorkerUtc.Clear();
            }
        }

        return accepted;
    }

    public void ActivateConnector(Mt5ConnectorSummary connector)
    {
        lock (_connectorFolderSync)
        {
            if (_connectorFolders.TryGetValue(
                    connector.ConnectorId,
                    out string? folder))
            {
                DirectoryInfo? parent = Directory.GetParent(folder);
                if (parent is not null)
                    Mt5Paths.UseConnectionsRoot(parent.FullName);
            }
        }
    }

    public IReadOnlyList<Mt5ConnectorSummary> DiscoverConnectors()
    {
        var discovered = new List<(Mt5ConnectorSummary Connector, string Folder)>();

        foreach (string root in Mt5Paths.GetConnectionsRootCandidates())
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string folder in Directory.EnumerateDirectories(root))
                {
                    string connectorId = Path.GetFileName(folder);

                    if (!Mt5Paths.IsValidConnectorId(connectorId))
                        continue;

                    Mt5ConnectorSummary? connector =
                        TryReadConnector(folder, connectorId);

                    if (connector?.IsConnected == true)
                    {
                        discovered.Add((connector, folder));
                        RememberGoodConnector(connector, folder);
                    }
                    else
                    {
                        // V300 replaces heartbeat files through a short-lived
                        // temporary file. A scan can land exactly between the
                        // close and replace operations. Keep the last complete
                        // heartbeat until its normal freshness timeout expires
                        // instead of flashing Offline for one polling cycle.
                        Mt5ConnectorSummary? cached =
                            GetFreshCachedConnector(connectorId, folder);

                        if (cached is not null)
                            discovered.Add((cached, folder));
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        IReadOnlyList<(Mt5ConnectorSummary Connector, string Folder)> active =
            discovered
                .GroupBy(
                    item => item.Connector.ConnectorId,
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(item => item.Connector.UpdatedUnix)
                    .First())
                .OrderByDescending(item => item.Connector.UpdatedUnix)
                .ToArray();

        lock (_connectorFolderSync)
        {
            // Do not clear the map on a transient read failure. Existing
            // entries are removed only after their folder disappears or their
            // cached heartbeat naturally becomes stale.
            foreach ((Mt5ConnectorSummary connector, string folder) in active)
                _connectorFolders[connector.ConnectorId] = folder;

            string[] expired = _connectorFolders
                .Where(item =>
                    !Directory.Exists(item.Value) ||
                    !_lastGoodConnectors.TryGetValue(item.Key, out Mt5ConnectorSummary? cached) ||
                    cached is null ||
                    !cached.IsConnected)
                .Select(item => item.Key)
                .ToArray();

            foreach (string connectorId in expired)
            {
                _connectorFolders.Remove(connectorId);
                _lastGoodConnectors.Remove(connectorId);
            }
        }

        if (active.Count > 0)
        {
            DirectoryInfo? parent = Directory.GetParent(active[0].Folder);
            if (parent is not null)
                Mt5Paths.UseConnectionsRoot(parent.FullName);
        }

        return active
            .Select(item => item.Connector)
            .ToArray();
    }

    public Mt5ConnectorSummary? FindConnector(
        string connectorId)
    {
        string? mappedFolder = null;
        lock (_connectorFolderSync)
            _connectorFolders.TryGetValue(connectorId, out mappedFolder);

        if (!string.IsNullOrWhiteSpace(mappedFolder) && Directory.Exists(mappedFolder))
        {
            Mt5ConnectorSummary? current =
                TryReadConnector(mappedFolder, connectorId);

            if (current?.IsConnected == true)
            {
                RememberGoodConnector(current, mappedFolder);
                return current;
            }

            Mt5ConnectorSummary? cached =
                GetFreshCachedConnector(connectorId, mappedFolder);

            if (cached is not null)
                return cached;
        }

        return DiscoverConnectors()
            .FirstOrDefault(
                item => string.Equals(
                    item.ConnectorId,
                    connectorId,
                    StringComparison.Ordinal));
    }

    private void RememberGoodConnector(
        Mt5ConnectorSummary connector,
        string folder)
    {
        lock (_connectorFolderSync)
        {
            _connectorFolders[connector.ConnectorId] = folder;
            _lastGoodConnectors[connector.ConnectorId] = connector;
        }
    }

    private Mt5ConnectorSummary? GetFreshCachedConnector(
        string connectorId,
        string expectedFolder)
    {
        lock (_connectorFolderSync)
        {
            if (!_lastGoodConnectors.TryGetValue(
                    connectorId,
                    out Mt5ConnectorSummary? cached) ||
                cached is null ||
                !cached.IsConnected)
            {
                return null;
            }

            if (_connectorFolders.TryGetValue(
                    connectorId,
                    out string? mappedFolder) &&
                !string.Equals(
                    mappedFolder,
                    expectedFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return cached;
        }
    }

    public IReadOnlyList<Candle> ReadCandles(
        string connectorId)
    {
        return ReadCandleFile(GetReadableBridgeFilePath(connectorId, "candles.csv"));
    }

    public IEnumerable<Candle> EnumerateCandles(
        string connectorId)
    {
        return EnumerateCandleFile(
            GetReadableBridgeFilePath(connectorId, "candles.csv"));
    }

    public DateTime GetCandlesLastWriteUtc(
        string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "candles.csv");

        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public long GetCandlesFileLength(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "candles.csv");
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }


    public IReadOnlyList<Candle> ReadChartBootstrapCandles(string connectorId)
    {
        return ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "chart_bootstrap.csv"));
    }

    public DateTime GetChartBootstrapLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "chart_bootstrap.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public IReadOnlyList<Candle> ReadRecentM1Candles(string connectorId)
    {
        return ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "m1_recent.csv"));
    }

    public DateTime GetRecentM1LastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "m1_recent.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public IReadOnlyList<Candle> ReadRecentSecondCandles(string connectorId)
    {
        return ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "seconds_recent.csv"));
    }

    public DateTime GetRecentSecondsLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "seconds_recent.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public Candle? ReadLiveSecondCandle(string connectorId)
    {
        IReadOnlyList<Candle> candles = ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "second_live.csv"));
        return candles.Count == 0 ? null : candles[^1];
    }

    public DateTime GetLiveSecondLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "second_live.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public Candle? ReadClosedSecondCandle(string connectorId)
    {
        IReadOnlyList<Candle> candles = ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "second_closed.csv"));
        return candles.Count == 0 ? null : candles[^1];
    }

    public DateTime GetClosedSecondLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "second_closed.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public Candle? ReadLiveCandle(
        string connectorId)
    {
        string path =
            GetFilePath(
                connectorId,
                "candle_live.csv",
                forReading: true);

        if(!File.Exists(path))
            return null;

        try
        {
            using FileStream stream =
                OpenSharedRead(path);

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true);

            bool firstLine = true;
            Candle? liveCandle = null;

            while(!reader.EndOfStream)
            {
                string? line =
                    reader.ReadLine();

                if(string.IsNullOrWhiteSpace(line))
                    continue;

                if(firstLine)
                {
                    firstLine = false;
                    continue;
                }

                Candle? parsed =
                    TryParseCandle(line);

                if(parsed is not null)
                    liveCandle = parsed;
            }

            return liveCandle;
        }
        catch(IOException)
        {
            return null;
        }
        catch(UnauthorizedAccessException)
        {
            return null;
        }
    }

    public DateTime GetLiveCandleLastWriteUtc(
        string connectorId)
    {
        string path =
            GetFilePath(
                connectorId,
                "candle_live.csv",
                forReading: true);

        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public Candle? ReadClosedCandle(string connectorId)
    {
        IReadOnlyList<Candle> candles =
            ReadCandleFile(GetReadableBridgeFilePath(connectorId, "candle_closed.csv"));
        return candles.Count == 0 ? null : candles[^1];
    }

    public DateTime GetClosedCandleLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "candle_closed.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public IReadOnlyList<Candle> ReadAllNativeClosedCandles(string connectorId)
    {
        return ReadCandleFile(
            GetReadableBridgeFilePath(connectorId, "native_closed_all.csv"));
    }

    public DateTime GetAllNativeClosedLastWriteUtc(string connectorId)
    {
        string path = GetReadableBridgeFilePath(connectorId, "native_closed_all.csv");
        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public DateTime GetTickArchiveLastWriteUtc(
        string connectorId)
    {
        return _tickArchiveCache.GetLastWriteUtc(
            GetConnectorFolder(connectorId));
    }

    public IReadOnlyList<Candle> ReadTickArchiveCandles(
        string connectorId,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        bool liveOnly = false,
        int maximumBuckets = 300_000)
    {
        return _tickArchiveCache.ReadCandles(
            connectorId,
            GetConnectorFolder(connectorId),
            symbol,
            digits,
            point,
            timeframe,
            liveOnly,
            maximumBuckets);
    }

    public IReadOnlyList<Candle> ReadTickArchiveCandlesFromFolder(
        string cacheKey,
        string folder,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe)
    {
        return _tickArchiveCache.ReadCandles(
            cacheKey,
            folder,
            symbol,
            digits,
            point,
            timeframe,
            liveOnly: false,
            maximumBuckets: 300_000);
    }

    public void ResetTickArchiveCache(
        string connectorId,
        string? symbol = null)
    {
        _tickArchiveCache.Reset(connectorId, symbol);
    }

    public bool IsHistoryWorkerOnline(string connectorId)
    {
        string path = GetReadableBridgeFilePath(
            connectorId,
            "history_worker_heartbeat.json");

        bool cachedHealthy = false;
        lock (_connectorFolderSync)
        {
            cachedHealthy = _lastGoodHistoryWorkerUtc.TryGetValue(
                connectorId,
                out DateTime observedUtc) &&
                DateTime.UtcNow - observedUtc <= TimeSpan.FromSeconds(30);
        }

        if (!File.Exists(path) ||
            DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromSeconds(20))
        {
            return cachedHealthy;
        }

        try
        {
            using FileStream stream = OpenSharedRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            bool online = root.TryGetProperty("online", out JsonElement onlineElement) &&
                          onlineElement.ValueKind == JsonValueKind.True;
            string role = root.TryGetProperty("role", out JsonElement roleElement)
                ? roleElement.GetString() ?? string.Empty
                : string.Empty;
            string version = root.TryGetProperty("bridge_version", out JsonElement versionElement)
                ? versionElement.GetString() ?? string.Empty
                : string.Empty;
            string heartbeatConnector = root.TryGetProperty("connector_id", out JsonElement connectorElement)
                ? connectorElement.GetString() ?? string.Empty
                : string.Empty;

            bool healthy = online &&
                           string.Equals(role, "history_worker", StringComparison.OrdinalIgnoreCase) &&
                           version.StartsWith("3.5.0", StringComparison.OrdinalIgnoreCase) &&
                           (string.IsNullOrWhiteSpace(heartbeatConnector) ||
                            string.Equals(heartbeatConnector, connectorId, StringComparison.Ordinal));

            if (healthy)
            {
                lock (_connectorFolderSync)
                    _lastGoodHistoryWorkerUtc[connectorId] = DateTime.UtcNow;
            }

            return healthy || cachedHealthy;
        }
        catch (IOException)
        {
            return cachedHealthy;
        }
        catch (UnauthorizedAccessException)
        {
            return cachedHealthy;
        }
        catch (JsonException)
        {
            return cachedHealthy;
        }
    }

    public Mt5HistoryStatus? ReadHistoryStatus(
        string connectorId)
    {
        HistoryStatusDocument? document =
            TryReadBridgeJson<HistoryStatusDocument>(
                GetConnectorFolder(connectorId),
                "history_status.json");

        if (document is null)
            return null;

        return new Mt5HistoryStatus(
            document.RequestId,
            document.Symbol,
            document.Timeframe,
            document.Status,
            document.Synchronized,
            document.ExportedBars,
            document.FirstBarUnix,
            document.LatestBarUnix,
            document.ServerFirstUnix,
            document.SeriesFirstUnix,
            document.TerminalFirstUnix,
            document.TargetFirstUnix,
            document.AvailableFirstUnix,
            document.NativeRangeComplete,
            document.NativeRangePartial,
            document.CoverageReason,
            document.LastErrorCode,
            document.HistorySyncComplete,
            document.LimitedByMaxBars,
            document.TerminalMaxBars,
            document.TargetTotalBars,
            document.ProgressPercent,
            document.CurrentBarUnix,
            document.CurrentBlockStartUnix,
            document.CurrentBlockEndUnix,
            document.SpeedBarsPerSecond,
            document.RetryCount,
            document.FailureCode,
            document.FailureStage,
            document.FailureExpectedBars,
            document.FailureActualBars,
            document.FailureExpectedFirstUnix,
            document.FailureActualFirstUnix,
            document.FailureExpectedLatestUnix,
            document.FailureActualLatestUnix,
            document.FailureFilePath,
            document.Message,
            document.UpdatedUnix);
    }

    public IReadOnlyList<Mt5SymbolInfo> ReadSymbols(
        string connectorId)
    {
        string path =
            GetFilePath(
                connectorId,
                "symbols.psv",
                forReading: true);

        if (!File.Exists(path))
            return Array.Empty<Mt5SymbolInfo>();

        var symbols =
            new List<Mt5SymbolInfo>();

        try
        {
            using FileStream stream =
                OpenSharedRead(path);

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true);

            bool firstLine = true;

            while (!reader.EndOfStream)
            {
                string? line =
                    reader.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                string[] fields =
                    line.Split('|');

                if (fields.Length != 7)
                    continue;

                for (int index = 0;
                     index < fields.Length;
                     index++)
                {
                    fields[index] =
                        NormalizePsvField(
                            fields[index]);
                }

                if (string.IsNullOrWhiteSpace(fields[0]) ||
                    !bool.TryParse(
                        fields[3].Trim(),
                        out bool selected) ||
                    !bool.TryParse(
                        fields[4].Trim(),
                        out bool visible) ||
                    !bool.TryParse(
                        fields[5].Trim(),
                        out bool custom) ||
                    !TryInt(
                        fields[6],
                        out int digits))
                {
                    continue;
                }

                symbols.Add(
                    new Mt5SymbolInfo(
                        fields[0].Trim(),
                        fields[1].Trim(),
                        fields[2].Trim(),
                        selected,
                        visible,
                        custom,
                        digits));
            }
        }
        catch (IOException)
        {
            return Array.Empty<Mt5SymbolInfo>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<Mt5SymbolInfo>();
        }

        return symbols
            .OrderByDescending(
                item =>
                    item.IsSelectedInMarketWatch)
            .ThenBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DateTime GetSymbolsLastWriteUtc(
        string connectorId)
    {
        string path =
            GetFilePath(
                connectorId,
                "symbols.psv",
                forReading: true);

        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.MinValue;
    }

    public string RequestSymbolsRefresh(
        string connectorId)
    {
        if (!Mt5Paths.IsValidConnectorId(
                connectorId))
        {
            throw new InvalidOperationException(
                "The MT5 connector is invalid.");
        }

        string requestId =
            Guid.NewGuid().ToString("N");

        var request =
            new SymbolListRequestDocument
            {
                RequestId = requestId,
                ConnectorId = connectorId,
                RequestedUnix =
                    DateTimeOffset.UtcNow
                        .ToUnixTimeSeconds()
            };

        string json =
            JsonSerializer.Serialize(
                request,
                JsonOptions);

        WriteAtomicText(
            GetFilePath(
                connectorId,
                "symbols_request.json"),
            json);

        return requestId;
    }

    public string SendChartSelectionRequest(
        string connectorId,
        string symbol,
        string timeframe)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(symbol) ||
            string.IsNullOrWhiteSpace(timeframe))
        {
            throw new InvalidOperationException(
                "The MT5 chart selection is invalid.");
        }

        string requestId =
            Guid.NewGuid().ToString("N");

        var request =
            new ChartSelectionRequestDocument
            {
                RequestId = requestId,
                ConnectorId = connectorId,
                Symbol = symbol.Trim(),
                Timeframe = timeframe.Trim(),
                RequestedUnix =
                    DateTimeOffset.UtcNow
                        .ToUnixTimeSeconds()
            };

        string json =
            JsonSerializer.Serialize(
                request,
                JsonOptions);

        WriteAtomicText(
            GetFilePath(
                connectorId,
                "chart_request.json"),
            json);

        return requestId;
    }

    public string SendHistoryRequest(
        string connectorId,
        string action,
        string symbol,
        string timeframe,
        bool includeTicks,
        long minimumTickMilliseconds = 0,
        long minimumCandleUnix = 0,
        bool includeCandles = true)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(action) ||
            string.IsNullOrWhiteSpace(symbol) ||
            string.IsNullOrWhiteSpace(timeframe))
        {
            throw new InvalidOperationException(
                "The MT5 history request is invalid.");
        }

        string requestId = Guid.NewGuid().ToString("N");
        var request = new HistoryRequestDocument
        {
            RequestId = requestId,
            ConnectorId = connectorId,
            Action = action.Trim().ToLowerInvariant(),
            Symbol = symbol.Trim(),
            Timeframe = timeframe.Trim(),
            IncludeTicks = includeTicks ? 1 : 0,
            IncludeCandles = includeCandles ? 1 : 0,
            MinimumTickMilliseconds = Math.Max(0, minimumTickMilliseconds),
            MinimumCandleUnix = Math.Max(0, minimumCandleUnix),
            RequestedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        WriteAtomicText(
            GetFilePath(connectorId, "history_request.json"),
            JsonSerializer.Serialize(request, JsonOptions));

        return requestId;
    }

    public string SendHistoryControl(
        string connectorId,
        string requestId,
        string action)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(action))
        {
            throw new InvalidOperationException(
                "The MT5 history control request is invalid.");
        }

        string controlId = Guid.NewGuid().ToString("N");
        var request = new HistoryControlRequestDocument
        {
            ControlId = controlId,
            ConnectorId = connectorId,
            RequestId = requestId?.Trim() ?? string.Empty,
            Action = action.Trim().ToLowerInvariant(),
            RequestedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        WriteAtomicText(
            GetFilePath(connectorId, "history_control.json"),
            JsonSerializer.Serialize(request, JsonOptions));

        return controlId;
    }

    public string SendTickRequest(
        Mt5ConnectorSummary connector,
        Candle candle)
    {
        if (!Mt5Paths.IsValidConnectorId(connector.ConnectorId) ||
            string.IsNullOrWhiteSpace(candle.Symbol) ||
            string.IsNullOrWhiteSpace(candle.Timeframe))
        {
            throw new InvalidOperationException(
                "The selected connector or candle is invalid.");
        }

        long startMilliseconds = candle.StartUnix * 1000;
        long endMilliseconds = candle.EndUnix * 1000 - 1;

        if (endMilliseconds <= startMilliseconds)
        {
            throw new InvalidOperationException(
                "The selected candle has an invalid time range.");
        }

        string requestId = Guid.NewGuid().ToString("N");

        var request = new TickRequestDocument
        {
            RequestId = requestId,
            ConnectorId = connector.ConnectorId,
            Symbol = candle.Symbol,
            Timeframe = candle.Timeframe,
            StartMilliseconds = startMilliseconds,
            EndMilliseconds = endMilliseconds,
            RequestedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        string json = JsonSerializer.Serialize(request, JsonOptions);
        WriteAtomicText(
            GetFilePath(connector.ConnectorId, "tick_request.json"),
            json);

        return requestId;
    }

    public TickResponseResult TryReadTickResponse(
        string connectorId,
        string requestId)
    {
        string selectionPath =
            GetReadableBridgeFilePath(connectorId, "selection.json");

        TickSelectionDocument? selection =
            TryReadJson<TickSelectionDocument>(selectionPath);

        if (selection is null ||
            !string.Equals(
                selection.RequestId,
                requestId,
                StringComparison.Ordinal))
        {
            return TickResponseResult.Pending;
        }

        if (!string.Equals(
                selection.Status,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            string message = string.IsNullOrWhiteSpace(selection.Message)
                ? "MT5 could not read the requested tick history."
                : selection.Message;

            return new TickResponseResult(
                true,
                false,
                message,
                selection.ErrorCode,
                Array.Empty<MarketTick>());
        }

        string ticksFileName =
            string.IsNullOrWhiteSpace(selection.TicksFile)
                ? "ticks.csv"
                : Path.GetFileName(selection.TicksFile);

        string ticksPath =
            GetReadableBridgeFilePath(connectorId, ticksFileName);

        IReadOnlyList<MarketTick> ticks =
            ReadTicks(ticksPath, requestId);

        if (ticks.Count != selection.TickCount)
            return TickResponseResult.Pending;

        return new TickResponseResult(
            true,
            true,
            selection.Message,
            0,
            ticks);
    }

    public string GetConnectorFolder(string connectorId)
    {
        lock (_connectorFolderSync)
        {
            if (_connectorFolders.TryGetValue(connectorId, out string? folder))
                return folder;
        }

        return Mt5Paths.GetConnectorFolder(connectorId);
    }

    private static Mt5ConnectorSummary? TryReadConnector(
        string folder,
        string expectedConnectorId)
    {
        ConnectionDocument? connection =
            TryReadBridgeJson<ConnectionDocument>(
                folder,
                "connection.json");

        const string dedicatedLiveHeartbeat = "live_channel_heartbeat.json";
        bool dedicatedHeartbeatPresent =
            HasNonEmptyFile(Path.Combine(folder, dedicatedLiveHeartbeat)) ||
            HasNonEmptyFile(Path.Combine(folder, dedicatedLiveHeartbeat + ".tmp"));
        bool dedicatedHeartbeatRequired =
            connection?.BridgeVersion?.StartsWith(
                "3.0.0",
                StringComparison.OrdinalIgnoreCase) == true;

        // V300 uses a dedicated live heartbeat. This prevents V270/V290 or a
        // wrongly configured second EA from overwriting the status consumed by
        // the desktop. Fall back to heartbeat.json only for older bridge setups.
        string heartbeatFileName =
            dedicatedHeartbeatPresent || dedicatedHeartbeatRequired
                ? dedicatedLiveHeartbeat
                : "heartbeat.json";

        string heartbeatPath =
            ResolveReadableBridgeFilePath(
                folder,
                heartbeatFileName);

        HeartbeatDocument? heartbeat =
            TryReadBridgeJson<HeartbeatDocument>(
                folder,
                heartbeatFileName);

        // The bridge writes through .tmp files before replacing the final
        // files. Some MT5 installations leave only one of the two documents
        // available temporarily, so discovery must not reject the connector
        // merely because one metadata file is missing or stale.
        if (connection is null && heartbeat is null)
            return null;

        if (connection is not null && connection.ProtocolVersion != 2)
            return null;

        if (heartbeat is not null && heartbeat.ProtocolVersion != 2)
            return null;

        if (connection is null && heartbeat is not null)
        {
            connection = new ConnectionDocument
            {
                ProtocolVersion = 2,
                ConnectorId = expectedConnectorId,
                ConnectorName = expectedConnectorId,
                BridgeVersion = heartbeat.BridgeVersion
            };
        }

        if (heartbeat is null && connection is not null)
        {
            long metadataUnix =
                GetFileUnix(
                    ResolveReadableBridgeFilePath(
                        folder,
                        "connection.json"));

            heartbeat = new HeartbeatDocument
            {
                ProtocolVersion = 2,
                ConnectorId = expectedConnectorId,
                BridgeVersion = connection.BridgeVersion,
                TerminalConnected = true,
                AccountConnected = connection.AccountLogin > 0,
                UpdatedUnix = metadataUnix
            };
        }

        if (connection is null || heartbeat is null)
            return null;

        bool connectionIdMatches =
            string.IsNullOrWhiteSpace(connection.ConnectorId) ||
            string.Equals(
                connection.ConnectorId,
                expectedConnectorId,
                StringComparison.Ordinal);

        bool heartbeatIdMatches =
            string.IsNullOrWhiteSpace(heartbeat.ConnectorId) ||
            string.Equals(
                heartbeat.ConnectorId,
                expectedConnectorId,
                StringComparison.Ordinal);

        if (!connectionIdMatches && !heartbeatIdMatches)
            return null;

        string bridgeVersion =
            !string.IsNullOrWhiteSpace(heartbeat.BridgeVersion)
                ? heartbeat.BridgeVersion
                : connection.BridgeVersion;

        long heartbeatFileUnix = GetFileUnix(heartbeatPath);

        long effectiveHeartbeatUnix =
            Math.Max(
                heartbeat.UpdatedUnix,
                heartbeatFileUnix);

        long dataActivityUnix =
            GetLatestBridgeActivityUnix(
                folder,
                heartbeatPath);

        bool hasDataAvailable =
            HasUsableBridgeData(folder);

        return new Mt5ConnectorSummary(
            expectedConnectorId,
            string.IsNullOrWhiteSpace(connection.ConnectorName)
                ? expectedConnectorId
                : connection.ConnectorName,
            bridgeVersion,
            connection.Broker,
            connection.Server,
            connection.TerminalBuild,
            connection.AccountLogin,
            heartbeat.Symbol,
            heartbeat.Timeframe,
            heartbeat.Digits,
            heartbeat.Point,
            heartbeat.TerminalConnected,
            heartbeat.AccountConnected,
            effectiveHeartbeatUnix,
            dataActivityUnix,
            hasDataAvailable,
            heartbeat.ServerUtcOffsetMinutes,
            heartbeat.TickSize);
    }

    private static long GetFileUnix(string path)
    {
        try
        {
            return File.Exists(path)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(path))
                    .ToUnixTimeSeconds()
                : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool HasUsableBridgeData(
        string folder)
    {
        try
        {
            if (HasBridgeOutput(folder, "candles.csv") ||
                HasBridgeOutput(folder, "candle_live.csv"))
            {
                return true;
            }

            string[] usefulPatterns =
            {
                "ticks_live_*.csv",
                "ticks_history_*.csv",
                "market_book_*.csv",
                "trade_transactions_*.csv",
                "capture_status.json",
                "history_status.json",
                "runtime_state.json",
                "symbol_snapshot.json",
                "symbols.psv",
                "symbols_request.json",
                "chart_selection.json",
                "*.json.tmp",
                "*.csv.tmp",
                "*.psv.tmp"
            };

            foreach (string pattern in usefulPatterns)
            {
                foreach (string path in
                         Directory.EnumerateFiles(
                             folder,
                             pattern,
                             SearchOption.TopDirectoryOnly))
                {
                    if (HasNonEmptyFile(path))
                        return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool HasBridgeOutput(
        string folder,
        string fileName)
    {
        string path = Path.Combine(folder, fileName);
        return HasNonEmptyFile(path) ||
               HasNonEmptyFile(path + ".tmp");
    }

    private static bool HasNonEmptyFile(
        string path)
    {
        try
        {
            return
                File.Exists(path) &&
                new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long GetLatestBridgeActivityUnix(
        string folder,
        string heartbeatPath)
    {
        long latestUnix = 0;

        try
        {
            foreach (string path in
                     Directory.EnumerateFiles(
                         folder,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                string fileName =
                    Path.GetFileName(path);

                // connection.json is mostly metadata. Heartbeat has its own
                // dedicated freshness calculation above.
                if (string.Equals(
                        fileName,
                        "connection.json",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        fileName,
                        "connection.json.tmp",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        path,
                        heartbeatPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long modifiedUnix =
                    new DateTimeOffset(
                        File.GetLastWriteTimeUtc(path))
                    .ToUnixTimeSeconds();

                latestUnix =
                    Math.Max(
                        latestUnix,
                        modifiedUnix);
            }
        }
        catch (IOException)
        {
            return latestUnix;
        }
        catch (UnauthorizedAccessException)
        {
            return latestUnix;
        }

        return latestUnix;
    }

    private static string NormalizePsvField(
        string value)
    {
        string normalized =
            value.Trim();

        if (normalized.Length >= 2 &&
            normalized[0] == '"' &&
            normalized[^1] == '"')
        {
            normalized =
                normalized[1..^1]
                    .Replace(
                        "\"\"",
                        "\"",
                        StringComparison.Ordinal);
        }

        return normalized;
    }

    private static IEnumerable<Candle> EnumerateCandleFile(string path)
    {
        if (!File.Exists(path))
            yield break;

        using FileStream stream = OpenSharedRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        bool firstLine = true;

        while (!reader.EndOfStream)
        {
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (firstLine)
            {
                firstLine = false;
                continue;
            }

            Candle? candle = TryParseCandle(line);
            if (candle is not null)
                yield return candle;
        }
    }

    private static IReadOnlyList<Candle> ReadCandleFile(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<Candle>();

        var candles = new List<Candle>();
        try
        {
            using FileStream stream = OpenSharedRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            bool firstLine = true;
            while (!reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                Candle? candle = TryParseCandle(line);
                if (candle is not null)
                    candles.Add(candle);
            }
        }
        catch (IOException) { return Array.Empty<Candle>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<Candle>(); }

        return candles;
    }

    private static Candle? TryParseCandle(string line)
    {
        IReadOnlyList<string> fields = CsvLineParser.Parse(line);

        if (fields.Count != 15)
            return null;

        if (!TryInt(fields[2], out int digits) ||
            !TryDouble(fields[3], out double point) ||
            !TryLong(fields[4], out long startUnix) ||
            !TryLong(fields[5], out long endUnix) ||
            !TryDouble(fields[7], out double open) ||
            !TryDouble(fields[8], out double high) ||
            !TryDouble(fields[9], out double low) ||
            !TryDouble(fields[10], out double close) ||
            !TryLong(fields[11], out long tickVolume) ||
            !TryInt(fields[12], out int spread) ||
            !TryLong(fields[13], out long realVolume) ||
            !bool.TryParse(fields[14].Trim(), out bool isClosed))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(fields[0]) ||
            string.IsNullOrWhiteSpace(fields[1]) ||
            point <= 0 ||
            endUnix <= startUnix)
        {
            return null;
        }

        return new Candle(
            fields[0].Trim(),
            fields[1].Trim(),
            digits,
            point,
            startUnix,
            endUnix,
            fields[6].Trim(),
            open,
            high,
            low,
            close,
            tickVolume,
            spread,
            realVolume,
            isClosed);
    }

    public IReadOnlyList<MarketTick> ReadLiveRawTicksSince(
        string connectorId,
        string symbol,
        long afterMilliseconds,
        int maximumRecords = 50_000)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(symbol) ||
            maximumRecords <= 0)
        {
            return Array.Empty<MarketTick>();
        }

        string folder = GetConnectorFolder(connectorId);
        if (!Directory.Exists(folder))
            return Array.Empty<MarketTick>();

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        string[] files = Directory
            .EnumerateFiles(folder, $"ticks_live_{safeSymbol}_*.csv", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .TakeLast(2)
            .ToArray();
        if (files.Length == 0)
            return Array.Empty<MarketTick>();

        var ticks = new List<MarketTick>(Math.Min(maximumRecords, 50_000));
        foreach (string path in files)
        {
            try
            {
                using FileStream stream = OpenSharedRead(path);
                // Live tick files can grow to many MB. For an incremental live
                // read we only need the tail; archived history uses ticks.tlt.
                const long tailBytes = 16L * 1024 * 1024;
                if (afterMilliseconds > 0 && stream.Length > tailBytes)
                {
                    stream.Seek(stream.Length - tailBytes, SeekOrigin.Begin);
                    using var discard = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
                    _ = discard.ReadLine();
                    while (!discard.EndOfStream)
                    {
                        string? line = discard.ReadLine();
                        if (TryParseLiveRawTick(line, symbol, afterMilliseconds, out MarketTick tick))
                            ticks.Add(tick);
                    }
                }
                else
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
                    _ = reader.ReadLine(); // header
                    while (!reader.EndOfStream)
                    {
                        string? line = reader.ReadLine();
                        if (TryParseLiveRawTick(line, symbol, afterMilliseconds, out MarketTick tick))
                            ticks.Add(tick);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // OrderBy is stable, so raw events that share the same millisecond keep
        // their original bridge-file sequence. Keep the newest records when the
        // live tail is larger than the requested window.
        return ticks
            .OrderBy(item => item.TimeMilliseconds)
            .TakeLast(maximumRecords)
            .ToArray();
    }

    private static bool TryParseLiveRawTick(
        string? line,
        string symbol,
        long afterMilliseconds,
        out MarketTick tick)
    {
        tick = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;
        IReadOnlyList<string> fields = CsvLineParser.Parse(line);
        if (fields.Count < 11 ||
            !string.Equals(fields[2].Trim(), symbol, StringComparison.OrdinalIgnoreCase) ||
            !TryLong(fields[3], out long timeMilliseconds) ||
            timeMilliseconds < afterMilliseconds ||
            !TryDouble(fields[5], out double bid) ||
            !TryDouble(fields[6], out double ask) ||
            !TryDouble(fields[7], out double last) ||
            !TryDouble(fields[8], out double volume) ||
            !uint.TryParse(fields[9].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint flags) ||
            !TryDouble(fields[10], out double volumeReal))
        {
            return false;
        }
        double display = bid > 0 ? bid : last > 0 ? last : ask;
        if (!double.IsFinite(display) || display <= 0)
            return false;
        tick = new MarketTick(
            timeMilliseconds,
            timeMilliseconds / 1000,
            bid,
            ask,
            last,
            Math.Max(0, volume),
            flags,
            Math.Max(0, volumeReal));
        return true;
    }

    private static IReadOnlyList<MarketTick> ReadTicks(
        string path,
        string requestId)
    {
        if (!File.Exists(path))
            return Array.Empty<MarketTick>();

        var ticks = new List<MarketTick>();

        try
        {
            using FileStream stream = OpenSharedRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);

            bool firstLine = true;

            while (!reader.EndOfStream)
            {
                string? line = reader.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                IReadOnlyList<string> fields = CsvLineParser.Parse(line);

                if (fields.Count != 9 ||
                    !string.Equals(
                        fields[0].Trim(),
                        requestId,
                        StringComparison.Ordinal) ||
                    !TryLong(fields[1], out long timeMilliseconds) ||
                    !TryLong(fields[2], out long timeUnix) ||
                    !TryDouble(fields[3], out double bid) ||
                    !TryDouble(fields[4], out double ask) ||
                    !TryDouble(fields[5], out double last) ||
                    !TryDouble(fields[6], out double volume) ||
                    !uint.TryParse(
                        fields[7].Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint flags) ||
                    !TryDouble(fields[8], out double volumeReal))
                {
                    continue;
                }

                ticks.Add(
                    new MarketTick(
                        timeMilliseconds,
                        timeUnix,
                        bid,
                        ask,
                        last,
                        volume,
                        flags,
                        volumeReal));
            }
        }
        catch (IOException)
        {
            return Array.Empty<MarketTick>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<MarketTick>();
        }

        return ticks;
    }

    private string GetFilePath(
        string connectorId,
        string fileName,
        bool forReading = false)
    {
        string safeFileName = Path.GetFileName(fileName);
        string folder = GetConnectorFolder(connectorId);

        return forReading
            ? ResolveReadableBridgeFilePath(folder, safeFileName)
            : Path.Combine(folder, safeFileName);
    }

    private string GetReadableBridgeFilePath(
        string connectorId,
        string fileName)
    {
        string safeFileName = Path.GetFileName(fileName);
        return ResolveReadableBridgeFilePath(
            GetConnectorFolder(connectorId),
            safeFileName);
    }

    // Bridge V300 first writes <file>.tmp and then replaces the final file.
    // A few MT5 installations leave the newest output as .tmp when their
    // Common Files FileMove operation is denied. Reading the newest non-empty
    // final/temporary output keeps both bridge generations compatible.
    private static string ResolveReadableBridgeFilePath(
        string folder,
        string fileName)
    {
        string finalPath = Path.Combine(folder, fileName);
        string temporaryPath = finalPath + ".tmp";

        bool finalExists = HasNonEmptyFile(finalPath);
        bool temporaryExists = HasNonEmptyFile(temporaryPath);

        if (!temporaryExists)
            return finalPath;

        if (!finalExists)
            return temporaryPath;

        try
        {
            return File.GetLastWriteTimeUtc(temporaryPath) >
                   File.GetLastWriteTimeUtc(finalPath)
                ? temporaryPath
                : finalPath;
        }
        catch (IOException)
        {
            return finalPath;
        }
        catch (UnauthorizedAccessException)
        {
            return finalPath;
        }
    }

    private static T? TryReadBridgeJson<T>(
        string folder,
        string fileName)
    {
        string finalPath = Path.Combine(folder, fileName);
        string temporaryPath = finalPath + ".tmp";
        string preferred = ResolveReadableBridgeFilePath(folder, fileName);

        T? value = TryReadJson<T>(preferred);
        if (value is not null)
            return value;

        string alternate = string.Equals(
            preferred,
            finalPath,
            StringComparison.OrdinalIgnoreCase)
            ? temporaryPath
            : finalPath;

        return TryReadJson<T>(alternate);
    }

    private static void WriteAtomicText(
        string targetPath,
        string contents)
    {
        string? folder = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(folder) ||
            !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                "The selected MT5 connector folder was not found.");
        }

        object writeLock = AtomicWriteLocks.GetOrAdd(
            Path.GetFullPath(targetPath),
            static _ => new object());

        lock (writeLock)
        {
            WriteAtomicTextLocked(targetPath, folder, contents);
        }
    }

    private static void WriteAtomicTextLocked(
        string targetPath,
        string folder,
        string contents)
    {
        // A unique temporary name prevents two TickLab actions from sharing the
        // same .tmp file. It remains in the connector folder so a successful
        // rename stays on the same volume.
        string temporaryPath = Path.Combine(
            folder,
            $".{Path.GetFileName(targetPath)}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}.tmp");

        byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
        Exception? lastError = null;

        try
        {
            WriteCompleteFile(temporaryPath, bytes);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    ClearReadOnlyAttribute(targetPath);
                    File.Move(temporaryPath, targetPath, true);
                    return;
                }
                catch (Exception exception) when (IsBridgeWriteSharingFailure(exception))
                {
                    lastError = exception;

                    // MT5 may have the old request open with read/write sharing
                    // but without delete sharing. In that state MoveFile is
                    // denied although writing the small request itself is safe.
                    // Its JSON reader already treats a short in-progress parse as
                    // pending and retries on the next bridge timer tick.
                    try
                    {
                        WriteCompleteFile(targetPath, bytes);
                        return;
                    }
                    catch (Exception directException) when (IsBridgeWriteSharingFailure(directException))
                    {
                        lastError = directException;
                    }

                    Thread.Sleep(Math.Min(250, 20 * (attempt + 1)));
                }
            }
        }
        finally
        {
            TryDeleteTemporaryBridgeFile(temporaryPath);
        }

        throw new IOException(
            "The MT5 request folder is temporarily locked or is not writable. " +
            "Keep MT5 open, allow TickLab through antivirus/Controlled Folder Access, " +
            "and verify that the connector folder permits file changes.",
            lastError);
    }

    private static void WriteCompleteFile(
        string path,
        byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);

        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool IsBridgeWriteSharingFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static void ClearReadOnlyAttribute(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteTemporaryBridgeFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static T? TryReadJson<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            using FileStream stream = OpenSharedRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static FileStream OpenSharedRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

    private static bool TryInt(string text, out int value) =>
        int.TryParse(
            text.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryLong(string text, out long value) =>
        long.TryParse(
            text.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}
