using System.Globalization;
using System.Text.Json;
using TickLab.Core.History;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

/// <summary>
/// Permanent native MT5 candle archive. Each symbol has one CandleHistory
/// folder and each native MT5 timeframe has one independently repairable file.
/// Candle files are never quarter-split; raw ticks remain quarter-split.
/// </summary>
internal sealed class NativeCandleArchiveStore
{
    private const int RecordSize = 69;
    private const string FolderName = "CandleHistory";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _rootPath;

    public NativeCandleArchiveStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string GetFolder(string connectorId, string symbol)
    {
        string folder = Path.Combine(
            _rootPath,
            Sanitize(connectorId),
            Sanitize(symbol),
            FolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public bool HasData(string connectorId, string symbol, string timeframe) =>
        File.Exists(GetDataPath(connectorId, symbol, timeframe));

    public HistoryImportResult Import(
        string connectorId,
        IEnumerable<Candle> candles,
        string expectedSymbol,
        string expectedTimeframe,
        CancellationToken cancellationToken,
        IProgress<HistoryImportProgress>? progress,
        long expectedRecords,
        long? minimumStartUnix,
        string? onlySegmentKey)
    {
        string folder = GetFolder(connectorId, expectedSymbol);
        string targetPath = GetDataPath(connectorId, expectedSymbol, expectedTimeframe);
        string incomingPath = targetPath + ".import.tmp";
        long imported = 0;
        long earliest = 0;
        long latest = 0;
        long lastStart = long.MinValue;
        int digits = 0;
        double point = 0;

        lock (_sync)
        {
            try
            {
                using (var stream = new FileStream(
                           incomingPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    foreach (Candle candle in candles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!string.Equals(candle.Symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(candle.Timeframe, expectedTimeframe, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "The MT5 history snapshot contains a different symbol or timeframe.");
                        }

                        if (!candle.IsClosed)
                            continue;

                        if (minimumStartUnix.HasValue && candle.StartUnix < minimumStartUnix.Value)
                            continue;

                        if (!string.IsNullOrWhiteSpace(onlySegmentKey) &&
                            !IsInsideSegment(candle.StartUnix, onlySegmentKey))
                        {
                            continue;
                        }

                        ValidateCandleFast(candle);

                        if (candle.StartUnix < lastStart)
                            throw new InvalidDataException("MT5 candle history is not ordered oldest to newest.");
                        if (candle.StartUnix == lastStart)
                            continue;

                        WriteRecord(writer, candle);
                        imported++;
                        earliest = imported == 1 ? candle.StartUnix : Math.Min(earliest, candle.StartUnix);
                        latest = Math.Max(latest, candle.StartUnix);
                        lastStart = candle.StartUnix;
                        digits = candle.Digits;
                        point = candle.Point;

                        if (imported % 25_000 == 0)
                        {
                            progress?.Report(new HistoryImportProgress(
                                0,
                                1,
                                imported,
                                Math.Max(expectedRecords, imported),
                                expectedTimeframe));
                        }
                    }

                    writer.Flush();
                    stream.Flush(true);
                }

                if (imported == 0)
                {
                    File.Delete(incomingPath);
                    return new HistoryImportResult(
                        false,
                        "MT5 did not provide candle history for this timeframe.",
                        expectedSymbol,
                        expectedTimeframe,
                        0,
                        GetFolderSize(folder));
                }

                Merge(targetPath, incomingPath);
                NativeCandleMetadata metadata = ReadMetadata(connectorId, expectedSymbol, expectedTimeframe)
                    ?? new NativeCandleMetadata(
                        connectorId,
                        expectedSymbol,
                        expectedTimeframe,
                        digits,
                        point,
                        0,
                        0,
                        0,
                        DateTime.UtcNow);

                NativeCandleFileSummary summary = InspectFile(
                    connectorId,
                    expectedSymbol,
                    expectedTimeframe,
                    targetPath,
                    metadata.Digits > 0 ? metadata.Digits : digits,
                    metadata.Point > 0 ? metadata.Point : point);

                WriteMetadata(
                    connectorId,
                    expectedSymbol,
                    expectedTimeframe,
                    new NativeCandleMetadata(
                        connectorId,
                        expectedSymbol,
                        expectedTimeframe,
                        digits > 0 ? digits : metadata.Digits,
                        point > 0 ? point : metadata.Point,
                        summary.RecordCount,
                        summary.EarliestUnix,
                        summary.LatestUnix,
                        DateTime.UtcNow));

                return new HistoryImportResult(
                    true,
                    $"Imported and verified {imported:N0} native MT5 candles.",
                    expectedSymbol,
                    expectedTimeframe,
                    imported,
                    GetFolderSize(folder));
            }
            finally
            {
                try { File.Delete(incomingPath); } catch { }
            }
        }
    }

    public IReadOnlyList<Candle> Read(
        string connectorId,
        string symbol,
        string timeframe,
        int maximumRecords)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        string path = GetDataPath(connectorId, symbol, timeframe);
        NativeCandleMetadata? metadata = ReadMetadata(connectorId, symbol, timeframe);
        if (!File.Exists(path) || metadata is null)
            return Array.Empty<Candle>();

        lock (_sync)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long total = stream.Length / RecordSize;
            int count = (int)Math.Min(total, maximumRecords);
            if (count <= 0)
                return Array.Empty<Candle>();

            stream.Seek((total - count) * RecordSize, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream);
            var result = new List<Candle>(count);
            for (int index = 0; index < count; index++)
                result.Add(ReadRecord(reader, metadata));
            return result;
        }
    }

    public IReadOnlyList<Candle> ReadFirst(
        string connectorId,
        string symbol,
        string timeframe,
        int maximumRecords)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        string path = GetDataPath(connectorId, symbol, timeframe);
        NativeCandleMetadata? metadata = ReadMetadata(connectorId, symbol, timeframe);
        if (!File.Exists(path) || metadata is null)
            return Array.Empty<Candle>();

        lock (_sync)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1 << 16,
                FileOptions.SequentialScan);
            long total = stream.Length / RecordSize;
            int count = (int)Math.Min(total, maximumRecords);
            if (count <= 0)
                return Array.Empty<Candle>();

            using var reader = new BinaryReader(stream);
            var result = new List<Candle>(count);
            for (int index = 0; index < count; index++)
                result.Add(ReadRecord(reader, metadata));
            return result;
        }
    }

    public IReadOnlyList<Candle> ReadBefore(
        string connectorId,
        string symbol,
        string timeframe,
        long beforeUnix,
        int maximumRecords)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        string path = GetDataPath(connectorId, symbol, timeframe);
        NativeCandleMetadata? metadata = ReadMetadata(connectorId, symbol, timeframe);
        if (!File.Exists(path) || metadata is null)
            return Array.Empty<Candle>();

        lock (_sync)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            long total = stream.Length / RecordSize;
            long low = 0;
            long high = total;
            while (low < high)
            {
                long middle = low + (high - low) / 2;
                stream.Seek(middle * RecordSize, SeekOrigin.Begin);
                long start = reader.ReadInt64();
                if (start < beforeUnix)
                    low = middle + 1;
                else
                    high = middle;
            }

            long endExclusive = low;
            int count = (int)Math.Min(endExclusive, maximumRecords);
            if (count <= 0)
                return Array.Empty<Candle>();

            stream.Seek((endExclusive - count) * RecordSize, SeekOrigin.Begin);
            var result = new List<Candle>(count);
            for (int index = 0; index < count; index++)
                result.Add(ReadRecord(reader, metadata));
            return result;
        }
    }

    public void UpsertClosed(string connectorId, Candle candle)
    {
        if (!candle.IsClosed)
            return;

        string path = GetDataPath(connectorId, candle.Symbol, candle.Timeframe);
        lock (_sync)
        {
            NativeCandleMetadata? metadata = ReadMetadata(
                connectorId,
                candle.Symbol,
                candle.Timeframe);

            if (!File.Exists(path) || metadata is null)
            {
                using var create = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new BinaryWriter(create);
                WriteRecord(writer, candle);
                writer.Flush();
                create.Flush(true);
                WriteMetadata(
                    connectorId,
                    candle.Symbol,
                    candle.Timeframe,
                    new NativeCandleMetadata(
                        connectorId,
                        candle.Symbol,
                        candle.Timeframe,
                        candle.Digits,
                        candle.Point,
                        1,
                        candle.StartUnix,
                        candle.StartUnix,
                        DateTime.UtcNow));
                return;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read);
            long count = stream.Length / RecordSize;
            if (count <= 0)
            {
                stream.SetLength(0);
                using var emptyWriter = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                WriteRecord(emptyWriter, candle);
                emptyWriter.Flush();
                stream.Flush(true);
                WriteMetadata(
                    connectorId,
                    candle.Symbol,
                    candle.Timeframe,
                    metadata with
                    {
                        Digits = candle.Digits,
                        Point = candle.Point,
                        RecordCount = 1,
                        EarliestUnix = candle.StartUnix,
                        LatestUnix = candle.StartUnix,
                        UpdatedUtc = DateTime.UtcNow
                    });
                return;
            }

            stream.Seek((count - 1) * RecordSize, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            long latestStart = reader.ReadInt64();

            if (candle.StartUnix > latestStart)
            {
                stream.Seek(0, SeekOrigin.End);
                using var appendWriter = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                WriteRecord(appendWriter, candle);
                appendWriter.Flush();
                stream.Flush(true);
                WriteMetadata(
                    connectorId,
                    candle.Symbol,
                    candle.Timeframe,
                    metadata with
                    {
                        Digits = candle.Digits,
                        Point = candle.Point,
                        RecordCount = count + 1,
                        EarliestUnix = metadata.EarliestUnix > 0 ? metadata.EarliestUnix : candle.StartUnix,
                        LatestUnix = candle.StartUnix,
                        UpdatedUtc = DateTime.UtcNow
                    });
                return;
            }

            if (candle.StartUnix == latestStart)
            {
                stream.Seek((count - 1) * RecordSize, SeekOrigin.Begin);
                using var replaceWriter = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                WriteRecord(replaceWriter, candle);
                replaceWriter.Flush();
                stream.Flush(true);
                WriteMetadata(
                    connectorId,
                    candle.Symbol,
                    candle.Timeframe,
                    metadata with
                    {
                        Digits = candle.Digits,
                        Point = candle.Point,
                        LatestUnix = candle.StartUnix,
                        UpdatedUtc = DateTime.UtcNow
                    });
                return;
            }
        }

        // An older repair is uncommon and uses the full exact merge path.
        Import(
            connectorId,
            new[] { candle },
            candle.Symbol,
            candle.Timeframe,
            CancellationToken.None,
            null,
            1,
            null,
            null);
    }

    public NativeCandleFileSummary? GetSummary(
        string connectorId,
        string symbol,
        string timeframe)
    {
        string path = GetDataPath(connectorId, symbol, timeframe);
        NativeCandleMetadata? metadata = ReadMetadata(connectorId, symbol, timeframe);
        if (!File.Exists(path) || metadata is null)
            return null;

        lock (_sync)
        {
            return InspectFile(
                connectorId,
                symbol,
                timeframe,
                path,
                metadata.Digits,
                metadata.Point);
        }
    }

    public IReadOnlyList<NativeCandleFileSummary> GetSummaries(
        string connectorId,
        string? symbol = null)
    {
        string connectorFolder = Path.Combine(_rootPath, Sanitize(connectorId));
        if (!Directory.Exists(connectorFolder))
            return Array.Empty<NativeCandleFileSummary>();

        IEnumerable<string> symbolFolders = string.IsNullOrWhiteSpace(symbol)
            ? Directory.EnumerateDirectories(connectorFolder)
            : new[] { Path.Combine(connectorFolder, Sanitize(symbol)) };

        var summaries = new List<NativeCandleFileSummary>();
        lock (_sync)
        {
            foreach (string symbolFolder in symbolFolders)
            {
                string candleFolder = Path.Combine(symbolFolder, FolderName);
                if (!Directory.Exists(candleFolder))
                    continue;

                string displaySymbol = Path.GetFileName(symbolFolder);
                foreach (string path in Directory.EnumerateFiles(candleFolder, "*.tlc", SearchOption.TopDirectoryOnly))
                {
                    string timeframe = Path.GetFileNameWithoutExtension(path);
                    NativeCandleMetadata? metadata = ReadMetadata(connectorId, displaySymbol, timeframe);
                    if (metadata is null)
                        continue;
                    summaries.Add(InspectFile(
                        connectorId,
                        displaySymbol,
                        timeframe,
                        path,
                        metadata.Digits,
                        metadata.Point));
                }
            }
        }

        return summaries
            .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => TimeframeOrder(item.Timeframe))
            .ToArray();
    }

    public void DeleteTimeframe(string connectorId, string symbol, string timeframe)
    {
        lock (_sync)
        {
            File.Delete(GetDataPath(connectorId, symbol, timeframe));
            File.Delete(GetMetadataPath(connectorId, symbol, timeframe));
        }
    }

    public void DeleteSymbol(string connectorId, string symbol)
    {
        string folder = GetFolder(connectorId, symbol);
        lock (_sync)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private string GetDataPath(string connectorId, string symbol, string timeframe) =>
        Path.Combine(GetFolder(connectorId, symbol), Sanitize(timeframe) + ".tlc");

    private string GetMetadataPath(string connectorId, string symbol, string timeframe) =>
        Path.Combine(GetFolder(connectorId, symbol), Sanitize(timeframe) + ".json");

    private NativeCandleMetadata? ReadMetadata(string connectorId, string symbol, string timeframe)
    {
        string path = GetMetadataPath(connectorId, symbol, timeframe);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NativeCandleMetadata>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void WriteMetadata(
        string connectorId,
        string symbol,
        string timeframe,
        NativeCandleMetadata metadata)
    {
        string path = GetMetadataPath(connectorId, symbol, timeframe);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(metadata, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static NativeCandleFileSummary InspectFile(
        string connectorId,
        string symbol,
        string timeframe,
        string path,
        int digits,
        double point)
    {
        var info = new FileInfo(path);
        long count = info.Exists ? info.Length / RecordSize : 0;
        long earliest = 0;
        long latest = 0;
        string status = info.Exists && info.Length % RecordSize == 0 ? "OK" : "Repair required";

        if (count > 0)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            earliest = reader.ReadInt64();
            stream.Seek((count - 1) * RecordSize, SeekOrigin.Begin);
            latest = reader.ReadInt64();
        }

        return new NativeCandleFileSummary(
            connectorId,
            symbol,
            timeframe,
            count,
            earliest,
            latest,
            info.Exists ? info.Length : 0,
            digits,
            point,
            status,
            path);
    }

    private static void Merge(string targetPath, string incomingPath)
    {
        if (!File.Exists(targetPath))
        {
            File.Move(incomingPath, targetPath, true);
            VerifyStructureFast(targetPath);
            return;
        }

        string mergedPath = targetPath + ".merge.tmp";
        using (var oldStream = new FileStream(
                   targetPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   1 << 20,
                   FileOptions.SequentialScan))
        using (var newStream = new FileStream(
                   incomingPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   1 << 20,
                   FileOptions.SequentialScan))
        using (var output = new FileStream(
                   mergedPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   1 << 20,
                   FileOptions.SequentialScan))
        {
            byte[] oldRecord = new byte[RecordSize];
            byte[] newRecord = new byte[RecordSize];
            bool hasOld = TryReadRawRecord(oldStream, oldRecord);
            bool hasNew = TryReadRawRecord(newStream, newRecord);

            while (hasOld || hasNew)
            {
                long oldStart = hasOld ? BitConverter.ToInt64(oldRecord, 0) : long.MaxValue;
                long newStart = hasNew ? BitConverter.ToInt64(newRecord, 0) : long.MaxValue;

                if (newStart < oldStart)
                {
                    output.Write(newRecord, 0, RecordSize);
                    hasNew = TryReadRawRecord(newStream, newRecord);
                }
                else if (oldStart < newStart)
                {
                    output.Write(oldRecord, 0, RecordSize);
                    hasOld = TryReadRawRecord(oldStream, oldRecord);
                }
                else
                {
                    // Fresh native MT5 candle wins at the matching timestamp.
                    output.Write(newRecord, 0, RecordSize);
                    hasOld = TryReadRawRecord(oldStream, oldRecord);
                    hasNew = TryReadRawRecord(newStream, newRecord);
                }
            }

            output.Flush(true);
        }

        File.Move(mergedPath, targetPath, true);
        VerifyStructureFast(targetPath);
    }

    private static void VerifyStructureFast(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length % RecordSize != 0)
            throw new InvalidDataException("Saved native candle file has an incomplete binary record.");

        long count = info.Length / RecordSize;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.RandomAccess);
        using var reader = new BinaryReader(stream);
        long first = reader.ReadInt64();
        stream.Seek((count - 1) * RecordSize, SeekOrigin.Begin);
        long last = reader.ReadInt64();
        if (first <= 0 || last < first)
            throw new InvalidDataException("Saved native candle boundaries are invalid.");
    }

    private static bool TryReadRawRecord(Stream stream, byte[] record)
    {
        if (stream.Position + RecordSize > stream.Length)
            return false;

        int offset = 0;
        while (offset < RecordSize)
        {
            int read = stream.Read(record, offset, RecordSize - offset);
            if (read <= 0)
                return false;
            offset += read;
        }
        return true;
    }

    private static void ValidateCandleFast(Candle candle)
    {
        if (candle.StartUnix <= 0 || candle.EndUnix <= candle.StartUnix)
            throw new InvalidDataException("MT5 candle timestamp range is invalid.");
        if (!double.IsFinite(candle.Open) ||
            !double.IsFinite(candle.High) ||
            !double.IsFinite(candle.Low) ||
            !double.IsFinite(candle.Close))
        {
            throw new InvalidDataException("MT5 candle contains a non-finite price.");
        }
        if (candle.High < candle.Low ||
            candle.High < candle.Open ||
            candle.High < candle.Close ||
            candle.Low > candle.Open ||
            candle.Low > candle.Close)
        {
            throw new InvalidDataException("MT5 candle OHLC values are inconsistent.");
        }
        if (candle.TickVolume < 0 || candle.RealVolume < 0 || candle.Spread < 0)
            throw new InvalidDataException("MT5 candle volume or spread is invalid.");
        if (candle.Digits < 0 || candle.Digits > 16 || !double.IsFinite(candle.Point) || candle.Point <= 0)
            throw new InvalidDataException("MT5 symbol price precision is invalid.");
    }

    private static void WriteRecord(BinaryWriter writer, Candle candle)
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

    private static Candle ReadRecord(BinaryReader reader, NativeCandleMetadata metadata)
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

    private static bool IsInsideSegment(long startUnix, string segmentKey)
    {
        string[] parts = segmentKey.Split("_to_", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) ||
            !DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end))
        {
            return true;
        }
        long minimum = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds();
        long maximum = new DateTimeOffset(DateTime.SpecifyKind(end.AddDays(1), DateTimeKind.Utc)).ToUnixTimeSeconds();
        return startUnix >= minimum && startUnix < maximum;
    }

    private static long GetFolderSize(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
            : 0;

    private static int TimeframeOrder(string timeframe)
    {
        int index = TimeframeDefinition.NativeMt5Timeframes
            .Select((value, position) => (value, position))
            .FirstOrDefault(item => string.Equals(item.value, timeframe, StringComparison.Ordinal)).position;
        return index;
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record NativeCandleMetadata(
        string ConnectorId,
        string Symbol,
        string Timeframe,
        int Digits,
        double Point,
        long RecordCount,
        long EarliestUnix,
        long LatestUnix,
        DateTime UpdatedUtc);
}

public sealed record NativeCandleFileSummary(
    string ConnectorId,
    string Symbol,
    string Timeframe,
    long RecordCount,
    long EarliestUnix,
    long LatestUnix,
    long SizeBytes,
    int Digits,
    double Point,
    string Status,
    string FilePath);
