namespace TickLab.Core.Scripting;

public enum TickScriptDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record TickScriptDiagnostic(
    TickScriptDiagnosticSeverity Severity,
    int Line,
    int Column,
    string Code,
    string Message)
{
    public string Location => Line > 0
        ? Column > 0 ? $"Ln {Line}, Col {Column}" : $"Ln {Line}"
        : "General";
}
