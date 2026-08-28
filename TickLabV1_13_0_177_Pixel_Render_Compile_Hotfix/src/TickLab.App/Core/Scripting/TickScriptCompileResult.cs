namespace TickLab.Core.Scripting;

public sealed record TickScriptCompileResult(
    bool Success,
    string ScriptName,
    TickScriptKind Kind,
    IReadOnlyList<TickScriptDiagnostic> Diagnostics,
    IReadOnlyList<string> Functions,
    int InputCount,
    int OutputCount,
    int ActionCount)
{
    public static TickScriptCompileResult Failed(
        string scriptName,
        TickScriptKind kind,
        IEnumerable<TickScriptDiagnostic> diagnostics) =>
        new(
            false,
            scriptName,
            kind,
            diagnostics.ToArray(),
            Array.Empty<string>(),
            0,
            0,
            0);
}
