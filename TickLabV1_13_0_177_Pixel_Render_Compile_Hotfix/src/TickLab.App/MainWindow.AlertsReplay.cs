using System.Windows;
using System.Windows.Threading;
using TickLab.Core.Alerts;
using TickLab.Core.Diagnostics;
using TickLab.Core.Drawing;
using TickLab.Core.Indicators;
using TickLab.Core.Market;
using TickLab.Core.Replay;
using TickLab.Core.Scripting;
using TickLab.Core.Settings;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Settings;
using TickLab.Desktop.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private readonly AlertStore _alertStore = new();
    private AlertDocument _alertDocument = new();
    private AlertManagerWindow? _alertManagerWindow;
    private readonly Dictionary<string, double> _alertPreviousValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _alertPreviousStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _alertPreviousCandleStarts = new(StringComparer.Ordinal);

    private readonly DispatcherTimer _replayTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private MarketReplayWindow? _replayWindow;
    private ReplayRuntime? _replay;
    private bool _replayLoading;
    private double _replaySpeed = 1;
    private int? _replayMarkerChartId;
    private int? _replaySetupChartId;
    private bool _replayRangeMode;
    private ReplayPlaybackDirection _replayDirection = ReplayPlaybackDirection.Forward;
    private bool _alertPopupOpen;

    private enum ReplayPlaybackDirection
    {
        Forward,
        Reverse
    }

    private sealed class ReplayRuntime
    {
        public required int ChartId { get; init; }
        public required ChartRuntimeContext Context { get; init; }
        public required string ConnectorId { get; init; }
        public required int ServerUtcOffsetMinutes { get; init; }
        public required List<Candle> OriginalSourceCandles { get; init; }
        public required List<Candle> OriginalDisplayCandles { get; init; }
        // The visible chart is replaced by replay candles, but the real bridge
        // stream keeps updating these hidden lists. End Replay reveals this
        // continuously updated state instead of an old frozen snapshot.
        public required List<Candle> HiddenLiveSourceCandles { get; set; }
        public required List<Candle> HiddenLiveDisplayCandles { get; set; }
        public required ChartViewportState OriginalViewport { get; init; }
        public required bool OriginalOlderLoaded { get; init; }
        public required bool OriginalNewerLoaded { get; init; }
        public required MarketReplayEngine Engine { get; init; }
        public required List<MarketTick> Ticks { get; set; }
        public Stack<MarketTick> RedoTicks { get; } = new();
        public ChartWindowAnchor? ReplayStartViewportAnchor { get; init; }
        public bool ReplayViewportApplied { get; set; }
        public int TickIndex { get; set; }
        public bool HasMore { get; set; }
        public long NextStartMilliseconds { get; set; }
        public bool IsPlaying { get; set; }
        public long StartUnix { get; init; }
        public long? EndUnixExclusive { get; init; }
        public long? EndMillisecondsExclusive => EndUnixExclusive.HasValue
            ? checked(EndUnixExclusive.Value * 1000L)
            : null;
        public bool RangeCompleted { get; set; }
        public long ProcessedTicks { get; set; }
        public double SimulatedMilliseconds { get; set; }
        public DateTime LastPlaybackUtc { get; set; }
        public DateTime LastVisualRefreshUtc { get; set; } = DateTime.MinValue;
    }

    private void InitializeAlertsAndReplay()
    {
        _alertDocument = _alertStore.Load();
        _replayTimer.Tick += ReplayTimer_Tick;
        RefreshAlertLines();
    }

    private void AlertsButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureAlertManagerWindow();
        _alertManagerWindow!.SetDocument(_alertDocument);
        if (!_alertManagerWindow.IsVisible)
            _alertManagerWindow.Show();
        _alertManagerWindow.Activate();
    }

    private void EnsureAlertManagerWindow()
    {
        if (_alertManagerWindow is not null)
            return;

        _alertManagerWindow = new AlertManagerWindow { Owner = this };
        _alertManagerWindow.NewRequested += () => EditAlert(null);
        _alertManagerWindow.EditRequested += rule => EditAlert(rule);
        _alertManagerWindow.ToggleRequested += ToggleAlert;
        _alertManagerWindow.DeleteRequested += DeleteAlert;
        _alertManagerWindow.DeleteSelectedRequested += DeleteSelectedAlerts;
        _alertManagerWindow.LineColorRequested += ChangeAlertLineColor;
        _alertManagerWindow.LineThicknessRequested += ChangeAlertLineThickness;
        _alertManagerWindow.ClearLogRequested += () =>
        {
            _alertDocument = _alertDocument with { Log = Array.Empty<AlertLogEntry>() };
            SaveAlerts();
        };
    }

    private void CreatePriceAlert(ChartRuntimeContext context, double price)
    {
        int digits = context.Chart.Candles.LastOrDefault()?.Digits
                     ?? context.DisplayCandles.LastOrDefault()?.Digits
                     ?? 5;
        string formatted = price.ToString($"F{Math.Clamp(digits, 0, 10)}", System.Globalization.CultureInfo.InvariantCulture);
        EditAlert(new AlertRule
        {
            Name = $"{context.Symbol} touches {formatted}",
            ChartId = context.PaneId,
            Symbol = context.Symbol,
            Timeframe = context.Timeframe.DisplayText,
            Condition = AlertConditionType.PriceTouches,
            PriceSource = AlertPriceSource.Bid,
            Threshold = price,
            Frequency = AlertFrequency.Once,
            PlaySound = true,
            ShowDesktopPopup = true
        });
    }

    private void CreateDrawingAlert(ChartRuntimeContext context, ChartDrawing drawing)
    {
        EditAlert(new AlertRule
        {
            Name = $"{context.Symbol} {drawing.DisplayName} cross",
            ChartId = context.PaneId,
            Symbol = context.Symbol,
            Timeframe = context.Timeframe.DisplayText,
            Condition = AlertConditionType.DrawingCross,
            DrawingId = drawing.Id,
            Frequency = AlertFrequency.OncePerCandle,
            PlaySound = true,
            ShowDesktopPopup = true
        });
    }

    private void EditAlert(AlertRule? rule)
    {
        IReadOnlyList<AlertEditorChartOption> charts = _chartContexts.Values
            .OrderBy(item => item.PaneId)
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
            .Select(item => new AlertEditorChartOption(
                item.PaneId,
                item.Symbol,
                item.Timeframe.DisplayText,
                item.Chart.ChartDrawings
                    .Where(drawing => !drawing.IsHidden)
                    .ToDictionary(
                        drawing => drawing.Id,
                        drawing => string.IsNullOrWhiteSpace(drawing.Name)
                            ? $"{drawing.DisplayName} ({drawing.Id[..Math.Min(6, drawing.Id.Length)]})"
                            : drawing.Name,
                        StringComparer.Ordinal),
                item.IndicatorResults
                    .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.Name))
                    .Concat(item.BuiltInIndicatorResults.Values.SelectMany(result =>
                        result.Series
                            .Where(series => series.Style.Visible)
                            .Select(series => new KeyValuePair<string, string>(
                                $"builtin:{result.InstanceId}:{series.Key}",
                                $"{result.Name} · {series.Label}"))))
                    .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        if (charts.Count == 0)
        {
            MessageBox.Show(this, "Open a connected chart before creating an alert.", "Alerts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new AlertEditorWindow(charts, rule) { Owner = this };
        if (editor.ShowDialog() != true)
            return;
        AlertRule? result = editor.Result;
        if (result is null)
            return;

        AlertRule saved = result;
        AlertRule[] rules = _alertDocument.Rules
            .Where(item => !string.Equals(item.Id, saved.Id, StringComparison.Ordinal))
            .Append(saved)
            .OrderBy(item => item.CreatedUnix)
            .ToArray();
        _alertDocument = _alertDocument with { Rules = rules };
        SaveAlerts();
        StatusText.Text = $"Alert saved: {saved.Name}.";
    }

    private void ToggleAlert(AlertRule rule)
    {
        ReplaceAlertRule(rule with { Enabled = !rule.Enabled });
        SaveAlerts();
    }

    private void DeleteAlert(AlertRule rule)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Delete alert ‘{rule.Name}’?",
            "Delete Alert",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        RemoveAlertRule(rule);
    }

    private void DeleteSelectedAlerts(IReadOnlyList<AlertRule> rules)
    {
        AlertRule[] selected = rules
            .Where(rule => _alertDocument.Rules.Any(item => string.Equals(item.Id, rule.Id, StringComparison.Ordinal)))
            .GroupBy(rule => rule.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (selected.Length == 0)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Delete {selected.Length} selected alert{(selected.Length == 1 ? string.Empty : "s")}?",
            "Delete Selected Alerts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        HashSet<string> ids = selected.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        _alertDocument = _alertDocument with
        {
            Rules = _alertDocument.Rules.Where(rule => !ids.Contains(rule.Id)).ToArray()
        };
        foreach (string id in ids)
        {
            _alertPreviousValues.Remove(id);
            _alertPreviousStates.Remove(id);
            _alertPreviousCandleStarts.Remove(id);
        }
        SaveAlerts();
        StatusText.Text = $"Removed {selected.Length} selected alert{(selected.Length == 1 ? string.Empty : "s")}.";
    }

    private void ChangeAlertLineThickness(AlertRule rule, double pixels)
    {
        pixels = Math.Clamp(double.IsFinite(pixels) ? pixels : 1.25, 0.5, 8.0);
        ReplaceAlertRule(rule with { LineThickness = pixels });
        SaveAlerts();
        StatusText.Text = $"Alert line thickness updated: {rule.Name} · {pixels:0.#} px.";
    }

    private void EditAlertById(string alertId)
    {
        AlertRule? rule = _alertDocument.Rules.FirstOrDefault(item =>
            string.Equals(item.Id, alertId, StringComparison.Ordinal));
        if (rule is not null)
            EditAlert(rule);
    }

    private void RemoveAlertById(string alertId)
    {
        AlertRule? rule = _alertDocument.Rules.FirstOrDefault(item =>
            string.Equals(item.Id, alertId, StringComparison.Ordinal));
        if (rule is null)
            return;

        RemoveAlertRule(rule);
        StatusText.Text = $"Alert removed: {rule.Name}.";
    }

    private void RemoveAlertRule(AlertRule rule)
    {
        _alertDocument = _alertDocument with
        {
            Rules = _alertDocument.Rules
                .Where(item => !string.Equals(item.Id, rule.Id, StringComparison.Ordinal))
                .ToArray()
        };
        _alertPreviousValues.Remove(rule.Id);
        _alertPreviousStates.Remove(rule.Id);
        _alertPreviousCandleStarts.Remove(rule.Id);
        SaveAlerts();
    }

    private void ChangeAlertLineColor(AlertRule rule)
    {
        Window owner = _alertManagerWindow is not null ? _alertManagerWindow : this;
        var picker = new DrawingColorPickerWindow(rule.LineColor) { Owner = owner };
        if (picker.ShowDialog() != true)
            return;

        ReplaceAlertRule(rule with { LineColor = picker.SelectedColor });
        SaveAlerts();
        StatusText.Text = $"Alert line colour updated: {rule.Name}.";
    }

    private void SaveAlerts()
    {
        _alertStore.Save(_alertDocument);
        _alertManagerWindow?.SetDocument(_alertDocument);
        RefreshAlertLines();
    }

    private void RefreshAlertLines()
    {
        foreach (ChartRuntimeContext context in _chartContexts.Values)
        {
            context.Chart.AlertLines = _alertDocument.Rules
                .Where(rule => rule.Enabled &&
                               rule.ChartId == context.PaneId &&
                               string.Equals(rule.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(rule.Timeframe, context.Timeframe.DisplayText, StringComparison.OrdinalIgnoreCase) &&
                               IsPriceLineAlert(rule.Condition))
                .Select(rule => new AlertLineOverlay(
                    rule.Id,
                    rule.Threshold,
                    rule.Name,
                    true,
                    rule.LineColor,
                    rule.LineThickness))
                .ToArray();
        }
    }

    private static bool IsPriceLineAlert(AlertConditionType condition) => condition is
        AlertConditionType.PriceAbove or
        AlertConditionType.PriceBelow or
        AlertConditionType.PriceCrossesUp or
        AlertConditionType.PriceCrossesDown or
        AlertConditionType.PriceTouches;

    private void MoveAlertLine(ChartRuntimeContext context, string alertId, double price)
    {
        AlertRule? rule = _alertDocument.Rules.FirstOrDefault(item =>
            string.Equals(item.Id, alertId, StringComparison.Ordinal) && item.ChartId == context.PaneId);
        if (rule is null)
            return;

        int digits = context.Chart.Candles.LastOrDefault()?.Digits
                     ?? context.DisplayCandles.LastOrDefault()?.Digits
                     ?? 5;
        double rounded = Math.Round(price, Math.Clamp(digits, 0, 10));
        string formatted = rounded.ToString($"F{Math.Clamp(digits, 0, 10)}", System.Globalization.CultureInfo.InvariantCulture);
        bool automaticName = rule.Name.StartsWith(context.Symbol + " touches ", StringComparison.OrdinalIgnoreCase) ||
                             rule.Name.StartsWith(context.Symbol + " crosses ", StringComparison.OrdinalIgnoreCase);
        ReplaceAlertRule(rule with
        {
            Name = automaticName ? $"{context.Symbol} touches {formatted}" : rule.Name,
            Threshold = rounded,
            HasTriggered = false,
            LastTriggeredUnix = null,
            LastTriggeredCandleUnix = null,
            LastMessage = string.Empty,
            Enabled = true
        });
        _alertPreviousValues.Remove(rule.Id);
        _alertPreviousStates.Remove(rule.Id);
        SaveAlerts();
        StatusText.Text = $"Alert moved to {formatted} on Chart {context.PaneId}.";
    }

    private void ReplaceAlertRule(AlertRule replacement)
    {
        _alertDocument = _alertDocument with
        {
            Rules = _alertDocument.Rules
                .Select(item => string.Equals(item.Id, replacement.Id, StringComparison.Ordinal)
                    ? replacement
                    : item)
                .ToArray()
        };
    }

    private void EvaluateLiveAlerts(ChartRuntimeContext context)
    {
        if (IsReplayChart(context.PaneId))
            return;

        IReadOnlyList<Candle> alertCandles = context.Chart.Candles.Count > 0
            ? context.Chart.Candles
            : context.PaneId == _activePricePaneId
                ? _displayCandles
                : context.DisplayCandles;
        if (alertCandles.Count == 0)
            return;

        Candle liveCandle = alertCandles[^1];
        Candle? latestClosedCandle = alertCandles.LastOrDefault(item => item.IsClosed);
        AlertRule[] rules = _alertDocument.Rules
            .Where(rule => rule.Enabled &&
                           rule.ChartId == context.PaneId &&
                           string.Equals(rule.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(rule.Timeframe, context.Timeframe.DisplayText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rules.Length == 0)
            return;

        bool changed = false;
        foreach (AlertRule rule in rules)
        {
            if (rule.Frequency == AlertFrequency.Once && rule.HasTriggered)
                continue;

            Candle candle = rule.Condition == AlertConditionType.CandleClosed
                ? latestClosedCandle ?? liveCandle
                : liveCandle;
            bool condition = TryEvaluateAlertCondition(rule, context, candle, out double observed, out string detail);
            _alertPreviousStates.TryGetValue(rule.Id, out bool previousState);
            bool risingState = condition && !previousState;
            bool shouldTrigger = rule.Condition switch
            {
                AlertConditionType.PriceCrossesUp or
                AlertConditionType.PriceCrossesDown or
                AlertConditionType.PriceTouches or
                AlertConditionType.DrawingCross or
                AlertConditionType.IndicatorCrossesUp or
                AlertConditionType.IndicatorCrossesDown => condition,
                AlertConditionType.CandleOpened or
                AlertConditionType.CandleClosed => condition,
                _ => risingState
            };

            _alertPreviousStates[rule.Id] = condition;
            _alertPreviousValues[rule.Id] = observed;

            if (!shouldTrigger || !CanTriggerForFrequency(rule, candle))
                continue;

            string message = $"{context.Symbol} {context.Timeframe.DisplayText}: {detail}";
            AlertRule updated = rule with
            {
                HasTriggered = true,
                LastTriggeredUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastTriggeredCandleUnix = candle.StartUnix,
                LastMessage = message,
                Enabled = rule.Frequency == AlertFrequency.Once ? false : rule.Enabled
            };
            ReplaceAlertRule(updated);
            AddAlertLog(updated, message);
            NotifyAlert(updated, message);
            changed = true;
        }

        if (changed)
            SaveAlerts();
    }

    private bool TryEvaluateAlertCondition(
        AlertRule rule,
        ChartRuntimeContext context,
        Candle candle,
        out double observed,
        out string detail)
    {
        observed = GetAlertPrice(rule.PriceSource, candle);
        detail = string.Empty;
        _alertPreviousValues.TryGetValue(rule.Id, out double previous);

        switch (rule.Condition)
        {
            case AlertConditionType.PriceAbove:
                detail = $"{rule.PriceSource} {observed:G10} is above {rule.Threshold:G10}.";
                return observed > rule.Threshold;
            case AlertConditionType.PriceBelow:
                detail = $"{rule.PriceSource} {observed:G10} is below {rule.Threshold:G10}.";
                return observed < rule.Threshold;
            case AlertConditionType.PriceCrossesUp:
                detail = $"{rule.PriceSource} crossed above {rule.Threshold:G10}.";
                return previous > 0 && previous <= rule.Threshold && observed > rule.Threshold;
            case AlertConditionType.PriceCrossesDown:
                detail = $"{rule.PriceSource} crossed below {rule.Threshold:G10}.";
                return previous > 0 && previous >= rule.Threshold && observed < rule.Threshold;
            case AlertConditionType.PriceTouches:
                detail = $"Market touched {rule.Threshold:G10} ({rule.PriceSource} {observed:G10}).";
                double tolerance = Math.Max(candle.Point * 0.5, 1e-12);
                double sourceOffset = rule.PriceSource == AlertPriceSource.Ask
                    ? candle.Spread * candle.Point
                    : 0.0;
                double observedLow = Math.Min(candle.Low + sourceOffset, candle.High + sourceOffset);
                double observedHigh = Math.Max(candle.Low + sourceOffset, candle.High + sourceOffset);
                bool candleRangeTouched = rule.Threshold >= observedLow - tolerance &&
                                          rule.Threshold <= observedHigh + tolerance;
                bool closeCrossed = previous > 0 &&
                                    ((previous <= rule.Threshold && observed >= rule.Threshold) ||
                                     (previous >= rule.Threshold && observed <= rule.Threshold));
                return candleRangeTouched || Math.Abs(observed - rule.Threshold) <= tolerance || closeCrossed;
            case AlertConditionType.SpreadAbove:
                observed = candle.Spread;
                detail = $"Spread {observed:G8} points is above {rule.Threshold:G8}.";
                return observed > rule.Threshold;
            case AlertConditionType.CandleOpened:
                observed = candle.Open;
                _alertPreviousCandleStarts.TryGetValue(rule.Id, out long previousOpen);
                if (previousOpen == 0)
                {
                    _alertPreviousCandleStarts[rule.Id] = candle.StartUnix;
                    return false;
                }
                if (previousOpen == candle.StartUnix)
                    return false;
                _alertPreviousCandleStarts[rule.Id] = candle.StartUnix;
                detail = $"New candle opened at {candle.Open:G10}.";
                return true;
            case AlertConditionType.CandleClosed:
                observed = candle.Close;
                _alertPreviousCandleStarts.TryGetValue(rule.Id, out long previousClosed);
                if (!candle.IsClosed)
                    return false;
                if (previousClosed == 0)
                {
                    _alertPreviousCandleStarts[rule.Id] = candle.StartUnix;
                    return false;
                }
                if (previousClosed == candle.StartUnix)
                    return false;
                _alertPreviousCandleStarts[rule.Id] = candle.StartUnix;
                detail = $"Candle closed at {candle.Close:G10}.";
                return true;
            case AlertConditionType.DrawingCross:
                if (!TryGetDrawingPrice(context, rule.DrawingId, candle.StartUnix, out double drawingPrice))
                    return false;
                detail = $"Price crossed drawing level {drawingPrice:G10}.";
                return previous > 0 &&
                       ((previous <= drawingPrice && candle.Close > drawingPrice) ||
                        (previous >= drawingPrice && candle.Close < drawingPrice));
            case AlertConditionType.IndicatorAbove:
            case AlertConditionType.IndicatorBelow:
            case AlertConditionType.IndicatorCrossesUp:
            case AlertConditionType.IndicatorCrossesDown:
                if (!TryGetIndicatorValue(context, rule.IndicatorKey, out observed))
                    return false;
                detail = $"Indicator {observed:G10} vs {rule.Threshold:G10}.";
                return rule.Condition switch
                {
                    AlertConditionType.IndicatorAbove => observed > rule.Threshold,
                    AlertConditionType.IndicatorBelow => observed < rule.Threshold,
                    AlertConditionType.IndicatorCrossesUp => previous != 0 && previous <= rule.Threshold && observed > rule.Threshold,
                    AlertConditionType.IndicatorCrossesDown => previous != 0 && previous >= rule.Threshold && observed < rule.Threshold,
                    _ => false
                };
            default:
                return false;
        }
    }

    private static double GetAlertPrice(AlertPriceSource source, Candle candle) => source switch
    {
        AlertPriceSource.Ask => candle.Close + candle.Spread * candle.Point,
        AlertPriceSource.Last => candle.Close,
        AlertPriceSource.Close => candle.Close,
        _ => candle.Close
    };

    private static bool TryGetIndicatorValue(ChartRuntimeContext context, string key, out double value)
    {
        value = 0;
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = key.Split(':', 3);
            if (parts.Length != 3 ||
                !context.BuiltInIndicatorResults.TryGetValue(parts[1], out BuiltInIndicatorResult? builtIn))
            {
                return false;
            }
            IndicatorSeriesResult? series = builtIn.Series.FirstOrDefault(item =>
                string.Equals(item.Key, parts[2], StringComparison.OrdinalIgnoreCase));
            double? latestBuiltIn = series?.Values.LastOrDefault(item => item.HasValue);
            if (!latestBuiltIn.HasValue || !double.IsFinite(latestBuiltIn.Value))
                return false;
            value = latestBuiltIn.Value;
            return true;
        }

        if (!context.IndicatorResults.TryGetValue(key, out TickScriptIndicatorResult? result))
            return false;
        double? latest = result.Values.LastOrDefault(item => item.HasValue);
        if (!latest.HasValue || !double.IsFinite(latest.Value))
            return false;
        value = latest.Value;
        return true;
    }

    private static bool TryGetDrawingPrice(ChartRuntimeContext context, string drawingId, long timeUnix, out double price)
    {
        price = 0;
        ChartDrawing? drawing = context.Chart.ChartDrawings.FirstOrDefault(item => item.Id == drawingId);
        if (drawing is null || drawing.Anchors.Count == 0)
            return false;

        DrawingAnchor first = drawing.Anchors[0];
        if (drawing.Anchors.Count == 1 || drawing.Anchors[1].StartUnix == first.StartUnix)
        {
            price = first.Price;
            return double.IsFinite(price);
        }

        DrawingAnchor second = drawing.Anchors[1];
        double ratio = (timeUnix - first.StartUnix) / (double)(second.StartUnix - first.StartUnix);
        price = first.Price + (second.Price - first.Price) * ratio;
        return double.IsFinite(price);
    }

    private static bool CanTriggerForFrequency(AlertRule rule, Candle candle) => rule.Frequency switch
    {
        AlertFrequency.Once => !rule.HasTriggered,
        AlertFrequency.OncePerCandle => rule.LastTriggeredCandleUnix != candle.StartUnix,
        AlertFrequency.OncePerCandleClose => candle.IsClosed && rule.LastTriggeredCandleUnix != candle.StartUnix,
        _ => !rule.LastTriggeredUnix.HasValue ||
             DateTimeOffset.UtcNow.ToUnixTimeSeconds() - rule.LastTriggeredUnix.Value >= 2
    };

    private void AddAlertLog(AlertRule rule, string message)
    {
        var entry = new AlertLogEntry(
            Guid.NewGuid().ToString("N"),
            rule.Id,
            rule.Name,
            rule.Symbol,
            rule.Timeframe,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            message);
        _alertDocument = _alertDocument with
        {
            Log = _alertDocument.Log.Prepend(entry).Take(1000).ToArray()
        };
    }

    private void NotifyAlert(AlertRule rule, string message)
    {
        StatusText.Text = $"ALERT: {message}";
        if (rule.PlaySound)
            AlertBellPlayer.PlayFor(TimeSpan.FromSeconds(5));

        if (!rule.ShowDesktopPopup)
            return;

        string heading = IsPriceLineAlert(rule.Condition)
            ? "Market touched that price"
            : "Alert condition met";

        // Never block the UI/live-data dispatcher with ShowDialog. A modal alert
        // could itself create missed live candles while waiting for the user.
        if (!_alertPopupOpen)
        {
            _alertPopupOpen = true;
            var popup = new AlertTriggeredWindow(rule.Name, message, repeatSound: false, heading)
            {
                Owner = this
            };
            popup.Closed += (_, _) => _alertPopupOpen = false;
            popup.Show();
            popup.Activate();
        }
        else
        {
            var toast = new AlertToastWindow(rule.Name, message) { Owner = this };
            toast.Show();
        }
    }

    private bool TryGetReplayTargetContext(out ChartRuntimeContext context)
    {
        int? paneId = _replay?.ChartId ?? _replayMarkerChartId ?? _replaySetupChartId;
        if (paneId.HasValue && _chartContexts.TryGetValue(paneId.Value, out ChartRuntimeContext? found))
        {
            context = found;
            return true;
        }

        context = ActiveChartContext;
        _replaySetupChartId = context.PaneId;
        return !string.IsNullOrWhiteSpace(context.Symbol);
    }

    private static long GetReplayBucketStart(ReplayRuntime runtime, MarketTick tick) =>
        runtime.Context.Timeframe.GetBucketStartUnix(
            tick.TimeUnix,
            runtime.ServerUtcOffsetMinutes);

    private static bool TryPeekNextReplayTick(
        ReplayRuntime runtime,
        out MarketTick tick)
    {
        if (runtime.RedoTicks.Count > 0)
        {
            tick = runtime.RedoTicks.Peek();
            return true;
        }

        if (runtime.TickIndex >= 0 && runtime.TickIndex < runtime.Ticks.Count)
        {
            tick = runtime.Ticks[runtime.TickIndex];
            return true;
        }

        tick = default;
        return false;
    }

    private static bool ProcessNextReplayTick(
        ReplayRuntime runtime,
        bool synchronizeClock = true)
    {
        if (!TryPeekNextReplayTick(runtime, out MarketTick tick))
            return false;

        if (runtime.EndMillisecondsExclusive is long endMillisecondsExclusive &&
            tick.TimeMilliseconds >= endMillisecondsExclusive)
        {
            runtime.RangeCompleted = true;
            runtime.Engine.CompleteCurrentCandle();
            return false;
        }

        if (runtime.RedoTicks.Count > 0)
            runtime.RedoTicks.Pop();
        else
            runtime.TickIndex++;

        runtime.RangeCompleted = false;
        runtime.Engine.Process(tick);
        if (synchronizeClock)
            runtime.SimulatedMilliseconds = tick.TimeMilliseconds;
        runtime.ProcessedTicks++;
        return true;
    }

    private static bool ProcessPreviousReplayTick(ReplayRuntime runtime, bool synchronizeClock = true)
    {
        if (!runtime.Engine.TryUndoLastTick(out MarketTick undoneTick))
            return false;

        runtime.RedoTicks.Push(undoneTick);
        runtime.ProcessedTicks = Math.Max(0, runtime.ProcessedTicks - 1);
        runtime.RangeCompleted = false;
        if (synchronizeClock)
        {
            runtime.SimulatedMilliseconds = runtime.Engine.LastTick?.TimeMilliseconds
                                            ?? checked(runtime.StartUnix * 1000L);
        }
        return true;
    }

    private void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        ChartRuntimeContext context = ActiveChartContext;
        if (_replayMarkerChartId.HasValue && _replayMarkerChartId.Value != context.PaneId)
            ClearReplayMarker();
        _replaySetupChartId = context.PaneId;
        IReadOnlyList<Candle> availableCandles = context.Chart.Candles.Count > 0
            ? context.Chart.Candles
            : context.DisplayCandles;
        if (string.IsNullOrWhiteSpace(context.Symbol) || availableCandles.Count == 0)
        {
            MessageBox.Show(this, "Open a chart with saved history before starting replay.", "Market Replay", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_replay is not null && _replay.ChartId != context.PaneId)
            StopReplay(restoreChart: true);

        Candle? selectedReplayCandle = _selectedCandle is not null &&
                                       string.Equals(_selectedCandle.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                       availableCandles.Any(item => item.StartUnix == _selectedCandle.StartUnix)
            ? _selectedCandle
            : null;
        DateTime initial = Mt5ServerClock.ToDisplayTime(
                selectedReplayCandle?.StartUnix ?? availableCandles[^1].StartUnix)
            .DateTime;
        if (_replayWindow is null)
        {
            _replayWindow = new MarketReplayWindow(
                context.PaneId,
                context.Symbol,
                context.Timeframe.DisplayText,
                initial)
            {
                Owner = this
            };
            _replayWindow.ReplayLineChanged += SetReplayLineEnabled;
            _replayWindow.ReplayRangeChanged += SetReplayRangeMode;
            _replayWindow.LoadRequested += time => _ = LoadReplayAsync(time, startPlaying: false);
            _replayWindow.PlayPauseRequested += StartOrToggleReplay;
            _replayWindow.ReverseRequested += StartReverseReplay;
            _replayWindow.ForwardRequested += StartForwardReplay;
            _replayWindow.StepTickRequested += () => AdvanceReplayTicks(1);
            _replayWindow.StepCandleRequested += StepReplayCandle;
            _replayWindow.StopRequested += () => StopReplay(restoreChart: true);
            _replayWindow.SpeedChanged += ApplyReplaySpeed;
            _replayWindow.StartLineColorChanged += color => ApplyReplaySelectorColor(start: true, color);
            _replayWindow.EndLineColorChanged += color => ApplyReplaySelectorColor(start: false, color);
            _replayWindow.StartLineThicknessChanged += pixels => ApplyReplaySelectorThickness(start: true, pixels);
            _replayWindow.EndLineThicknessChanged += pixels => ApplyReplaySelectorThickness(start: false, pixels);
        }
        else
        {
            _replayWindow.Title = $"Market Replay — Chart {context.PaneId} · {context.Symbol} · {context.Timeframe.DisplayText}";
            _replayWindow.SetMarkerTime(initial);
        }

        _replayWindow.SetReplayLineStyles(
            context.Settings.ReplayStartLineColor,
            context.Settings.ReplayEndLineColor,
            context.Settings.ReplayStartLineThickness,
            context.Settings.ReplayEndLineThickness);
        if (!_replayWindow.IsVisible)
            _replayWindow.Show();
        if (_replay is null)
            _replayWindow.SetCompactMode(true);
        _replayWindow.Activate();
        UpdateReplayWindow();
        _replayWindow.SetReplayRangeChecked(_replayRangeMode);
        _replayWindow.SetReplayLineChecked(HasReplayMarker());
    }

    private void ApplyReplaySpeed(double speed)
    {
        _replaySpeed = Math.Clamp(speed, 0.01, 30000.0);
        if (_replay is { IsPlaying: true } runtime)
        {
            // Reset the wall-clock sample when speed changes so both forward
            // and reverse use the newly selected multiplier from this instant.
            runtime.LastPlaybackUtc = DateTime.UtcNow;
        }
        UpdateReplayWindow();
    }

    private void ApplyReplaySelectorColor(bool start, string color)
    {
        if (!TryGetReplayTargetContext(out ChartRuntimeContext context))
            context = ActiveChartContext;

        context.Settings = start
            ? context.Settings with { ReplayStartLineColor = color }
            : context.Settings with { ReplayEndLineColor = color };
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        context.IndicatorStack.SetChartSettings(context.Settings);
        _replayWindow?.SetReplayLineStyles(
            context.Settings.ReplayStartLineColor,
            context.Settings.ReplayEndLineColor,
            context.Settings.ReplayStartLineThickness,
            context.Settings.ReplayEndLineThickness);
        SaveWorkspace();
    }

    private void ApplyReplaySelectorThickness(bool start, double pixels)
    {
        if (!TryGetReplayTargetContext(out ChartRuntimeContext context))
            context = ActiveChartContext;

        pixels = Math.Clamp(double.IsFinite(pixels) ? pixels : 1.0, 1.0, 5.0);
        context.Settings = start
            ? context.Settings with { ReplayStartLineThickness = pixels }
            : context.Settings with { ReplayEndLineThickness = pixels };
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        context.IndicatorStack.SetChartSettings(context.Settings);
        _replayWindow?.SetReplayLineStyles(
            context.Settings.ReplayStartLineColor,
            context.Settings.ReplayEndLineColor,
            context.Settings.ReplayStartLineThickness,
            context.Settings.ReplayEndLineThickness);
        SaveWorkspace();
    }

    private static bool IsReplayMarker(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] CandleMarker? marker) =>
        marker is not null && marker.Source.StartsWith("TickLabReplay", StringComparison.OrdinalIgnoreCase);

    private static bool IsReplayEndMarker(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] CandleMarker? marker) =>
        marker is not null && string.Equals(marker.Source, "TickLabReplayEnd", StringComparison.OrdinalIgnoreCase);

    private bool HasReplayMarker()
    {
        if (!_replayMarkerChartId.HasValue ||
            !_chartContexts.TryGetValue(_replayMarkerChartId.Value, out ChartRuntimeContext? context))
            return false;
        bool hasStart = IsReplayMarker(context.Chart.InteractiveSelectionMarker) &&
                        !IsReplayEndMarker(context.Chart.InteractiveSelectionMarker);
        bool hasEnd = IsReplayEndMarker(context.Chart.InteractiveReplayEndMarker);
        return hasStart && (!_replayRangeMode || hasEnd);
    }

    private void SetReplayLineEnabled(bool enabled)
    {
        if (enabled)
            EnableReplayMarker();
        else if (_replay is not null)
            StopReplay(restoreChart: true, clearMarker: true);
        else
        {
            ClearReplayMarker();
            UpdateReplayWindow("Replay line removed. Tick Replay line to choose another past candle.");
        }
    }

    private void SetReplayRangeMode(bool enabled)
    {
        if (_replayRangeMode == enabled)
            return;

        _replayRangeMode = enabled;
        if (_replay is not null)
            StopReplay(restoreChart: true, clearMarker: false);

        if (!TryGetReplayTargetContext(out ChartRuntimeContext context))
            return;

        if (!enabled)
        {
            context.Chart.InteractiveReplayEndMarker = null;
            UpdateReplayWindow("Single-start replay selected. Yellow line = replay start.");
            return;
        }

        if (IsReplayMarker(context.Chart.InteractiveSelectionMarker))
        {
            PlaceOrRefreshReplayEndMarker(context, keepExistingStart: true);
            context.Chart.MarkerSelectionMode = true;
            _replayWindow?.SetReplayLineChecked(true);
            UpdateReplayWindow("Replay range selected. Yellow = start, red = end. Drag either line, then press Play.");
        }
        else
        {
            UpdateReplayWindow("Replay range selected. Tick Replay line to show yellow start and red end selectors.");
        }
    }

    private void EnableReplayMarker()
    {
        if (!TryGetReplayTargetContext(out ChartRuntimeContext context))
            return;
        IReadOnlyList<Candle> availableCandles = context.Chart.Candles.Count > 0
            ? context.Chart.Candles
            : context.DisplayCandles;
        if (availableCandles.Count == 0)
        {
            _replayWindow?.SetReplayLineChecked(false);
            return;
        }

        if (_replay is not null)
            StopReplay(restoreChart: true, clearMarker: false);

        ClearReplayMarker(syncCheckBox: false);
        _replayMarkerChartId = context.PaneId;
        _replaySetupChartId = context.PaneId;

        (int firstVisible, int lastExclusive) = GetReplayVisibleRange(context, availableCandles.Count);
        int visibleCount = Math.Max(1, lastExclusive - firstVisible);
        int startIndex = _replayRangeMode
            ? firstVisible + Math.Clamp((int)Math.Round((visibleCount - 1) * 0.35), 0, visibleCount - 1)
            : firstVisible + Math.Clamp((visibleCount - 1) / 2, 0, visibleCount - 1);
        Candle startCandle = availableCandles[Math.Clamp(startIndex, 0, availableCandles.Count - 1)];

        context.Chart.InteractiveSelectionMarker = new CandleMarker(
            "TL_REPLAY_" + Guid.NewGuid().ToString("N"),
            context.Symbol,
            context.Timeframe.DisplayText,
            startCandle.StartUnix,
            "Replay start",
            "TickLabReplay",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        context.Chart.MarkerSelectionMode = true;

        if (_replayRangeMode)
            PlaceOrRefreshReplayEndMarker(context, keepExistingStart: true);
        else
            context.Chart.InteractiveReplayEndMarker = null;

        _replayWindow?.SetMarkerTime(Mt5ServerClock.ToDisplayTime(startCandle.StartUnix).DateTime);
        _replayWindow?.SetReplayLineChecked(true);
        _replayWindow?.SetState(
            false,
            false,
            _replayRangeMode
                ? "Replay range ready. Yellow = start, red = end. Drag either line, then press Play."
                : "Replay start ready. Drag the yellow line if needed, then press Play.",
            "The selector was placed inside the candles currently visible on this chart. Moving the selector never starts replay.");
    }

    private void PlaceOrRefreshReplayEndMarker(ChartRuntimeContext context, bool keepExistingStart)
    {
        IReadOnlyList<Candle> availableCandles = context.Chart.Candles.Count > 0
            ? context.Chart.Candles
            : context.DisplayCandles;
        if (availableCandles.Count == 0)
            return;

        (int firstVisible, int lastExclusive) = GetReplayVisibleRange(context, availableCandles.Count);
        int visibleCount = Math.Max(1, lastExclusive - firstVisible);
        int endIndex = firstVisible + Math.Clamp((int)Math.Round((visibleCount - 1) * 0.65), 0, visibleCount - 1);

        CandleMarker? startMarker = context.Chart.InteractiveSelectionMarker;
        if (keepExistingStart && IsReplayMarker(startMarker) && endIndex < availableCandles.Count &&
            availableCandles[endIndex].StartUnix <= startMarker.StartUnix)
        {
            int startIndex = availableCandles.ToList().FindIndex(item => item.StartUnix == startMarker.StartUnix);
            if (startIndex >= 0)
                endIndex = Math.Min(availableCandles.Count - 1, Math.Max(startIndex + 1, endIndex));
        }

        Candle endCandle = availableCandles[Math.Clamp(endIndex, 0, availableCandles.Count - 1)];
        context.Chart.InteractiveReplayEndMarker = new CandleMarker(
            "TL_REPLAY_END_" + Guid.NewGuid().ToString("N"),
            context.Symbol,
            context.Timeframe.DisplayText,
            endCandle.StartUnix,
            "Replay end",
            "TickLabReplayEnd",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static (int FirstVisible, int LastExclusive) GetReplayVisibleRange(
        ChartRuntimeContext context,
        int candleCount)
    {
        if (candleCount <= 0)
            return (0, 0);

        ChartViewportSnapshot? viewport = context.Chart.CaptureViewportSnapshot();
        if (viewport is not null)
        {
            int first = Math.Clamp(viewport.FirstIndex, 0, candleCount - 1);
            int last = Math.Clamp(viewport.LastExclusive, first + 1, candleCount);
            return (first, last);
        }

        int fallbackVisible = Math.Min(candleCount, 110);
        return (Math.Max(0, candleCount - fallbackVisible), candleCount);
    }

    private static long GetReplayPreparationEndUnix(
        ChartRuntimeContext context,
        long startUnix,
        long? endUnixExclusive,
        int serverUtcOffsetMinutes)
    {
        // Replay only needs the selected candle and a small read-ahead window
        // to start. Never hold Play while an entire three-month segment is
        // canonicalized.
        long bucketEnd = context.Timeframe.GetBucketEndUnix(
            startUnix,
            serverUtcOffsetMinutes);
        long preferredEnd = Math.Max(bucketEnd, checked(startUnix + 3600L));
        preferredEnd = Math.Min(preferredEnd, checked(startUnix + 7200L));
        if (endUnixExclusive.HasValue)
            preferredEnd = Math.Min(preferredEnd, endUnixExclusive.Value);
        return Math.Max(startUnix + 1, preferredEnd);
    }

    private async Task<CanonicalTickReadResult> ReadReplayTicksImmediatelyAsync(
        Mt5ConnectorSummary connector,
        ChartRuntimeContext context,
        long startUnix,
        long? endUnixExclusive)
    {
        string connectorId = connector.ConnectorId;
        string symbol = context.Symbol;
        long startMilliseconds = checked(startUnix * 1000L);
        long preparationEndUnix = GetReplayPreparationEndUnix(
            context,
            startUnix,
            endUnixExclusive,
            connector.ServerUtcOffsetMinutes);
        long preparationEndMilliseconds = checked(preparationEndUnix * 1000L);
        long canonicalReadEndMilliseconds = endUnixExclusive.HasValue
            ? Math.Min(
                preparationEndMilliseconds,
                checked(endUnixExclusive.Value * 1000L))
            : preparationEndMilliseconds;

        // Primary instant Play path: TickLab's permanent ticks.tlt archive is
        // already binary-searchable by timestamp. Seek it first so pressing Play
        // never scans a large bridge CSV before replay can begin.
        CanonicalTickReadResult permanentRead = await Task.Run(
            () => _historyStore.ReadTicksForReplay(
                connectorId,
                symbol,
                startMilliseconds,
                endMilliseconds: canonicalReadEndMilliseconds,
                maximumRecords: 250_000,
                cancellationToken: _lifetime.Token),
            _lifetime.Token);
        if (permanentRead.Ticks.Count > 0)
            return permanentRead;

        // Very recent ticks may not have reached ticks.tlt yet. Only then fall
        // back to the small matching bridge source slice and warm the permanent
        // archive in the background after replay has already started.
        try
        {
            CanonicalTickReadResult bridgeRead = await Task.Run(
                () => _historyStore.ReadBridgeTicksForReplayFast(
                    connectorId,
                    symbol,
                    startMilliseconds,
                    preparationEndMilliseconds,
                    maximumRecords: 250_000,
                    serverUtcOffsetMinutes: connector.ServerUtcOffsetMinutes,
                    cancellationToken: _lifetime.Token),
                _lifetime.Token);

            if (bridgeRead.Ticks.Count > 0)
            {
                ScheduleReplayCanonicalWarmup(
                    connectorId,
                    symbol,
                    connector.ServerUtcOffsetMinutes,
                    startUnix,
                    preparationEndUnix);
                return bridgeRead;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return permanentRead;
    }

    private void ScheduleReplayCanonicalWarmup(
        string connectorId,
        string symbol,
        int serverUtcOffsetMinutes,
        long startUnix,
        long endUnixExclusive)
    {
        if (_isClosing)
            return;

        long minimumStartUnix = Math.Max(0, startUnix - 60);
        long maximumEndUnix = Math.Max(minimumStartUnix, endUnixExclusive - 1);
        string segmentKey = PersistentHistoryStore.GetSegmentKey(
            startUnix,
            serverUtcOffsetMinutes);

        Task background = Task.Run(() => _historyStore.SyncTickArchives(
            connectorId,
            symbol,
            CancellationToken.None,
            includeHistorical: true,
            serverUtcOffsetMinutes: serverUtcOffsetMinutes,
            minimumStartUnix: minimumStartUnix,
            onlySegmentKey: segmentKey,
            maximumEndUnix: maximumEndUnix));
        _ = background.ContinueWith(
            completed =>
            {
                Exception? error = completed.Exception?.GetBaseException();
                if (error is not null)
                {
                    TickLabErrorEngine.Report(
                        error,
                        new TickLabErrorContext(
                            "Replay index warmup",
                            "bounded_replay_tick_index",
                            "Replay used the saved raw source directly. The persistent replay index can be retried later without affecting saved ticks.",
                            ErrorCode: "TL-REPLAY-INDEX-WARMUP",
                            Symbol: symbol,
                            ConnectorId: connectorId),
                        TickLabErrorSeverity.Warning,
                        owner: null,
                        showPopup: false);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async Task LoadReplayAsync(DateTime serverDateTime, bool startPlaying = false)
    {
        _ = serverDateTime;
        if (_replayLoading || _selectedConnector is null)
            return;

        Mt5ConnectorSummary connector = _selectedConnector;
        if (!TryGetReplayTargetContext(out ChartRuntimeContext context))
            return;

        string replaySymbol = context.Symbol;
        string replayTimeframeKey = context.Timeframe.Key;
        _replayLoading = true;
        ReplayRuntime? runtime = null;
        try
        {
            CandleMarker? replayMarker = context.Chart.InteractiveSelectionMarker;
            CandleMarker? replayEndMarker = _replayRangeMode
                ? context.Chart.InteractiveReplayEndMarker
                : null;
            if (!IsReplayMarker(replayMarker) || IsReplayEndMarker(replayMarker))
            {
                _replayWindow?.SetState(
                    false,
                    false,
                    "Replay start is not selected.",
                    "Tick Replay line to place the yellow start selector inside the visible chart.");
                return;
            }
            if (_replayRangeMode && !IsReplayEndMarker(replayEndMarker))
            {
                _replayWindow?.SetState(
                    false,
                    false,
                    "Replay end is not selected.",
                    "Replay range requires both selectors: yellow start and red end.");
                return;
            }

            context.Chart.MarkerSelectionMode = false;
            StopReplay(restoreChart: true, clearMarker: false);

            long requestedUnix = replayMarker.StartUnix;
            long startUnix = context.Timeframe.GetBucketStartUnix(
                requestedUnix,
                connector.ServerUtcOffsetMinutes);
            long? endUnixExclusive = null;
            if (_replayRangeMode && replayEndMarker is not null)
            {
                long endBucketStart = context.Timeframe.GetBucketStartUnix(
                    replayEndMarker.StartUnix,
                    connector.ServerUtcOffsetMinutes);
                if (endBucketStart <= startUnix)
                {
                    _replayWindow?.SetState(
                        false,
                        false,
                        "Invalid replay range.",
                        "The red end line must be to the right of the yellow start line.");
                    return;
                }
                endUnixExclusive = context.Timeframe.GetBucketEndUnix(
                    endBucketStart,
                    connector.ServerUtcOffsetMinutes);
            }

            List<Candle> currentDisplay = context.Chart.Candles.Count > 0
                ? context.Chart.Candles.ToList()
                : context.DisplayCandles.ToList();
            if (currentDisplay.Count == 0)
                throw new InvalidOperationException(
                    "The selected chart has no candle history to anchor replay.");

            Candle sample = currentDisplay.FirstOrDefault(item => item.StartUnix >= startUnix)
                            ?? currentDisplay[^1];
            List<Candle> completed = currentDisplay
                .Where(item => item.EndUnix <= startUnix)
                .TakeLast(12_000)
                .ToList();
            var engine = new MarketReplayEngine(
                context.Symbol,
                context.Timeframe,
                sample.Digits,
                sample.Point,
                connector.ServerUtcOffsetMinutes,
                completed);

            List<Candle> originalSource = context.PaneId == _activePricePaneId
                ? _sourceCandles.ToList()
                : context.SourceCandles.ToList();
            List<Candle> originalDisplay = currentDisplay.ToList();
            ChartWindowAnchor? replayStartViewportAnchor =
                context.Chart.CaptureWindowAnchorAtOrBefore(
                    startUnix,
                    excludeExact: true);

            runtime = new ReplayRuntime
            {
                ChartId = context.PaneId,
                Context = context,
                ConnectorId = connector.ConnectorId,
                ServerUtcOffsetMinutes = connector.ServerUtcOffsetMinutes,
                OriginalSourceCandles = originalSource,
                OriginalDisplayCandles = originalDisplay,
                HiddenLiveSourceCandles = originalSource.ToList(),
                HiddenLiveDisplayCandles = originalDisplay.ToList(),
                OriginalViewport = context.Chart.CaptureViewport(),
                OriginalOlderLoaded = context.AllOlderHistoryLoaded,
                OriginalNewerLoaded = context.AllNewerHistoryLoaded,
                Engine = engine,
                Ticks = new List<MarketTick>(),
                ReplayStartViewportAnchor = replayStartViewportAnchor,
                TickIndex = 0,
                HasMore = true,
                NextStartMilliseconds = checked(startUnix * 1000L),
                StartUnix = startUnix,
                EndUnixExclusive = endUnixExclusive,
                SimulatedMilliseconds = checked(startUnix * 1000L),
                LastPlaybackUtc = DateTime.UtcNow
            };
            _replay = runtime;
            _replayDirection = ReplayPlaybackDirection.Forward;
            _replayWindow?.SetPlaybackDirection(reverse: false);

            ResetContextHistoryPaging(context);
            // ResetContextHistoryPaging intentionally increments IdentityGeneration.
            // Capture the replay guard AFTER that self-triggered reset; otherwise
            // Replay cancels itself after the asynchronous tick read and reports
            // that the target changed even when the user changed nothing.
            int identityGeneration = context.IdentityGeneration;
            context.AllOlderHistoryLoaded = true;
            context.AllNewerHistoryLoaded = true;
            UpdateChartPagingAvailability(context);
            context.Chart.InteractiveSelectionMarker = replayMarker;
            context.Chart.InteractiveReplayEndMarker = _replayRangeMode
                ? replayEndMarker
                : null;
            context.Chart.MarkerSelectionMode = true;
            _replayMarkerChartId = context.PaneId;
            _replaySetupChartId = context.PaneId;
            _replayWindow?.SetReplayLineChecked(true);
            _replayWindow?.SetReplayRangeChecked(_replayRangeMode);

            // IMPORTANT: switch the visible chart immediately. The candle under
            // the yellow line and every candle to its right disappear before
            // any disk/index work starts. The live bridge continues into the
            // hidden live snapshot while Replay owns only the visible copy.
            RenderReplayChart(forceFit: false);
            _replayWindow?.SetState(
                loaded: false,
                playing: false,
                status: "Starting replay…",
                progress: "Future candles are hidden. Opening the saved raw ticks at the yellow start line…");

            CanonicalTickReadResult read = await ReadReplayTicksImmediatelyAsync(
                connector,
                context,
                startUnix,
                endUnixExclusive);

            if (!_chartContexts.TryGetValue(context.PaneId, out ChartRuntimeContext? liveContext) ||
                !ReferenceEquals(liveContext, context) ||
                context.IdentityGeneration != identityGeneration ||
                !string.Equals(context.Symbol, replaySymbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(context.Timeframe.Key, replayTimeframeKey, StringComparison.OrdinalIgnoreCase) ||
                _replay is null ||
                !ReferenceEquals(_replay, runtime))
            {
                if (_replay is not null && ReferenceEquals(_replay, runtime))
                    StopReplay(restoreChart: true, clearMarker: false);
                _replayWindow?.SetState(
                    false,
                    false,
                    "Replay target changed.",
                    "Open Replay again on the chart you want to replay.");
                return;
            }

            CandleMarker? currentMarker = context.Chart.InteractiveSelectionMarker;
            CandleMarker? currentEndMarker = context.Chart.InteractiveReplayEndMarker;
            bool startChanged = currentMarker is null ||
                                !string.Equals(currentMarker.Id, replayMarker.Id, StringComparison.Ordinal) ||
                                currentMarker.StartUnix != replayMarker.StartUnix;
            bool endChanged = _replayRangeMode &&
                              (replayEndMarker is null ||
                               currentEndMarker is null ||
                               !string.Equals(currentEndMarker.Id, replayEndMarker.Id, StringComparison.Ordinal) ||
                               currentEndMarker.StartUnix != replayEndMarker.StartUnix);
            if (startChanged || endChanged)
            {
                StopReplay(restoreChart: true, clearMarker: false);
                _replayWindow?.SetState(
                    false,
                    false,
                    "Replay loading cancelled.",
                    "A replay selector was moved or removed. Press Play again after choosing the final start/end positions.");
                return;
            }

            if (read.Ticks.Count == 0)
            {
                StopReplay(restoreChart: true, clearMarker: false);
                MessageBox.Show(
                    this,
                    "Replay unavailable — no saved raw tick data exists for the selected period.",
                    "Market Replay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _replayWindow?.SetState(
                    false,
                    false,
                    "No tick data available.",
                    "Choose a candle covered by the saved raw tick archive.");
                return;
            }

            runtime.Ticks = read.Ticks.ToList();
            runtime.TickIndex = 0;
            runtime.NextStartMilliseconds = read.NextStartMilliseconds;
            long initialReadEndUnix = GetReplayPreparationEndUnix(
                context,
                startUnix,
                endUnixExclusive,
                connector.ServerUtcOffsetMinutes);
            bool initialSliceReachesRangeEnd = endUnixExclusive.HasValue &&
                                               initialReadEndUnix >= endUnixExclusive.Value;
            runtime.HasMore = read.HasMore || !initialSliceReachesRangeEnd;
            runtime.SimulatedMilliseconds = runtime.Ticks[0].TimeMilliseconds;
            runtime.LastPlaybackUtc = DateTime.UtcNow;

            // Reveal only the first raw tick of the selected candle, then let
            // the replay clock build the rest exactly tick by tick.
            ProcessNextReplayTick(runtime);
            RenderReplayChart(forceFit: false);
            if (startPlaying && !runtime.RangeCompleted)
            {
                runtime.IsPlaying = true;
                runtime.LastPlaybackUtc = DateTime.UtcNow;
                _replayTimer.Start();
            }

            UpdateReplayWindow();
            if (startPlaying && runtime.IsPlaying)
                _replayWindow?.SetCompactMode(true);
            StatusText.Text =
                $"Replay started for Chart {context.PaneId}: {context.Symbol} {context.Timeframe.DisplayText}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (runtime is not null &&
                _replay is not null &&
                ReferenceEquals(runtime, _replay))
            {
                StopReplay(restoreChart: true, clearMarker: false);
            }

            MessageBox.Show(
                this,
                exception.Message,
                "Market Replay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _replayLoading = false;
            if (_replay is null && IsReplayMarker(context.Chart.InteractiveSelectionMarker))
                context.Chart.MarkerSelectionMode = true;
        }
    }

    private async void StartOrToggleReplay()
    {
        if (_replayLoading)
            return;

        if (_replay is null)
        {
            _replayDirection = ReplayPlaybackDirection.Forward;
            _replayWindow?.SetPlaybackDirection(reverse: false);
            if (!TryGetReplayTargetContext(out ChartRuntimeContext context) ||
                !IsReplayMarker(context.Chart.InteractiveSelectionMarker) ||
                IsReplayEndMarker(context.Chart.InteractiveSelectionMarker))
            {
                UpdateReplayWindow("Tick Replay line first, position the yellow start selector, then press Play.");
                return;
            }

            if (_replayRangeMode && !IsReplayEndMarker(context.Chart.InteractiveReplayEndMarker))
            {
                UpdateReplayWindow("Replay range needs both selectors. Yellow = start, red = end.");
                return;
            }

            DateTime serverTime = Mt5ServerClock
                .ToDisplayTime(context.Chart.InteractiveSelectionMarker!.StartUnix)
                .DateTime;
            await LoadReplayAsync(serverTime, startPlaying: true);
            return;
        }

        if (_replay.RangeCompleted)
        {
            UpdateReplayWindow("Replay range is complete. Move the selector(s), then press Play to start again.");
            return;
        }

        _replay.IsPlaying = !_replay.IsPlaying;
        if (_replay.IsPlaying)
        {
            _replay.LastPlaybackUtc = DateTime.UtcNow;
            _replayTimer.Start();
        }
        else
        {
            _replayTimer.Stop();
        }
        UpdateReplayWindow();
        if (_replay.IsPlaying)
            _replayWindow?.SetCompactMode(true);
    }

    private void StartReverseReplay()
    {
        if (_replayLoading)
            return;

        if (_replay is null)
        {
            UpdateReplayWindow("Start replay first, then Reverse can run back toward the yellow start line.");
            return;
        }

        if (!_replay.Engine.CanUndo)
        {
            _replay.IsPlaying = false;
            _replayTimer.Stop();
            UpdateReplayWindow("Replay is already at the yellow start line.");
            return;
        }

        ReplayRuntime runtime = _replay;
        _replayTimer.Stop();
        _replaySpeed = Math.Clamp(_replayWindow?.SelectedSpeed ?? _replaySpeed, 0.01, 30000.0);
        _replayDirection = ReplayPlaybackDirection.Reverse;
        runtime.RangeCompleted = false;
        runtime.IsPlaying = false;
        _replayWindow?.SetPlaybackDirection(reverse: true);

        // Make the first reverse response obey the same selected speed as the
        // continuous timer. Move the simulated clock by one timer quantum and
        // undo every tick inside that interval. If the selected speed is slow
        // and no earlier timestamp falls inside it, undo one tick so the button
        // still gives immediate visible feedback.
        long currentMilliseconds = runtime.Engine.LastTick?.TimeMilliseconds
                                   ?? checked(runtime.StartUnix * 1000L);
        double firstQuantum = _replayTimer.Interval.TotalMilliseconds * _replaySpeed;
        runtime.SimulatedMilliseconds = currentMilliseconds - firstQuantum;
        int firstReversed = 0;
        int firstReverseBudget = GetReplayTickBudget();
        while (runtime.Engine.LastTick is MarketTick currentTick &&
               currentTick.TimeMilliseconds > runtime.SimulatedMilliseconds &&
               firstReversed < firstReverseBudget)
        {
            if (!ProcessPreviousReplayTick(runtime, synchronizeClock: false))
                break;
            firstReversed++;
        }
        if (firstReversed == 0 && ProcessPreviousReplayTick(runtime, synchronizeClock: false))
            firstReversed = 1;
        if (firstReversed > 0)
            RenderReplayChart(forceFit: false);

        if (!runtime.Engine.CanUndo)
        {
            runtime.IsPlaying = false;
            runtime.SimulatedMilliseconds = checked(runtime.StartUnix * 1000L);
            UpdateReplayWindow("Replay returned to the yellow start line.");
            return;
        }

        runtime.IsPlaying = true;
        runtime.SimulatedMilliseconds = Math.Min(
            runtime.SimulatedMilliseconds,
            runtime.Engine.LastTick?.TimeMilliseconds ?? checked(runtime.StartUnix * 1000L));
        runtime.LastPlaybackUtc = DateTime.UtcNow;
        _replayTimer.Start();
        UpdateReplayWindow();
    }

    private async void StartForwardReplay()
    {
        if (_replayLoading)
            return;

        if (_replay is null)
        {
            if (!TryGetReplayTargetContext(out ChartRuntimeContext context) ||
                !IsReplayMarker(context.Chart.InteractiveSelectionMarker) ||
                IsReplayEndMarker(context.Chart.InteractiveSelectionMarker))
            {
                UpdateReplayWindow("Tick Replay line first, position the yellow start selector, then press Forward.");
                return;
            }

            if (_replayRangeMode && !IsReplayEndMarker(context.Chart.InteractiveReplayEndMarker))
            {
                UpdateReplayWindow("Replay range needs both selectors. Yellow = start, red = end.");
                return;
            }

            DateTime serverTime = Mt5ServerClock
                .ToDisplayTime(context.Chart.InteractiveSelectionMarker!.StartUnix)
                .DateTime;
            _replayDirection = ReplayPlaybackDirection.Forward;
            _replayWindow?.SetPlaybackDirection(reverse: false);
            await LoadReplayAsync(serverTime, startPlaying: true);
            return;
        }

        if (_replay.RangeCompleted && _replay.RedoTicks.Count == 0)
        {
            UpdateReplayWindow("Replay range is complete. Reverse first or move the selector(s) to start again.");
            return;
        }

        _replayDirection = ReplayPlaybackDirection.Forward;
        _replay.RangeCompleted = false;
        _replay.IsPlaying = true;
        _replay.SimulatedMilliseconds = _replay.Engine.LastTick?.TimeMilliseconds
                                        ?? checked(_replay.StartUnix * 1000L);
        _replay.LastPlaybackUtc = DateTime.UtcNow;
        _replayWindow?.SetPlaybackDirection(reverse: false);
        _replayTimer.Start();
        UpdateReplayWindow();
    }

    private void CompleteReplayRangeImmediately(ReplayRuntime runtime)
    {
        if (_replay is null || !ReferenceEquals(runtime, _replay))
            return;

        runtime.IsPlaying = false;
        runtime.RangeCompleted = true;
        runtime.HasMore = false;
        _replayTimer.Stop();
        runtime.Engine.CompleteCurrentCandle();

        // The playback state is authoritative immediately. Do not leave the
        // compact/full Replay button showing Pause while any final chart or
        // indicator redraw is still being completed.
        UpdateReplayWindow("Replay range complete at the red end line.");

        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_replay is not null && ReferenceEquals(runtime, _replay))
                    RenderReplayChart(forceFit: false);
            }),
            DispatcherPriority.Background);
    }

    private int GetReplayTickBudget() =>
        _replaySpeed >= 5000.0
            ? 750_000
            : _replaySpeed >= 100.0
                ? 350_000
                : 20_000;

    private bool ShouldRenderReplayFrame(ReplayRuntime runtime)
    {
        if (_replaySpeed < 100.0)
        {
            runtime.LastVisualRefreshUtc = DateTime.UtcNow;
            return true;
        }

        DateTime now = DateTime.UtcNow;
        if (now - runtime.LastVisualRefreshUtc < TimeSpan.FromMilliseconds(100))
            return false;
        runtime.LastVisualRefreshUtc = now;
        return true;
    }

    private void ReplayTimer_Tick(object? sender, EventArgs e)
    {
        ReplayRuntime? runtime = _replay;
        if (runtime is null || !runtime.IsPlaying || _replayLoading)
            return;

        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - runtime.LastPlaybackUtc).TotalMilliseconds, 0, 1000);
        runtime.LastPlaybackUtc = now;
        double scaledElapsed = elapsed * Math.Max(0.01, _replaySpeed);

        if (_replayDirection == ReplayPlaybackDirection.Reverse)
        {
            runtime.RangeCompleted = false;
            runtime.SimulatedMilliseconds -= scaledElapsed;

            long? previousTickTime = runtime.Engine.PreviousTickTimeMilliseconds;
            if (previousTickTime.HasValue &&
                runtime.SimulatedMilliseconds - previousTickTime.Value > 60_000)
            {
                // Skip closed sessions/weekends in reverse just as forward
                // replay skips them, while preserving timing within active data.
                runtime.SimulatedMilliseconds = previousTickTime.Value;
            }

            int reversed = 0;
            int reverseBudget = GetReplayTickBudget();
            while (runtime.Engine.LastTick is MarketTick currentTick &&
                   currentTick.TimeMilliseconds > runtime.SimulatedMilliseconds &&
                   reversed < reverseBudget)
            {
                if (!ProcessPreviousReplayTick(runtime, synchronizeClock: false))
                    break;
                reversed++;
            }

            if (reversed > 0 && ShouldRenderReplayFrame(runtime))
                RenderReplayChart(forceFit: false);

            if (!runtime.Engine.CanUndo)
            {
                runtime.IsPlaying = false;
                _replayTimer.Stop();
                runtime.SimulatedMilliseconds = checked(runtime.StartUnix * 1000L);
                UpdateReplayWindow("Replay returned to the yellow start line.");
                return;
            }

            UpdateReplayWindow();
            return;
        }

        runtime.SimulatedMilliseconds += scaledElapsed;

        if (TryPeekNextReplayTick(runtime, out MarketTick nextTick) &&
            nextTick.TimeMilliseconds - runtime.SimulatedMilliseconds > 60_000)
        {
            // Skip closed sessions/weekends but preserve exact timing inside active tick periods.
            runtime.SimulatedMilliseconds = nextTick.TimeMilliseconds;
        }

        int processed = 0;
        int forwardBudget = GetReplayTickBudget();
        while (TryPeekNextReplayTick(runtime, out nextTick) &&
               nextTick.TimeMilliseconds <= runtime.SimulatedMilliseconds &&
               processed < forwardBudget)
        {
            if (!ProcessNextReplayTick(runtime, synchronizeClock: false))
                break;
            processed++;
        }

        if (runtime.RangeCompleted)
        {
            CompleteReplayRangeImmediately(runtime);
            return;
        }

        if (processed > 0 && ShouldRenderReplayFrame(runtime))
            RenderReplayChart(forceFit: false);

        if (runtime.RedoTicks.Count == 0 && runtime.TickIndex >= runtime.Ticks.Count)
        {
            if (runtime.HasMore)
                _ = LoadNextReplayBatchAsync();
            else
            {
                if (runtime.EndUnixExclusive.HasValue)
                {
                    CompleteReplayRangeImmediately(runtime);
                }
                else
                {
                    runtime.IsPlaying = false;
                    _replayTimer.Stop();
                    runtime.Engine.CompleteCurrentCandle();
                    RenderReplayChart(forceFit: false);
                    UpdateReplayWindow("Replay reached the end of available tick data.");
                }
            }
        }
        else
        {
            UpdateReplayWindow();
        }
    }

    private async void AdvanceReplayTicks(int count)
    {
        if (_replay is null || _replayLoading || count <= 0)
            return;

        try
        {
            _replay.IsPlaying = false;
            _replayDirection = ReplayPlaybackDirection.Forward;
            _replayWindow?.SetPlaybackDirection(reverse: false);
            _replayTimer.Stop();
            int processed = 0;
            while (_replay is ReplayRuntime runtime && processed < count)
            {
                if (runtime.RedoTicks.Count == 0 &&
                    runtime.TickIndex >= runtime.Ticks.Count)
                {
                    if (!runtime.HasMore)
                        break;
                    await LoadNextReplayBatchAsync();
                    continue;
                }

                if (!ProcessNextReplayTick(runtime))
                    break;
                processed++;
            }

            if (processed > 0 || (_replay?.RangeCompleted ?? false))
                RenderReplayChart(forceFit: false);

            if (_replay is ReplayRuntime current && current.RangeCompleted)
            {
                current.IsPlaying = false;
                UpdateReplayWindow("Replay range complete at the red end line.");
            }
            else if (_replay is ReplayRuntime exhausted &&
                     exhausted.RedoTicks.Count == 0 &&
                     exhausted.TickIndex >= exhausted.Ticks.Count &&
                     !exhausted.HasMore)
            {
                exhausted.Engine.CompleteCurrentCandle();
                if (exhausted.EndUnixExclusive.HasValue)
                    exhausted.RangeCompleted = true;
                RenderReplayChart(forceFit: false);
                UpdateReplayWindow(exhausted.EndUnixExclusive.HasValue
                    ? "Replay range complete at the red end line."
                    : "Replay reached the end of available tick data.");
            }
            else
            {
                UpdateReplayWindow();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _replayTimer.Stop();
            if (_replay is not null)
                _replay.IsPlaying = false;
            UpdateReplayWindow($"Replay paused: {exception.Message}");
        }
    }

    private async void StepReplayCandle()
    {
        if (_replay is null || _replayLoading)
            return;

        try
        {
            _replay.IsPlaying = false;
            _replayDirection = ReplayPlaybackDirection.Forward;
            _replayWindow?.SetPlaybackDirection(reverse: false);
            _replayTimer.Stop();
            ReplayRuntime runtime = _replay;
            long? targetBucketStart = runtime.Engine.CurrentCandleStartUnix;
            int processed = 0;

            while (_replay is not null && ReferenceEquals(runtime, _replay) && processed < 1_000_000)
            {
                if (runtime.RedoTicks.Count == 0 &&
                    runtime.TickIndex >= runtime.Ticks.Count)
                {
                    if (!runtime.HasMore)
                    {
                        runtime.Engine.CompleteCurrentCandle();
                        break;
                    }

                    await LoadNextReplayBatchAsync();
                    if (_replay is null || !ReferenceEquals(runtime, _replay) ||
                        (runtime.RedoTicks.Count == 0 && runtime.Ticks.Count == 0))
                        break;
                }

                if (!TryPeekNextReplayTick(runtime, out MarketTick nextTick))
                    break;
                long nextBucketStart = GetReplayBucketStart(runtime, nextTick);
                targetBucketStart ??= nextBucketStart;

                if (nextBucketStart != targetBucketStart.Value)
                {
                    // Close the selected candle without consuming the first tick of
                    // the following candle, so one Step candle reveals exactly one bar.
                    runtime.Engine.CompleteCurrentCandle();
                    break;
                }

                if (!ProcessNextReplayTick(runtime))
                    break;
                processed++;
            }

            if (processed > 0 || targetBucketStart.HasValue || runtime.RangeCompleted)
                RenderReplayChart(forceFit: false);
            if (runtime.RangeCompleted)
            {
                runtime.IsPlaying = false;
                UpdateReplayWindow("Replay range complete at the red end line.");
            }
            else if (runtime.RedoTicks.Count == 0 &&
                     runtime.TickIndex >= runtime.Ticks.Count &&
                     !runtime.HasMore &&
                     runtime.EndUnixExclusive.HasValue)
            {
                runtime.Engine.CompleteCurrentCandle();
                runtime.RangeCompleted = true;
                RenderReplayChart(forceFit: false);
                UpdateReplayWindow("Replay range complete at the red end line.");
            }
            else
            {
                UpdateReplayWindow();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _replayTimer.Stop();
            if (_replay is not null)
                _replay.IsPlaying = false;
            UpdateReplayWindow($"Replay paused: {exception.Message}");
        }
    }

    private async Task LoadNextReplayBatchAsync()
    {
        if (_replay is null || _replayLoading)
            return;

        _replayLoading = true;
        bool resume = _replay.IsPlaying;
        _replayTimer.Stop();
        try
        {
            ReplayRuntime runtime = _replay;
            long? finalEndMilliseconds = runtime.EndMillisecondsExclusive;
            if (finalEndMilliseconds.HasValue &&
                runtime.NextStartMilliseconds >= finalEndMilliseconds.Value)
            {
                CompleteReplayRangeImmediately(runtime);
                return;
            }

            // Keep playback ahead of disk I/O by reading only the next small
            // raw-file slice. This never waits behind the canonical archive
            // builder and uses the persistent source-range index.
            long sliceStartUnix = runtime.NextStartMilliseconds / 1000;
            long sliceEndUnix = checked(sliceStartUnix + 3600L);
            if (runtime.EndUnixExclusive.HasValue)
                sliceEndUnix = Math.Min(sliceEndUnix, runtime.EndUnixExclusive.Value);
            sliceEndUnix = Math.Max(sliceStartUnix + 1, sliceEndUnix);

            long sliceEndMilliseconds = checked(sliceEndUnix * 1000L);
            CanonicalTickReadResult read = await Task.Run(
                () => _historyStore.ReadTicksForReplay(
                    runtime.ConnectorId,
                    runtime.Context.Symbol,
                    runtime.NextStartMilliseconds,
                    sliceEndMilliseconds,
                    250_000,
                    _lifetime.Token),
                _lifetime.Token);

            if (_replay is null || !ReferenceEquals(runtime, _replay))
                return;

            if (read.Ticks.Count == 0)
            {
                try
                {
                    read = await Task.Run(
                        () => _historyStore.ReadBridgeTicksForReplayFast(
                            runtime.ConnectorId,
                            runtime.Context.Symbol,
                            runtime.NextStartMilliseconds,
                            sliceEndMilliseconds,
                            250_000,
                            runtime.ServerUtcOffsetMinutes,
                            _lifetime.Token),
                        _lifetime.Token);
                    if (read.Ticks.Count > 0)
                    {
                        ScheduleReplayCanonicalWarmup(
                            runtime.ConnectorId,
                            runtime.Context.Symbol,
                            runtime.ServerUtcOffsetMinutes,
                            sliceStartUnix,
                            sliceEndUnix);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (_replay is null || !ReferenceEquals(runtime, _replay))
                return;

            runtime.Ticks = read.Ticks.ToList();
            runtime.TickIndex = 0;
            runtime.NextStartMilliseconds = read.NextStartMilliseconds;
            bool sliceReachesRangeEnd = runtime.EndUnixExclusive.HasValue &&
                                        sliceEndUnix >= runtime.EndUnixExclusive.Value;
            runtime.HasMore = read.Ticks.Count > 0 &&
                              (read.HasMore || !sliceReachesRangeEnd);

            if (runtime.Ticks.Count == 0)
            {
                runtime.HasMore = false;
                runtime.IsPlaying = false;
                if (runtime.EndUnixExclusive.HasValue)
                {
                    CompleteReplayRangeImmediately(runtime);
                }
                else
                {
                    UpdateReplayWindow("Replay reached the end of available saved tick data.");
                }
                return;
            }

            runtime.IsPlaying = resume;
            runtime.LastPlaybackUtc = DateTime.UtcNow;
            if (resume)
                _replayTimer.Start();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (_replay is not null)
                _replay.IsPlaying = false;
            UpdateReplayWindow($"Replay paused: {exception.Message}");
        }
        finally
        {
            _replayLoading = false;
        }
    }

    private void RefreshReplayIndicators(ChartRuntimeContext source, bool force)
    {
        RefreshAppliedIndicatorsForContext(source, force);
        RefreshBuiltInIndicatorsForContext(source, force);
        foreach (IndicatorWorkspaceRuntimeContext workspace in _indicatorWorkspaceContexts.Values
                     .Where(item => item.ConnectedPricePaneId == source.PaneId)
                     .ToArray())
        {
            RefreshIndicatorWorkspace(workspace, force);
        }
    }

    private void RenderReplayChart(bool forceFit)
    {
        if (_replay is null)
            return;

        ReplayRuntime runtime = _replay;
        List<Candle> visible = runtime.Engine.Candles.TakeLast(12_000).ToList();
        int previousCount = runtime.Context.DisplayCandles.Count;
        long previousLastStart = previousCount > 0
            ? runtime.Context.DisplayCandles[^1].StartUnix
            : long.MinValue;
        int appendedCount = visible.Count > previousCount &&
                            visible.Count > 0 &&
                            visible[^1].StartUnix > previousLastStart
            ? visible.Count - previousCount
            : 0;

        // Replay owns independent source/display snapshots. A tick may update the
        // active candle but must never alias or mutate the original live history.
        runtime.Context.SourceCandles = visible.ToList();
        runtime.Context.DisplayCandles = visible.ToList();
        if (!runtime.ReplayViewportApplied &&
            runtime.ReplayStartViewportAnchor is not null)
        {
            // Initial Replay hide must not pull the remaining history to the
            // right chart wall. Keep the candle immediately before the yellow
            // selector in the same visual slot, leaving the original right-side
            // area empty for replay candles to grow into.
            runtime.Context.Chart.ReplaceDataPreservingAnchor(
                runtime.Context.DisplayCandles,
                runtime.ReplayStartViewportAnchor);
            runtime.ReplayViewportApplied = true;
        }
        else
        {
            runtime.Context.Chart.ReplaceDataKeepingViewport(
                runtime.Context.DisplayCandles,
                appendedCount);
        }
        if (forceFit)
            runtime.Context.Chart.ResetToLaunchView();

        RefreshReplayIndicators(runtime.Context, force: true);
        if (runtime.Context.PaneId == _activePricePaneId)
        {
            _sourceCandles = runtime.Context.SourceCandles;
            _displayCandles = runtime.Context.DisplayCandles;
            if (visible.Count > 0)
                UpdateChartUi("tick-by-tick market replay");
        }
        SyncDetachedChartWindows();
    }

    private void StopReplay(bool restoreChart, bool clearMarker = true)
    {
        _replayTimer.Stop();
        if (clearMarker)
            ClearReplayMarker();
        if (_replay is null)
        {
            UpdateReplayWindow("Replay ended. Replay line removed.");
            return;
        }

        ReplayRuntime runtime = _replay;
        CandleMarker? retainedReplayMarker = !clearMarker && IsReplayMarker(runtime.Context.Chart.InteractiveSelectionMarker)
            ? runtime.Context.Chart.InteractiveSelectionMarker
            : null;
        CandleMarker? retainedReplayEndMarker = !clearMarker && IsReplayEndMarker(runtime.Context.Chart.InteractiveReplayEndMarker)
            ? runtime.Context.Chart.InteractiveReplayEndMarker
            : null;
        runtime.IsPlaying = false;
        if (restoreChart)
        {
            // Reveal the continuously updated hidden live state. The bridge
            // never stopped while Replay was visible, so End Replay does not
            // jump back to the stale snapshot captured at Replay start.
            runtime.Context.SourceCandles = runtime.HiddenLiveSourceCandles.ToList();
            runtime.Context.DisplayCandles = runtime.HiddenLiveDisplayCandles.ToList();
            runtime.Context.AllOlderHistoryLoaded = runtime.OriginalOlderLoaded;
            runtime.Context.AllNewerHistoryLoaded = runtime.OriginalNewerLoaded;
            runtime.Context.Chart.Candles = runtime.Context.DisplayCandles;
            runtime.Context.Chart.RestoreViewport(runtime.OriginalViewport);
            UpdateChartPagingAvailability(runtime.Context);
            runtime.Context.Chart.InteractiveSelectionMarker = retainedReplayMarker;
            runtime.Context.Chart.InteractiveReplayEndMarker = retainedReplayEndMarker;
            runtime.Context.Chart.MarkerSelectionMode = retainedReplayMarker is not null || retainedReplayEndMarker is not null;
            if (runtime.Context.PaneId == _activePricePaneId)
            {
                _sourceCandles = runtime.Context.SourceCandles;
                _displayCandles = runtime.Context.DisplayCandles;
                if (_displayCandles.Count > 0)
                    UpdateChartUi("live chart restored after replay");
            }
        }
        _replay = null;
        RefreshReplayIndicators(runtime.Context, force: true);
        SyncDetachedChartWindows();
        UpdateReplayWindow("Replay ended. Live chart restored.");
    }


    private void HandleInteractiveMarkerRemoveRequested(CandleMarker marker)
    {
        if (!IsReplayMarker(marker))
            return;

        ReplayRuntime? runtime = _replay;
        bool activeReplayLine = runtime is not null &&
                                runtime.ChartId == (_replayMarkerChartId ?? runtime.ChartId);
        if (activeReplayLine)
            StopReplay(restoreChart: true, clearMarker: true);
        else
        {
            ClearReplayMarker();
            UpdateReplayWindow("Replay line removed. Choose another start point when ready.");
        }
    }

    private void ClearReplayMarker(bool syncCheckBox = true)
    {
        if (_replayMarkerChartId.HasValue &&
            _chartContexts.TryGetValue(_replayMarkerChartId.Value, out ChartRuntimeContext? markerContext))
        {
            markerContext.Chart.MarkerSelectionMode = false;
            if (IsReplayMarker(markerContext.Chart.InteractiveSelectionMarker))
                markerContext.Chart.InteractiveSelectionMarker = null;
            if (IsReplayEndMarker(markerContext.Chart.InteractiveReplayEndMarker))
                markerContext.Chart.InteractiveReplayEndMarker = null;
        }
        else
        {
            foreach (ChartRuntimeContext context in _chartContexts.Values)
            {
                bool changed = false;
                if (IsReplayMarker(context.Chart.InteractiveSelectionMarker))
                {
                    context.Chart.InteractiveSelectionMarker = null;
                    changed = true;
                }
                if (IsReplayEndMarker(context.Chart.InteractiveReplayEndMarker))
                {
                    context.Chart.InteractiveReplayEndMarker = null;
                    changed = true;
                }
                if (changed)
                    context.Chart.MarkerSelectionMode = false;
            }
        }
        _replayMarkerChartId = null;
        if (syncCheckBox)
            _replayWindow?.SetReplayLineChecked(false);
    }

    private void UpdateReplayWindow(string? overrideStatus = null)
    {
        if (_replayWindow is null)
            return;
        if (_replay is null)
        {
            _replayWindow.SetReplayRangeChecked(_replayRangeMode);
            _replayWindow.SetReplayLineChecked(HasReplayMarker());
            string defaultStatus = HasReplayMarker()
                ? (_replayRangeMode
                    ? "Replay range ready. Yellow = start, red = end. Drag either line, then press Play."
                    : "Replay start ready. Drag the yellow line if needed, then press Play.")
                : (_replayRangeMode
                    ? "Tick Replay line to show yellow start and red end selectors."
                    : "Tick Replay line to show a yellow replay-start selector.");
            _replayWindow.SetState(false, false, overrideStatus ?? defaultStatus, "Moving replay selectors never loads or starts replay. Live bridge recording continues in the background.");
            return;
        }

        MarketTick? tick = _replay.Engine.LastTick;
        string time = tick.HasValue
            ? tick.Value.Time.ToString("yyyy-MM-dd HH:mm:ss.fff")
            : Mt5ServerClock.ToDisplayTime(_replay.StartUnix).ToString("yyyy-MM-dd HH:mm:ss");
        string direction = _replayDirection == ReplayPlaybackDirection.Reverse
            ? "reverse"
            : "forward";
        string status = overrideStatus ?? (_replay.IsPlaying
            ? $"Replay running {direction}."
            : $"Replay paused · {direction} selected.");
        string range = _replay.EndUnixExclusive.HasValue
            ? $" · range to {Mt5ServerClock.ToDisplayTime(_replay.EndUnixExclusive.Value - 1):yyyy-MM-dd HH:mm:ss}"
            : string.Empty;
        string progress = $"{_replay.Context.Symbol} · {_replay.Context.Timeframe.DisplayText} · tick {_replay.ProcessedTicks:N0} · {time} · {direction} · speed {_replaySpeed:G}×{range}";
        _replayWindow.SetPlaybackDirection(_replayDirection == ReplayPlaybackDirection.Reverse);
        _replayWindow.SetState(true, _replay.IsPlaying, status, progress);
    }

    private void HandleReplayMarkerMoved(CandleMarker marker)
    {
        if (!IsReplayMarker(marker))
            return;
        if (_replayMarkerChartId.HasValue)
            _replaySetupChartId = _replayMarkerChartId.Value;
        if (!IsReplayEndMarker(marker))
            _replayWindow?.SetMarkerTime(Mt5ServerClock.ToDisplayTime(marker.StartUnix).DateTime);
    }

    private void HandleReplayMarkerPlacementCompleted(CandleMarker marker)
    {
        if (!IsReplayMarker(marker) || marker.StartUnix == long.MinValue)
            return;

        HandleReplayMarkerMoved(marker);
        _replayWindow?.SetReplayLineChecked(true);
        _replayWindow?.SetReplayRangeChecked(_replayRangeMode);

        if (_replay is not null)
            StopReplay(restoreChart: true, clearMarker: false);

        if (_replayRangeMode && TryGetReplayTargetContext(out ChartRuntimeContext context))
        {
            CandleMarker? startMarker = context.Chart.InteractiveSelectionMarker;
            CandleMarker? endMarker = context.Chart.InteractiveReplayEndMarker;
            if (IsReplayMarker(startMarker) && IsReplayEndMarker(endMarker) &&
                endMarker.StartUnix <= startMarker.StartUnix)
            {
                UpdateReplayWindow("Red end line must be to the right of the yellow start line before you press Play.");
                return;
            }
        }

        UpdateReplayWindow(_replayRangeMode
            ? "Replay range updated. Yellow = start, red = end. Press Play when ready."
            : "Replay start updated. Press Play when ready.");
    }

    private bool IsReplayChart(int paneId) => _replay?.ChartId == paneId;

    private void CloseAlertsAndReplayForShutdown()
    {
        _replayTimer.Stop();
        _replayWindow?.CloseForShutdown();
        _replayWindow = null;
        _alertManagerWindow?.CloseForShutdown();
        _alertManagerWindow = null;
    }
}
