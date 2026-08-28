using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TickLab.Core.Market;

namespace TickLab.Gateway.FileBridge;

public sealed class MarkerExchangeService
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string RootPath
    {
        get
        {
            string connections = Mt5Paths.GetConnectionsRoot();
            string tickLabRoot = Directory.GetParent(connections)?.FullName
                ?? Path.Combine(connections, "..");
            return Path.GetFullPath(Path.Combine(tickLabRoot, "Markers"));
        }
    }

    private string IncomingPath => Path.Combine(RootPath, "mt5_to_ticklab.pipe");
    private string OutgoingPath => Path.Combine(RootPath, "ticklab_to_mt5.pipe");
    private string CursorPath => Path.Combine(RootPath, "ticklab_in.cursor");
    private string StatePath => Path.Combine(RootPath, "ticklab_markers.json");

    public IReadOnlyList<CandleMarker> LoadMarkers()
    {
        lock (_sync)
        {
            EnsureRoot();
            if (!File.Exists(StatePath))
                return Array.Empty<CandleMarker>();
            try
            {
                List<CandleMarker>? loaded = JsonSerializer.Deserialize<List<CandleMarker>>(
                    File.ReadAllText(StatePath), _jsonOptions);
                return loaded ?? new List<CandleMarker>();
            }
            catch
            {
                return Array.Empty<CandleMarker>();
            }
        }
    }

    public IReadOnlyList<CandleMarkerTransfer> ReadPendingFromMt5()
    {
        lock (_sync)
        {
            EnsureRoot();
            if (!File.Exists(IncomingPath))
                return Array.Empty<CandleMarkerTransfer>();

            string[] lines = File.ReadAllLines(IncomingPath, Encoding.UTF8);
            int cursor = ReadCursor();
            cursor = Math.Clamp(cursor, 0, lines.Length);
            var result = new List<CandleMarkerTransfer>();

            for (int index = cursor; index < lines.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                    continue;
                if (!TryParse(lines[index], out CandleMarkerTransfer? item))
                {
                    throw new InvalidDataException(
                        $"Malformed MT5 marker event at line {index + 1:N0}. The receive cursor was not advanced.");
                }
                result.Add(item!);
            }

            File.WriteAllText(CursorPath, lines.Length.ToString(CultureInfo.InvariantCulture));
            return result;
        }
    }

    public void SendAddToMt5(CandleMarker marker) => Append(
        OutgoingPath,
        new CandleMarkerTransfer(
            marker.Id, "add", marker.Symbol, marker.Timeframe,
            marker.StartUnix, marker.Source, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), marker.Label));

    public void SendRemoveToMt5(CandleMarker marker) => Append(
        OutgoingPath,
        new CandleMarkerTransfer(
            marker.Id, "remove", marker.Symbol, marker.Timeframe,
            marker.StartUnix, marker.Source, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), marker.Label));

    public void SaveMarkers(IEnumerable<CandleMarker> markers)
    {
        lock (_sync)
        {
            EnsureRoot();
            string temporary = StatePath + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(markers.OrderBy(item => item.StartUnix).ToArray(), _jsonOptions),
                Encoding.UTF8);
            File.Move(temporary, StatePath, true);
        }
    }

    private void Append(string path, CandleMarkerTransfer item)
    {
        lock (_sync)
        {
            EnsureRoot();
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(Serialize(item));
            writer.Flush();
        }
    }

    private int ReadCursor()
    {
        if (!File.Exists(CursorPath))
            return 0;
        return int.TryParse(File.ReadAllText(CursorPath).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? Math.Max(0, value)
            : 0;
    }

    private void EnsureRoot() => Directory.CreateDirectory(RootPath);

    private static string Serialize(CandleMarkerTransfer item) => string.Join('|',
        Clean(item.Id), Clean(item.Action), Clean(item.Symbol), Clean(item.Timeframe),
        item.StartUnix.ToString(CultureInfo.InvariantCulture), Clean(item.Source),
        item.CreatedUnix.ToString(CultureInfo.InvariantCulture), Clean(item.Label));

    private static bool TryParse(string line, out CandleMarkerTransfer? item)
    {
        item = null;
        string[] fields = line.Split('|');
        if (fields.Length < 8 ||
            !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long startUnix) ||
            !long.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long createdUnix) ||
            string.IsNullOrWhiteSpace(fields[0]))
        {
            return false;
        }

        item = new CandleMarkerTransfer(
            fields[0], fields[1], fields[2], fields[3], startUnix,
            fields[5], createdUnix, string.Join('|', fields.Skip(7)));
        return true;
    }

    private static string Clean(string? value) =>
        (value ?? string.Empty).Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
