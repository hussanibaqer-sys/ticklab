using TickLab.Core.Settings;

namespace TickLab.Core.Indicators;

public static class BuiltInIndicatorCatalog
{
    private static readonly string[] AppliedPriceChoices = Enum.GetNames<IndicatorAppliedPrice>();
    private static readonly string[] MaMethodChoices = Enum.GetNames<IndicatorMaMethod>();
    private static readonly string[] VolumeChoices = Enum.GetNames<IndicatorVolumeMode>();

    private static readonly IReadOnlyList<BuiltInIndicatorDefinition> DefinitionsInternal = Build();

    public static IReadOnlyList<BuiltInIndicatorDefinition> Definitions => DefinitionsInternal;

    public static BuiltInIndicatorDefinition Find(BuiltInIndicatorKind kind) =>
        DefinitionsInternal.First(item => item.Kind == kind);

    public static BuiltInIndicatorInstance CreateDefault(BuiltInIndicatorKind kind) =>
        Find(kind).CreateDefault();

    public static string CategoryLabel(BuiltInIndicatorCategory category) => category switch
    {
        BuiltInIndicatorCategory.Trend => "Trend",
        BuiltInIndicatorCategory.Oscillator => "Oscillators",
        BuiltInIndicatorCategory.Volume => "Volumes",
        BuiltInIndicatorCategory.BillWilliams => "Bill Williams",
        _ => category.ToString()
    };

    private static IReadOnlyList<BuiltInIndicatorDefinition> Build()
    {
        var list = new List<BuiltInIndicatorDefinition>();

        void Add(
            BuiltInIndicatorKind kind,
            string name,
            BuiltInIndicatorCategory category,
            BuiltInIndicatorPlacement placement,
            IReadOnlyList<IndicatorParameterDefinition> parameters,
            Dictionary<string, double>? numbers = null,
            Dictionary<string, string>? texts = null,
            Dictionary<string, bool>? flags = null,
            IReadOnlyList<IndicatorStyleSetting>? styles = null,
            IReadOnlyList<IndicatorLevelSetting>? levels = null,
            double? fixedMinimum = null,
            double? fixedMaximum = null)
        {
            list.Add(new BuiltInIndicatorDefinition(
                kind,
                name,
                category,
                placement,
                parameters,
                () => new BuiltInIndicatorInstance
                {
                    Kind = kind,
                    DisplayName = name,
                    NumericParameters = numbers is null
                        ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, double>(numbers, StringComparer.OrdinalIgnoreCase),
                    TextParameters = texts is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(texts, StringComparer.OrdinalIgnoreCase),
                    BooleanParameters = flags is null
                        ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, bool>(flags, StringComparer.OrdinalIgnoreCase),
                    Styles = styles?.Select(item => item with { }).ToArray() ?? Array.Empty<IndicatorStyleSetting>(),
                    Levels = levels?.Select(item => item with { }).ToArray() ?? Array.Empty<IndicatorLevelSetting>(),
                    UseFixedMinimum = fixedMinimum.HasValue,
                    FixedMinimum = fixedMinimum ?? 0,
                    UseFixedMaximum = fixedMaximum.HasValue,
                    FixedMaximum = fixedMaximum ?? 0
                }));
        }

        IndicatorParameterDefinition Int(string key, string label, int min = 1, int max = 100000) =>
            new(key, label, IndicatorParameterType.Integer, min, max, 1);
        IndicatorParameterDefinition Number(string key, string label, double min, double max, double step) =>
            new(key, label, IndicatorParameterType.Double, min, max, step);
        IndicatorParameterDefinition Choice(string key, string label, IReadOnlyList<string> choices) =>
            new(key, label, IndicatorParameterType.Choice, Choices: choices);
        IndicatorStyleSetting Line(string key, string label, string color, double width = 1.0, ChartLineStyle lineStyle = ChartLineStyle.Solid) =>
            new() { SeriesKey = key, Label = label, Color = color, FillColor = color, Width = width, LineStyle = lineStyle, DrawMode = IndicatorSeriesDrawMode.Line };
        IndicatorStyleSetting Hist(string key, string label, string positive, string negative, bool bySlope = false, bool bySign = true) =>
            new() { SeriesKey = key, Label = label, Color = positive, NegativeColor = negative, FillColor = positive, Width = 2.0, DrawMode = IndicatorSeriesDrawMode.Histogram, ColorBySlope = bySlope, ColorBySign = bySign };
        IndicatorStyleSetting Dot(string key, string label, string color) =>
            new() { SeriesKey = key, Label = label, Color = color, FillColor = color, Width = 2.0, DrawMode = IndicatorSeriesDrawMode.Dots };
        IndicatorStyleSetting Arrow(string key, string label, string color, bool up) =>
            new() { SeriesKey = key, Label = label, Color = color, FillColor = color, Width = 2.0, DrawMode = up ? IndicatorSeriesDrawMode.ArrowUp : IndicatorSeriesDrawMode.ArrowDown };
        IndicatorLevelSetting Level(double value, string label = "") =>
            new() { Value = value, Label = label, Color = "#808080", Width = 1, LineStyle = ChartLineStyle.Dashed };

        IReadOnlyList<IndicatorParameterDefinition> PricePeriodShift(params IndicatorParameterDefinition[] extra) =>
            new[] { Int("Period", "Period", 1, 100000), Int("Shift", "Shift", 0, 10000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }.Concat(extra).ToArray();
        IReadOnlyList<IndicatorParameterDefinition> MaPeriodShift(params IndicatorParameterDefinition[] extra) =>
            new[] { Int("Period", "Period", 1, 100000), Int("Shift", "Shift", 0, 10000), Choice("Method", "MA method", MaMethodChoices), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }.Concat(extra).ToArray();

        Add(BuiltInIndicatorKind.AdaptiveMovingAverage, "Adaptive Moving Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            PricePeriodShift(Int("FastPeriod", "Fast EMA period", 1, 10000), Int("SlowPeriod", "Slow EMA period", 2, 10000)),
            new() { ["Period"] = 10, ["FastPeriod"] = 2, ["SlowPeriod"] = 30, ["Shift"] = 0 },
            new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("ama", "AMA", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.AverageDirectionalMovementIndex, "Average Directional Movement Index", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "ADX period", 1, 100000) }, new() { ["Period"] = 14 },
            styles: new[] { Line("adx", "ADX", "#FFFFFF", 1.3), Line("plus", "+DI", "#53CFA4"), Line("minus", "-DI", "#F07B85") });
        Add(BuiltInIndicatorKind.AverageDirectionalMovementIndexWilder, "Average Directional Movement Index Wilder", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "ADX period", 1, 100000) }, new() { ["Period"] = 14 },
            styles: new[] { Line("adx", "ADX Wilder", "#FFFFFF", 1.3), Line("plus", "+DI", "#53CFA4"), Line("minus", "-DI", "#F07B85") });
        Add(BuiltInIndicatorKind.BollingerBands, "Bollinger Bands", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            PricePeriodShift(Number("Deviation", "Deviations", 0.01, 100, 0.1)),
            new() { ["Period"] = 20, ["Shift"] = 0, ["Deviation"] = 2.0 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() },
            styles: new[] { Line("middle", "Middle", "#A0A0A0"), Line("upper", "Upper", "#4AA3FF"), Line("lower", "Lower", "#4AA3FF") });
        Add(BuiltInIndicatorKind.DoubleExponentialMovingAverage, "Double Exponential Moving Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            PricePeriodShift(), new() { ["Period"] = 14, ["Shift"] = 0 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("dema", "DEMA", "#FFB000", 1.5) });
        Add(BuiltInIndicatorKind.Envelopes, "Envelopes", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            MaPeriodShift(Number("Deviation", "Deviation (%)", 0.001, 100, 0.1)),
            new() { ["Period"] = 14, ["Shift"] = 0, ["Deviation"] = 0.1 },
            new() { ["Method"] = IndicatorMaMethod.Simple.ToString(), ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() },
            styles: new[] { Line("upper", "Upper", "#4AA3FF"), Line("lower", "Lower", "#F07B85") });
        Add(BuiltInIndicatorKind.FractalAdaptiveMovingAverage, "Fractal Adaptive Moving Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            PricePeriodShift(), new() { ["Period"] = 14, ["Shift"] = 0 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("frama", "FRAMA", "#B26CFF", 1.5) });
        Add(BuiltInIndicatorKind.IchimokuKinkoHyo, "Ichimoku Kinko Hyo", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            new[] { Int("Tenkan", "Tenkan-sen", 1, 100000), Int("Kijun", "Kijun-sen", 1, 100000), Int("SenkouB", "Senkou Span B", 1, 100000) },
            new() { ["Tenkan"] = 9, ["Kijun"] = 26, ["SenkouB"] = 52 },
            styles: new[]
            {
                Line("tenkan", "Tenkan-sen", "#0078D7"), Line("kijun", "Kijun-sen", "#C00000"),
                Line("senkouA", "Senkou Span A", "#53D98A"), Line("senkouB", "Senkou Span B", "#E0647D"),
                Line("chikou", "Chikou Span", "#808080")
            });
        Add(BuiltInIndicatorKind.MovingAverage, "Moving Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            MaPeriodShift(), new() { ["Period"] = 14, ["Shift"] = 0 }, new() { ["Method"] = IndicatorMaMethod.Simple.ToString(), ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("ma", "Moving Average", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.ParabolicSar, "Parabolic SAR", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            new[] { Number("Step", "Step", 0.0001, 1.0, 0.01), Number("Maximum", "Maximum", 0.001, 10, 0.01) }, new() { ["Step"] = 0.02, ["Maximum"] = 0.2 }, styles: new[] { Dot("sar", "SAR", "#00C8C8") });
        Add(BuiltInIndicatorKind.StandardDeviation, "Standard Deviation", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.SeparateWindow,
            MaPeriodShift(), new() { ["Period"] = 20, ["Shift"] = 0 }, new() { ["Method"] = IndicatorMaMethod.Simple.ToString(), ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("stddev", "Standard Deviation", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.TripleExponentialMovingAverage, "Triple Exponential Moving Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            PricePeriodShift(), new() { ["Period"] = 14, ["Shift"] = 0 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("tema", "TEMA", "#B26CFF", 1.5) });
        Add(BuiltInIndicatorKind.VariableIndexDynamicAverage, "Variable Index Dynamic Average", BuiltInIndicatorCategory.Trend, BuiltInIndicatorPlacement.Overlay,
            new[] { Int("CmoPeriod", "CMO period", 1, 100000), Int("EmaPeriod", "EMA period", 1, 100000), Int("Shift", "Shift", 0, 10000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) },
            new() { ["CmoPeriod"] = 9, ["EmaPeriod"] = 12, ["Shift"] = 0 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("vidya", "VIDYA", "#FF8C00", 1.5) });

        Add(BuiltInIndicatorKind.AverageTrueRange, "Average True Range", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 14 }, styles: new[] { Line("atr", "ATR", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.BearsPower, "Bears Power", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 13 }, styles: new[] { Hist("bears", "Bears Power", "#53CFA4", "#F07B85", bySign: true) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.BullsPower, "Bulls Power", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 13 }, styles: new[] { Hist("bulls", "Bulls Power", "#53CFA4", "#F07B85", bySign: true) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.ChaikinOscillator, "Chaikin Oscillator", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("FastPeriod", "Fast MA", 1, 100000), Int("SlowPeriod", "Slow MA", 2, 100000), Choice("Method", "MA method", MaMethodChoices), Choice("Volume", "Volumes", VolumeChoices) },
            new() { ["FastPeriod"] = 3, ["SlowPeriod"] = 10 }, new() { ["Method"] = IndicatorMaMethod.Exponential.ToString(), ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Line("chaikin", "Chaikin", "#4AA3FF", 1.5) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.CommodityChannelIndex, "Commodity Channel Index", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }, new() { ["Period"] = 14 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Typical.ToString() }, styles: new[] { Line("cci", "CCI", "#4AA3FF", 1.5) }, levels: new[] { Level(100), Level(-100) });
        Add(BuiltInIndicatorKind.DeMarker, "DeMarker", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 14 }, styles: new[] { Line("demarker", "DeMarker", "#4AA3FF", 1.5) }, levels: new[] { Level(0.7), Level(0.3) }, fixedMinimum: 0, fixedMaximum: 1);
        Add(BuiltInIndicatorKind.ForceIndex, "Force Index", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("Method", "MA method", MaMethodChoices), Choice("Volume", "Volumes", VolumeChoices) }, new() { ["Period"] = 13 }, new() { ["Method"] = IndicatorMaMethod.Exponential.ToString(), ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Line("force", "Force Index", "#4AA3FF", 1.5) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.Macd, "MACD", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("FastPeriod", "Fast EMA", 1, 100000), Int("SlowPeriod", "Slow EMA", 2, 100000), Int("SignalPeriod", "Signal SMA", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) },
            new() { ["FastPeriod"] = 12, ["SlowPeriod"] = 26, ["SignalPeriod"] = 9 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Hist("main", "MACD", "#53CFA4", "#F07B85", bySign: true), Line("signal", "Signal", "#FF0000") }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.Momentum, "Momentum", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }, new() { ["Period"] = 14 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("momentum", "Momentum", "#4AA3FF", 1.5) }, levels: new[] { Level(100) });
        Add(BuiltInIndicatorKind.MovingAverageOfOscillator, "Moving Average of Oscillator", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("FastPeriod", "Fast EMA", 1, 100000), Int("SlowPeriod", "Slow EMA", 2, 100000), Int("SignalPeriod", "Signal SMA", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) },
            new() { ["FastPeriod"] = 12, ["SlowPeriod"] = 26, ["SignalPeriod"] = 9 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Hist("osma", "OsMA", "#53CFA4", "#F07B85", bySign: true) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.RelativeStrengthIndex, "Relative Strength Index", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }, new() { ["Period"] = 14 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("rsi", "RSI", "#4AA3FF", 1.5) }, levels: new[] { Level(70), Level(30) }, fixedMinimum: 0, fixedMaximum: 100);
        Add(BuiltInIndicatorKind.RelativeVigorIndex, "Relative Vigor Index", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 10 }, styles: new[] { Line("main", "RVI", "#53CFA4", 1.5), Line("signal", "Signal", "#F07B85") }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.StochasticOscillator, "Stochastic Oscillator", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("KPeriod", "%K period", 1, 100000), Int("DPeriod", "%D period", 1, 100000), Int("Slowing", "Slowing", 1, 100000), Choice("Method", "MA method", MaMethodChoices), Choice("PriceField", "Price field", new[] { "LowHigh", "CloseClose" }) },
            new() { ["KPeriod"] = 5, ["DPeriod"] = 3, ["Slowing"] = 3 }, new() { ["Method"] = IndicatorMaMethod.Simple.ToString(), ["PriceField"] = "LowHigh" }, styles: new[] { Line("main", "%K", "#53CFA4", 1.5), Line("signal", "%D", "#F07B85", 1.0, ChartLineStyle.Dotted) }, levels: new[] { Level(80), Level(20) }, fixedMinimum: 0, fixedMaximum: 100);
        Add(BuiltInIndicatorKind.Trix, "Triple Exponential Average", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) }, new() { ["Period"] = 14 }, new() { ["AppliedPrice"] = IndicatorAppliedPrice.Close.ToString() }, styles: new[] { Line("trix", "TRIX", "#4AA3FF", 1.5) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.WilliamsPercentRange, "Williams' Percent Range", BuiltInIndicatorCategory.Oscillator, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000) }, new() { ["Period"] = 14 }, styles: new[] { Line("wpr", "Williams %R", "#4AA3FF", 1.5) }, levels: new[] { Level(-20), Level(-80) }, fixedMinimum: -100, fixedMaximum: 0);

        Add(BuiltInIndicatorKind.AccumulationDistribution, "Accumulation/Distribution", BuiltInIndicatorCategory.Volume, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Choice("Volume", "Volumes", VolumeChoices) }, texts: new() { ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Line("ad", "A/D", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.MoneyFlowIndex, "Money Flow Index", BuiltInIndicatorCategory.Volume, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("Period", "Period", 1, 100000), Choice("Volume", "Volumes", VolumeChoices) }, new() { ["Period"] = 14 }, new() { ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Line("mfi", "MFI", "#4AA3FF", 1.5) }, levels: new[] { Level(80), Level(20) }, fixedMinimum: 0, fixedMaximum: 100);
        Add(BuiltInIndicatorKind.OnBalanceVolume, "On Balance Volume", BuiltInIndicatorCategory.Volume, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Choice("Volume", "Volumes", VolumeChoices) }, texts: new() { ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Line("obv", "OBV", "#4AA3FF", 1.5) });
        Add(BuiltInIndicatorKind.Volumes, "Volumes", BuiltInIndicatorCategory.Volume, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Choice("Volume", "Volumes", VolumeChoices) }, texts: new() { ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() }, styles: new[] { Hist("volume", "Volumes", "#53CFA4", "#F07B85", bySlope: true, bySign: false) }, fixedMinimum: 0);

        Add(BuiltInIndicatorKind.AcceleratorOscillator, "Accelerator Oscillator", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.SeparateWindow,
            Array.Empty<IndicatorParameterDefinition>(), styles: new[] { Hist("ac", "AC", "#53CFA4", "#F07B85", bySlope: true, bySign: false) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.Alligator, "Alligator", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.Overlay,
            new[] { Int("JawPeriod", "Jaws period", 1, 100000), Int("JawShift", "Jaws shift", 0, 10000), Int("TeethPeriod", "Teeth period", 1, 100000), Int("TeethShift", "Teeth shift", 0, 10000), Int("LipsPeriod", "Lips period", 1, 100000), Int("LipsShift", "Lips shift", 0, 10000), Choice("Method", "MA method", MaMethodChoices), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) },
            new() { ["JawPeriod"] = 13, ["JawShift"] = 8, ["TeethPeriod"] = 8, ["TeethShift"] = 5, ["LipsPeriod"] = 5, ["LipsShift"] = 3 }, new() { ["Method"] = IndicatorMaMethod.Smoothed.ToString(), ["AppliedPrice"] = IndicatorAppliedPrice.Median.ToString() },
            styles: new[] { Line("jaw", "Jaws", "#0078D7", 1.5), Line("teeth", "Teeth", "#C00000", 1.5), Line("lips", "Lips", "#00C853", 1.5) });
        Add(BuiltInIndicatorKind.AwesomeOscillator, "Awesome Oscillator", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.SeparateWindow,
            Array.Empty<IndicatorParameterDefinition>(), styles: new[] { Hist("ao", "AO", "#53CFA4", "#F07B85", bySlope: true, bySign: false) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.Fractals, "Fractals", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.Overlay,
            Array.Empty<IndicatorParameterDefinition>(), styles: new[] { Arrow("up", "Up Fractal", "#53CFA4", true), Arrow("down", "Down Fractal", "#F07B85", false) });
        Add(BuiltInIndicatorKind.GatorOscillator, "Gator Oscillator", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Int("JawPeriod", "Jaws period", 1, 100000), Int("JawShift", "Jaws shift", 0, 10000), Int("TeethPeriod", "Teeth period", 1, 100000), Int("TeethShift", "Teeth shift", 0, 10000), Int("LipsPeriod", "Lips period", 1, 100000), Int("LipsShift", "Lips shift", 0, 10000), Choice("Method", "MA method", MaMethodChoices), Choice("AppliedPrice", "Apply to", AppliedPriceChoices) },
            new() { ["JawPeriod"] = 13, ["JawShift"] = 8, ["TeethPeriod"] = 8, ["TeethShift"] = 5, ["LipsPeriod"] = 5, ["LipsShift"] = 3 }, new() { ["Method"] = IndicatorMaMethod.Smoothed.ToString(), ["AppliedPrice"] = IndicatorAppliedPrice.Median.ToString() },
            styles: new[] { Hist("upper", "Jaws–Teeth", "#53CFA4", "#F07B85", bySlope: true, bySign: false), Hist("lower", "Teeth–Lips", "#53CFA4", "#F07B85", bySlope: true, bySign: false) }, levels: new[] { Level(0) });
        Add(BuiltInIndicatorKind.MarketFacilitationIndex, "Market Facilitation Index", BuiltInIndicatorCategory.BillWilliams, BuiltInIndicatorPlacement.SeparateWindow,
            new[] { Choice("Volume", "Volumes", VolumeChoices) }, texts: new() { ["Volume"] = IndicatorVolumeMode.TickVolume.ToString() },
            styles: new[]
            {
                new IndicatorStyleSetting { SeriesKey = "mfi", Label = "BW MFI", Color = "#00C853", NegativeColor = "#F07B85", Width = 3, DrawMode = IndicatorSeriesDrawMode.Histogram, ColorBySlope = true }
            }, fixedMinimum: 0);

        return list;
    }
}
