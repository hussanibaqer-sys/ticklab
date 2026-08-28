using System.IO;
using System.Globalization;
using System.Text;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

internal sealed class TickArchiveCandleCache
{
    private readonly Dictionary<string, TickArchiveSeries> _series =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTime GetLastWriteUtc(
        string connectorFolder)
    {
        if (!Directory.Exists(connectorFolder))
            return DateTime.MinValue;

        try
        {
            DateTime stateLatest = Directory
                .EnumerateFiles(
                    connectorFolder,
                    "tick_archive_state_*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (stateLatest != DateTime.MinValue)
                return stateLatest;
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }

        DateTime latest = DateTime.MinValue;

        try
        {
            foreach (string path in Directory.EnumerateFiles(
                         connectorFolder,
                         "ticks_*.csv",
                         SearchOption.TopDirectoryOnly))
            {
                latest = Max(latest, File.GetLastWriteTimeUtc(path));
            }
        }
        catch (IOException)
        {
            return latest;
        }
        catch (UnauthorizedAccessException)
        {
            return latest;
        }

        return latest;
    }

    public IReadOnlyList<Candle> ReadCandles(
        string connectorId,
        string connectorFolder,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        bool liveOnly = false,
        int maximumBuckets = 300_000)
    {
        maximumBuckets = Math.Max(1, maximumBuckets);
        string key =
            $"{connectorId}|{symbol}|{timeframe.Key}|{liveOnly}|{maximumBuckets}";

        if (!_series.TryGetValue(key, out TickArchiveSeries? series))
        {
            series = new TickArchiveSeries(
                symbol,
                digits,
                point,
                timeframe,
                liveOnly,
                maximumBuckets);
            _series[key] = series;
        }

        return series.Refresh(connectorFolder);
    }

    public void Reset(
        string connectorId,
        string? symbol = null)
    {
        string prefix = connectorId + "|";

        foreach (string key in _series.Keys.ToArray())
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(symbol) ||
                key.StartsWith(
                    prefix + symbol + "|",
                    StringComparison.OrdinalIgnoreCase))
            {
                _series.Remove(key);
            }
        }
    }

    private static IEnumerable<string> EnumerateTickFiles(
        string connectorFolder,
        string symbol,
        bool liveOnly)
    {
        string safeSymbol = SanitizeFilePart(symbol);

        IEnumerable<string> live = Directory
            .EnumerateFiles(
                connectorFolder,
                $"ticks_live_{safeSymbol}_*.csv",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        if (liveOnly)
        {
            // Only the current and previous daily file are needed to update
            // the active candle. Permanent historical seconds are read from
            // TickLab's binary store instead of reparsing old CSV files.
            return live.TakeLast(2);
        }

        return Directory
            .EnumerateFiles(
                connectorFolder,
                $"ticks_history_{safeSymbol}_*.csv",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Concat(live);
    }

    private static string SanitizeFilePart(string value)
    {
        char[] invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        return new string(value
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
    }

    private static DateTime Max(
        DateTime left,
        DateTime right) =>
        left >= right ? left : right;

    private sealed class TickArchiveSeries
    {
        private readonly string _symbol;
        private readonly int _digits;
        private readonly double _point;
        private readonly TimeframeDefinition _timeframe;
        private readonly bool _liveOnly;
        private readonly int _maximumBuckets;
        private readonly SortedDictionary<long, TickBucket> _buckets = new();
        private readonly Dictionary<string, TickFileState> _fileStates =
            new(StringComparer.OrdinalIgnoreCase);
        private long _readOrder;
        private List<Candle> _snapshot = new();
        private bool _snapshotDirty = true;
        private long _dirtyFromBucketStart = long.MinValue;

        public TickArchiveSeries(
            string symbol,
            int digits,
            double point,
            TimeframeDefinition timeframe,
            bool liveOnly,
            int maximumBuckets)
        {
            _symbol = symbol;
            _digits = Math.Clamp(digits, 0, 10);
            _point = point > 0 ? point : 0.00001;
            _timeframe = timeframe;
            _liveOnly = liveOnly;
            _maximumBuckets = Math.Max(1, maximumBuckets);
        }

        public IReadOnlyList<Candle> Refresh(
            string connectorFolder)
        {
            if (!Directory.Exists(connectorFolder))
                return Array.Empty<Candle>();

            string[] files;

            try
            {
                files = EnumerateTickFiles(
                        connectorFolder,
                        _symbol,
                        _liveOnly)
                    .ToArray();
            }
            catch (IOException)
            {
                return Snapshot();
            }
            catch (UnauthorizedAccessException)
            {
                return Snapshot();
            }

            if (MustRebuild(files))
            {
                _buckets.Clear();
                _fileStates.Clear();
                _readOrder = 0;
                _snapshot = new List<Candle>();
                _snapshotDirty = true;
                _dirtyFromBucketStart = long.MinValue;
            }

            foreach (string path in files)
                ReadNewRows(path);

            return Snapshot();
        }

        private bool MustRebuild(
            IReadOnlyCollection<string> files)
        {
            var current = new HashSet<string>(
                files,
                StringComparer.OrdinalIgnoreCase);

            if (_fileStates.Keys.Any(path => !current.Contains(path)))
                return true;

            foreach (string path in files)
            {
                if (!_fileStates.TryGetValue(path, out TickFileState? state))
                    continue;

                try
                {
                    var info = new FileInfo(path);
                    bool historical =
                        Path.GetFileName(path)
                            .StartsWith(
                                "ticks_history_",
                                StringComparison.OrdinalIgnoreCase);

                    if (info.Length < state.Length ||
                        (historical &&
                         info.LastWriteTimeUtc > state.LastWriteUtc))
                    {
                        return true;
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
            }

            return false;
        }

        private void ReadNewRows(
            string path)
        {
            long startOffset =
                _fileStates.TryGetValue(path, out TickFileState? state)
                    ? state.Length
                    : 0;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (startOffset > stream.Length)
                    startOffset = 0;

                stream.Seek(startOffset, SeekOrigin.Begin);

                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 16 * 1024,
                    leaveOpen: true);

                bool skipHeader = startOffset == 0;

                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (skipHeader)
                    {
                        skipHeader = false;
                        continue;
                    }

                    AddTick(line);
                }

                _fileStates[path] = new TickFileState(
                    stream.Position,
                    File.GetLastWriteTimeUtc(path));
            }
            catch (IOException)
            {
                // The bridge may be replacing a history segment atomically.
                // The next polling pass will retry it.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep already loaded tick candles available.
            }
        }

        private void AddTick(
            string line)
        {
            IReadOnlyList<string> fields = CsvLineParser.Parse(line);

            if (fields.Count < 18 ||
                !string.Equals(
                    fields[2].Trim(),
                    _symbol,
                    StringComparison.OrdinalIgnoreCase) ||
                !TryLong(fields[3], out long timeMilliseconds) ||
                !TryLong(fields[4], out long timeUnix) ||
                !TryDouble(fields[5], out double bid) ||
                !TryDouble(fields[6], out double ask) ||
                !TryDouble(fields[7], out double last) ||
                !TryDouble(fields[10], out double volumeReal))
            {
                return;
            }

            if (timeUnix <= 0 && timeMilliseconds > 0)
                timeUnix = timeMilliseconds / 1000;

            if (timeUnix <= 0)
                return;

            double price = bid > 0
                ? bid
                : last > 0
                    ? last
                    : ask;

            if (!double.IsFinite(price) || price <= 0)
                return;

            long bucketStart =
                _timeframe.GetBucketStartUnix(timeUnix);
            long bucketEnd =
                _timeframe.GetBucketEndUnix(bucketStart);
            int spread =
                bid > 0 && ask > 0
                    ? (int)Math.Round((ask - bid) / _point)
                    : 0;

            long order = ++_readOrder;

            if (!_buckets.TryGetValue(bucketStart, out TickBucket? bucket))
            {
                bucket = new TickBucket(
                    bucketStart,
                    bucketEnd,
                    timeMilliseconds,
                    order,
                    price,
                    spread,
                    volumeReal);
                _buckets[bucketStart] = bucket;
            }
            else
            {
                bucket.Add(
                    timeMilliseconds,
                    order,
                    price,
                    spread,
                    volumeReal);
            }

            while (_buckets.Count > _maximumBuckets)
            {
                long oldest = _buckets.First().Key;
                _buckets.Remove(oldest);
                if (_snapshot.Count > 0 && _snapshot[0].StartUnix == oldest)
                    _snapshot.RemoveAt(0);
            }

            _snapshotDirty = true;
            _dirtyFromBucketStart = _dirtyFromBucketStart == long.MinValue
                ? bucketStart
                : Math.Min(_dirtyFromBucketStart, bucketStart);
        }

        private IReadOnlyList<Candle> Snapshot()
        {
            if (!_snapshotDirty)
                return _snapshot;

            if (_buckets.Count == 0)
            {
                _snapshot = new List<Candle>();
                _snapshotDirty = false;
                _dirtyFromBucketStart = long.MinValue;
                return _snapshot;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (_snapshot.Count == 0 || _dirtyFromBucketStart == long.MinValue)
            {
                _snapshot = _buckets.Values
                    .Select(bucket => bucket.ToCandle(
                        _symbol,
                        _timeframe.DisplayText,
                        _digits,
                        _point,
                        now))
                    .ToList();
            }
            else
            {
                int replaceIndex = LowerBoundSnapshot(_dirtyFromBucketStart);
                if (replaceIndex < _snapshot.Count)
                {
                    _snapshot.RemoveRange(
                        replaceIndex,
                        _snapshot.Count - replaceIndex);
                }

                foreach (TickBucket bucket in _buckets.Values
                             .Where(item => item.StartUnix >= _dirtyFromBucketStart))
                {
                    _snapshot.Add(bucket.ToCandle(
                        _symbol,
                        _timeframe.DisplayText,
                        _digits,
                        _point,
                        now));
                }
            }

            _snapshotDirty = false;
            _dirtyFromBucketStart = long.MinValue;
            return _snapshot;
        }

        private int LowerBoundSnapshot(long startUnix)
        {
            int low = 0;
            int high = _snapshot.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_snapshot[middle].StartUnix < startUnix)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private static bool TryLong(
            string text,
            out long value) =>
            long.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);

        private static bool TryDouble(
            string text,
            out double value) =>
            double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
    }

    private sealed record TickFileState(
        long Length,
        DateTime LastWriteUtc);

    private sealed class TickBucket
    {
        private long _firstTimeMilliseconds;
        private long _lastTimeMilliseconds;
        private long _firstOrder;
        private long _lastOrder;
        private double _open;
        private double _high;
        private double _low;
        private double _close;
        private long _tickVolume;
        private int _spread;
        private double _realVolume;

        public TickBucket(
            long startUnix,
            long endUnix,
            long timeMilliseconds,
            long order,
            double price,
            int spread,
            double volumeReal)
        {
            StartUnix = startUnix;
            EndUnix = endUnix;
            _firstTimeMilliseconds = timeMilliseconds;
            _lastTimeMilliseconds = timeMilliseconds;
            _firstOrder = order;
            _lastOrder = order;
            _open = price;
            _high = price;
            _low = price;
            _close = price;
            _tickVolume = 1;
            _spread = spread;
            _realVolume = Math.Max(0, volumeReal);
        }

        public long StartUnix { get; }
        public long EndUnix { get; }

        public void Add(
            long timeMilliseconds,
            long order,
            double price,
            int spread,
            double volumeReal)
        {
            if (timeMilliseconds < _firstTimeMilliseconds ||
                (timeMilliseconds == _firstTimeMilliseconds &&
                 order < _firstOrder))
            {
                _firstTimeMilliseconds = timeMilliseconds;
                _firstOrder = order;
                _open = price;
            }

            if (timeMilliseconds > _lastTimeMilliseconds ||
                (timeMilliseconds == _lastTimeMilliseconds &&
                 order >= _lastOrder))
            {
                _lastTimeMilliseconds = timeMilliseconds;
                _lastOrder = order;
                _close = price;
                _spread = spread;
            }

            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
            _tickVolume++;
            _realVolume += Math.Max(0, volumeReal);
        }

        public Candle ToCandle(
            string symbol,
            string timeframe,
            int digits,
            double point,
            long nowUnix)
        {
            string startText = DateTimeOffset
                .FromUnixTimeSeconds(StartUnix)
                .ToUniversalTime()
                .ToString(
                    "yyyy.MM.dd HH:mm:ss",
                    CultureInfo.InvariantCulture);

            return new Candle(
                symbol,
                timeframe,
                digits,
                point,
                StartUnix,
                EndUnix,
                startText,
                _open,
                _high,
                _low,
                _close,
                _tickVolume,
                _spread,
                (long)Math.Round(_realVolume),
                EndUnix <= nowUnix);
        }
    }
}
