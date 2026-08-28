using System.Text.Json;
using TickLab.Core.Alerts;

namespace TickLab.Desktop.Settings;

public sealed class AlertStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly string _backupPath;
    private readonly object _sync = new();

    public AlertStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TickLab");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "alerts.json");
        _backupPath = _path + ".bak";
    }

    public AlertDocument Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                    return new AlertDocument();
                return Sanitize(JsonSerializer.Deserialize<AlertDocument>(File.ReadAllText(_path), JsonOptions));
            }
            catch
            {
                try
                {
                    if (File.Exists(_backupPath))
                        return Sanitize(JsonSerializer.Deserialize<AlertDocument>(File.ReadAllText(_backupPath), JsonOptions));
                }
                catch
                {
                }
                return new AlertDocument();
            }
        }
    }

    public void Save(AlertDocument document)
    {
        lock (_sync)
        {
            AlertDocument safe = Sanitize(document);
            string temporary = _path + ".tmp";
            string json = JsonSerializer.Serialize(safe, JsonOptions);
            using (var stream = new FileStream(
                       temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_path))
            {
                try
                {
                    File.Replace(temporary, _path, _backupPath, true);
                }
                catch
                {
                    File.Copy(_path, _backupPath, true);
                    File.Move(temporary, _path, true);
                }
            }
            else
            {
                File.Move(temporary, _path, true);
                File.Copy(_path, _backupPath, true);
            }
        }
    }

    private static AlertDocument Sanitize(AlertDocument? document)
    {
        document ??= new AlertDocument();
        return document with
        {
            Rules = (document.Rules ?? Array.Empty<AlertRule>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray(),
            Log = (document.Log ?? Array.Empty<AlertLogEntry>())
                .OrderByDescending(item => item.TriggeredUnix)
                .Take(1000)
                .ToArray()
        };
    }
}
