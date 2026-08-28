using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickLab.Core.Indicators;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Settings;

public sealed record ChartTemplateEntry(
    string Name,
    ChartSettings Settings,
    IReadOnlyList<BuiltInIndicatorInstance>? BuiltInIndicators = null);

public sealed class ChartTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _sync = new();

    public ChartTemplateStore()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TickLab");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "chart-templates.json");
    }

    public IReadOnlyList<ChartTemplateEntry> LoadAll()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return Array.Empty<ChartTemplateEntry>();
            try
            {
                return (JsonSerializer.Deserialize<List<ChartTemplateEntry>>(File.ReadAllText(_path), JsonOptions)
                    ?? new List<ChartTemplateEntry>())
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Settings is not null)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<ChartTemplateEntry>();
            }
        }
    }

    public bool Contains(string name) =>
        LoadAll().Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Save(string name, ChartSettings settings, IReadOnlyList<BuiltInIndicatorInstance>? builtInIndicators = null)
    {
        string cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0)
            throw new ArgumentException("Template name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            List<ChartTemplateEntry> entries = LoadAll().ToList();
            int index = entries.FindIndex(item => string.Equals(item.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            var replacement = new ChartTemplateEntry(cleanName, settings, builtInIndicators ?? Array.Empty<BuiltInIndicatorInstance>());
            if (index >= 0)
                entries[index] = replacement;
            else
                entries.Add(replacement);
            Write(entries);
        }
    }

    public bool Delete(string name)
    {
        lock (_sync)
        {
            List<ChartTemplateEntry> entries = LoadAll().ToList();
            int removed = entries.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return false;
            Write(entries);
            return true;
        }
    }

    public void Export(string filePath, string name)
    {
        ChartTemplateEntry entry = LoadAll().First(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(filePath, JsonSerializer.Serialize(entry, JsonOptions));
    }

    public ChartTemplateEntry Import(string filePath)
    {
        ChartTemplateEntry entry = JsonSerializer.Deserialize<ChartTemplateEntry>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidDataException("The template file is empty or invalid.");
        Save(entry.Name, entry.Settings, entry.BuiltInIndicators);
        return entry;
    }

    private void Write(IReadOnlyList<ChartTemplateEntry> entries)
    {
        string temporary = _path + ".tmp";
        string backup = _path + ".bak";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase), JsonOptions));
        if (File.Exists(_path))
        {
            try { File.Replace(temporary, _path, backup, true); }
            catch { File.Copy(_path, backup, true); File.Move(temporary, _path, true); }
        }
        else
        {
            File.Move(temporary, _path, true);
        }
    }
}
