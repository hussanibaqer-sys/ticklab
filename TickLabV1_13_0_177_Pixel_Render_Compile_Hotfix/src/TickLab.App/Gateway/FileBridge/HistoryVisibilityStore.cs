using System.Text.Json;

namespace TickLab.Gateway.FileBridge;

internal sealed class HistoryVisibilityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _path;

    public HistoryVisibilityStore(string rootPath)
    {
        string settingsFolder = Path.Combine(rootPath, "_settings");
        Directory.CreateDirectory(settingsFolder);
        _path = Path.Combine(settingsFolder, "history_visibility.json");
    }

    public bool IsNativeVisible(string connectorId, string symbol, string timeframe) =>
        GetValue(CreateKey("CANDLE", connectorId, symbol, timeframe));

    public void SetNativeVisible(string connectorId, string symbol, string timeframe, bool visible) =>
        SetValue(CreateKey("CANDLE", connectorId, symbol, timeframe), visible);

    public bool IsTickVisible(string connectorId, string symbol, string segmentKey) =>
        GetValue(CreateKey("TICK", connectorId, symbol, segmentKey));

    public void SetTickVisible(string connectorId, string symbol, string segmentKey, bool visible) =>
        SetValue(CreateKey("TICK", connectorId, symbol, segmentKey), visible);

    public void RemoveNative(string connectorId, string symbol, string? timeframe = null)
    {
        string prefix = CreateKeyPrefix("CANDLE", connectorId, symbol);
        RemoveMatching(prefix, timeframe);
    }

    public void RemoveTick(string connectorId, string symbol, string? segmentKey = null)
    {
        string prefix = CreateKeyPrefix("TICK", connectorId, symbol);
        RemoveMatching(prefix, segmentKey);
    }

    private bool GetValue(string key)
    {
        lock (_sync)
        {
            VisibilityDocument document = Read();
            return !document.Items.TryGetValue(key, out bool visible) || visible;
        }
    }

    private void SetValue(string key, bool visible)
    {
        lock (_sync)
        {
            VisibilityDocument document = Read();
            var items = new Dictionary<string, bool>(document.Items, StringComparer.OrdinalIgnoreCase)
            {
                [key] = visible
            };
            Write(new VisibilityDocument(items));
        }
    }

    private void RemoveMatching(string prefix, string? item)
    {
        lock (_sync)
        {
            VisibilityDocument document = Read();
            var items = new Dictionary<string, bool>(document.Items, StringComparer.OrdinalIgnoreCase);
            string? exact = string.IsNullOrWhiteSpace(item)
                ? null
                : prefix + Normalize(item);
            foreach (string key in items.Keys.ToArray())
            {
                if (exact is not null
                        ? string.Equals(key, exact, StringComparison.OrdinalIgnoreCase)
                        : key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    items.Remove(key);
                }
            }
            Write(new VisibilityDocument(items));
        }
    }

    private VisibilityDocument Read()
    {
        if (!File.Exists(_path))
            return new VisibilityDocument(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        try
        {
            VisibilityDocument? document = JsonSerializer.Deserialize<VisibilityDocument>(
                File.ReadAllText(_path),
                JsonOptions);
            return new VisibilityDocument(
                (document?.Items ?? new Dictionary<string, bool>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return new VisibilityDocument(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void Write(VisibilityDocument document)
    {
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporary, _path, true);
    }

    private static string CreateKey(string kind, string connectorId, string symbol, string item) =>
        CreateKeyPrefix(kind, connectorId, symbol) + Normalize(item);

    private static string CreateKeyPrefix(string kind, string connectorId, string symbol) =>
        string.Join("|", Normalize(kind), Normalize(connectorId), Normalize(symbol)) + "|";

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed class VisibilityDocument
    {
        public VisibilityDocument()
        {
        }

        public VisibilityDocument(Dictionary<string, bool> items)
        {
            Items = items;
        }

        public Dictionary<string, bool> Items { get; init; } = new();
    }
}

public sealed record HiddenHistoryRange(
    string Key,
    long StartUnix,
    long EndUnix);
