using System.Text.RegularExpressions;

namespace TickLab.Core.Scripting;

public sealed class TickScriptCompiler
{
    public const string CompilerVersion = "1.1";

    private static readonly Regex DeclarationRegex = new(
        "^\\s*(?<kind>indicator|strategy|ea|script|scanner|screener|alertscript|drawing|chart|library|datatool)\\s*\\(\\s*\\\"(?<name>(?:[^\\\"\\\\]|\\\\.)+)\\\"(?<arguments>[^)]*)\\)\\s*;?\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FunctionCallRegex = new(
        "(?<![A-Za-z0-9_])(?<name>[A-Za-z_][A-Za-z0-9_.]*)\\s*\\(",
        RegexOptions.Compiled);

    private static readonly Regex InputCallRegex = new(
        "(?<![A-Za-z0-9_])input\\.(int|float|bool|string|source)\\s*\\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> KnownFunctions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "indicator", "strategy", "ea", "script", "scanner", "screener", "alertscript", "drawing", "chart", "library", "datatool",
        "input.int", "input.float", "input.bool", "input.string", "input.source",
        "plot", "plotshape", "plotchar", "hline", "barcolor", "bgcolor", "fill",
        "sma", "ema", "rma", "wma", "vwma", "rsi", "atr", "tr", "stdev",
        "highest", "lowest", "sum", "change", "roc", "momentum",
        "crossover", "crossunder", "rising", "falling",
        "abs", "min", "max", "round", "floor", "ceil", "sqrt", "pow", "log", "exp",
        "nz", "valuewhen", "barssince", "timestamp",
        "strategy.entry", "strategy.exit", "strategy.close", "strategy.order",
        "strategy.cancel", "strategy.cancel_all", "strategy.risk",
        "alert", "alertcondition", "scan.result", "drawing.line", "drawing.box", "drawing.label", "chart.candle", "chart.bar", "data.output"
    };

    private static readonly HashSet<string> RenderingFunctions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "plot", "plotshape", "plotchar", "hline", "barcolor", "bgcolor", "fill"
    };

    private static readonly HashSet<string> StrategyFunctions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "strategy.entry", "strategy.exit", "strategy.close", "strategy.order",
        "strategy.cancel", "strategy.cancel_all", "strategy.risk"
    };

    private static readonly string[] ForbiddenLiveTradingTokens =
    {
        "ordersend", "order_send", "mt5.", "ctrader.", "ninjatrader.",
        "socket", "webrequest", "dllimport", "process.start"
    };

    public TickScriptCompileResult Compile(
        string requestedName,
        TickScriptKind requestedKind,
        string source)
    {
        var diagnostics = new List<TickScriptDiagnostic>();
        string name = requestedName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                0,
                0,
                "TLS1001",
                "Enter a script name."));
        }
        else if (name.Length > 80)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                0,
                0,
                "TLS1002",
                "Script name cannot exceed 80 characters."));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                1,
                1,
                "TLS1003",
                "The editor is empty."));
            return TickScriptCompileResult.Failed(name, requestedKind, diagnostics);
        }

        string[] lines = NormalizeNewLines(source).Split('\n');
        int declarationLineIndex = FindFirstCodeLine(lines);
        Match declaration = Match.Empty;

        if (declarationLineIndex < 0)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                1,
                1,
                "TLS1100",
                "Add a declaration such as indicator(...), strategy(...), scanner(...), drawing(...) or library(...)."));
        }
        else
        {
            declaration = DeclarationRegex.Match(lines[declarationLineIndex]);
            if (!declaration.Success)
            {
                diagnostics.Add(new TickScriptDiagnostic(
                    TickScriptDiagnosticSeverity.Error,
                    declarationLineIndex + 1,
                    1,
                    "TLS1101",
                    "The first code line must use the declaration that matches the selected script type."));
            }
            else
            {
                ValidateDeclaration(
                    requestedName,
                    requestedKind,
                    declaration,
                    declarationLineIndex + 1,
                    diagnostics);
            }
        }

        ValidateBalancedTokens(lines, diagnostics);
        IReadOnlyList<string> analysisLines = MaskCommentsAndStrings(lines);
        ValidateForbiddenTokens(analysisLines, diagnostics);

        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int outputCount = 0;
        int actionCount = 0;
        int inputCount = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string code = analysisLines[index];
            if (string.IsNullOrWhiteSpace(code))
                continue;

            inputCount += InputCallRegex.Matches(code).Count;
            foreach (Match call in FunctionCallRegex.Matches(code))
            {
                string function = call.Groups["name"].Value;
                functions.Add(function);

                if (RenderingFunctions.Contains(function))
                    outputCount++;
                if (StrategyFunctions.Contains(function))
                    actionCount++;

                if (!KnownFunctions.Contains(function))
                {
                    diagnostics.Add(new TickScriptDiagnostic(
                        TickScriptDiagnosticSeverity.Error,
                        index + 1,
                        call.Index + 1,
                        "TLS1200",
                        $"Unknown function '{function}'."));
                }
            }
        }

        if (requestedKind == TickScriptKind.Indicator && outputCount == 0)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                0,
                0,
                "TLS1300",
                "An indicator must draw at least one output using plot, plotshape, hline, barcolor, bgcolor or fill."));
        }

        if (requestedKind.IsTradingSimulation() && actionCount == 0)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                0,
                0,
                "TLS1301",
                "An EA/strategy must include at least one strategy.entry, strategy.exit, strategy.close or strategy.order action."));
        }

        if (requestedKind.IsTradingSimulation())
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Info,
                0,
                0,
                "TLS0002",
                "EA scripts compile for TickLab simulation and backtesting only; they cannot place live trades."));
        }

        bool success = diagnostics.All(item => item.Severity != TickScriptDiagnosticSeverity.Error);
        if (success)
        {
            diagnostics.Insert(0, new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Info,
                0,
                0,
                "TLS0001",
                $"Compile succeeded with {inputCount} input(s), {outputCount} chart output(s) and {actionCount} strategy action(s)."));
        }

        return new TickScriptCompileResult(
            success,
            name,
            requestedKind,
            diagnostics,
            functions.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            inputCount,
            outputCount,
            actionCount);
    }

    private static void ValidateDeclaration(
        string requestedName,
        TickScriptKind requestedKind,
        Match declaration,
        int line,
        ICollection<TickScriptDiagnostic> diagnostics)
    {
        string declaredKind = declaration.Groups["kind"].Value;
        TickScriptKind actualKind = Enum.GetValues<TickScriptKind>()
            .First(kind => string.Equals(kind.DeclarationName(), declaredKind, StringComparison.OrdinalIgnoreCase) ||
                           (kind == TickScriptKind.Scanner && string.Equals(declaredKind, "screener", StringComparison.OrdinalIgnoreCase)));

        if (actualKind != requestedKind)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                line,
                1,
                "TLS1102",
                $"The declaration is {actualKind.DisplayName()}, but the editor type is {requestedKind.DisplayName()}."));
        }

        string declaredName = Regex.Unescape(declaration.Groups["name"].Value);
        if (!string.Equals(declaredName.Trim(), requestedName.Trim(), StringComparison.Ordinal))
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Warning,
                line,
                1,
                "TLS1103",
                $"The declaration name '{declaredName}' differs from the save name '{requestedName.Trim()}'. The save name will be used."));
        }
    }

    private static void ValidateForbiddenTokens(
        IReadOnlyList<string> lines,
        ICollection<TickScriptDiagnostic> diagnostics)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string lower = lines[index].ToLowerInvariant();
            foreach (string forbidden in ForbiddenLiveTradingTokens)
            {
                int position = lower.IndexOf(forbidden, StringComparison.Ordinal);
                if (position < 0)
                    continue;

                diagnostics.Add(new TickScriptDiagnostic(
                    TickScriptDiagnosticSeverity.Error,
                    index + 1,
                    position + 1,
                    "TLS1400",
                    $"'{forbidden}' is blocked. TickLab scripts cannot access live-trading or external-process APIs."));
            }
        }
    }

    private static void ValidateBalancedTokens(
        IReadOnlyList<string> lines,
        ICollection<TickScriptDiagnostic> diagnostics)
    {
        var stack = new Stack<(char Token, int Line, int Column)>();
        bool inBlockComment = false;
        bool inString = false;
        bool escaped = false;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            string line = lines[lineIndex];
            for (int columnIndex = 0; columnIndex < line.Length; columnIndex++)
            {
                char current = line[columnIndex];
                char next = columnIndex + 1 < line.Length ? line[columnIndex + 1] : '\0';

                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        columnIndex++;
                    }
                    continue;
                }

                if (!inString && current == '/' && next == '*')
                {
                    inBlockComment = true;
                    columnIndex++;
                    continue;
                }

                if (!inString && current == '/' && next == '/')
                    break;

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (current == '"')
                        inString = false;
                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current is '(' or '[' or '{')
                {
                    stack.Push((current, lineIndex + 1, columnIndex + 1));
                    continue;
                }

                if (current is not (')' or ']' or '}'))
                    continue;

                if (stack.Count == 0)
                {
                    diagnostics.Add(new TickScriptDiagnostic(
                        TickScriptDiagnosticSeverity.Error,
                        lineIndex + 1,
                        columnIndex + 1,
                        "TLS1500",
                        $"Unexpected closing '{current}'."));
                    continue;
                }

                (char token, int openLine, int openColumn) = stack.Pop();
                if (!IsMatchingPair(token, current))
                {
                    diagnostics.Add(new TickScriptDiagnostic(
                        TickScriptDiagnosticSeverity.Error,
                        lineIndex + 1,
                        columnIndex + 1,
                        "TLS1501",
                        $"Closing '{current}' does not match '{token}' opened at line {openLine}, column {openColumn}."));
                }
            }
        }

        if (inString)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                lines.Count,
                Math.Max(1, lines[^1].Length),
                "TLS1502",
                "Unterminated string literal."));
        }

        if (inBlockComment)
        {
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                lines.Count,
                1,
                "TLS1503",
                "Unterminated block comment."));
        }

        while (stack.Count > 0)
        {
            (char token, int line, int column) = stack.Pop();
            diagnostics.Add(new TickScriptDiagnostic(
                TickScriptDiagnosticSeverity.Error,
                line,
                column,
                "TLS1504",
                $"Opening '{token}' is not closed."));
        }
    }

    private static IReadOnlyList<string> MaskCommentsAndStrings(
        IReadOnlyList<string> lines)
    {
        var masked = new string[lines.Count];
        bool inBlockComment = false;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            string line = lines[lineIndex];
            char[] output = line.ToCharArray();
            bool inString = false;
            bool escaped = false;

            for (int columnIndex = 0; columnIndex < line.Length; columnIndex++)
            {
                char current = line[columnIndex];
                char next = columnIndex + 1 < line.Length ? line[columnIndex + 1] : '\0';

                if (inBlockComment)
                {
                    output[columnIndex] = ' ';
                    if (current == '*' && next == '/')
                    {
                        output[columnIndex + 1] = ' ';
                        inBlockComment = false;
                        columnIndex++;
                    }
                    continue;
                }

                if (inString)
                {
                    output[columnIndex] = ' ';
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (current == '"')
                        inString = false;
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    for (int rest = columnIndex; rest < output.Length; rest++)
                        output[rest] = ' ';
                    break;
                }

                if (current == '/' && next == '*')
                {
                    output[columnIndex] = ' ';
                    output[columnIndex + 1] = ' ';
                    inBlockComment = true;
                    columnIndex++;
                    continue;
                }

                if (current == '"')
                {
                    output[columnIndex] = ' ';
                    inString = true;
                }
            }

            masked[lineIndex] = new string(output);
        }

        return masked;
    }

    private static int FindFirstCodeLine(IReadOnlyList<string> lines)
    {
        bool inBlockComment = false;
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Trim();
            if (inBlockComment)
            {
                if (line.Contains("*/", StringComparison.Ordinal))
                    inBlockComment = false;
                continue;
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                inBlockComment = !line.Contains("*/", StringComparison.Ordinal);
                continue;
            }

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            return index;
        }

        return -1;
    }

    private static bool IsMatchingPair(char open, char close) =>
        (open == '(' && close == ')') ||
        (open == '[' && close == ']') ||
        (open == '{' && close == '}');

    private static string NormalizeNewLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
             .Replace('\r', '\n');
}
