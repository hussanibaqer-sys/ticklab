using TickLab.Core.Market;

namespace TickLab.Core.Indicators;

public static class BuiltInIndicatorEngine
{
    public static BuiltInIndicatorResult Evaluate(
        BuiltInIndicatorInstance instance,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<double?>? firstIndicatorData = null,
        IReadOnlyList<double?>? previousIndicatorData = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(candles);
        BuiltInIndicatorDefinition definition = BuiltInIndicatorCatalog.Find(instance.Kind);
        double[] source = GetSource(instance, candles, firstIndicatorData, previousIndicatorData);
        List<IndicatorSeriesResult> series = instance.Kind switch
        {
            BuiltInIndicatorKind.AdaptiveMovingAverage => Ama(instance, source),
            BuiltInIndicatorKind.AverageDirectionalMovementIndex => Adx(instance, candles, wilder: false),
            BuiltInIndicatorKind.AverageDirectionalMovementIndexWilder => Adx(instance, candles, wilder: true),
            BuiltInIndicatorKind.BollingerBands => Bollinger(instance, source),
            BuiltInIndicatorKind.DoubleExponentialMovingAverage => Dema(instance, source),
            BuiltInIndicatorKind.Envelopes => Envelopes(instance, source),
            BuiltInIndicatorKind.FractalAdaptiveMovingAverage => Frama(instance, candles, source),
            BuiltInIndicatorKind.IchimokuKinkoHyo => Ichimoku(instance, candles),
            BuiltInIndicatorKind.MovingAverage => MovingAverage(instance, source),
            BuiltInIndicatorKind.ParabolicSar => ParabolicSar(instance, candles),
            BuiltInIndicatorKind.StandardDeviation => StandardDeviation(instance, source),
            BuiltInIndicatorKind.TripleExponentialMovingAverage => Tema(instance, source),
            BuiltInIndicatorKind.VariableIndexDynamicAverage => Vidya(instance, source),
            BuiltInIndicatorKind.AverageTrueRange => Atr(instance, candles),
            BuiltInIndicatorKind.BearsPower => BearsPower(instance, candles),
            BuiltInIndicatorKind.BullsPower => BullsPower(instance, candles),
            BuiltInIndicatorKind.ChaikinOscillator => Chaikin(instance, candles),
            BuiltInIndicatorKind.CommodityChannelIndex => Cci(instance, source),
            BuiltInIndicatorKind.DeMarker => DeMarker(instance, candles),
            BuiltInIndicatorKind.ForceIndex => ForceIndex(instance, candles),
            BuiltInIndicatorKind.Macd => Macd(instance, source, osmaOnly: false),
            BuiltInIndicatorKind.Momentum => Momentum(instance, source),
            BuiltInIndicatorKind.MovingAverageOfOscillator => Macd(instance, source, osmaOnly: true),
            BuiltInIndicatorKind.RelativeStrengthIndex => Rsi(instance, source),
            BuiltInIndicatorKind.RelativeVigorIndex => Rvi(instance, candles),
            BuiltInIndicatorKind.StochasticOscillator => Stochastic(instance, candles),
            BuiltInIndicatorKind.Trix => Trix(instance, source),
            BuiltInIndicatorKind.WilliamsPercentRange => WilliamsR(instance, candles),
            BuiltInIndicatorKind.AccumulationDistribution => AccumulationDistribution(instance, candles),
            BuiltInIndicatorKind.MoneyFlowIndex => MoneyFlowIndex(instance, candles),
            BuiltInIndicatorKind.OnBalanceVolume => OnBalanceVolume(instance, candles),
            BuiltInIndicatorKind.Volumes => Volumes(instance, candles),
            BuiltInIndicatorKind.AcceleratorOscillator => Accelerator(instance, candles),
            BuiltInIndicatorKind.Alligator => Alligator(instance, candles),
            BuiltInIndicatorKind.AwesomeOscillator => Awesome(instance, candles),
            BuiltInIndicatorKind.Fractals => Fractals(instance, candles),
            BuiltInIndicatorKind.GatorOscillator => Gator(instance, candles),
            BuiltInIndicatorKind.MarketFacilitationIndex => MarketFacilitation(instance, candles),
            _ => new List<IndicatorSeriesResult>()
        };

        double? minimum = instance.UseFixedMinimum ? instance.FixedMinimum : null;
        double? maximum = instance.UseFixedMaximum ? instance.FixedMaximum : null;
        return new BuiltInIndicatorResult(
            instance.InstanceId,
            instance.Kind,
            string.IsNullOrWhiteSpace(instance.DisplayName) ? definition.Name : instance.DisplayName,
            definition.Placement,
            series,
            instance.Levels,
            minimum,
            maximum,
            BuildDescription(instance));
    }

    private static string BuildDescription(BuiltInIndicatorInstance instance)
    {
        if (instance.NumericParameters.Count == 0)
            return BuiltInIndicatorCatalog.Find(instance.Kind).Name;
        string parameters = string.Join(", ", instance.NumericParameters
            .OrderBy(item => item.Key)
            .Select(item => $"{item.Key}={item.Value:0.####}"));
        return $"{BuiltInIndicatorCatalog.Find(instance.Kind).Name} ({parameters})";
    }

    private static IndicatorStyleSetting Style(BuiltInIndicatorInstance instance, string key) =>
        instance.Styles.FirstOrDefault(item => string.Equals(item.SeriesKey, key, StringComparison.OrdinalIgnoreCase))
        ?? new IndicatorStyleSetting { SeriesKey = key, Label = key };

    private static IndicatorSeriesResult Series(
        BuiltInIndicatorInstance instance,
        string key,
        string label,
        double[] values,
        int shift = 0,
        string? fillTo = null) =>
        new(key, label, ToNullable(values), Style(instance, key), shift, fillTo);

    private static IReadOnlyList<double?> ToNullable(double[] values) =>
        values.Select(value => double.IsFinite(value) ? (double?)value : null).ToArray();

    private static double[] Empty(int count) => Enumerable.Repeat(double.NaN, count).ToArray();

    private static double[] GetSource(
        BuiltInIndicatorInstance instance,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<double?>? firstIndicatorData,
        IReadOnlyList<double?>? previousIndicatorData)
    {
        IndicatorAppliedPrice applied = ParseEnum(instance.Text("AppliedPrice", IndicatorAppliedPrice.Close.ToString()), IndicatorAppliedPrice.Close);
        if (applied == IndicatorAppliedPrice.FirstIndicatorData && firstIndicatorData is not null)
            return NormalizeExternal(firstIndicatorData, candles.Count, candles);
        if (applied == IndicatorAppliedPrice.PreviousIndicatorData && previousIndicatorData is not null)
            return NormalizeExternal(previousIndicatorData, candles.Count, candles);
        return candles.Select(candle => applied switch
        {
            IndicatorAppliedPrice.Open => candle.Open,
            IndicatorAppliedPrice.High => candle.High,
            IndicatorAppliedPrice.Low => candle.Low,
            IndicatorAppliedPrice.Median => (candle.High + candle.Low) / 2.0,
            IndicatorAppliedPrice.Typical => (candle.High + candle.Low + candle.Close) / 3.0,
            IndicatorAppliedPrice.WeightedClose => (candle.High + candle.Low + candle.Close * 2.0) / 4.0,
            _ => candle.Close
        }).ToArray();
    }

    private static double[] NormalizeExternal(IReadOnlyList<double?> source, int count, IReadOnlyList<Candle> candles)
    {
        var values = new double[count];
        for (int index = 0; index < count; index++)
            values[index] = index < source.Count && source[index].HasValue && double.IsFinite(source[index]!.Value)
                ? source[index]!.Value
                : candles[index].Close;
        return values;
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse(value, true, out T parsed) ? parsed : fallback;

    private static long Volume(BuiltInIndicatorInstance instance, Candle candle)
    {
        IndicatorVolumeMode mode = ParseEnum(instance.Text("Volume", IndicatorVolumeMode.TickVolume.ToString()), IndicatorVolumeMode.TickVolume);
        return mode == IndicatorVolumeMode.RealVolume ? Math.Max(0, candle.RealVolume) : Math.Max(0, candle.TickVolume);
    }

    private static double[] Ma(double[] source, int period, IndicatorMaMethod method) => method switch
    {
        IndicatorMaMethod.Exponential => Ema(source, period),
        IndicatorMaMethod.Smoothed => Smma(source, period),
        IndicatorMaMethod.LinearWeighted => Lwma(source, period),
        _ => Sma(source, period)
    };

    private static List<IndicatorSeriesResult> Ama(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 10);
        int fast = instance.Int("FastPeriod", 2);
        int slow = instance.Int("SlowPeriod", 30);
        int shift = instance.IntAllowZero("Shift", 0);
        double fastSc = 2.0 / (fast + 1.0);
        double slowSc = 2.0 / (slow + 1.0);
        double[] output = Empty(source.Length);
        if (source.Length == 0)
            return new() { Series(instance, "ama", "AMA", output, shift) };
        output[0] = source[0];
        for (int i = 1; i < source.Length; i++)
        {
            if (i < period)
            {
                output[i] = output[i - 1] + slowSc * slowSc * (source[i] - output[i - 1]);
                continue;
            }
            double direction = Math.Abs(source[i] - source[i - period]);
            double volatility = 0;
            for (int j = i - period + 1; j <= i; j++)
                volatility += Math.Abs(source[j] - source[j - 1]);
            double er = volatility <= 1e-15 ? 0 : direction / volatility;
            double sc = Math.Pow(er * (fastSc - slowSc) + slowSc, 2);
            output[i] = output[i - 1] + sc * (source[i] - output[i - 1]);
        }
        return new() { Series(instance, "ama", "AMA", output, shift) };
    }

    private static List<IndicatorSeriesResult> Adx(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles, bool wilder)
    {
        int period = instance.Int("Period", 14);
        int n = candles.Count;
        double[] tr = Empty(n), plusDm = Empty(n), minusDm = Empty(n);
        if (n > 0) { tr[0] = candles[0].High - candles[0].Low; plusDm[0] = 0; minusDm[0] = 0; }
        for (int i = 1; i < n; i++)
        {
            double up = candles[i].High - candles[i - 1].High;
            double down = candles[i - 1].Low - candles[i].Low;
            plusDm[i] = up > down && up > 0 ? up : 0;
            minusDm[i] = down > up && down > 0 ? down : 0;
            tr[i] = Math.Max(candles[i].High - candles[i].Low,
                Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close), Math.Abs(candles[i].Low - candles[i - 1].Close)));
        }
        double[] atr = wilder ? Smma(tr, period) : Ema(tr, period);
        double[] plusSmooth = wilder ? Smma(plusDm, period) : Ema(plusDm, period);
        double[] minusSmooth = wilder ? Smma(minusDm, period) : Ema(minusDm, period);
        double[] plus = Empty(n), minus = Empty(n), dx = Empty(n);
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(atr[i]) || atr[i] <= 1e-15) continue;
            plus[i] = 100.0 * plusSmooth[i] / atr[i];
            minus[i] = 100.0 * minusSmooth[i] / atr[i];
            double sum = plus[i] + minus[i];
            dx[i] = sum <= 1e-15 ? 0 : 100.0 * Math.Abs(plus[i] - minus[i]) / sum;
        }
        double[] adx = wilder ? Smma(dx, period) : Ema(dx, period);
        return new()
        {
            Series(instance, "adx", wilder ? "ADX Wilder" : "ADX", adx),
            Series(instance, "plus", "+DI", plus),
            Series(instance, "minus", "-DI", minus)
        };
    }

    private static List<IndicatorSeriesResult> Bollinger(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 20);
        int shift = instance.IntAllowZero("Shift", 0);
        double deviation = Math.Max(0, instance.Number("Deviation", 2));
        double[] middle = Sma(source, period);
        double[] sd = RollingStdDev(source, period, middle);
        double[] upper = Empty(source.Length), lower = Empty(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            if (!double.IsFinite(middle[i]) || !double.IsFinite(sd[i])) continue;
            upper[i] = middle[i] + deviation * sd[i];
            lower[i] = middle[i] - deviation * sd[i];
        }
        return new()
        {
            Series(instance, "middle", "Middle", middle, shift),
            Series(instance, "upper", "Upper", upper, shift, "lower"),
            Series(instance, "lower", "Lower", lower, shift)
        };
    }

    private static List<IndicatorSeriesResult> Dema(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14);
        int shift = instance.IntAllowZero("Shift", 0);
        double[] e1 = Ema(source, period), e2 = Ema(e1, period), result = Empty(source.Length);
        for (int i = 0; i < result.Length; i++)
            if (double.IsFinite(e1[i]) && double.IsFinite(e2[i])) result[i] = 2 * e1[i] - e2[i];
        return new() { Series(instance, "dema", "DEMA", result, shift) };
    }

    private static List<IndicatorSeriesResult> Envelopes(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14);
        int shift = instance.IntAllowZero("Shift", 0);
        double deviation = Math.Max(0, instance.Number("Deviation", 0.1)) / 100.0;
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Simple.ToString()), IndicatorMaMethod.Simple);
        double[] average = Ma(source, period, method), upper = Empty(source.Length), lower = Empty(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            if (!double.IsFinite(average[i])) continue;
            upper[i] = average[i] * (1 + deviation);
            lower[i] = average[i] * (1 - deviation);
        }
        return new() { Series(instance, "upper", "Upper", upper, shift), Series(instance, "lower", "Lower", lower, shift) };
    }

    private static List<IndicatorSeriesResult> Frama(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles, double[] source)
    {
        int period = Math.Max(2, instance.Int("Period", 14));
        if ((period & 1) == 1) period++;
        int half = period / 2;
        int shift = instance.IntAllowZero("Shift", 0);
        double[] result = Empty(source.Length);
        if (source.Length == 0) return new() { Series(instance, "frama", "FRAMA", result, shift) };
        result[0] = source[0];
        for (int i = 1; i < source.Length; i++)
        {
            if (i < period - 1) { result[i] = source[i]; continue; }
            double high1 = double.MinValue, low1 = double.MaxValue, high2 = double.MinValue, low2 = double.MaxValue, high3 = double.MinValue, low3 = double.MaxValue;
            for (int j = i - period + 1; j <= i - half; j++) { high1 = Math.Max(high1, candles[j].High); low1 = Math.Min(low1, candles[j].Low); }
            for (int j = i - half + 1; j <= i; j++) { high2 = Math.Max(high2, candles[j].High); low2 = Math.Min(low2, candles[j].Low); }
            for (int j = i - period + 1; j <= i; j++) { high3 = Math.Max(high3, candles[j].High); low3 = Math.Min(low3, candles[j].Low); }
            double n1 = (high1 - low1) / half, n2 = (high2 - low2) / half, n3 = (high3 - low3) / period;
            double dimension = n1 > 0 && n2 > 0 && n3 > 0 ? (Math.Log(n1 + n2) - Math.Log(n3)) / Math.Log(2) : 1;
            double alpha = Math.Clamp(Math.Exp(-4.6 * (dimension - 1)), 0.01, 1.0);
            result[i] = alpha * source[i] + (1 - alpha) * result[i - 1];
        }
        return new() { Series(instance, "frama", "FRAMA", result, shift) };
    }

    private static List<IndicatorSeriesResult> Ichimoku(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int tenkan = instance.Int("Tenkan", 9), kijun = instance.Int("Kijun", 26), senkouBPeriod = instance.Int("SenkouB", 52);
        double[] tenkanValues = MidRange(candles, tenkan), kijunValues = MidRange(candles, kijun), senkouA = Empty(candles.Count), senkouB = MidRange(candles, senkouBPeriod), chikou = candles.Select(c => c.Close).ToArray();
        for (int i = 0; i < candles.Count; i++)
            if (double.IsFinite(tenkanValues[i]) && double.IsFinite(kijunValues[i])) senkouA[i] = (tenkanValues[i] + kijunValues[i]) / 2.0;
        return new()
        {
            Series(instance, "tenkan", "Tenkan-sen", tenkanValues),
            Series(instance, "kijun", "Kijun-sen", kijunValues),
            Series(instance, "senkouA", "Senkou Span A", senkouA, kijun, "senkouB"),
            Series(instance, "senkouB", "Senkou Span B", senkouB, kijun),
            Series(instance, "chikou", "Chikou Span", chikou, -kijun)
        };
    }

    private static List<IndicatorSeriesResult> MovingAverage(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14), shift = instance.IntAllowZero("Shift", 0);
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Simple.ToString()), IndicatorMaMethod.Simple);
        return new() { Series(instance, "ma", "Moving Average", Ma(source, period, method), shift) };
    }

    private static List<IndicatorSeriesResult> ParabolicSar(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double step = Math.Clamp(instance.Number("Step", 0.02), 0.0001, 1.0);
        double maximum = Math.Max(step, instance.Number("Maximum", 0.2));
        int n = candles.Count;
        double[] result = Empty(n);
        if (n == 0) return new() { Series(instance, "sar", "SAR", result) };
        bool up = n < 2 || candles[Math.Min(1, n - 1)].Close >= candles[0].Close;
        double sar = up ? candles[0].Low : candles[0].High;
        double ep = up ? candles[0].High : candles[0].Low;
        double af = step;
        result[0] = sar;
        for (int i = 1; i < n; i++)
        {
            sar += af * (ep - sar);
            if (up)
            {
                sar = Math.Min(sar, candles[i - 1].Low);
                if (i > 1) sar = Math.Min(sar, candles[i - 2].Low);
                if (candles[i].Low < sar)
                {
                    up = false; sar = ep; ep = candles[i].Low; af = step;
                }
                else if (candles[i].High > ep) { ep = candles[i].High; af = Math.Min(maximum, af + step); }
            }
            else
            {
                sar = Math.Max(sar, candles[i - 1].High);
                if (i > 1) sar = Math.Max(sar, candles[i - 2].High);
                if (candles[i].High > sar)
                {
                    up = true; sar = ep; ep = candles[i].High; af = step;
                }
                else if (candles[i].Low < ep) { ep = candles[i].Low; af = Math.Min(maximum, af + step); }
            }
            result[i] = sar;
        }
        return new() { Series(instance, "sar", "SAR", result) };
    }

    private static List<IndicatorSeriesResult> StandardDeviation(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 20), shift = instance.IntAllowZero("Shift", 0);
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Simple.ToString()), IndicatorMaMethod.Simple);
        double[] average = Ma(source, period, method), sd = RollingStdDev(source, period, average);
        return new() { Series(instance, "stddev", "Standard Deviation", sd, shift) };
    }

    private static List<IndicatorSeriesResult> Tema(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14), shift = instance.IntAllowZero("Shift", 0);
        double[] e1 = Ema(source, period), e2 = Ema(e1, period), e3 = Ema(e2, period), result = Empty(source.Length);
        for (int i = 0; i < source.Length; i++)
            if (double.IsFinite(e1[i]) && double.IsFinite(e2[i]) && double.IsFinite(e3[i])) result[i] = 3 * e1[i] - 3 * e2[i] + e3[i];
        return new() { Series(instance, "tema", "TEMA", result, shift) };
    }

    private static List<IndicatorSeriesResult> Vidya(BuiltInIndicatorInstance instance, double[] source)
    {
        int cmoPeriod = instance.Int("CmoPeriod", 9), emaPeriod = instance.Int("EmaPeriod", 12), shift = instance.IntAllowZero("Shift", 0);
        double alpha = 2.0 / (emaPeriod + 1.0);
        double[] result = Empty(source.Length);
        if (source.Length == 0) return new() { Series(instance, "vidya", "VIDYA", result, shift) };
        result[0] = source[0];
        for (int i = 1; i < source.Length; i++)
        {
            int start = Math.Max(1, i - cmoPeriod + 1);
            double up = 0, down = 0;
            for (int j = start; j <= i; j++)
            {
                double delta = source[j] - source[j - 1];
                if (delta >= 0) up += delta; else down -= delta;
            }
            double cmo = up + down <= 1e-15 ? 0 : Math.Abs((up - down) / (up + down));
            double adaptive = alpha * cmo;
            result[i] = adaptive * source[i] + (1 - adaptive) * result[i - 1];
        }
        return new() { Series(instance, "vidya", "VIDYA", result, shift) };
    }

    private static List<IndicatorSeriesResult> Atr(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        return new() { Series(instance, "atr", "ATR", Smma(TrueRange(candles), instance.Int("Period", 14))) };
    }

    private static List<IndicatorSeriesResult> BearsPower(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] ema = Ema(candles.Select(c => c.Close).ToArray(), instance.Int("Period", 13)), result = Empty(candles.Count);
        for (int i = 0; i < candles.Count; i++) if (double.IsFinite(ema[i])) result[i] = candles[i].Low - ema[i];
        return new() { Series(instance, "bears", "Bears Power", result) };
    }

    private static List<IndicatorSeriesResult> BullsPower(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] ema = Ema(candles.Select(c => c.Close).ToArray(), instance.Int("Period", 13)), result = Empty(candles.Count);
        for (int i = 0; i < candles.Count; i++) if (double.IsFinite(ema[i])) result[i] = candles[i].High - ema[i];
        return new() { Series(instance, "bulls", "Bulls Power", result) };
    }

    private static List<IndicatorSeriesResult> Chaikin(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int fast = instance.Int("FastPeriod", 3), slow = instance.Int("SlowPeriod", 10);
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Exponential.ToString()), IndicatorMaMethod.Exponential);
        double[] ad = AccumulationDistributionValues(instance, candles), fastMa = Ma(ad, fast, method), slowMa = Ma(ad, slow, method), result = Empty(candles.Count);
        for (int i = 0; i < candles.Count; i++) if (double.IsFinite(fastMa[i]) && double.IsFinite(slowMa[i])) result[i] = fastMa[i] - slowMa[i];
        return new() { Series(instance, "chaikin", "Chaikin", result) };
    }

    private static List<IndicatorSeriesResult> Cci(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14);
        double[] average = Sma(source, period), result = Empty(source.Length);
        for (int i = period - 1; i < source.Length; i++)
        {
            if (!double.IsFinite(average[i])) continue;
            double deviation = 0;
            for (int j = i - period + 1; j <= i; j++) deviation += Math.Abs(source[j] - average[i]);
            deviation /= period;
            result[i] = deviation <= 1e-15 ? 0 : (source[i] - average[i]) / (0.015 * deviation);
        }
        return new() { Series(instance, "cci", "CCI", result) };
    }

    private static List<IndicatorSeriesResult> DeMarker(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int period = instance.Int("Period", 14), n = candles.Count;
        double[] max = new double[n], min = new double[n];
        for (int i = 1; i < n; i++)
        {
            max[i] = Math.Max(0, candles[i].High - candles[i - 1].High);
            min[i] = Math.Max(0, candles[i - 1].Low - candles[i].Low);
        }
        double[] maxMa = Sma(max, period), minMa = Sma(min, period), result = Empty(n);
        for (int i = 0; i < n; i++)
        {
            double sum = maxMa[i] + minMa[i];
            if (double.IsFinite(sum)) result[i] = sum <= 1e-15 ? 0 : maxMa[i] / sum;
        }
        return new() { Series(instance, "demarker", "DeMarker", result) };
    }

    private static List<IndicatorSeriesResult> ForceIndex(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] raw = Empty(candles.Count);
        if (candles.Count > 0) raw[0] = 0;
        for (int i = 1; i < candles.Count; i++) raw[i] = (candles[i].Close - candles[i - 1].Close) * Volume(instance, candles[i]);
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Exponential.ToString()), IndicatorMaMethod.Exponential);
        return new() { Series(instance, "force", "Force Index", Ma(raw, instance.Int("Period", 13), method)) };
    }

    private static List<IndicatorSeriesResult> Macd(BuiltInIndicatorInstance instance, double[] source, bool osmaOnly)
    {
        int fast = instance.Int("FastPeriod", 12), slow = instance.Int("SlowPeriod", 26), signalPeriod = instance.Int("SignalPeriod", 9);
        double[] fastEma = Ema(source, fast), slowEma = Ema(source, slow), main = Empty(source.Length);
        for (int i = 0; i < source.Length; i++) if (double.IsFinite(fastEma[i]) && double.IsFinite(slowEma[i])) main[i] = fastEma[i] - slowEma[i];
        double[] signal = Sma(main, signalPeriod), osma = Empty(source.Length);
        for (int i = 0; i < source.Length; i++) if (double.IsFinite(main[i]) && double.IsFinite(signal[i])) osma[i] = main[i] - signal[i];
        return osmaOnly
            ? new() { Series(instance, "osma", "OsMA", osma) }
            : new() { Series(instance, "main", "MACD", main), Series(instance, "signal", "Signal", signal) };
    }

    private static List<IndicatorSeriesResult> Momentum(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14);
        double[] result = Empty(source.Length);
        for (int i = period; i < source.Length; i++) result[i] = Math.Abs(source[i - period]) <= 1e-15 ? 0 : source[i] / source[i - period] * 100.0;
        return new() { Series(instance, "momentum", "Momentum", result) };
    }

    private static List<IndicatorSeriesResult> Rsi(BuiltInIndicatorInstance instance, double[] source)
    {
        return new() { Series(instance, "rsi", "RSI", RsiValues(source, instance.Int("Period", 14))) };
    }

    private static List<IndicatorSeriesResult> Rvi(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int n = candles.Count, period = instance.Int("Period", 10);
        double[] numerator = Empty(n), denominator = Empty(n);
        for (int i = 3; i < n; i++)
        {
            numerator[i] = ((candles[i].Close - candles[i].Open) + 2 * (candles[i - 1].Close - candles[i - 1].Open) + 2 * (candles[i - 2].Close - candles[i - 2].Open) + (candles[i - 3].Close - candles[i - 3].Open)) / 6.0;
            denominator[i] = ((candles[i].High - candles[i].Low) + 2 * (candles[i - 1].High - candles[i - 1].Low) + 2 * (candles[i - 2].High - candles[i - 2].Low) + (candles[i - 3].High - candles[i - 3].Low)) / 6.0;
        }
        double[] numMa = Sma(numerator, period), denMa = Sma(denominator, period), main = Empty(n), signal = Empty(n);
        for (int i = 0; i < n; i++) if (double.IsFinite(numMa[i]) && double.IsFinite(denMa[i])) main[i] = Math.Abs(denMa[i]) <= 1e-15 ? 0 : numMa[i] / denMa[i];
        for (int i = 3; i < n; i++) if (double.IsFinite(main[i]) && double.IsFinite(main[i - 1]) && double.IsFinite(main[i - 2]) && double.IsFinite(main[i - 3])) signal[i] = (main[i] + 2 * main[i - 1] + 2 * main[i - 2] + main[i - 3]) / 6.0;
        return new() { Series(instance, "main", "RVI", main), Series(instance, "signal", "Signal", signal) };
    }

    private static List<IndicatorSeriesResult> Stochastic(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int kPeriod = instance.Int("KPeriod", 5), dPeriod = instance.Int("DPeriod", 3), slowing = instance.Int("Slowing", 3), n = candles.Count;
        bool closeClose = string.Equals(instance.Text("PriceField", "LowHigh"), "CloseClose", StringComparison.OrdinalIgnoreCase);
        double[] raw = Empty(n);
        for (int i = kPeriod - 1; i < n; i++)
        {
            double highest = double.MinValue, lowest = double.MaxValue;
            for (int j = i - kPeriod + 1; j <= i; j++)
            {
                highest = Math.Max(highest, closeClose ? candles[j].Close : candles[j].High);
                lowest = Math.Min(lowest, closeClose ? candles[j].Close : candles[j].Low);
            }
            raw[i] = highest - lowest <= 1e-15 ? 0 : (candles[i].Close - lowest) / (highest - lowest) * 100.0;
        }
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Simple.ToString()), IndicatorMaMethod.Simple);
        double[] main = Ma(raw, slowing, method), signal = Ma(main, dPeriod, method);
        return new() { Series(instance, "main", "%K", main), Series(instance, "signal", "%D", signal) };
    }

    private static List<IndicatorSeriesResult> Trix(BuiltInIndicatorInstance instance, double[] source)
    {
        int period = instance.Int("Period", 14);
        double[] e1 = Ema(source, period), e2 = Ema(e1, period), e3 = Ema(e2, period), result = Empty(source.Length);
        for (int i = 1; i < source.Length; i++) if (double.IsFinite(e3[i]) && double.IsFinite(e3[i - 1]) && Math.Abs(e3[i - 1]) > 1e-15) result[i] = (e3[i] - e3[i - 1]) / e3[i - 1] * 100.0;
        return new() { Series(instance, "trix", "TRIX", result) };
    }

    private static List<IndicatorSeriesResult> WilliamsR(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int period = instance.Int("Period", 14), n = candles.Count;
        double[] result = Empty(n);
        for (int i = period - 1; i < n; i++)
        {
            double highest = double.MinValue, lowest = double.MaxValue;
            for (int j = i - period + 1; j <= i; j++) { highest = Math.Max(highest, candles[j].High); lowest = Math.Min(lowest, candles[j].Low); }
            result[i] = highest - lowest <= 1e-15 ? 0 : -100.0 * (highest - candles[i].Close) / (highest - lowest);
        }
        return new() { Series(instance, "wpr", "Williams %R", result) };
    }

    private static List<IndicatorSeriesResult> AccumulationDistribution(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles) =>
        new() { Series(instance, "ad", "A/D", AccumulationDistributionValues(instance, candles)) };

    private static double[] AccumulationDistributionValues(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] result = new double[candles.Count];
        double cumulative = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            double range = candles[i].High - candles[i].Low;
            double multiplier = range <= 1e-15 ? 0 : ((candles[i].Close - candles[i].Low) - (candles[i].High - candles[i].Close)) / range;
            cumulative += multiplier * Volume(instance, candles[i]);
            result[i] = cumulative;
        }
        return result;
    }

    private static List<IndicatorSeriesResult> MoneyFlowIndex(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        int period = instance.Int("Period", 14), n = candles.Count;
        double[] typical = candles.Select(c => (c.High + c.Low + c.Close) / 3.0).ToArray(), result = Empty(n), positive = new double[n], negative = new double[n];
        for (int i = 1; i < n; i++)
        {
            double flow = typical[i] * Volume(instance, candles[i]);
            if (typical[i] > typical[i - 1]) positive[i] = flow; else if (typical[i] < typical[i - 1]) negative[i] = flow;
        }
        double pos = 0, neg = 0;
        for (int i = 0; i < n; i++)
        {
            pos += positive[i]; neg += negative[i];
            if (i >= period) { pos -= positive[i - period]; neg -= negative[i - period]; }
            if (i >= period - 1) result[i] = neg <= 1e-15 ? 100 : 100.0 - 100.0 / (1.0 + pos / neg);
        }
        return new() { Series(instance, "mfi", "MFI", result) };
    }

    private static List<IndicatorSeriesResult> OnBalanceVolume(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] result = new double[candles.Count];
        for (int i = 1; i < candles.Count; i++)
        {
            long volume = Volume(instance, candles[i]);
            result[i] = result[i - 1] + (candles[i].Close > candles[i - 1].Close ? volume : candles[i].Close < candles[i - 1].Close ? -volume : 0);
        }
        return new() { Series(instance, "obv", "OBV", result) };
    }

    private static List<IndicatorSeriesResult> Volumes(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles) =>
        new() { Series(instance, "volume", "Volumes", candles.Select(c => (double)Volume(instance, c)).ToArray()) };

    private static List<IndicatorSeriesResult> Accelerator(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] ao = AwesomeValues(candles), signal = Sma(ao, 5), ac = Empty(candles.Count);
        for (int i = 0; i < candles.Count; i++) if (double.IsFinite(ao[i]) && double.IsFinite(signal[i])) ac[i] = ao[i] - signal[i];
        return new() { Series(instance, "ac", "AC", ac) };
    }

    private static List<IndicatorSeriesResult> Alligator(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] median = candles.Select(c => (c.High + c.Low) / 2.0).ToArray();
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Smoothed.ToString()), IndicatorMaMethod.Smoothed);
        double[] jaw = Ma(median, instance.Int("JawPeriod", 13), method), teeth = Ma(median, instance.Int("TeethPeriod", 8), method), lips = Ma(median, instance.Int("LipsPeriod", 5), method);
        return new()
        {
            Series(instance, "jaw", "Jaws", jaw, instance.IntAllowZero("JawShift", 8)),
            Series(instance, "teeth", "Teeth", teeth, instance.IntAllowZero("TeethShift", 5)),
            Series(instance, "lips", "Lips", lips, instance.IntAllowZero("LipsShift", 3))
        };
    }

    private static List<IndicatorSeriesResult> Awesome(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles) =>
        new() { Series(instance, "ao", "AO", AwesomeValues(candles)) };

    private static double[] AwesomeValues(IReadOnlyList<Candle> candles)
    {
        double[] median = candles.Select(c => (c.High + c.Low) / 2.0).ToArray(), fast = Sma(median, 5), slow = Sma(median, 34), result = Empty(candles.Count);
        for (int i = 0; i < candles.Count; i++) if (double.IsFinite(fast[i]) && double.IsFinite(slow[i])) result[i] = fast[i] - slow[i];
        return result;
    }

    private static List<IndicatorSeriesResult> Fractals(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] up = Empty(candles.Count), down = Empty(candles.Count);
        for (int i = 2; i < candles.Count - 2; i++)
        {
            if (candles[i].High > candles[i - 1].High && candles[i].High > candles[i - 2].High && candles[i].High > candles[i + 1].High && candles[i].High > candles[i + 2].High) up[i] = candles[i].High;
            if (candles[i].Low < candles[i - 1].Low && candles[i].Low < candles[i - 2].Low && candles[i].Low < candles[i + 1].Low && candles[i].Low < candles[i + 2].Low) down[i] = candles[i].Low;
        }
        return new() { Series(instance, "up", "Up Fractal", up), Series(instance, "down", "Down Fractal", down) };
    }

    private static List<IndicatorSeriesResult> Gator(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] median = candles.Select(c => (c.High + c.Low) / 2.0).ToArray();
        IndicatorMaMethod method = ParseEnum(instance.Text("Method", IndicatorMaMethod.Smoothed.ToString()), IndicatorMaMethod.Smoothed);
        double[] jaw = Ma(median, instance.Int("JawPeriod", 13), method), teeth = Ma(median, instance.Int("TeethPeriod", 8), method), lips = Ma(median, instance.Int("LipsPeriod", 5), method);
        int n = candles.Count; double[] upper = Empty(n), lower = Empty(n);
        for (int i = 0; i < n; i++)
        {
            if (double.IsFinite(jaw[i]) && double.IsFinite(teeth[i])) upper[i] = Math.Abs(jaw[i] - teeth[i]);
            if (double.IsFinite(teeth[i]) && double.IsFinite(lips[i])) lower[i] = -Math.Abs(teeth[i] - lips[i]);
        }
        return new() { Series(instance, "upper", "Jaws–Teeth", upper), Series(instance, "lower", "Teeth–Lips", lower) };
    }

    private static List<IndicatorSeriesResult> MarketFacilitation(BuiltInIndicatorInstance instance, IReadOnlyList<Candle> candles)
    {
        double[] result = new double[candles.Count];
        for (int i = 0; i < candles.Count; i++)
        {
            long volume = Volume(instance, candles[i]);
            result[i] = volume <= 0 ? 0 : (candles[i].High - candles[i].Low) / volume;
        }
        return new() { Series(instance, "mfi", "BW MFI", result) };
    }

    private static double[] Sma(double[] source, int period)
    {
        period = Math.Max(1, period); double[] output = Empty(source.Length); double sum = 0; int valid = 0;
        var queue = new Queue<double>();
        for (int i = 0; i < source.Length; i++)
        {
            double value = source[i]; queue.Enqueue(value);
            if (double.IsFinite(value)) { sum += value; valid++; }
            if (queue.Count > period) { double removed = queue.Dequeue(); if (double.IsFinite(removed)) { sum -= removed; valid--; } }
            if (queue.Count == period && valid == period) output[i] = sum / period;
        }
        return output;
    }

    private static double[] Ema(double[] source, int period)
    {
        period = Math.Max(1, period); double[] output = Empty(source.Length); double alpha = 2.0 / (period + 1.0); double ema = double.NaN;
        for (int i = 0; i < source.Length; i++)
        {
            if (!double.IsFinite(source[i])) continue;
            ema = double.IsFinite(ema) ? alpha * source[i] + (1 - alpha) * ema : source[i];
            output[i] = ema;
        }
        return output;
    }

    private static double[] Smma(double[] source, int period)
    {
        period = Math.Max(1, period); double[] output = Empty(source.Length); double sum = 0; int count = 0; double previous = double.NaN;
        for (int i = 0; i < source.Length; i++)
        {
            if (!double.IsFinite(source[i])) continue;
            if (count < period)
            {
                sum += source[i]; count++;
                if (count == period) { previous = sum / period; output[i] = previous; }
            }
            else
            {
                previous = (previous * (period - 1) + source[i]) / period;
                output[i] = previous;
            }
        }
        return output;
    }

    private static double[] Lwma(double[] source, int period)
    {
        period = Math.Max(1, period); double[] output = Empty(source.Length); double denominator = period * (period + 1) / 2.0;
        for (int i = period - 1; i < source.Length; i++)
        {
            double sum = 0; bool valid = true;
            for (int j = 0; j < period; j++) { double value = source[i - period + 1 + j]; if (!double.IsFinite(value)) { valid = false; break; } sum += value * (j + 1); }
            if (valid) output[i] = sum / denominator;
        }
        return output;
    }

    private static double[] RollingStdDev(double[] source, int period, double[] average)
    {
        double[] output = Empty(source.Length);
        for (int i = period - 1; i < source.Length; i++)
        {
            if (!double.IsFinite(average[i])) continue;
            double sum = 0; bool valid = true;
            for (int j = i - period + 1; j <= i; j++) { if (!double.IsFinite(source[j])) { valid = false; break; } double d = source[j] - average[i]; sum += d * d; }
            if (valid) output[i] = Math.Sqrt(sum / period);
        }
        return output;
    }

    private static double[] MidRange(IReadOnlyList<Candle> candles, int period)
    {
        double[] output = Empty(candles.Count);
        for (int i = period - 1; i < candles.Count; i++)
        {
            double high = double.MinValue, low = double.MaxValue;
            for (int j = i - period + 1; j <= i; j++) { high = Math.Max(high, candles[j].High); low = Math.Min(low, candles[j].Low); }
            output[i] = (high + low) / 2.0;
        }
        return output;
    }

    private static double[] TrueRange(IReadOnlyList<Candle> candles)
    {
        double[] tr = new double[candles.Count];
        if (candles.Count == 0) return tr;
        tr[0] = candles[0].High - candles[0].Low;
        for (int i = 1; i < candles.Count; i++) tr[i] = Math.Max(candles[i].High - candles[i].Low, Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close), Math.Abs(candles[i].Low - candles[i - 1].Close)));
        return tr;
    }

    private static double[] RsiValues(double[] source, int period)
    {
        double[] output = Empty(source.Length); if (source.Length <= period) return output;
        double gain = 0, loss = 0;
        for (int i = 1; i <= period; i++) { double delta = source[i] - source[i - 1]; if (delta >= 0) gain += delta; else loss -= delta; }
        gain /= period; loss /= period; output[period] = loss <= 1e-15 ? 100 : 100 - 100 / (1 + gain / loss);
        for (int i = period + 1; i < source.Length; i++)
        {
            double delta = source[i] - source[i - 1]; gain = (gain * (period - 1) + Math.Max(0, delta)) / period; loss = (loss * (period - 1) + Math.Max(0, -delta)) / period;
            output[i] = loss <= 1e-15 ? 100 : 100 - 100 / (1 + gain / loss);
        }
        return output;
    }
}
