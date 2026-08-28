using System.Globalization;
using System.Text;
using System.Text.Json;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

internal sealed class CanonicalTickArchiveStore
{
    private const int RecordSize = 60;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly object _replayIndexSync = new();
    private readonly Dictionary<string, ReplaySourceIndex> _replayIndexMemory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _coverageSync = new();
    private readonly Dictionary<string, CanonicalTickCoverage> _canonicalCoverageCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _canonicalSegmentFilesCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BridgeCoverageCacheEntry> _bridgeCoverageCache =
        new(StringComparer.OrdinalIgnoreCase);

    public CanonicalTickSyncResult SyncFromBridge(
        string sourceFolder,
        string targetRoot,
        string symbol,
        bool includeHistorical,
        bool includeRecentAndLive,
        bool forceHistoricalReindex,
        int serverUtcOffsetMinutes,
        long? minimumStartUnix,
        string? onlySegmentKey,
        long? maximumEndUnix,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);
        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        var candidates = new List<string>();

        if (includeHistorical)
        {
            // Historical chart repair is always range-bounded. Reuse the same
            // filename-range catalog as seconds/Tick navigation instead of
            // recursively enumerating years of raw CSV snapshots for every page.
            if (minimumStartUnix.HasValue || maximumEndUnix.HasValue)
            {
                long startMilliseconds = minimumStartUnix.HasValue
                    ? Math.Max(0L, checked(minimumStartUnix.Value * 1000L))
                    : 0L;
                long endMillisecondsExclusive = maximumEndUnix.HasValue
                    ? maximumEndUnix.Value >= (long.MaxValue / 1000L) - 1L
                        ? long.MaxValue
                        : checked((maximumEndUnix.Value + 1L) * 1000L)
                    : long.MaxValue;
                candidates.AddRange(ResolveReplayHistoricalSources(
                    sourceFolder,
                    targetRoot,
                    safeSymbol,
                    startMilliseconds,
                    endMillisecondsExclusive,
                    allowFullIndexRebuild: true));
            }
            else
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    sourceFolder,
                    $"ticks_history_{safeSymbol}_*.csv",
                    SearchOption.AllDirectories));
            }

            if (!string.IsNullOrWhiteSpace(onlySegmentKey))
            {
                candidates = candidates
                    .Where(path => HistoricalSourceOverlapsRequest(
                        path,
                        minimumStartUnix,
                        maximumEndUnix,
                        onlySegmentKey,
                        serverUtcOffsetMinutes))
                    .ToList();
            }
        }

        if (includeRecentAndLive)
        {
            // The bridge rotates a small repair slice through the moving
            // 30-minute window. Each slice is authoritative only for the timestamp
            // groups it actually contains.
            candidates.AddRange(Directory.EnumerateFiles(
                sourceFolder,
                $"ticks_recent_{safeSymbol}.csv",
                SearchOption.AllDirectories));

            IEnumerable<string> liveCandidates = Directory.EnumerateFiles(
                    sourceFolder,
                    $"ticks_live_{safeSymbol}_*.csv",
                    SearchOption.AllDirectories)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

            // During normal live saving only the newest daily files can grow.
            // Do not rescan every old live CSV every five seconds. A full Import
            // or Recheck still includes every live file for archival completeness.
            candidates.AddRange(includeHistorical
                ? liveCandidates.Where(path => LiveSourceOverlapsRequest(
                    path,
                    minimumStartUnix,
                    maximumEndUnix,
                    onlySegmentKey,
                    serverUtcOffsetMinutes))
                : liveCandidates.TakeLast(2));
        }

        string statePath = Path.Combine(targetRoot, "tick_sync_state.json");
        TickSyncState state = ReadJson<TickSyncState>(statePath) ??
            new TickSyncState(new Dictionary<string, TickSourceState>(StringComparer.OrdinalIgnoreCase));
        var updatedStates = (state.Sources ??
                new Dictionary<string, TickSourceState>())
            .ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        long parsedRows = 0;
        long canonicalTicks = 0;
        int processedFiles = 0;
        var touchedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int changedSinceCheckpoint = 0;

        foreach (string sourcePath in candidates
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            // Cancellation is a normal watchdog outcome. Stop between source
            // files. Expensive source parsing and merge preparation deliberately
            // run OUTSIDE the canonical read lock. Foreground chart/Find/Replay
            // readers therefore never sit behind a whole CSV import. Only the
            // final atomic segment replacement is serialized.
            if (cancellationToken.IsCancellationRequested)
                break;

            bool completedSource = false;
            bool stateChangedThisSource = false;
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length == 0)
            {
                completedSource = true;
            }
            else
            {
                string stateKey = Path.GetFullPath(sourcePath);
                updatedStates.TryGetValue(stateKey, out TickSourceState? previous);
                string sourceName = Path.GetFileName(sourcePath);
                bool isGrowingLive = sourceName
                    .StartsWith("ticks_live_", StringComparison.OrdinalIgnoreCase);
                bool isAuthoritativeSnapshot = sourceName
                    .StartsWith("ticks_history_", StringComparison.OrdinalIgnoreCase) ||
                    sourceName.StartsWith("ticks_recent_", StringComparison.OrdinalIgnoreCase);
                bool forceThisHistoricalSource =
                    forceHistoricalReindex &&
                    sourceName.StartsWith(
                        "ticks_history_",
                        StringComparison.OrdinalIgnoreCase);
                bool unchanged = !forceThisHistoricalSource &&
                    previous is not null &&
                    previous.ProcessedBytes == info.Length &&
                    previous.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks;
                if (unchanged)
                {
                    completedSource = true;
                }
                else
                {
                    long startOffset = 0;
                    TickMergeMode mergeMode = isAuthoritativeSnapshot
                        ? TickMergeMode.ReplaceTimestampGroup
                        : TickMergeMode.MaximumMultiplicity;
                    if (isGrowingLive && previous is not null &&
                        previous.ProcessedBytes > 0 &&
                        previous.ProcessedBytes <= info.Length)
                    {
                        // Resume from the beginning of the last timestamp
                        // group so a growing live file remains idempotent.
                        startOffset = Math.Clamp(previous.ResumeOffset, 0, info.Length);
                    }

                    bool importWholeHistoricalSource =
                        !includeRecentAndLive &&
                        sourceName.StartsWith(
                            "ticks_history_",
                            StringComparison.OrdinalIgnoreCase);

                    TickSourceImportResult imported = ImportSourceFile(
                        sourcePath,
                        targetRoot,
                        symbol,
                        startOffset,
                        mergeMode,
                        serverUtcOffsetMinutes,
                        importWholeHistoricalSource ? null : minimumStartUnix,
                        importWholeHistoricalSource ? null : maximumEndUnix,
                        onlySegmentKey,
                        cancellationToken);

                    if (imported.Completed)
                    {
                        parsedRows += imported.ParsedRows;
                        canonicalTicks += imported.CanonicalTicks;
                        processedFiles++;
                        foreach (string segment in imported.TouchedSegments)
                            touchedSegments.Add(segment);

                        info.Refresh();
                        long resumeOffset = isGrowingLive
                            ? FindLastTimestampGroupStart(sourcePath, symbol)
                            : 0;
                        updatedStates[stateKey] = new TickSourceState(
                            info.Length,
                            info.LastWriteTimeUtc.Ticks,
                            DateTime.UtcNow,
                            resumeOffset);

                        stateChangedThisSource = true;
                        completedSource = true;
                    }
                }
            }

            if (stateChangedThisSource)
            {
                changedSinceCheckpoint++;
                if (changedSinceCheckpoint >= 16)
                {
                    lock (_sync)
                        WriteJsonAtomic(statePath, new TickSyncState(updatedStates));
                    changedSinceCheckpoint = 0;
                }
            }

            if (!completedSource)
                break;

            // Yield between small source files so foreground replay reads can
            // acquire the canonical lock without waiting for a whole quarter.
            Thread.Yield();
        }

        if (changedSinceCheckpoint > 0)
        {
            lock (_sync)
                WriteJsonAtomic(statePath, new TickSyncState(updatedStates));
        }

        if (touchedSegments.Count > 0)
        {
            string coverageKey = Path.GetFullPath(targetRoot);
            lock (_coverageSync)
            {
                _canonicalCoverageCache.Remove(coverageKey);
                _canonicalSegmentFilesCache.Remove(coverageKey);
            }
        }

        return new CanonicalTickSyncResult(
            processedFiles,
            parsedRows,
            canonicalTicks,
            touchedSegments.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<Candle> ReadCandles(
        string targetRoot,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        int maximumRecords,
        int serverUtcOffsetMinutes,
        long? beforeUnix,
        long? minimumUnix,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedSegmentKeys = null)
    {
        if (!Directory.Exists(targetRoot))
            return Array.Empty<Candle>();

        maximumRecords = Math.Max(1, maximumRecords);
        long? latestAvailable = FindLatestTickUnix(
            targetRoot,
            beforeUnix,
            excludedSegmentKeys);
        if (!latestAvailable.HasValue)
            return Array.Empty<Candle>();

        long duration = Math.Max(1, timeframe.ToApproximateSeconds());
        long calculatedMinimum = latestAvailable.Value - checked(duration * maximumRecords * 2L);
        long rangeMinimum = minimumUnix.HasValue
            ? Math.Max(minimumUnix.Value, calculatedMinimum)
            : calculatedMinimum;
        long rangeMaximum = beforeUnix ?? long.MaxValue;

        string[] segmentFiles = GetCanonicalSegmentFiles(targetRoot);

        var result = new List<Candle>(Math.Min(maximumRecords, 100_000));
        TickCandleBuilder? bucket = null;
        long serverNow = Mt5ServerClock.ServerNowUnix(serverUtcOffsetMinutes);

        lock (_sync)
        {
            foreach (string path in segmentFiles)
            {
                // Replacing one chart request with another is expected. Stop
                // this read cooperatively instead of throwing an exception
                // that Visual Studio may break on as a user-unhandled error.
                if (cancellationToken.IsCancellationRequested)
                    return Array.Empty<Candle>();
                string? segmentKey = Path.GetFileName(Path.GetDirectoryName(path));
                if (string.IsNullOrWhiteSpace(segmentKey))
                    continue;
                if (excludedSegmentKeys is not null && excludedSegmentKeys.Contains(segmentKey))
                    continue;

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                long total = stream.Length / RecordSize;
                if (total <= 0)
                    continue;

                using var reader = new BinaryReader(stream);
                long startIndex = LowerBoundTime(reader, stream, total, checked(rangeMinimum * 1000L));
                long endIndex = rangeMaximum == long.MaxValue
                    ? total
                    : LowerBoundTime(reader, stream, total, checked(rangeMaximum * 1000L));
                if (startIndex >= endIndex)
                    continue;

                stream.Seek(startIndex * RecordSize, SeekOrigin.Begin);
                for (long index = startIndex; index < endIndex; index++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return Array.Empty<Candle>();

                    CanonicalTick tick = ReadRecord(reader);
                    long tickUnix = tick.TimeMilliseconds / 1000;
                    long bucketStart = timeframe.GetBucketStartUnix(
                        tickUnix,
                        serverUtcOffsetMinutes);
                    long bucketEnd = timeframe.GetBucketEndUnix(
                        bucketStart,
                        serverUtcOffsetMinutes);
                    double price = tick.Bid > 0
                        ? tick.Bid
                        : tick.Last > 0
                            ? tick.Last
                            : tick.Ask;
                    if (!double.IsFinite(price) || price <= 0)
                        continue;

                    int spread = tick.Bid > 0 && tick.Ask > 0 && point > 0
                        ? Math.Max(0, (int)Math.Round((tick.Ask - tick.Bid) / point))
                        : 0;

                    if (bucket is null || bucket.StartUnix != bucketStart)
                    {
                        if (bucket is not null)
                            result.Add(bucket.ToCandle(symbol, timeframe.DisplayText, digits, point, serverNow));
                        bucket = new TickCandleBuilder(bucketStart, bucketEnd, price, spread, tick);
                    }
                    else
                    {
                        bucket.Add(price, spread, tick);
                    }
                }
            }
        }

        if (bucket is not null)
            result.Add(bucket.ToCandle(symbol, timeframe.DisplayText, digits, point, serverNow));

        return result.Count <= maximumRecords
            ? result
            : result.TakeLast(maximumRecords).ToArray();
    }


    /// <summary>
    /// Builds a bounded seconds-candle page directly from the completed
    /// ticks_history_* bridge snapshots. This is a chart-read fallback, not a
    /// replacement for the canonical ticks.tlt archive: it lets historical
    /// seconds navigation work immediately even when background canonical
    /// indexing has not yet reached the requested timestamp.
    /// </summary>
    public IReadOnlyList<Candle> ReadHistoricalSourceCandles(
        string sourceFolder,
        string targetRoot,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        int maximumRecords,
        int serverUtcOffsetMinutes,
        long beforeUnix,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedSegmentKeys = null)
    {
        if (!Directory.Exists(sourceFolder) ||
            string.IsNullOrWhiteSpace(targetRoot) ||
            string.IsNullOrWhiteSpace(symbol) ||
            timeframe.Unit != TimeframeUnit.Second ||
            maximumRecords <= 0 ||
            beforeUnix <= 0)
        {
            return Array.Empty<Candle>();
        }

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        long beforeMilliseconds;
        long requiredActiveMilliseconds;
        try
        {
            beforeMilliseconds = checked(beforeUnix * 1000L);
            int timeframeSeconds =
                (int)Math.Clamp(timeframe.ToApproximateSeconds(), 1L, int.MaxValue);
            // A small safety margin is enough because completed history files
            // already describe active market time. The old 1.5-page multiplier
            // made 15s/30s/45s pages parse dozens of unnecessary 30-minute CSVs.
            requiredActiveMilliseconds = checked(
                Math.Max(1L, timeframeSeconds) *
                Math.Max(1L, maximumRecords) *
                1100L);
        }
        catch (OverflowException)
        {
            return Array.Empty<Candle>();
        }

        // Manual seconds-history paging uses the same cached filename-range
        // index as replay/Find Candle. This avoids enumerating the complete raw
        // history tree every time the user scrolls one page farther left.
        const long EightDaysMilliseconds = 8L * 24L * 60L * 60L * 1000L;
        long searchHalfSpan = Math.Max(EightDaysMilliseconds, requiredActiveMilliseconds * 4L);
        long searchStartMilliseconds = Math.Max(0L, beforeMilliseconds - searchHalfSpan);
        IReadOnlyList<string> indexedPaths = ResolveReplayHistoricalSources(
            sourceFolder,
            targetRoot,
            safeSymbol,
            searchStartMilliseconds,
            beforeMilliseconds);

        var candidates = new List<(string Path, long StartMilliseconds, long EndMilliseconds)>();
        foreach (string path in indexedPaths)
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();
            if (!TryGetHistoricalSourceRangeMilliseconds(
                    path,
                    out long startMilliseconds,
                    out long endMilliseconds) ||
                startMilliseconds >= beforeMilliseconds)
            {
                continue;
            }

            candidates.Add((path, startMilliseconds, endMilliseconds));
        }

        if (candidates.Count == 0)
            return Array.Empty<Candle>();

        candidates.Sort((left, right) =>
            right.EndMilliseconds.CompareTo(left.EndMilliseconds));
        var selected = new List<(string Path, long StartMilliseconds, long EndMilliseconds)>();
        long selectedActiveMilliseconds = 0;
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();

            long usableEnd = Math.Min(candidate.EndMilliseconds, beforeMilliseconds - 1L);
            if (usableEnd < candidate.StartMilliseconds)
                continue;

            selected.Add(candidate);
            selectedActiveMilliseconds = checked(
                selectedActiveMilliseconds +
                Math.Max(1L, usableEnd - candidate.StartMilliseconds + 1L));
            if (selectedActiveMilliseconds >= requiredActiveMilliseconds)
                break;
        }

        if (selected.Count == 0)
            return Array.Empty<Candle>();

        long minimumMilliseconds = selected.Min(item => item.StartMilliseconds);
        var buckets = new SortedDictionary<long, TickCandleBuilder>();
        long serverNow = Mt5ServerClock.ServerNowUnix(serverUtcOffsetMinutes);

        foreach (var candidate in selected
                     .OrderBy(item => item.StartMilliseconds)
                     .ThenBy(item => item.EndMilliseconds))
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();

            try
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 128 * 1024);
                _ = reader.ReadLine(); // header

                while (!reader.EndOfStream)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return Array.Empty<Candle>();

                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line) ||
                        !TryParseTick(line, symbol, out CanonicalTick tick) ||
                        tick.TimeMilliseconds < minimumMilliseconds ||
                        tick.TimeMilliseconds >= beforeMilliseconds)
                    {
                        continue;
                    }

                    long tickUnix = tick.TimeMilliseconds / 1000L;
                    if (excludedSegmentKeys is not null)
                    {
                        string segmentKey = PersistentHistoryStore.GetSegmentKey(
                            tickUnix,
                            serverUtcOffsetMinutes);
                        if (excludedSegmentKeys.Contains(segmentKey))
                            continue;
                    }

                    long bucketStart = timeframe.GetBucketStartUnix(
                        tickUnix,
                        serverUtcOffsetMinutes);
                    if (bucketStart >= beforeUnix)
                        continue;
                    long bucketEnd = timeframe.GetBucketEndUnix(
                        bucketStart,
                        serverUtcOffsetMinutes);
                    double price = tick.Bid > 0
                        ? tick.Bid
                        : tick.Last > 0
                            ? tick.Last
                            : tick.Ask;
                    if (!double.IsFinite(price) || price <= 0)
                        continue;

                    int spread = tick.Bid > 0 && tick.Ask > 0 && point > 0
                        ? Math.Max(0, (int)Math.Round((tick.Ask - tick.Bid) / point))
                        : 0;

                    if (!buckets.TryGetValue(bucketStart, out TickCandleBuilder? builder))
                    {
                        builder = new TickCandleBuilder(
                            bucketStart,
                            bucketEnd,
                            price,
                            spread,
                            tick);
                        buckets.Add(bucketStart, builder);
                    }
                    else
                    {
                        builder.Add(price, spread, tick);
                    }
                }
            }
            catch (IOException)
            {
                // A bridge snapshot can be atomically replaced while TickLab is
                // reading it. Continue with the other completed snapshots.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (buckets.Count == 0)
            return Array.Empty<Candle>();

        return buckets.Values
            .Select(builder => builder.ToCandle(
                symbol,
                timeframe.DisplayText,
                digits,
                point,
                serverNow))
            .TakeLast(maximumRecords)
            .ToArray();
    }


    /// <summary>
    /// Direct Find Candle path for seconds charts. It selects the raw bridge
    /// snapshots nearest the requested timestamp and builds a centered candle
    /// page from only those files. It never walks backward from the live edge
    /// or through every intervening historical window.
    /// </summary>
    public IReadOnlyList<Candle> ReadHistoricalSourceCandlesAroundTimestamp(
        string sourceFolder,
        string targetRoot,
        string symbol,
        int digits,
        double point,
        TimeframeDefinition timeframe,
        int maximumRecords,
        int serverUtcOffsetMinutes,
        long focusUnix,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedSegmentKeys = null)
    {
        if (!Directory.Exists(sourceFolder) ||
            string.IsNullOrWhiteSpace(targetRoot) ||
            string.IsNullOrWhiteSpace(symbol) ||
            timeframe.Unit != TimeframeUnit.Second ||
            maximumRecords <= 0 ||
            focusUnix <= 0)
        {
            return Array.Empty<Candle>();
        }

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        long focusMilliseconds;
        long requiredActiveMilliseconds;
        try
        {
            focusMilliseconds = checked(focusUnix * 1000L);
            int timeframeSeconds =
                (int)Math.Clamp(timeframe.ToApproximateSeconds(), 1L, int.MaxValue);
            // About 1.5 pages of active source time gives Find Candle enough
            // context on both sides while keeping IO strictly bounded.
            requiredActiveMilliseconds = checked(
                Math.Max(1L, timeframeSeconds) *
                Math.Max(1L, maximumRecords) *
                1500L);
        }
        catch (OverflowException)
        {
            return Array.Empty<Candle>();
        }

        // Foreground Find Candle must remain target-local. A few hours covers
        // the normal 1,600-candle page plus neighboring 30-minute bridge
        // snapshots. Closed-market timestamps fail quickly instead of widening
        // into a multi-day filesystem walk.
        const long FourHoursMilliseconds = 4L * 60L * 60L * 1000L;
        long searchHalfSpan = Math.Max(FourHoursMilliseconds, requiredActiveMilliseconds * 4L);
        long searchStartMilliseconds = Math.Max(0L, focusMilliseconds - searchHalfSpan);
        long searchEndMillisecondsExclusive;
        try
        {
            searchEndMillisecondsExclusive = checked(focusMilliseconds + searchHalfSpan + 1L);
        }
        catch (OverflowException)
        {
            searchEndMillisecondsExclusive = long.MaxValue;
        }

        var candidates = new List<(string Path, long StartMilliseconds, long EndMilliseconds)>();
        IReadOnlyList<string> indexedPaths = ResolveReplayHistoricalSources(
            sourceFolder,
            targetRoot,
            safeSymbol,
            searchStartMilliseconds,
            searchEndMillisecondsExclusive,
            allowFullIndexRebuild: false);
        foreach (string path in indexedPaths)
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();
            if (TryGetHistoricalSourceRangeMilliseconds(
                    path,
                    out long startMilliseconds,
                    out long endMilliseconds))
            {
                candidates.Add((path, startMilliseconds, endMilliseconds));
            }
        }

        if (candidates.Count == 0)
            return Array.Empty<Candle>();

        static long DistanceToTimestamp(
            (string Path, long StartMilliseconds, long EndMilliseconds) item,
            long timestamp)
        {
            if (timestamp < item.StartMilliseconds)
                return item.StartMilliseconds - timestamp;
            if (timestamp > item.EndMilliseconds)
                return timestamp - item.EndMilliseconds;
            return 0;
        }

        var selected = new List<(string Path, long StartMilliseconds, long EndMilliseconds)>();
        long selectedActiveMilliseconds = 0;
        foreach (var candidate in candidates
                     .OrderBy(item => DistanceToTimestamp(item, focusMilliseconds))
                     .ThenBy(item => item.StartMilliseconds))
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();

            selected.Add(candidate);
            selectedActiveMilliseconds = checked(
                selectedActiveMilliseconds +
                Math.Max(1L, candidate.EndMilliseconds - candidate.StartMilliseconds + 1L));
            if (selectedActiveMilliseconds >= requiredActiveMilliseconds)
                break;
        }

        if (selected.Count == 0)
            return Array.Empty<Candle>();

        var buckets = new SortedDictionary<long, TickCandleBuilder>();
        long serverNow = Mt5ServerClock.ServerNowUnix(serverUtcOffsetMinutes);
        foreach (var candidate in selected
                     .OrderBy(item => item.StartMilliseconds)
                     .ThenBy(item => item.EndMilliseconds))
        {
            if (cancellationToken.IsCancellationRequested)
                return Array.Empty<Candle>();

            try
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 128 * 1024);
                _ = reader.ReadLine(); // header

                while (!reader.EndOfStream)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return Array.Empty<Candle>();

                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line) ||
                        !TryParseTick(line, symbol, out CanonicalTick tick))
                    {
                        continue;
                    }

                    long tickUnix = tick.TimeMilliseconds / 1000L;
                    if (excludedSegmentKeys is not null)
                    {
                        string segmentKey = PersistentHistoryStore.GetSegmentKey(
                            tickUnix,
                            serverUtcOffsetMinutes);
                        if (excludedSegmentKeys.Contains(segmentKey))
                            continue;
                    }

                    long bucketStart = timeframe.GetBucketStartUnix(
                        tickUnix,
                        serverUtcOffsetMinutes);
                    long bucketEnd = timeframe.GetBucketEndUnix(
                        bucketStart,
                        serverUtcOffsetMinutes);
                    double price = tick.Bid > 0
                        ? tick.Bid
                        : tick.Last > 0
                            ? tick.Last
                            : tick.Ask;
                    if (!double.IsFinite(price) || price <= 0)
                        continue;

                    int spread = tick.Bid > 0 && tick.Ask > 0 && point > 0
                        ? Math.Max(0, (int)Math.Round((tick.Ask - tick.Bid) / point))
                        : 0;

                    if (!buckets.TryGetValue(bucketStart, out TickCandleBuilder? builder))
                    {
                        builder = new TickCandleBuilder(
                            bucketStart,
                            bucketEnd,
                            price,
                            spread,
                            tick);
                        buckets.Add(bucketStart, builder);
                    }
                    else
                    {
                        builder.Add(price, spread, tick);
                    }
                }
            }
            catch (IOException)
            {
                // Snapshot may be atomically replaced by MT5; continue with the
                // other nearest files instead of turning Find Candle into a scan.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (buckets.Count == 0)
            return Array.Empty<Candle>();

        Candle[] all = buckets.Values
            .Select(builder => builder.ToCandle(
                symbol,
                timeframe.DisplayText,
                digits,
                point,
                serverNow))
            .OrderBy(candle => candle.StartUnix)
            .ToArray();
        if (all.Length <= maximumRecords)
            return all;

        long focusBucketStart = timeframe.GetBucketStartUnix(
            focusUnix,
            serverUtcOffsetMinutes);
        int low = 0;
        int high = all.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (all[middle].StartUnix < focusBucketStart)
                low = middle + 1;
            else
                high = middle;
        }

        int insertion = low;
        int desiredLeft = maximumRecords / 2;
        int start = Math.Clamp(insertion - desiredLeft, 0, all.Length - maximumRecords);
        return all.Skip(start).Take(maximumRecords).ToArray();
    }

    public CanonicalTickReadResult ReadTicks(
        string targetRoot,
        long startMilliseconds,
        long? endMilliseconds,
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(targetRoot) || maximumRecords <= 0)
            return CanonicalTickReadResult.Empty;

        long upperExclusive = endMilliseconds ?? long.MaxValue;
        var result = new List<MarketTick>(Math.Min(maximumRecords, 250_000));
        bool hasMore = false;
        long nextStart = startMilliseconds;

        string[] segmentFiles = GetCanonicalSegmentFiles(targetRoot);

        lock (_sync)
        {
            foreach (string path in segmentFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                long total = stream.Length / RecordSize;
                if (total <= 0)
                    continue;

                using var reader = new BinaryReader(stream);
                long first = LowerBoundTime(reader, stream, total, startMilliseconds);
                long last = upperExclusive == long.MaxValue
                    ? total
                    : LowerBoundTime(reader, stream, total, upperExclusive);
                if (first >= last)
                    continue;

                stream.Seek(first * RecordSize, SeekOrigin.Begin);
                for (long index = first; index < last; index++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    CanonicalTick tick = ReadRecord(reader);
                    int multiplicity = Math.Max(1, tick.Multiplicity);
                    if (result.Count > 0 && result.Count + multiplicity > maximumRecords)
                    {
                        hasMore = true;
                        nextStart = tick.TimeMilliseconds;
                        break;
                    }

                    for (int copy = 0; copy < multiplicity && result.Count < maximumRecords; copy++)
                    {
                        result.Add(new MarketTick(
                            tick.TimeMilliseconds,
                            tick.TimeMilliseconds / 1000,
                            tick.Bid,
                            tick.Ask,
                            tick.Last,
                            tick.Volume,
                            unchecked((uint)Math.Max(0, tick.Flags)),
                            tick.VolumeReal));
                    }
                    nextStart = tick.TimeMilliseconds + 1;

                    if (result.Count >= maximumRecords)
                    {
                        hasMore = index + 1 < last;
                        break;
                    }
                }

                if (hasMore || cancellationToken.IsCancellationRequested)
                    break;
            }
        }

        if (result.Count == 0)
            nextStart = startMilliseconds;
        return new CanonicalTickReadResult(result, hasMore, nextStart);
    }

    /// <summary>
    /// Reads the ticks immediately preceding <paramref name="endMillisecondsExclusive"/>.
    /// Unlike the forward range reader, this is safe for chart history paging because
    /// a record limit can never create a missing middle between the returned page and
    /// the already-visible newer ticks.
    /// </summary>
    public CanonicalTickReadResult ReadTicksBefore(
        string targetRoot,
        long endMillisecondsExclusive,
        int maximumRecords,
        CancellationToken cancellationToken,
        long minimumMilliseconds = 0)
    {
        if (!Directory.Exists(targetRoot) || maximumRecords <= 0 || endMillisecondsExclusive <= minimumMilliseconds)
            return CanonicalTickReadResult.Empty;

        string[] segmentFiles = GetCanonicalSegmentFiles(targetRoot)
            .Reverse()
            .ToArray();

        var chunksNewestFirst = new List<List<MarketTick>>();
        int remaining = maximumRecords;
        bool hasMore = false;

        lock (_sync)
        {
            for (int fileIndex = 0; fileIndex < segmentFiles.Length && remaining > 0; fileIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string path = segmentFiles[fileIndex];
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                long total = stream.Length / RecordSize;
                if (total <= 0)
                    continue;

                using var reader = new BinaryReader(stream);
                long first = LowerBoundTime(reader, stream, total, minimumMilliseconds);
                long lastExclusive = LowerBoundTime(reader, stream, total, endMillisecondsExclusive);
                if (first >= lastExclusive)
                    continue;

                // Every canonical record expands to at least one market tick. Reading
                // at most 'remaining' records from the tail is therefore sufficient
                // to obtain the last 'remaining' ticks without scanning a quarter.
                long startIndex = Math.Max(first, lastExclusive - remaining);
                var chunk = new List<MarketTick>(Math.Min(remaining * 2, 250_000));
                stream.Seek(startIndex * RecordSize, SeekOrigin.Begin);
                for (long index = startIndex; index < lastExclusive; index++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    CanonicalTick tick = ReadRecord(reader);
                    int multiplicity = Math.Max(1, tick.Multiplicity);
                    for (int copy = 0; copy < multiplicity; copy++)
                    {
                        chunk.Add(new MarketTick(
                            tick.TimeMilliseconds,
                            tick.TimeMilliseconds / 1000,
                            tick.Bid,
                            tick.Ask,
                            tick.Last,
                            tick.Volume,
                            unchecked((uint)Math.Max(0, tick.Flags)),
                            tick.VolumeReal));
                    }
                }

                if (chunk.Count > remaining)
                    chunk = chunk.TakeLast(remaining).ToList();
                if (chunk.Count == 0)
                    continue;

                chunksNewestFirst.Add(chunk);
                remaining -= chunk.Count;
                if (remaining <= 0)
                {
                    hasMore = startIndex > first || fileIndex + 1 < segmentFiles.Length;
                    break;
                }
            }
        }

        if (chunksNewestFirst.Count == 0)
            return CanonicalTickReadResult.Empty;

        var result = new List<MarketTick>(maximumRecords - remaining);
        for (int index = chunksNewestFirst.Count - 1; index >= 0; index--)
            result.AddRange(chunksNewestFirst[index]);

        if (result.Count > maximumRecords)
            result = result.TakeLast(maximumRecords).ToList();
        long nextStart = result.Count > 0 ? result[0].TimeMilliseconds : minimumMilliseconds;
        return new CanonicalTickReadResult(result, hasMore, nextStart);
    }


    /// <summary>
    /// Fast replay fallback that reads only bridge tick source files whose
    /// filename range overlaps the requested interval. It avoids waiting for a
    /// quarter-wide canonical conversion when the replay-ready archive has not
    /// reached the selected candle yet.
    /// </summary>
    public CanonicalTickReadResult ReadBridgeTicksForReplay(
        string sourceFolder,
        string targetRoot,
        string symbol,
        long startMilliseconds,
        long endMillisecondsExclusive,
        int maximumRecords,
        int serverUtcOffsetMinutes,
        CancellationToken cancellationToken,
        bool takeLatest = false,
        bool allowFullIndexRebuild = true)
    {
        if (!Directory.Exists(sourceFolder) ||
            string.IsNullOrWhiteSpace(symbol) ||
            maximumRecords <= 0 ||
            endMillisecondsExclusive <= startMilliseconds)
        {
            return CanonicalTickReadResult.Empty;
        }

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        long startUnix = startMilliseconds / 1000;
        long endUnix = Math.Max(startUnix, (endMillisecondsExclusive - 1) / 1000);
        var candidates = new List<string>();

        candidates.AddRange(ResolveReplayHistoricalSources(
            sourceFolder,
            targetRoot,
            safeSymbol,
            startMilliseconds,
            endMillisecondsExclusive,
            allowFullIndexRebuild));

        string recentPath = Directory
            .EnumerateFiles(
                sourceFolder,
                $"ticks_recent_{safeSymbol}.csv",
                SearchOption.AllDirectories)
            .FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(recentPath))
            candidates.Add(recentPath);

        foreach (string path in Directory.EnumerateFiles(
                     sourceFolder,
                     $"ticks_live_{safeSymbol}_*.csv",
                     SearchOption.AllDirectories))
        {
            if (LiveSourceOverlapsRequest(
                    path,
                    startUnix,
                    endUnix,
                    onlySegmentKey: null,
                    serverUtcOffsetMinutes: serverUtcOffsetMinutes))
            {
                candidates.Add(path);
            }
        }

        var merged = new SortedDictionary<long, Dictionary<TickValueKey, int>>();
        foreach (string path in candidates
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(item => Path.GetFileName(item), StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                string sourceName = Path.GetFileName(path);
                bool authoritative =
                    sourceName.StartsWith("ticks_history_", StringComparison.OrdinalIgnoreCase) ||
                    sourceName.StartsWith("ticks_recent_", StringComparison.OrdinalIgnoreCase);
                var local = new SortedDictionary<long, Dictionary<TickValueKey, int>>();

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 128 * 1024);
                _ = reader.ReadLine(); // header

                while (!reader.EndOfStream)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line) ||
                        !TryParseTick(line, symbol, out CanonicalTick tick) ||
                        tick.TimeMilliseconds < startMilliseconds ||
                        tick.TimeMilliseconds >= endMillisecondsExclusive)
                    {
                        continue;
                    }

                    if (!local.TryGetValue(
                            tick.TimeMilliseconds,
                            out Dictionary<TickValueKey, int>? group))
                    {
                        group = new Dictionary<TickValueKey, int>();
                        local.Add(tick.TimeMilliseconds, group);
                    }

                    var key = new TickValueKey(
                        tick.Bid,
                        tick.Ask,
                        tick.Last,
                        tick.Volume,
                        tick.Flags,
                        tick.VolumeReal);
                    group.TryGetValue(key, out int count);
                    group[key] = count + 1;
                }

                foreach ((long timestamp, Dictionary<TickValueKey, int> localGroup) in local)
                {
                    if (authoritative || !merged.TryGetValue(
                            timestamp,
                            out Dictionary<TickValueKey, int>? mergedGroup))
                    {
                        merged[timestamp] = new Dictionary<TickValueKey, int>(localGroup);
                        continue;
                    }

                    foreach ((TickValueKey key, int count) in localGroup)
                    {
                        mergedGroup.TryGetValue(key, out int existing);
                        if (count > existing)
                            mergedGroup[key] = count;
                    }
                }
            }
            catch (IOException)
            {
                // The bridge rotates/replaces recent/live files atomically.
                // One transient source must never abort a historical replay.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue with the other overlapping sources and canonical fallback.
            }
        }

        var result = new List<MarketTick>(Math.Min(maximumRecords, 250_000));
        bool hasMore = false;
        long nextStart = startMilliseconds;

        IEnumerable<KeyValuePair<long, Dictionary<TickValueKey, int>>> timestampGroups =
            takeLatest ? merged.Reverse() : merged;

        foreach ((long timestamp, Dictionary<TickValueKey, int> group) in timestampGroups)
        {
            IEnumerable<KeyValuePair<TickValueKey, int>> valueGroups =
                takeLatest
                    ? group.OrderByDescending(item => item.Key, TickValueKeyComparer.Instance)
                    : group.OrderBy(item => item.Key, TickValueKeyComparer.Instance);

            foreach ((TickValueKey key, int multiplicity) in valueGroups)
            {
                if (result.Count > 0 && result.Count + multiplicity > maximumRecords)
                {
                    hasMore = true;
                    nextStart = timestamp;
                    break;
                }

                for (int copy = 0; copy < multiplicity && result.Count < maximumRecords; copy++)
                {
                    result.Add(new MarketTick(
                        timestamp,
                        timestamp / 1000,
                        key.Bid,
                        key.Ask,
                        key.Last,
                        key.Volume,
                        unchecked((uint)Math.Max(0, key.Flags)),
                        key.VolumeReal));
                }
                nextStart = takeLatest ? timestamp : timestamp + 1;

                if (result.Count >= maximumRecords)
                {
                    hasMore = true;
                    break;
                }
            }

            if (hasMore)
                break;
        }

        if (takeLatest)
            result.Reverse();
        if (result.Count == 0)
            nextStart = startMilliseconds;
        else if (takeLatest)
            nextStart = result[0].TimeMilliseconds;
        return new CanonicalTickReadResult(result, hasMore, nextStart);
    }

    private IReadOnlyList<string> ResolveReplayHistoricalSources(
        string sourceFolder,
        string targetRoot,
        string safeSymbol,
        long startMilliseconds,
        long endMillisecondsExclusive,
        bool allowFullIndexRebuild = true)
    {
        Directory.CreateDirectory(targetRoot);
        string indexPath = Path.Combine(targetRoot, "replay_source_index_v3.json");
        string fullSourceFolder = Path.GetFullPath(sourceFolder);

        static bool IsUnderFolder(string path, string folder)
        {
            string fullPath = Path.GetFullPath(path);
            string normalizedFolder = folder.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
        }

        ReplaySourceIndex? cached;
        lock (_replayIndexSync)
        {
            if (!_replayIndexMemory.TryGetValue(indexPath, out cached))
            {
                cached = ReadJson<ReplaySourceIndex>(indexPath);
                if (cached is not null)
                    _replayIndexMemory[indexPath] = cached;
            }
        }

        // Migrate the v147 complete-only v2 catalog when it belongs to this
        // source root. This avoids one expensive first-run rescan after upgrade.
        if (cached is null)
        {
            string legacyPath = Path.Combine(targetRoot, "replay_source_index_v2.json");
            ReplaySourceIndex? legacy;
            lock (_replayIndexSync)
                legacy = ReadJson<ReplaySourceIndex>(legacyPath);
            if (legacy is not null && legacy.Version == 2)
            {
                ReplaySourceIndexEntry[] migrated = (legacy.Sources ?? Array.Empty<ReplaySourceIndexEntry>())
                    .Where(entry =>
                        string.Equals(entry.Symbol, safeSymbol, StringComparison.OrdinalIgnoreCase) &&
                        IsUnderFolder(entry.Path, fullSourceFolder))
                    .Select(entry => entry with
                    {
                        SourceRoot = fullSourceFolder,
                        CatalogUtcTicks = legacy.UpdatedUtc.Ticks
                    })
                    .ToArray();
                if (migrated.Length > 0)
                {
                    cached = new ReplaySourceIndex(3, legacy.UpdatedUtc, migrated);
                    try
                    {
                        lock (_replayIndexSync)
                        {
                            WriteJsonAtomic(indexPath, cached);
                            _replayIndexMemory[indexPath] = cached;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        bool IsRootEntry(ReplaySourceIndexEntry entry) =>
            string.Equals(entry.Symbol, safeSymbol, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(entry.SourceRoot, fullSourceFolder, StringComparison.OrdinalIgnoreCase) ||
             (string.IsNullOrWhiteSpace(entry.SourceRoot) && IsUnderFolder(entry.Path, fullSourceFolder)));

        bool HasFreshRootCatalog(ReplaySourceIndex sourceIndex)
        {
            ReplaySourceIndexEntry[] rootEntries =
                (sourceIndex.Sources ?? Array.Empty<ReplaySourceIndexEntry>())
                    .Where(IsRootEntry)
                    .ToArray();
            if (rootEntries.Length == 0)
                return false;

            long catalogTicks = rootEntries.Max(entry =>
                entry.CatalogUtcTicks > 0 ? entry.CatalogUtcTicks : sourceIndex.UpdatedUtc.Ticks);
            long directoryTicks;
            try
            {
                directoryTicks = Directory.GetLastWriteTimeUtc(fullSourceFolder).Ticks;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            return catalogTicks >= directoryTicks;
        }

        string[] RangeMatches(ReplaySourceIndex sourceIndex) =>
            (sourceIndex.Sources ?? Array.Empty<ReplaySourceIndexEntry>())
                .Where(entry =>
                    string.Equals(entry.Symbol, safeSymbol, StringComparison.OrdinalIgnoreCase) &&
                    entry.EndMilliseconds >= startMilliseconds &&
                    entry.StartMilliseconds < endMillisecondsExclusive &&
                    IsRootEntry(entry))
                .OrderBy(entry => entry.StartMilliseconds)
                .ThenBy(entry => entry.EndMilliseconds)
                .Select(entry => entry.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        // Reuse any known range immediately, even if MT5 has changed another
        // file in the connector folder since the catalog was built. Rebuilding
        // thousands of historical filenames synchronously on every directory
        // timestamp change was the main seconds/Tick contention loop. If a
        // complete catalog has no match and is still fresh, the range is a real
        // market gap. If it is stale, fall back to a tiny target-local probe; do
        // not turn normal chart scrolling into a global rescan.
        if (cached is not null && cached.Version == 3)
        {
            string[] cachedMatches = RangeMatches(cached);
            if (cachedMatches.Length > 0)
                return cachedMatches;

            bool hasRootCatalog = (cached.Sources ?? Array.Empty<ReplaySourceIndexEntry>())
                .Any(IsRootEntry);
            if (hasRootCatalog)
            {
                if (HasFreshRootCatalog(cached))
                    return Array.Empty<string>();
                allowFullIndexRebuild = false;
            }
        }

        if (!allowFullIndexRebuild)
        {
            const long TimestampPrefixBucketMilliseconds = 10_000_000L;
            long firstBucket = Math.Max(
                0L,
                Math.Max(0L, startMilliseconds - TimestampPrefixBucketMilliseconds) /
                TimestampPrefixBucketMilliseconds);
            long lastInclusive = endMillisecondsExclusive <= 0
                ? long.MaxValue
                : Math.Max(startMilliseconds, endMillisecondsExclusive - 1L);
            long lastBucket = lastInclusive == long.MaxValue
                ? firstBucket
                : (lastInclusive + TimestampPrefixBucketMilliseconds) /
                  TimestampPrefixBucketMilliseconds;

            var targeted = new List<ReplaySourceIndexEntry>();
            try
            {
                for (long bucket = firstBucket; bucket <= lastBucket; bucket++)
                {
                    string prefix = bucket.ToString(CultureInfo.InvariantCulture);
                    foreach (string path in Directory.EnumerateFiles(
                                 sourceFolder,
                                 $"ticks_history_{safeSymbol}_{prefix}*.csv",
                                 SearchOption.TopDirectoryOnly))
                    {
                        if (!TryGetHistoricalSourceRangeMilliseconds(
                                path, out long fileStart, out long fileEnd) ||
                            fileEnd < startMilliseconds ||
                            fileStart >= endMillisecondsExclusive)
                        {
                            continue;
                        }

                        var info = new FileInfo(path);
                        targeted.Add(new ReplaySourceIndexEntry(
                            safeSymbol,
                            Path.GetFullPath(path),
                            fileStart,
                            fileEnd,
                            info.Exists ? info.Length : 0,
                            info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                            fullSourceFolder));
                    }
                }
            }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }

            return targeted
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.StartMilliseconds)
                .ThenBy(entry => entry.EndMilliseconds)
                .Select(entry => entry.Path)
                .ToArray();
        }

        // Full discovery is performed only when this source root has no usable
        // complete catalog yet. Include legacy/custom nested bridge layouts too;
        // v3 then keeps this one recursive discovery out of every chart page.
        var discovered = new List<ReplaySourceIndexEntry>();
        long catalogUtcTicks = DateTime.UtcNow.Ticks;
        string[] discoveredPaths;
        try
        {
            discoveredPaths = Directory.EnumerateFiles(
                    sourceFolder,
                    $"ticks_history_{safeSymbol}_*.csv",
                    SearchOption.AllDirectories)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        foreach (string path in discoveredPaths)
        {
            if (!TryGetHistoricalSourceRangeMilliseconds(
                    path, out long fileStart, out long fileEnd))
                continue;

            var info = new FileInfo(path);
            discovered.Add(new ReplaySourceIndexEntry(
                safeSymbol,
                Path.GetFullPath(path),
                fileStart,
                fileEnd,
                info.Exists ? info.Length : 0,
                info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                fullSourceFolder,
                catalogUtcTicks));
        }

        ReplaySourceIndexEntry[] preserved = cached is not null && cached.Version == 3
            ? (cached.Sources ?? Array.Empty<ReplaySourceIndexEntry>())
                .Where(entry =>
                    !(string.Equals(entry.Symbol, safeSymbol, StringComparison.OrdinalIgnoreCase) &&
                      (string.Equals(entry.SourceRoot, fullSourceFolder, StringComparison.OrdinalIgnoreCase) ||
                       (string.IsNullOrWhiteSpace(entry.SourceRoot) && IsUnderFolder(entry.Path, fullSourceFolder)))))
                .ToArray()
            : Array.Empty<ReplaySourceIndexEntry>();

        var rebuilt = new ReplaySourceIndex(
            3,
            DateTime.UtcNow,
            preserved.Concat(discovered)
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(entry => entry.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.StartMilliseconds)
                .ToArray());
        try
        {
            lock (_replayIndexSync)
            {
                WriteJsonAtomic(indexPath, rebuilt);
                _replayIndexMemory[indexPath] = rebuilt;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return RangeMatches(rebuilt);
    }

    public CanonicalTickCoverage GetBridgeHistoricalSourceCoverage(
        string sourceFolder,
        string targetRoot,
        string symbol,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceFolder) || string.IsNullOrWhiteSpace(symbol))
            return CanonicalTickCoverage.Empty;

        string fullSourceFolder = Path.GetFullPath(sourceFolder);
        string cacheKey = $"{fullSourceFolder}|{Mt5Paths.SanitizeFilePart(symbol)}";
        long directoryWriteTicks;
        try
        {
            directoryWriteTicks = Directory.GetLastWriteTimeUtc(fullSourceFolder).Ticks;
        }
        catch (IOException)
        {
            directoryWriteTicks = 0;
        }
        catch (UnauthorizedAccessException)
        {
            directoryWriteTicks = 0;
        }

        lock (_coverageSync)
        {
            if (_bridgeCoverageCache.TryGetValue(cacheKey, out BridgeCoverageCacheEntry? cached) &&
                (directoryWriteTicks == 0 || cached.DirectoryWriteUtcTicks == directoryWriteTicks))
            {
                return cached.Coverage;
            }
        }

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        IReadOnlyList<string> paths = ResolveReplayHistoricalSources(
            sourceFolder,
            targetRoot,
            safeSymbol,
            0L,
            long.MaxValue,
            allowFullIndexRebuild: true);

        long earliest = long.MaxValue;
        long latest = long.MinValue;
        foreach (string path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
                return CanonicalTickCoverage.Empty;
            if (!TryGetHistoricalSourceRangeMilliseconds(
                    path, out long startMilliseconds, out long endMilliseconds))
                continue;
            earliest = Math.Min(earliest, startMilliseconds);
            latest = Math.Max(latest, endMilliseconds);
        }

        CanonicalTickCoverage result = earliest == long.MaxValue || latest == long.MinValue
            ? CanonicalTickCoverage.Empty
            : new CanonicalTickCoverage(true, earliest, latest);
        lock (_coverageSync)
            _bridgeCoverageCache[cacheKey] = new BridgeCoverageCacheEntry(directoryWriteTicks, result);
        return result;
    }

    public CanonicalHistoricalSourceWindow GetHistoricalSourceWindowBefore(
        string sourceFolder,
        string targetRoot,
        string symbol,
        long beforeUnix,
        int timeframeSeconds,
        int targetCandles,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceFolder) ||
            string.IsNullOrWhiteSpace(symbol) ||
            beforeUnix <= 0 ||
            targetCandles <= 0)
        {
            return CanonicalHistoricalSourceWindow.Empty;
        }

        string safeSymbol = Mt5Paths.SanitizeFilePart(symbol);
        long beforeMilliseconds;
        long requiredActiveMilliseconds;
        try
        {
            beforeMilliseconds = checked(beforeUnix * 1000L);
            // Read enough complete historical source files to cover about 1.5
            // chart pages in active market time. Summing source-file durations
            // instead of wall-clock distance crosses weekends/market closures
            // without indexing days of unrelated history.
            requiredActiveMilliseconds = checked(
                Math.Max(1L, timeframeSeconds) *
                Math.Max(1L, targetCandles) *
                1500L);
        }
        catch (OverflowException)
        {
            return CanonicalHistoricalSourceWindow.Empty;
        }

        var candidates = new List<(long StartMilliseconds, long EndMilliseconds)>();
        IReadOnlyList<string> indexedPaths = ResolveReplayHistoricalSources(
            sourceFolder,
            targetRoot,
            safeSymbol,
            0L,
            beforeMilliseconds,
            allowFullIndexRebuild: true);
        foreach (string path in indexedPaths)
        {
            if (cancellationToken.IsCancellationRequested)
                return CanonicalHistoricalSourceWindow.Empty;
            if (!TryGetHistoricalSourceRangeMilliseconds(
                    path, out long startMilliseconds, out long endMilliseconds) ||
                startMilliseconds >= beforeMilliseconds)
            {
                continue;
            }
            candidates.Add((startMilliseconds, endMilliseconds));
        }

        if (candidates.Count == 0)
            return CanonicalHistoricalSourceWindow.Empty;

        candidates.Sort((left, right) =>
            right.EndMilliseconds.CompareTo(left.EndMilliseconds));

        long selectedStart = long.MaxValue;
        long selectedEnd = long.MinValue;
        long activeMilliseconds = 0;
        int selectedFiles = 0;

        foreach ((long startMilliseconds, long endMilliseconds) in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                return CanonicalHistoricalSourceWindow.Empty;

            // A source that straddles the requested candle boundary is still
            // imported as a complete authoritative 30-minute snapshot. This
            // avoids marking a partially imported file as fully synchronized.
            long usableEnd = Math.Min(endMilliseconds, beforeMilliseconds - 1);
            if (usableEnd < startMilliseconds)
                continue;

            selectedStart = Math.Min(selectedStart, startMilliseconds);
            selectedEnd = Math.Max(selectedEnd, endMilliseconds);
            activeMilliseconds = checked(activeMilliseconds +
                Math.Max(1L, usableEnd - startMilliseconds + 1L));
            selectedFiles++;

            if (activeMilliseconds >= requiredActiveMilliseconds)
                break;
        }

        if (selectedFiles == 0 || selectedStart == long.MaxValue || selectedEnd == long.MinValue)
            return CanonicalHistoricalSourceWindow.Empty;

        long minimumStartUnix = selectedStart / 1000L;
        long maximumEndUnix = (selectedEnd + 999L) / 1000L;
        return new CanonicalHistoricalSourceWindow(
            true,
            minimumStartUnix,
            maximumEndUnix,
            selectedFiles);
    }

    public CanonicalTickCoverage GetCoverage(
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(targetRoot))
            return CanonicalTickCoverage.Empty;

        string coverageKey = Path.GetFullPath(targetRoot);
        lock (_coverageSync)
        {
            if (_canonicalCoverageCache.TryGetValue(coverageKey, out CanonicalTickCoverage? cachedCoverage))
                return cachedCoverage;
        }

        long earliest = long.MaxValue;
        long latest = long.MinValue;
        lock (_sync)
        {
            foreach (string path in GetCanonicalSegmentFiles(targetRoot))
            {
                if (cancellationToken.IsCancellationRequested)
                    return CanonicalTickCoverage.Empty;
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                long total = stream.Length / RecordSize;
                if (total <= 0)
                    continue;

                using var reader = new BinaryReader(stream);
                stream.Seek(0, SeekOrigin.Begin);
                CanonicalTick first = ReadRecord(reader);
                stream.Seek((total - 1) * RecordSize, SeekOrigin.Begin);
                CanonicalTick last = ReadRecord(reader);
                earliest = Math.Min(earliest, first.TimeMilliseconds);
                latest = Math.Max(latest, last.TimeMilliseconds);
            }
        }

        CanonicalTickCoverage result = earliest == long.MaxValue || latest == long.MinValue
            ? CanonicalTickCoverage.Empty
            : new CanonicalTickCoverage(true, earliest, latest);
        lock (_coverageSync)
            _canonicalCoverageCache[coverageKey] = result;
        return result;
    }


    public bool HasCanonicalData(string targetRoot) =>
        Directory.Exists(targetRoot) && GetCanonicalSegmentFiles(targetRoot).Length > 0;

    private string[] GetCanonicalSegmentFiles(string targetRoot)
    {
        if (!Directory.Exists(targetRoot))
            return Array.Empty<string>();

        string key = Path.GetFullPath(targetRoot);
        lock (_coverageSync)
        {
            if (_canonicalSegmentFilesCache.TryGetValue(key, out string[]? cached))
                return cached;
        }

        string[] files = Directory
            .EnumerateFiles(targetRoot, "ticks.tlt", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetDirectoryName(path), StringComparer.Ordinal)
            .ToArray();
        lock (_coverageSync)
            _canonicalSegmentFilesCache[key] = files;
        return files;
    }

    private static bool TryGetHistoricalSourceRangeMilliseconds(
        string sourcePath,
        out long startMilliseconds,
        out long endMilliseconds)
    {
        startMilliseconds = 0;
        endMilliseconds = 0;
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        int endSeparator = stem.LastIndexOf('_');
        int startSeparator = endSeparator > 0
            ? stem.LastIndexOf('_', endSeparator - 1)
            : -1;
        return startSeparator >= 0 &&
               endSeparator > startSeparator + 1 &&
               long.TryParse(
                   stem[(startSeparator + 1)..endSeparator],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out startMilliseconds) &&
               long.TryParse(
                   stem[(endSeparator + 1)..],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out endMilliseconds);
    }

    private static bool HistoricalSourceOverlapsRequest(
        string sourcePath,
        long? minimumStartUnix,
        long? maximumEndUnix,
        string? onlySegmentKey,
        int serverUtcOffsetMinutes)
    {
        if (!TryGetHistoricalSourceRangeMilliseconds(
                sourcePath,
                out long startMsc,
                out long endMsc))
        {
            // Unknown legacy names remain eligible for the canonical archive so
            // data is never hidden. The fast replay fallback deliberately skips
            // unknown names because it must never scan unrelated large files.
            return true;
        }

        long startUnix = startMsc / 1000;
        long endUnix = endMsc / 1000;
        if (minimumStartUnix.HasValue && endUnix < minimumStartUnix.Value)
            return false;
        if (maximumEndUnix.HasValue && startUnix > maximumEndUnix.Value)
            return false;

        if (!string.IsNullOrWhiteSpace(onlySegmentKey))
        {
            string startSegment = PersistentHistoryStore.GetSegmentKey(
                startUnix,
                serverUtcOffsetMinutes);
            string endSegment = PersistentHistoryStore.GetSegmentKey(
                endUnix,
                serverUtcOffsetMinutes);
            if (!string.Equals(startSegment, onlySegmentKey, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(endSegment, onlySegmentKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LiveSourceOverlapsRequest(
        string sourcePath,
        long? minimumStartUnix,
        long? maximumEndUnix,
        string? onlySegmentKey,
        int serverUtcOffsetMinutes)
    {
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        int separator = stem.LastIndexOf('_');
        string dateKey = separator >= 0 && separator + 1 < stem.Length
            ? stem[(separator + 1)..]
            : string.Empty;
        if (!DateTime.TryParseExact(
                dateKey,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime fileDate))
        {
            return true;
        }

        DateTimeOffset dayStart = new(
            DateTime.SpecifyKind(fileDate, DateTimeKind.Unspecified),
            TimeSpan.Zero);
        long startUnix = dayStart.ToUnixTimeSeconds();
        long endUnix = dayStart.AddDays(1).ToUnixTimeSeconds() - 1;
        if (minimumStartUnix.HasValue && endUnix < minimumStartUnix.Value)
            return false;
        if (maximumEndUnix.HasValue && startUnix > maximumEndUnix.Value)
            return false;

        if (!string.IsNullOrWhiteSpace(onlySegmentKey))
        {
            string segment = PersistentHistoryStore.GetSegmentKey(
                startUnix,
                serverUtcOffsetMinutes);
            if (!string.Equals(segment, onlySegmentKey, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private TickSourceImportResult ImportSourceFile(
        string sourcePath,
        string targetRoot,
        string symbol,
        long startOffset,
        TickMergeMode mergeMode,
        int serverUtcOffsetMinutes,
        long? minimumStartUnix,
        long? maximumEndUnix,
        string? onlySegmentKey,
        CancellationToken cancellationToken)
    {
        string temporaryRoot = Path.Combine(targetRoot, ".import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var writers = new Dictionary<string, IncomingSegmentWriter>(StringComparer.OrdinalIgnoreCase);
        long parsedRows = 0;
        long canonicalTicks = 0;

        try
        {
            using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (startOffset > 0 && startOffset < stream.Length)
                stream.Seek(startOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 128 * 1024, leaveOpen: true);

            if (startOffset == 0)
                _ = reader.ReadLine(); // header

            long currentTimestamp = long.MinValue;
            var group = new Dictionary<TickValueKey, int>();

            void FlushGroup()
            {
                if (currentTimestamp == long.MinValue || group.Count == 0)
                    return;

                long unix = currentTimestamp / 1000;
                string segment = PersistentHistoryStore.GetSegmentKey(
                    unix,
                    serverUtcOffsetMinutes);
                if ((!minimumStartUnix.HasValue || unix >= minimumStartUnix.Value) &&
                    (!maximumEndUnix.HasValue || unix <= maximumEndUnix.Value) &&
                    (string.IsNullOrWhiteSpace(onlySegmentKey) ||
                     string.Equals(segment, onlySegmentKey, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!writers.TryGetValue(segment, out IncomingSegmentWriter? writer))
                    {
                        writer = new IncomingSegmentWriter(
                            Path.Combine(temporaryRoot, segment + ".tlt"));
                        writers.Add(segment, writer);
                    }

                    foreach ((TickValueKey key, int multiplicity) in group
                                 .OrderBy(item => item.Key, TickValueKeyComparer.Instance))
                    {
                        writer.Write(new CanonicalTick(
                            currentTimestamp,
                            key.Bid,
                            key.Ask,
                            key.Last,
                            key.Volume,
                            key.Flags,
                            key.VolumeReal,
                            multiplicity));
                        canonicalTicks += multiplicity;
                    }
                }

                group.Clear();
            }

            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Nothing has been merged yet, so discarding this temporary
                    // file is safe and the source can be retried later.
                    return new TickSourceImportResult(
                        0,
                        0,
                        Array.Empty<string>(),
                        Completed: false);
                }

                string? line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line) ||
                    !TryParseTick(line, symbol, out CanonicalTick parsed))
                {
                    continue;
                }

                parsedRows++;
                if ((parsedRows & 0x3FFF) == 0)
                    Thread.Yield();

                if (parsed.TimeMilliseconds < currentTimestamp)
                    continue;
                if (parsed.TimeMilliseconds != currentTimestamp)
                {
                    FlushGroup();
                    currentTimestamp = parsed.TimeMilliseconds;
                }

                var key = new TickValueKey(
                    parsed.Bid,
                    parsed.Ask,
                    parsed.Last,
                    parsed.Volume,
                    parsed.Flags,
                    parsed.VolumeReal);
                group.TryGetValue(key, out int count);
                group[key] = count + 1;
            }

            FlushGroup();
            foreach (IncomingSegmentWriter writer in writers.Values)
                writer.Dispose();
            writers.Clear();

            if (cancellationToken.IsCancellationRequested)
            {
                return new TickSourceImportResult(
                    0,
                    0,
                    Array.Empty<string>(),
                    Completed: false);
            }

            var preparedCommits = new List<PreparedSegmentCommit>();
            foreach (string incomingPath in Directory.EnumerateFiles(temporaryRoot, "*.tlt"))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new TickSourceImportResult(
                        0,
                        0,
                        Array.Empty<string>(),
                        Completed: false);
                }

                string segment = Path.GetFileNameWithoutExtension(incomingPath);
                string segmentFolder = Path.Combine(targetRoot, segment);
                string targetPath = Path.Combine(segmentFolder, "ticks.tlt");
                string preparedPath = Path.Combine(
                    temporaryRoot,
                    segment + ".ready.tlt");

                // Build the potentially expensive quarter merge and metadata
                // outside the shared canonical lock. Existing readers continue
                // using the old immutable file until the ready file is complete.
                if (!PrepareMergedSegment(
                        targetPath,
                        incomingPath,
                        preparedPath,
                        mergeMode,
                        cancellationToken))
                {
                    return new TickSourceImportResult(
                        0,
                        0,
                        Array.Empty<string>(),
                        Completed: false);
                }
                TickSegmentMetadata metadata = ReadSegmentMetadata(preparedPath);
                preparedCommits.Add(new PreparedSegmentCommit(
                    segment,
                    segmentFolder,
                    targetPath,
                    preparedPath,
                    metadata));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new TickSourceImportResult(
                    0,
                    0,
                    Array.Empty<string>(),
                    Completed: false);
            }

            // Commit is intentionally tiny: directory creation + atomic replace
            // + a small metadata JSON write. Foreground history readers can only
            // be delayed by this short transactional section, never by CSV parse
            // or quarter-file merge preparation.
            lock (_sync)
            {
                foreach (PreparedSegmentCommit commit in preparedCommits)
                {
                    Directory.CreateDirectory(commit.SegmentFolder);
                    File.Move(commit.PreparedPath, commit.TargetPath, true);
                    WriteJsonAtomic(
                        Path.Combine(commit.SegmentFolder, "tick_segment.json"),
                        commit.Metadata);
                }
            }

            return new TickSourceImportResult(
                parsedRows,
                canonicalTicks,
                preparedCommits
                    .Select(item => item.Segment)
                    .ToArray(),
                Completed: true);
        }
        finally
        {
            foreach (IncomingSegmentWriter writer in writers.Values)
                writer.Dispose();
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch
            {
            }
        }
    }

    private static bool PrepareMergedSegment(
        string targetPath,
        string incomingPath,
        string preparedPath,
        TickMergeMode mode,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(targetPath))
        {
            File.Copy(incomingPath, preparedPath, true);
            return !cancellationToken.IsCancellationRequested;
        }

        using (var oldReader = new TickGroupReader(targetPath))
        using (var newReader = new TickGroupReader(incomingPath))
        using (var output = new FileStream(
                   preparedPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        using (var writer = new BinaryWriter(output))
        {
            IReadOnlyList<CanonicalTick>? oldGroup = oldReader.ReadGroup();
            IReadOnlyList<CanonicalTick>? newGroup = newReader.ReadGroup();
            int groupsProcessed = 0;

            while (oldGroup is not null || newGroup is not null)
            {
                if ((groupsProcessed++ & 0x3FFF) == 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return false;
                    Thread.Yield();
                }

                if (newGroup is null ||
                    (oldGroup is not null &&
                     oldGroup[0].TimeMilliseconds < newGroup[0].TimeMilliseconds))
                {
                    WriteGroup(writer, oldGroup!);
                    oldGroup = oldReader.ReadGroup();
                    continue;
                }

                if (oldGroup is null ||
                    newGroup[0].TimeMilliseconds < oldGroup[0].TimeMilliseconds)
                {
                    WriteGroup(writer, newGroup);
                    newGroup = newReader.ReadGroup();
                    continue;
                }

                if (mode == TickMergeMode.ReplaceTimestampGroup)
                {
                    // Native history and recent repair snapshots are
                    // authoritative for every timestamp they contain.
                    WriteGroup(writer, newGroup);
                }
                else
                {
                    var combined = oldGroup.ToDictionary(
                        item => item.ValueKey,
                        item => item,
                        TickValueKeyEqualityComparer.Instance);
                    foreach (CanonicalTick incoming in newGroup)
                    {
                        if (combined.TryGetValue(
                                incoming.ValueKey,
                                out CanonicalTick existing))
                        {
                            combined[incoming.ValueKey] = existing with
                            {
                                Multiplicity = Math.Max(
                                    existing.Multiplicity,
                                    incoming.Multiplicity)
                            };
                        }
                        else
                        {
                            combined[incoming.ValueKey] = incoming;
                        }
                    }

                    WriteGroup(
                        writer,
                        combined.Values.OrderBy(
                            item => item.ValueKey,
                            TickValueKeyComparer.Instance));
                }

                oldGroup = oldReader.ReadGroup();
                newGroup = newReader.ReadGroup();
            }

            writer.Flush();
            output.Flush(true);
        }

        return !cancellationToken.IsCancellationRequested;
    }

    private static void WriteGroup(
        BinaryWriter writer,
        IEnumerable<CanonicalTick> group)
    {
        foreach (CanonicalTick tick in group)
            WriteRecord(writer, tick);
    }

    private static TickSegmentMetadata ReadSegmentMetadata(string dataPath)
    {
        using var stream = new FileStream(
            dataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long records = stream.Length / RecordSize;
        long ticks = 0;
        long earliest = 0;
        long latest = 0;
        using var reader = new BinaryReader(stream);
        for (long index = 0; index < records; index++)
        {
            CanonicalTick tick = ReadRecord(reader);
            ticks += tick.Multiplicity;
            earliest = index == 0 ? tick.TimeMilliseconds : earliest;
            latest = tick.TimeMilliseconds;
        }

        return new TickSegmentMetadata(
            records,
            ticks,
            earliest,
            latest,
            DateTime.UtcNow);
    }

    private long? FindLatestTickUnix(
        string targetRoot,
        long? beforeUnix,
        IReadOnlySet<string>? excludedSegmentKeys)
    {
        string[] files = GetCanonicalSegmentFiles(targetRoot)
            .Reverse()
            .ToArray();

        foreach (string path in files)
        {
            string? segmentKey = Path.GetFileName(Path.GetDirectoryName(path));
            if (!string.IsNullOrWhiteSpace(segmentKey) &&
                excludedSegmentKeys is not null &&
                excludedSegmentKeys.Contains(segmentKey))
            {
                continue;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long total = stream.Length / RecordSize;
            if (total == 0)
                continue;
            using var reader = new BinaryReader(stream);
            long index = beforeUnix.HasValue
                ? LowerBoundTime(reader, stream, total, checked(beforeUnix.Value * 1000L)) - 1
                : total - 1;
            if (index < 0)
                continue;
            stream.Seek(index * RecordSize, SeekOrigin.Begin);
            return reader.ReadInt64() / 1000;
        }

        return null;
    }

    private static long LowerBoundTime(
        BinaryReader reader,
        Stream stream,
        long totalRecords,
        long timeMilliseconds)
    {
        long low = 0;
        long high = totalRecords;
        while (low < high)
        {
            long middle = low + (high - low) / 2;
            stream.Seek(middle * RecordSize, SeekOrigin.Begin);
            long value = reader.ReadInt64();
            if (value < timeMilliseconds)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static long FindLastTimestampGroupStart(
        string path,
        string symbol)
    {
        const int InitialWindow = 1 << 20;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length == 0)
            return 0;

        long windowSize = Math.Min(stream.Length, InitialWindow);
        while (true)
        {
            long windowStart = Math.Max(0, stream.Length - windowSize);
            bool startsAtLineBoundary = windowStart == 0;
            if (windowStart > 0)
            {
                stream.Seek(windowStart - 1, SeekOrigin.Begin);
                startsAtLineBoundary = stream.ReadByte() == (byte)'\n';
            }
            stream.Seek(windowStart, SeekOrigin.Begin);
            byte[] buffer = new byte[checked((int)(stream.Length - windowStart))];
            int read = 0;
            while (read < buffer.Length)
            {
                int amount = stream.Read(buffer, read, buffer.Length - read);
                if (amount <= 0)
                    break;
                read += amount;
            }

            int first = 0;
            if (!startsAtLineBoundary)
            {
                while (first < read && buffer[first] != (byte)'\n')
                    first++;
                if (first < read)
                    first++;
            }

            var rows = new List<(long Offset, long Timestamp)>();
            int lineStart = first;
            for (int index = first; index <= read; index++)
            {
                if (index < read && buffer[index] != (byte)'\n')
                    continue;

                int length = index - lineStart;
                if (length > 0 && buffer[lineStart + length - 1] == (byte)'\r')
                    length--;
                if (length > 0)
                {
                    string line = Encoding.UTF8.GetString(buffer, lineStart, length);
                    if (TryParseTick(line, symbol, out CanonicalTick tick))
                        rows.Add((windowStart + lineStart, tick.TimeMilliseconds));
                }
                lineStart = index + 1;
            }

            if (rows.Count == 0)
                return 0;

            long finalTimestamp = rows[^1].Timestamp;
            int groupStart = rows.Count - 1;
            while (groupStart > 0 && rows[groupStart - 1].Timestamp == finalTimestamp)
                groupStart--;

            if (groupStart > 0 || windowStart == 0)
                return rows[groupStart].Offset;

            long expanded = Math.Min(stream.Length, checked(windowSize * 2));
            if (expanded == windowSize)
                return rows[0].Offset;
            windowSize = expanded;
        }
    }

    private static bool TryParseTick(
        string line,
        string symbol,
        out CanonicalTick tick)
    {
        tick = default;
        IReadOnlyList<string> fields = CsvLineParser.Parse(line);
        if (fields.Count < 11 ||
            !string.Equals(fields[2].Trim(), symbol, StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long timeMilliseconds) ||
            !double.TryParse(fields[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double bid) ||
            !double.TryParse(fields[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ask) ||
            !double.TryParse(fields[7].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double last) ||
            !long.TryParse(fields[8].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long volume) ||
            !long.TryParse(fields[9].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long flags) ||
            !double.TryParse(fields[10].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double volumeReal))
        {
            return false;
        }

        double price = bid > 0 ? bid : last > 0 ? last : ask;
        if (timeMilliseconds <= 0 || !double.IsFinite(price) || price <= 0)
            return false;

        tick = new CanonicalTick(
            timeMilliseconds,
            bid,
            ask,
            last,
            Math.Max(0, volume),
            flags,
            Math.Max(0, volumeReal),
            1);
        return true;
    }

    private static void WriteRecord(BinaryWriter writer, CanonicalTick tick)
    {
        writer.Write(tick.TimeMilliseconds);
        writer.Write(tick.Bid);
        writer.Write(tick.Ask);
        writer.Write(tick.Last);
        writer.Write(tick.Volume);
        writer.Write(tick.Flags);
        writer.Write(tick.VolumeReal);
        writer.Write(tick.Multiplicity);
    }

    private static CanonicalTick ReadRecord(BinaryReader reader) =>
        new(
            reader.ReadInt64(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadDouble(),
            reader.ReadInt32());

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

    private enum TickMergeMode
    {
        MaximumMultiplicity,
        ReplaceTimestampGroup
    }

    private sealed class IncomingSegmentWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;

        public IncomingSegmentWriter(string path)
        {
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            _writer = new BinaryWriter(_stream);
        }

        public void Write(CanonicalTick tick) => WriteRecord(_writer, tick);

        public void Dispose()
        {
            _writer.Flush();
            _stream.Flush(true);
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    private sealed class TickGroupReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private CanonicalTick? _pending;

        public TickGroupReader(string path)
        {
            _stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            _reader = new BinaryReader(_stream);
        }

        public IReadOnlyList<CanonicalTick>? ReadGroup()
        {
            CanonicalTick? first = _pending;
            _pending = null;
            if (first is null)
            {
                if (_stream.Position + RecordSize > _stream.Length)
                    return null;
                first = ReadRecord(_reader);
            }

            var result = new List<CanonicalTick> { first.Value };
            while (_stream.Position + RecordSize <= _stream.Length)
            {
                CanonicalTick next = ReadRecord(_reader);
                if (next.TimeMilliseconds != first.Value.TimeMilliseconds)
                {
                    _pending = next;
                    break;
                }
                result.Add(next);
            }
            return result;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }

    private sealed class TickCandleBuilder
    {
        private double _open;
        private double _high;
        private double _low;
        private double _close;
        private long _tickVolume;
        private int _spread;
        private double _realVolume;

        public TickCandleBuilder(
            long startUnix,
            long endUnix,
            double price,
            int spread,
            CanonicalTick tick)
        {
            StartUnix = startUnix;
            EndUnix = endUnix;
            _open = price;
            _high = price;
            _low = price;
            _close = price;
            _tickVolume = tick.Multiplicity;
            _spread = spread;
            _realVolume = tick.VolumeReal * tick.Multiplicity;
        }

        public long StartUnix { get; }
        public long EndUnix { get; }

        public void Add(double price, int spread, CanonicalTick tick)
        {
            _high = Math.Max(_high, price);
            _low = Math.Min(_low, price);
            _close = price;
            _tickVolume += tick.Multiplicity;
            _spread = spread;
            _realVolume += tick.VolumeReal * tick.Multiplicity;
        }

        public Candle ToCandle(
            string symbol,
            string timeframe,
            int digits,
            double point,
            long serverNowUnix) =>
            new(
                symbol,
                timeframe,
                digits,
                point,
                StartUnix,
                EndUnix,
                Mt5ServerClock.ToDisplayTime(StartUnix)
                    .ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
                _open,
                _high,
                _low,
                _close,
                _tickVolume,
                _spread,
                (long)Math.Round(_realVolume),
                EndUnix <= serverNowUnix);
    }

    private readonly record struct CanonicalTick(
        long TimeMilliseconds,
        double Bid,
        double Ask,
        double Last,
        long Volume,
        long Flags,
        double VolumeReal,
        int Multiplicity)
    {
        public TickValueKey ValueKey =>
            new(Bid, Ask, Last, Volume, Flags, VolumeReal);
    }

    private readonly record struct TickValueKey(
        double Bid,
        double Ask,
        double Last,
        long Volume,
        long Flags,
        double VolumeReal);

    private sealed class TickValueKeyComparer : IComparer<TickValueKey>
    {
        public static TickValueKeyComparer Instance { get; } = new();

        public int Compare(TickValueKey x, TickValueKey y)
        {
            int result = BitConverter.DoubleToInt64Bits(x.Bid).CompareTo(BitConverter.DoubleToInt64Bits(y.Bid));
            if (result != 0) return result;
            result = BitConverter.DoubleToInt64Bits(x.Ask).CompareTo(BitConverter.DoubleToInt64Bits(y.Ask));
            if (result != 0) return result;
            result = BitConverter.DoubleToInt64Bits(x.Last).CompareTo(BitConverter.DoubleToInt64Bits(y.Last));
            if (result != 0) return result;
            result = x.Volume.CompareTo(y.Volume);
            if (result != 0) return result;
            result = x.Flags.CompareTo(y.Flags);
            if (result != 0) return result;
            return BitConverter.DoubleToInt64Bits(x.VolumeReal)
                .CompareTo(BitConverter.DoubleToInt64Bits(y.VolumeReal));
        }
    }

    private sealed class TickValueKeyEqualityComparer : IEqualityComparer<TickValueKey>
    {
        public static TickValueKeyEqualityComparer Instance { get; } = new();
        public bool Equals(TickValueKey x, TickValueKey y) =>
            TickValueKeyComparer.Instance.Compare(x, y) == 0;
        public int GetHashCode(TickValueKey obj) => HashCode.Combine(
            BitConverter.DoubleToInt64Bits(obj.Bid),
            BitConverter.DoubleToInt64Bits(obj.Ask),
            BitConverter.DoubleToInt64Bits(obj.Last),
            obj.Volume,
            obj.Flags,
            BitConverter.DoubleToInt64Bits(obj.VolumeReal));
    }

    private sealed record TickSyncState(
        IReadOnlyDictionary<string, TickSourceState> Sources);

    private sealed record TickSourceState(
        long ProcessedBytes,
        long LastWriteUtcTicks,
        DateTime UpdatedUtc,
        long ResumeOffset = 0);

    private sealed record TickSegmentMetadata(
        long CanonicalRecords,
        long TickCount,
        long EarliestTimeMilliseconds,
        long LatestTimeMilliseconds,
        DateTime UpdatedUtc);

    private sealed record PreparedSegmentCommit(
        string Segment,
        string SegmentFolder,
        string TargetPath,
        string PreparedPath,
        TickSegmentMetadata Metadata);

    private sealed record TickSourceImportResult(
        long ParsedRows,
        long CanonicalTicks,
        IReadOnlyList<string> TouchedSegments,
        bool Completed);

    private sealed record BridgeCoverageCacheEntry(
        long DirectoryWriteUtcTicks,
        CanonicalTickCoverage Coverage);

    private sealed record ReplaySourceIndex(
        int Version,
        DateTime UpdatedUtc,
        IReadOnlyList<ReplaySourceIndexEntry> Sources);

    private sealed record ReplaySourceIndexEntry(
        string Symbol,
        string Path,
        long StartMilliseconds,
        long EndMilliseconds,
        long Length,
        long LastWriteUtcTicks,
        string SourceRoot = "",
        long CatalogUtcTicks = 0);
}

internal sealed record CanonicalHistoricalSourceWindow(
    bool HasData,
    long MinimumStartUnix,
    long MaximumEndUnix,
    int SourceFiles)
{
    public static CanonicalHistoricalSourceWindow Empty { get; } =
        new(false, 0, 0, 0);
}

public sealed record CanonicalTickCoverage(
    bool HasData,
    long EarliestTimeMilliseconds,
    long LatestTimeMilliseconds)
{
    public static CanonicalTickCoverage Empty { get; } = new(false, 0, 0);
}

public sealed record CanonicalTickReadResult(
    IReadOnlyList<MarketTick> Ticks,
    bool HasMore,
    long NextStartMilliseconds)
{
    public static CanonicalTickReadResult Empty { get; } =
        new(Array.Empty<MarketTick>(), false, 0);
}

internal sealed record CanonicalTickSyncResult(
    int ProcessedFiles,
    long ParsedRows,
    long CanonicalTicks,
    IReadOnlyList<string> TouchedSegments);
