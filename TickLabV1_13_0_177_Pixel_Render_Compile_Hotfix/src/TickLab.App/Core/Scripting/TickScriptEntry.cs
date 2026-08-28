namespace TickLab.Core.Scripting;

public sealed record TickScriptEntry(
    string Name,
    TickScriptKind Kind,
    string SourcePath,
    string CompiledPath,
    DateTime ModifiedUtc,
    bool IsCompiled)
{
    public string TypeText => Kind.DisplayName();
    public string ModifiedText => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string StatusText => IsCompiled ? "Compiled" : "Source only";
}
