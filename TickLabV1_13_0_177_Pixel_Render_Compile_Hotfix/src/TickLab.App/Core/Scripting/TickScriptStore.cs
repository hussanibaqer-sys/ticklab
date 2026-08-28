using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TickLab.Core.Scripting;

public sealed class TickScriptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IReadOnlyDictionary<TickScriptKind, string> _folders;

    public TickScriptStore()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootPath = Path.Combine(localAppData, "TickLab");
        _folders = Enum.GetValues<TickScriptKind>()
            .ToDictionary(kind => kind, kind => Path.Combine(RootPath, kind.FolderName()));
    }

    public string RootPath { get; }
    public string IndicatorsPath => GetFolder(TickScriptKind.Indicator);
    public string ExpertAdvisorsPath => GetFolder(TickScriptKind.ExpertAdvisor);
    public string GetFolder(TickScriptKind kind) => _folders[kind];

    public IReadOnlyList<TickScriptEntry> GetScripts()
    {
        var entries = new List<TickScriptEntry>();
        foreach (TickScriptKind kind in Enum.GetValues<TickScriptKind>())
            AddScripts(entries, GetFolder(kind), kind);

        return entries
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<TickScriptEntry> GetIndicators() =>
        GetScripts()
            .Where(item => item.Kind == TickScriptKind.Indicator && item.IsCompiled)
            .ToArray();

    public string LoadSource(TickScriptEntry entry) =>
        File.ReadAllText(entry.SourcePath, Encoding.UTF8);

    public TickScriptEntry SaveCompiled(
        string name,
        TickScriptKind kind,
        string source,
        TickScriptCompileResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException("Cannot save a failed compilation.");

        string safeName = SanitizeFileName(name);
        string folder = GetFolder(kind);
        Directory.CreateDirectory(folder);

        string sourcePath = Path.Combine(folder, safeName + ".tlscript");
        string compiledPath = Path.Combine(folder, safeName + ".tlc.json");
        string sourceHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();

        string runtimeMode = kind switch
        {
            TickScriptKind.Indicator => "Chart indicator",
            TickScriptKind.Strategy or TickScriptKind.ExpertAdvisor =>
                "Simulation and backtesting only",
            _ => kind.DisplayName()
        };

        var compiled = new CompiledTickScript(
            2,
            TickScriptCompiler.CompilerVersion,
            name.Trim(),
            kind.ToString(),
            kind.DisplayName(),
            Path.GetFileName(sourcePath),
            sourceHash,
            DateTime.UtcNow,
            result.Functions,
            result.InputCount,
            result.OutputCount,
            result.ActionCount,
            runtimeMode);

        WriteAtomic(sourcePath, source);
        WriteAtomic(compiledPath, JsonSerializer.Serialize(compiled, JsonOptions));

        return new TickScriptEntry(
            name.Trim(),
            kind,
            sourcePath,
            compiledPath,
            File.GetLastWriteTimeUtc(sourcePath),
            true);
    }

    public void Delete(TickScriptEntry entry)
    {
        if (File.Exists(entry.SourcePath))
            File.Delete(entry.SourcePath);
        if (File.Exists(entry.CompiledPath))
            File.Delete(entry.CompiledPath);
    }

    public static string CreateTemplate(TickScriptKind kind, string name)
    {
        string safeName = string.IsNullOrWhiteSpace(name)
            ? $"My {kind.DisplayName()}"
            : name.Trim().Replace('"', '\'');
        string declaration = kind.DeclarationName();

        string[] body = kind switch
        {
            TickScriptKind.Indicator =>
            [
                $"{declaration}(\"{safeName}\", overlay=false)",
                string.Empty,
                "length = input.int(14, \"Length\")",
                "value = rsi(close, length)",
                "plot(value, \"RSI\")",
                "hline(70, \"Overbought\")",
                "hline(30, \"Oversold\")"
            ],
            TickScriptKind.Strategy or TickScriptKind.ExpertAdvisor =>
            [
                $"{declaration}(\"{safeName}\", overlay=true)",
                string.Empty,
                "fast = ema(close, 9)",
                "slow = ema(close, 21)",
                "strategy.entry(\"Long\", crossover(fast, slow))",
                "strategy.close(\"Long\", crossunder(fast, slow))"
            ],
            TickScriptKind.Scanner =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "match = rsi(close, 14) < 30",
                "scan.result(match, \"RSI oversold\")"
            ],
            TickScriptKind.Alert =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "condition = crossover(ema(close, 9), ema(close, 21))",
                "alertcondition(condition, \"EMA crossover\")"
            ],
            TickScriptKind.DrawingTool =>
            [
                $"{declaration}(\"{safeName}\", overlay=true)",
                string.Empty,
                "drawing.line(lowest(low, 20), highest(high, 20))"
            ],
            TickScriptKind.CustomChart =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "chart.candle(open, high, low, close)"
            ],
            TickScriptKind.Library =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "exportValue = ema(close, 20)"
            ],
            TickScriptKind.DataTool =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "data.output(close, \"Close\")"
            ],
            _ =>
            [
                $"{declaration}(\"{safeName}\")",
                string.Empty,
                "value = close"
            ]
        };

        return "// TickScript " + kind.DisplayName() + Environment.NewLine +
               string.Join(Environment.NewLine, body) + Environment.NewLine;
    }

    public static TickScriptKind DetectKind(string source, TickScriptKind fallback)
    {
        foreach (string raw in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            foreach (TickScriptKind kind in Enum.GetValues<TickScriptKind>())
            {
                if (line.StartsWith(kind.DeclarationName() + "(", StringComparison.OrdinalIgnoreCase))
                    return kind;
            }

            if (line.StartsWith("screener(", StringComparison.OrdinalIgnoreCase))
                return TickScriptKind.Scanner;

            break;
        }

        return fallback;
    }

    private static void AddScripts(
        ICollection<TickScriptEntry> target,
        string folder,
        TickScriptKind kind)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string sourcePath in Directory.EnumerateFiles(folder, "*.tlscript"))
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string compiledPath = Path.Combine(folder, name + ".tlc.json");
            target.Add(new TickScriptEntry(
                name,
                kind,
                sourcePath,
                compiledPath,
                File.GetLastWriteTimeUtc(sourcePath),
                File.Exists(compiledPath)));
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(name.Trim().Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        safe = safe.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "Untitled" : safe;
    }

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private sealed record CompiledTickScript(
        int SchemaVersion,
        string CompilerVersion,
        string Name,
        string Kind,
        string DisplayKind,
        string SourceFile,
        string SourceSha256,
        DateTime CompiledAtUtc,
        IReadOnlyList<string> Functions,
        int InputCount,
        int OutputCount,
        int ActionCount,
        string RuntimeMode);
}
