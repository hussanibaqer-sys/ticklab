using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using TickLab.Core.Indicators;
using TickLab.Core.Market;
using TickLab.Core.Scripting;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Settings;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private sealed class IndicatorWorkspaceRuntimeContext
    {
        public required int PaneId { get; init; }
        public required IndicatorPaneStackControl Stack { get; init; }
        public int? ConnectedPricePaneId { get; set; }
        public bool SyncWithPriceChart { get; set; }
        public List<TickScriptEntry> AppliedIndicators { get; } = new();
        public Dictionary<string, TickScriptIndicatorResult> IndicatorResults { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TickScriptIndicatorAppearance> IndicatorAppearances { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<BuiltInIndicatorInstance> BuiltInIndicators { get; } = new();
        public Dictionary<string, BuiltInIndicatorResult> BuiltInIndicatorResults { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool RefreshRunning { get; set; }
        public bool RefreshPending { get; set; }
        public DateTime LastRefreshUtc { get; set; } = DateTime.MinValue;
        public int SourceGeneration { get; set; }
    }

    private readonly Dictionary<int, IndicatorWorkspaceRuntimeContext> _indicatorWorkspaceContexts = new();
    private IndicatorPlaceAddress? _lastIndicatorPlaceAddress;

    private void RememberIndicatorPlacementTarget(int workspaceId, int partitionId, WorkspacePaneHandle? pane)
    {
        if (workspaceId <= 0 || partitionId <= 0)
            return;

        if (pane is null)
        {
            _lastIndicatorPlaceAddress = new IndicatorPlaceAddress(
                workspaceId,
                partitionId,
                null,
                $"Workspace {workspaceId} → Partition {partitionId} → Empty workspace");
            return;
        }

        if (pane.Kind != WorkspacePaneKind.PriceChart || !_chartContexts.TryGetValue(pane.Id, out ChartRuntimeContext? chart))
            return;

        string symbol = string.IsNullOrWhiteSpace(chart.Symbol) ? "No symbol" : chart.Symbol;
        _lastIndicatorPlaceAddress = new IndicatorPlaceAddress(
            workspaceId,
            partitionId,
            chart.PaneId,
            $"Workspace {workspaceId} → Partition {partitionId} → {symbol} · {chart.Timeframe.DisplayText}");
    }

    private IndicatorPlacementOptions? BuildIndicatorPlacementOptions(
        int? currentPlacePaneId = null,
        int? currentConnectedPricePaneId = null,
        bool? initialSyncWithPriceChart = null)
    {
        var places = new List<IndicatorPlaceAddress>();
        foreach (WorkspacePageRuntime page in _workspacePages.Values.OrderBy(item => item.Id))
        {
            for (int partition = 1; partition <= page.Surface.LayoutCount; partition++)
            {
                WorkspacePaneHandle? pane = page.Surface.GetPane(partition);
                if (pane is null)
                {
                    places.Add(new IndicatorPlaceAddress(
                        page.Id,
                        partition,
                        null,
                        $"Workspace {page.Id} → Partition {partition} → Empty workspace"));
                    continue;
                }

                if (pane.Kind == WorkspacePaneKind.Indicator && pane.Id == currentPlacePaneId)
                {
                    places.Add(new IndicatorPlaceAddress(
                        page.Id,
                        partition,
                        null,
                        $"Workspace {page.Id} → Partition {partition} → Current indicator workspace")
                    {
                        IndicatorWorkspacePaneId = pane.Id
                    });
                    continue;
                }

                if (pane.Kind != WorkspacePaneKind.PriceChart || !_chartContexts.TryGetValue(pane.Id, out ChartRuntimeContext? chart))
                    continue;

                string symbol = string.IsNullOrWhiteSpace(chart.Symbol) ? "No symbol" : chart.Symbol;
                places.Add(new IndicatorPlaceAddress(
                    page.Id,
                    partition,
                    chart.PaneId,
                    $"Workspace {page.Id} → Partition {partition} → {symbol} · {chart.Timeframe.DisplayText}"));
            }
        }

        if (places.Count == 0)
        {
            StatusText.Text = "Create a workspace or price chart before applying an indicator.";
            return null;
        }

        var connections = new List<IndicatorConnectionAddress>
        {
            new(null, "Not connected")
        };
        connections.AddRange(_chartContexts.Values
            .OrderBy(chart => FindPaneLocation(chart.PaneId).WorkspaceId ?? int.MaxValue)
            .ThenBy(chart => FindPaneLocation(chart.PaneId).PartitionId ?? int.MaxValue)
            .ThenBy(chart => chart.PaneId)
            .Select(chart =>
            {
                (int? workspace, int? partition) = FindPaneLocation(chart.PaneId);
                string location = workspace.HasValue && partition.HasValue
                    ? $"Workspace {workspace} → Partition {partition}"
                    : "Floating chart";
                string symbol = string.IsNullOrWhiteSpace(chart.Symbol) ? "No symbol" : chart.Symbol;
                return new IndicatorConnectionAddress(
                    chart.PaneId,
                    $"{location} → {symbol} · {chart.Timeframe.DisplayText}");
            }));

        IndicatorPlaceAddress initialPlace = places.FirstOrDefault(item =>
                currentPlacePaneId.HasValue &&
                (item.PriceChartPaneId == currentPlacePaneId || item.IndicatorWorkspacePaneId == currentPlacePaneId))
            ?? places.FirstOrDefault(item =>
                _lastIndicatorPlaceAddress is not null &&
                item.WorkspaceId == _lastIndicatorPlaceAddress.WorkspaceId &&
                item.PartitionId == _lastIndicatorPlaceAddress.PartitionId &&
                item.PriceChartPaneId == _lastIndicatorPlaceAddress.PriceChartPaneId)
            ?? places.FirstOrDefault(item => item.PriceChartPaneId == _activePricePaneId)
            ?? places[0];

        int? initialSourcePaneId = currentConnectedPricePaneId ?? initialPlace.PriceChartPaneId;
        IndicatorConnectionAddress initialConnection = initialSourcePaneId is int chartPaneId
            ? connections.FirstOrDefault(item => item.PriceChartPaneId == chartPaneId) ?? connections[0]
            : connections[0];

        return new IndicatorPlacementOptions(
            places,
            connections,
            initialPlace,
            initialConnection,
            initialSyncWithPriceChart ?? initialPlace.IsPriceChart);
    }

    private void ApplyBuiltInIndicatorFromSelection(BuiltInIndicatorKind kind)
    {
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions();
        if (options is null)
            return;

        BuiltInIndicatorInstance initial = BuiltInIndicatorCatalog.CreateDefault(kind);
        var settings = new BuiltInIndicatorSettingsWindow(initial, options) { Owner = this };
        if (settings.ShowDialog() != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;

        if (placement.PlaceAddress.PriceChartPaneId is int chartPaneId &&
            _chartContexts.TryGetValue(chartPaneId, out ChartRuntimeContext? chart))
        {
            AddOrReplaceBuiltInIndicator(chart, settings.Result, replaceExisting: false);
            _indicatorsWindow?.Hide();
            StatusText.Text = $"Applied {settings.Result.DisplayName} to Chart {chart.PaneId}.";
            SaveWorkspace();
            return;
        }

        PlaceConfiguredBuiltInIndicatorInWorkspace(settings.Result, placement);
    }

    private void ApplyTickScriptIndicatorFromSelection(TickScriptEntry entry)
    {
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions();
        if (options is null)
            return;

        TickScriptIndicatorAppearance current = TickScriptIndicatorAppearance.Default with { };
        var settings = new TickScriptIndicatorSettingsWindow(entry, current, options) { Owner = this };
        bool? accepted = settings.ShowDialog();
        if (settings.OpenCodeEditorRequested)
        {
            OpenIndicatorInEditor(entry);
            return;
        }
        if (accepted != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;

        if (placement.PlaceAddress.PriceChartPaneId is int chartPaneId &&
            _chartContexts.TryGetValue(chartPaneId, out ChartRuntimeContext? chart))
        {
            ApplyIndicatorToContext(chart, entry, settings.Result);
            _indicatorsWindow?.Hide();
            StatusText.Text = $"Applied {entry.Name} to Chart {chart.PaneId}.";
            return;
        }

        PlaceConfiguredTickScriptIndicatorInWorkspace(entry, settings.Result, placement);
    }

    private void PlaceConfiguredTickScriptIndicatorInWorkspace(
        TickScriptEntry entry,
        TickScriptIndicatorAppearance appearance,
        IndicatorPlacementResult placement)
    {
        if (!TryCreateTickScriptIndicatorWorkspace(entry, appearance, placement, out IndicatorWorkspaceRuntimeContext context))
            return;

        _indicatorsWindow?.Hide();
        StatusText.Text = context.ConnectedPricePaneId.HasValue
            ? $"Placed {entry.Name} and connected it to Chart {context.ConnectedPricePaneId}."
            : $"Placed {entry.Name} in Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}. It is not connected.";
        SaveWorkspace();
    }

    private bool TryCreateTickScriptIndicatorWorkspace(
        TickScriptEntry entry,
        TickScriptIndicatorAppearance appearance,
        IndicatorPlacementResult placement,
        out IndicatorWorkspaceRuntimeContext context)
    {
        context = null!;
        int paneId = AllocateLowestPaneId();
        IndicatorWorkspaceRuntimeContext created = CreateIndicatorWorkspaceContext(paneId);
        created.AppliedIndicators.Add(entry);
        created.IndicatorAppearances[entry.SourcePath] = appearance with { LinkedGroupId = string.Empty };
        created.Stack.AddOrReplace(entry, CreateDisconnectedTickScriptResult(entry), created.IndicatorAppearances[entry.SourcePath]);

        var target = new WorkspacePartitionTarget(
            placement.PlaceAddress.WorkspaceId,
            placement.PlaceAddress.PartitionId,
            string.Empty);
        if (!AttachIndicatorWorkspacePane(created, target, entry.Name))
            return false;

        ConnectConfiguredIndicatorWorkspace(created, placement);
        context = created;
        return true;
    }

    private void PlaceConfiguredBuiltInIndicatorInWorkspace(
        BuiltInIndicatorInstance instance,
        IndicatorPlacementResult placement)
    {
        if (!TryCreateBuiltInIndicatorWorkspace(instance, placement, out IndicatorWorkspaceRuntimeContext context))
            return;

        _indicatorsWindow?.Hide();
        StatusText.Text = context.ConnectedPricePaneId.HasValue
            ? $"Placed {instance.DisplayName} and connected it to Chart {context.ConnectedPricePaneId}."
            : $"Placed {instance.DisplayName} in Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}. It is not connected.";
        SaveWorkspace();
    }

    private bool TryCreateBuiltInIndicatorWorkspace(
        BuiltInIndicatorInstance instance,
        IndicatorPlacementResult placement,
        out IndicatorWorkspaceRuntimeContext context)
    {
        context = null!;
        int paneId = AllocateLowestPaneId();
        IndicatorWorkspaceRuntimeContext created = CreateIndicatorWorkspaceContext(paneId);
        BuiltInIndicatorInstance copy = CloneBuiltInIndicator(instance) with { LinkedGroupId = string.Empty };
        created.BuiltInIndicators.Add(copy);
        created.Stack.AddOrReplace(copy, CreateDisconnectedBuiltInResult(copy));

        var target = new WorkspacePartitionTarget(
            placement.PlaceAddress.WorkspaceId,
            placement.PlaceAddress.PartitionId,
            string.Empty);
        if (!AttachIndicatorWorkspacePane(created, target, copy.DisplayName))
            return false;

        ConnectConfiguredIndicatorWorkspace(created, placement);
        context = created;
        return true;
    }

    private void ConnectConfiguredIndicatorWorkspace(
        IndicatorWorkspaceRuntimeContext context,
        IndicatorPlacementResult placement)
    {
        if (placement.ConnectAddress.PriceChartPaneId is int sourcePaneId &&
            _chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? source))
        {
            SetIndicatorWorkspaceSource(context, source, placement.SyncWithPriceChart);
            return;
        }

        DisconnectIndicatorWorkspaceSource(context);
    }

    private void PlaceTickScriptIndicatorInWorkspace(TickScriptEntry entry)
    {
        WorkspacePartitionTarget? target = SelectWorkspacePartition(entry.Name);
        if (target is null)
            return;

        int paneId = AllocateLowestPaneId();
        IndicatorWorkspaceRuntimeContext context = CreateIndicatorWorkspaceContext(paneId);
        context.AppliedIndicators.Add(entry);
        context.IndicatorAppearances[entry.SourcePath] = TickScriptIndicatorAppearance.Default with { };
        context.Stack.AddOrReplace(
            entry,
            CreateDisconnectedTickScriptResult(entry),
            context.IndicatorAppearances[entry.SourcePath]);

        if (!AttachIndicatorWorkspacePane(context, target, entry.Name))
            return;
        _indicatorsWindow?.Hide();
        StatusText.Text = $"Placed {entry.Name} in Workspace {target.WorkspaceId}, Partition {target.PartitionId}. Use Connect to choose its price chart.";
        SaveWorkspace();
    }

    private void PlaceBuiltInIndicatorInWorkspace(BuiltInIndicatorKind kind)
    {
        BuiltInIndicatorInstance initial = BuiltInIndicatorCatalog.CreateDefault(kind);
        var settings = new BuiltInIndicatorSettingsWindow(initial) { Owner = this };
        if (settings.ShowDialog() != true)
            return;

        WorkspacePartitionTarget? target = SelectWorkspacePartition(settings.Result.DisplayName);
        if (target is null)
            return;

        int paneId = AllocateLowestPaneId();
        IndicatorWorkspaceRuntimeContext context = CreateIndicatorWorkspaceContext(paneId);
        BuiltInIndicatorInstance instance = CloneBuiltInIndicator(settings.Result);
        context.BuiltInIndicators.Add(instance);
        context.Stack.AddOrReplace(instance, CreateDisconnectedBuiltInResult(instance));

        if (!AttachIndicatorWorkspacePane(context, target, instance.DisplayName))
            return;
        _indicatorsWindow?.Hide();
        StatusText.Text = $"Placed {instance.DisplayName} in Workspace {target.WorkspaceId}, Partition {target.PartitionId}. Use Connect to choose its price chart.";
        SaveWorkspace();
    }

    private WorkspacePartitionTarget? SelectWorkspacePartition(string indicatorName)
    {
        WorkspacePartitionTarget[] targets = _workspacePages.Values
            .OrderBy(page => page.Id == _activeWorkspaceId ? 0 : 1)
            .ThenBy(page => page.Id)
            .SelectMany(page => Enumerable.Range(1, page.Surface.LayoutCount)
                .Select(partition => new WorkspacePartitionTarget(
                    page.Id,
                    partition,
                    page.Surface.GetPane(partition)?.Title ?? string.Empty)))
            .ToArray();

        if (targets.Length == 0)
        {
            StatusText.Text = "Create a workspace before placing an indicator.";
            return null;
        }

        var picker = new WorkspacePartitionPickerWindow(
            "Place indicator in workspace",
            $"{indicatorName} will become an independent workspace pane with its own vertical scale. It can be connected to any price chart afterward.",
            targets)
        {
            Owner = this
        };
        return picker.ShowDialog() == true ? picker.SelectedTarget : null;
    }

    private WorkspacePartitionTarget? SelectEmptyWorkspacePartition(string indicatorName)
    {
        WorkspacePartitionTarget[] targets = _workspacePages.Values
            .OrderBy(page => page.Id == _activeWorkspaceId ? 0 : 1)
            .ThenBy(page => page.Id)
            .SelectMany(page => Enumerable.Range(1, page.Surface.LayoutCount)
                .Where(partition => page.Surface.GetPane(partition) is null)
                .Select(partition => new WorkspacePartitionTarget(page.Id, partition, string.Empty)))
            .ToArray();

        if (targets.Length == 0)
        {
            StatusText.Text = "No empty workspace partition is available. Create or divide a workspace first.";
            return null;
        }

        var picker = new WorkspacePartitionPickerWindow(
            "Move indicator to window",
            $"Choose the empty workspace partition that will receive only this {indicatorName} instance.",
            targets)
        {
            Owner = this
        };
        return picker.ShowDialog() == true ? picker.SelectedTarget : null;
    }

    private IndicatorPlacementResult CreateWorkspaceMovePlacement(
        WorkspacePartitionTarget target,
        int? connectedPricePaneId,
        bool syncWithPriceChart)
    {
        var place = new IndicatorPlaceAddress(
            target.WorkspaceId,
            target.PartitionId,
            null,
            $"Workspace {target.WorkspaceId} → Partition {target.PartitionId} → Empty workspace");
        IndicatorConnectionAddress connection = connectedPricePaneId is int paneId &&
            _chartContexts.TryGetValue(paneId, out ChartRuntimeContext? chart) && chart is not null
                ? new IndicatorConnectionAddress(paneId, FormatChartIndicatorAddress(chart))
                : new IndicatorConnectionAddress(null, "Not connected");
        return new IndicatorPlacementResult(place, connection, syncWithPriceChart && connection.PriceChartPaneId.HasValue);
    }

    private void MoveTickScriptIndicatorToWindow(ChartRuntimeContext source, TickScriptEntry entry)
    {
        WorkspacePartitionTarget? target = SelectEmptyWorkspacePartition(entry.Name);
        if (target is null)
            return;
        TickScriptIndicatorAppearance appearance = GetTickScriptAppearance(source, entry.SourcePath) with
        {
            LinkedGroupId = string.Empty
        };
        IndicatorPlacementResult placement = CreateWorkspaceMovePlacement(
            target,
            source.PaneId,
            source.SyncIndicatorsWithPriceChart);
        if (!TryCreateTickScriptIndicatorWorkspace(entry, appearance, placement, out _))
            return;
        RemoveAppliedIndicator(source, entry);
        StatusText.Text = $"Moved {entry.Name} to Workspace {target.WorkspaceId}, Partition {target.PartitionId}.";
        SaveWorkspace();
    }

    private void MoveBuiltInIndicatorToWindow(ChartRuntimeContext source, BuiltInIndicatorInstance instance)
    {
        BuiltInIndicatorInstance? current = source.BuiltInIndicators.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (current is null)
            return;
        WorkspacePartitionTarget? target = SelectEmptyWorkspacePartition(current.DisplayName);
        if (target is null)
            return;
        IndicatorPlacementResult placement = CreateWorkspaceMovePlacement(
            target,
            source.PaneId,
            source.SyncIndicatorsWithPriceChart);
        if (!TryCreateBuiltInIndicatorWorkspace(CloneBuiltInIndicator(current), placement, out _))
            return;
        RemoveBuiltInIndicator(source, current);
        StatusText.Text = $"Moved {current.DisplayName} to Workspace {target.WorkspaceId}, Partition {target.PartitionId}.";
        SaveWorkspace();
    }

    private void MoveIndicatorWorkspaceTickScriptToWindow(
        IndicatorWorkspaceRuntimeContext source,
        TickScriptEntry entry)
    {
        WorkspacePartitionTarget? target = SelectEmptyWorkspacePartition(entry.Name);
        if (target is null)
            return;
        TickScriptIndicatorAppearance appearance = GetIndicatorWorkspaceAppearance(source, entry.SourcePath) with
        {
            LinkedGroupId = string.Empty
        };
        IndicatorPlacementResult placement = CreateWorkspaceMovePlacement(
            target,
            source.ConnectedPricePaneId,
            source.SyncWithPriceChart);
        if (!TryCreateTickScriptIndicatorWorkspace(entry, appearance, placement, out _))
            return;
        RemoveIndicatorWorkspaceTickScript(source, entry);
        StatusText.Text = $"Moved {entry.Name} to Workspace {target.WorkspaceId}, Partition {target.PartitionId}.";
        SaveWorkspace();
    }

    private void MoveIndicatorWorkspaceBuiltInToWindow(
        IndicatorWorkspaceRuntimeContext source,
        BuiltInIndicatorInstance instance)
    {
        BuiltInIndicatorInstance? current = source.BuiltInIndicators.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (current is null)
            return;
        WorkspacePartitionTarget? target = SelectEmptyWorkspacePartition(current.DisplayName);
        if (target is null)
            return;
        IndicatorPlacementResult placement = CreateWorkspaceMovePlacement(
            target,
            source.ConnectedPricePaneId,
            source.SyncWithPriceChart);
        if (!TryCreateBuiltInIndicatorWorkspace(CloneBuiltInIndicator(current), placement, out _))
            return;
        RemoveIndicatorWorkspaceBuiltIn(source, current);
        StatusText.Text = $"Moved {current.DisplayName} to Workspace {target.WorkspaceId}, Partition {target.PartitionId}.";
        SaveWorkspace();
    }

    private IndicatorWorkspaceRuntimeContext CreateIndicatorWorkspaceContext(int paneId)
    {
        var stack = new IndicatorPaneStackControl
        {
            IndependentWorkspaceMode = true,
            SyncWithPriceChart = false
        };
        stack.SetSourceChart(null, connected: false);
        stack.SetChartSettings(ActiveChartContext.Settings);

        var context = new IndicatorWorkspaceRuntimeContext
        {
            PaneId = paneId,
            Stack = stack,
            SyncWithPriceChart = false
        };
        stack.PlacementAddressProvider = () => FormatIndicatorWorkspaceAddress(paneId);
        _indicatorWorkspaceContexts[paneId] = context;
        WireIndicatorWorkspaceContext(context);
        return context;
    }

    private void WireIndicatorWorkspaceContext(IndicatorWorkspaceRuntimeContext context)
    {
        context.Stack.ConnectSourceRequested += () => ConnectIndicatorWorkspaceToPriceChart(context);
        context.Stack.SyncWithPriceChartChanged += value =>
        {
            context.SourceGeneration++;
            context.SyncWithPriceChart = value;
            if (value && TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source))
            {
                context.Stack.SetViewport(source.Chart.CaptureViewportSnapshot());
                context.Stack.SetCrosshairRatio(null);
            }
            RefreshIndicatorWorkspace(context, force: true);
            SaveWorkspace();
        };
        context.Stack.CrosshairRatioChanged += ratio =>
        {
            if (context.SyncWithPriceChart && TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source))
                source.Chart.SetExternalCrosshairRatio(ratio);
        };
        context.Stack.HorizontalWheelRequested += (delta, ratio) =>
        {
            if (context.SyncWithPriceChart && TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source))
                source.Chart.ApplyLinkedHorizontalWheel(delta, ratio);
        };
        context.Stack.HorizontalPanRequested += slots =>
        {
            if (context.SyncWithPriceChart && TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source))
                source.Chart.PanHorizontalBySlots(slots);
        };
        context.Stack.RefreshIndicatorRequested += entry => RefreshIndicatorWorkspace(context, force: true);
        context.Stack.EditIndicatorRequested += entry => EditIndicatorWorkspaceTickScript(context, entry);
        context.Stack.OpenIndicatorEditorRequested += OpenIndicatorInEditor;
        context.Stack.MoveIndicatorToWindowRequested += entry => MoveIndicatorWorkspaceTickScriptToWindow(context, entry);
        context.Stack.MoveIndicatorToChartRequested += entry => RouteIndicatorWorkspaceTickScript(context, entry, IndicatorRouteAction.Move);
        context.Stack.RemoveIndicatorRequested += entry => RemoveIndicatorWorkspaceTickScript(context, entry);
        context.Stack.RouteIndicatorRequested += (entry, action) => RouteIndicatorWorkspaceTickScript(context, entry, action);
        context.Stack.RefreshBuiltInIndicatorRequested += instance => RefreshIndicatorWorkspace(context, force: true);
        context.Stack.EditBuiltInIndicatorRequested += instance => EditIndicatorWorkspaceBuiltIn(context, instance);
        context.Stack.MoveBuiltInIndicatorToWindowRequested += instance => MoveIndicatorWorkspaceBuiltInToWindow(context, instance);
        context.Stack.MoveBuiltInIndicatorToChartRequested += instance => RouteIndicatorWorkspaceBuiltIn(context, instance, IndicatorRouteAction.Move);
        context.Stack.RemoveBuiltInIndicatorRequested += instance => RemoveIndicatorWorkspaceBuiltIn(context, instance);
        context.Stack.RouteBuiltInIndicatorRequested += (instance, action) => RouteIndicatorWorkspaceBuiltIn(context, instance, action);
    }

    private bool AttachIndicatorWorkspacePane(
        IndicatorWorkspaceRuntimeContext context,
        WorkspacePartitionTarget target,
        string indicatorName)
    {
        if (!_workspacePages.TryGetValue(target.WorkspaceId, out WorkspacePageRuntime? page))
        {
            _indicatorWorkspaceContexts.Remove(context.PaneId);
            StatusText.Text = "The selected workspace no longer exists.";
            return false;
        }

        var pane = new WorkspacePaneHandle(
            context.PaneId,
            WorkspacePaneKind.Indicator,
            $"Indicator {context.PaneId} · {indicatorName}",
            context.Stack);
        _workspacePaneRegistry[pane.Id] = pane;
        AttachPaneToTarget(page, target.PartitionId, pane, sourceWorkspaceId: null, sourcePartitionId: null);

        (int? workspace, int? partition) = FindPaneLocation(context.PaneId);
        bool attached = (workspace.HasValue && partition.HasValue) || _floatingPaneWindows.ContainsKey(context.PaneId);
        if (!attached)
        {
            _workspacePaneRegistry.Remove(context.PaneId);
            _indicatorWorkspaceContexts.Remove(context.PaneId);
            return false;
        }

        if (!page.IsDetached)
            SwitchToWorkspace(page.Id, bringDetachedToFront: false);
        return true;
    }

    private void ConnectIndicatorWorkspaceToPriceChart(IndicatorWorkspaceRuntimeContext context)
    {
        ChartRuntimeContext? target = SelectPriceChartForIndicatorWorkspace(
            context,
            context.Stack.Entries.FirstOrDefault()?.Name
                ?? context.Stack.BuiltInEntries.FirstOrDefault()?.DisplayName
                ?? $"Indicator {context.PaneId}",
            IndicatorRouteAction.Connect);
        if (target is null)
            return;
        SetIndicatorWorkspaceSource(context, target, enableSync: context.SyncWithPriceChart);
        StatusText.Text = $"Connected Indicator {context.PaneId} to Chart {target.PaneId}. Use Sync with Price Chart to link or unlink navigation.";
        SaveWorkspace();
    }

    private ChartRuntimeContext? SelectPriceChartForIndicatorWorkspace(
        IndicatorWorkspaceRuntimeContext context,
        string indicatorName,
        IndicatorRouteAction action)
    {
        IndicatorRouteTarget[] targets = _chartContexts.Values
            .OrderBy(chart => FindPaneLocation(chart.PaneId).WorkspaceId ?? int.MaxValue)
            .ThenBy(chart => FindPaneLocation(chart.PaneId).PartitionId ?? int.MaxValue)
            .ThenBy(chart => chart.PaneId)
            .Select(chart =>
            {
                (int? workspace, int? partition) = FindPaneLocation(chart.PaneId);
                return new IndicatorRouteTarget(
                    chart.PaneId,
                    workspace,
                    partition,
                    string.IsNullOrWhiteSpace(chart.Symbol) ? "No symbol" : chart.Symbol,
                    chart.Timeframe.DisplayText);
            })
            .ToArray();
        if (targets.Length == 0)
        {
            StatusText.Text = "Open a price chart before connecting, copying or moving this indicator.";
            return null;
        }

        string? description = action == IndicatorRouteAction.Connect
            ? "Connect chooses the price chart that supplies data and time to this independent indicator pane. The indicator stays in its workspace partition."
            : null;
        var window = new IndicatorRouteWindow(indicatorName, action, targets, description) { Owner = this };
        if (window.ShowDialog() != true)
            return null;
        IndicatorRouteTarget? selectedTarget = window.SelectedTarget;
        return selectedTarget is null ? null : _chartContexts.GetValueOrDefault(selectedTarget.PaneId);
    }

    private void SetIndicatorWorkspaceSource(
        IndicatorWorkspaceRuntimeContext context,
        ChartRuntimeContext source,
        bool enableSync)
    {
        context.SourceGeneration++;
        context.ConnectedPricePaneId = source.PaneId;
        context.SyncWithPriceChart = enableSync;
        context.Stack.SetChartSettings(source.Settings);
        context.Stack.SetSourceChart(FormatIndicatorSourceLabel(source), connected: true);
        context.Stack.SetTimeScaleCandles(source.Chart.Candles);
        context.Stack.SyncWithPriceChart = enableSync;
        context.Stack.SetViewport(source.Chart.CaptureViewportSnapshot());
        RefreshIndicatorWorkspace(context, force: true);
    }

    private void DisconnectIndicatorWorkspaceSource(IndicatorWorkspaceRuntimeContext context)
    {
        context.SourceGeneration++;
        context.ConnectedPricePaneId = null;
        context.SyncWithPriceChart = false;
        context.Stack.SetSourceChart(null, connected: false);
        context.Stack.SetTimeScaleCandles(Array.Empty<Candle>());
        context.Stack.SyncWithPriceChart = false;
    }

    private string FormatIndicatorSourceLabel(ChartRuntimeContext source)
    {
        (int? workspace, int? partition) = FindPaneLocation(source.PaneId);
        string location = workspace.HasValue && partition.HasValue
            ? $"Workspace {workspace} → Partition {partition} → Chart {source.PaneId}"
            : $"Floating → Chart {source.PaneId}";
        string symbol = string.IsNullOrWhiteSpace(source.Symbol) ? "No symbol" : source.Symbol;
        return $"{location} · {symbol} {source.Timeframe.DisplayText}";
    }

    private bool TryGetIndicatorWorkspaceSource(
        IndicatorWorkspaceRuntimeContext context,
        [NotNullWhen(true)] out ChartRuntimeContext? source)
    {
        source = null;
        return context.ConnectedPricePaneId is int paneId &&
               _chartContexts.TryGetValue(paneId, out source);
    }

    private void RefreshAllIndependentIndicatorWorkspaces(bool force = false)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values.ToArray())
            RefreshIndicatorWorkspace(context, force);
    }

    private void RefreshIndicatorWorkspace(IndicatorWorkspaceRuntimeContext context, bool force)
    {
        if (_isClosing || !TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source) || source.Chart.Candles.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        if (!force && now - context.LastRefreshUtc < TimeSpan.FromMilliseconds(400))
        {
            context.RefreshPending = true;
            return;
        }
        if (context.RefreshRunning)
        {
            context.RefreshPending = true;
            return;
        }
        _ = RefreshIndicatorWorkspaceAsync(context, source);
    }

    private async Task RefreshIndicatorWorkspaceAsync(
        IndicatorWorkspaceRuntimeContext context,
        ChartRuntimeContext source)
    {
        if (context.RefreshRunning || source.Chart.Candles.Count == 0)
            return;
        context.RefreshRunning = true;
        context.RefreshPending = false;
        context.LastRefreshUtc = DateTime.UtcNow;

        int paneId = context.PaneId;
        int sourceGeneration = context.SourceGeneration;
        int sourcePaneId = source.PaneId;
        int sourceIdentityGeneration = source.IdentityGeneration;
        // Replay indicators must never see future candles, even when their
        // navigation is not synchronized with the price chart. Navigation may
        // remain independent; the data snapshot always comes from replay.
        Candle[] candles = source.Chart.Candles.ToArray();
        if (candles.Length == 0)
        {
            context.RefreshRunning = false;
            return;
        }
        long lastCandleStartUnix = candles[^1].StartUnix;
        TickScriptEntry[] scripts = context.AppliedIndicators.ToArray();
        BuiltInIndicatorInstance[] builtIns = context.BuiltInIndicators.Select(CloneBuiltInIndicator).ToArray();
        string timeframe = source.Timeframe.DisplayText;

        try
        {
            var calculation = await Task.Run(() =>
            {
                var customResults = new Dictionary<string, TickScriptIndicatorResult>(StringComparer.OrdinalIgnoreCase);
                var builtInResults = new Dictionary<string, BuiltInIndicatorResult>(StringComparer.OrdinalIgnoreCase);
                var store = new TickScriptStore();
                foreach (TickScriptEntry entry in scripts)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    string scriptSource = store.LoadSource(entry);
                    customResults[entry.SourcePath] = TickScriptIndicatorRuntime.Evaluate(entry.Name, scriptSource, candles);
                }

                IReadOnlyList<double?>? first = null;
                IReadOnlyList<double?>? previous = null;
                foreach (BuiltInIndicatorInstance instance in builtIns)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (!instance.VisibleOnAllTimeframes &&
                        !instance.VisibleTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    BuiltInIndicatorResult result = BuiltInIndicatorEngine.Evaluate(instance, candles, first, previous);
                    builtInResults[instance.InstanceId] = result;
                    IndicatorSeriesResult? primary = result.Series.FirstOrDefault(series => series.Style.Visible);
                    if (primary is not null)
                    {
                        previous = primary.Values;
                        first ??= primary.Values;
                    }
                }
                return (customResults, builtInResults);
            }, _lifetime.Token);

            if (_isClosing ||
                !_indicatorWorkspaceContexts.TryGetValue(paneId, out IndicatorWorkspaceRuntimeContext? liveContext) ||
                !ReferenceEquals(liveContext, context) ||
                liveContext.SourceGeneration != sourceGeneration ||
                liveContext.ConnectedPricePaneId != sourcePaneId ||
                !_chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? liveSource) ||
                liveSource.IdentityGeneration != sourceIdentityGeneration)
            {
                return;
            }

            IReadOnlyList<Candle> liveCandles = liveSource.Chart.Candles;
            if (liveCandles.Count == 0 ||
                liveCandles[^1].StartUnix != lastCandleStartUnix)
            {
                return;
            }

            liveContext.IndicatorResults.Clear();
            foreach ((string key, TickScriptIndicatorResult result) in calculation.customResults)
                liveContext.IndicatorResults[key] = result;
            liveContext.BuiltInIndicatorResults.Clear();
            foreach ((string key, BuiltInIndicatorResult result) in calculation.builtInResults)
                liveContext.BuiltInIndicatorResults[key] = result;

            foreach (TickScriptEntry entry in liveContext.AppliedIndicators)
            {
                if (!liveContext.IndicatorResults.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? result))
                    continue;
                TickScriptIndicatorAppearance appearance = GetIndicatorWorkspaceAppearance(liveContext, entry.SourcePath);
                liveContext.Stack.UpdateResult(entry, result, appearance);
            }
            foreach (BuiltInIndicatorInstance instance in liveContext.BuiltInIndicators)
            {
                if (liveContext.BuiltInIndicatorResults.TryGetValue(instance.InstanceId, out BuiltInIndicatorResult? result))
                    liveContext.Stack.UpdateResult(instance, result);
            }
            liveContext.Stack.SetChartSettings(liveSource.Settings);
            liveContext.Stack.SetSourceChart(FormatIndicatorSourceLabel(liveSource), connected: true);
            liveContext.Stack.SetTimeScaleCandles(liveCandles);
            liveContext.Stack.SetViewport(liveSource.Chart.CaptureViewportSnapshot());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Independent indicator calculation failed: {exception.Message}";
        }
        finally
        {
            context.RefreshRunning = false;
            if (context.RefreshPending && !_isClosing)
            {
                context.RefreshPending = false;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => RefreshIndicatorWorkspace(context, force: false)));
            }
        }
    }

    private void SyncIndependentIndicatorWorkspacesViewport(
        ChartRuntimeContext source,
        ChartViewportSnapshot viewport)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId == source.PaneId && context.SyncWithPriceChart)
                context.Stack.SetViewport(viewport);
        }
    }

    private void SyncIndependentIndicatorWorkspacesCrosshair(
        ChartRuntimeContext source,
        double? ratio)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId == source.PaneId && context.SyncWithPriceChart)
                context.Stack.SetCrosshairRatio(ratio);
        }
    }

    private void SyncIndependentIndicatorWorkspacesVertical(
        ChartRuntimeContext source,
        ChartVerticalSyncAction action)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId == source.PaneId && context.SyncWithPriceChart)
                context.Stack.ApplyLinkedVerticalAction(action);
        }
    }

    private TickScriptIndicatorAppearance GetIndicatorWorkspaceAppearance(
        IndicatorWorkspaceRuntimeContext context,
        string sourcePath)
    {
        if (context.IndicatorAppearances.TryGetValue(sourcePath, out TickScriptIndicatorAppearance? appearance))
            return appearance;
        TickScriptIndicatorAppearance created = TickScriptIndicatorAppearance.Default with { };
        context.IndicatorAppearances[sourcePath] = created;
        return created;
    }

    private void EditIndicatorWorkspaceTickScript(
        IndicatorWorkspaceRuntimeContext context,
        TickScriptEntry entry)
    {
        TickScriptIndicatorAppearance current = GetIndicatorWorkspaceAppearance(context, entry.SourcePath);
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions(
            context.PaneId,
            context.ConnectedPricePaneId,
            context.SyncWithPriceChart);
        var settings = new TickScriptIndicatorSettingsWindow(entry, current, options) { Owner = this };
        bool? accepted = settings.ShowDialog();
        if (settings.OpenCodeEditorRequested)
        {
            OpenIndicatorInEditor(entry);
            return;
        }
        if (accepted != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;

        TickScriptIndicatorAppearance updated = settings.Result with { LinkedGroupId = string.Empty };
        ApplyWorkspaceTickScriptPropertyChanges(context, entry, updated, placement);
    }

    private void EditIndicatorWorkspaceBuiltIn(
        IndicatorWorkspaceRuntimeContext context,
        BuiltInIndicatorInstance instance)
    {
        BuiltInIndicatorInstance? current = context.BuiltInIndicators.FirstOrDefault(item => item.InstanceId == instance.InstanceId);
        if (current is null)
            return;
        IndicatorPlacementOptions? options = BuildIndicatorPlacementOptions(
            context.PaneId,
            context.ConnectedPricePaneId,
            context.SyncWithPriceChart);
        var settings = new BuiltInIndicatorSettingsWindow(current, options) { Owner = this };
        if (settings.ShowDialog() != true || settings.PlacementResult is not IndicatorPlacementResult placement)
            return;
        BuiltInIndicatorInstance updated = CloneBuiltInIndicator(settings.Result with
        {
            InstanceId = current.InstanceId,
            LinkedGroupId = string.Empty
        });
        ApplyWorkspaceBuiltInPropertyChanges(context, current, updated, placement);
    }

    private void ApplyWorkspaceTickScriptPropertyChanges(
        IndicatorWorkspaceRuntimeContext context,
        TickScriptEntry entry,
        TickScriptIndicatorAppearance updated,
        IndicatorPlacementResult placement)
    {
        if (placement.PlaceAddress.PriceChartPaneId is int targetChartPaneId &&
            _chartContexts.TryGetValue(targetChartPaneId, out ChartRuntimeContext? targetChart))
        {
            if (!TryCopyTickScriptIndicatorToChart(targetChart, entry, updated))
                return;
            RemoveIndicatorWorkspaceTickScript(context, entry);
            StatusText.Text = $"Moved {entry.Name} to Chart {targetChart.PaneId}.";
            SaveWorkspace();
            return;
        }

        if (placement.PlaceAddress.IndicatorWorkspacePaneId == context.PaneId)
        {
            context.IndicatorAppearances[entry.SourcePath] = updated;
            if (placement.ConnectAddress.PriceChartPaneId is int sourcePaneId &&
                _chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? source))
            {
                SetIndicatorWorkspaceSource(context, source, placement.SyncWithPriceChart);
            }
            else
            {
                DisconnectIndicatorWorkspaceSource(context);
            }

            if (context.IndicatorResults.TryGetValue(entry.SourcePath, out TickScriptIndicatorResult? result))
                context.Stack.UpdateResult(entry, result, updated);
            else
                context.Stack.UpdateResult(entry, CreateDisconnectedTickScriptResult(entry), updated);
            StatusText.Text = $"Updated {entry.Name} placement and connection.";
            SaveWorkspace();
            return;
        }

        if (!TryCreateTickScriptIndicatorWorkspace(entry, updated, placement, out _))
            return;
        RemoveIndicatorWorkspaceTickScript(context, entry);
        StatusText.Text = $"Moved {entry.Name} to Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}.";
        SaveWorkspace();
    }

    private void ApplyWorkspaceBuiltInPropertyChanges(
        IndicatorWorkspaceRuntimeContext context,
        BuiltInIndicatorInstance current,
        BuiltInIndicatorInstance updated,
        IndicatorPlacementResult placement)
    {
        if (placement.PlaceAddress.PriceChartPaneId is int targetChartPaneId &&
            _chartContexts.TryGetValue(targetChartPaneId, out ChartRuntimeContext? targetChart))
        {
            BuiltInIndicatorInstance copy = CloneBuiltInIndicator(updated) with
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                LinkedGroupId = string.Empty
            };
            if (!TryCopyBuiltInIndicatorToChart(targetChart, copy))
                return;
            RemoveIndicatorWorkspaceBuiltIn(context, current);
            StatusText.Text = $"Moved {updated.DisplayName} to Chart {targetChart.PaneId}.";
            SaveWorkspace();
            return;
        }

        if (placement.PlaceAddress.IndicatorWorkspacePaneId == context.PaneId)
        {
            int index = context.BuiltInIndicators.FindIndex(item => item.InstanceId == current.InstanceId);
            if (index < 0)
                return;
            context.BuiltInIndicators[index] = updated;
            if (placement.ConnectAddress.PriceChartPaneId is int sourcePaneId &&
                _chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? source))
            {
                SetIndicatorWorkspaceSource(context, source, placement.SyncWithPriceChart);
            }
            else
            {
                DisconnectIndicatorWorkspaceSource(context);
            }
            RefreshIndicatorWorkspace(context, force: true);
            StatusText.Text = $"Updated {updated.DisplayName} placement and connection.";
            SaveWorkspace();
            return;
        }

        BuiltInIndicatorInstance moved = CloneBuiltInIndicator(updated) with
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            LinkedGroupId = string.Empty
        };
        if (!TryCreateBuiltInIndicatorWorkspace(moved, placement, out _))
            return;
        RemoveIndicatorWorkspaceBuiltIn(context, current);
        StatusText.Text = $"Moved {updated.DisplayName} to Workspace {placement.PlaceAddress.WorkspaceId}, Partition {placement.PlaceAddress.PartitionId}.";
        SaveWorkspace();
    }

    private void RemoveIndicatorWorkspaceTickScript(
        IndicatorWorkspaceRuntimeContext context,
        TickScriptEntry entry)
    {
        context.AppliedIndicators.RemoveAll(item =>
            string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
        context.IndicatorResults.Remove(entry.SourcePath);
        context.IndicatorAppearances.Remove(entry.SourcePath);
        context.Stack.Remove(entry);
        FinishIndicatorWorkspaceRemoval(context, entry.Name);
    }

    private void RemoveIndicatorWorkspaceBuiltIn(
        IndicatorWorkspaceRuntimeContext context,
        BuiltInIndicatorInstance instance)
    {
        context.BuiltInIndicators.RemoveAll(item => item.InstanceId == instance.InstanceId);
        context.BuiltInIndicatorResults.Remove(instance.InstanceId);
        context.Stack.Remove(instance);
        FinishIndicatorWorkspaceRemoval(context, instance.DisplayName);
    }

    private void FinishIndicatorWorkspaceRemoval(
        IndicatorWorkspaceRuntimeContext context,
        string indicatorName)
    {
        if (context.AppliedIndicators.Count == 0 && context.BuiltInIndicators.Count == 0)
        {
            if (_workspacePaneRegistry.TryGetValue(context.PaneId, out WorkspacePaneHandle? pane))
            {
                (int? workspace, int? partition) = FindPaneLocation(context.PaneId);
                if (workspace is int workspaceId && partition is int partitionId &&
                    _workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
                {
                    page.Surface.DetachPane(partitionId);
                }
                RemovePanePermanently(pane);
            }
        }
        StatusText.Text = $"Removed {indicatorName}.";
        SaveWorkspace();
    }

    private void RouteIndicatorWorkspaceTickScript(
        IndicatorWorkspaceRuntimeContext source,
        TickScriptEntry entry,
        IndicatorRouteAction action)
    {
        ChartRuntimeContext? target = SelectPriceChartForIndicatorWorkspace(source, entry.Name, action);
        if (target is null)
            return;

        if (action == IndicatorRouteAction.Connect)
        {
            SetIndicatorWorkspaceSource(source, target, enableSync: source.SyncWithPriceChart);
            StatusText.Text = $"Connected {entry.Name} to Chart {target.PaneId}.";
            SaveWorkspace();
            return;
        }

        TickScriptIndicatorAppearance appearance = GetIndicatorWorkspaceAppearance(source, entry.SourcePath);
        bool copied = TryCopyTickScriptIndicatorToChart(target, entry, appearance);
        if (!copied)
            return;
        if (action == IndicatorRouteAction.Move)
            RemoveIndicatorWorkspaceTickScript(source, entry);
        else
            StatusText.Text = $"Copied {entry.Name} to Chart {target.PaneId}; the workspace indicator remains unchanged.";
        SaveWorkspace();
    }

    private void RouteIndicatorWorkspaceBuiltIn(
        IndicatorWorkspaceRuntimeContext source,
        BuiltInIndicatorInstance instance,
        IndicatorRouteAction action)
    {
        BuiltInIndicatorInstance? current = source.BuiltInIndicators.FirstOrDefault(item => item.InstanceId == instance.InstanceId);
        if (current is null)
            return;
        ChartRuntimeContext? target = SelectPriceChartForIndicatorWorkspace(source, current.DisplayName, action);
        if (target is null)
            return;

        if (action == IndicatorRouteAction.Connect)
        {
            SetIndicatorWorkspaceSource(source, target, enableSync: source.SyncWithPriceChart);
            StatusText.Text = $"Connected {current.DisplayName} to Chart {target.PaneId}.";
            SaveWorkspace();
            return;
        }

        BuiltInIndicatorInstance copy = CloneBuiltInIndicator(current) with
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            LinkedGroupId = string.Empty
        };
        bool copied = TryCopyBuiltInIndicatorToChart(target, copy);
        if (!copied)
            return;
        if (action == IndicatorRouteAction.Move)
            RemoveIndicatorWorkspaceBuiltIn(source, current);
        else
            StatusText.Text = $"Copied {current.DisplayName} to Chart {target.PaneId}; the workspace indicator remains unchanged.";
        SaveWorkspace();
    }

    private bool TryCopyTickScriptIndicatorToChart(
        ChartRuntimeContext target,
        TickScriptEntry entry,
        TickScriptIndicatorAppearance appearance)
    {
        try
        {
            int existing = target.AppliedIndicators.FindIndex(item =>
                string.Equals(item.SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                target.AppliedIndicators[existing] = entry;
            else
                target.AppliedIndicators.Add(entry);
            target.IndicatorAppearances[entry.SourcePath] = appearance with { LinkedGroupId = string.Empty };
            RefreshAppliedIndicatorsForContext(target, force: true);
            if (ReferenceEquals(target, ActiveChartContext))
            {
                ShowIndicatorsForActiveChart();
                RefreshIndicatorsWindowAppliedList();
            }
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Indicator copy failed: {exception.Message}";
            return false;
        }
    }

    private bool TryCopyBuiltInIndicatorToChart(
        ChartRuntimeContext target,
        BuiltInIndicatorInstance instance)
    {
        try
        {
            AddOrReplaceBuiltInIndicator(target, instance, replaceExisting: false);
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Indicator copy failed: {exception.Message}";
            return false;
        }
    }

    private void RemoveIndicatorWorkspaceContext(int paneId)
    {
        _indicatorWorkspaceContexts.Remove(paneId);
    }

    private void HandlePriceChartRemovedForIndicatorWorkspaces(int paneId)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId != paneId)
                continue;
            context.SourceGeneration++;
            context.ConnectedPricePaneId = null;
            context.SyncWithPriceChart = false;
            context.Stack.SetSourceChart(null, connected: false);
            context.Stack.SetTimeScaleCandles(Array.Empty<Candle>());
            context.Stack.SyncWithPriceChart = false;
        }
    }

    private void RefreshIndicatorWorkspaceSourceLabels(ChartRuntimeContext source)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId == source.PaneId)
                context.Stack.SetSourceChart(FormatIndicatorSourceLabel(source), connected: true);
        }
    }

    private void ApplyIndicatorWorkspaceChartSettings(ChartRuntimeContext source)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId == source.PaneId)
                context.Stack.SetChartSettings(source.Settings);
        }
    }

    private static TickScriptIndicatorResult CreateDisconnectedTickScriptResult(TickScriptEntry entry) =>
        new(entry.Name, false, Array.Empty<double?>(), null, null, "Connect this indicator workspace to a price chart.");

    private static BuiltInIndicatorResult CreateDisconnectedBuiltInResult(BuiltInIndicatorInstance instance) =>
        new(
            instance.InstanceId,
            instance.Kind,
            instance.DisplayName,
            BuiltInIndicatorPlacement.SeparateWindow,
            Array.Empty<IndicatorSeriesResult>(),
            instance.Levels,
            instance.UseFixedMinimum ? instance.FixedMinimum : null,
            instance.UseFixedMaximum ? instance.FixedMaximum : null,
            "Connect this indicator workspace to a price chart.");

    private IReadOnlyList<AppliedTickScriptIndicatorPreference> CaptureIndicatorWorkspaceTickScripts(
        IndicatorWorkspaceRuntimeContext context) =>
        context.AppliedIndicators.Select(entry => new AppliedTickScriptIndicatorPreference
        {
            SourcePath = entry.SourcePath,
            Appearance = GetIndicatorWorkspaceAppearance(context, entry.SourcePath) with { }
        }).ToArray();

    private FrameworkElement CreateIndicatorWorkspacePaneFromPreference(WorkspacePanePreference preference)
    {
        IndicatorWorkspaceRuntimeContext context = CreateIndicatorWorkspaceContext(preference.PaneId);
        context.ConnectedPricePaneId = preference.ConnectedPricePaneId > 0
            ? preference.ConnectedPricePaneId
            : null;
        context.SyncWithPriceChart = preference.SyncIndicatorsWithPriceChart;
        context.Stack.SyncWithPriceChart = false;

        IReadOnlyList<TickScriptEntry> available = new TickScriptStore().GetIndicators();
        foreach (AppliedTickScriptIndicatorPreference saved in preference.TickScriptIndicators ?? Array.Empty<AppliedTickScriptIndicatorPreference>())
        {
            TickScriptEntry? entry = available.FirstOrDefault(item =>
                string.Equals(item.SourcePath, saved.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;
            context.AppliedIndicators.Add(entry);
            context.IndicatorAppearances[entry.SourcePath] =
                (saved.Appearance ?? TickScriptIndicatorAppearance.Default) with { LinkedGroupId = string.Empty };
            context.Stack.AddOrReplace(entry, CreateDisconnectedTickScriptResult(entry), context.IndicatorAppearances[entry.SourcePath]);
        }
        foreach (BuiltInIndicatorInstance saved in preference.BuiltInIndicators ?? Array.Empty<BuiltInIndicatorInstance>())
        {
            BuiltInIndicatorInstance instance = CloneBuiltInIndicator(saved) with { LinkedGroupId = string.Empty };
            context.BuiltInIndicators.Add(instance);
            context.Stack.AddOrReplace(instance, CreateDisconnectedBuiltInResult(instance));
        }
        context.Stack.SetChartSettings(preference.ChartSettings ?? ActiveChartContext.Settings);
        context.Stack.RestoreViewportState(preference.Viewport);
        context.Stack.SetSourceChart(null, connected: false);
        context.Stack.SetTimeScaleCandles(Array.Empty<Candle>());
        return context.Stack;
    }

    private void RestoreIndependentIndicatorConnections(bool clearMissingSources)
    {
        foreach (IndicatorWorkspaceRuntimeContext context in _indicatorWorkspaceContexts.Values)
        {
            if (context.ConnectedPricePaneId is int sourcePaneId &&
                _chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? source))
            {
                SetIndicatorWorkspaceSource(context, source, context.SyncWithPriceChart);
                continue;
            }

            // Floating price charts are restored after embedded workspace panes. Keep the
            // saved source id during that first phase so the second pass can reconnect it.
            if (!clearMissingSources && context.ConnectedPricePaneId.HasValue)
                continue;

            context.ConnectedPricePaneId = null;
            context.SyncWithPriceChart = false;
            context.Stack.SetSourceChart(null, connected: false);
            context.Stack.SetTimeScaleCandles(Array.Empty<Candle>());
            context.Stack.SyncWithPriceChart = false;
        }
    }
}
