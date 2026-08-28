using TickLab.Core.Indicators;
using TickLab.Core.Scripting;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<IndicatorRouteTarget> BuildIndicatorRouteTargets(ChartRuntimeContext source)
    {
        return _chartContexts.Values
            .Where(context => !ReferenceEquals(context, source))
            .OrderBy(context =>
            {
                (int? workspace, int? partition) = FindPaneLocation(context.PaneId);
                return workspace ?? int.MaxValue;
            })
            .ThenBy(context => FindPaneLocation(context.PaneId).PartitionId ?? int.MaxValue)
            .ThenBy(context => context.PaneId)
            .Select(context =>
            {
                (int? workspace, int? partition) = FindPaneLocation(context.PaneId);
                return new IndicatorRouteTarget(
                    context.PaneId,
                    workspace,
                    partition,
                    string.IsNullOrWhiteSpace(context.Symbol) ? "No symbol" : context.Symbol,
                    context.Timeframe.DisplayText);
            })
            .ToArray();
    }

    private ChartRuntimeContext? SelectIndicatorRouteTarget(
        ChartRuntimeContext source,
        string indicatorName,
        IndicatorRouteAction action)
    {
        IReadOnlyList<IndicatorRouteTarget> targets = BuildIndicatorRouteTargets(source);
        if (targets.Count == 0)
        {
            StatusText.Text = "Open another price chart before connecting, copying or moving an indicator.";
            return null;
        }

        var window = new IndicatorRouteWindow(indicatorName, action, targets) { Owner = this };
        if (window.ShowDialog() != true)
            return null;
        IndicatorRouteTarget? selectedTarget = window.SelectedTarget;
        return selectedTarget is not null && _chartContexts.TryGetValue(selectedTarget.PaneId, out ChartRuntimeContext? target)
            ? target
            : null;
    }

    private void RouteTickScriptIndicator(
        ChartRuntimeContext source,
        TickScriptEntry entry,
        IndicatorRouteAction action)
    {
        ChartRuntimeContext? target = SelectIndicatorRouteTarget(source, entry.Name, action);
        if (target is null)
            return;

        TickScriptIndicatorAppearance sourceAppearance = GetTickScriptAppearance(source, entry.SourcePath);
        bool targetAlreadyContains = target.AppliedIndicators.Any(item =>
            string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (targetAlreadyContains && action is IndicatorRouteAction.Copy or IndicatorRouteAction.Move)
        {
            StatusText.Text = $"{entry.Name} is already applied to chart {target.PaneId}. Use Connect to link its settings.";
            return;
        }

        switch (action)
        {
            case IndicatorRouteAction.Connect:
            {
                string group = string.IsNullOrWhiteSpace(sourceAppearance.LinkedGroupId)
                    ? Guid.NewGuid().ToString("N")
                    : sourceAppearance.LinkedGroupId;
                TickScriptIndicatorAppearance linked = sourceAppearance with { LinkedGroupId = group };
                source.IndicatorAppearances[entry.SourcePath] = linked;
                if (source.IndicatorResults.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? sourceResult) && sourceResult is not null)
                    source.IndicatorStack.UpdateResult(entry, sourceResult, linked);
                ApplyIndicatorToContext(target, entry, linked);
                StatusText.Text = $"Connected {entry.Name} to chart {target.PaneId}. Colour and display changes now stay linked.";
                break;
            }
            case IndicatorRouteAction.Copy:
                ApplyIndicatorToContext(target, entry, sourceAppearance with { LinkedGroupId = string.Empty });
                StatusText.Text = $"Copied {entry.Name} to chart {target.PaneId} as an independent indicator.";
                break;
            case IndicatorRouteAction.Move:
                ApplyIndicatorToContext(target, entry, sourceAppearance with { });
                RemoveAppliedIndicator(source, entry);
                StatusText.Text = $"Moved {entry.Name} to chart {target.PaneId}.";
                break;
        }
        SaveWorkspace();
    }

    private void RouteBuiltInIndicator(
        ChartRuntimeContext source,
        BuiltInIndicatorInstance instance,
        IndicatorRouteAction action)
    {
        BuiltInIndicatorInstance? current = source.BuiltInIndicators.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (current is null)
            return;

        ChartRuntimeContext? target = SelectIndicatorRouteTarget(source, current.DisplayName, action);
        if (target is null)
            return;

        switch (action)
        {
            case IndicatorRouteAction.Connect:
            {
                string group = string.IsNullOrWhiteSpace(current.LinkedGroupId)
                    ? Guid.NewGuid().ToString("N")
                    : current.LinkedGroupId;
                BuiltInIndicatorInstance linkedSource = current with { LinkedGroupId = group };
                AddOrReplaceBuiltInIndicator(source, linkedSource, replaceExisting: true);
                BuiltInIndicatorInstance linkedTarget = CloneBuiltInIndicator(current) with
                {
                    InstanceId = Guid.NewGuid().ToString("N"),
                    LinkedGroupId = group
                };
                AddOrReplaceBuiltInIndicator(target, linkedTarget, replaceExisting: false);
                StatusText.Text = $"Connected {current.DisplayName} to chart {target.PaneId}. Settings now stay linked.";
                break;
            }
            case IndicatorRouteAction.Copy:
            {
                BuiltInIndicatorInstance copy = CloneBuiltInIndicator(current) with
                {
                    InstanceId = Guid.NewGuid().ToString("N"),
                    LinkedGroupId = string.Empty
                };
                AddOrReplaceBuiltInIndicator(target, copy, replaceExisting: false);
                StatusText.Text = $"Copied {current.DisplayName} to chart {target.PaneId} as an independent indicator.";
                break;
            }
            case IndicatorRouteAction.Move:
            {
                BuiltInIndicatorInstance moved = CloneBuiltInIndicator(current);
                AddOrReplaceBuiltInIndicator(target, moved, replaceExisting: false);
                RemoveBuiltInIndicator(source, current);
                StatusText.Text = $"Moved {current.DisplayName} to chart {target.PaneId}.";
                break;
            }
        }
        SaveWorkspace();
    }
}
