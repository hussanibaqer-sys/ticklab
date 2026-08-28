using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Drawing;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public partial class DrawingToolPaletteWindow : Window
{
    private DrawingToolCategory _category;
    private IReadOnlyList<DrawingToolDefinition> _tools = Array.Empty<DrawingToolDefinition>();
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private bool _allowDeactivateClose = true;

    public DrawingToolPaletteWindow()
    {
        InitializeComponent();
        ApplicationThemeManager.ApplyToWindow(this);
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    public event Action<string>? ToolSelected;
    public event Action<string>? FavoriteToggled;

    public void SetCategory(DrawingToolCategory category, IEnumerable<string> favoriteIds)
    {
        _category = category;
        _tools = DrawingToolCatalog.InCategory(category);
        _favorites.Clear();
        foreach (string id in favoriteIds)
            _favorites.Add(id);
        TitleText.Text = DrawingToolCatalog.CategoryName(category);
        bool light = ApplicationThemeManager.CurrentTheme == "Light";
        Brush categoryStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#374151" : "#E5E7EB")!);
        CategoryIconHost.Child = DrawingToolIconFactory.CreateCategoryIcon(category, 20, categoryStroke);
        SearchBox.Text = string.Empty;
        BuildRows();
    }

    public void SetDeactivateClose(bool enabled) => _allowDeactivateClose = enabled;

    private void BuildRows()
    {
        string query = SearchBox.Text.Trim();
        IEnumerable<DrawingToolDefinition> visible = _tools;
        if (!string.IsNullOrWhiteSpace(query))
        {
            visible = visible.Where(item =>
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        ToolRowsPanel.Children.Clear();
        int count = 0;
        foreach (DrawingToolDefinition tool in visible)
        {
            ToolRowsPanel.Children.Add(CreateRow(tool));
            count++;
        }
        FooterText.Text = count == 0
            ? "No matching drawing tools"
            : $"{count:N0} tools • Click the large star to add/remove a favorite";
    }

    private UIElement CreateRow(DrawingToolDefinition tool)
    {
        bool light = ApplicationThemeManager.CurrentTheme == "Light";
        Brush iconBackground = Brush(light ? "#F1F3F5" : "#111827");
        Brush iconStroke = Brush(light ? "#374151" : "#D6E0EC");
        Brush mainText = Brush(light ? "#20232A" : "#EAF1FA");
        Brush mutedText = Brush(light ? "#737780" : "#8FA2B8");
        Brush hover = Brush(light ? "#EEF2FF" : "#12253C");

        var border = new Border
        {
            CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 2, 2, 3),
            Padding = new Thickness(7, 5, 5, 5), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, Tag = tool.Id, SnapsToDevicePixels = true
        };
        var grid = new Grid { SnapsToDevicePixels = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        var iconHost = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(7),
            Background = iconBackground, SnapsToDevicePixels = true,
            Child = DrawingToolIconFactory.CreateToolIcon(tool, 19, iconStroke)
        };
        grid.Children.Add(iconHost);

        var textPanel = new StackPanel { Margin = new Thickness(5, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = tool.DisplayName, Foreground = mainText, FontSize = 12.5,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = FriendlyHint(tool), Foreground = mutedText, FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        bool favorite = _favorites.Contains(tool.Id);
        var star = new Button
        {
            Width = 34, Height = 32, MinHeight = 32, Padding = new Thickness(0), Margin = new Thickness(0),
            FontSize = 20, FontWeight = FontWeights.Normal, Content = favorite ? "★" : "☆",
            Foreground = favorite ? Brush("#F59E0B") : mutedText, Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, ToolTip = favorite ? "Remove from favorites" : "Add to favorites", Tag = tool.Id
        };
        star.Click += FavoriteButton_Click;
        Grid.SetColumn(star, 2);
        grid.Children.Add(star);

        border.Child = grid;
        border.MouseEnter += (_, _) => border.Background = hover;
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
            ToolSelected?.Invoke(tool.Id);
            Close();
        };
        return border;
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color)!);

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
            return;
        if (!_favorites.Add(id))
            _favorites.Remove(id);
        FavoriteToggled?.Invoke(id);
        BuildRows();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string FriendlyHint(DrawingToolDefinition tool)
    {
        if (tool.IsCursorTool)
            return "Cursor and selection mode";
        if (tool.VariableAnchors)
            return "Click points • double-click or Enter to finish";
        return tool.MaximumAnchors switch
        {
            1 => "Click once to place",
            2 => "Click start and end",
            3 => "Place three anchor points",
            _ => $"Place {tool.MaximumAnchors} anchor points"
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => BuildRows();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            DrawingToolDefinition? first = _tools.FirstOrDefault(item =>
                string.IsNullOrWhiteSpace(SearchBox.Text) ||
                item.DisplayName.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (first is not null)
            {
                ToolSelected?.Invoke(first.Id);
                Close();
            }
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_allowDeactivateClose && IsVisible)
            Close();
    }
}
