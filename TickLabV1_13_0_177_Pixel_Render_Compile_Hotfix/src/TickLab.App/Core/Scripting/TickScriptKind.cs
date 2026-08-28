namespace TickLab.Core.Scripting;

public enum TickScriptKind
{
    Indicator,
    Strategy,
    ExpertAdvisor,
    Utility,
    Scanner,
    Alert,
    DrawingTool,
    CustomChart,
    Library,
    DataTool
}

public static class TickScriptKindExtensions
{
    public static string DisplayName(this TickScriptKind kind) => kind switch
    {
        TickScriptKind.Indicator => "Indicator",
        TickScriptKind.Strategy => "Strategy",
        TickScriptKind.ExpertAdvisor => "EA / Expert Advisor",
        TickScriptKind.Utility => "Script / Utility",
        TickScriptKind.Scanner => "Scanner / Screener",
        TickScriptKind.Alert => "Alert",
        TickScriptKind.DrawingTool => "Drawing Tool",
        TickScriptKind.CustomChart => "Custom Chart",
        TickScriptKind.Library => "Library / Module",
        TickScriptKind.DataTool => "Data Tool",
        _ => kind.ToString()
    };

    public static string DeclarationName(this TickScriptKind kind) => kind switch
    {
        TickScriptKind.Indicator => "indicator",
        TickScriptKind.Strategy => "strategy",
        TickScriptKind.ExpertAdvisor => "ea",
        TickScriptKind.Utility => "script",
        TickScriptKind.Scanner => "scanner",
        TickScriptKind.Alert => "alertscript",
        TickScriptKind.DrawingTool => "drawing",
        TickScriptKind.CustomChart => "chart",
        TickScriptKind.Library => "library",
        TickScriptKind.DataTool => "datatool",
        _ => "script"
    };

    public static string FolderName(this TickScriptKind kind) => kind switch
    {
        TickScriptKind.Indicator => "Indicators",
        TickScriptKind.Strategy => "Strategies",
        TickScriptKind.ExpertAdvisor => "EAs",
        TickScriptKind.Utility => "Scripts",
        TickScriptKind.Scanner => "Scanners",
        TickScriptKind.Alert => "Alerts",
        TickScriptKind.DrawingTool => "DrawingTools",
        TickScriptKind.CustomChart => "CustomCharts",
        TickScriptKind.Library => "Libraries",
        TickScriptKind.DataTool => "DataTools",
        _ => "Scripts"
    };

    public static bool IsTradingSimulation(this TickScriptKind kind) =>
        kind is TickScriptKind.Strategy or TickScriptKind.ExpertAdvisor;
}
