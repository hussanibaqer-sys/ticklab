using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickLab.Core.History;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class ExternalHistoryStore
{
    private const int CandleRecordSize = 69;
    private const int TickRecordSize = 56;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _rootPath;

    public ExternalHistoryStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _rootPath = Path.Combine(localAppData, "TickLab", "ExternalHistory");
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public string GetConnectorExternalFolder(string connectorId)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(connectorId));

        string folder = Path.Combine(_rootPath, Mt5Paths.SanitizeFilePart(connectorId));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public int RescanPortableDatasets(string connectorId)
    {
        string connectorFolder = GetConnectorExternalFolder(connectorId);
        int rebound = 0;
        lock (_sync)
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         connectorFolder,
                         "manifest.json",
                         SearchOption.AllDirectories))
            {
                ExternalDatasetManifest? manifest = ReadManifest(manifestPath);
                if (manifest is null)
                    continue;

                string folder = Path.GetDirectoryName(manifestPath) ?? string.Empty;
                string dataPath = Path.Combine(
                    folder,
                    manifest.Kind == ExternalDataKind.RawTicks ? "ticks.tlt" : "m1.tlc");
                if (!File.Exists(dataPath))
                    continue;

                if (!string.Equals(
                        manifest.ConnectorId,
                        connectorId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteManifest(folder, manifest with
                    {
                        ConnectorId = connectorId,
                        DatasetFolder = string.Empty
                    });
                    rebound++;
                }
            }
        }

        return rebound;
    }

    public IReadOnlyList<ExternalDatasetManifest> GetDatasets(
        string connectorId,
        string? symbol = null)
    {
        if (!Directory.Exists(_rootPath))
            return Array.Empty<ExternalDatasetManifest>();

        var result = new List<ExternalDatasetManifest>();
        lock (_sync)
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         _rootPath,
                         "manifest.json",
                         SearchOption.AllDirectories))
            {
                ExternalDatasetManifest? manifest = ReadManifest(manifestPath);
                if (manifest is null ||
                    !string.Equals(manifest.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(symbol) &&
                     !string.Equals(manifest.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(manifest with { DatasetFolder = Path.GetDirectoryName(manifestPath) ?? string.Empty });
            }
        }

        return result
            .OrderByDescending(item => item.Enabled)
            .ThenByDescending(item => item.Priority)
            .ThenByDescending(item => item.ImportedAtUtc)
            .ToArray();
    }

    public ExternalImportResult ImportDelimitedFile(
        string filePath,
        ExternalImportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("External history file was not found.", filePath);
        if (!Mt5Paths.IsValidConnectorId(options.ConnectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Symbol))
            throw new ArgumentException("A symbol is required.", nameof(options));
        if (options.Point <= 0 || options.Digits < 0)
            throw new ArgumentException("Digits and point size must be valid.", nameof(options));

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16);

        string? headerLine = ReadNextDataLine(reader);
        if (headerLine is null)
            throw new InvalidDataException("The selected file is empty.");

        char delimiter = DetectDelimiter(headerLine);
        string[] headers = ParseDelimitedLine(headerLine, delimiter)
            .Select(NormalizeHeader)
            .ToArray();
        var map = headers
            .Select((name, index) => new { name, index })
            .Where(item => !string.IsNullOrWhiteSpace(item.name))
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);

        ExternalDataKind kind = DetectKind(map);
        string datasetId = Guid.NewGuid().ToString("N");
        string folder = GetDatasetFolder(options.ConnectorId, options.Symbol, datasetId);
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, kind == ExternalDataKind.RawTicks ? "ticks.tlt.tmp" : "m1.tlc.tmp");
        string final = Path.Combine(folder, kind == ExternalDataKind.RawTicks ? "ticks.tlt" : "m1.tlc");

        long accepted = 0;
        long rejected = 0;
        long earliestUnix = long.MaxValue;
        long latestUnix = long.MinValue;
        long sequence = 0;
        long previousUnixOrMilliseconds = long.MinValue;
        long previousSequence = long.MinValue;
        double tickSize = options.TickSize > 0 ? options.TickSize : options.Point;

        try
        {
            using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(output);

            string? line;
            long lineNumber = 1;
            while ((line = reader.ReadLine()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;

                IReadOnlyList<string> fields = ParseDelimitedLine(line, delimiter);
                if (kind == ExternalDataKind.M1Candles)
                {
                    if (!TryParseCandle(fields, map, options, out Candle? candle))
                    {
                        rejected++;
                        continue;
                    }

                    if (candle.StartUnix <= previousUnixOrMilliseconds)
                    {
                        rejected++;
                        continue;
                    }

                    WriteCandle(writer, candle);
                    previousUnixOrMilliseconds = candle.StartUnix;
                    accepted++;
                    earliestUnix = Math.Min(earliestUnix, candle.StartUnix);
                    latestUnix = Math.Max(latestUnix, candle.StartUnix);
                }
                else
                {
                    sequence++;
                    if (!TryParseTick(fields, map, options, sequence, out ExternalTick tick))
                    {
                        rejected++;
                        continue;
                    }

                    if (tick.TimeMilliseconds < previousUnixOrMilliseconds ||
                        (tick.TimeMilliseconds == previousUnixOrMilliseconds && tick.Sequence <= previousSequence))
                    {
                        rejected++;
                        continue;
                    }

                    WriteTick(writer, tick);
                    previousUnixOrMilliseconds = tick.TimeMilliseconds;
                    previousSequence = tick.Sequence;
                    accepted++;
                    earliestUnix = Math.Min(earliestUnix, tick.TimeMilliseconds / 1000);
                    latestUnix = Math.Max(latestUnix, tick.TimeMilliseconds / 1000);
                }
            }

            writer.Flush();
            output.Flush(true);

            if (accepted == 0)
                throw new InvalidDataException("No valid M1 candle or tick records were found in this file.");

            File.Move(temporary, final, true);
            VerifyImportedBinary(final, kind, accepted, earliestUnix, latestUnix);
            string fileSha256 = ComputeSha256(final);

            var manifest = new ExternalDatasetManifest(
                datasetId,
                options.ConnectorId,
                options.Symbol.Trim(),
                string.IsNullOrWhiteSpace(options.DisplayName)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : options.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(options.SourceName)
                    ? "External file"
                    : options.SourceName.Trim(),
                kind,
                options.Digits,
                options.Point,
                tickSize,
                options.ServerUtcOffsetMinutes,
                options.TimeZoneVerified,
                options.SourceMatchesBroker,
                true,
                options.Priority,
                Array.Empty<ExternalPriorityRule>(),
                accepted,
                rejected,
                earliestUnix,
                latestUnix,
                DateTime.UtcNow,
                Path.GetFileName(filePath),
                fileSha256,
                string.Empty);

            WriteManifest(folder, manifest);
            return new ExternalImportResult(true, manifest, accepted, rejected,
                $"Imported {accepted:N0} external {DescribeKind(kind)} records. {rejected:N0} invalid line(s) were ignored.");
        }
        catch
        {
            TryDelete(temporary);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
            throw;
        }
    }

    public IReadOnlyList<Candle> ReadM1Candles(
        string connectorId,
        string symbol,
        int maximumRecords,
        long? beforeUnix = null)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        IReadOnlyList<ExternalDatasetManifest> manifests = GetDatasets(connectorId, symbol)
            .Where(item => item.Enabled)
            .ToArray();
        if (manifests.Count == 0)
            return Array.Empty<Candle>();

        var selected = new Dictionary<long, (Candle Candle, int Priority, DateTime ImportedAt)>();
        lock (_sync)
        {
            foreach (ExternalDatasetManifest manifest in manifests)
            {
                string folder = manifest.DatasetFolder;
                if (manifest.Kind == ExternalDataKind.M1Candles)
                {
                    foreach (Candle candle in ReadCandleTail(folder, manifest, maximumRecords, beforeUnix))
                        SelectCandle(selected, candle, EffectivePriority(manifest, candle.StartUnix), manifest.ImportedAtUtc);
                }
                else
                {
                    foreach (Candle candle in ReadTickDerivedCandles(
                                 folder,
                                 manifest,
                                 TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!,
                                 maximumRecords,
                                 beforeUnix))
                    {
                        SelectCandle(selected, candle, 1_000_000 + EffectivePriority(manifest, candle.StartUnix), manifest.ImportedAtUtc);
                    }
                }
            }
        }

        return selected.Values
            .Select(item => item.Candle)
            .OrderBy(item => item.StartUnix)
            .TakeLast(maximumRecords)
            .ToArray();
    }

    public IReadOnlyList<Candle> ReadSecondCandles(
        string connectorId,
        string symbol,
        TimeframeDefinition timeframe,
        int maximumRecords,
        long? beforeUnix = null)
    {
        if (!timeframe.UsesTickArchive)
            return Array.Empty<Candle>();

        maximumRecords = Math.Max(1, maximumRecords);
        IReadOnlyList<ExternalDatasetManifest> manifests = GetDatasets(connectorId, symbol)
            .Where(item => item.Enabled && item.Kind == ExternalDataKind.RawTicks)
            .ToArray();
        var selected = new Dictionary<long, (Candle Candle, int Priority, DateTime ImportedAt)>();

        lock (_sync)
        {
            foreach (ExternalDatasetManifest manifest in manifests)
            {
                foreach (Candle candle in ReadTickDerivedCandles(
                             manifest.DatasetFolder,
                             manifest,
                             timeframe,
                             maximumRecords,
                             beforeUnix))
                {
                    SelectCandle(selected, candle, EffectivePriority(manifest, candle.StartUnix), manifest.ImportedAtUtc);
                }
            }
        }

        return selected.Values
            .Select(item => item.Candle)
            .OrderBy(item => item.StartUnix)
            .TakeLast(maximumRecords)
            .ToArray();
    }

    public void SetEnabled(string datasetId, bool enabled) =>
        UpdateManifest(datasetId, manifest => manifest with { Enabled = enabled });

    public void SetPriority(string datasetId, int priority) =>
        UpdateManifest(datasetId, manifest => manifest with { Priority = priority });

    public void SetPriorityRule(
        string datasetId,
        long startUnix,
        long endUnix,
        int priority)
    {
        if (endUnix <= startUnix)
            throw new ArgumentException("Priority range end must be after its start.");

        UpdateManifest(datasetId, manifest =>
        {
            ExternalPriorityRule[] rules = manifest.PriorityRules
                .Where(rule => rule.EndUnix <= startUnix || rule.StartUnix >= endUnix)
                .Append(new ExternalPriorityRule(startUnix, endUnix, priority))
                .OrderBy(rule => rule.StartUnix)
                .ToArray();
            return manifest with { PriorityRules = rules };
        });
    }

    public void ClearPriorityRules(string datasetId) =>
        UpdateManifest(datasetId, manifest => manifest with
        {
            PriorityRules = Array.Empty<ExternalPriorityRule>()
        });

    public void DeleteDataset(string datasetId)
    {
        string? folder = FindDatasetFolder(datasetId);
        if (folder is null)
            return;

        lock (_sync)
            Directory.Delete(folder, true);
    }

    private void UpdateManifest(
        string datasetId,
        Func<ExternalDatasetManifest, ExternalDatasetManifest> update)
    {
        string? folder = FindDatasetFolder(datasetId);
        if (folder is null)
            throw new DirectoryNotFoundException("External dataset was not found.");

        lock (_sync)
        {
            ExternalDatasetManifest manifest = ReadManifest(Path.Combine(folder, "manifest.json"))
                ?? throw new InvalidDataException("External dataset manifest is invalid.");
            WriteManifest(folder, update(manifest with { DatasetFolder = string.Empty }));
        }
    }

    private string? FindDatasetFolder(string datasetId)
    {
        return Directory.Exists(_rootPath)
            ? Directory.EnumerateFiles(_rootPath, "manifest.json", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(folder => folder is not null &&
                    string.Equals(Path.GetFileName(folder), datasetId, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static void SelectCandle(
        IDictionary<long, (Candle Candle, int Priority, DateTime ImportedAt)> selected,
        Candle candle,
        int priority,
        DateTime importedAt)
    {
        if (!selected.TryGetValue(candle.StartUnix, out var existing) ||
            priority > existing.Priority ||
            (priority == existing.Priority && importedAt > existing.ImportedAt))
        {
            selected[candle.StartUnix] = (candle, priority, importedAt);
        }
    }

    private static int EffectivePriority(ExternalDatasetManifest manifest, long unix)
    {
        ExternalPriorityRule? rule = manifest.PriorityRules
            .Where(item => unix >= item.StartUnix && unix < item.EndUnix)
            .OrderByDescending(item => item.Priority)
            .FirstOrDefault();
        return rule?.Priority ?? manifest.Priority;
    }

    private static IEnumerable<Candle> ReadCandleTail(
        string folder,
        ExternalDatasetManifest manifest,
        int maximumRecords,
        long? beforeUnix)
    {
        string path = Path.Combine(folder, "m1.tlc");
        if (!File.Exists(path))
            yield break;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long total = stream.Length / CandleRecordSize;
        using var reader = new BinaryReader(stream);
        long endExclusive = total;
        if (beforeUnix.HasValue)
        {
            long low = 0;
            long high = total;
            while (low < high)
            {
                long middle = low + (high - low) / 2;
                stream.Seek(middle * CandleRecordSize, SeekOrigin.Begin);
                long startUnix = reader.ReadInt64();
                if (startUnix < beforeUnix.Value)
                    low = middle + 1;
                else
                    high = middle;
            }
            endExclusive = low;
        }

        long start = Math.Max(0, endExclusive - maximumRecords);
        stream.Seek(start * CandleRecordSize, SeekOrigin.Begin);
        var result = new List<Candle>(maximumRecords);
        while (stream.Position + CandleRecordSize <= stream.Length &&
               stream.Position / CandleRecordSize < endExclusive)
        {
            result.Add(ReadCandle(reader, manifest));
        }

        foreach (Candle candle in result.TakeLast(maximumRecords))
            yield return candle;
    }

    private static IEnumerable<Candle> ReadTickDerivedCandles(
        string folder,
        ExternalDatasetManifest manifest,
        TimeframeDefinition timeframe,
        int maximumRecords,
        long? beforeUnix)
    {
        string path = Path.Combine(folder, "ticks.tlt");
        if (!File.Exists(path))
            yield break;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long total = stream.Length / TickRecordSize;
        using var reader = new BinaryReader(stream);
        long endExclusive = total;
        if (beforeUnix.HasValue)
        {
            long beforeMilliseconds = checked(beforeUnix.Value * 1000L);
            long low = 0;
            long high = total;
            while (low < high)
            {
                long middle = low + (high - low) / 2;
                stream.Seek(middle * TickRecordSize, SeekOrigin.Begin);
                long timeMilliseconds = reader.ReadInt64();
                if (timeMilliseconds < beforeMilliseconds)
                    low = middle + 1;
                else
                    high = middle;
            }
            endExclusive = low;
        }

        long estimatedTicksPerBucket = timeframe.Unit == TimeframeUnit.Second
            ? Math.Max(1, timeframe.Quantity * 8L)
            : Math.Max(1, timeframe.ToApproximateSeconds() * 8L);
        long start = Math.Max(0, endExclusive - maximumRecords * estimatedTicksPerBucket * 2L);
        stream.Seek(start * TickRecordSize, SeekOrigin.Begin);

        var result = new List<Candle>(maximumRecords);
        Candle? current = null;
        while (stream.Position + TickRecordSize <= stream.Length &&
               stream.Position / TickRecordSize < endExclusive)
        {
            ExternalTick tick = ReadTick(reader);
            long unix = tick.TimeMilliseconds / 1000;
            if (beforeUnix.HasValue && unix >= beforeUnix.Value)
                continue;

            double price = tick.Bid > 0 ? tick.Bid : tick.Last > 0 ? tick.Last : tick.Ask;
            if (!double.IsFinite(price) || price <= 0)
                continue;

            long bucketStart = timeframe.GetBucketStartUnix(unix, manifest.ServerUtcOffsetMinutes);
            long bucketEnd = timeframe.GetBucketEndUnix(bucketStart, manifest.ServerUtcOffsetMinutes);
            int spread = tick.Bid > 0 && tick.Ask > 0 && manifest.Point > 0
                ? Math.Max(0, (int)Math.Round((tick.Ask - tick.Bid) / manifest.Point))
                : 0;

            if (current is null || current.StartUnix != bucketStart)
            {
                if (current is not null)
                    result.Add(current);
                current = new Candle(
                    manifest.Symbol,
                    timeframe.DisplayText,
                    manifest.Digits,
                    manifest.Point,
                    bucketStart,
                    bucketEnd,
                    DateTimeOffset.FromUnixTimeSeconds(bucketStart)
                        .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
                    price,
                    price,
                    price,
                    price,
                    1,
                    spread,
                    (long)Math.Max(0, Math.Round(tick.Volume)),
                    true);
            }
            else
            {
                current = current with
                {
                    High = Math.Max(current.High, price),
                    Low = Math.Min(current.Low, price),
                    Close = price,
                    TickVolume = current.TickVolume + 1,
                    Spread = spread,
                    RealVolume = current.RealVolume + (long)Math.Max(0, Math.Round(tick.Volume))
                };
            }
        }

        if (current is not null)
            result.Add(current);

        foreach (Candle candle in result.TakeLast(maximumRecords))
            yield return candle;
    }

    private static bool TryParseCandle(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        ExternalImportOptions options,
        out Candle? candle)
    {
        candle = null;
        if (!TryReadUnix(fields, map, options.ServerUtcOffsetMinutes, out long startUnix) ||
            !TryReadDouble(fields, map, new[] { "open", "o" }, out double open) ||
            !TryReadDouble(fields, map, new[] { "high", "h" }, out double high) ||
            !TryReadDouble(fields, map, new[] { "low", "l" }, out double low) ||
            !TryReadDouble(fields, map, new[] { "close", "c" }, out double close))
        {
            return false;
        }

        if (!double.IsFinite(open) || !double.IsFinite(high) ||
            !double.IsFinite(low) || !double.IsFinite(close) ||
            high < low || open < low || open > high || close < low || close > high)
        {
            return false;
        }

        long tickVolume = ReadLong(fields, map, new[] { "tick_volume", "tickvolume", "volume", "vol" });
        long realVolume = ReadLong(fields, map, new[] { "real_volume", "realvolume", "volume_real" });
        int spread = (int)Math.Clamp(ReadLong(fields, map, new[] { "spread" }), 0, int.MaxValue);
        candle = new Candle(
            options.Symbol.Trim(),
            "PERIOD_M1",
            options.Digits,
            options.Point,
            startUnix,
            startUnix + 60,
            DateTimeOffset.FromUnixTimeSeconds(startUnix)
                .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
            open,
            high,
            low,
            close,
            Math.Max(0, tickVolume),
            spread,
            Math.Max(0, realVolume),
            true);
        return true;
    }

    private static bool TryParseTick(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        ExternalImportOptions options,
        long fallbackSequence,
        out ExternalTick tick)
    {
        tick = default;
        if (!TryReadUnixMilliseconds(fields, map, options.ServerUtcOffsetMinutes, out long timeMilliseconds))
            return false;

        TryReadDouble(fields, map, new[] { "bid" }, out double bid);
        TryReadDouble(fields, map, new[] { "ask" }, out double ask);
        TryReadDouble(fields, map, new[] { "last", "price" }, out double last);
        TryReadDouble(fields, map, new[] { "volume", "volume_real", "real_volume" }, out double volume);
        long sequence = ReadLong(fields, map, new[] { "sequence", "seq", "id" });
        long flags = ReadLong(fields, map, new[] { "flags", "flag" });
        if (sequence <= 0)
            sequence = fallbackSequence;

        double price = bid > 0 ? bid : last > 0 ? last : ask;
        if (!double.IsFinite(price) || price <= 0)
            return false;

        tick = new ExternalTick(
            timeMilliseconds,
            sequence,
            bid,
            ask,
            last,
            Math.Max(0, volume),
            flags);
        return true;
    }

    private static bool TryReadUnix(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        int offsetMinutes,
        out long unix)
    {
        unix = 0;
        if (TryReadValue(fields, map, new[] { "start_unix", "time_unix", "unix", "timestamp" }, out string value) &&
            TryParseTimeValue(value, offsetMinutes, out long milliseconds))
        {
            unix = milliseconds / 1000;
            return unix > 0;
        }

        string date = ReadValue(fields, map, new[] { "date", "day" });
        string time = ReadValue(fields, map, new[] { "time", "datetime", "date_time", "timestamp_text" });
        string combined = string.IsNullOrWhiteSpace(date) ? time : $"{date} {time}";
        if (!TryParseTimeValue(combined, offsetMinutes, out long parsedMilliseconds))
            return false;
        unix = parsedMilliseconds / 1000;
        return unix > 0;
    }

    private static bool TryReadUnixMilliseconds(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        int offsetMinutes,
        out long milliseconds)
    {
        milliseconds = 0;
        if (TryReadValue(fields, map,
                new[] { "time_msc", "time_ms", "timestamp_ms", "timestamp_msc", "milliseconds", "time_unix", "unix", "timestamp" },
                out string value) &&
            TryParseTimeValue(value, offsetMinutes, out milliseconds))
        {
            return milliseconds > 0;
        }

        string date = ReadValue(fields, map, new[] { "date", "day" });
        string time = ReadValue(fields, map, new[] { "time", "datetime", "date_time", "timestamp_text" });
        return TryParseTimeValue(
            string.IsNullOrWhiteSpace(date) ? time : $"{date} {time}",
            offsetMinutes,
            out milliseconds);
    }

    private static bool TryParseTimeValue(string value, int offsetMinutes, out long milliseconds)
    {
        milliseconds = 0;
        value = value.Trim();
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
        {
            long utcMilliseconds = numeric > 10_000_000_000L ? numeric : numeric * 1000L;
            milliseconds = Mt5ServerClock.UtcMillisecondsToServerMilliseconds(
                utcMilliseconds, offsetMinutes);
            return milliseconds > 0;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset withOffset))
        {
            bool containsExplicitOffset = value.EndsWith('Z') ||
                value.LastIndexOf('+') > 7 ||
                value.LastIndexOf('-') > 9;
            if (!containsExplicitOffset && DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out DateTime localWallClock))
            {
                withOffset = new DateTimeOffset(
                    DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified),
                    TimeSpan.FromMinutes(offsetMinutes));
            }

            milliseconds = Mt5ServerClock.UtcMillisecondsToServerMilliseconds(
                withOffset.ToUnixTimeMilliseconds(), offsetMinutes);
            return milliseconds > 0;
        }

        return false;
    }

    private static ExternalDataKind DetectKind(IReadOnlyDictionary<string, int> map)
    {
        bool candles = HasAny(map, "open", "o") && HasAny(map, "high", "h") &&
                       HasAny(map, "low", "l") && HasAny(map, "close", "c");
        bool ticks = HasAny(map, "bid") || HasAny(map, "ask") || HasAny(map, "last", "price");
        if (candles)
            return ExternalDataKind.M1Candles;
        if (ticks)
            return ExternalDataKind.RawTicks;
        throw new InvalidDataException("The header does not contain recognizable OHLC or Bid/Ask/Last columns.");
    }

    private static bool HasAny(IReadOnlyDictionary<string, int> map, params string[] names) =>
        names.Any(map.ContainsKey);

    private static bool TryReadDouble(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        IReadOnlyList<string> names,
        out double value)
    {
        value = 0;
        return TryReadValue(fields, map, names, out string text) &&
               double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static long ReadLong(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        IReadOnlyList<string> names)
    {
        if (!TryReadValue(fields, map, names, out string text))
            return 0;
        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            return integer;
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? (long)Math.Round(number)
            : 0;
    }

    private static string ReadValue(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        IReadOnlyList<string> names) =>
        TryReadValue(fields, map, names, out string value) ? value : string.Empty;

    private static bool TryReadValue(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> map,
        IReadOnlyList<string> names,
        out string value)
    {
        foreach (string name in names)
        {
            if (map.TryGetValue(name, out int index) && index >= 0 && index < fields.Count)
            {
                value = fields[index];
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }

    private static char DetectDelimiter(string header)
    {
        char[] candidates = { ',', ';', '\t', '|' };
        return candidates
            .OrderByDescending(candidate => header.Count(character => character == candidate))
            .First();
    }

    private static IReadOnlyList<string> ParseDelimitedLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == delimiter && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    private static string NormalizeHeader(string header)
    {
        var result = new StringBuilder();
        foreach (char character in header.Trim().Trim('\uFEFF').ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                result.Append(character);
            else if (result.Length > 0 && result[^1] != '_')
                result.Append('_');
        }
        return result.ToString().Trim('_');
    }

    private static string? ReadNextDataLine(StreamReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                return line;
        }
        return null;
    }

    private static void WriteCandle(BinaryWriter writer, Candle candle)
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

    private static Candle ReadCandle(BinaryReader reader, ExternalDatasetManifest manifest)
    {
        long startUnix = reader.ReadInt64();
        long endUnix = reader.ReadInt64();
        return new Candle(
            manifest.Symbol,
            "PERIOD_M1",
            manifest.Digits,
            manifest.Point,
            startUnix,
            endUnix,
            DateTimeOffset.FromUnixTimeSeconds(startUnix)
                .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadInt64(),
            reader.ReadInt32(),
            reader.ReadInt64(),
            reader.ReadBoolean());
    }

    private static void WriteTick(BinaryWriter writer, ExternalTick tick)
    {
        writer.Write(tick.TimeMilliseconds);
        writer.Write(tick.Sequence);
        writer.Write(tick.Bid);
        writer.Write(tick.Ask);
        writer.Write(tick.Last);
        writer.Write(tick.Volume);
        writer.Write(tick.Flags);
    }

    private static ExternalTick ReadTick(BinaryReader reader) =>
        new(
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadInt64());

    private string GetDatasetFolder(string connectorId, string symbol, string datasetId) =>
        Path.Combine(
            _rootPath,
            Mt5Paths.SanitizeFilePart(connectorId),
            Mt5Paths.SanitizeFilePart(symbol),
            datasetId);


    private static void VerifyImportedBinary(
        string path,
        ExternalDataKind kind,
        long expectedCount,
        long expectedEarliestUnix,
        long expectedLatestUnix)
    {
        int recordSize = kind == ExternalDataKind.RawTicks
            ? TickRecordSize
            : CandleRecordSize;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length % recordSize != 0 || stream.Length / recordSize != expectedCount)
            throw new InvalidDataException("External history failed binary record-count verification.");
        if (expectedCount == 0)
            return;

        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        long firstRaw = reader.ReadInt64();
        stream.Seek((expectedCount - 1) * recordSize, SeekOrigin.Begin);
        long lastRaw = reader.ReadInt64();
        long firstUnix = kind == ExternalDataKind.RawTicks ? firstRaw / 1000 : firstRaw;
        long lastUnix = kind == ExternalDataKind.RawTicks ? lastRaw / 1000 : lastRaw;
        if (firstUnix != expectedEarliestUnix || lastUnix != expectedLatestUnix)
            throw new InvalidDataException("External history failed boundary verification.");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static ExternalDatasetManifest? ReadManifest(string path)
    {
        try
        {
            ExternalDatasetManifest? manifest = JsonSerializer.Deserialize<ExternalDatasetManifest>(
                File.ReadAllText(path),
                JsonOptions);
            return manifest is null
                ? null
                : manifest with
                {
                    PriorityRules = manifest.PriorityRules ?? Array.Empty<ExternalPriorityRule>()
                };
        }
        catch
        {
            return null;
        }
    }

    private static void WriteManifest(string folder, ExternalDatasetManifest manifest)
    {
        string path = Path.Combine(folder, "manifest.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            manifest with { DatasetFolder = string.Empty }, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static string DescribeKind(ExternalDataKind kind) =>
        kind == ExternalDataKind.RawTicks ? "raw tick" : "M1 candle";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private readonly record struct ExternalTick(
        long TimeMilliseconds,
        long Sequence,
        double Bid,
        double Ask,
        double Last,
        double Volume,
        long Flags);
}

public enum ExternalDataKind
{
    M1Candles,
    RawTicks
}

public sealed record ExternalPriorityRule(
    long StartUnix,
    long EndUnix,
    int Priority);

public sealed record ExternalDatasetManifest(
    string DatasetId,
    string ConnectorId,
    string Symbol,
    string DisplayName,
    string SourceName,
    ExternalDataKind Kind,
    int Digits,
    double Point,
    double TickSize,
    int ServerUtcOffsetMinutes,
    bool TimeZoneVerified,
    bool SourceMatchesBroker,
    bool Enabled,
    int Priority,
    IReadOnlyList<ExternalPriorityRule> PriorityRules,
    long AcceptedRecords,
    long RejectedRecords,
    long EarliestUnix,
    long LatestUnix,
    DateTime ImportedAtUtc,
    string OriginalFileName,
    string? FileSha256,
    string DatasetFolder);

public sealed record ExternalImportOptions(
    string ConnectorId,
    string Symbol,
    string DisplayName,
    string SourceName,
    int Digits,
    double Point,
    double TickSize,
    int ServerUtcOffsetMinutes,
    bool TimeZoneVerified,
    bool SourceMatchesBroker,
    int Priority = 0);

public sealed record ExternalImportResult(
    bool Success,
    ExternalDatasetManifest Dataset,
    long AcceptedRecords,
    long RejectedRecords,
    string Message);
