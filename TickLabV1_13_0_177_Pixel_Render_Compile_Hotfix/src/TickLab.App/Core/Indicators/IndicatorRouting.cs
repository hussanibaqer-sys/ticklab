namespace TickLab.Core.Indicators;

public enum IndicatorRouteAction
{
    Connect,
    Copy,
    Move
}

public sealed record IndicatorRouteTarget(
    int PaneId,
    int? WorkspaceId,
    int? PartitionId,
    string Symbol,
    string Timeframe)
{
    public string DisplayText => WorkspaceId.HasValue && PartitionId.HasValue
        ? $"Workspace {WorkspaceId} → Partition {PartitionId} → Chart {PaneId} — {Symbol} {Timeframe}"
        : $"Floating → Chart {PaneId} — {Symbol} {Timeframe}";
}
