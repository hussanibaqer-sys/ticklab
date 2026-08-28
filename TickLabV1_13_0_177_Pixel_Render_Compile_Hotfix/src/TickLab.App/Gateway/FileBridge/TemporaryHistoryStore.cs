using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TickLab.Core.History;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class TemporaryHistoryStore
{
    private const int RecordSize = 69;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _rootPath;

    public TemporaryHistoryStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _rootPath = Path.Combine(localAppData, "TickLab", "TemporaryHistory");
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public TemporaryHistoryImportResult ReplaceSnapshot(
        string connectorId,
        IEnumerable<Candle> candles,
        string expectedSymbol,
        string expectedTimeframe,
        CancellationToken cancellationToken = default,
        long expectedRecords = 0)
    {
        if (!Mt5Paths.IsValidConnectorId(connectorId))
            throw new ArgumentException("Invalid connector ID.", nameof(connectorId));

        string folder = GetDatasetFolder(connectorId, expectedSymbol, expectedTimeframe);
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, "candles.tlc.tmp");
        string target = Path.Combine(folder, "candles.tlc");

        long count = 0;
        long earliest = 0;
        long latest = 0;
        long previous = long.MinValue;
        int digits = 0;
        double point = 0;
        var validationIssues = new List<HistoryIntegrityIssue>();

        lock (_sync)
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                foreach (Candle candle in candles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.Equals(candle.Symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(candle.Timeframe, expectedTimeframe, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The MT5 temporary history contains a different symbol or timeframe.");
                    }

                    if (candle.StartUnix < previous)
                        throw new InvalidDataException("MT5 temporary history is not ordered oldest to newest.");

                    if (candle.StartUnix == previous)
                        continue;

                    HistoryIntegrityReport one = HistoryIntegrityService.ValidateCandles(
                        new[] { candle }, expectedSymbol, expectedTimeframe);
                    if (!one.Passed)
                    {
                        validationIssues.AddRange(one.Issues);
                        continue;
                    }

                    WriteCandleRecord(writer, candle);
                    count++;
                    earliest = count == 1 ? candle.StartUnix : Math.Min(earliest, candle.StartUnix);
                    latest = Math.Max(latest, candle.StartUnix);
                    previous = candle.StartUnix;
                    digits = candle.Digits;
                    point = candle.Point;
                }

                writer.Flush();
                stream.Flush(true);
            }

            bool countMismatch = expectedRecords > 0 && count != expectedRecords;
            if (count == 0 || validationIssues.Count > 0 || countMismatch)
            {
                File.Delete(temporary);
                string reason = count == 0
                    ? "MT5 did not provide valid temporary history for this timeframe."
                    : countMismatch
                        ? $"MT5 exported {expectedRecords:N0} records, but only {count:N0} passed transfer verification. The previous temporary snapshot was preserved."
                        : $"{validationIssues.Count:N0} temporary MT5 record(s) failed integrity validation. The previous snapshot was preserved.";
                return new TemporaryHistoryImportResult(
                    false,
                    reason,
                    expectedSymbol,
                    expectedTimeframe,
                    count,
                    earliest,
                    validationIssues);
            }

            File.Move(temporary, target, true);
            VerifySnapshot(target, count, earliest, latest);
            WriteJsonAtomic(
                Path.Combine(folder, "dataset.json"),
                new TemporaryDatasetMetadata(
                    connectorId,
                    expectedSymbol,
                    expectedTimeframe,
                    digits,
                    point,
                    count,
                    earliest,
                    latest,
                    expectedRecords,
                    DateTime.UtcNow));
        }

        return new TemporaryHistoryImportResult(
            true,
            $"Refreshed {count:N0} native MT5 candles.",
            expectedSymbol,
            expectedTimeframe,
            count,
            earliest,
            validationIssues);
    }

    public IReadOnlyList<Candle> ReadCandles(
        string connectorId,
        string symbol,
        string timeframe,
        int maximumRecords = int.MaxValue)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        string folder = GetDatasetFolder(connectorId, symbol, timeframe);
        string dataPath = Path.Combine(folder, "candles.tlc");
        TemporaryDatasetMetadata? metadata = ReadJson<TemporaryDatasetMetadata>(
            Path.Combine(folder, "dataset.json"));

        if (metadata is null || !File.Exists(dataPath))
            return Array.Empty<Candle>();

        lock (_sync)
        {
            using var stream = new FileStream(
                dataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            long totalRecords = stream.Length / RecordSize;
            int recordsToRead = (int)Math.Min(totalRecords, maximumRecords);
            stream.Seek((totalRecords - recordsToRead) * RecordSize, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream);
            var result = new List<Candle>(recordsToRead);

            for (int index = 0; index < recordsToRead; index++)
                result.Add(ReadCandleRecord(reader, metadata));

            return result;
        }
    }

    public IReadOnlyList<Candle> ReadCandlesBefore(
        string connectorId,
        string symbol,
        string timeframe,
        long beforeUnix,
        int maximumRecords = 200_000)
    {
        maximumRecords = Math.Max(1, maximumRecords);
        string folder = GetDatasetFolder(connectorId, symbol, timeframe);
        string dataPath = Path.Combine(folder, "candles.tlc");
        TemporaryDatasetMetadata? metadata = ReadJson<TemporaryDatasetMetadata>(
            Path.Combine(folder, "dataset.json"));

        if (metadata is null || !File.Exists(dataPath))
            return Array.Empty<Candle>();

        lock (_sync)
        {
            using var stream = new FileStream(
                dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            long totalRecords = stream.Length / RecordSize;
            long low = 0;
            long high = totalRecords;

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
            long startRecord = endExclusive - recordsToRead;
            stream.Seek(startRecord * RecordSize, SeekOrigin.Begin);
            var result = new List<Candle>(recordsToRead);
            for (int index = 0; index < recordsToRead; index++)
                result.Add(ReadCandleRecord(reader, metadata));
            return result;
        }
    }

    public long? GetFirstAvailableUnix(
        string connectorId,
        string symbol,
        string timeframe)
    {
        TemporaryDatasetMetadata? metadata = ReadJson<TemporaryDatasetMetadata>(
            Path.Combine(GetDatasetFolder(connectorId, symbol, timeframe), "dataset.json"));
        return metadata?.EarliestUnix;
    }

    public void DeleteSymbol(string connectorId, string symbol)
    {
        lock (_sync)
        {
            string folder = Path.Combine(_rootPath, Sanitize(connectorId), Sanitize(symbol));
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private string GetDatasetFolder(string connectorId, string symbol, string timeframe) =>
        Path.Combine(_rootPath, Sanitize(connectorId), Sanitize(symbol), Sanitize(timeframe));

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }


    private static void VerifySnapshot(
        string path,
        long expectedCount,
        long expectedEarliest,
        long expectedLatest)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length % RecordSize != 0 || stream.Length / RecordSize != expectedCount)
            throw new InvalidDataException("Temporary MT5 history failed record-count verification.");
        if (expectedCount == 0)
            return;

        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        long first = reader.ReadInt64();
        stream.Seek((expectedCount - 1) * RecordSize, SeekOrigin.Begin);
        long last = reader.ReadInt64();
        if (first != expectedEarliest || last != expectedLatest)
            throw new InvalidDataException("Temporary MT5 history failed boundary verification.");
    }

    private static void WriteCandleRecord(BinaryWriter writer, Candle candle)
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

    private static Candle ReadCandleRecord(BinaryReader reader, TemporaryDatasetMetadata metadata)
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

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    private sealed record TemporaryDatasetMetadata(
        string ConnectorId,
        string Symbol,
        string Timeframe,
        int Digits,
        double Point,
        long RecordCount,
        long EarliestUnix,
        long LatestUnix,
        long ExpectedRecords,
        DateTime UpdatedUtc);
}

public sealed record TemporaryHistoryImportResult(
    bool Success,
    string Message,
    string Symbol,
    string Timeframe,
    long ImportedRecords,
    long EarliestUnix,
    IReadOnlyList<HistoryIntegrityIssue> Issues);
