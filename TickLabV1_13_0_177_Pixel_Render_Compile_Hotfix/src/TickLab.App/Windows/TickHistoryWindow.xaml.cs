using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Core.Diagnostics;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Windows;

public enum TickHistoryWindowAction
{
    None,
    RefreshTicks,
    BuildCandles
}

public partial class TickHistoryWindow : Window
{
    private readonly PersistentHistoryStore _store;
    private readonly string _connectorId;
    private readonly IReadOnlyList<string> _symbols;
    private IReadOnlyList<SymbolRow> _symbolRows = Array.Empty<SymbolRow>();

    public TickHistoryWindow(
        PersistentHistoryStore store,
        string connectorId,
        IEnumerable<string> symbols,
        string? preferredSymbol)
    {
        InitializeComponent();
        _store = store;
        _connectorId = connectorId;
        _symbols = symbols
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ConnectorText.Text = connectorId;
        Loaded += (_, _) =>
        {
            RefreshSymbolRows(preferredSymbol);
            RefreshRows();
        };
    }

    public TickHistoryWindowAction RequestedAction { get; private set; }
    public string OperationSymbol { get; private set; } = string.Empty;
    public string? OperationSegmentKey { get; private set; }
    public bool ReturnToConnections { get; private set; }
    public bool VisibilityChanged { get; private set; }

    private SymbolRow? SelectedSymbolRow => SymbolsList.SelectedItem as SymbolRow;
    private string? SelectedSymbol => SelectedSymbolRow?.Symbol;
    private TickRow? SelectedRow => SegmentsGrid.SelectedItem as TickRow;

    private void RefreshSymbolRows(string? preferredSymbol = null)
    {
        string? selected = preferredSymbol ?? SelectedSymbol;
        _symbolRows = _symbols.Select(symbol =>
        {
            TickHistoryFolderSummary[] folders = _store.GetTickHistoryFolders(_connectorId, symbol).ToArray();
            bool? aggregate = folders.Length == 0 || folders.All(item => item.IsVisible)
                ? true
                : folders.All(item => !item.IsVisible)
                    ? false
                    : null;
            return new SymbolRow(symbol, aggregate);
        }).ToArray();
        SymbolsList.ItemsSource = _symbolRows;
        SymbolsList.SelectedItem = _symbolRows.FirstOrDefault(item =>
            string.Equals(item.Symbol, selected, StringComparison.OrdinalIgnoreCase))
            ?? _symbolRows.FirstOrDefault();
    }

    private void SymbolsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        string? symbol = SelectedSymbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            SegmentsGrid.ItemsSource = null;
            FolderTitle.Text = "Select an instrument";
            FolderPathText.Text = string.Empty;
            return;
        }

        FolderTitle.Text = symbol + " — raw tick folders (actual stored coverage)";
        FolderPathText.Text = _store.GetTickArchiveFolder(_connectorId, symbol);
        SegmentsGrid.ItemsSource = _store.GetTickHistoryFolders(_connectorId, symbol)
            .Select(item => new TickRow(
                item.IsVisible,
                item.SegmentKey,
                FormatDate(item.ActualEarliestUnix),
                FormatDate(item.ActualLatestUnix),
                FormatBytes(item.SizeBytes),
                item.Status,
                Path.GetDirectoryName(item.FilePath) ?? item.FilePath))
            .ToArray();
    }

    private void RefreshTicksButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        OperationSymbol = SelectedSymbol;
        OperationSegmentKey = SelectedRow?.SegmentKey;
        RequestedAction = TickHistoryWindowAction.RefreshTicks;
        DialogResult = true;
    }

    private void BuildCandlesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null || SelectedRow is null)
        {
            StatusText.Text = "Select one three-month tick folder first.";
            return;
        }
        OperationSymbol = SelectedSymbol;
        OperationSegmentKey = SelectedRow.SegmentKey;
        RequestedAction = TickHistoryWindowAction.BuildCandles;
        DialogResult = true;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        OpenFolder(_store.GetTickArchiveFolder(_connectorId, SelectedSymbol));
    }

    private void OpenSymbolFolder_Click(object sender, RoutedEventArgs e) => OpenFolderButton_Click(sender, e);

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        OpenFolder(SelectedRow.Path);
    }

    private void CopySymbolFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        TryCopyFileDrop(_store.GetTickArchiveFolder(_connectorId, SelectedSymbol), "Tick history folder");
    }

    private void CopySymbolPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        CopyText(_store.GetTickArchiveFolder(_connectorId, SelectedSymbol), "Tick folder path copied.");
    }

    private void CopyFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        TryCopyFileDrop(SelectedRow.Path, "Three-month tick folder");
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        CopyText(SelectedRow.Path, "Tick folder path copied.");
    }

    private void DeleteSymbolFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        string path = _store.GetTickArchiveFolder(_connectorId, SelectedSymbol);
        if (!Directory.Exists(path))
        {
            StatusText.Text = "That tick history folder does not exist.";
            return;
        }
        if (MessageBox.Show(
                this,
                $"Delete every saved tick folder for {SelectedSymbol}? This cannot be undone.",
                "Permanent delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (new PinPromptWindow(path) { Owner = this }.ShowDialog() != true)
            return;
        try
        {
            _store.DeleteAllTickHistoryForSymbol(_connectorId, SelectedSymbol);
            VisibilityChanged = true;
            StatusText.Text = $"Deleted all tick history for {SelectedSymbol}.";
            RefreshSymbolRows(SelectedSymbol);
            RefreshRows();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Delete failed: {exception.Message}";
            ReportFailure(exception, "delete_symbol_tick_history", path, null);
        }
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null || SelectedRow is null) return;
        if (MessageBox.Show(
                this,
                $"Delete every saved tick in {SelectedSymbol} {SelectedRow.SegmentKey}? This cannot be undone.",
                "Permanent delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (new PinPromptWindow(SelectedRow.Path) { Owner = this }.ShowDialog() != true)
            return;
        try
        {
            _store.DeleteTickHistoryFolder(_connectorId, SelectedSymbol, SelectedRow.SegmentKey);
            VisibilityChanged = true;
            StatusText.Text = $"Deleted tick folder {SelectedRow.SegmentKey}.";
            RefreshSymbolRows(SelectedSymbol);
            RefreshRows();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Delete failed: {exception.Message}";
            ReportFailure(exception, "delete_tick_segment", SelectedRow.Path, SelectedRow.SegmentKey);
        }
    }

    private void SymbolVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not string symbol)
            return;
        bool visible = checkBox.IsChecked == true;
        _store.SetAllTickHistoryVisibility(_connectorId, symbol, visible);
        VisibilityChanged = true;
        StatusText.Text = visible
            ? $"All saved {symbol} tick folders are enabled."
            : $"All saved {symbol} tick folders are hidden but remain on disk.";
        RefreshSymbolRows(symbol);
        RefreshRows();
        e.Handled = true;
    }

    private void SegmentVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null || sender is not CheckBox checkBox || checkBox.Tag is not string segmentKey)
            return;
        bool visible = checkBox.IsChecked == true;
        _store.SetTickHistoryVisible(_connectorId, SelectedSymbol, segmentKey, visible);
        VisibilityChanged = true;
        StatusText.Text = visible
            ? $"{segmentKey} is enabled. Its ticks may appear again."
            : $"{segmentKey} is hidden. Its real three-month position remains as empty chart space.";
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        e.Handled = true;
    }

    private void ShowAllSymbols_Click(object sender, RoutedEventArgs e)
    {
        foreach (string symbol in _symbols)
            _store.SetAllTickHistoryVisibility(_connectorId, symbol, true);
        VisibilityChanged = true;
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        StatusText.Text = "All tick history folders are enabled.";
    }

    private void HideAllSymbols_Click(object sender, RoutedEventArgs e)
    {
        foreach (string symbol in _symbols)
            _store.SetAllTickHistoryVisibility(_connectorId, symbol, false);
        VisibilityChanged = true;
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        StatusText.Text = "All tick history folders are hidden. No tick files were deleted.";
    }

    private void SymbolItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void SegmentRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void TryCopyFileDrop(string path, string description)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            StatusText.Text = $"{description} does not exist yet.";
            return;
        }

        try
        {
            var collection = new StringCollection { Path.GetFullPath(path) };
            var data = new DataObject();
            data.SetFileDropList(collection);
            data.SetData(DataFormats.UnicodeText, Path.GetFullPath(path));
            data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            SetClipboardDataWithRetry(data);
            StatusText.Text = $"{description} copied. Open Explorer and press Ctrl+V.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Copy failed: {exception.Message}";
            ReportFailure(exception, "copy_tick_file_or_folder", path, SelectedRow?.SegmentKey);
        }
    }

    private static void SetClipboardDataWithRetry(object data)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (Exception exception)
            {
                last = exception;
                Thread.Sleep(60);
            }
        }
        throw new InvalidOperationException("Windows clipboard is busy.", last);
    }

    private void CopyText(string text, string success)
    {
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            SetClipboardDataWithRetry(data);
            StatusText.Text = success;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Copy failed: {exception.Message}";
            ReportFailure(exception, "copy_tick_path", text, SelectedRow?.SegmentKey);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            StatusText.Text = "Opened folder in Windows Explorer.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Open folder failed: {exception.Message}";
            ReportFailure(exception, "open_tick_folder", path, SelectedRow?.SegmentKey);
        }
    }

    private void ReportFailure(
        Exception exception,
        string stage,
        string? path,
        string? segmentKey)
    {
        TickLabErrorEngine.Report(
            exception,
            new TickLabErrorContext(
                "Tick history",
                stage,
                "Copy diagnostics. Keep the raw tick archive unchanged and retry only this action.",
                ErrorCode: "TL-TICK-FILE",
                Symbol: SelectedSymbol,
                ConnectorId: _connectorId,
                FilePath: path,
                AdditionalData: string.IsNullOrWhiteSpace(segmentKey)
                    ? null
                    : new Dictionary<string, string> { ["Segment"] = segmentKey }),
            TickLabErrorSeverity.Error,
            this);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToConnections = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatDate(long unix) => unix <= 0
        ? "—"
        : DateTimeOffset.FromUnixTimeSeconds(unix).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed record SymbolRow(string Symbol, bool? IsVisible);

    private sealed record TickRow(
        bool IsVisible,
        string SegmentKey,
        string From,
        string To,
        string Size,
        string Status,
        string Path);
}
