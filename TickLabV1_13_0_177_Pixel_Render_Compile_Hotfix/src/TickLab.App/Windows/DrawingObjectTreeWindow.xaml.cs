using System.Windows;
using System.Windows.Controls;
using TickLab.Core.Drawing;

namespace TickLab.Desktop.Windows;

public partial class DrawingObjectTreeWindow : Window
{
    private IReadOnlyList<ChartDrawing> _drawings = Array.Empty<ChartDrawing>();

    public DrawingObjectTreeWindow() => InitializeComponent();

    public event Action<string>? SelectRequested;
    public event Action<string>? ToggleHiddenRequested;
    public event Action<string>? ToggleLockedRequested;
    public event Action<string>? BringFrontRequested;
    public event Action<string>? SendBackRequested;
    public event Action<string>? SettingsRequested;
    public event Action<string>? RemoveRequested;
    public event Action? RefreshRequested;

    public void SetDrawings(IReadOnlyList<ChartDrawing> drawings)
    {
        _drawings = drawings;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string search = SearchBox.Text.Trim();
        IEnumerable<ChartDrawing> query = _drawings;
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase));
        ObjectsGrid.ItemsSource = query.Select(item => new Row(item.Id,
            string.IsNullOrWhiteSpace(item.Name) ? item.DisplayName : item.Name,
            item.DisplayName, item.Symbol, !item.IsHidden, item.IsLocked,
            string.IsNullOrWhiteSpace(item.GroupId) ? "—" : item.GroupId[..Math.Min(8, item.GroupId.Length)])).ToArray();
    }

    private string? SelectedId => ObjectsGrid.SelectedItem is Row row ? row.Id : null;
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void ObjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (SelectedId is string id) SelectRequested?.Invoke(id); }
    private void ShowHideButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) ToggleHiddenRequested?.Invoke(id); }
    private void LockButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) ToggleLockedRequested?.Invoke(id); }
    private void FrontButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) BringFrontRequested?.Invoke(id); }
    private void BackButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) SendBackRequested?.Invoke(id); }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) SettingsRequested?.Invoke(id); }
    private void RemoveButton_Click(object sender, RoutedEventArgs e) { if (SelectedId is string id) RemoveRequested?.Invoke(id); }

    private sealed record Row(string Id, string Name, string Tool, string Symbol, bool Visible, bool Locked, string Group);
}
