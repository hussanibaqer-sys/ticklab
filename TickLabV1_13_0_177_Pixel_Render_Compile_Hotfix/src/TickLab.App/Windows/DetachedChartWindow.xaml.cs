using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Core.Market;
using TickLab.Core.Settings;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop.Windows;

public sealed record WorkspaceAttachTarget(int WorkspaceId, int PartitionId, bool IsEmpty, string OccupantTitle);
public sealed record WorkspaceAttachRequest(int WorkspaceId, int PartitionId);

public partial class DetachedChartWindow : Window
{
    private FrameworkElement? _hostedElement;
    private CandleChartControl? _hostedChart;
    private Point _dragStart;
    private bool _dragBadgePressed;
    private UIElement? _dragSource;

    public DetachedChartWindow(int chartNumber)
    {
        InitializeComponent();
        ChartNumber = chartNumber;
        ChartNumberText.Text = chartNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        };
        LocationChanged += (_, _) => WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        DetachedChart.HostContextMenuItemsProvider = BuildHostContextMenuItems;
        UpdateMaximizeGlyph();
    }

    public int ChartNumber { get; }
    public Func<IReadOnlyList<WorkspaceAttachTarget>>? AttachTargetsProvider { get; set; }

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(ChartSymbolText.Text)
            ? $"Chart {ChartNumber}"
            : ChartSymbolText.Text;

    public event EventHandler? WindowSelected;
    public event EventHandler<WorkspaceAttachRequest>? AttachRequested;
    public event EventHandler<int>? MakeWorkspaceRequested;
    public event EventHandler? WindowGeometryChanged;

    public void UpdateChart(
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<ChartTimelineGap> timelineGaps,
        IReadOnlyList<CandleMarker> markers,
        IReadOnlyList<DemoTradeLineOverlay> demoTradeLines,
        ChartSettings settings,
        int serverUtcOffsetMinutes,
        long? nativeBoundaryUnix,
        string nativeBoundaryLabel)
    {
        if (_hostedElement is not null)
            return;

        ChartSymbolText.Text = string.IsNullOrWhiteSpace(symbol) ? "Price Chart" : symbol;
        ChartTimeframeText.Text = timeframe;
        Title = $"TickLab — {ChartNumber} · {ChartSymbolText.Text} · {timeframe}";
        DetachedChart.Settings = settings;
        DetachedChart.ServerUtcOffsetMinutes = serverUtcOffsetMinutes;
        DetachedChart.NativeHistoryBoundaryUnix = nativeBoundaryUnix;
        DetachedChart.HistoryBoundaryLabel = nativeBoundaryLabel;
        DetachedChart.TimelineGaps = timelineGaps;
        DetachedChart.Markers = markers;
        DetachedChart.DemoTradeLines = demoTradeLines;
        DetachedChart.ReplaceDataKeepingViewport(candles);
    }


    public void UpdateDemoTradeLines(IReadOnlyList<DemoTradeLineOverlay> demoTradeLines)
    {
        if (_hostedElement is not null)
            return;
        DetachedChart.DemoTradeLines = demoTradeLines ?? Array.Empty<DemoTradeLineOverlay>();
    }

    public void SetHostedContent(FrameworkElement content, string title)
    {
        RemoveFromVisualParent(content);
        _hostedElement = content;
        _hostedChart = FindChartControl(content);
        if (_hostedChart is not null)
            _hostedChart.HostContextMenuItemsProvider = BuildHostContextMenuItems;
        HostedContent.Content = content;
        HostedContent.Visibility = Visibility.Visible;
        DetachedChart.Visibility = Visibility.Collapsed;
        ChartSymbolText.Text = string.IsNullOrWhiteSpace(title) ? "Tool" : title;
        ChartTimeframeText.Text = string.Empty;
        Title = $"TickLab — {ChartNumber} · {ChartSymbolText.Text}";
    }

    public void UpdateHostedIdentity(string symbol, string timeframe)
    {
        ChartSymbolText.Text = string.IsNullOrWhiteSpace(symbol) ? "Price Chart" : symbol;
        ChartTimeframeText.Text = timeframe ?? string.Empty;
        Title = $"TickLab — {ChartNumber} · {ChartSymbolText.Text} · {ChartTimeframeText.Text}";
    }

    public FrameworkElement? ReleaseHostedContent()
    {
        FrameworkElement? content = _hostedElement;
        if (_hostedChart is not null)
            _hostedChart.HostContextMenuItemsProvider = null;
        HostedContent.Content = null;
        HostedContent.Visibility = Visibility.Collapsed;
        DetachedChart.Visibility = Visibility.Visible;
        _hostedElement = null;
        _hostedChart = null;
        return content;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        WindowSelected?.Invoke(this, EventArgs.Empty);
    }

    private void WindowDragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideDragHandle(source))
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void WindowDragBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenWindowContextMenu(WindowDragBar);
        e.Handled = true;
    }

    private void WindowMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenWindowContextMenu(sender as FrameworkElement ?? WindowDragBar);

    private void OpenWindowContextMenu(FrameworkElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget };
        foreach (MenuItem item in BuildHostContextMenuItems())
            menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private IReadOnlyList<MenuItem> BuildHostContextMenuItems()
    {
        var items = new List<MenuItem>
        {
            BuildAttachMenu(),
            new MenuItem
            {
                Header = "Detach from Workspace — already detached",
                IsEnabled = false,
                IsCheckable = true,
                IsChecked = true
            },
            BuildMakeWorkspaceMenu()
        };
        return items;
    }

    private MenuItem BuildAttachMenu()
    {
        var attach = new MenuItem { Header = "Attach to Workspace" };
        IReadOnlyList<WorkspaceAttachTarget> targets =
            AttachTargetsProvider?.Invoke() ?? Array.Empty<WorkspaceAttachTarget>();
        foreach (IGrouping<int, WorkspaceAttachTarget> workspace in targets
                     .OrderBy(item => item.WorkspaceId)
                     .ThenBy(item => item.PartitionId)
                     .GroupBy(item => item.WorkspaceId))
        {
            var workspaceItem = new MenuItem { Header = $"Workspace {workspace.Key}" };
            foreach (WorkspaceAttachTarget target in workspace)
            {
                var partition = new MenuItem
                {
                    Header = target.IsEmpty
                        ? $"Partition {target.PartitionId} — Empty"
                        : $"Partition {target.PartitionId} — {target.OccupantTitle}"
                };
                partition.Click += (_, _) => AttachRequested?.Invoke(
                    this,
                    new WorkspaceAttachRequest(target.WorkspaceId, target.PartitionId));
                workspaceItem.Items.Add(partition);
            }
            attach.Items.Add(workspaceItem);
        }
        if (attach.Items.Count == 0)
            attach.Items.Add(new MenuItem { Header = "No workspace partitions available", IsEnabled = false });
        return attach;
    }

    private MenuItem BuildMakeWorkspaceMenu()
    {
        var makeWorkspace = new MenuItem { Header = "Make Workspace" };
        foreach (int layout in new[] { 1, 2, 3, 4, 6 })
        {
            int captured = layout;
            var item = new MenuItem { Header = layout == 1 ? "1 — Single" : $"{layout} partitions" };
            item.Click += (_, _) => MakeWorkspaceRequested?.Invoke(this, captured);
            makeWorkspace.Items.Add(item);
        }
        return makeWorkspace;
    }

    private void ChartNumberBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragBadgePressed = true;
        _dragStart = e.GetPosition(this);
        _dragSource = sender as UIElement ?? ChartNumberBadge;
        _dragSource.CaptureMouse();
        e.Handled = true;
    }

    private void ChartNumberBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragBadgePressed || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragBadgePressed = false;
        UIElement dragSource = _dragSource ?? ChartNumberBadge;
        dragSource.ReleaseMouseCapture();
        _dragSource = null;
        var data = new DataObject(WorkspaceSurfaceControl.PaneDragFormat, ChartNumber);
        DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
        e.Handled = true;
    }

    private void ChartNumberBadge_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragBadgePressed = false;
        (_dragSource ?? ChartNumberBadge).ReleaseMouseCapture();
        _dragSource = null;
        e.Handled = true;
    }

    private bool IsInsideDragHandle(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ChartNumberBadge) || ReferenceEquals(current, ChartDragGrip))
                return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static CandleChartControl? FindChartControl(DependencyObject root)
    {
        if (root is CandleChartControl chart)
            return chart;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            CandleChartControl? found = FindChartControl(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton is null)
            return;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized
            ? "Restore this chart"
            : "Maximize this chart";
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
