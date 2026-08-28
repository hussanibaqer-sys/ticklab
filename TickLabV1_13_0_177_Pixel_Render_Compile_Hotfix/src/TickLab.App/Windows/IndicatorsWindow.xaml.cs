using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Core.Indicators;
using TickLab.Core.Scripting;

namespace TickLab.Desktop.Windows;

public sealed record AppliedIndicatorListItem(string Key, string Name, string Kind, string Placement);

public partial class IndicatorsWindow : Window
{
    private readonly TickScriptStore _store = new();
    private IReadOnlyList<TickScriptEntry> _allCustom = Array.Empty<TickScriptEntry>();
    private IReadOnlyList<BuiltInListItem> _allBuiltIn = Array.Empty<BuiltInListItem>();
    private IReadOnlyList<AppliedIndicatorListItem> _allApplied = Array.Empty<AppliedIndicatorListItem>();
    private bool _allowClose;

    public IndicatorsWindow()
    {
        InitializeComponent();
        _allBuiltIn = BuiltInIndicatorCatalog.Definitions
            .Select(definition => new BuiltInListItem(
                definition.Kind,
                BuiltInIndicatorCatalog.CategoryLabel(definition.Category),
                definition.Name,
                definition.Placement == BuiltInIndicatorPlacement.Overlay ? "Main chart" : "Separate window"))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToArray();
        Refresh();
    }

    public event Action<TickScriptEntry>? ApplyRequested;
    public event Action<BuiltInIndicatorKind>? BuiltInApplyRequested;
    public event Action<string>? AppliedEditRequested;
    public event Action<string>? AppliedRemoveRequested;
    public event Action<string, IndicatorRouteAction>? AppliedRouteRequested;
    public event Action? OpenEditorRequested;

    public void Refresh()
    {
        _allCustom = _store.GetIndicators();
        ApplyFilter();
    }

    public void SetAppliedIndicators(IReadOnlyList<AppliedIndicatorListItem> items)
    {
        _allApplied = items ?? Array.Empty<AppliedIndicatorListItem>();
        ApplyFilter();
    }

    public void ShowAppliedTab()
    {
        IndicatorTabs.SelectedIndex = 2;
        ApplyFilter();
    }

    public void ShowLibraryTab()
    {
        IndicatorTabs.SelectedIndex = 0;
        SearchBox.Focus();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = (SearchBox.Text ?? string.Empty).Trim();
        BuiltInList.ItemsSource = _allBuiltIn.Where(item =>
            query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        IndicatorList.ItemsSource = _allCustom.Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        AppliedList.ItemsSource = _allApplied.Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        StatusText.Text = $"{BuiltInList.Items.Count} built-in · {IndicatorList.Items.Count} custom · {AppliedList.Items.Count} on chart";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (RemoveButton is null || DeleteScriptButton is null || OpenFolderButton is null || OpenEditorButton is null || ApplyButton is null || IndicatorTabs is null)
            return;
        bool applied = IndicatorTabs.SelectedIndex == 2;
        bool custom = IndicatorTabs.SelectedIndex == 1;
        RemoveButton.IsEnabled = applied && AppliedList.SelectedItem is AppliedIndicatorListItem;
        DeleteScriptButton.IsEnabled = custom && IndicatorList.SelectedItem is TickScriptEntry;
        OpenFolderButton.IsEnabled = custom;
        OpenEditorButton.IsEnabled = IndicatorTabs.SelectedIndex != 0;
        ApplyButton.Content = applied ? "Properties…" : "Properties & Apply";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void IndicatorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void AppliedList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void IndicatorList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (IndicatorTabs.SelectedIndex == 0)
        {
            if (BuiltInList.SelectedItem is BuiltInListItem item)
                BuiltInApplyRequested?.Invoke(item.Kind);
            else
                StatusText.Text = "Select a built-in indicator first.";
        }
        else if (IndicatorTabs.SelectedIndex == 1)
        {
            if (IndicatorList.SelectedItem is TickScriptEntry entry)
                ApplyRequested?.Invoke(entry);
            else
                StatusText.Text = "Select a custom indicator first.";
        }
        else if (AppliedList.SelectedItem is AppliedIndicatorListItem applied)
        {
            AppliedEditRequested?.Invoke(applied.Key);
        }
        else
        {
            StatusText.Text = "Select an indicator on the chart first.";
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveSelectedApplied();
    private void AppliedPropertiesMenu_Click(object sender, RoutedEventArgs e) => ApplyButton_Click(sender, e);
    private void AppliedRemoveMenu_Click(object sender, RoutedEventArgs e) => RemoveSelectedApplied();
    private void AppliedRouteMenu_Click(object sender, RoutedEventArgs e)
    {
        if (AppliedList.SelectedItem is not AppliedIndicatorListItem applied)
        {
            StatusText.Text = "Select an indicator on the chart first.";
            return;
        }
        string actionText = (sender as MenuItem)?.Tag?.ToString() ?? string.Empty;
        if (Enum.TryParse(actionText, true, out IndicatorRouteAction action))
            AppliedRouteRequested?.Invoke(applied.Key, action);
    }

    private void RemoveSelectedApplied()
    {
        if (AppliedList.SelectedItem is not AppliedIndicatorListItem applied)
        {
            StatusText.Text = "Select an indicator on the chart first.";
            return;
        }
        AppliedRemoveRequested?.Invoke(applied.Key);
    }

    private void BuiltInList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ApplyButton_Click(sender, e);
    private void IndicatorList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ApplyButton_Click(sender, e);
    private void AppliedList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ApplyButton_Click(sender, e);

    private void IndicatorList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IndicatorList.SelectedItem is not TickScriptEntry entry)
            return;
        DataObject data = new("TickLab.TickScriptIndicator", entry.SourcePath);
        DragDrop.DoDragDrop(IndicatorList, data, DragDropEffects.Copy);
    }

    private void OpenEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IndicatorTabs.SelectedIndex == 2 && AppliedList.SelectedItem is AppliedIndicatorListItem applied && applied.Key.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
            AppliedEditRequested?.Invoke(applied.Key);
        else
            OpenEditorRequested?.Invoke();
    }


    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = _store.GetFolder(TickScriptKind.Indicator);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void DeleteScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (IndicatorList.SelectedItem is not TickScriptEntry entry)
        {
            StatusText.Text = "Select a custom TickScript indicator first.";
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete '{entry.Name}' from the TickScript Indicators folder?\n\nThis deletes both the source and compiled indicator files.",
            "Delete TickScript indicator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            _store.Delete(entry);
            Refresh();
            StatusText.Text = $"Deleted {entry.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete TickScript indicator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
    public void CloseForShutdown() { _allowClose = true; Close(); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (_allowClose) return; e.Cancel = true; Hide(); }

    private sealed record BuiltInListItem(BuiltInIndicatorKind Kind, string Category, string Name, string Placement);
}
