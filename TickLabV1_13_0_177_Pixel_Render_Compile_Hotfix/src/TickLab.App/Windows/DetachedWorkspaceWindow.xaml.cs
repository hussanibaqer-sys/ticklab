using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop.Windows;

public partial class DetachedWorkspaceWindow : Window
{
    private bool _allowClose;

    public DetachedWorkspaceWindow(int workspaceId, WorkspaceSurfaceControl surface)
    {
        InitializeComponent();
        WorkspaceId = workspaceId;
        WorkspaceNumberText.Text = workspaceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WorkspaceTitleText.Text = $"Workspace {workspaceId}";
        Title = $"TickLab — Workspace {workspaceId}";
        WorkspaceHost.Content = surface;
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        };
        LocationChanged += (_, _) => WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => WindowGeometryChanged?.Invoke(this, EventArgs.Empty);
        UpdateMaximizeGlyph();
    }

    public int WorkspaceId { get; }
    public WorkspaceSurfaceControl Surface =>
        WorkspaceHost.Content as WorkspaceSurfaceControl
        ?? throw new InvalidOperationException("The workspace surface is not attached to this window.");

    public event EventHandler? AttachToTickLabRequested;
    public event EventHandler? CloseWorkspaceRequested;
    public event EventHandler? WindowGeometryChanged;

    public WorkspaceSurfaceControl ReleaseSurface()
    {
        WorkspaceSurfaceControl surface = Surface;
        WorkspaceHost.Content = null;
        return surface;
    }

    public void CloseWithoutPrompt()
    {
        _allowClose = true;
        Close();
    }


    public void BeginDragFromCurrentPointer()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The pointer was released before the detached window received control.
            }
        }));
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            CloseWorkspaceRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }

    private void WindowDragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
        var menu = new ContextMenu();
        var attach = new MenuItem { Header = "Attach to TickLab" };
        attach.Click += (_, _) => AttachToTickLabRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(attach);
        var restore = new MenuItem { Header = "Restore all partitions" };
        restore.Click += (_, _) => Surface.RestorePartitions();
        menu.Items.Add(restore);
        menu.PlacementTarget = WindowDragBar;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWorkspaceRequested?.Invoke(this, EventArgs.Empty);

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton is null)
            return;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore this workspace" : "Maximize this workspace";
    }
}
