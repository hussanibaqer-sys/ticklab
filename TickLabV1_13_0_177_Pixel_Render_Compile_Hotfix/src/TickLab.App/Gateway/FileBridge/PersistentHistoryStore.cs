using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TickLab.Core.History;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class PersistentHistoryStore
{
    private const int RecordSize = 69;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    private readonly object _sync = new();
    private readonly object _tickSync = new();
    private readonly CanonicalTickArchiveStore _canonicalTicks = new();
    private readonly NativeCandleArchiveStore _nativeCandles;
    private readonly HistoryVisibilityStore _visibility;
    private readonly string _rootPath;

    public PersistentHistoryStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        _rootPath = Path.Combine(localAppData, "TickLab", "History");
        Directory.CreateDirectory(_rootPath);
        _nativeCandles = new NativeCandleArchiveStore(_rootPath);
        _visibility = new HistoryVisibilityStore(_rootPath);
    }

    public string RootPath => _rootPath;

    public string GetConnectorHistoryFolder(string connectorId)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(connectorId));

        string folder = Path.Combine(_rootPath, Sanitize(connectorId));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void SetNativeAvailabilityBoundary(
        string connectorId,
        string symbol,
        string timeframe,
        long firstAvailableUnix)
    {
        if (firstAvailableUnix <= 0)
            return;

        lock (_sync)
        {
            string path = GetNativeBoundaryPath(connectorId, symbol);
            NativeBoundarySettings settings = ReadJson<NativeBoundarySettings>(path)
                ?? new NativeBoundarySettings(
                    new Dictionary<string, NativeBoundaryEntry>(StringComparer.Ordinal));
            var boundaries = (settings.Boundaries ??
                    new Dictionary<string, NativeBoundaryEntry>())
                .ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal);
            boundaries[timeframe] = new NativeBoundaryEntry(
                firstAvailableUnix,
                DateTime.UtcNow);
            WriteJsonAtomic(path, new NativeBoundarySettings(boundaries));
        }
    }

    public long? GetNativeAvailabilityBoundary(
        string connectorId,
        string symbol,
        string timeframe)
    {
        NativeBoundarySettings? settings = ReadJson<NativeBoundarySettings>(
            GetNativeBoundaryPath(connectorId, symbol));
        return settings?.Boundaries is not null &&
               settings.Boundaries.TryGetValue(timeframe, out NativeBoundaryEntry? entry) &&
               entry is not null
            ? entry.FirstAvailableUnix
            : null;
    }

    public PortableHistoryScanResult RescanPortableHistory(string connectorId)
    {
        string connectorFolder = GetConnectorHistoryFolder(connectorId);
        InstrumentSettings settings = ReadInstrumentSettings(connectorId);
        var instruments = settings.Instruments.ToList();
        int discovered = 0;
        int alreadyKnown = 0;

        lock (_sync)
        {
            foreach (string symbolFolder in Directory.EnumerateDirectories(connectorFolder))
            {
                string symbol = Path.GetFileName(symbolFolder);
                if (string.Equals(symbol, "_settings", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool hasM1 = (Directory.Exists(Path.Combine(symbolFolder, "CandleHistory")) &&
                    Directory.EnumerateFiles(
                        Path.Combine(symbolFolder, "CandleHistory"),
                        "PERIOD_M1.tlc",
                        SearchOption.TopDirectoryOnly).Any()) ||
                    (Directory.Exists(Path.Combine(symbolFolder, "PERIOD_M1")) &&
                    Directory.EnumerateFiles(
                        Path.Combine(symbolFolder, "PERIOD_M1"),
                        "candles.tlc",
                        SearchOption.AllDirectories).Any());
                bool hasTicks = Directory.Exists(Path.Combine(symbolFolder, "_ticks")) &&
                    Directory.EnumerateFiles(
                        Path.Combine(symbolFolder, "_ticks"),
                        "ticks.tlt",
                        SearchOption.AllDirectories).Any();
                if (!hasM1 && !hasTicks)
                    continue;

                int index = instruments.FindIndex(item =>
                    string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    alreadyKnown++;
                    continue;
                }

                instruments.Add(new SavedInstrumentState(symbol, true, DateTime.UtcNow));
                discovered++;
            }

            if (discovered > 0)
            {
                WriteJsonAtomic(
                    GetInstrumentSettingsPath(connectorId),
                    new InstrumentSettings(instruments));
            }
        }

        return new PortableHistoryScanResult(discovered, alreadyKnown, connectorFolder);
    }

    public HistoryImportResult ImportCandles(
        string connectorId,
        IReadOnlyList<Candle> candles,
        CancellationToken cancellationToken = default,
        IProgress<HistoryImportProgress>? progress = null,
        bool copyTickArchives = true,
        int serverUtcOffsetMinutes = 0,
        long? minimumStartUnix = null,
        string? onlySegmentKey = null)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(connectorId));

        if (candles.Count == 0)
        {
            return new HistoryImportResult(
                false,
                "MT5 did not provide candle history for this chart.",
                string.Empty,
                string.Empty,
                0,
                0);
        }

        Candle first = candles[0];
        string symbol = first.Symbol.Trim();
        string timeframe = first.Timeframe.Trim();

        if (candles.Any(candle =>
                !string.Equals(candle.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candle.Timeframe, timeframe, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The imported MT5 candle file contains mixed symbols or timeframes.");
        }

        HistoryImportResult result = ImportCandleStream(
            connectorId,
            candles,
            symbol,
            timeframe,
            cancellationToken,
            progress,
            expectedRecords: candles.Count,
            serverUtcOffsetMinutes: serverUtcOffsetMinutes,
            minimumStartUnix: minimumStartUnix,
            onlySegmentKey: onlySegmentKey);

        if (copyTickArchives && result.Success)
        {
            SyncTickArchives(
                connectorId,
                symbol,
                cancellationToken,
                includeHistorical: true,
                minimumStartUnix: minimumStartUnix,
                onlySegmentKey: onlySegmentKey,
                serverUtcOffsetMinutes: serverUtcOffsetMinutes);
        }

        return result;
    }

    public HistoryImportResult ImportCandleStream(
        string connectorId,
        IEnumerable<Candle> candles,
        string expectedSymbol,
        string expectedTimeframe,
        CancellationToken cancellationToken = default,
        IProgress<HistoryImportProgress>? progress = null,
        long expectedRecords = 0,
        int serverUtcOffsetMinutes = 0,
        long? minimumStartUnix = null,
        string? onlySegmentKey = null)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(connectorId));

        _ = serverUtcOffsetMinutes;
        HistoryImportResult result = _nativeCandles.Import(
            connectorId,
            candles,
            expectedSymbol,
            expectedTimeframe,
            cancellationToken,
            progress,
            expectedRecords,
            minimumStartUnix,
            onlySegmentKey);

        if (result.Success)
        {
            lock (_sync)
                SetInstrumentSavingInternal(connectorId, expectedSymbol, true);
        }

        return result;
    }

    public IReadOnlyList<Candle> ReadCandles(
        string connectorId,
        string symbol,
        string timeframe,
        HistoryLoadSelection? selection = null,
        int maximumRecords = int.MaxValue)
    {
        IReadOnlyList<Candle> flatNative = _nativeCandles.HasData(connectorId, symbol, timeframe)
            ? _nativeCandles.Read(connectorId, symbol, timeframe, maximumRecords)
            : Array.Empty<Candle>();
        if (!string.Equals(timeframe, "PERIOD_M1", StringComparison.Ordinal))
            return flatNative;

        selection ??= HistoryLoadSelection.RecentThreeMonths;
        maximumRecords = Math.Max(1, maximumRecords);
        string datasetFolder = GetDatasetFolder(connectorId, symbol, timeframe);

        if (!Directory.Exists(datasetFolder))
            return flatNative;

        DatasetMetadata? metadata = ReadJson<DatasetMetadata>(
            Path.Combine(datasetFolder, "dataset.json"));

        if (metadata is null)
            return flatNative;

        string[] segmentFolders = Directory
            .EnumerateDirectories(datasetFolder)
            .Where(path => File.Exists(Path.Combine(path, "candles.tlc")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        long? cutoff = null;
        if (selection.Mode == HistoryDisplayMode.RecentThreeMonths)
        {
            cutoff = DateTimeOffset.UtcNow
                .AddMonths(-3)
                .ToUnixTimeSeconds();

            segmentFolders = segmentFolders
                .Where(path =>
                {
                    SegmentMetadata? segment = ReadSegmentMetadata(path);
                    return segment is not null && segment.LatestUnix >= cutoff.Value;
                })
                .ToArray();
        }
        else if (selection.Mode == HistoryDisplayMode.SelectedSegments)
        {
            var selected = new HashSet<string>(
                selection.SegmentKeys ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            segmentFolders = segmentFolders
                .Where(path => selected.Contains(Path.GetFileName(path)))
                .ToArray();
        }

        var result = new List<Candle>(Math.Min(maximumRecords, 64_000));

        lock (_sync)
        {
            // Read newest folders first and seek directly to the tail of each
            // binary segment. This keeps all-history selections responsive
            // without loading millions of records into the UI process.
            for (int index = segmentFolders.Length - 1;
                 index >= 0 && result.Count < maximumRecords;
                 index--)
            {
                int remaining = maximumRecords - result.Count;
                ReadSegmentTail(segmentFolders[index], metadata, result, remaining);
            }
        }

        IEnumerable<Candle> ordered = result
            .OrderBy(candle => candle.StartUnix)
            .GroupBy(candle => candle.StartUnix)
            .Select(items => items.Last());

        if (cutoff.HasValue)
            ordered = ordered.Where(candle => candle.StartUnix >= cutoff.Value);

        return HistoryIntegrityService
            .MergeWithPriority(ordered.ToArray(), flatNative)
            .TakeLast(maximumRecords)
            .ToArray();
    }

    public IReadOnlyList<Candle> ReadFirstCandles(
        string connectorId,
        string symbol,
        string timeframe,
        int maximumRecords)
    {
        if (!_nativeCandles.HasData(connectorId, symbol, timeframe))
            return Array.Empty<Candle>();

        return _nativeCandles.ReadFirst(
            connectorId,
            symbol,
            timeframe,
            Math.Max(1, maximumRecords));
    }

    public IReadOnlyList<Candle> ReadCandlesBefore(
        string connectorId,
        string symbol,
        string timeframe,
        long beforeUnix,
        HistoryLoadSelection? selection = null,
        int maximumRecords = 200_000)
    {
        IReadOnlyList<Candle> flatNative = _nativeCandles.HasData(connectorId, symbol, timeframe)
            ? _nativeCandles.ReadBefore(connectorId, symbol, timeframe, beforeUnix, maximumRecords)
            : Array.Empty<Candle>();
        if (!string.Equals(timeframe, "PERIOD_M1", StringComparison.Ordinal))
            return flatNative;

        selection ??= HistoryLoadSelection.RecentThreeMonths;
        maximumRecords = Math.Max(1, maximumRecords);
        string datasetFolder = GetDatasetFolder(connectorId, symbol, timeframe);

        if (!Directory.Exists(datasetFolder))
            return flatNative;

        DatasetMetadata? metadata = ReadJson<DatasetMetadata>(
            Path.Combine(datasetFolder, "dataset.json"));

        if (metadata is null)
            return flatNative;

        string[] segmentFolders = Directory
            .EnumerateDirectories(datasetFolder)
            .Where(path => File.Exists(Path.Combine(path, "candles.tlc")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        long? cutoff = null;
        if (selection.Mode == HistoryDisplayMode.RecentThreeMonths)
        {
            cutoff = DateTimeOffset.UtcNow
                .AddMonths(-3)
                .ToUnixTimeSeconds();

            segmentFolders = segmentFolders
                .Where(path =>
                {
                    SegmentMetadata? segment = ReadSegmentMetadata(path);
                    return segment is not null && segment.LatestUnix >= cutoff.Value;
                })
                .ToArray();
        }
        else if (selection.Mode == HistoryDisplayMode.SelectedSegments)
        {
            var selected = new HashSet<string>(
                selection.SegmentKeys ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            segmentFolders = segmentFolders
                .Where(path => selected.Contains(Path.GetFileName(path)))
                .ToArray();
        }

        var result = new List<Candle>(Math.Min(maximumRecords, 64_000));

        lock (_sync)
        {
            for (int index = segmentFolders.Length - 1;
                 index >= 0 && result.Count < maximumRecords;
                 index--)
            {
                SegmentMetadata? segment = ReadSegmentMetadata(segmentFolders[index]);
                if (segment is null || segment.EarliestUnix >= beforeUnix)
                    continue;

                int remaining = maximumRecords - result.Count;
                ReadSegmentBeforeTail(
                    segmentFolders[index],
                    metadata,
                    beforeUnix,
                    result,
                    remaining);
            }
        }

        IEnumerable<Candle> ordered = result
            .Where(candle => candle.StartUnix < beforeUnix)
            .OrderBy(candle => candle.StartUnix)
            .GroupBy(candle => candle.StartUnix)
            .Select(items => items.Last());

        if (cutoff.HasValue)
            ordered = ordered.Where(candle => candle.StartUnix >= cutoff.Value);

        return HistoryIntegrityService
            .MergeWithPriority(ordered.ToArray(), flatNative)
            .TakeLast(maximumRecords)
            .ToArray();
    }

    public void UpsertLiveCandle(
        string connectorId,
        Candle candle,
        int serverUtcOffsetMinutes = 0)
    {
        _ = serverUtcOffsetMinutes;
        if (candle.IsClosed && TimeframeDefinition.NativeMt5Timeframes.Contains(candle.Timeframe, StringComparer.Ordinal))
        {
            _nativeCandles.UpsertClosed(connectorId, candle);
            return;
        }

        // Forming candles are display-only. Live Bridge permanently stores raw ticks only.
        if (!candle.IsClosed)
            return;

        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(candle.Symbol) ||
            !string.Equals(candle.Timeframe, "PERIOD_M1", StringComparison.Ordinal))
        {
            return;
        }

        string segmentKey = GetSegmentKey(candle.StartUnix, serverUtcOffsetMinutes);
        string segmentFolder = GetSegmentFolder(
            connectorId,
            candle.Symbol,
            candle.Timeframe,
            segmentKey);

        lock (_sync)
        {
            Directory.CreateDirectory(segmentFolder);
            string dataPath = Path.Combine(segmentFolder, "candles.tlc");
            SegmentMetadata? metadata = ReadSegmentMetadata(segmentFolder);

            if (metadata is null || !File.Exists(dataPath))
            {
                WriteSegmentAtomic(segmentFolder, new[] { candle });
                WriteDatasetMetadata(
                    connectorId,
                    candle.Symbol,
                    candle.Timeframe,
                    candle.Digits,
                    candle.Point);
                return;
            }

            using var stream = new FileStream(
                dataPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read);

            if (metadata.Count > 0 && stream.Length >= RecordSize)
            {
                stream.Seek(-RecordSize, SeekOrigin.End);
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                long lastStart = reader.ReadInt64();

                if (lastStart == candle.StartUnix)
                {
                    stream.Seek(-RecordSize, SeekOrigin.End);
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                    WriteCandleRecord(writer, candle);
                }
                else if (lastStart < candle.StartUnix)
                {
                    stream.Seek(0, SeekOrigin.End);
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                    WriteCandleRecord(writer, candle);
                    metadata = metadata with
                    {
                        Count = metadata.Count + 1,
                        LatestUnix = candle.StartUnix
                    };
                }
                else
                {
                    return;
                }
            }
            else
            {
                stream.Seek(0, SeekOrigin.End);
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                WriteCandleRecord(writer, candle);
                metadata = metadata with
                {
                    Count = Math.Max(1, metadata.Count),
                    EarliestUnix = candle.StartUnix,
                    LatestUnix = candle.StartUnix
                };
            }

            WriteJsonAtomic(
                Path.Combine(segmentFolder, "segment.json"),
                metadata with
                {
                    UpdatedUtc = DateTime.UtcNow
                });
        }
    }

    public IReadOnlyList<HistoryDatasetSummary> GetDatasets(
        string? connectorId = null)
    {
        var results = new List<HistoryDatasetSummary>();
        IEnumerable<string> connectorFolders;

        if (!string.IsNullOrWhiteSpace(connectorId))
        {
            string folder = Path.Combine(_rootPath, Sanitize(connectorId));
            connectorFolders = Directory.Exists(folder)
                ? new[] { folder }
                : Array.Empty<string>();
        }
        else
        {
            connectorFolders = Directory.Exists(_rootPath)
                ? Directory.EnumerateDirectories(_rootPath)
                : Array.Empty<string>();
        }

        foreach (string connectorFolder in connectorFolders)
        {
            string id = Path.GetFileName(connectorFolder);
            foreach (string symbolFolder in Directory.EnumerateDirectories(connectorFolder))
            {
                string symbol = Path.GetFileName(symbolFolder);
                if (string.Equals(symbol, "_settings", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (string timeframeFolder in Directory.EnumerateDirectories(symbolFolder))
                {
                    DatasetMetadata? metadata = ReadJson<DatasetMetadata>(
                        Path.Combine(timeframeFolder, "dataset.json"));

                    if (metadata is null)
                        continue;

                    HistorySegmentSummary[] segments = GetSegments(
                        id,
                        metadata.Symbol,
                        metadata.Timeframe)
                        .ToArray();

                    results.Add(
                        new HistoryDatasetSummary(
                            id,
                            metadata.Symbol,
                            metadata.Timeframe,
                            segments.Sum(segment => segment.RecordCount),
                            segments.Length == 0 ? 0 : segments.Min(segment => segment.EarliestUnix),
                            segments.Length == 0 ? 0 : segments.Max(segment => segment.LatestUnix),
                            segments.Sum(segment => segment.SizeBytes),
                            IsInstrumentSaving(id, metadata.Symbol),
                            segments));
                }
            }
        }

        return results
            .OrderBy(item => item.ConnectorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Timeframe, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<HistorySegmentSummary> GetSegments(
        string connectorId,
        string symbol,
        string timeframe)
    {
        string datasetFolder = GetDatasetFolder(connectorId, symbol, timeframe);

        if (!Directory.Exists(datasetFolder))
            return Array.Empty<HistorySegmentSummary>();

        var segments = new List<HistorySegmentSummary>();

        foreach (string folder in Directory.EnumerateDirectories(datasetFolder))
        {
            SegmentMetadata? metadata = ReadSegmentMetadata(folder);
            if (metadata is null)
                continue;

            long size = Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path).Length)
                .Sum();

            string candlePath = Path.Combine(folder, "candles.tlc");
            long dataLength = File.Exists(candlePath)
                ? new FileInfo(candlePath).Length
                : -1;
            bool healthy = dataLength >= 0 &&
                           dataLength % RecordSize == 0 &&
                           dataLength / RecordSize == metadata.Count;

            segments.Add(
                new HistorySegmentSummary(
                    Path.GetFileName(folder),
                    metadata.EarliestUnix,
                    metadata.LatestUnix,
                    metadata.Count,
                    size,
                    healthy ? "Healthy" : "Corrupted"));
        }

        return segments
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<SavedInstrumentState> GetSavedInstruments(
        string connectorId)
    {
        InstrumentSettings settings = ReadInstrumentSettings(connectorId);
        return settings.Instruments
            .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void SetInstrumentSaving(
        string connectorId,
        string symbol,
        bool enabled)
    {
        lock (_sync)
            SetInstrumentSavingInternal(connectorId, symbol, enabled);
    }

    public bool IsInstrumentSaving(
        string connectorId,
        string symbol)
    {
        return ReadInstrumentSettings(connectorId)
            .Instruments
            .Any(item =>
                item.Enabled &&
                string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    public void DeleteDataset(
        string connectorId,
        string symbol,
        string? timeframe = null)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(timeframe))
                _nativeCandles.DeleteTimeframe(connectorId, symbol, timeframe);

            string path = string.IsNullOrWhiteSpace(timeframe)
                ? GetSymbolFolder(connectorId, symbol)
                : GetDatasetFolder(connectorId, symbol, timeframe);

            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    public void DeleteSegment(
        string connectorId,
        string symbol,
        string timeframe,
        string segmentKey)
    {
        lock (_sync)
        {
            string path = GetSegmentFolder(
                connectorId,
                symbol,
                timeframe,
                segmentKey);

            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    public void DeleteAll(string connectorId)
    {
        lock (_sync)
        {
            string path = Path.Combine(_rootPath, Sanitize(connectorId));
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    public string GetNativeCandleHistoryFolder(string connectorId, string symbol) =>
        _nativeCandles.GetFolder(connectorId, symbol);

    public NativeCandleFileSummary? GetNativeCandleFile(
        string connectorId,
        string symbol,
        string timeframe) =>
        _nativeCandles.GetSummary(connectorId, symbol, timeframe);

    public IReadOnlyList<NativeCandleFileSummary> GetNativeCandleFiles(
        string connectorId,
        string? symbol = null) =>
        _nativeCandles.GetSummaries(connectorId, symbol);

    public void DeleteNativeCandleTimeframe(
        string connectorId,
        string symbol,
        string timeframe)
    {
        _nativeCandles.DeleteTimeframe(connectorId, symbol, timeframe);
        _visibility.RemoveNative(connectorId, symbol, timeframe);
    }

    public void DeleteNativeCandleSymbol(string connectorId, string symbol)
    {
        _nativeCandles.DeleteSymbol(connectorId, symbol);
        _visibility.RemoveNative(connectorId, symbol);
    }

    public bool IsNativeCandleVisible(
        string connectorId,
        string symbol,
        string timeframe) =>
        _visibility.IsNativeVisible(connectorId, symbol, timeframe);

    public void SetNativeCandleVisible(
        string connectorId,
        string symbol,
        string timeframe,
        bool visible) =>
        _visibility.SetNativeVisible(connectorId, symbol, timeframe, visible);

    public void SetAllNativeCandleVisibility(
        string connectorId,
        string symbol,
        bool visible)
    {
        foreach (string timeframe in TimeframeDefinition.NativeMt5Timeframes)
            _visibility.SetNativeVisible(connectorId, symbol, timeframe, visible);
    }

    public bool IsTickHistoryVisible(
        string connectorId,
        string symbol,
        string segmentKey) =>
        _visibility.IsTickVisible(connectorId, symbol, segmentKey);

    public void SetTickHistoryVisible(
        string connectorId,
        string symbol,
        string segmentKey,
        bool visible) =>
        _visibility.SetTickVisible(connectorId, symbol, segmentKey, visible);

    public void SetAllTickHistoryVisibility(
        string connectorId,
        string symbol,
        bool visible)
    {
        foreach (TickHistoryFolderSummary folder in GetTickHistoryFolders(connectorId, symbol))
            _visibility.SetTickVisible(connectorId, symbol, folder.SegmentKey, visible);
    }

    public IReadOnlyList<HiddenHistoryRange> GetHiddenTickHistoryRanges(
        string connectorId,
        string symbol) =>
        GetTickHistoryFolders(connectorId, symbol)
            .Where(item => !item.IsVisible && item.StartUnix > 0 && item.EndUnix > item.StartUnix)
            .Select(item => new HiddenHistoryRange(item.SegmentKey, item.StartUnix, item.EndUnix))
            .OrderBy(item => item.StartUnix)
            .ToArray();

    public string GetTickArchiveFolder(
        string connectorId,
        string symbol)
    {
        return Path.Combine(GetSymbolFolder(connectorId, symbol), "_ticks");
    }

    public IReadOnlyList<TickHistoryFolderSummary> GetTickHistoryFolders(
        string connectorId,
        string? symbol = null)
    {
        string connectorFolder = GetConnectorHistoryFolder(connectorId);
        if (!Directory.Exists(connectorFolder))
            return Array.Empty<TickHistoryFolderSummary>();

        IEnumerable<string> symbolFolders = string.IsNullOrWhiteSpace(symbol)
            ? Directory.EnumerateDirectories(connectorFolder)
            : new[] { GetSymbolFolder(connectorId, symbol) };

        var result = new List<TickHistoryFolderSummary>();
        foreach (string symbolFolder in symbolFolders)
        {
            string tickRoot = Path.Combine(symbolFolder, "_ticks");
            if (!Directory.Exists(tickRoot))
                continue;

            string displaySymbol = Path.GetFileName(symbolFolder);
            foreach (string segmentFolder in Directory.EnumerateDirectories(tickRoot))
            {
                string path = Path.Combine(segmentFolder, "ticks.tlt");
                if (!File.Exists(path))
                    continue;

                var info = new FileInfo(path);
                string key = Path.GetFileName(segmentFolder);
                (long start, long end) = ParseSegmentRange(key);
                CanonicalTickCoverage actualCoverage = _canonicalTicks.GetCoverage(
                    segmentFolder,
                    CancellationToken.None);
                long actualEarliestUnix = actualCoverage.HasData
                    ? actualCoverage.EarliestTimeMilliseconds / 1000L
                    : 0;
                long actualLatestUnix = actualCoverage.HasData
                    ? actualCoverage.LatestTimeMilliseconds / 1000L
                    : 0;
                result.Add(new TickHistoryFolderSummary(
                    displaySymbol,
                    key,
                    start,
                    end,
                    info.Length,
                    path,
                    info.Length > 0 && actualCoverage.HasData ? "OK" : "Empty",
                    _visibility.IsTickVisible(connectorId, displaySymbol, key),
                    actualEarliestUnix,
                    actualLatestUnix));
            }
        }

        return result
            .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SegmentKey, StringComparer.Ordinal)
            .ToArray();
    }

    public void DeleteAllTickHistoryForSymbol(
        string connectorId,
        string symbol)
    {
        string folder = GetTickArchiveFolder(connectorId, symbol);
        lock (_tickSync)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        _visibility.RemoveTick(connectorId, symbol);
    }

    public void DeleteTickHistoryFolder(
        string connectorId,
        string symbol,
        string segmentKey)
    {
        string folder = Path.Combine(
            GetTickArchiveFolder(connectorId, symbol),
            Sanitize(segmentKey));
        lock (_tickSync)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        _visibility.RemoveTick(connectorId, symbol, segmentKey);
    }

    public TickProjectionResult BuildMissingNativeCandlesFromTicks(
        string connectorId,
        string symbol,
        string segmentKey,
        int digits,
        double point,
        int serverUtcOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        (long startUnix, long endUnix) = ParseSegmentRange(segmentKey);
        if (startUnix <= 0 || endUnix <= startUnix)
            throw new InvalidDataException("The selected three-month tick folder has an invalid date range.");

        string tickRoot = GetTickArchiveFolder(connectorId, symbol);
        if (!Directory.Exists(Path.Combine(tickRoot, segmentKey)))
            throw new DirectoryNotFoundException("The selected tick history folder does not exist.");

        long generated = 0;
        long inserted = 0;
        int completed = 0;
        foreach (string code in TimeframeDefinition.NativeMt5Timeframes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeframeDefinition timeframe = TimeframeDefinition.FromNativeMt5Code(code);
            long duration = Math.Max(1, timeframe.ToApproximateSeconds());
            int maximum = (int)Math.Clamp(
                ((endUnix - startUnix) / duration) + 32,
                32,
                300_000);

            IReadOnlyList<Candle> projected = _canonicalTicks.ReadCandles(
                tickRoot,
                symbol,
                digits,
                point,
                timeframe,
                maximum,
                serverUtcOffsetMinutes,
                beforeUnix: endUnix,
                minimumUnix: startUnix,
                cancellationToken: cancellationToken)
                .Where(candle => candle.StartUnix >= startUnix && candle.StartUnix < endUnix)
                .Select(candle => candle with { Timeframe = code })
                .ToArray();

            generated += projected.Count;
            if (projected.Count == 0)
            {
                completed++;
                continue;
            }

            IReadOnlyList<Candle> existing = _nativeCandles.ReadBefore(
                connectorId,
                symbol,
                code,
                endUnix,
                maximum + 32);
            var existingStarts = existing
                .Where(candle => candle.StartUnix >= startUnix)
                .Select(candle => candle.StartUnix)
                .ToHashSet();
            Candle[] missing = projected
                .Where(candle => !existingStarts.Contains(candle.StartUnix))
                .ToArray();

            if (missing.Length > 0)
            {
                HistoryImportResult result = _nativeCandles.Import(
                    connectorId,
                    missing,
                    symbol,
                    code,
                    cancellationToken,
                    null,
                    missing.Length,
                    startUnix,
                    segmentKey);
                if (result.Success)
                    inserted += result.ImportedRecords;
            }

            completed++;
        }

        if (inserted > 0)
        {
            lock (_sync)
                SetInstrumentSavingInternal(connectorId, symbol, true);
        }

        return new TickProjectionResult(
            symbol,
            segmentKey,
            completed,
            generated,
            inserted,
            inserted > 0
                ? $"Built {inserted:N0} missing candles from saved ticks without replacing native MT5 candles."
                : "No missing native candle slots were found in this tick folder.");
    }

    private static (long StartUnix, long EndUnix) ParseSegmentRange(string segmentKey)
    {
        string[] parts = segmentKey.Split(
            "_to_",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) ||
            !DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end))
        {
            return (0, 0);
        }

        long first = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds();
        long after = new DateTimeOffset(DateTime.SpecifyKind(end.AddDays(1), DateTimeKind.Utc)).ToUnixTimeSeconds();
        return (first, after);
    }

    public long GetDatasetSizeBytes(
        string connectorId,
        string symbol)
    {
        string folder = GetSymbolFolder(connectorId, symbol);
        if (!Directory.Exists(folder))
            return 0;

        return Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path).Length)
            .Sum();
    }

    public void SyncTickArchives(
        string connectorId,
        string symbol,
        CancellationToken cancellationToken = default,
        bool includeHistorical = false,
        int serverUtcOffsetMinutes = 0,
        long? minimumStartUnix = null,
        string? onlySegmentKey = null,
        long? maximumEndUnix = null,
        bool includeRecentAndLive = true,
        bool forceHistoricalReindex = false)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) ||
            string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        string sourceFolder = Mt5Paths.GetConnectorFolder(connectorId);
        if (!Directory.Exists(sourceFolder))
            return;

        string targetRoot = GetTickArchiveFolder(connectorId, symbol);
        Directory.CreateDirectory(targetRoot);

        lock (_tickSync)
        {
            _canonicalTicks.SyncFromBridge(
                sourceFolder,
                targetRoot,
                symbol,
                includeHistorical,
                includeRecentAndLive,
                forceHistoricalReindex,
                serverUtcOffsetMinutes,
                minimumStartUnix,
                onlySegmentKey,
                maximumEndUnix,
                cancellationToken);
        }
    }

    private static IReadOnlyList<string> GetBridgeHistoricalSourceFolders(
        string connectorId)
    {
        var folders = new List<string>();

        void AddFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                return;
            }

            if (!Directory.Exists(fullPath) ||
                folders.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            folders.Add(fullPath);
        }

        // Prefer the connector root currently selected by TickLab, then search
        // every other valid MT5 Common-Files root. Older historical backfill
        // snapshots can legitimately remain in a previous/discovered root even
        // while the live bridge is now using another one.
        AddFolder(Mt5Paths.GetConnectorFolder(connectorId));
        foreach (string root in Mt5Paths.GetConnectionsRootCandidates())
            AddFolder(Path.Combine(root, connectorId));

        return folders;
    }

    private void SyncTickArchivesFromBridgeFolder(
        string sourceFolder,
        string connectorId,
        string symbol,
        CancellationToken cancellationToken,
        int serverUtcOffsetMinutes,
        long minimumStartUnix,
        long maximumEndUnix)
    {
        if (!Directory.Exists(sourceFolder) ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }

        string targetRoot = GetTickArchiveFolder(connectorId, symbol);
        Directory.CreateDirectory(targetRoot);

        lock (_tickSync)
        {
            _canonicalTicks.SyncFromBridge(
                sourceFolder,
                targetRoot,
                symbol,
                includeHistorical: true,
                includeRecentAndLive: false,
                forceHistoricalReindex: true,
                serverUtcOffsetMinutes,
                minimumStartUnix,
                onlySegmentKey: null,
                maximumEndUnix,
                cancellationToken);
        }
    }

    public CanonicalTickCoverage GetBridgeHistoricalTickSourceCoverage(
        string connectorId,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickCoverage.Empty;

        long earliest = long.MaxValue;
        long latest = long.MinValue;
        foreach (string sourceFolder in GetBridgeHistoricalSourceFolders(connectorId))
        {
            if (cancellationToken.IsCancellationRequested)
                return CanonicalTickCoverage.Empty;

            CanonicalTickCoverage coverage =
                _canonicalTicks.GetBridgeHistoricalSourceCoverage(
                    sourceFolder,
                    GetTickArchiveFolder(connectorId, symbol),
                    symbol,
                    cancellationToken);
            if (!coverage.HasData)
                continue;

            earliest = Math.Min(earliest, coverage.EarliestTimeMilliseconds);
            latest = Math.Max(latest, coverage.LatestTimeMilliseconds);
        }

        return earliest == long.MaxValue || latest == long.MinValue
            ? CanonicalTickCoverage.Empty
            : new CanonicalTickCoverage(true, earliest, latest);
    }

    public CanonicalTickCoverage GetTickCoverageForReplay(
        string connectorId,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickCoverage.Empty;

        return _canonicalTicks.GetCoverage(
            GetTickArchiveFolder(connectorId, symbol),
            cancellationToken);
    }

    public CanonicalTickReadResult ReadBridgeTicksForReplayFast(
        string connectorId,
        string symbol,
        long startMilliseconds,
        long endMillisecondsExclusive,
        int maximumRecords = 250_000,
        int serverUtcOffsetMinutes = 0,
        CancellationToken cancellationToken = default,
        bool takeLatest = false,
        bool allowFullIndexRebuild = true)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickReadResult.Empty;

        string sourceFolder = Mt5Paths.GetConnectorFolder(connectorId);
        return _canonicalTicks.ReadBridgeTicksForReplay(
            sourceFolder,
            GetTickArchiveFolder(connectorId, symbol),
            symbol,
            startMilliseconds,
            endMillisecondsExclusive,
            Math.Clamp(maximumRecords, 1, 2_000_000),
            serverUtcOffsetMinutes,
            cancellationToken,
            takeLatest,
            allowFullIndexRebuild);
    }

    public CanonicalTickReadResult ReadTicksForReplay(
        string connectorId,
        string symbol,
        long startMilliseconds,
        long? endMilliseconds = null,
        int maximumRecords = 250_000,
        CancellationToken cancellationToken = default)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickReadResult.Empty;

        return _canonicalTicks.ReadTicks(
            GetTickArchiveFolder(connectorId, symbol),
            startMilliseconds,
            endMilliseconds,
            Math.Clamp(maximumRecords, 1, 2_000_000),
            cancellationToken);
    }

    public CanonicalTickReadResult ReadTicksBeforeForReplay(
        string connectorId,
        string symbol,
        long endMillisecondsExclusive,
        int maximumRecords = 250_000,
        CancellationToken cancellationToken = default,
        long minimumMilliseconds = 0)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickReadResult.Empty;

        return _canonicalTicks.ReadTicksBefore(
            GetTickArchiveFolder(connectorId, symbol),
            endMillisecondsExclusive,
            Math.Clamp(maximumRecords, 1, 2_000_000),
            cancellationToken,
            minimumMilliseconds);
    }

    public CanonicalTickReadResult ReadBridgeTicksBeforeForReplayFast(
        string connectorId,
        string symbol,
        long startMilliseconds,
        long endMillisecondsExclusive,
        int maximumRecords = 250_000,
        int serverUtcOffsetMinutes = 0,
        CancellationToken cancellationToken = default,
        bool allowFullIndexRebuild = false)
    {
        return ReadBridgeTicksForReplayFast(
            connectorId,
            symbol,
            startMilliseconds,
            endMillisecondsExclusive,
            maximumRecords,
            serverUtcOffsetMinutes,
            cancellationToken,
            takeLatest: true,
            allowFullIndexRebuild: allowFullIndexRebuild);
    }

    public IReadOnlyList<Candle> ReadRecentM1FromTickArchives(
        string connectorId,
        string symbol,
        int digits,
        double point,
        long minimumStartUnix,
        int serverUtcOffsetMinutes = 0,
        CancellationToken cancellationToken = default)
    {
        string tickFolder = GetTickArchiveFolder(connectorId, symbol);
        var timeframe = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)
            ?? TimeframeDefinition.CreateCustom(1, TimeframeUnit.Minute);

        return _canonicalTicks.ReadCandles(
            tickFolder,
            symbol,
            digits,
            point,
            timeframe,
            maximumRecords: 180,
            serverUtcOffsetMinutes: serverUtcOffsetMinutes,
            beforeUnix: null,
            minimumUnix: minimumStartUnix,
            cancellationToken: cancellationToken);
    }

    public IReadOnlyList<Candle> ReadSecondCandlesFromTickArchives(
        string connectorId,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        int maximumRecords = 300_000,
        int serverUtcOffsetMinutes = 0,
        CancellationToken cancellationToken = default,
        long? beforeUnix = null,
        IReadOnlySet<string>? excludedSegmentKeys = null)
    {
        if (timeframe.Unit != TimeframeUnit.Second)
            throw new ArgumentException("A second timeframe is required.", nameof(timeframe));

        return _canonicalTicks.ReadCandles(
            GetTickArchiveFolder(connectorId, symbol),
            symbol,
            digits,
            point,
            timeframe,
            maximumRecords: maximumRecords,
            serverUtcOffsetMinutes: serverUtcOffsetMinutes,
            beforeUnix: beforeUnix,
            minimumUnix: null,
            cancellationToken: cancellationToken,
            excludedSegmentKeys: excludedSegmentKeys);
    }

    public IReadOnlyList<Candle> ReadSecondCandlesOnDemand(
        string connectorId,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        int maximumRecords = 1_600,
        int serverUtcOffsetMinutes = 0,
        CancellationToken cancellationToken = default,
        long? beforeUnix = null,
        IReadOnlySet<string>? excludedSegmentKeys = null,
        long? focusUnix = null)
    {
        IReadOnlyList<Candle> canonical = ReadSecondCandlesFromTickArchives(
            connectorId,
            symbol,
            digits,
            point,
            timeframe,
            maximumRecords,
            serverUtcOffsetMinutes,
            cancellationToken,
            beforeUnix,
            excludedSegmentKeys);

        long? focusBucketStart = focusUnix.HasValue
            ? timeframe.GetBucketStartUnix(focusUnix.Value, serverUtcOffsetMinutes)
            : null;

        bool CoversFocus(IReadOnlyList<Candle> candles)
        {
            if (!focusBucketStart.HasValue)
                return true;
            if (candles.Count == 0)
                return false;

            // We only need the virtual page to span the requested historical
            // moment. The exact 1-second bucket can legitimately be empty when
            // no market tick occurred in that second; Find Candle performs its
            // own exact-candle check after this surrounding page is generated.
            return candles[0].StartUnix <= focusBucketStart.Value &&
                   candles[^1].StartUnix >= focusBucketStart.Value;
        }


        static IReadOnlyList<Candle> MergeSecondPages(
            IReadOnlyList<Candle> primary,
            IReadOnlyList<Candle> secondary,
            int maximum)
        {
            if (secondary.Count == 0)
                return primary.Count <= maximum
                    ? primary
                    : primary.TakeLast(maximum).ToArray();
            if (primary.Count == 0)
                return secondary.Count <= maximum
                    ? secondary
                    : secondary.TakeLast(maximum).ToArray();

            var merged = new SortedDictionary<long, Candle>();
            foreach (Candle candle in secondary)
                merged[candle.StartUnix] = candle;
            // Canonical ticks remain authoritative when both sources contain
            // the same bucket. The bridge source is only an immediate fallback
            // while canonical indexing catches up.
            foreach (Candle candle in primary)
                merged[candle.StartUnix] = candle;

            return merged.Values.TakeLast(maximum).ToArray();
        }

        IReadOnlyList<Candle> best = canonical;

        IReadOnlyList<Candle> MergeAroundFocus(
            IReadOnlyList<Candle> primary,
            IReadOnlyList<Candle> secondary)
        {
            if (!focusBucketStart.HasValue)
                return MergeSecondPages(primary, secondary, maximumRecords);

            var merged = new SortedDictionary<long, Candle>();
            foreach (Candle candle in secondary)
                merged[candle.StartUnix] = candle;
            // Canonical archive remains authoritative on duplicate buckets.
            foreach (Candle candle in primary)
                merged[candle.StartUnix] = candle;

            Candle[] values = merged.Values.ToArray();
            if (values.Length <= maximumRecords)
                return values;

            int low = 0;
            int high = values.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (values[middle].StartUnix < focusBucketStart.Value)
                    low = middle + 1;
                else
                    high = middle;
            }

            int desiredLeft = maximumRecords / 2;
            int start = Math.Clamp(low - desiredLeft, 0, values.Length - maximumRecords);
            return values.Skip(start).Take(maximumRecords).ToArray();
        }

        // Find Candle on a seconds chart is a direct timestamp operation. Look
        // only at the raw source snapshots nearest focusUnix and return that
        // centered page. Never walk backward through each historical window.
        if (focusUnix.HasValue && !cancellationToken.IsCancellationRequested)
        {
            foreach (string sourceFolder in GetBridgeHistoricalSourceFolders(connectorId))
            {
                IReadOnlyList<Candle> directFocused =
                    _canonicalTicks.ReadHistoricalSourceCandlesAroundTimestamp(
                        sourceFolder,
                        GetTickArchiveFolder(connectorId, symbol),
                        symbol,
                        digits,
                        point,
                        timeframe,
                        maximumRecords,
                        serverUtcOffsetMinutes,
                        focusUnix.Value,
                        cancellationToken,
                        excludedSegmentKeys);
                best = MergeAroundFocus(best, directFocused);
                if (CoversFocus(best))
                    break;
            }

            // A missing/closed-market timestamp should fail quickly. Do not turn
            // it into a multi-minute sequential repair scan.
            return best;
        }

        // If a historical page is not already complete in ticks.tlt, read the
        // exact bounded bridge snapshots directly. This removes the old
        // ~30-minute wall caused by waiting for background canonical indexing.
        // The read remains page-bounded and never projects the whole archive.
        if (beforeUnix.HasValue &&
            !cancellationToken.IsCancellationRequested &&
            (best.Count < maximumRecords || !CoversFocus(best)))
        {
            foreach (string sourceFolder in GetBridgeHistoricalSourceFolders(connectorId))
            {
                IReadOnlyList<Candle> direct = _canonicalTicks.ReadHistoricalSourceCandles(
                    sourceFolder,
                    GetTickArchiveFolder(connectorId, symbol),
                    symbol,
                    digits,
                    point,
                    timeframe,
                    maximumRecords,
                    serverUtcOffsetMinutes,
                    beforeUnix.Value,
                    cancellationToken,
                    excludedSegmentKeys);
                best = MergeSecondPages(best, direct, maximumRecords);
                if (best.Count >= maximumRecords && CoversFocus(best))
                    break;
            }
        }

        // The launch page can be satisfied by newest canonical ticks. A direct
        // historical Find is different: 1,600 canonical candles from some older
        // area do NOT prove the requested timestamp is indexed. Keep repairing
        // until the returned page actually spans focusUnix. This is what lets a
        // never-before-opened 1s date be generated immediately from raw ticks.
        if (!beforeUnix.HasValue ||
            cancellationToken.IsCancellationRequested ||
            (best.Count >= maximumRecords && CoversFocus(best)))
        {
            return best;
        }

        // Foreground seconds navigation must be read-only and page-bounded.
        // Older versions imported/reindexed raw snapshots into ticks.tlt here,
        // contending with Tick Chart reads and freezing both chart types. Fill a
        // sparse page by reading progressively older completed snapshots directly;
        // canonical archival synchronization remains a separate background concern.
        long searchBeforeUnix = best.Count > 0
            ? Math.Min(beforeUnix.Value, best[0].StartUnix)
            : beforeUnix.Value;
        var visitedBefore = new HashSet<long>();
        IReadOnlyList<string> sourceFolders = GetBridgeHistoricalSourceFolders(connectorId);

        while (!cancellationToken.IsCancellationRequested && best.Count < maximumRecords)
        {
            if (searchBeforeUnix <= 0 || !visitedBefore.Add(searchBeforeUnix))
                break;

            long nextBeforeUnix = searchBeforeUnix;
            int countBefore = best.Count;
            long earliestBefore = best.Count > 0 ? best[0].StartUnix : long.MaxValue;

            foreach (string sourceFolder in sourceFolders)
            {
                IReadOnlyList<Candle> olderDirect =
                    _canonicalTicks.ReadHistoricalSourceCandles(
                        sourceFolder,
                        GetTickArchiveFolder(connectorId, symbol),
                        symbol,
                        digits,
                        point,
                        timeframe,
                        maximumRecords,
                        serverUtcOffsetMinutes,
                        searchBeforeUnix,
                        cancellationToken,
                        excludedSegmentKeys);
                if (olderDirect.Count == 0)
                    continue;

                best = MergeSecondPages(best, olderDirect, maximumRecords);
                long directEarliest = olderDirect[0].StartUnix;
                if (directEarliest < nextBeforeUnix)
                    nextBeforeUnix = directEarliest;

                if (best.Count >= maximumRecords)
                    break;
            }

            if (best.Count >= maximumRecords)
                break;

            long earliestAfter = best.Count > 0 ? best[0].StartUnix : long.MaxValue;
            bool madeProgress = best.Count > countBefore || earliestAfter < earliestBefore;
            if (!madeProgress || nextBeforeUnix >= searchBeforeUnix)
                break;

            searchBeforeUnix = nextBeforeUnix;
        }

        return best;
    }

    private void WriteDatasetMetadata(
        string connectorId,
        string symbol,
        string timeframe,
        int digits,
        double point)
    {
        string folder = GetDatasetFolder(connectorId, symbol, timeframe);
        Directory.CreateDirectory(folder);

        WriteJsonAtomic(
            Path.Combine(folder, "dataset.json"),
            new DatasetMetadata(
                connectorId,
                symbol,
                timeframe,
                digits,
                point,
                DateTime.UtcNow));
    }

    private static void WriteSegmentAtomic(
        string segmentFolder,
        IReadOnlyList<Candle> candles)
    {
        string dataPath = Path.Combine(segmentFolder, "candles.tlc");
        string temporary = dataPath + ".tmp";

        using (var stream = new FileStream(
                   temporary,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            foreach (Candle candle in candles)
                WriteCandleRecord(writer, candle);

            writer.Flush();
            stream.Flush(true);
        }

        File.Move(temporary, dataPath, true);

        WriteJsonAtomic(
            Path.Combine(segmentFolder, "segment.json"),
            new SegmentMetadata(
                candles.Count,
                candles.Count == 0 ? 0 : candles[0].StartUnix,
                candles.Count == 0 ? 0 : candles[^1].StartUnix,
                DateTime.UtcNow));
    }

    private static void ReadSegment(
        string segmentFolder,
        DatasetMetadata metadata,
        ICollection<Candle> destination)
    {
        string path = Path.Combine(segmentFolder, "candles.tlc");
        if (!File.Exists(path))
            return;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);

        while (stream.Position + RecordSize <= stream.Length)
            destination.Add(ReadCandleRecord(reader, metadata));
    }

    private static void WriteCandleRecord(
        BinaryWriter writer,
        Candle candle)
    {
        writer.Write(candle.StartUnix);
        writer.Write(candle.EndUnix);
        writer.Write(candle.Open);
        writer.Write(candle.High);
        writer.Write(candle.Low);
        writer.Write(candle.Close);
        writer.Write(candle.TickVolume);
        writer.Write(candle.Spread);
        writer.Write(candle.RealVolume);
        writer.Write(candle.IsClosed);
    }

    private static Candle ReadCandleRecord(
        BinaryReader reader,
        DatasetMetadata metadata)
    {
        long startUnix = reader.ReadInt64();
        long endUnix = reader.ReadInt64();
        double open = reader.ReadDouble();
        double high = reader.ReadDouble();
        double low = reader.ReadDouble();
        double close = reader.ReadDouble();
        long tickVolume = reader.ReadInt64();
        int spread = reader.ReadInt32();
        long realVolume = reader.ReadInt64();
        bool isClosed = reader.ReadBoolean();

        return new Candle(
            metadata.Symbol,
            metadata.Timeframe,
            metadata.Digits,
            metadata.Point,
            startUnix,
            endUnix,
            DateTimeOffset.FromUnixTimeSeconds(startUnix)
                .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
            open,
            high,
            low,
            close,
            tickVolume,
            spread,
            realVolume,
            isClosed);
    }

    private void MergeImportedSegment(
        string segmentFolder,
        string importedPath,
        long importedCount,
        long importedEarliest,
        long importedLatest)
    {
        string targetPath = Path.Combine(segmentFolder, "candles.tlc");
        if (!File.Exists(targetPath))
        {
            File.Move(importedPath, targetPath, true);
            VerifySegmentFileStructure(
                targetPath, importedCount, importedEarliest, importedLatest);
            WriteJsonAtomic(
                Path.Combine(segmentFolder, "segment.json"),
                new SegmentMetadata(
                    importedCount,
                    importedEarliest,
                    importedLatest,
                    DateTime.UtcNow));
            return;
        }

        string mergedPath = targetPath + ".merge.tmp";
        long count = 0;
        long earliest = 0;
        long latest = 0;

        using (var oldStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var newStream = new FileStream(importedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(mergedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[]? oldRecord = ReadRawRecord(oldStream);
            byte[]? newRecord = ReadRawRecord(newStream);

            while (oldRecord is not null || newRecord is not null)
            {
                byte[] selected;
                long oldStart = oldRecord is null ? long.MaxValue : BitConverter.ToInt64(oldRecord, 0);
                long newStart = newRecord is null ? long.MaxValue : BitConverter.ToInt64(newRecord, 0);

                if (newStart < oldStart)
                {
                    selected = newRecord!;
                    newRecord = ReadRawRecord(newStream);
                }
                else if (oldStart < newStart)
                {
                    selected = oldRecord!;
                    oldRecord = ReadRawRecord(oldStream);
                }
                else
                {
                    // The newly downloaded native MT5 record replaces the
                    // old record at the same candle opening time.
                    selected = newRecord!;
                    oldRecord = ReadRawRecord(oldStream);
                    newRecord = ReadRawRecord(newStream);
                }

                output.Write(selected, 0, selected.Length);
                long start = BitConverter.ToInt64(selected, 0);
                count++;
                earliest = count == 1 ? start : Math.Min(earliest, start);
                latest = Math.Max(latest, start);
            }

            output.Flush(true);
        }

        File.Move(mergedPath, targetPath, true);
        VerifyImportedRecordsPresent(targetPath, importedPath);
        File.Delete(importedPath);
        WriteJsonAtomic(
            Path.Combine(segmentFolder, "segment.json"),
            new SegmentMetadata(count, earliest, latest, DateTime.UtcNow));
    }


    private static void VerifySegmentFileStructure(
        string path,
        long expectedCount,
        long expectedEarliest,
        long expectedLatest)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length % RecordSize != 0)
            throw new InvalidDataException("Saved candle segment has an incomplete binary record.");

        long count = stream.Length / RecordSize;
        if (count != expectedCount)
            throw new InvalidDataException("Saved candle segment record count did not verify after replacement.");
        if (count == 0)
            return;

        byte[]? first = ReadRawRecord(stream);
        stream.Seek((count - 1) * RecordSize, SeekOrigin.Begin);
        byte[]? last = ReadRawRecord(stream);
        if (first is null || last is null ||
            BitConverter.ToInt64(first, 0) != expectedEarliest ||
            BitConverter.ToInt64(last, 0) != expectedLatest)
        {
            throw new InvalidDataException("Saved candle segment boundaries did not verify after replacement.");
        }
    }

    private static void VerifyImportedRecordsPresent(
        string targetPath,
        string importedPath)
    {
        using var target = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var incoming = new FileStream(importedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[]? targetRecord = ReadRawRecord(target);
        byte[]? incomingRecord = ReadRawRecord(incoming);

        while (incomingRecord is not null)
        {
            long incomingStart = BitConverter.ToInt64(incomingRecord, 0);
            while (targetRecord is not null &&
                   BitConverter.ToInt64(targetRecord, 0) < incomingStart)
            {
                targetRecord = ReadRawRecord(target);
            }

            if (targetRecord is null ||
                BitConverter.ToInt64(targetRecord, 0) != incomingStart ||
                !targetRecord.AsSpan().SequenceEqual(incomingRecord))
            {
                throw new InvalidDataException(
                    $"Imported MT5 candle at {incomingStart} did not verify in permanent storage.");
            }

            incomingRecord = ReadRawRecord(incoming);
        }
    }
    private static byte[]? ReadRawRecord(Stream stream)
    {
        if (stream.Position + RecordSize > stream.Length)
            return null;

        byte[] record = new byte[RecordSize];
        int offset = 0;
        while (offset < record.Length)
        {
            int read = stream.Read(record, offset, record.Length - offset);
            if (read <= 0)
                return null;
            offset += read;
        }

        return record;
    }

    private void SetInstrumentSavingInternal(
        string connectorId,
        string symbol,
        bool enabled)
    {
        InstrumentSettings settings = ReadInstrumentSettings(connectorId);
        var instruments = settings.Instruments.ToList();
        int index = instruments.FindIndex(item =>
            string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        var updated = new SavedInstrumentState(symbol, enabled, DateTime.UtcNow);

        if (index >= 0)
            instruments[index] = updated;
        else
            instruments.Add(updated);

        WriteJsonAtomic(
            GetInstrumentSettingsPath(connectorId),
            new InstrumentSettings(instruments));
    }

    private InstrumentSettings ReadInstrumentSettings(string connectorId)
    {
        return ReadJson<InstrumentSettings>(GetInstrumentSettingsPath(connectorId))
               ?? new InstrumentSettings(Array.Empty<SavedInstrumentState>());
    }

    private string GetNativeBoundaryPath(string connectorId, string symbol)
    {
        string folder = GetSymbolFolder(connectorId, symbol);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "native_boundaries.json");
    }

    private string GetInstrumentSettingsPath(string connectorId)
    {
        string folder = Path.Combine(
            _rootPath,
            Sanitize(connectorId),
            "_settings");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "instruments.json");
    }

    private string GetSymbolFolder(
        string connectorId,
        string symbol)
    {
        return Path.Combine(
            _rootPath,
            Sanitize(connectorId),
            Sanitize(symbol));
    }

    private string GetDatasetFolder(
        string connectorId,
        string symbol,
        string timeframe)
    {
        return Path.Combine(
            GetSymbolFolder(connectorId, symbol),
            Sanitize(timeframe));
    }

    private string GetSegmentFolder(
        string connectorId,
        string symbol,
        string timeframe,
        string segmentKey)
    {
        return Path.Combine(
            GetDatasetFolder(connectorId, symbol, timeframe),
            Sanitize(segmentKey));
    }

    public static string GetSegmentKey(
        long mt5ServerUnix,
        int serverUtcOffsetMinutes = 0)
    {
        // Native bridge timestamps are already encoded in MT5 broker-server
        // wall-clock time. The optional offset remains for source compatibility
        // but must not be applied a second time.
        _ = serverUtcOffsetMinutes;
        DateTimeOffset time = Mt5ServerClock.ToDisplayTime(mt5ServerUnix);
        int firstMonth = ((time.Month - 1) / 3) * 3 + 1;
        var start = new DateTimeOffset(time.Year, firstMonth, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddMonths(3).AddDays(-1);
        return $"{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}";
    }

    public static string GetCurrentSegmentKey(int serverUtcOffsetMinutes) =>
        GetSegmentKey(Mt5ServerClock.ServerNowUnix(serverUtcOffsetMinutes));

    public static long GetSegmentStartUnix(string segmentKey)
    {
        string startText = segmentKey.Split(
            "_to_",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        if (!DateTime.TryParseExact(
                startText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime start))
        {
            throw new InvalidDataException(
                $"The three-month segment key '{segmentKey}' is invalid.");
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
            TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
    }

    private static SegmentMetadata? ReadSegmentMetadata(string folder) =>
        ReadJson<SegmentMetadata>(Path.Combine(folder, "segment.json"));

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJsonAtomic<T>(
        string path,
        T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static void ReadSegmentBeforeTail(
        string segmentFolder,
        DatasetMetadata metadata,
        long beforeUnix,
        ICollection<Candle> destination,
        int maximumRecords)
    {
        string path = Path.Combine(segmentFolder, "candles.tlc");
        if (!File.Exists(path) || maximumRecords <= 0)
            return;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);

        long availableRecords = stream.Length / RecordSize;
        long low = 0;
        long high = availableRecords;

        // Find the first record whose start is >= beforeUnix.
        while (low < high)
        {
            long middle = low + (high - low) / 2;
            stream.Seek(middle * RecordSize, SeekOrigin.Begin);
            long startUnix = reader.ReadInt64();

            if (startUnix < beforeUnix)
                low = middle + 1;
            else
                high = middle;
        }

        long endExclusive = low;
        int recordsToRead = (int)Math.Min(endExclusive, maximumRecords);
        if (recordsToRead <= 0)
            return;

        stream.Seek((endExclusive - recordsToRead) * RecordSize, SeekOrigin.Begin);
        for (int index = 0; index < recordsToRead; index++)
            destination.Add(ReadCandleRecord(reader, metadata));
    }

    private static void ReadSegmentTail(
        string segmentFolder,
        DatasetMetadata metadata,
        ICollection<Candle> destination,
        int maximumRecords)
    {
        string path = Path.Combine(segmentFolder, "candles.tlc");
        if (!File.Exists(path) || maximumRecords <= 0)
            return;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        long availableRecords = stream.Length / RecordSize;
        int recordsToRead = (int)Math.Min(availableRecords, maximumRecords);
        if (recordsToRead <= 0)
            return;

        stream.Seek((availableRecords - recordsToRead) * RecordSize, SeekOrigin.Begin);
        using var reader = new BinaryReader(stream);
        for (int index = 0; index < recordsToRead; index++)
            destination.Add(ReadCandleRecord(reader, metadata));
    }

    private sealed class StreamingExactDatasetWriter
    {
        private readonly PersistentHistoryStore _owner;
        private readonly string _connectorId;
        private readonly string _symbol;
        private readonly string _timeframe;
        private readonly int _digits;
        private readonly double _point;
        private readonly int _serverUtcOffsetMinutes;
        private string _segmentKey = string.Empty;
        private string _segmentFolder = string.Empty;
        private string _temporaryPath = string.Empty;
        private FileStream? _stream;
        private BinaryWriter? _writer;
        private long _segmentCount;
        private long _segmentEarliest;
        private long _segmentLatest;
        private long _lastStartUnix = long.MinValue;

        public StreamingExactDatasetWriter(
            PersistentHistoryStore owner,
            string connectorId,
            string symbol,
            string timeframe,
            int digits,
            double point,
            int serverUtcOffsetMinutes)
        {
            _owner = owner;
            _connectorId = connectorId;
            _symbol = symbol;
            _timeframe = timeframe;
            _digits = digits;
            _point = point;
            _serverUtcOffsetMinutes = serverUtcOffsetMinutes;
        }

        public int CompletedSegments { get; private set; }
        public string CurrentSegment => _segmentKey;

        public void Add(Candle candle)
        {
            HistoryIntegrityReport integrity = HistoryIntegrityService.ValidateCandles(
                new[] { candle },
                _symbol,
                _timeframe);
            if (!integrity.Passed)
                throw new InvalidDataException(integrity.Issues[0].Message);

            if (candle.StartUnix < _lastStartUnix)
                throw new InvalidDataException("MT5 candle history is not ordered oldest to newest.");

            if (candle.StartUnix == _lastStartUnix)
                return;

            string key = GetSegmentKey(candle.StartUnix, _serverUtcOffsetMinutes);
            if (!string.Equals(key, _segmentKey, StringComparison.Ordinal))
            {
                FinalizeSegment();
                OpenSegment(key);
            }

            WriteCandleRecord(_writer!, candle);
            _segmentCount++;
            _segmentEarliest = _segmentCount == 1
                ? candle.StartUnix
                : Math.Min(_segmentEarliest, candle.StartUnix);
            _segmentLatest = Math.Max(_segmentLatest, candle.StartUnix);
            _lastStartUnix = candle.StartUnix;
        }

        public void Complete()
        {
            FinalizeSegment();
            _owner.WriteDatasetMetadata(
                _connectorId,
                _symbol,
                _timeframe,
                _digits,
                _point);
        }

        public void Abort()
        {
            try { _writer?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            _writer = null;
            _stream = null;
            if (!string.IsNullOrWhiteSpace(_temporaryPath))
            {
                try { File.Delete(_temporaryPath); } catch { }
            }
        }

        private void OpenSegment(string key)
        {
            _segmentKey = key;
            _segmentFolder = _owner.GetSegmentFolder(
                _connectorId,
                _symbol,
                _timeframe,
                key);
            Directory.CreateDirectory(_segmentFolder);
            _temporaryPath = Path.Combine(_segmentFolder, "candles.tlc.import.tmp");
            _stream = new FileStream(
                _temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            _writer = new BinaryWriter(_stream);
            _segmentCount = 0;
            _segmentEarliest = 0;
            _segmentLatest = 0;
        }

        private void FinalizeSegment()
        {
            if (_writer is null || _stream is null)
                return;

            _writer.Flush();
            _stream.Flush(true);
            _writer.Dispose();
            _stream.Dispose();
            _writer = null;
            _stream = null;

            if (_segmentCount > 0)
            {
                _owner.MergeImportedSegment(
                    _segmentFolder,
                    _temporaryPath,
                    _segmentCount,
                    _segmentEarliest,
                    _segmentLatest);
                CompletedSegments++;
            }
            else
            {
                File.Delete(_temporaryPath);
            }

            _temporaryPath = string.Empty;
            _segmentKey = string.Empty;
            _segmentFolder = string.Empty;
        }
    }

    private sealed record NativeBoundarySettings(
        IReadOnlyDictionary<string, NativeBoundaryEntry> Boundaries);

    private sealed record NativeBoundaryEntry(
        long FirstAvailableUnix,
        DateTime UpdatedUtc);

    private sealed record DatasetMetadata(
        string ConnectorId,
        string Symbol,
        string Timeframe,
        int Digits,
        double Point,
        DateTime UpdatedUtc);

    private sealed record SegmentMetadata(
        long Count,
        long EarliestUnix,
        long LatestUnix,
        DateTime UpdatedUtc);

    private sealed record InstrumentSettings(
        IReadOnlyList<SavedInstrumentState> Instruments);
}

public enum HistoryDisplayMode
{
    RecentThreeMonths,
    SelectedSegments,
    AllSavedHistory
}

public sealed record HistoryLoadSelection(
    HistoryDisplayMode Mode,
    IReadOnlyList<string>? SegmentKeys = null)
{
    public static HistoryLoadSelection RecentThreeMonths { get; } =
        new(HistoryDisplayMode.RecentThreeMonths);

    public static HistoryLoadSelection All { get; } =
        new(HistoryDisplayMode.AllSavedHistory);
}

public sealed record PortableHistoryScanResult(
    int DiscoveredInstruments,
    int AlreadyKnownInstruments,
    string FolderPath);

public sealed record TickHistoryFolderSummary(
    string Symbol,
    string SegmentKey,
    long StartUnix,
    long EndUnix,
    long SizeBytes,
    string FilePath,
    string Status,
    bool IsVisible,
    long ActualEarliestUnix,
    long ActualLatestUnix);

public sealed record TickProjectionResult(
    string Symbol,
    string SegmentKey,
    int CompletedTimeframes,
    long GeneratedCandles,
    long InsertedCandles,
    string Message);

public sealed record HistoryImportProgress(
    int CompletedSegments,
    int TotalSegments,
    long ImportedRecords,
    long TotalRecords,
    string CurrentSegment);

public sealed record HistoryImportResult(
    bool Success,
    string Message,
    string Symbol,
    string Timeframe,
    long ImportedRecords,
    long SizeBytes);

public sealed record HistorySegmentSummary(
    string Key,
    long EarliestUnix,
    long LatestUnix,
    long RecordCount,
    long SizeBytes,
    string Status);

public sealed record HistoryDatasetSummary(
    string ConnectorId,
    string Symbol,
    string Timeframe,
    long RecordCount,
    long EarliestUnix,
    long LatestUnix,
    long SizeBytes,
    bool SavingEnabled,
    IReadOnlyList<HistorySegmentSummary> Segments);

public sealed record SavedInstrumentState(
    string Symbol,
    bool Enabled,
    DateTime UpdatedUtc);
