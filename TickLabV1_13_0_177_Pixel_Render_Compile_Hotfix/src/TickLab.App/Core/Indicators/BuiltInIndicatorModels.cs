using TickLab.Core.Settings;

namespace TickLab.Core.Indicators;

public enum BuiltInIndicatorCategory
{
    Trend,
    Oscillator,
    Volume,
    BillWilliams
}

public enum BuiltInIndicatorKind
{
    AdaptiveMovingAverage,
    AverageDirectionalMovementIndex,
    AverageDirectionalMovementIndexWilder,
    BollingerBands,
    DoubleExponentialMovingAverage,
    Envelopes,
    FractalAdaptiveMovingAverage,
    IchimokuKinkoHyo,
    MovingAverage,
    ParabolicSar,
    StandardDeviation,
    TripleExponentialMovingAverage,
    VariableIndexDynamicAverage,
    AverageTrueRange,
    BearsPower,
    BullsPower,
    ChaikinOscillator,
    CommodityChannelIndex,
    DeMarker,
    ForceIndex,
    Macd,
    Momentum,
    MovingAverageOfOscillator,
    RelativeStrengthIndex,
    RelativeVigorIndex,
    StochasticOscillator,
    Trix,
    WilliamsPercentRange,
    AccumulationDistribution,
    MoneyFlowIndex,
    OnBalanceVolume,
    Volumes,
    AcceleratorOscillator,
    Alligator,
    AwesomeOscillator,
    Fractals,
    GatorOscillator,
    MarketFacilitationIndex
}

public enum BuiltInIndicatorPlacement
{
    Overlay,
    SeparateWindow
}

public enum IndicatorParameterType
{
    Integer,
    Double,
    Choice,
    Boolean
}

public enum IndicatorSeriesDrawMode
{
    Line,
    Histogram,
    Dots,
    ArrowUp,
    ArrowDown,
    CloudBoundary
}

public enum IndicatorAppliedPrice
{
    Close,
    Open,
    High,
    Low,
    Median,
    Typical,
    WeightedClose,
    FirstIndicatorData,
    PreviousIndicatorData
}

public enum IndicatorMaMethod
{
    Simple,
    Exponential,
    Smoothed,
    LinearWeighted
}

public enum IndicatorVolumeMode
{
    TickVolume,
    RealVolume
}

public sealed record IndicatorParameterDefinition(
    string Key,
    string Label,
    IndicatorParameterType Type,
    double Minimum = double.MinValue,
    double Maximum = double.MaxValue,
    double Increment = 1.0,
    IReadOnlyList<string>? Choices = null);

public sealed record IndicatorStyleSetting
{
    public string SeriesKey { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Color { get; init; } = "#5B86C4";
    public string NegativeColor { get; init; } = "#DF5C68";
    public string FillColor { get; init; } = "#5B86C4";
    public double FillOpacity { get; init; } = 0.14;
    public string LabelColor { get; init; } = "#D8D8D8";
    public double Width { get; init; } = 1.0;
    public ChartLineStyle LineStyle { get; init; } = ChartLineStyle.Solid;
    public IndicatorSeriesDrawMode DrawMode { get; init; } = IndicatorSeriesDrawMode.Line;
    public bool ColorBySlope { get; init; }
    public bool ColorBySign { get; init; }
    public bool Visible { get; init; } = true;
}

public sealed record IndicatorLevelSetting
{
    public double Value { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Color { get; init; } = "#808080";
    public double Width { get; init; } = 1.0;
    public ChartLineStyle LineStyle { get; init; } = ChartLineStyle.Dashed;
}

public sealed record BuiltInIndicatorInstance
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string LinkedGroupId { get; init; } = string.Empty;
    public BuiltInIndicatorKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Dictionary<string, double> NumericParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TextParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> BooleanParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<IndicatorStyleSetting> Styles { get; init; } = Array.Empty<IndicatorStyleSetting>();
    public IReadOnlyList<IndicatorLevelSetting> Levels { get; init; } = Array.Empty<IndicatorLevelSetting>();
    public bool UseFixedMinimum { get; init; }
    public bool UseFixedMaximum { get; init; }
    public double FixedMinimum { get; init; }
    public double FixedMaximum { get; init; }
    public bool ShowDataWindowValues { get; init; } = true;
    public bool VisibleOnAllTimeframes { get; init; } = true;
    public IReadOnlyList<string> VisibleTimeframes { get; init; } = Array.Empty<string>();

    public int Int(string key, int fallback) =>
        NumericParameters.TryGetValue(key, out double value)
            ? Math.Max(1, (int)Math.Round(value))
            : fallback;

    public int IntAllowZero(string key, int fallback) =>
        NumericParameters.TryGetValue(key, out double value)
            ? Math.Max(0, (int)Math.Round(value))
            : fallback;

    public double Number(string key, double fallback) =>
        NumericParameters.TryGetValue(key, out double value) && double.IsFinite(value)
            ? value
            : fallback;

    public string Text(string key, string fallback) =>
        TextParameters.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    public bool Flag(string key, bool fallback) =>
        BooleanParameters.TryGetValue(key, out bool value) ? value : fallback;
}

public sealed record BuiltInIndicatorDefinition(
    BuiltInIndicatorKind Kind,
    string Name,
    BuiltInIndicatorCategory Category,
    BuiltInIndicatorPlacement Placement,
    IReadOnlyList<IndicatorParameterDefinition> Parameters,
    Func<BuiltInIndicatorInstance> CreateDefault);

public sealed record IndicatorSeriesResult(
    string Key,
    string Label,
    IReadOnlyList<double?> Values,
    IndicatorStyleSetting Style,
    int Shift = 0,
    string? FillToSeriesKey = null);

public sealed record BuiltInIndicatorResult(
    string InstanceId,
    BuiltInIndicatorKind Kind,
    string Name,
    BuiltInIndicatorPlacement Placement,
    IReadOnlyList<IndicatorSeriesResult> Series,
    IReadOnlyList<IndicatorLevelSetting> Levels,
    double? FixedMinimum,
    double? FixedMaximum,
    string Description);
