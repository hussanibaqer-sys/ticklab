using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Drawing;

namespace TickLab.Desktop.Windows;

public partial class DrawingFavoritesWindow : Window
{
    private string _draggedId = string.Empty;
    private Point _dragStart;
    private bool _isCompact;
    private int _toolCount;
    private bool _movingWindow;
    private Point _windowDragOffset;
    private Window? _ownerHook;
    private double _maximumHostWidth = 820.0;

    public DrawingFavoritesWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) => ClampToOwnerFrame();
        SizeChanged += (_, _) => ClampToOwnerFrame();
        Loaded += (_, _) => AttachOwnerFrameTracking();
    }

    public event Action<string>? ToolSelected;
    public event Action<string>? RemoveRequested;
    public event Action<string, string>? MoveRequested;
    public event Action<bool>? CompactChanged;

    public bool IsCompact => _isCompact;
    public double DesiredExpandedWidth => Math.Max(118.0, 80.0 + Math.Max(1, _toolCount) * 40.0);

    public void SetMaximumHostWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 1)
            return;
        _maximumHostWidth = Math.Max(MinWidth, width);
        UpdateBarSize();
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachOwnerFrameTracking();
        base.OnClosed(e);
    }

    private void AttachOwnerFrameTracking()
    {
        if (ReferenceEquals(_ownerHook, Owner))
            return;
        DetachOwnerFrameTracking();
        _ownerHook = Owner;
        if (_ownerHook is null)
            return;
        _ownerHook.LocationChanged += OwnerFrameChanged;
        _ownerHook.SizeChanged += OwnerFrameSizeChanged;
        _ownerHook.StateChanged += OwnerFrameChanged;
        ClampToOwnerFrame();
    }

    private void DetachOwnerFrameTracking()
    {
        if (_ownerHook is null)
            return;
        _ownerHook.LocationChanged -= OwnerFrameChanged;
        _ownerHook.SizeChanged -= OwnerFrameSizeChanged;
        _ownerHook.StateChanged -= OwnerFrameChanged;
        _ownerHook = null;
    }

    private void OwnerFrameChanged(object? sender, EventArgs e) => ClampToOwnerFrame();
    private void OwnerFrameSizeChanged(object? sender, SizeChangedEventArgs e) => ClampToOwnerFrame();

    public void EnsureVisible(Window? anchor = null)
    {
        Window? owner = Owner ?? anchor;
        Rect frame = GetOwnerFrame(owner);
        double width = EffectiveWidth();
        double height = EffectiveHeight();
        bool outside = !double.IsFinite(Left) || !double.IsFinite(Top) ||
                       Left < frame.Left || Top < frame.Top ||
                       Left + width > frame.Right || Top + height > frame.Bottom;
        if (outside)
        {
            Left = frame.Left + 72;
            Top = frame.Top + 76;
        }
        ClampToOwnerFrame(owner);
    }

    private Rect GetOwnerFrame(Window? owner = null)
    {
        owner ??= Owner;
        if (owner is not null && double.IsFinite(owner.Left) && double.IsFinite(owner.Top))
        {
            double width = owner.ActualWidth > 1 ? owner.ActualWidth : owner.Width;
            double height = owner.ActualHeight > 1 ? owner.ActualHeight : owner.Height;
            if (double.IsFinite(width) && double.IsFinite(height) && width > 1 && height > 1)
                return new Rect(owner.Left, owner.Top, width, height);
        }

        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private double EffectiveWidth() => Math.Max(MinWidth, ActualWidth > 1 ? ActualWidth : Width);
    private double EffectiveHeight() => Math.Max(MinHeight, ActualHeight > 1 ? ActualHeight : Height);

    private void ClampToOwnerFrame(Window? owner = null)
    {
        if (!IsLoaded && !double.IsFinite(Left))
            return;

        const double margin = 6.0;
        Rect frame = GetOwnerFrame(owner);
        double width = EffectiveWidth();
        double height = EffectiveHeight();
        double maxAvailableWidth = Math.Max(MinWidth, frame.Width - margin * 2);
        if (width > maxAvailableWidth + 0.5)
        {
            Width = maxAvailableWidth;
            width = maxAvailableWidth;
        }
        double minLeft = frame.Left + margin;
        double minTop = frame.Top + margin;
        double maxLeft = Math.Max(minLeft, frame.Right - width - margin);
        double maxTop = Math.Max(minTop, frame.Bottom - height - margin);
        double clampedLeft = Math.Clamp(double.IsFinite(Left) ? Left : minLeft, minLeft, maxLeft);
        double clampedTop = Math.Clamp(double.IsFinite(Top) ? Top : minTop, minTop, maxTop);

        if (Math.Abs(Left - clampedLeft) > 0.25)
            Left = clampedLeft;
        if (Math.Abs(Top - clampedTop) > 0.25)
            Top = clampedTop;
    }

    public void SetTools(IEnumerable<string> ids)
    {
        string[] values = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _toolCount = values.Length;
        FavoritesPanel.Children.Clear();
        foreach (string id in values)
        {
            DrawingToolDefinition? tool = DrawingToolCatalog.Find(id);
            if (tool is not null)
                FavoritesPanel.Children.Add(CreateButton(tool));
        }
        UpdateBarSize();
    }

    public void SetCompact(bool compact)
    {
        _isCompact = compact;
        FavoritesScroll.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MinimizeButton.Content = compact ? "□" : "—";
        UpdateBarSize();
        CompactChanged?.Invoke(compact);
    }

    private void UpdateBarSize()
    {
        Height = 46;
        if (_isCompact)
        {
            Width = 76;
            ClampToOwnerFrame();
            return;
        }
        Rect frame = GetOwnerFrame();
        double frameLimit = Math.Max(MinWidth, frame.Width - 12);
        Width = Math.Min(DesiredExpandedWidth, Math.Min(_maximumHostWidth, frameLimit));
        ClampToOwnerFrame();
    }

    private Button CreateButton(DrawingToolDefinition tool)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            MinHeight = 36,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromRgb(13, 26, 43)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 54, 80)),
            Foreground = Brushes.White,
            ToolTip = $"{tool.DisplayName}\nDrag left or right to reorder • Right-click to remove",
            Tag = tool.Id,
            Content = DrawingToolIconFactory.CreateToolIcon(tool, 18, new SolidColorBrush(Color.FromRgb(213, 224, 237)))
        };
        button.Click += (_, _) => ToolSelected?.Invoke(tool.Id);
        button.PreviewMouseLeftButtonDown += Favorite_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += Favorite_PreviewMouseMove;
        button.AllowDrop = true;
        button.Drop += Favorite_Drop;
        button.PreviewMouseRightButtonUp += (_, e) =>
        {
            RemoveRequested?.Invoke(tool.Id);
            e.Handled = true;
        };
        return button;
    }

    private void Favorite_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _draggedId = (sender as Button)?.Tag as string ?? string.Empty;
    }

    private void Favorite_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_draggedId))
            return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("TickLabDrawingFavorite", _draggedId), DragDropEffects.Move);
    }

    private void Favorite_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TickLabDrawingFavorite") || sender is not Button target || target.Tag is not string targetId)
            return;
        string sourceId = e.Data.GetData("TickLabDrawingFavorite") as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sourceId) && !string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
            MoveRequested?.Invoke(sourceId, targetId);
        e.Handled = true;
    }

    private void FavoritesPanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TickLabDrawingFavorite"))
            return;
        string sourceId = e.Data.GetData("TickLabDrawingFavorite") as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sourceId))
            MoveRequested?.Invoke(sourceId, string.Empty);
        e.Handled = true;
    }


    private void FavoritesScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? -80 : 80;
        FavoritesScroll.ScrollToHorizontalOffset(Math.Clamp(FavoritesScroll.HorizontalOffset + delta, 0, FavoritesScroll.ScrollableWidth));
        e.Handled = true;
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not UIElement grip)
            return;
        AttachOwnerFrameTracking();
        _movingWindow = true;
        _windowDragOffset = e.GetPosition(this);
        grip.CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        Window? owner = Owner;
        if (!_movingWindow || e.LeftButton != MouseButtonState.Pressed || owner is null)
            return;
        Point mouseInOwner = e.GetPosition(owner);
        Left = owner.Left + mouseInOwner.X - _windowDragOffset.X;
        Top = owner.Top + mouseInOwner.Y - _windowDragOffset.Y;
        ClampToOwnerFrame(owner);
        e.Handled = true;
    }

    private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_movingWindow)
            return;
        _movingWindow = false;
        if (sender is UIElement grip && grip.IsMouseCaptured)
            grip.ReleaseMouseCapture();
        ClampToOwnerFrame();
        e.Handled = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => SetCompact(!_isCompact);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
