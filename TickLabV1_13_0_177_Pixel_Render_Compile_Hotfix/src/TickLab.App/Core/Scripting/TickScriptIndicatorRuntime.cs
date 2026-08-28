using System.Globalization;
using System.Text.RegularExpressions;
using TickLab.Core.Market;

namespace TickLab.Core.Scripting;

public sealed record TickScriptIndicatorResult(string Name, bool Overlay, IReadOnlyList<double?> Values,
    double? HorizontalUpper, double? HorizontalLower, string Description);

public static class TickScriptIndicatorRuntime
{
    private static readonly Regex IndicatorRegex = new("indicator\\(\\s*\"(?<name>[^\"]+)\"(?<args>[^)]*)\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RsiRegex = new("rsi\\(\\s*(?<source>close|open|high|low)\\s*,\\s*(?<length>[A-Za-z_][A-Za-z0-9_]*|[0-9]+)\\s*\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MaRegex = new("(?<kind>sma|ema)\\(\\s*(?<source>close|open|high|low)\\s*,\\s*(?<length>[A-Za-z_][A-Za-z0-9_]*|[0-9]+)\\s*\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InputRegex = new(
        "(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*input\\.(?:int|float)\\(\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HlineRegex = new(
        "hline\\(\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?|[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TickScriptIndicatorResult Evaluate(string name, string source, IReadOnlyList<Candle> candles)
    {
        var inputs = InputRegex.Matches(source).Cast<Match>().ToDictionary(
            match => match.Groups["name"].Value,
            match => double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase);
        Match declaration = IndicatorRegex.Match(source);
        bool overlay = declaration.Success && declaration.Groups["args"].Value.Contains("overlay=true", StringComparison.OrdinalIgnoreCase);
        string displayName = declaration.Success ? declaration.Groups["name"].Value : name;
        double[] values = candles.Select(c => c.Close).ToArray();
        string description;

        Match rsi = RsiRegex.Match(source);
        if (rsi.Success)
        {
            int length = ResolveLength(rsi.Groups["length"].Value, inputs, 14);
            values = ComputeRsi(GetSource(candles, rsi.Groups["source"].Value), length);
            overlay = false;
            description = $"RSI ({length})";
        }
        else
        {
            Match ma = MaRegex.Match(source);
            if (ma.Success)
            {
                int length = ResolveLength(ma.Groups["length"].Value, inputs, 20);
                double[] sourceValues = GetSource(candles, ma.Groups["source"].Value);
                values = string.Equals(ma.Groups["kind"].Value, "ema", StringComparison.OrdinalIgnoreCase)
                    ? ComputeEma(sourceValues, length) : ComputeSma(sourceValues, length);
                description = $"{ma.Groups["kind"].Value.ToUpperInvariant()} ({length})";
            }
            else
            {
                description = "Close series preview";
            }
        }

        double? upper = null, lower = null;
        double[] levels = HlineRegex.Matches(source).Cast<Match>()
            .Select(match => ResolveNumber(match.Groups["value"].Value, inputs))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Take(2)
            .ToArray();
        if (levels.Length > 0) upper = levels[0];
        if (levels.Length > 1) lower = levels[1];
        return new TickScriptIndicatorResult(displayName, overlay,
            values.Select(v => double.IsNaN(v) ? (double?)null : v).ToArray(), upper, lower, description);
    }

    private static int ResolveLength(string token, IReadOnlyDictionary<string, double> inputs, int fallback) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int literal)
            ? Math.Max(1, literal)
            : inputs.TryGetValue(token, out double value)
                ? Math.Max(1, (int)Math.Round(value))
                : fallback;

    private static double? ResolveNumber(string token, IReadOnlyDictionary<string, double> inputs) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal)
            ? literal
            : inputs.TryGetValue(token, out double value)
                ? value
                : null;
    private static double[] GetSource(IReadOnlyList<Candle> candles, string source) => candles.Select(c => source.ToLowerInvariant() switch
    { "open" => c.Open, "high" => c.High, "low" => c.Low, _ => c.Close }).ToArray();

    private static double[] ComputeSma(double[] source, int length)
    {
        var output = Enumerable.Repeat(double.NaN, source.Length).ToArray();
        double sum = 0;
        for (int i=0;i<source.Length;i++) { sum += source[i]; if(i>=length) sum -= source[i-length]; if(i>=length-1) output[i]=sum/length; }
        return output;
    }
    private static double[] ComputeEma(double[] source, int length)
    {
        var output = Enumerable.Repeat(double.NaN, source.Length).ToArray(); if(source.Length==0) return output;
        double alpha=2.0/(length+1.0), ema=source[0]; output[0]=ema;
        for(int i=1;i<source.Length;i++){ ema=alpha*source[i]+(1-alpha)*ema; output[i]=ema; }
        return output;
    }
    private static double[] ComputeRsi(double[] source, int length)
    {
        var output = Enumerable.Repeat(double.NaN, source.Length).ToArray(); if(source.Length<=length) return output;
        double gain=0, loss=0;
        for(int i=1;i<=length;i++){ double d=source[i]-source[i-1]; if(d>=0) gain+=d; else loss-=d; }
        gain/=length; loss/=length; output[length]=loss==0?100:100-(100/(1+gain/loss));
        for(int i=length+1;i<source.Length;i++){ double d=source[i]-source[i-1]; double g=Math.Max(0,d), l=Math.Max(0,-d); gain=(gain*(length-1)+g)/length; loss=(loss*(length-1)+l)/length; output[i]=loss==0?100:100-(100/(1+gain/loss)); }
        return output;
    }
}
