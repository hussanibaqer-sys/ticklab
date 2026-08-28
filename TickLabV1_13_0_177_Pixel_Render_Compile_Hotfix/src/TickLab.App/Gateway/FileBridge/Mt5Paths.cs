using System.IO;

namespace TickLab.Gateway.FileBridge;

public static class Mt5Paths
{
    private static readonly object Sync = new();
    private static string? _manualConnectionsRoot;
    private static string? _activeConnectionsRoot;

    public static string? ManualConnectionsRoot
    {
        get
        {
            lock (Sync)
                return _manualConnectionsRoot;
        }
    }

    public static string GetConnectionsRoot()
    {
        lock (Sync)
        {
            return _activeConnectionsRoot
                ?? _manualConnectionsRoot
                ?? GetDefaultConnectionsRoot();
        }
    }

    public static IReadOnlyList<string> GetConnectionsRootCandidates()
    {
        var candidates = new List<string>();

        lock (Sync)
        {
            AddUnique(candidates, _manualConnectionsRoot);
            AddUnique(candidates, _activeConnectionsRoot);
        }

        AddUnique(candidates, GetDefaultConnectionsRoot());

        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        AddUnique(
            candidates,
            Path.Combine(
                localAppData,
                "MetaQuotes",
                "Terminal",
                "Common",
                "Files",
                "TickLab",
                "Connections"));

        AddDiscoveredCommonRoots(
            candidates,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData));

        AddDiscoveredCommonRoots(candidates, localAppData);

        string userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        string commonAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        string commonDocuments = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDocuments);

        AddUnique(candidates, Path.Combine(
            userProfile, "AppData", "Roaming", "MetaQuotes", "Terminal",
            "Common", "Files", "TickLab", "Connections"));
        AddUnique(candidates, Path.Combine(
            userProfile, "AppData", "Local", "MetaQuotes", "Terminal",
            "Common", "Files", "TickLab", "Connections"));
        AddUnique(candidates, Path.Combine(
            commonAppData, "MetaQuotes", "Terminal", "Common", "Files",
            "TickLab", "Connections"));
        AddUnique(candidates, Path.Combine(
            commonDocuments, "MetaQuotes", "Terminal", "Common", "Files",
            "TickLab", "Connections"));

        return candidates;
    }

    public static bool SetManualBridgeFolder(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            lock (Sync)
            {
                _manualConnectionsRoot = null;
                _activeConnectionsRoot = null;
            }

            return true;
        }

        string? resolved = ResolveConnectionsRoot(selectedPath);
        if (resolved is null)
            return false;

        lock (Sync)
        {
            _manualConnectionsRoot = resolved;
            _activeConnectionsRoot = resolved;
        }

        return true;
    }

    public static void UseConnectionsRoot(string root)
    {
        string? resolved = ResolveConnectionsRoot(root);
        if (resolved is null)
            return;

        lock (Sync)
            _activeConnectionsRoot = resolved;
    }

    public static string? ResolveConnectionsRoot(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return null;

        try
        {
            string path = Path.GetFullPath(selectedPath.Trim().Trim('"'));
            string name = Path.GetFileName(
                path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            if (string.Equals(name, "Connections", StringComparison.OrdinalIgnoreCase))
                return path;

            if (string.Equals(name, "TickLab", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(path, "Connections");

            if (string.Equals(name, "Files", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(path, "TickLab", "Connections");

            if (File.Exists(Path.Combine(path, "connection.json")) ||
                File.Exists(Path.Combine(path, "connection.json.tmp")) ||
                File.Exists(Path.Combine(path, "live_channel_heartbeat.json")) ||
                File.Exists(Path.Combine(path, "live_channel_heartbeat.json.tmp")) ||
                File.Exists(Path.Combine(path, "heartbeat.json")) ||
                File.Exists(Path.Combine(path, "heartbeat.json.tmp")))
            {
                DirectoryInfo? parent = Directory.GetParent(path);
                return parent?.FullName;
            }

            string nested = Path.Combine(path, "TickLab", "Connections");
            if (Directory.Exists(nested))
                return nested;

            string direct = Path.Combine(path, "Connections");
            if (Directory.Exists(direct))
                return direct;

            // For a user-selected empty TickLab/Common Files folder, return
            // the professional default location that the bridge will create.
            return string.Equals(name, "Common", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(path, "Files", "TickLab", "Connections")
                : direct;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    public static string SanitizeFilePart(string value)
    {
        string safe = new(value
            .Trim()
            .Select(character => Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    public static bool IsValidConnectorId(string? connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
            return false;

        return connectorId.All(
            character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_');
    }

    public static string GetConnectorFolder(string connectorId)
    {
        if (!IsValidConnectorId(connectorId))
        {
            throw new ArgumentException(
                "Invalid MT5 connector ID.",
                nameof(connectorId));
        }

        return Path.Combine(GetConnectionsRoot(), connectorId);
    }

    private static string GetDefaultConnectionsRoot()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        return Path.Combine(
            appData,
            "MetaQuotes",
            "Terminal",
            "Common",
            "Files",
            "TickLab",
            "Connections");
    }

    private static void AddDiscoveredCommonRoots(
        ICollection<string> candidates,
        string appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
            return;

        string terminalRoot = Path.Combine(
            appDataRoot,
            "MetaQuotes",
            "Terminal");

        if (!Directory.Exists(terminalRoot))
            return;

        var pending = new Stack<string>();
        pending.Push(terminalRoot);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> children;

            try
            {
                children = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);

                if (string.Equals(name, "Common", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(candidates, Path.Combine(
                        child, "Files", "TickLab", "Connections"));
                }

                // MT5 terminal trees are shallow; still cap traversal so an
                // unexpected junction cannot turn auto-detection into a full
                // disk scan.
                int relativeDepth = Path.GetRelativePath(terminalRoot, child)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Length;
                if (relativeDepth < 6)
                    pending.Push(child);
            }
        }
    }

    private static void AddUnique(
        ICollection<string> candidates,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            candidates.Add(fullPath);
    }
}
