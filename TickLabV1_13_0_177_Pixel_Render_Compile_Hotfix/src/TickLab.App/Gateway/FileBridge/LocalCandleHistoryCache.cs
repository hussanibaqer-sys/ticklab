using System.IO;
using System.Globalization;
using System.Text;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class LocalCandleHistoryCache
{
    private static readonly TimeSpan MinimumFlushInterval =
        TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootPath;

    public LocalCandleHistoryCache()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _rootPath = Path.Combine(
            localAppData,
            "TickLab",
            "HistoryCache");
        Directory.CreateDirectory(_rootPath);
    }

    public IReadOnlyList<Candle> Read(
        string connectorId,
        string symbol,
        string timeframe)
    {
        CacheEntry entry = GetOrLoad(connectorId, symbol, timeframe);
        Prune(entry);
        return new List<Candle>(entry.Candles);
    }

    public IReadOnlyList<Candle> Merge(
        string connectorId,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> incoming)
    {
        CacheEntry entry = GetOrLoad(connectorId, symbol, timeframe);

        List<Candle> normalizedIncoming =
            NormalizeCandles(
                incoming,
                symbol,
                timeframe);

        if (normalizedIncoming.Count == 0)
            return new List<Candle>(entry.Candles);

        var merged = new List<Candle>(
            entry.Candles.Count +
            normalizedIncoming.Count);

        int existingIndex = 0;
        int incomingIndex = 0;

        while (existingIndex < entry.Candles.Count &&
               incomingIndex < normalizedIncoming.Count)
        {
            Candle existing =
                entry.Candles[existingIndex];
            Candle replacement =
                normalizedIncoming[incomingIndex];

            if (existing.StartUnix <
                replacement.StartUnix)
            {
                merged.Add(existing);
                existingIndex++;
            }
            else if (replacement.StartUnix <
                     existing.StartUnix)
            {
                merged.Add(replacement);
                incomingIndex++;
            }
            else
            {
                // The latest MT5 row replaces the cached row.
                merged.Add(replacement);
                existingIndex++;
                incomingIndex++;
            }
        }

        while (existingIndex < entry.Candles.Count)
            merged.Add(entry.Candles[existingIndex++]);

        while (incomingIndex < normalizedIncoming.Count)
            merged.Add(normalizedIncoming[incomingIndex++]);

        entry.Candles = merged;
        Prune(entry);
        entry.Dirty = true;
        return new List<Candle>(entry.Candles);
    }

    public void UpdateSnapshot(
        string connectorId,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> candles,
        bool forceWrite)
    {
        if (candles.Count == 0)
            return;

        CacheEntry entry = GetOrLoad(connectorId, symbol, timeframe);
        entry.Candles =
            NormalizeCandles(
                candles,
                symbol,
                timeframe);
        Prune(entry);
        entry.Dirty = true;
        FlushEntryIfDue(entry, forceWrite);
    }

    public void FlushDue()
    {
        foreach (CacheEntry entry in _entries.Values)
            FlushEntryIfDue(entry, force: false);
    }

    public void FlushAll()
    {
        foreach (CacheEntry entry in _entries.Values)
            FlushEntryIfDue(entry, force: true);
    }

    private CacheEntry GetOrLoad(
        string connectorId,
        string symbol,
        string timeframe)
    {
        string key = $"{connectorId}|{symbol}|{timeframe}";

        if (_entries.TryGetValue(key, out CacheEntry? existing))
            return existing;

        string path = GetCachePath(connectorId, symbol, timeframe);
        var entry = new CacheEntry(path, ReadFile(path));
        _entries[key] = entry;
        return entry;
    }

    private static List<Candle> NormalizeCandles(
        IReadOnlyList<Candle> candles,
        string symbol,
        string timeframe)
    {
        var normalized =
            new List<Candle>(candles.Count);
        bool ordered = true;

        foreach (Candle candle in candles)
        {
            if (!string.Equals(
                    candle.Symbol,
                    symbol,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    candle.Timeframe,
                    timeframe,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (normalized.Count == 0)
            {
                normalized.Add(candle);
                continue;
            }

            long previousStart =
                normalized[^1].StartUnix;

            if (candle.StartUnix > previousStart)
            {
                normalized.Add(candle);
            }
            else if (candle.StartUnix == previousStart)
            {
                normalized[^1] = candle;
            }
            else
            {
                ordered = false;
                normalized.Add(candle);
            }
        }

        if (ordered)
            return normalized;

        return normalized
            .OrderBy(candle => candle.StartUnix)
            .GroupBy(candle => candle.StartUnix)
            .Select(group => group.Last())
            .ToList();
    }

    private void Prune(CacheEntry entry)
    {
        long cutoff = DateTimeOffset.UtcNow
            .AddMonths(-3)
            .ToUnixTimeSeconds();

        int firstRetained = entry.Candles.FindIndex(
            candle => candle.EndUnix >= cutoff);

        if (firstRetained > 0)
        {
            entry.Candles.RemoveRange(0, firstRetained);
            entry.Dirty = true;
        }
        else if (firstRetained < 0 && entry.Candles.Count > 0)
        {
            entry.Candles.Clear();
            entry.Dirty = true;
        }
    }

    private void FlushEntryIfDue(
        CacheEntry entry,
        bool force)
    {
        if (!entry.Dirty)
            return;

        if (!force &&
            DateTime.UtcNow - entry.LastWriteUtc < MinimumFlushInterval)
        {
            return;
        }

        try
        {
            WriteFile(entry.Path, entry.Candles);
            entry.Dirty = false;
            entry.LastWriteUtc = DateTime.UtcNow;
        }
        catch (IOException)
        {
            // Keep the in-memory history and retry on a later flush.
        }
        catch (UnauthorizedAccessException)
        {
            // TickLab remains usable even if the cache folder is unavailable.
        }
    }

    private string GetCachePath(
        string connectorId,
        string symbol,
        string timeframe)
    {
        string folder = Path.Combine(
            _rootPath,
            Sanitize(connectorId),
            Sanitize(symbol));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Sanitize(timeframe) + ".csv");
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) ||
                character is '-' or '_'
                    ? character
                    : '_');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static List<Candle> ReadFile(string path)
    {
        if (!File.Exists(path))
            return new List<Candle>();

        var candles = new List<Candle>();

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
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
        catch (IOException)
        {
            return new List<Candle>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<Candle>();
        }

        return candles
            .OrderBy(candle => candle.StartUnix)
            .GroupBy(candle => candle.StartUnix)
            .Select(group => group.Last())
            .ToList();
    }

    private static void WriteFile(
        string path,
        IReadOnlyList<Candle> candles)
    {
        string temporaryPath = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var writer = new StreamWriter(
                   temporaryPath,
                   append: false,
                   encoding: new UTF8Encoding(false)))
        {
            writer.WriteLine(
                "symbol,timeframe,digits,point,start_unix,end_unix,start_text," +
                "open,high,low,close,tick_volume,spread,real_volume,is_closed");

            foreach (Candle candle in candles)
            {
                writer.WriteLine(string.Join(",",
                    Quote(candle.Symbol),
                    Quote(candle.Timeframe),
                    candle.Digits.ToString(CultureInfo.InvariantCulture),
                    candle.Point.ToString("R", CultureInfo.InvariantCulture),
                    candle.StartUnix.ToString(CultureInfo.InvariantCulture),
                    candle.EndUnix.ToString(CultureInfo.InvariantCulture),
                    Quote(candle.StartText),
                    candle.Open.ToString("R", CultureInfo.InvariantCulture),
                    candle.High.ToString("R", CultureInfo.InvariantCulture),
                    candle.Low.ToString("R", CultureInfo.InvariantCulture),
                    candle.Close.ToString("R", CultureInfo.InvariantCulture),
                    candle.TickVolume.ToString(CultureInfo.InvariantCulture),
                    candle.Spread.ToString(CultureInfo.InvariantCulture),
                    candle.RealVolume.ToString(CultureInfo.InvariantCulture),
                    candle.IsClosed ? "true" : "false"));
            }
        }

        File.Move(temporaryPath, path, true);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    private static Candle? TryParseCandle(string line)
    {
        IReadOnlyList<string> fields = CsvLineParser.Parse(line);

        if (fields.Count != 15 ||
            !int.TryParse(fields[2], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int digits) ||
            !double.TryParse(fields[3], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double point) ||
            !long.TryParse(fields[4], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long startUnix) ||
            !long.TryParse(fields[5], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long endUnix) ||
            !double.TryParse(fields[7], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double open) ||
            !double.TryParse(fields[8], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double high) ||
            !double.TryParse(fields[9], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double low) ||
            !double.TryParse(fields[10], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double close) ||
            !long.TryParse(fields[11], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long tickVolume) ||
            !int.TryParse(fields[12], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int spread) ||
            !long.TryParse(fields[13], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long realVolume) ||
            !bool.TryParse(fields[14], out bool isClosed))
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

    private sealed class CacheEntry
    {
        public CacheEntry(
            string path,
            List<Candle> candles)
        {
            Path = path;
            Candles = candles;
            LastWriteUtc = File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }

        public string Path { get; }
        public List<Candle> Candles { get; set; }
        public bool Dirty { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }
}
