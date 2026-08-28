using TickLab.Core.Indicators;
using TickLab.Core.Scripting;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<ChartIndicatorMenuEntry> BuildChartIndicatorMenuEntries(ChartRuntimeContext context)
    {
        var items = new List<ChartIndicatorMenuEntry>();
        items.AddRange(context.BuiltInIndicators.Select(instance =>
        {
            BuiltInIndicatorDefinition definition = BuiltInIndicatorCatalog.Find(instance.Kind);
            string surface = definition.Placement == BuiltInIndicatorPlacement.Overlay ? "On price chart" : "Separate indicator pane";
            string placement = $"{FormatChartIndicatorAddress(context)} → {surface}";
            return new ChartIndicatorMenuEntry("builtin:" + instance.InstanceId, instance.DisplayName, placement);
        }));
        items.AddRange(context.AppliedIndicators.Select(entry =>
        {
            bool overlay = context.IndicatorResults.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? result) &&
                           result is not null &&
                           result.Overlay;
            string surface = overlay ? "On price chart" : "Separate indicator pane";
            return new ChartIndicatorMenuEntry("script:" + entry.SourcePath, entry.Name, $"{FormatChartIndicatorAddress(context)} → {surface}");
        }));
        return items.OrderBy(item => item.Placement).ThenBy(item => item.DisplayName).ToArray();
    }

    private IReadOnlyList<AppliedIndicatorListItem> BuildAppliedIndicatorList(ChartRuntimeContext context)
    {
        var items = new List<AppliedIndicatorListItem>();
        items.AddRange(context.BuiltInIndicators.Select(instance =>
        {
            BuiltInIndicatorDefinition definition = BuiltInIndicatorCatalog.Find(instance.Kind);
            string surface = definition.Placement == BuiltInIndicatorPlacement.Overlay ? "On price chart" : "Separate indicator pane";
            return new AppliedIndicatorListItem("builtin:" + instance.InstanceId, instance.DisplayName, "MT5 built-in", $"{FormatChartIndicatorAddress(context)} → {surface}");
        }));
        items.AddRange(context.AppliedIndicators.Select(entry =>
        {
            bool overlay = context.IndicatorResults.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? result) &&
                           result is not null &&
                           result.Overlay;
            string surface = overlay ? "On price chart" : "Separate indicator pane";
            return new AppliedIndicatorListItem("script:" + entry.SourcePath, entry.Name, "TickScript", $"{FormatChartIndicatorAddress(context)} → {surface}");
        }));
        return items.OrderBy(item => item.Placement).ThenBy(item => item.Name).ToArray();
    }

    private void RefreshIndicatorsWindowAppliedList()
    {
        if (_indicatorsWindow is null)
            return;
        _indicatorsWindow.SetAppliedIndicators(BuildAppliedIndicatorList(ActiveChartContext));
    }

    private void OpenIndicatorManager(CandleChartControl chart, bool showApplied)
    {
        ActivateChartControl(chart);
        IndicatorsButton_Click(this, new System.Windows.RoutedEventArgs());
        RefreshIndicatorsWindowAppliedList();
        if (showApplied)
            _indicatorsWindow?.ShowAppliedTab();
        else
            _indicatorsWindow?.ShowLibraryTab();
    }

    private void EditIndicatorByKey(ChartRuntimeContext context, string key)
    {
        ActivateWorkspacePane(context.PaneId);
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            string id = key["builtin:".Length..];
            BuiltInIndicatorInstance? instance = context.BuiltInIndicators.FirstOrDefault(item => string.Equals(item.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (instance is not null)
                EditBuiltInIndicator(context, instance);
            return;
        }

        if (key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
        {
            string path = key["script:".Length..];
            TickScriptEntry? entry = context.AppliedIndicators.FirstOrDefault(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                EditTickScriptIndicatorProperties(context, entry);
        }
    }


    private void RefreshIndicatorByKey(ChartRuntimeContext context, string key)
    {
        ActivateWorkspacePane(context.PaneId);
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            RefreshBuiltInIndicatorsForContext(context, force: true);
            StatusText.Text = "Selected built-in indicator refreshed.";
            return;
        }
        if (key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
        {
            string path = key["script:".Length..];
            TickScriptEntry? entry = context.AppliedIndicators.FirstOrDefault(item =>
                string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                RefreshAppliedIndicator(context, entry, force: true);
                StatusText.Text = $"{entry.Name} refreshed.";
            }
        }
    }

    private void MoveIndicatorToWindowByKey(ChartRuntimeContext context, string key)
    {
        ActivateWorkspacePane(context.PaneId);
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            string id = key["builtin:".Length..];
            BuiltInIndicatorInstance? instance = context.BuiltInIndicators.FirstOrDefault(item =>
                string.Equals(item.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (instance is not null)
                MoveBuiltInIndicatorToWindow(context, instance);
            return;
        }

        if (key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
        {
            string path = key["script:".Length..];
            TickScriptEntry? entry = context.AppliedIndicators.FirstOrDefault(item =>
                string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                MoveTickScriptIndicatorToWindow(context, entry);
        }
    }

    private void MoveIndicatorToChartByKey(ChartRuntimeContext context, string key)
    {
        RouteIndicatorByKey(context, key, IndicatorRouteAction.Move);
    }

    private void RemoveIndicatorByKey(ChartRuntimeContext context, string key)
    {
        ActivateWorkspacePane(context.PaneId);
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            string id = key["builtin:".Length..];
            BuiltInIndicatorInstance? instance = context.BuiltInIndicators.FirstOrDefault(item => string.Equals(item.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (instance is not null)
                RemoveBuiltInIndicator(context, instance);
            return;
        }

        if (key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
        {
            string path = key["script:".Length..];
            TickScriptEntry? entry = context.AppliedIndicators.FirstOrDefault(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                RemoveAppliedIndicator(context, entry);
        }
    }
    private void RouteIndicatorByKey(
        ChartRuntimeContext context,
        string key,
        IndicatorRouteAction action)
    {
        ActivateWorkspacePane(context.PaneId);
        if (key.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            string id = key["builtin:".Length..];
            BuiltInIndicatorInstance? instance = context.BuiltInIndicators.FirstOrDefault(item =>
                string.Equals(item.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (instance is not null)
                RouteBuiltInIndicator(context, instance, action);
            return;
        }

        if (key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
        {
            string path = key["script:".Length..];
            TickScriptEntry? entry = context.AppliedIndicators.FirstOrDefault(item =>
                string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                RouteTickScriptIndicator(context, entry, action);
        }
    }

    private string FormatChartIndicatorAddress(ChartRuntimeContext context)
    {
        (int? workspace, int? partition) = FindPaneLocation(context.PaneId);
        string location = workspace.HasValue && partition.HasValue
            ? $"Workspace {workspace} → Partition {partition}"
            : $"Floating chart {context.PaneId}";
        string symbol = string.IsNullOrWhiteSpace(context.Symbol) ? "No symbol" : context.Symbol;
        return $"{location} → {symbol} · {context.Timeframe.DisplayText}";
    }

    private string FormatIndicatorWorkspaceAddress(int paneId)
    {
        (int? workspace, int? partition) = FindPaneLocation(paneId);
        return workspace.HasValue && partition.HasValue
            ? $"Workspace {workspace} → Partition {partition}"
            : $"Floating indicator workspace {paneId}";
    }

}
