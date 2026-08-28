namespace TickLab.Core.Settings;

public sealed record ChartViewportState(
    int VisibleCount,
    int RightOffset,
    bool VerticalAuto,
    double ManualMinimum,
    double ManualMaximum)
{
    public static ChartViewportState Default { get; } =
        new(110, 0, true, 0, 0);
}
