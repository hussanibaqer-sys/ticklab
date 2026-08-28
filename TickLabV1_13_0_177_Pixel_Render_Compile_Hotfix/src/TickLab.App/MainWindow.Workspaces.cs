using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Drawing;
using TickLab.Core.Indicators;
using TickLab.Core.Settings;
using TickLab.Core.Scripting;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Settings;
using TickLab.Desktop.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private sealed class WorkspacePageRuntime
    {
        public required int Id { get; init; }
        public required WorkspaceSurfaceControl Surface { get; init; }
        public bool IsDetached { get; set; }
        public bool IsMinimized { get; set; }
        public DetachedWorkspaceWindow? Window { get; set; }
        public WorkspacePagePreference? RestorePreference { get; set; }
    }

    private readonly Dictionary<int, WorkspacePageRuntime> _workspacePages = new();
    private readonly Dictionary<int, WorkspacePaneHandle> _workspacePaneRegistry = new();
    private readonly Dictionary<int, DetachedChartWindow> _floatingPaneWindows = new();
    private readonly HashSet<int> _floatingPaneClosingForAttach = new();
    private readonly List<WorkspacePanePreference> _pendingFloatingPaneRestore = new();
    private readonly List<int> _pendingDetachedWorkspaceRestore = new();
    private int _activeWorkspaceId;
    private int _preferredWorkspaceLayout = 1;
    private bool _workspaceSystemInitialized;
    private bool _primaryChartContentAssigned;
    private bool _brushPaletteOpen;

    private void InitializeWorkspaceSystem()
    {
        if (_workspaceSystemInitialized)
            return;

        _workspaceSystemInitialized = true;
        _preferredWorkspaceLayout = _preferences.PreferredWorkspaceLayout is 1 or 2 or 3 or 4 or 6
            ? _preferences.PreferredWorkspaceLayout
            : 1;

        WorkspacePageHost.Content = null;

        if (!_preferences.WorkspaceStateInitialized)
        {
            WorkspacePageRuntime page = CreateWorkspacePage(1, 1);
            WorkspacePaneHandle pane = CreatePriceChartPane(1);
            page.Surface.AttachPane(1, pane);
            ChartRuntimeContext primaryContext = GetChartContext(1);
            page.Surface.UpdatePaneIdentity(1, primaryContext.Symbol, primaryContext.Timeframe.DisplayText);
            _activeWorkspaceId = 1;
        }
        else
        {
            RestoreWorkspacePages(_preferences.Workspaces);
            _pendingFloatingPaneRestore.AddRange(_preferences.FloatingPanes ?? Array.Empty<WorkspacePanePreference>());
            _activeWorkspaceId = _preferences.ActiveWorkspaceId;
        }

        RestoreIndependentIndicatorConnections(clearMissingSources: false);

        if (_workspacePages.Count > 0)
        {
            if (!_workspacePages.ContainsKey(_activeWorkspaceId))
                _activeWorkspaceId = _workspacePages.Keys.Min();
            WorkspacePageRuntime activePage = _workspacePages[_activeWorkspaceId];
            if (activePage.IsMinimized && !activePage.IsDetached)
                ShowEmptyWorkspaceBackground($"Workspace {_activeWorkspaceId} is minimized");
            else
                SwitchToWorkspace(_activeWorkspaceId, bringDetachedToFront: false);
        }
        else
        {
            _activeWorkspaceId = 0;
            ShowEmptyWorkspaceBackground();
        }

        RefreshWorkspaceTabs();
    }

    private void RestoreWorkspacePages(IReadOnlyList<WorkspacePagePreference> preferences)
    {
        bool primaryUsed = false;
        foreach (WorkspacePagePreference saved in preferences
                     .Where(item => item.WorkspaceId > 0)
                     .OrderBy(item => item.WorkspaceId))
        {
            if (_workspacePages.ContainsKey(saved.WorkspaceId))
                continue;

            WorkspacePageRuntime page = CreateWorkspacePage(saved.WorkspaceId, saved.LayoutCount);
            page.IsDetached = saved.IsDetached;
            page.IsMinimized = saved.IsMinimized;
            page.Surface.ShowEmbeddedWindowControls = !saved.IsDetached;
            page.RestorePreference = saved;

            foreach (WorkspacePanePreference panePreference in saved.Panes
                         .Where(item => !item.IsFloating && item.PaneId > 0)
                         .OrderBy(item => item.PartitionId))
            {
                WorkspacePaneHandle pane = CreatePaneFromPreference(panePreference, ref primaryUsed);
                int partition = panePreference.PartitionId is >= 1 and <= 6
                    ? panePreference.PartitionId
                    : page.Surface.FirstEmptyPartition() ?? 1;
                if (!page.Surface.AttachPane(partition, pane))
                    _pendingFloatingPaneRestore.Add(panePreference with { IsFloating = true, PartitionId = 0 });
                else if (pane.Kind == WorkspacePaneKind.PriceChart)
                    page.Surface.UpdatePaneIdentity(pane.Id, panePreference.Symbol, panePreference.Timeframe);
            }

            if (saved.IsDetached)
                _pendingDetachedWorkspaceRestore.Add(saved.WorkspaceId);
        }
    }

    private WorkspacePageRuntime CreateWorkspacePage(int workspaceId, int layoutCount)
    {
        var surface = new WorkspaceSurfaceControl(workspaceId, layoutCount);
        var page = new WorkspacePageRuntime { Id = workspaceId, Surface = surface };
        _workspacePages[workspaceId] = page;

        surface.PartitionSelected += (_, partitionId) =>
        {
            _activeWorkspaceId = workspaceId;
            RememberIndicatorPlacementTarget(workspaceId, partitionId, surface.GetPane(partitionId));
            SaveWorkspace();
        };
        surface.EmptyPartitionContextRequested += (_, partitionId) =>
            ShowEmptyPartitionAddMenu(workspaceId, partitionId, surface);
        surface.PaneDropped += (_, request) => HandleWorkspacePaneDrop(request);
        surface.PaneDetachRequested += (_, request) => DetachPaneToFloating(request.WorkspaceId, request.PartitionId);
        surface.PaneCloseRequested += (_, request) => CloseWorkspacePane(request.WorkspaceId, request.PartitionId);
        surface.PaneActivated += (_, request) =>
        {
            RememberIndicatorPlacementTarget(request.WorkspaceId, request.PartitionId, request.Pane);
            ActivateWorkspacePane(request.Pane.Id);
        };
        surface.WorkspaceDetachRequested += (_, _) => DetachWorkspace(workspaceId);
        surface.WorkspaceMinimizeRequested += (_, _) => MinimizeWorkspaceInTickLab(workspaceId);
        surface.WorkspaceMaximizeRequested += (_, _) => MaximizeWorkspaceInTickLab(workspaceId);
        surface.WorkspaceCloseRequested += (_, _) => RequestCloseWorkspace(workspaceId);
        surface.WorkspaceChanged += (_, _) =>
        {
            RefreshWorkspaceTabs();
            SaveWorkspace();
        };
        return page;
    }

    private WorkspacePaneHandle CreatePaneFromPreference(
        WorkspacePanePreference preference,
        ref bool primaryUsed)
    {
        WorkspacePaneKind kind = Enum.TryParse(preference.Kind, true, out WorkspacePaneKind parsed)
            ? parsed
            : WorkspacePaneKind.Other;

        FrameworkElement content;
        if (kind == WorkspacePaneKind.PriceChart)
        {
            if (preference.PaneId == 1 && !primaryUsed && !_primaryChartContentAssigned)
            {
                content = MainChartPaneRoot;
                UpdateChartContextIdentity(1, preference.Symbol, preference.Timeframe);
                ChartRuntimeContext primaryContext = GetChartContext(1);
                primaryContext.Settings = EnforceSyntheticSecondsLock(
                    preference.ChartSettings ?? ChartSettings.Default,
                    primaryContext.Timeframe);
                primaryContext.BuiltInIndicators.Clear();
                primaryContext.BuiltInIndicators.AddRange(CloneBuiltInIndicators(preference.BuiltInIndicators ?? Array.Empty<BuiltInIndicatorInstance>()));
                primaryContext.AppliedIndicators.Clear();
                primaryContext.IndicatorResults.Clear();
                primaryContext.IndicatorAppearances.Clear();
                RestoreTickScriptIndicatorsForContext(primaryContext, preference.TickScriptIndicators, evaluateImmediately: false);
                primaryContext.SyncIndicatorsWithPriceChart = preference.SyncIndicatorsWithPriceChart;
                primaryContext.IndicatorStack.SyncWithPriceChart = primaryContext.SyncIndicatorsWithPriceChart;
                PrimaryCandleChart.Settings = primaryContext.Settings;
                PrimaryCandleChart.RestoreViewport(preference.Viewport);
                if (!string.IsNullOrWhiteSpace(preference.DrawingDocument))
                    PrimaryCandleChart.ImportDrawingWorkspaceJson(preference.DrawingDocument);
                _primaryChartContentAssigned = true;
                primaryUsed = true;
            }
            else
            {
                var chartPane = CreateChartPaneControl(preference.PaneId);
                chartPane.Symbol = preference.Symbol;
                chartPane.Timeframe = preference.Timeframe;
                UpdateChartContextIdentity(preference.PaneId, preference.Symbol, preference.Timeframe);
                ChartRuntimeContext restoredContext = GetChartContext(preference.PaneId);
                restoredContext.Settings = EnforceSyntheticSecondsLock(
                    preference.ChartSettings ?? ChartSettings.Default,
                    restoredContext.Timeframe);
                restoredContext.BuiltInIndicators.Clear();
                restoredContext.BuiltInIndicators.AddRange(CloneBuiltInIndicators(preference.BuiltInIndicators ?? Array.Empty<BuiltInIndicatorInstance>()));
                restoredContext.AppliedIndicators.Clear();
                restoredContext.IndicatorResults.Clear();
                restoredContext.IndicatorAppearances.Clear();
                RestoreTickScriptIndicatorsForContext(restoredContext, preference.TickScriptIndicators, evaluateImmediately: false);
                restoredContext.SyncIndicatorsWithPriceChart = preference.SyncIndicatorsWithPriceChart;
                restoredContext.IndicatorStack.SyncWithPriceChart = restoredContext.SyncIndicatorsWithPriceChart;
                chartPane.Chart.Settings = restoredContext.Settings;
                chartPane.Chart.RestoreViewport(preference.Viewport);
                if (!string.IsNullOrWhiteSpace(preference.DrawingDocument))
                    chartPane.Chart.ImportDrawingWorkspaceJson(preference.DrawingDocument);
                content = chartPane;
            }
        }
        else if (kind == WorkspacePaneKind.Indicator)
        {
            content = CreateIndicatorWorkspacePaneFromPreference(preference);
        }
        else
        {
            content = CreateRestoredToolPlaceholder(kind, preference.Title);
        }

        var pane = new WorkspacePaneHandle(
            preference.PaneId,
            kind,
            string.IsNullOrWhiteSpace(preference.Title)
                ? DefaultPaneTitle(preference.PaneId, kind)
                : preference.Title,
            content);
        _workspacePaneRegistry[pane.Id] = pane;
        return pane;
    }

    private WorkspacePaneHandle CreatePriceChartPane(int paneId)
    {
        FrameworkElement content;
        if (paneId == 1 && !_primaryChartContentAssigned)
        {
            content = MainChartPaneRoot;
            _primaryChartContentAssigned = true;
        }
        else
        {
            content = CreateChartPaneControl(paneId);
        }

        string symbol = string.IsNullOrWhiteSpace(_requestedSymbol) ? "Price Chart" : _requestedSymbol;
        var pane = new WorkspacePaneHandle(
            paneId,
            WorkspacePaneKind.PriceChart,
            $"Chart {paneId} · {symbol}",
            content);
        _workspacePaneRegistry[paneId] = pane;
        return pane;
    }

    private ChartPaneControl CreateChartPaneControl(int paneId)
    {
        var pane = new ChartPaneControl
        {
            Symbol = _requestedSymbol,
            Timeframe = _activeTimeframe.DisplayText
        };
        RegisterChartContext(paneId, pane.Chart, pane.TickChart, pane);
        ChartRuntimeContext context = GetChartContext(paneId);
        context.Settings = _preferences.Chart;
        pane.Chart.Settings = context.Settings;
        pane.Chart.Drop += CandleChart_Drop;
        pane.UpdateChart(
            _displayCandles,
            CandleChart.TimelineGaps,
            _candleMarkers,
            context.Settings,
            CandleChart.NativeHistoryBoundaryUnix,
            CandleChart.HistoryBoundaryLabel);
        return pane;
    }

    private static FrameworkElement CreateRestoredToolPlaceholder(WorkspacePaneKind kind, string title)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 17, 29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(35, 53, 77)),
            BorderThickness = new Thickness(1)
        };
        root.Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(title) ? kind.ToString() : title,
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 233, 244)),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Saved workspace position restored. Open the source again to reconnect live content.",
                    Foreground = new SolidColorBrush(Color.FromRgb(119, 139, 163)),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 360,
                    Margin = new Thickness(0, 8, 0, 0)
                }
            }
        };
        return root;
    }

    private static string DefaultPaneTitle(int paneId, WorkspacePaneKind kind) =>
        kind == WorkspacePaneKind.PriceChart ? $"Chart {paneId}" : $"{kind} {paneId}";

    private void RestoreWorkspaceWindowsAfterLoad()
    {
        if (!_workspaceSystemInitialized)
            return;

        foreach (int workspaceId in _pendingDetachedWorkspaceRestore.ToArray())
        {
            if (_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
                OpenDetachedWorkspaceWindow(page, page.RestorePreference);
        }
        _pendingDetachedWorkspaceRestore.Clear();

        foreach (WorkspacePanePreference preference in _pendingFloatingPaneRestore.ToArray())
        {
            WorkspacePaneHandle pane;
            if (_workspacePaneRegistry.TryGetValue(preference.PaneId, out WorkspacePaneHandle? existing))
            {
                pane = existing;
            }
            else
            {
                bool primaryUsed = _primaryChartContentAssigned;
                pane = CreatePaneFromPreference(preference, ref primaryUsed);
            }
            OpenFloatingPane(pane, preference);
        }
        _pendingFloatingPaneRestore.Clear();
        RestoreIndependentIndicatorConnections(clearMissingSources: true);
        RefreshWorkspaceTabs();
    }

    private void AddWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        int id = AllocateLowestWorkspaceId();
        CreateWorkspacePage(id, _preferredWorkspaceLayout);
        SwitchToWorkspace(id, bringDetachedToFront: false);
        StatusText.Text = $"Workspace {id} created with {_preferredWorkspaceLayout} partition(s).";
        SaveWorkspace();
    }

    private void DivideWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = DivideWorkspaceButton };
        AddLayoutMenuItem(menu, 1, "□  Single workspace");
        AddLayoutMenuItem(menu, 2, "▥  Two — left / right");
        AddLayoutMenuItem(menu, 3, "▥  Three — left / centre / right");
        AddLayoutMenuItem(menu, 4, "▦  Four — two up / two down");
        AddLayoutMenuItem(menu, 6, "▦  Six — three up / three down");
        menu.IsOpen = true;
    }

    private void AddLayoutMenuItem(ContextMenu menu, int layout, string label)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = _preferredWorkspaceLayout == layout
        };
        item.Click += (_, _) => ApplyWorkspaceLayout(layout);
        menu.Items.Add(item);
    }

    private void ApplyWorkspaceLayout(int layout)
    {
        _preferredWorkspaceLayout = layout;
        DivideWorkspaceButton.Content = $"▦ {layout}";

        if (_activeWorkspaceId > 0 &&
            _workspacePages.TryGetValue(_activeWorkspaceId, out WorkspacePageRuntime? page))
        {
            IReadOnlyList<WorkspacePaneHandle> overflow = page.Surface.SetLayout(layout);
            foreach (WorkspacePaneHandle attached in page.Surface.Panes.Values)
            {
                if (attached.Kind == WorkspacePaneKind.PriceChart && _chartContexts.TryGetValue(attached.Id, out ChartRuntimeContext? attachedContext))
                    page.Surface.UpdatePaneIdentity(attached.Id, attachedContext.Symbol, attachedContext.Timeframe.DisplayText);
            }
            foreach (WorkspacePaneHandle pane in overflow)
                OpenFloatingPane(pane);
        }

        SaveWorkspace();
    }

    private void ShowEmptyPartitionAddMenu(
        int workspaceId,
        int partitionId,
        WorkspaceSurfaceControl surface)
    {
        if (!surface.IsPartitionEmpty(partitionId))
            return;

        _activeWorkspaceId = workspaceId;
        surface.SelectPartition(partitionId);
        RememberIndicatorPlacementTarget(workspaceId, partitionId, pane: null);

        var menu = new ContextMenu { PlacementTarget = surface, Placement = PlacementMode.MousePoint };
        var add = new MenuItem { Header = "Add" };
        var chart = new MenuItem { Header = "Chart" };
        var indicator = new MenuItem { Header = "Indicator" };
        chart.Click += async (_, _) => await AddChartToExactPartitionAsync(workspaceId, partitionId);
        indicator.Click += (_, _) =>
        {
            RememberIndicatorPlacementTarget(workspaceId, partitionId, pane: null);
            IndicatorsButton_Click(this, new RoutedEventArgs());
        };
        add.Items.Add(chart);
        add.Items.Add(indicator);
        menu.Items.Add(add);
        menu.Items.Add(new Separator());
        var favoritesTabs = new MenuItem { Header = "Favorites Tabs" };
        var favoritesOn = new MenuItem { Header = "On" };
        var favoritesOff = new MenuItem { Header = "Off" };
        favoritesOn.Click += (_, _) => SetDrawingFavoritesProjectionVisible(true);
        favoritesOff.Click += (_, _) => SetDrawingFavoritesProjectionVisible(false);
        favoritesTabs.Items.Add(favoritesOn);
        favoritesTabs.Items.Add(favoritesOff);
        menu.Items.Add(favoritesTabs);
        menu.IsOpen = true;
    }

    private async Task AddChartToExactPartitionAsync(int workspaceId, int partitionId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page) ||
            !page.Surface.IsPartitionEmpty(partitionId))
        {
            StatusText.Text = "That workspace partition is no longer empty.";
            return;
        }

        Mt5SymbolInfo? selectedSymbol = await ShowSymbolPickerForSelectionAsync();
        if (selectedSymbol is null)
            return;

        int paneId = AllocateLowestPaneId();
        WorkspacePaneHandle pane = CreatePriceChartPane(paneId);
        if (!page.Surface.AttachPane(partitionId, pane))
        {
            RemovePanePermanently(pane);
            StatusText.Text = "Could not place the chart because the partition is occupied.";
            return;
        }

        _activeWorkspaceId = workspaceId;
        SwitchToWorkspace(workspaceId, bringDetachedToFront: false);
        ActivateWorkspacePane(paneId);
        await SafeSelectChartAsync(selectedSymbol.Name, _activeTimeframe);
        page.Surface.UpdatePaneIdentity(paneId, selectedSymbol.Name, _activeTimeframe.DisplayText);
        StatusText.Text = $"Opened {selectedSymbol.Name} in Workspace {workspaceId}, Partition {partitionId}.";
        SaveWorkspace();
    }

    private void CreateWorkspaceChart()
    {
        int paneId = AllocateLowestPaneId();
        WorkspacePaneHandle pane = CreatePriceChartPane(paneId);

        if (_activeWorkspaceId > 0 &&
            _workspacePages.TryGetValue(_activeWorkspaceId, out WorkspacePageRuntime? page) &&
            !page.IsDetached &&
            page.Surface.SelectedPartitionId is int selected)
        {
            AttachPaneToTarget(page, selected, pane, sourceWorkspaceId: null, sourcePartitionId: null);
            SwitchToWorkspace(page.Id, bringDetachedToFront: false);
            StatusText.Text = $"Chart {paneId} attached to Workspace {page.Id}, Partition {selected}.";
        }
        else
        {
            OpenFloatingPane(pane);
            StatusText.Text = $"Opened floating Chart {paneId}. Use the blue MOVE TO WORKSPACE handle to place it in any workspace partition.";
        }

        SaveWorkspace();
    }

    private int AllocateLowestPaneId()
    {
        int id = 1;
        while (_workspacePaneRegistry.ContainsKey(id) || _floatingPaneWindows.ContainsKey(id))
            id++;
        return id;
    }

    private int AllocateLowestWorkspaceId()
    {
        int id = 1;
        while (_workspacePages.ContainsKey(id))
            id++;
        return id;
    }

    private void SwitchToWorkspace(int workspaceId, bool bringDetachedToFront = true)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
        {
            ShowEmptyWorkspaceBackground();
            return;
        }

        _activeWorkspaceId = workspaceId;
        page.IsMinimized = false;
        if (page.IsDetached)
        {
            ShowEmptyWorkspaceBackground($"Workspace {workspaceId} is detached");
            if (bringDetachedToFront)
                page.Window?.BringToFront();
        }
        else
        {
            page.Surface.ShowEmbeddedWindowControls = true;
            RemoveFromVisualParent(page.Surface);
            WorkspacePageHost.Content = page.Surface;
            MainChartHeaderBorder.Visibility = Visibility.Collapsed;
            ChartTitleText.Text = $"Workspace {workspaceId}";
        }

        RefreshWorkspaceTabs();
        SaveWorkspace();
    }

    private void MinimizeWorkspaceInTickLab(int workspaceId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;
        if (page.IsDetached)
        {
            if (page.Window is not null)
                page.Window.WindowState = WindowState.Minimized;
            return;
        }

        page.IsMinimized = true;
        if (ReferenceEquals(WorkspacePageHost.Content, page.Surface))
            WorkspacePageHost.Content = null;
        _activeWorkspaceId = workspaceId;
        ShowEmptyWorkspaceBackground($"Workspace {workspaceId} is minimized");
        StatusText.Text = $"Workspace {workspaceId} minimized. Click its bottom tab to restore it.";
        RefreshWorkspaceTabs();
        SaveWorkspace();
    }

    private void MaximizeWorkspaceInTickLab(int workspaceId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;
        if (page.IsDetached)
        {
            if (page.Window is not null)
                page.Window.WindowState = WindowState.Maximized;
            return;
        }

        page.IsMinimized = false;
        SwitchToWorkspace(workspaceId, bringDetachedToFront: false);
        StatusText.Text = $"Workspace {workspaceId} fills the available TickLab workspace frame.";
    }

    private void ShowEmptyWorkspaceBackground(string? message = null)
    {
        MainChartHeaderBorder.Visibility = Visibility.Collapsed;
        WorkspacePageHost.Content = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 5, 5)),
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(message)
                        ? "No workspace attached\nUse + Workspace to create one"
                        : message,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                    FontSize = 15,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private void RefreshWorkspaceTabs()
    {
        if (WorkspaceTabsPanel is null)
            return;

        WorkspaceTabsPanel.Children.Clear();
        foreach (WorkspacePageRuntime page in _workspacePages.Values.OrderBy(item => item.Id))
        {
            bool active = page.Id == _activeWorkspaceId;
            int itemCount = page.Surface.Panes.Count;
            var button = new Button
            {
                Height = 30,
                MinHeight = 30,
                MinWidth = 112,
                MaxWidth = 175,
                Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 5, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 11.5,
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                Background = active
                    ? new SolidColorBrush(Color.FromRgb(31, 31, 31))
                    : new SolidColorBrush(Color.FromRgb(12, 12, 12)),
                BorderBrush = active
                    ? new SolidColorBrush(Color.FromRgb(82, 82, 82))
                    : new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                Content = $"{(page.IsDetached ? "↗ " : page.IsMinimized ? "— " : string.Empty)}Workspace {page.Id}  ·  {itemCount}",
                ToolTip = page.IsDetached
                    ? $"Bring detached Workspace {page.Id} to front"
                    : page.IsMinimized
                        ? $"Restore Workspace {page.Id}"
                        : $"Show Workspace {page.Id}"
            };
            button.Click += (_, _) => SwitchToWorkspace(page.Id);
            button.ContextMenu = BuildWorkspaceTabContextMenu(page);
            WorkspaceTabsPanel.Children.Add(button);
        }
    }

    private ContextMenu BuildWorkspaceTabContextMenu(WorkspacePageRuntime page)
    {
        var menu = new ContextMenu();
        var detach = new MenuItem { Header = page.IsDetached ? "Attach to TickLab" : "Detach workspace" };
        detach.Click += (_, _) =>
        {
            if (page.IsDetached)
                AttachWorkspaceToTickLab(page.Id);
            else
                DetachWorkspace(page.Id);
        };
        menu.Items.Add(detach);

        var layoutMenu = new MenuItem { Header = "Divide workspace" };
        foreach (int layout in new[] { 1, 2, 3, 4, 6 })
        {
            var item = new MenuItem { Header = layout == 1 ? "Single" : $"{layout} partitions" };
            item.Click += (_, _) =>
            {
                IReadOnlyList<WorkspacePaneHandle> overflow = page.Surface.SetLayout(layout);
                foreach (WorkspacePaneHandle pane in overflow)
                    OpenFloatingPane(pane);
                _preferredWorkspaceLayout = layout;
                SaveWorkspace();
            };
            layoutMenu.Items.Add(item);
        }
        menu.Items.Add(layoutMenu);

        var close = new MenuItem { Header = "Close workspace" };
        close.Click += (_, _) => RequestCloseWorkspace(page.Id);
        menu.Items.Add(close);
        return menu;
    }

    private void HandleWorkspacePaneDrop(WorkspacePaneDropRequest request)
    {
        if (!_workspacePages.TryGetValue(request.WorkspaceId, out WorkspacePageRuntime? targetPage) ||
            !_workspacePaneRegistry.TryGetValue(request.PaneId, out WorkspacePaneHandle? pane))
            return;

        (int? sourceWorkspace, int? sourcePartition) = FindPaneLocation(request.PaneId);
        AttachPaneToTarget(targetPage, request.PartitionId, pane, sourceWorkspace, sourcePartition);
    }

    private void AttachPaneToTarget(
        WorkspacePageRuntime targetPage,
        int targetPartition,
        WorkspacePaneHandle pane,
        int? sourceWorkspaceId,
        int? sourcePartitionId)
    {
        if (sourceWorkspaceId == targetPage.Id && sourcePartitionId == targetPartition)
            return;

        WorkspacePaneHandle? occupant = targetPage.Surface.GetPane(targetPartition);
        OccupiedPartitionDecision decision = OccupiedPartitionDecision.Replace;
        if (occupant is not null)
        {
            decision = WorkspaceDecisionDialog.ShowOccupiedPartition(this, occupant.Title, pane.Title);
            if (decision == OccupiedPartitionDecision.Cancel)
                return;
        }

        WorkspacePaneHandle incoming = TakePaneFromCurrentLocation(pane.Id) ?? pane;
        WorkspacePaneHandle? displaced = occupant is null
            ? null
            : targetPage.Surface.DetachPane(targetPartition);

        if (!targetPage.Surface.AttachPane(targetPartition, incoming))
        {
            if (displaced is not null)
                targetPage.Surface.AttachPane(targetPartition, displaced);
            OpenFloatingPane(incoming);
            return;
        }
        if (incoming.Kind == WorkspacePaneKind.PriceChart && _chartContexts.TryGetValue(incoming.Id, out ChartRuntimeContext? incomingContext))
        {
            targetPage.Surface.UpdatePaneIdentity(incoming.Id, incomingContext.Symbol, incomingContext.Timeframe.DisplayText);
            RefreshIndicatorWorkspaceSourceLabels(incomingContext);
        }

        if (displaced is not null)
        {
            if (decision == OccupiedPartitionDecision.Swap &&
                sourceWorkspaceId is int sourceWorkspace &&
                sourcePartitionId is int sourcePartition &&
                _workspacePages.TryGetValue(sourceWorkspace, out WorkspacePageRuntime? sourcePage) &&
                sourcePage.Surface.IsPartitionEmpty(sourcePartition))
            {
                sourcePage.Surface.AttachPane(sourcePartition, displaced);
                if (displaced.Kind == WorkspacePaneKind.PriceChart && _chartContexts.TryGetValue(displaced.Id, out ChartRuntimeContext? displacedContext))
                {
                    sourcePage.Surface.UpdatePaneIdentity(displaced.Id, displacedContext.Symbol, displacedContext.Timeframe.DisplayText);
                    RefreshIndicatorWorkspaceSourceLabels(displacedContext);
                }
            }
            else if (decision == OccupiedPartitionDecision.Replace)
            {
                RemovePanePermanently(displaced);
            }
            else
            {
                OpenFloatingPane(displaced);
            }
        }

        targetPage.Surface.ClearSelection();
        _activeWorkspaceId = targetPage.Id;
        if (!targetPage.IsDetached)
            SwitchToWorkspace(targetPage.Id, bringDetachedToFront: false);
        else
            targetPage.Window?.BringToFront();
        SaveWorkspace();
    }

    private WorkspacePaneHandle? TakePaneFromCurrentLocation(int paneId)
    {
        if (_floatingPaneWindows.TryGetValue(paneId, out DetachedChartWindow? floating))
        {
            FrameworkElement? content = floating.ReleaseHostedContent();
            _floatingPaneClosingForAttach.Add(paneId);
            _floatingPaneWindows.Remove(paneId);
            _detachedChartWindows.Remove(floating);
            floating.Close();
            if (_workspacePaneRegistry.TryGetValue(paneId, out WorkspacePaneHandle? pane) && content is not null)
                return pane;
        }

        foreach (WorkspacePageRuntime page in _workspacePages.Values)
        {
            WorkspacePaneHandle? pane = page.Surface.DetachPaneById(paneId);
            if (pane is not null)
                return pane;
        }

        return _workspacePaneRegistry.GetValueOrDefault(paneId);
    }

    private (int? WorkspaceId, int? PartitionId) FindPaneLocation(int paneId)
    {
        foreach (WorkspacePageRuntime page in _workspacePages.Values)
        {
            int? partition = page.Surface.FindPartitionForPane(paneId);
            if (partition.HasValue)
                return (page.Id, partition);
        }
        return (null, null);
    }

    private void DetachPaneToFloating(int workspaceId, int partitionId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;
        WorkspacePaneHandle? pane = page.Surface.DetachPane(partitionId);
        if (pane is null)
            return;
        OpenFloatingPane(pane);
        SaveWorkspace();
    }

    private void CloseWorkspacePane(int workspaceId, int partitionId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;
        WorkspacePaneHandle? pane = page.Surface.DetachPane(partitionId);
        if (pane is null)
            return;
        RemovePanePermanently(pane);
        SaveWorkspace();
    }

    private void RemovePanePermanently(WorkspacePaneHandle pane)
    {
        if (_floatingPaneWindows.TryGetValue(pane.Id, out DetachedChartWindow? floating))
        {
            _floatingPaneClosingForAttach.Add(pane.Id);
            floating.ReleaseHostedContent();
            _floatingPaneWindows.Remove(pane.Id);
            _detachedChartWindows.Remove(floating);
            floating.Close();
        }

        RemoveChartContext(pane.Id);
        RemoveIndicatorWorkspaceContext(pane.Id);
        _workspacePaneRegistry.Remove(pane.Id);
        if (_activePricePaneId == pane.Id)
        {
            _activePricePaneId = _workspacePaneRegistry.Values
                .Where(item => item.Kind == WorkspacePaneKind.PriceChart)
                .Select(item => item.Id)
                .OrderBy(id => id)
                .FirstOrDefault(1);
        }
        if (ReferenceEquals(pane.Content, MainChartPaneRoot))
        {
            _primaryChartContentAssigned = false;
            RemoveFromVisualParent(MainChartPaneRoot);
        }
    }

    private void OpenFloatingPane(WorkspacePaneHandle pane, WorkspacePanePreference? restore = null)
    {
        if (_floatingPaneWindows.TryGetValue(pane.Id, out DetachedChartWindow? existing))
        {
            existing.BringToFront();
            return;
        }

        RemoveFromVisualParent(pane.Content);
        var window = new DetachedChartWindow(pane.Id);
        window.SetHostedContent(pane.Content, pane.Title);
        window.AttachTargetsProvider = BuildAttachTargets;
        window.AttachRequested += (_, request) =>
        {
            if (_workspacePages.TryGetValue(request.WorkspaceId, out WorkspacePageRuntime? page))
            {
                (int? sourceWorkspace, int? sourcePartition) = FindPaneLocation(pane.Id);
                AttachPaneToTarget(page, request.PartitionId, pane, sourceWorkspace, sourcePartition);
            }
        };
        window.MakeWorkspaceRequested += (_, layout) => ConvertFloatingPaneToWorkspace(pane.Id, layout);
        window.WindowGeometryChanged += (_, _) => SaveWorkspace();
        window.WindowSelected += (_, _) =>
        {
            _activeDetachedChartWindow = window;
            ActivateWorkspacePane(pane.Id);
            RefreshWorkspaceTabs();
        };
        window.Closed += (_, _) =>
        {
            _detachedChartWindows.Remove(window);
            _floatingPaneWindows.Remove(pane.Id);
            if (_floatingPaneClosingForAttach.Remove(pane.Id) || _isClosing)
                return;
            window.ReleaseHostedContent();
            RemoveChartContext(pane.Id);
            RemoveIndicatorWorkspaceContext(pane.Id);
            _workspacePaneRegistry.Remove(pane.Id);
        if (_activePricePaneId == pane.Id)
        {
            _activePricePaneId = _workspacePaneRegistry.Values
                .Where(item => item.Kind == WorkspacePaneKind.PriceChart)
                .Select(item => item.Id)
                .OrderBy(id => id)
                .FirstOrDefault(1);
        }
            if (ReferenceEquals(pane.Content, MainChartPaneRoot))
                _primaryChartContentAssigned = false;
            SaveWorkspace();
            RefreshWorkspaceTabs();
        };

        if (restore is not null)
            RestoreFloatingWindowBounds(window, restore);
        else
            PositionDetachedWindow(window);

        _floatingPaneWindows[pane.Id] = window;
        if (!_detachedChartWindows.Contains(window))
            _detachedChartWindows.Add(window);
        window.Show();
        window.BringToFront();
        if (pane.Kind == WorkspacePaneKind.PriceChart &&
            _chartContexts.TryGetValue(pane.Id, out ChartRuntimeContext? floatingContext))
        {
            RefreshIndicatorWorkspaceSourceLabels(floatingContext);
            RefreshDemoTradeLines();
        }
        SaveWorkspace();
    }

    private static void RestoreFloatingWindowBounds(DetachedChartWindow window, WorkspacePanePreference preference)
    {
        Rect virtualBounds = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        double width = Math.Clamp(preference.WindowWidth, window.MinWidth, Math.Max(window.MinWidth, virtualBounds.Width));
        double height = Math.Clamp(preference.WindowHeight, window.MinHeight, Math.Max(window.MinHeight, virtualBounds.Height));
        double left = double.IsFinite(preference.WindowLeft) ? preference.WindowLeft : virtualBounds.Left + 60;
        double top = double.IsFinite(preference.WindowTop) ? preference.WindowTop : virtualBounds.Top + 60;
        window.Width = width;
        window.Height = height;
        window.Left = Math.Clamp(left, virtualBounds.Left + 5, Math.Max(virtualBounds.Left + 5, virtualBounds.Right - width - 5));
        window.Top = Math.Clamp(top, virtualBounds.Top + 5, Math.Max(virtualBounds.Top + 5, virtualBounds.Bottom - height - 5));
        if (preference.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    private IReadOnlyList<WorkspaceAttachTarget> BuildAttachTargets()
    {
        var targets = new List<WorkspaceAttachTarget>();
        foreach (WorkspacePageRuntime page in _workspacePages.Values.OrderBy(item => item.Id))
        {
            for (int partition = 1; partition <= page.Surface.LayoutCount; partition++)
            {
                WorkspacePaneHandle? occupant = page.Surface.GetPane(partition);
                targets.Add(new WorkspaceAttachTarget(
                    page.Id,
                    partition,
                    occupant is null,
                    occupant?.Title ?? string.Empty));
            }
        }
        return targets;
    }

    private void ConvertFloatingPaneToWorkspace(int paneId, int layout)
    {
        if (!_workspacePaneRegistry.TryGetValue(paneId, out WorkspacePaneHandle? pane) ||
            !_floatingPaneWindows.TryGetValue(paneId, out DetachedChartWindow? sourceWindow))
            return;

        Rect sourceBounds = sourceWindow.WindowState == WindowState.Normal
            ? new Rect(sourceWindow.Left, sourceWindow.Top, sourceWindow.Width, sourceWindow.Height)
            : sourceWindow.RestoreBounds;
        bool maximized = sourceWindow.WindowState == WindowState.Maximized;

        WorkspacePaneHandle incoming = TakePaneFromCurrentLocation(paneId) ?? pane;
        int workspaceId = AllocateLowestWorkspaceId();
        WorkspacePageRuntime page = CreateWorkspacePage(workspaceId, layout);
        page.Surface.AttachPane(1, incoming);
        page.IsDetached = true;
        OpenDetachedWorkspaceWindow(page, new WorkspacePagePreference
        {
            WorkspaceId = workspaceId,
            LayoutCount = layout,
            IsDetached = true,
            WindowLeft = sourceBounds.Left,
            WindowTop = sourceBounds.Top,
            WindowWidth = sourceBounds.Width,
            WindowHeight = sourceBounds.Height,
            WindowMaximized = maximized
        });
        _activeWorkspaceId = workspaceId;
        ShowEmptyWorkspaceBackground($"Workspace {workspaceId} is detached");
        RefreshWorkspaceTabs();
        SaveWorkspace();
    }

    private void DetachWorkspace(int workspaceId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;
        if (page.IsDetached)
        {
            page.Window?.BringToFront();
            return;
        }

        if (ReferenceEquals(WorkspacePageHost.Content, page.Surface))
            WorkspacePageHost.Content = null;
        page.IsDetached = true;
        page.IsMinimized = false;
        page.Surface.ShowEmbeddedWindowControls = false;
        OpenDetachedWorkspaceWindow(page, null);
        page.Window?.BeginDragFromCurrentPointer();

        WorkspacePageRuntime? next = _workspacePages.Values
            .Where(item => !item.IsDetached && item.Id != workspaceId)
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (next is not null)
            SwitchToWorkspace(next.Id, bringDetachedToFront: false);
        else
        {
            _activeWorkspaceId = workspaceId;
            ShowEmptyWorkspaceBackground($"Workspace {workspaceId} is detached");
        }
        RefreshWorkspaceTabs();
        SaveWorkspace();
    }

    private void OpenDetachedWorkspaceWindow(WorkspacePageRuntime page, WorkspacePagePreference? restore)
    {
        if (page.Window is not null)
        {
            page.Window.BringToFront();
            return;
        }

        RemoveFromVisualParent(page.Surface);
        var window = new DetachedWorkspaceWindow(page.Id, page.Surface);
        page.Window = window;
        page.IsDetached = true;
        page.IsMinimized = false;
        page.Surface.ShowEmbeddedWindowControls = false;
        window.AttachToTickLabRequested += (_, _) => AttachWorkspaceToTickLab(page.Id);
        window.CloseWorkspaceRequested += (_, _) => RequestCloseWorkspace(page.Id);
        window.WindowGeometryChanged += (_, _) => SaveWorkspace();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(page.Window, window))
                page.Window = null;
            RefreshWorkspaceTabs();
        };

        if (restore is not null)
            RestoreWorkspaceWindowBounds(window, restore);
        else
            PositionDetachedWindow(window);

        window.Show();
        window.BringToFront();
    }

    private static void RestoreWorkspaceWindowBounds(DetachedWorkspaceWindow window, WorkspacePagePreference preference)
    {
        Rect virtualBounds = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        double width = Math.Clamp(preference.WindowWidth, window.MinWidth, Math.Max(window.MinWidth, virtualBounds.Width));
        double height = Math.Clamp(preference.WindowHeight, window.MinHeight, Math.Max(window.MinHeight, virtualBounds.Height));
        double left = double.IsFinite(preference.WindowLeft) ? preference.WindowLeft : virtualBounds.Left + 70;
        double top = double.IsFinite(preference.WindowTop) ? preference.WindowTop : virtualBounds.Top + 70;
        window.Width = width;
        window.Height = height;
        window.Left = Math.Clamp(left, virtualBounds.Left + 5, Math.Max(virtualBounds.Left + 5, virtualBounds.Right - width - 5));
        window.Top = Math.Clamp(top, virtualBounds.Top + 5, Math.Max(virtualBounds.Top + 5, virtualBounds.Bottom - height - 5));
        if (preference.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    private void AttachWorkspaceToTickLab(int workspaceId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page) || !page.IsDetached)
            return;

        DetachedWorkspaceWindow? window = page.Window;
        WorkspaceSurfaceControl surface = window?.ReleaseSurface() ?? page.Surface;
        page.Window = null;
        page.IsDetached = false;
        page.IsMinimized = false;
        surface.ShowEmbeddedWindowControls = true;
        window?.CloseWithoutPrompt();
        RemoveFromVisualParent(surface);
        SwitchToWorkspace(workspaceId, bringDetachedToFront: false);
        SaveWorkspace();
    }

    private void RequestCloseWorkspace(int workspaceId)
    {
        if (!_workspacePages.TryGetValue(workspaceId, out WorkspacePageRuntime? page))
            return;

        WorkspaceCloseDecision decision = WorkspaceDecisionDialog.ShowWorkspaceClose(
            this,
            workspaceId,
            page.Surface.Panes.Count);
        if (decision == WorkspaceCloseDecision.Cancel)
            return;

        WorkspacePaneHandle[] panes = page.Surface.Panes.Values.OrderBy(item => item.Id).ToArray();
        foreach (WorkspacePaneHandle pane in panes)
        {
            page.Surface.DetachPaneById(pane.Id);
            if (decision == WorkspaceCloseDecision.DetachItems)
                OpenFloatingPane(pane);
            else
                RemovePanePermanently(pane);
        }

        if (ReferenceEquals(WorkspacePageHost.Content, page.Surface))
            WorkspacePageHost.Content = null;
        DetachedWorkspaceWindow? window = page.Window;
        page.Window = null;
        window?.ReleaseSurface();
        window?.CloseWithoutPrompt();
        _workspacePages.Remove(workspaceId);

        WorkspacePageRuntime? next = _workspacePages.Values
            .Where(item => !item.IsDetached)
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (next is not null)
            SwitchToWorkspace(next.Id, bringDetachedToFront: false);
        else
        {
            _activeWorkspaceId = _workspacePages.Keys.OrderBy(id => id).FirstOrDefault();
            ShowEmptyWorkspaceBackground();
        }
        RefreshWorkspaceTabs();
        SaveWorkspace();
    }

    private void SyncWorkspaceChartPanes()
    {
        if (!_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context))
            return;
        context.Symbol = _requestedSymbol;
        context.Timeframe = _activeTimeframe;
        if (context.Host is ChartPaneControl chartPane)
        {
            chartPane.Symbol = _requestedSymbol;
            chartPane.Timeframe = _activeTimeframe.DisplayText;
        }
        UpdateWorkspacePaneIdentity(context.PaneId, context.Symbol, context.Timeframe.DisplayText);
    }


    private bool AttachIndicatorPaneToSelectedPartition()
    {
        if (_activeWorkspaceId <= 0 ||
            !_workspacePages.TryGetValue(_activeWorkspaceId, out WorkspacePageRuntime? page) ||
            page.Surface.SelectedPartitionId is not int partition)
            return false;

        WorkspacePaneHandle? pane = _workspacePaneRegistry.Values.FirstOrDefault(item =>
            item.Kind == WorkspacePaneKind.Indicator && ReferenceEquals(item.Content, _indicatorPaneStack));
        if (pane is null)
        {
            int paneId = AllocateLowestPaneId();
            pane = new WorkspacePaneHandle(paneId, WorkspacePaneKind.Indicator, "Indicators", _indicatorPaneStack);
            _workspacePaneRegistry[paneId] = pane;
        }

        DockedToolContent.Content = null;
        ToolPartitionBorder.Visibility = Visibility.Collapsed;
        ToolPartitionSplitter.Visibility = Visibility.Collapsed;
        ToolPartitionRow.Height = new GridLength(0);
        ToolSplitterRow.Height = new GridLength(0);

        (int? sourceWorkspace, int? sourcePartition) = FindPaneLocation(pane.Id);
        AttachPaneToTarget(page, partition, pane, sourceWorkspace, sourcePartition);
        return true;
    }

    private void DrawingBrushButton_Click(object sender, RoutedEventArgs e)
    {
        _brushPaletteOpen = true;
        _openDrawingCategory = null;
        DrawingCategoryPaletteTitle.Text = "Brush, Pen & Highlighter";
        DrawingCategoryPaletteIconHost.Child = DrawingToolIconFactory.CreateToolIcon(
            DrawingToolCatalog.Find("brush")!,
            20,
            new SolidColorBrush(Color.FromRgb(112, 183, 255)));
        DrawingCategoryPaletteSearchBox.Text = string.Empty;
        DrawingPaletteColumn.Width = new GridLength(Math.Clamp(_drawingPaletteExpandedWidth, 210.0, 420.0));
        DrawingPaletteSplitterColumn.Width = new GridLength(5.0);
        DrawingCategoryPaletteBorder.Visibility = Visibility.Visible;
        DrawingPaletteSplitter.Visibility = Visibility.Visible;
        RebuildBrushPalette();
    }

    private void RebuildBrushPalette()
    {
        DrawingCategoryPaletteRowsPanel.Children.Clear();
        string query = DrawingCategoryPaletteSearchBox.Text.Trim();
        foreach (string id in new[] { "pen", "brush", "highlighter" })
        {
            DrawingToolDefinition? tool = DrawingToolCatalog.Find(id);
            if (tool is null)
                continue;
            if (!string.IsNullOrWhiteSpace(query) &&
                !tool.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !tool.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            DrawingCategoryPaletteRowsPanel.Children.Add(CreateDrawingPaletteRow(tool));
        }
    }

    private void ClearWorkspacePartitionSelection()
    {
        foreach (WorkspacePageRuntime page in _workspacePages.Values)
            page.Surface.ClearSelection();
    }


    private WorkspacePanePreference CapturePanePreference(
        WorkspacePaneHandle pane,
        int partitionId,
        bool isFloating,
        Rect bounds,
        bool maximized)
    {
        string symbol = _requestedSymbol;
        string timeframe = _activeTimeframe.DisplayText;
        ChartViewportState viewport = _preferences.Viewport;
        string drawingDocument = string.Empty;
        ChartSettings chartSettings = _preferences.Chart;
        IReadOnlyList<BuiltInIndicatorInstance> builtInIndicators = Array.Empty<BuiltInIndicatorInstance>();
        IReadOnlyList<AppliedTickScriptIndicatorPreference> tickScriptIndicators = Array.Empty<AppliedTickScriptIndicatorPreference>();
        bool syncIndicatorsWithPriceChart = true;
        int connectedPricePaneId = 0;

        if (ReferenceEquals(pane.Content, MainChartPaneRoot))
        {
            ChartRuntimeContext primary = GetChartContext(pane.Id);
            symbol = primary.Symbol;
            timeframe = primary.Timeframe.DisplayText;
            viewport = primary.Chart.CaptureViewport();
            drawingDocument = primary.Chart.ExportDrawingWorkspaceJson();
            chartSettings = primary.Settings;
            builtInIndicators = CloneBuiltInIndicators(primary.BuiltInIndicators);
            tickScriptIndicators = CaptureTickScriptIndicatorPreferences(primary);
            syncIndicatorsWithPriceChart = primary.SyncIndicatorsWithPriceChart;
        }
        else if (pane.Content is ChartPaneControl chartPane)
        {
            symbol = chartPane.Symbol;
            timeframe = chartPane.Timeframe;
            viewport = chartPane.Chart.CaptureViewport();
            drawingDocument = chartPane.Chart.ExportDrawingWorkspaceJson();
            ChartRuntimeContext context = GetChartContext(pane.Id);
            chartSettings = context.Settings;
            builtInIndicators = CloneBuiltInIndicators(context.BuiltInIndicators);
            tickScriptIndicators = CaptureTickScriptIndicatorPreferences(context);
            syncIndicatorsWithPriceChart = context.SyncIndicatorsWithPriceChart;
        }
        else if (_indicatorWorkspaceContexts.TryGetValue(pane.Id, out IndicatorWorkspaceRuntimeContext? indicatorContext))
        {
            builtInIndicators = CloneBuiltInIndicators(indicatorContext.BuiltInIndicators);
            tickScriptIndicators = CaptureIndicatorWorkspaceTickScripts(indicatorContext);
            syncIndicatorsWithPriceChart = indicatorContext.SyncWithPriceChart;
            connectedPricePaneId = indicatorContext.ConnectedPricePaneId ?? 0;
            viewport = indicatorContext.Stack.CaptureViewportState();
            if (indicatorContext.ConnectedPricePaneId is int sourcePaneId &&
                _chartContexts.TryGetValue(sourcePaneId, out ChartRuntimeContext? sourceContext))
            {
                symbol = sourceContext.Symbol;
                timeframe = sourceContext.Timeframe.DisplayText;
                chartSettings = sourceContext.Settings;
            }
        }

        return new WorkspacePanePreference
        {
            PaneId = pane.Id,
            Kind = pane.Kind.ToString(),
            Title = pane.Title,
            PartitionId = partitionId,
            IsFloating = isFloating,
            Symbol = symbol,
            Timeframe = timeframe,
            Viewport = viewport,
            DrawingDocument = drawingDocument,
            ChartSettings = chartSettings,
            BuiltInIndicators = builtInIndicators,
            TickScriptIndicators = tickScriptIndicators,
            SyncIndicatorsWithPriceChart = syncIndicatorsWithPriceChart,
            ConnectedPricePaneId = connectedPricePaneId,
            WindowLeft = bounds.IsEmpty ? double.NaN : bounds.Left,
            WindowTop = bounds.IsEmpty ? double.NaN : bounds.Top,
            WindowWidth = bounds.IsEmpty ? 980 : bounds.Width,
            WindowHeight = bounds.IsEmpty ? 620 : bounds.Height,
            WindowMaximized = maximized
        };
    }

    private IReadOnlyList<WorkspacePagePreference> CaptureWorkspacePagePreferences()
    {
        var result = new List<WorkspacePagePreference>();
        foreach (WorkspacePageRuntime page in _workspacePages.Values.OrderBy(item => item.Id))
        {
            Rect bounds = page.Window is null
                ? Rect.Empty
                : page.Window.WindowState == WindowState.Normal
                    ? new Rect(page.Window.Left, page.Window.Top, page.Window.Width, page.Window.Height)
                    : page.Window.RestoreBounds;

            var panes = new List<WorkspacePanePreference>();
            for (int partition = 1; partition <= page.Surface.LayoutCount; partition++)
            {
                WorkspacePaneHandle? pane = page.Surface.GetPane(partition);
                if (pane is null)
                    continue;
                panes.Add(CapturePanePreference(
                    pane,
                    partition,
                    isFloating: false,
                    bounds: Rect.Empty,
                    maximized: false));
            }

            result.Add(new WorkspacePagePreference
            {
                WorkspaceId = page.Id,
                LayoutCount = page.Surface.LayoutCount,
                IsDetached = page.IsDetached,
                IsMinimized = page.IsMinimized,
                WindowLeft = bounds.IsEmpty ? double.NaN : bounds.Left,
                WindowTop = bounds.IsEmpty ? double.NaN : bounds.Top,
                WindowWidth = bounds.IsEmpty ? 1180 : bounds.Width,
                WindowHeight = bounds.IsEmpty ? 760 : bounds.Height,
                WindowMaximized = page.Window?.WindowState == WindowState.Maximized,
                Panes = panes
            });
        }
        return result;
    }

    private IReadOnlyList<WorkspacePanePreference> CaptureFloatingPanePreferences()
    {
        var result = new List<WorkspacePanePreference>();
        foreach ((int paneId, DetachedChartWindow window) in _floatingPaneWindows.OrderBy(item => item.Key))
        {
            if (!_workspacePaneRegistry.TryGetValue(paneId, out WorkspacePaneHandle? pane))
                continue;
            Rect bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;
            result.Add(CapturePanePreference(
                pane,
                partitionId: 0,
                isFloating: true,
                bounds: bounds,
                maximized: window.WindowState == WindowState.Maximized));
        }
        return result;
    }

    private void UpdateWorkspacePaneIdentity(int paneId, string symbol, string timeframe)
    {
        foreach (WorkspacePageRuntime page in _workspacePages.Values)
            page.Surface.UpdatePaneIdentity(paneId, symbol, timeframe);

        if (_workspacePaneRegistry.TryGetValue(paneId, out WorkspacePaneHandle? pane) &&
            pane.Kind == WorkspacePaneKind.PriceChart)
            pane.Title = $"Chart {paneId} · {(string.IsNullOrWhiteSpace(symbol) ? "Price Chart" : symbol)} · {timeframe}";

        if (_floatingPaneWindows.TryGetValue(paneId, out DetachedChartWindow? window))
            window.UpdateHostedIdentity(symbol, timeframe);
        RefreshWorkspaceTabs();
    }

    private void CloseWorkspaceWindowsForApplicationExit()
    {
        foreach (WorkspacePageRuntime page in _workspacePages.Values)
        {
            if (page.Window is not null)
            {
                page.Window.CloseWithoutPrompt();
                page.Window = null;
            }
        }
    }

    private static void RemoveFromVisualParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }
}
