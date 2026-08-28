using TickLab.Core.Indicators;
using TickLab.Core.Settings;
using TickLab.Core.Scripting;

namespace TickLab.Desktop.Settings;

public sealed record WorkspacePanePreference
{
    public int PaneId { get; init; }
    public string Kind { get; init; } = "PriceChart";
    public string Title { get; init; } = "Chart";
    public int PartitionId { get; init; }
    public bool IsFloating { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public ChartViewportState Viewport { get; init; } = ChartViewportState.Default;
    public string DrawingDocument { get; init; } = string.Empty;
    public ChartSettings ChartSettings { get; init; } = ChartSettings.Default;
    public IReadOnlyList<BuiltInIndicatorInstance> BuiltInIndicators { get; init; } =
        Array.Empty<BuiltInIndicatorInstance>();
    public IReadOnlyList<AppliedTickScriptIndicatorPreference> TickScriptIndicators { get; init; } =
        Array.Empty<AppliedTickScriptIndicatorPreference>();
    public bool SyncIndicatorsWithPriceChart { get; init; } = true;
    public int ConnectedPricePaneId { get; init; }
    public double WindowLeft { get; init; } = double.NaN;
    public double WindowTop { get; init; } = double.NaN;
    public double WindowWidth { get; init; } = 980;
    public double WindowHeight { get; init; } = 620;
    public bool WindowMaximized { get; init; }
}

public sealed record WorkspacePagePreference
{
    public int WorkspaceId { get; init; }
    public int LayoutCount { get; init; } = 1;
    public bool IsDetached { get; init; }
    public bool IsMinimized { get; init; }
    public double WindowLeft { get; init; } = double.NaN;
    public double WindowTop { get; init; } = double.NaN;
    public double WindowWidth { get; init; } = 1180;
    public double WindowHeight { get; init; } = 760;
    public bool WindowMaximized { get; init; }
    public IReadOnlyList<WorkspacePanePreference> Panes { get; init; } = Array.Empty<WorkspacePanePreference>();
}
