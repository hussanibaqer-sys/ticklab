using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TickLab.Desktop.Controls;

public enum WorkspacePaneKind
{
    PriceChart,
    Indicator,
    ExpertAdvisor,
    Tool,
    Other
}

public sealed class WorkspacePaneHandle
{
    public WorkspacePaneHandle(int id, WorkspacePaneKind kind, string title, FrameworkElement content)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Content = content;
    }

    public int Id { get; }
    public WorkspacePaneKind Kind { get; }
    public string Title { get; set; }
    public FrameworkElement Content { get; }
}

public sealed record WorkspacePaneDropRequest(
    int WorkspaceId,
    int PartitionId,
    int PaneId);

public sealed record WorkspacePaneRequest(
    int WorkspaceId,
    int PartitionId,
    WorkspacePaneHandle Pane);

public sealed class WorkspaceSurfaceControl : Grid
{
    private sealed class PartitionSlot
    {
        public required int Id { get; init; }
        public required Border Border { get; init; }
        public required Grid Root { get; init; }
        public required TextBlock EmptyNumber { get; init; }
        public required ContentControl ContentHost { get; init; }
        public required Border SelectionFrame { get; init; }
        public required Border DropFrame { get; init; }
        public TextBlock? IdentityText { get; set; }
        public WorkspacePaneHandle? Pane { get; set; }
    }

    public const string PaneDragFormat = "TickLab.WorkspacePaneId";

    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(5, 5, 5));
    private static readonly Brush SplitBrush =
        new SolidColorBrush(Color.FromRgb(46, 46, 46));
    private static readonly Brush MutedNumberBrush =
        new SolidColorBrush(Color.FromRgb(92, 92, 92));
    private static readonly Brush YellowBrush =
        new SolidColorBrush(Color.FromRgb(245, 181, 68));

    private readonly Dictionary<int, PartitionSlot> _slots = new();
    private readonly Dictionary<int, WorkspacePaneHandle> _panes = new();
    private readonly Grid _partitionGrid = new();
    private readonly Border _workspaceHandleHotspot;
    private readonly Border _embeddedWorkspaceControls;
    private Point _workspaceHandleStart;
    private bool _workspaceHandlePressed;
    private int? _maximizedPartitionId;

    public WorkspaceSurfaceControl(int workspaceId, int layoutCount)
    {
        WorkspaceId = workspaceId;
        Focusable = true;
        Background = BackgroundBrush;
        ClipToBounds = true;

        Children.Add(_partitionGrid);

        _workspaceHandleHotspot = new Border
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 128, 154)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Opacity = 0,
            Cursor = Cursors.SizeAll,
            ToolTip = $"Drag Workspace {workspaceId} to another display",
            Margin = new Thickness(2),
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(162, 178, 199)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Panel.SetZIndex(_workspaceHandleHotspot, 1000);
        Children.Add(_workspaceHandleHotspot);

        _embeddedWorkspaceControls = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3),
            Padding = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(228, 8, 15, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 58, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Opacity = 0,
            ToolTip = $"Workspace {workspaceId} controls"
        };
        var workspaceControlPanel = new StackPanel { Orientation = Orientation.Horizontal };
        Button minimizeWorkspace = CreatePaneButton("—", $"Minimize Workspace {workspaceId}");
        Button maximizeWorkspace = CreatePaneButton("□", $"Maximize Workspace {workspaceId} inside TickLab");
        Button closeWorkspace = CreatePaneButton("×", $"Close Workspace {workspaceId}");
        minimizeWorkspace.Click += (_, _) => WorkspaceMinimizeRequested?.Invoke(this, EventArgs.Empty);
        maximizeWorkspace.Click += (_, _) => WorkspaceMaximizeRequested?.Invoke(this, EventArgs.Empty);
        closeWorkspace.Click += (_, _) => WorkspaceCloseRequested?.Invoke(this, EventArgs.Empty);
        workspaceControlPanel.Children.Add(minimizeWorkspace);
        workspaceControlPanel.Children.Add(maximizeWorkspace);
        workspaceControlPanel.Children.Add(closeWorkspace);
        _embeddedWorkspaceControls.Child = workspaceControlPanel;
        _embeddedWorkspaceControls.MouseEnter += (_, _) => _embeddedWorkspaceControls.Opacity = 1;
        _embeddedWorkspaceControls.MouseLeave += (_, _) => _embeddedWorkspaceControls.Opacity = 0;
        Panel.SetZIndex(_embeddedWorkspaceControls, 990);
        Children.Add(_embeddedWorkspaceControls);

        MouseMove += WorkspaceSurfaceControl_MouseMove;
        MouseLeave += (_, _) =>
        {
            if (!_workspaceHandlePressed)
                _workspaceHandleHotspot.Opacity = 0;
            _embeddedWorkspaceControls.Opacity = 0;
        };
        _workspaceHandleHotspot.MouseEnter += (_, _) => _workspaceHandleHotspot.Opacity = 0.92;
        _workspaceHandleHotspot.MouseLeave += (_, _) =>
        {
            if (!_workspaceHandlePressed)
                _workspaceHandleHotspot.Opacity = 0;
        };
        _workspaceHandleHotspot.PreviewMouseLeftButtonDown += WorkspaceHandle_PreviewMouseLeftButtonDown;
        _workspaceHandleHotspot.PreviewMouseMove += WorkspaceHandle_PreviewMouseMove;
        _workspaceHandleHotspot.PreviewMouseLeftButtonUp += WorkspaceHandle_PreviewMouseLeftButtonUp;

        SetLayout(layoutCount);
    }

    public int WorkspaceId { get; }
    public int LayoutCount { get; private set; }
    public int? SelectedPartitionId { get; private set; }
    public int? MaximizedPartitionId => _maximizedPartitionId;
    public IReadOnlyDictionary<int, WorkspacePaneHandle> Panes => _panes;
    public bool ShowEmbeddedWindowControls
    {
        get => _embeddedWorkspaceControls.Visibility == Visibility.Visible;
        set => _embeddedWorkspaceControls.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public event EventHandler<int>? PartitionSelected;
    public event EventHandler<int>? EmptyPartitionContextRequested;
    public event EventHandler<WorkspacePaneDropRequest>? PaneDropped;
    public event EventHandler<WorkspacePaneRequest>? PaneDetachRequested;
    public event EventHandler<WorkspacePaneRequest>? PaneCloseRequested;
    public event EventHandler<WorkspacePaneRequest>? PaneActivated;
    public event EventHandler? WorkspaceDetachRequested;
    public event EventHandler? WorkspaceMinimizeRequested;
    public event EventHandler? WorkspaceMaximizeRequested;
    public event EventHandler? WorkspaceCloseRequested;
    public event EventHandler? WorkspaceChanged;

    public IReadOnlyList<WorkspacePaneHandle> SetLayout(int layoutCount)
    {
        layoutCount = layoutCount is 1 or 2 or 3 or 4 or 6 ? layoutCount : 1;
        WorkspacePaneHandle[] existing = _slots.Values
            .Where(slot => slot.Pane is not null)
            .OrderBy(slot => slot.Id)
            .Select(slot => slot.Pane!)
            .ToArray();

        foreach (PartitionSlot slot in _slots.Values)
            slot.ContentHost.Content = null;

        _slots.Clear();
        _panes.Clear();
        _partitionGrid.Children.Clear();
        _partitionGrid.RowDefinitions.Clear();
        _partitionGrid.ColumnDefinitions.Clear();
        _maximizedPartitionId = null;
        SelectedPartitionId = null;
        LayoutCount = layoutCount;

        (int rows, int columns) = layoutCount switch
        {
            2 => (1, 2),
            3 => (1, 3),
            4 => (2, 2),
            6 => (2, 3),
            _ => (1, 1)
        };

        for (int row = 0; row < rows; row++)
            _partitionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int column = 0; column < columns; column++)
            _partitionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int index = 0; index < layoutCount; index++)
        {
            int partitionId = index + 1;
            PartitionSlot slot = CreatePartition(partitionId);
            Grid.SetRow(slot.Border, index / columns);
            Grid.SetColumn(slot.Border, index % columns);
            _partitionGrid.Children.Add(slot.Border);
            _slots[partitionId] = slot;
        }

        int keep = Math.Min(existing.Length, layoutCount);
        for (int index = 0; index < keep; index++)
            AttachPane(index + 1, existing[index]);

        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        return existing.Skip(keep).ToArray();
    }

    public bool IsPartitionEmpty(int partitionId) =>
        _slots.TryGetValue(partitionId, out PartitionSlot? slot) && slot.Pane is null;

    public WorkspacePaneHandle? GetPane(int partitionId) =>
        _slots.TryGetValue(partitionId, out PartitionSlot? slot) ? slot.Pane : null;

    public int? FirstEmptyPartition() =>
        _slots.Values.Where(slot => slot.Pane is null).OrderBy(slot => slot.Id).Select(slot => (int?)slot.Id).FirstOrDefault();

    public bool AttachPane(int partitionId, WorkspacePaneHandle pane)
    {
        if (!_slots.TryGetValue(partitionId, out PartitionSlot? slot) || slot.Pane is not null)
            return false;

        RemoveFromVisualParent(pane.Content);
        slot.Pane = pane;
        _panes[pane.Id] = pane;
        slot.ContentHost.Content = CreatePaneFrame(slot, pane);
        slot.EmptyNumber.Visibility = Visibility.Collapsed;
        slot.SelectionFrame.Visibility = Visibility.Collapsed;
        slot.DropFrame.Visibility = Visibility.Collapsed;
        if (SelectedPartitionId == partitionId)
            SelectedPartitionId = null;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public WorkspacePaneHandle? DetachPane(int partitionId)
    {
        if (!_slots.TryGetValue(partitionId, out PartitionSlot? slot) || slot.Pane is null)
            return null;

        WorkspacePaneHandle pane = slot.Pane;
        slot.ContentHost.Content = null;
        slot.Pane = null;
        _panes.Remove(pane.Id);
        slot.EmptyNumber.Visibility = Visibility.Visible;
        if (_maximizedPartitionId == partitionId)
            RestorePartitions();
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        return pane;
    }

    public WorkspacePaneHandle? DetachPaneById(int paneId)
    {
        PartitionSlot? slot = _slots.Values.FirstOrDefault(item => item.Pane?.Id == paneId);
        return slot is null ? null : DetachPane(slot.Id);
    }

    public int? FindPartitionForPane(int paneId) =>
        _slots.Values.FirstOrDefault(slot => slot.Pane?.Id == paneId)?.Id;

    public void UpdatePaneIdentity(int paneId, string symbol, string timeframe)
    {
        PartitionSlot? slot = _slots.Values.FirstOrDefault(item => item.Pane?.Id == paneId);
        if (slot?.Pane is null || slot.IdentityText is null)
            return;
        string identity = paneId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (slot.Pane.Kind == WorkspacePaneKind.PriceChart)
        {
            if (!string.IsNullOrWhiteSpace(symbol)) identity += $"  {symbol}";
            if (!string.IsNullOrWhiteSpace(timeframe)) identity += $"  {timeframe}";
            slot.Pane.Title = $"Chart {paneId} · {(string.IsNullOrWhiteSpace(symbol) ? "Price Chart" : symbol)} · {timeframe}";
        }
        slot.IdentityText.Text = identity;
    }

    public void SelectPartition(int partitionId)
    {
        if (!_slots.TryGetValue(partitionId, out PartitionSlot? selected))
            return;

        SelectedPartitionId = partitionId;
        foreach (PartitionSlot slot in _slots.Values)
            slot.SelectionFrame.Visibility = slot.Id == partitionId
                ? Visibility.Visible
                : Visibility.Collapsed;
        selected.Root.Focus();
        PartitionSelected?.Invoke(this, partitionId);
    }

    public void ClearSelection()
    {
        SelectedPartitionId = null;
        foreach (PartitionSlot slot in _slots.Values)
            slot.SelectionFrame.Visibility = Visibility.Collapsed;
    }

    public void ToggleMaximize(int partitionId)
    {
        if (!_slots.ContainsKey(partitionId))
            return;

        if (_maximizedPartitionId == partitionId)
        {
            RestorePartitions();
            return;
        }

        _maximizedPartitionId = partitionId;
        foreach (PartitionSlot slot in _slots.Values)
            slot.Border.Visibility = slot.Id == partitionId ? Visibility.Visible : Visibility.Collapsed;

        PartitionSlot target = _slots[partitionId];
        Grid.SetRow(target.Border, 0);
        Grid.SetColumn(target.Border, 0);
        Grid.SetRowSpan(target.Border, Math.Max(1, _partitionGrid.RowDefinitions.Count));
        Grid.SetColumnSpan(target.Border, Math.Max(1, _partitionGrid.ColumnDefinitions.Count));
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestorePartitions()
    {
        _maximizedPartitionId = null;
        int columns = LayoutCount switch { 3 or 6 => 3, 2 or 4 => 2, _ => 1 };
        foreach (PartitionSlot slot in _slots.Values)
        {
            int index = slot.Id - 1;
            slot.Border.Visibility = Visibility.Visible;
            Grid.SetRow(slot.Border, index / columns);
            Grid.SetColumn(slot.Border, index % columns);
            Grid.SetRowSpan(slot.Border, 1);
            Grid.SetColumnSpan(slot.Border, 1);
        }
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    private PartitionSlot CreatePartition(int partitionId)
    {
        var root = new Grid
        {
            Background = BackgroundBrush,
            AllowDrop = true,
            Focusable = true,
            Tag = partitionId
        };

        var emptyNumber = new TextBlock
        {
            Text = partitionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = MutedNumberBrush,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.44,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var contentHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var selectionFrame = new Border
        {
            BorderBrush = YellowBrush,
            BorderThickness = new Thickness(1.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var dropFrame = new Border
        {
            BorderBrush = YellowBrush,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(22, 245, 181, 68)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        root.Children.Add(emptyNumber);
        root.Children.Add(contentHost);
        root.Children.Add(selectionFrame);
        root.Children.Add(dropFrame);

        var border = new Border
        {
            Background = BackgroundBrush,
            BorderBrush = SplitBrush,
            BorderThickness = new Thickness(0.6),
            Child = root
        };

        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // A single click on empty workspace space selects it as the next
            // indicator placement target. Occupied chart clicks remain untouched.
            if (contentHost.Content is null && e.ClickCount == 1)
            {
                SelectPartition(partitionId);
                e.Handled = true;
            }
        };
        root.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (contentHost.Content is not null)
                return;
            SelectPartition(partitionId);
            EmptyPartitionContextRequested?.Invoke(this, partitionId);
            e.Handled = true;
        };
        root.DragEnter += (_, e) => UpdateDropHighlight(partitionId, e, true);
        root.DragOver += (_, e) => UpdateDropHighlight(partitionId, e, true);
        root.DragLeave += (_, _) => dropFrame.Visibility = Visibility.Collapsed;
        root.Drop += (_, e) =>
        {
            dropFrame.Visibility = Visibility.Collapsed;
            if (!TryReadPaneId(e.Data, out int paneId))
                return;
            PaneDropped?.Invoke(this, new WorkspacePaneDropRequest(WorkspaceId, partitionId, paneId));
            e.Handled = true;
        };

        return new PartitionSlot
        {
            Id = partitionId,
            Border = border,
            Root = root,
            EmptyNumber = emptyNumber,
            ContentHost = contentHost,
            SelectionFrame = selectionFrame,
            DropFrame = dropFrame
        };
    }

    private UIElement CreatePaneFrame(PartitionSlot slot, WorkspacePaneHandle pane)
    {
        var root = new Grid { Background = BackgroundBrush, ClipToBounds = true };
        root.Children.Add(pane.Content);

        var identityText = new TextBlock
        {
            Text = pane.Kind == WorkspacePaneKind.PriceChart
                ? pane.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{pane.Id}  {pane.Kind}",
            Foreground = new SolidColorBrush(Color.FromRgb(165, 181, 201)),
            FontSize = 9.2,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        slot.IdentityText = identityText;
        var identityBadge = new Border
        {
            MinWidth = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            Padding = new Thickness(5, 0, 5, 0),
            Background = new SolidColorBrush(Color.FromArgb(180, 4, 9, 15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 79, 100, 126)),
            BorderThickness = new Thickness(0.6),
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
            Child = identityText
        };
        Panel.SetZIndex(identityBadge, 450);
        root.Children.Add(identityBadge);

        var controls = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            Padding = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(225, 8, 15, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 58, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Visibility = Visibility.Visible,
            Opacity = 0
        };
        Panel.SetZIndex(controls, 500);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var grip = CreatePaneButton("⠿", $"Drag {pane.Title} to another partition");
        grip.Cursor = Cursors.SizeAll;
        Point gripStart = default;
        bool gripPressed = false;
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            gripPressed = true;
            gripStart = e.GetPosition(grip);
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.PreviewMouseMove += (_, e) =>
        {
            if (!gripPressed || e.LeftButton != MouseButtonState.Pressed)
                return;
            Point current = e.GetPosition(grip);
            if (Math.Abs(current.X - gripStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - gripStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            gripPressed = false;
            grip.ReleaseMouseCapture();
            var data = new DataObject(PaneDragFormat, pane.Id);
            DragDrop.DoDragDrop(grip, data, DragDropEffects.Move);
            e.Handled = true;
        };
        grip.PreviewMouseLeftButtonUp += (_, e) =>
        {
            gripPressed = false;
            grip.ReleaseMouseCapture();
            e.Handled = true;
        };

        var maximize = CreatePaneButton("□", "Maximize or restore this partition");
        var detach = CreatePaneButton("↗", "Detach this item");
        var close = CreatePaneButton("×", "Close this item");
        maximize.Click += (_, _) => ToggleMaximize(slot.Id);
        detach.Click += (_, _) => PaneDetachRequested?.Invoke(this, new WorkspacePaneRequest(WorkspaceId, slot.Id, pane));
        close.Click += (_, _) => PaneCloseRequested?.Invoke(this, new WorkspacePaneRequest(WorkspaceId, slot.Id, pane));
        panel.Children.Add(grip);
        panel.Children.Add(maximize);
        panel.Children.Add(detach);
        panel.Children.Add(close);
        controls.Child = panel;
        root.Children.Add(controls);

        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount > 1)
                return;
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
                return;
            PaneActivated?.Invoke(this, new WorkspacePaneRequest(WorkspaceId, slot.Id, pane));
        };
        // Window/pane controls remain physically present at the top-right but are
        // visually hidden until the pointer enters their own compact control area.
        // Moving anywhere else over the chart must not reveal them.
        controls.MouseEnter += (_, _) => controls.Opacity = 1;
        controls.MouseLeave += (_, _) => controls.Opacity = 0;
        return root;
    }

    private static Button CreatePaneButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 24,
            Height = 22,
            MinHeight = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Content = new TextBlock
            {
                Text = glyph == "—" ? "−" : glyph == "□" ? "▢" : glyph,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = glyph == "×" ? 14 : 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = tooltip,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 205, 205)),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(
            tooltip.StartsWith("Close", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(126, 35, 49)
                : Color.FromRgb(34, 34, 34));
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Button)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void UpdateDropHighlight(int partitionId, DragEventArgs e, bool visible)
    {
        if (!_slots.TryGetValue(partitionId, out PartitionSlot? slot) || !TryReadPaneId(e.Data, out _))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        slot.DropFrame.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static bool TryReadPaneId(IDataObject data, out int paneId)
    {
        paneId = 0;
        object? value = data.GetData(PaneDragFormat);
        return value switch
        {
            int number => (paneId = number) > 0,
            string text when int.TryParse(text, out int number) => (paneId = number) > 0,
            _ => false
        };
    }

    private void WorkspaceSurfaceControl_MouseMove(object sender, MouseEventArgs e)
    {
        Point point = e.GetPosition(this);
        if (point.X <= 22 && point.Y <= 22)
            _workspaceHandleHotspot.Opacity = 0.92;

    }

    private void WorkspaceHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _workspaceHandlePressed = true;
        _workspaceHandleStart = e.GetPosition(this);
        _workspaceHandleHotspot.CaptureMouse();
        e.Handled = true;
    }

    private void WorkspaceHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_workspaceHandlePressed || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _workspaceHandleStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _workspaceHandleStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _workspaceHandlePressed = false;
        _workspaceHandleHotspot.ReleaseMouseCapture();
        WorkspaceDetachRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void WorkspaceHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _workspaceHandlePressed = false;
        _workspaceHandleHotspot.ReleaseMouseCapture();
        e.Handled = true;
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
