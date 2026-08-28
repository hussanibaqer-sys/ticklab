using TickLab.Core.Indicators;
using TickLab.Core.Settings;
using TickLab.Core.Scripting;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Settings;

public sealed record UserPreferences
{
    public string ApplicationTheme { get; init; } = "Dark";
    public string LastConnectorId { get; init; } = string.Empty;
    public string LastSelectedConnectorId { get; init; } = string.Empty;
    public string BridgeFolderOverride { get; init; } = string.Empty;
    public string LastSelectedSymbol { get; init; } = string.Empty;
    public string LastSelectedTimeframe { get; init; } = string.Empty;
    public string LastChartSymbol { get; init; } = string.Empty;
    public string LastChartTimeframe { get; init; } = string.Empty;
    public string LastActiveTimeframeKey { get; init; } = string.Empty;
    public IReadOnlyList<CustomTimeframePreference> CustomTimeframes { get; init; } =
        Array.Empty<CustomTimeframePreference>();
    public IReadOnlyList<string> FavoriteTimeframeKeys { get; init; } = Array.Empty<string>();
    public bool TimeframeFavoritesWindowVisible { get; init; }
    public bool TimeframeFavoritesWindowCompact { get; init; }
    public double TimeframeFavoritesWindowLeft { get; init; } = double.NaN;
    public double TimeframeFavoritesWindowTop { get; init; } = double.NaN;
    public double TimeframeFavoritesWindowWidth { get; init; } = 430;
    public double TimeframeFavoritesWindowHeight { get; init; } = 46;
    public long? LastSelectedCandleStartUnix { get; init; }
    public ChartSettings Chart { get; init; } = ChartSettings.Default;
    public ChartViewportState Viewport { get; init; } = ChartViewportState.Default;
    public bool ReceiveMarkers { get; init; }
    public double WindowLeft { get; init; } = double.NaN;
    public double WindowTop { get; init; } = double.NaN;
    public double WindowWidth { get; init; } = 1240;
    public double WindowHeight { get; init; } = 760;
    public bool WindowMaximized { get; init; }
    public HistoryDisplayMode HistoryDisplayMode { get; init; } =
        HistoryDisplayMode.RecentThreeMonths;
    public IReadOnlyList<string> SelectedHistorySegments { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<string> DrawingDocuments { get; init; } =
        Array.Empty<string>();
    public bool DrawingToolbarCollapsed { get; init; }
    public bool DrawingFavoritesWindowVisible { get; init; }
    public bool DrawingFavoritesWindowCompact { get; init; }
    public double DrawingFavoritesWindowLeft { get; init; } = double.NaN;
    public double DrawingFavoritesWindowTop { get; init; } = double.NaN;
    public double DrawingFavoritesWindowWidth { get; init; } = 430;
    public double DrawingFavoritesWindowHeight { get; init; } = 86;
    public IReadOnlyList<string> AppliedIndicatorSourcePaths { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<AppliedTickScriptIndicatorPreference> AppliedTickScriptIndicators { get; init; } =
        Array.Empty<AppliedTickScriptIndicatorPreference>();
    public IReadOnlyList<BuiltInIndicatorInstance> AppliedBuiltInIndicators { get; init; } =
        Array.Empty<BuiltInIndicatorInstance>();
    public bool WorkspaceStateInitialized { get; init; }
    public int ActiveWorkspaceId { get; init; } = 1;
    public int PreferredWorkspaceLayout { get; init; } = 1;
    public IReadOnlyList<WorkspacePagePreference> Workspaces { get; init; } =
        Array.Empty<WorkspacePagePreference>();
    public IReadOnlyList<WorkspacePanePreference> FloatingPanes { get; init; } =
        Array.Empty<WorkspacePanePreference>();
}
