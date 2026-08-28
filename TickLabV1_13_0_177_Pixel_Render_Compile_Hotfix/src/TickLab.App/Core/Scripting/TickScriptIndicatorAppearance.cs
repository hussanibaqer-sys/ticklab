using TickLab.Core.Settings;

namespace TickLab.Core.Scripting;

public sealed record TickScriptIndicatorAppearance
{
    public string LineColor { get; init; } = "#5B86C4";
    public double LineWidth { get; init; } = 1.6;
    public ChartLineStyle LineStyle { get; init; } = ChartLineStyle.Solid;
    public string UpperLevelColor { get; init; } = "#F59E0B";
    public string LowerLevelColor { get; init; } = "#F59E0B";
    public double LevelWidth { get; init; } = 1.0;
    public ChartLineStyle LevelLineStyle { get; init; } = ChartLineStyle.Dashed;
    public string FillColor { get; init; } = "#5B86C4";
    public double FillOpacity { get; init; } = 0.0;
    public string LabelColor { get; init; } = "#D8D8D8";
    public bool Visible { get; init; } = true;
    public string LinkedGroupId { get; init; } = string.Empty;

    public static TickScriptIndicatorAppearance Default { get; } = new();
}

public sealed record AppliedTickScriptIndicatorPreference
{
    public string SourcePath { get; init; } = string.Empty;
    public TickScriptIndicatorAppearance Appearance { get; init; } = TickScriptIndicatorAppearance.Default;
}
