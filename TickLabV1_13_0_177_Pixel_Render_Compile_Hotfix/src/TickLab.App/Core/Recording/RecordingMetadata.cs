using System.Text.Json;

namespace TickLab.Desktop.Core.Recording;

internal sealed record RecordingMetadata(
    string FileName,
    string Description,
    DateTime CreatedUtc,
    string Kind)
{
    public static string MetadataPath(string mediaPath) => mediaPath + ".json";

    public static void Save(string mediaPath, string description, string kind)
    {
        var metadata = new RecordingMetadata(
            Path.GetFileName(mediaPath),
            description?.Trim() ?? string.Empty,
            DateTime.UtcNow,
            kind);
        File.WriteAllText(
            MetadataPath(mediaPath),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static RecordingMetadata? TryLoad(string mediaPath)
    {
        try
        {
            string path = MetadataPath(mediaPath);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<RecordingMetadata>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
