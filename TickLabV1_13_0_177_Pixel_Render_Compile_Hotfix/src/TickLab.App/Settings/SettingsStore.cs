using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickLab.Core.Settings;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            // Window positions deliberately use NaN as an "unset" sentinel.
            // This option writes named floating-point values as valid JSON strings
            // and reads them back safely instead of throwing during workspace save.
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;
    private readonly string _backupSettingsPath;
    private readonly object _sync = new();

    public SettingsStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string folder = Path.Combine(localAppData, "TickLab");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        _legacySettingsPath = Path.Combine(
            localAppData,
            "TickLabV1_2",
            "settings.json");
        _backupSettingsPath = _settingsPath + ".bak";
    }

    public UserPreferences Load()
    {
        string path = File.Exists(_settingsPath)
            ? _settingsPath
            : _legacySettingsPath;

        if (!File.Exists(path))
            return Sanitize(new UserPreferences());

        try
        {
            UserPreferences preferences = ReadPreferences(path);
            if (!File.Exists(_settingsPath))
                Save(preferences);
            return preferences;
        }
        catch
        {
            try
            {
                if (File.Exists(_backupSettingsPath))
                    return ReadPreferences(_backupSettingsPath);
            }
            catch
            {
                // The primary and backup are both unavailable. Use safe defaults.
            }
            return Sanitize(new UserPreferences());
        }
    }

    public void Save(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        lock (_sync)
        {
            UserPreferences safePreferences = Sanitize(preferences);
            string temporaryPath = _settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(safePreferences, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
            {
                try
                {
                    File.Replace(temporaryPath, _settingsPath, _backupSettingsPath, true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(_settingsPath, _backupSettingsPath, true);
                    File.Move(temporaryPath, _settingsPath, true);
                }
                catch (IOException)
                {
                    File.Copy(_settingsPath, _backupSettingsPath, true);
                    File.Move(temporaryPath, _settingsPath, true);
                }
            }
            else
            {
                File.Move(temporaryPath, _settingsPath, true);
                File.Copy(_settingsPath, _backupSettingsPath, true);
            }
        }
    }


    private static UserPreferences ReadPreferences(string path)
    {
        string json = File.ReadAllText(path);
        return Sanitize(
            JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions)
            ?? new UserPreferences());
    }

    private static UserPreferences Sanitize(UserPreferences preferences)
    {
        ChartViewportState viewport = preferences.Viewport ?? ChartViewportState.Default;
        bool validManualRange =
            double.IsFinite(viewport.ManualMinimum) &&
            double.IsFinite(viewport.ManualMaximum) &&
            viewport.ManualMaximum > viewport.ManualMinimum;

        ChartViewportState safeViewport = viewport with
        {
            VisibleCount = Math.Clamp(viewport.VisibleCount, 1, 1_500),
            RightOffset = Math.Clamp(viewport.RightOffset, -1_000_000_000, int.MaxValue / 2),
            VerticalAuto = viewport.VerticalAuto || !validManualRange,
            ManualMinimum = validManualRange ? viewport.ManualMinimum : 0,
            ManualMaximum = validManualRange ? viewport.ManualMaximum : 0
        };

        IReadOnlyList<WorkspacePagePreference> safeWorkspaces = (preferences.Workspaces ?? Array.Empty<WorkspacePagePreference>())
            .Select(page => page with
            {
                Panes = (page.Panes ?? Array.Empty<WorkspacePanePreference>())
                    .Select(pane => pane with
                    {
                        ChartSettings = pane.ChartSettings ?? ChartSettings.Default,
                        BuiltInIndicators = pane.BuiltInIndicators ?? Array.Empty<TickLab.Core.Indicators.BuiltInIndicatorInstance>(),
                        TickScriptIndicators = pane.TickScriptIndicators ?? Array.Empty<TickLab.Core.Scripting.AppliedTickScriptIndicatorPreference>()
                    })
                    .ToArray()
            })
            .ToArray();
        IReadOnlyList<WorkspacePanePreference> safeFloatingPanes = (preferences.FloatingPanes ?? Array.Empty<WorkspacePanePreference>())
            .Select(pane => pane with
                    {
                        ChartSettings = pane.ChartSettings ?? ChartSettings.Default,
                        BuiltInIndicators = pane.BuiltInIndicators ?? Array.Empty<TickLab.Core.Indicators.BuiltInIndicatorInstance>(),
                        TickScriptIndicators = pane.TickScriptIndicators ?? Array.Empty<TickLab.Core.Scripting.AppliedTickScriptIndicatorPreference>()
                    })
            .ToArray();

        return preferences with
        {
            ApplicationTheme = string.Equals(preferences.ApplicationTheme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark",
            Chart = preferences.Chart ?? ChartSettings.Default,
            Viewport = safeViewport,
            WindowLeft = NormalizePosition(preferences.WindowLeft),
            WindowTop = NormalizePosition(preferences.WindowTop),
            WindowWidth = NormalizeSize(preferences.WindowWidth, 1240, 480, 20_000),
            WindowHeight = NormalizeSize(preferences.WindowHeight, 760, 320, 20_000),
            DrawingFavoritesWindowLeft = NormalizePosition(preferences.DrawingFavoritesWindowLeft),
            DrawingFavoritesWindowTop = NormalizePosition(preferences.DrawingFavoritesWindowTop),
            DrawingFavoritesWindowWidth = NormalizeSize(
                preferences.DrawingFavoritesWindowWidth,
                430,
                260,
                900),
            DrawingFavoritesWindowHeight = NormalizeSize(
                preferences.DrawingFavoritesWindowHeight,
                86,
                46,
                220),
            CustomTimeframes = preferences.CustomTimeframes ?? Array.Empty<CustomTimeframePreference>(),
            FavoriteTimeframeKeys = preferences.FavoriteTimeframeKeys ?? Array.Empty<string>(),
            TimeframeFavoritesWindowLeft = NormalizePosition(preferences.TimeframeFavoritesWindowLeft),
            TimeframeFavoritesWindowTop = NormalizePosition(preferences.TimeframeFavoritesWindowTop),
            TimeframeFavoritesWindowWidth = NormalizeSize(preferences.TimeframeFavoritesWindowWidth, 430, 118, 900),
            TimeframeFavoritesWindowHeight = NormalizeSize(preferences.TimeframeFavoritesWindowHeight, 46, 46, 100),
            SelectedHistorySegments = preferences.SelectedHistorySegments ?? Array.Empty<string>(),
            DrawingDocuments = preferences.DrawingDocuments ?? Array.Empty<string>(),
            AppliedIndicatorSourcePaths = preferences.AppliedIndicatorSourcePaths ?? Array.Empty<string>(),
            AppliedTickScriptIndicators = preferences.AppliedTickScriptIndicators ?? Array.Empty<TickLab.Core.Scripting.AppliedTickScriptIndicatorPreference>(),
            AppliedBuiltInIndicators = preferences.AppliedBuiltInIndicators ?? Array.Empty<TickLab.Core.Indicators.BuiltInIndicatorInstance>(),
            ActiveWorkspaceId = Math.Max(0, preferences.ActiveWorkspaceId),
            PreferredWorkspaceLayout = preferences.PreferredWorkspaceLayout is 1 or 2 or 3 or 4 or 6
                ? preferences.PreferredWorkspaceLayout
                : 1,
            Workspaces = safeWorkspaces,
            FloatingPanes = safeFloatingPanes
        };
    }

    private static double NormalizePosition(double value) =>
        double.IsFinite(value) ? value : double.NaN;

    private static double NormalizeSize(
        double value,
        double fallback,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value) || value <= 0)
            return fallback;

        return Math.Clamp(value, minimum, maximum);
    }
}
