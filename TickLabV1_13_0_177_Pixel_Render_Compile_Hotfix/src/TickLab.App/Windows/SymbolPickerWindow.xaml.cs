using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Windows;

public partial class SymbolPickerWindow : Window
{
    private IReadOnlyList<Mt5SymbolInfo> _allSymbols;
    private readonly string _currentSymbol;
    private readonly HashSet<string> _favouriteSymbols = new(StringComparer.OrdinalIgnoreCase);
    private string _activeFilter = "All";
    private bool _filterDragging;
    private Point _filterDragStart;
    private double _filterDragStartOffset;
    private static readonly string FavouriteFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TickLab",
        "symbol-favourites.json");

    public Mt5SymbolInfo? SelectedSymbol { get; private set; }

    public event EventHandler? RefreshRequested;

    public SymbolPickerWindow(
        IReadOnlyList<Mt5SymbolInfo> symbols,
        string currentSymbol)
    {
        InitializeComponent();

        _allSymbols =
            symbols ??
            Array.Empty<Mt5SymbolInfo>();

        _currentSymbol =
            currentSymbol ??
            string.Empty;

        LoadFavourites();
        BuildFilterButtons();
        Loaded += SymbolPickerWindow_Loaded;
        ApplyFilter();
    }

    public void ReplaceSymbols(
        IReadOnlyList<Mt5SymbolInfo> symbols)
    {
        _allSymbols =
            symbols ??
            Array.Empty<Mt5SymbolInfo>();

        ApplyFilter();
    }


    private static bool IsWithinButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void FilterScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;
        double delta = e.Delta > 0 ? -110.0 : 110.0;
        viewer.ScrollToHorizontalOffset(Math.Clamp(viewer.HorizontalOffset + delta, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private void FilterScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer viewer ||
            IsWithinButton(e.OriginalSource as DependencyObject))
            return;
        _filterDragging = true;
        _filterDragStart = e.GetPosition(viewer);
        _filterDragStartOffset = viewer.HorizontalOffset;
        viewer.CaptureMouse();
        e.Handled = true;
    }

    private void FilterScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_filterDragging || sender is not ScrollViewer viewer || e.LeftButton != MouseButtonState.Pressed)
            return;
        double dx = e.GetPosition(viewer).X - _filterDragStart.X;
        viewer.ScrollToHorizontalOffset(Math.Clamp(_filterDragStartOffset - dx, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }

    private void FilterScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndFilterDrag(sender as ScrollViewer);
    }

    private void FilterScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _filterDragging = false;
    }

    private void EndFilterDrag(ScrollViewer? viewer)
    {
        if (!_filterDragging)
            return;
        _filterDragging = false;
        if (viewer?.IsMouseCaptured == true)
            viewer.ReleaseMouseCapture();
    }

    private void BuildFilterButtons()
    {
        FilterPanel.Children.Clear();
        foreach (string filter in InstrumentCategoryClassifier.Filters)
        {
            var button = new ToggleButton
            {
                Content = filter,
                Tag = filter,
                MinWidth = 66,
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 3, 10, 3),
                IsChecked = string.Equals(filter, _activeFilter, StringComparison.OrdinalIgnoreCase)
            };
            button.Click += FilterButton_Click;
            FilterPanel.Children.Add(button);
        }
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string filter)
            return;

        _activeFilter = filter;
        foreach (ToggleButton candidate in FilterPanel.Children.OfType<ToggleButton>())
            candidate.IsChecked = ReferenceEquals(candidate, button);
        ApplyFilter();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void SearchBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!SearchBox.IsKeyboardFocusWithin)
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }
    }

    private void SymbolPickerWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void SearchBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Down &&
            SymbolsList.Items.Count > 0)
        {
            SymbolsList.SelectedIndex =
                Math.Max(
                    0,
                    SymbolsList.SelectedIndex);

            SymbolsList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (SymbolsList.SelectedItem is null &&
                SymbolsList.Items.Count > 0)
            {
                SymbolsList.SelectedIndex = 0;
            }

            AcceptSelection();
            e.Handled = true;
        }
    }

    private void SymbolsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled =
            SymbolsList.SelectedItem is SymbolPickerItem;
    }

    private void SymbolsList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void SelectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        AcceptSelection();
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void AcceptSelection()
    {
        if (SymbolsList.SelectedItem is not SymbolPickerItem selected)
            return;

        SelectedSymbol = selected.Symbol;
        DialogResult = true;
    }

    private void FavouriteStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string symbolName || string.IsNullOrWhiteSpace(symbolName))
            return;

        string? selectedName = (SymbolsList.SelectedItem as SymbolPickerItem)?.Name;
        if (!_favouriteSymbols.Add(symbolName))
            _favouriteSymbols.Remove(symbolName);
        SaveFavourites();
        ApplyFilter(selectedName ?? symbolName);
        e.Handled = true;
    }

    private void ApplyFilter(string? preferredSelection = null)
    {
        string search = SearchBox?.Text?.Trim() ?? string.Empty;

        IEnumerable<Mt5SymbolInfo> source = _allSymbols;
        if (string.Equals(_activeFilter, "Favorites", StringComparison.OrdinalIgnoreCase))
            source = source.Where(item => _favouriteSymbols.Contains(item.Name));
        else if (!string.Equals(_activeFilter, "All", StringComparison.OrdinalIgnoreCase))
            source = source.Where(item => string.Equals(InstrumentCategoryClassifier.Classify(item), _activeFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        SymbolPickerItem[] filtered = source
            .OrderBy(item => _favouriteSymbols.Contains(item.Name) ? 0 : 1)
            .ThenBy(item => string.Equals(item.Name, search, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new SymbolPickerItem(item, _favouriteSymbols.Contains(item.Name)))
            .ToArray();

        SymbolsList.ItemsSource = filtered;
        EmptyText.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        int favouriteCount = _allSymbols.Count(item => _favouriteSymbols.Contains(item.Name));
        CountText.Text = filtered.Length == _allSymbols.Count
            ? $"{favouriteCount:N0} favourites · {filtered.Length:N0} instruments"
            : $"{filtered.Length:N0} of {_allSymbols.Count:N0} instruments · {favouriteCount:N0} favourites";

        SelectButton.IsEnabled = false;
        string wantedSelection = !string.IsNullOrWhiteSpace(preferredSelection)
            ? preferredSelection
            : _currentSymbol;
        SymbolPickerItem? current = filtered.FirstOrDefault(item =>
            string.Equals(item.Name, wantedSelection, StringComparison.OrdinalIgnoreCase));
        SymbolPickerItem? exact = string.IsNullOrWhiteSpace(search)
            ? null
            : filtered.FirstOrDefault(item => string.Equals(item.Name, search, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            SymbolsList.SelectedItem = exact;
            SymbolsList.ScrollIntoView(exact);
        }
        else if (current is not null)
        {
            SymbolsList.SelectedItem = current;
            SymbolsList.ScrollIntoView(current);
        }
        else if (filtered.Length > 0)
        {
            SymbolsList.SelectedIndex = 0;
            SymbolsList.ScrollIntoView(filtered[0]);
        }
    }

    private void LoadFavourites()
    {
        try
        {
            if (!File.Exists(FavouriteFilePath))
                return;
            string[]? saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FavouriteFilePath));
            if (saved is null)
                return;
            foreach (string symbol in saved.Where(item => !string.IsNullOrWhiteSpace(item)))
                _favouriteSymbols.Add(symbol.Trim());
        }
        catch
        {
            _favouriteSymbols.Clear();
        }
    }

    private void SaveFavourites()
    {
        try
        {
            string? directory = Path.GetDirectoryName(FavouriteFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            string[] saved = _favouriteSymbols.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
            File.WriteAllText(FavouriteFilePath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Favourite ordering remains usable for this session even if persistence is unavailable.
        }
    }

    private sealed record SymbolPickerItem(Mt5SymbolInfo Symbol, bool IsFavourite)
    {
        public string Name => Symbol.Name;
        public string Description => Symbol.Description;
        public string Path => Symbol.Path;
        public string StarGlyph => IsFavourite ? "★" : "☆";
    }
}
