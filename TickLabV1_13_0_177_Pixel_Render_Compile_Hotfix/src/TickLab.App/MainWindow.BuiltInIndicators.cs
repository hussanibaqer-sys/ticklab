using TickLab.Core.Indicators;
using TickLab.Core.Market;
using TickLab.Core.Scripting;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private void ApplyBuiltInIndicatorToActiveChart(BuiltInIndicatorKind kind)
    {
        BuiltInIndicatorInstance instance = BuiltInIndicatorCatalog.CreateDefault(kind);
        var settings = new BuiltInIndicatorSettingsWindow(instance) { Owner = this };
        if (settings.ShowDialog() != true)
            return;
        AddOrReplaceBuiltInIndicator(ActiveChartContext, settings.Result, replaceExisting: false);
        _indicatorsWindow?.Hide();
        StatusText.Text = $"Applied {settings.Result.DisplayName}.";
        SaveWorkspace();
    }

    private void EditBuiltInIndicator(BuiltInIndicatorInstance instance) =>
        EditBuiltInIndicator(ActiveChartContext, instance);

    private void EditBuiltInIndicator(ChartRuntimeContext context, BuiltInIndicatorInstance instance)
    {
        BuiltInIndicatorInstance? current = context.BuiltInIndicators.FirstOrDefault(item => item.InstanceId == instance.InstanceId);
        if (current is null)
            return;
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions(context.PaneId, context.PaneId, true);
        var settings = new BuiltInIndicatorSettingsWindow(current, options) { Owner = this };
        if (settings.ShowDialog() != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;

        if (placement.PlaceAddress.PriceChartPaneId != context.PaneId)
        {
            BuiltInIndicatorInstance moved = CloneBuiltInIndicator(settings.Result) with
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                LinkedGroupId = string.Empty
            };
            if (placement.PlaceAddress.PriceChartPaneId is int targetPaneId &&
                _chartContexts.TryGetValue(targetPaneId, out ChartRuntimeContext? targetChart))
            {
                if (!TryCopyBuiltInIndicatorToChart(targetChart, moved))
                    return;
                RemoveBuiltInIndicator(context, current);
                StatusText.Text = $"Moved {moved.DisplayName} to Chart {targetChart.PaneId}.";
                SaveWorkspace();
                return;
            }

            if (!TryCreateBuiltInIndicatorWorkspace(moved, placement, out _))
                return;
            RemoveBuiltInIndicator(context, current);
            StatusText.Text = $"Moved {moved.DisplayName} to Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}.";
            SaveWorkspace();
            return;
        }

        string linkedGroup = current.LinkedGroupId;
        if (string.IsNullOrWhiteSpace(linkedGroup))
        {
            AddOrReplaceBuiltInIndicator(
                context,
                settings.Result with { InstanceId = current.InstanceId, LinkedGroupId = string.Empty },
                replaceExisting: true);
        }
        else
        {
            foreach (ChartRuntimeContext targetContext in _chartContexts.Values.ToArray())
            {
                BuiltInIndicatorInstance[] linked = targetContext.BuiltInIndicators
                    .Where(item => string.Equals(item.LinkedGroupId, linkedGroup, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (BuiltInIndicatorInstance target in linked)
                {
                    AddOrReplaceBuiltInIndicator(
                        targetContext,
                        settings.Result with { InstanceId = target.InstanceId, LinkedGroupId = linkedGroup },
                        replaceExisting: true);
                }
            }
        }
        StatusText.Text = $"Updated {settings.Result.DisplayName}.";
        SaveWorkspace();
    }

    private void AddOrReplaceBuiltInIndicator(ChartRuntimeContext context, BuiltInIndicatorInstance instance, bool replaceExisting)
    {
        int index = context.BuiltInIndicators.FindIndex(item => item.InstanceId == instance.InstanceId);
        if (index >= 0)
            context.BuiltInIndicators[index] = CloneBuiltInIndicator(instance);
        else
            context.BuiltInIndicators.Add(CloneBuiltInIndicator(instance));
        RefreshBuiltInIndicatorsForContext(context, force: true);
        if (ReferenceEquals(context, ActiveChartContext))
        {
            ShowIndicatorsForActiveChart();
            RefreshIndicatorsWindowAppliedList();
        }
    }

    private void RemoveBuiltInIndicator(BuiltInIndicatorInstance instance) =>
        RemoveBuiltInIndicator(ActiveChartContext, instance);

    private void RemoveBuiltInIndicator(ChartRuntimeContext context, BuiltInIndicatorInstance instance)
    {
        context.BuiltInIndicators.RemoveAll(item => item.InstanceId == instance.InstanceId);
        context.BuiltInIndicatorResults.Remove(instance.InstanceId);
        context.IndicatorStack.Remove(instance);
        ApplyBuiltInOverlayResults(context);
        if (ReferenceEquals(context, ActiveChartContext))
        {
            ShowIndicatorsForActiveChart();
            RefreshIndicatorsWindowAppliedList();
            StatusText.Text = $"Removed {instance.DisplayName}.";
        }
        SaveWorkspace();
    }

    private void RestoreAppliedBuiltInIndicators()
    {
        ChartRuntimeContext context = GetChartContext(1);
        if (context.BuiltInIndicators.Count > 0)
            return;
        foreach (BuiltInIndicatorInstance instance in _preferences.AppliedBuiltInIndicators ?? Array.Empty<BuiltInIndicatorInstance>())
            context.BuiltInIndicators.Add(CloneBuiltInIndicator(instance));
        if (context.BuiltInIndicators.Count > 0)
            RefreshBuiltInIndicatorsForContext(context, force: true);
    }

    private void RefreshAllBuiltInIndicators(bool force = false)
    {
        foreach (ChartRuntimeContext context in _chartContexts.Values.ToArray())
            RefreshBuiltInIndicatorsForContext(context, force);
    }

    private void RefreshBuiltInIndicatorsForContext(ChartRuntimeContext context, bool force)
    {
        if (context.BuiltInIndicators.Count == 0)
        {
            context.BuiltInIndicatorResults.Clear();
            context.Chart.BuiltInIndicatorOverlays = Array.Empty<BuiltInIndicatorResult>();
            return;
        }
        if (_isClosing || context.Chart.Candles.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        if (!force && now - context.LastBuiltInIndicatorRefreshUtc < TimeSpan.FromMilliseconds(350))
        {
            context.BuiltInIndicatorRefreshPending = true;
            return;
        }
        if (context.BuiltInIndicatorRefreshRunning)
        {
            context.BuiltInIndicatorRefreshPending = true;
            return;
        }
        _ = RefreshBuiltInIndicatorsForContextAsync(context);
    }

    private async Task RefreshBuiltInIndicatorsForContextAsync(ChartRuntimeContext context)
    {
        if (context.BuiltInIndicatorRefreshRunning || context.BuiltInIndicators.Count == 0 || context.Chart.Candles.Count == 0)
            return;
        context.BuiltInIndicatorRefreshRunning = true;
        context.BuiltInIndicatorRefreshPending = false;
        context.LastBuiltInIndicatorRefreshUtc = DateTime.UtcNow;
        int paneId = context.PaneId;
        string symbol = context.Symbol;
        string timeframeKey = context.Timeframe.Key;
        if (!TryCreateSafeIndicatorSnapshot(context, out Candle[] candles, out int candleRevision, out long lastCandleStartUnix))
        {
            context.BuiltInIndicatorRefreshRunning = false;
            return;
        }
        BuiltInIndicatorInstance[] instances = context.BuiltInIndicators.Select(CloneBuiltInIndicator).ToArray();
        string timeframe = context.Timeframe.DisplayText;

        try
        {
            Dictionary<string, BuiltInIndicatorResult> results = await Task.Run(() =>
            {
                var calculated = new Dictionary<string, BuiltInIndicatorResult>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyList<double?>? first = null;
                IReadOnlyList<double?>? previous = null;
                foreach (BuiltInIndicatorInstance instance in instances)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (!instance.VisibleOnAllTimeframes &&
                        !instance.VisibleTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    BuiltInIndicatorResult result = BuiltInIndicatorEngine.Evaluate(instance, candles, first, previous);
                    calculated[instance.InstanceId] = result;
                    IndicatorSeriesResult? primary = result.Series.FirstOrDefault(series => series.Style.Visible);
                    if (primary is not null)
                    {
                        previous = primary.Values;
                        first ??= primary.Values;
                    }
                }
                return calculated;
            }, _lifetime.Token);

            if (_isClosing || !_chartContexts.TryGetValue(paneId, out ChartRuntimeContext? liveContext) ||
                !string.Equals(liveContext.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(liveContext.Timeframe.Key, timeframeKey, StringComparison.OrdinalIgnoreCase) ||
                liveContext.CandleRevision != candleRevision ||
                liveContext.Chart.Candles.Count == 0 ||
                liveContext.Chart.Candles[^1].StartUnix != lastCandleStartUnix)
            {
                return;
            }

            liveContext.BuiltInIndicatorResults.Clear();
            foreach ((string key, BuiltInIndicatorResult result) in results)
                liveContext.BuiltInIndicatorResults[key] = result;

            foreach (BuiltInIndicatorInstance instance in liveContext.BuiltInIndicators)
            {
                if (!results.TryGetValue(instance.InstanceId, out BuiltInIndicatorResult? result))
                    continue;
                if (result.Placement == BuiltInIndicatorPlacement.SeparateWindow)
                    liveContext.IndicatorStack.AddOrReplace(instance, result);
                else
                    liveContext.IndicatorStack.Remove(instance);
            }
            liveContext.IndicatorStack.SetViewport(liveContext.Chart.CaptureViewportSnapshot());
            ApplyBuiltInOverlayResults(liveContext);
            if (ReferenceEquals(liveContext, ActiveChartContext))
                ShowIndicatorsForActiveChart();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Indicator calculation failed: {exception.Message}";
        }
        finally
        {
            context.BuiltInIndicatorRefreshRunning = false;
            if (context.BuiltInIndicatorRefreshPending && !_isClosing)
            {
                context.BuiltInIndicatorRefreshPending = false;
                Dispatcher.BeginInvoke(new Action(() => RefreshBuiltInIndicatorsForContext(context, force: false)));
            }
        }
    }

    private static BuiltInIndicatorInstance CloneBuiltInIndicator(BuiltInIndicatorInstance instance) => instance with
    {
        NumericParameters = new Dictionary<string, double>(instance.NumericParameters, StringComparer.OrdinalIgnoreCase),
        TextParameters = new Dictionary<string, string>(instance.TextParameters, StringComparer.OrdinalIgnoreCase),
        BooleanParameters = new Dictionary<string, bool>(instance.BooleanParameters, StringComparer.OrdinalIgnoreCase),
        Styles = instance.Styles.Select(item => item with { }).ToArray(),
        Levels = instance.Levels.Select(item => item with { }).ToArray(),
        VisibleTimeframes = instance.VisibleTimeframes.ToArray()
    };

    private static IReadOnlyList<BuiltInIndicatorInstance> CloneBuiltInIndicators(IEnumerable<BuiltInIndicatorInstance> instances) =>
        instances.Select(CloneBuiltInIndicator).ToArray();

    private static void ApplyBuiltInOverlayResults(ChartRuntimeContext context)
    {
        context.Chart.BuiltInIndicatorOverlays = context.BuiltInIndicatorResults.Values
            .Where(result => result.Placement == BuiltInIndicatorPlacement.Overlay)
            .ToArray();
    }
}
