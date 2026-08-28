using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Core.Diagnostics;
using TickLab.Core.Market;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Windows;

public enum CandleHistoryWindowAction
{
    None,
    RefreshAll
}

public partial class CandleHistoryWindow : Window
{
    private readonly PersistentHistoryStore _store;
    private readonly string _connectorId;
    private readonly IReadOnlyList<string> _symbols;
    private IReadOnlyList<SymbolRow> _symbolRows = Array.Empty<SymbolRow>();

    public CandleHistoryWindow(
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

    public CandleHistoryWindowAction RequestedAction { get; private set; }
    public string OperationSymbol { get; private set; } = string.Empty;
    public bool ReturnToConnections { get; private set; }
    public bool VisibilityChanged { get; private set; }

    private SymbolRow? SelectedSymbolRow => SymbolsList.SelectedItem as SymbolRow;
    private string? SelectedSymbol => SelectedSymbolRow?.Symbol;
    private CandleRow? SelectedRow => TimeframesGrid.SelectedItem as CandleRow;

    private void RefreshSymbolRows(string? preferredSymbol = null)
    {
        string? selected = preferredSymbol ?? SelectedSymbol;
        _symbolRows = _symbols
            .Select(symbol =>
            {
                bool[] states = TimeframeDefinition.NativeMt5Timeframes
                    .Select(code => _store.IsNativeCandleVisible(_connectorId, symbol, code))
                    .ToArray();
                bool? aggregate = states.All(value => value)
                    ? true
                    : states.All(value => !value)
                        ? false
                        : null;
                return new SymbolRow(symbol, aggregate);
            })
            .ToArray();
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
            TimeframesGrid.ItemsSource = null;
            FolderTitle.Text = "Select an instrument";
            FolderPathText.Text = string.Empty;
            return;
        }

        FolderTitle.Text = symbol + " — native candle files";
        FolderPathText.Text = _store.GetNativeCandleHistoryFolder(_connectorId, symbol);
        var byTimeframe = _store.GetNativeCandleFiles(_connectorId, symbol)
            .ToDictionary(item => item.Timeframe, StringComparer.Ordinal);
        TimeframesGrid.ItemsSource = TimeframeDefinition.NativeMt5Timeframes
            .Select(code => byTimeframe.TryGetValue(code, out NativeCandleFileSummary? item)
                ? new CandleRow(
                    _store.IsNativeCandleVisible(_connectorId, symbol, code),
                    code,
                    FormatTime(item.EarliestUnix),
                    FormatTime(item.LatestUnix),
                    item.RecordCount.ToString("N0", CultureInfo.InvariantCulture),
                    FormatBytes(item.SizeBytes),
                    item.Status,
                    item.FilePath)
                : new CandleRow(
                    _store.IsNativeCandleVisible(_connectorId, symbol, code),
                    code,
                    "—",
                    "—",
                    "0",
                    "0 B",
                    "Not imported",
                    Path.Combine(FolderPathText.Text, code + ".tlc")))
            .ToArray();
    }

    private void RefreshAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedSymbol))
            return;
        OperationSymbol = SelectedSymbol!;
        RequestedAction = CandleHistoryWindowAction.RefreshAll;
        DialogResult = true;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        OpenFolder(_store.GetNativeCandleHistoryFolder(_connectorId, SelectedSymbol));
    }

    private void OpenSymbolFolder_Click(object sender, RoutedEventArgs e) => OpenFolderButton_Click(sender, e);

    private void OpenTimeframeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        string folder = Path.GetDirectoryName(SelectedRow.Path) ?? FolderPathText.Text;
        OpenFolder(folder);
    }

    private void CopySymbolFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        TryCopyFileDrop(_store.GetNativeCandleHistoryFolder(_connectorId, SelectedSymbol), "Candle history folder");
    }

    private void CopySymbolPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        CopyText(_store.GetNativeCandleHistoryFolder(_connectorId, SelectedSymbol), "Folder path copied.");
    }

    private void CopyTimeframeFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        TryCopyFileDrop(SelectedRow.Path, "Timeframe file");
    }

    private void CopyTimeframePath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null) return;
        CopyText(SelectedRow.Path, "Timeframe path copied.");
    }

    private void DeleteSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null) return;
        string path = _store.GetNativeCandleHistoryFolder(_connectorId, SelectedSymbol);
        if (!ConfirmDelete(
                $"Delete every permanent native candle timeframe for {SelectedSymbol}? Raw tick history will not be deleted.",
                path))
        {
            return;
        }
        try
        {
            _store.DeleteNativeCandleSymbol(_connectorId, SelectedSymbol);
            VisibilityChanged = true;
            StatusText.Text = $"Deleted candle history for {SelectedSymbol}.";
            RefreshSymbolRows(SelectedSymbol);
            RefreshRows();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Delete failed: {exception.Message}";
            ReportFailure(exception, "delete_symbol_history", path, SelectedSymbol);
        }
    }

    private void DeleteTimeframe_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null || SelectedRow is null)
            return;
        if (!File.Exists(SelectedRow.Path))
        {
            StatusText.Text = "That timeframe file does not exist.";
            return;
        }
        if (!ConfirmDelete(
                $"Delete all saved {SelectedSymbol} {SelectedRow.Timeframe} candles?",
                SelectedRow.Path))
        {
            return;
        }
        try
        {
            _store.DeleteNativeCandleTimeframe(_connectorId, SelectedSymbol, SelectedRow.Timeframe);
            VisibilityChanged = true;
            StatusText.Text = $"Deleted {SelectedSymbol} {SelectedRow.Timeframe}.";
            RefreshRows();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Delete failed: {exception.Message}";
            ReportFailure(exception, "delete_timeframe_history", SelectedRow.Path, SelectedRow.Timeframe);
        }
    }

    private void SymbolVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not string symbol)
            return;
        bool visible = checkBox.IsChecked == true;
        _store.SetAllNativeCandleVisibility(_connectorId, symbol, visible);
        VisibilityChanged = true;
        StatusText.Text = visible
            ? $"All saved {symbol} candle timeframes are enabled for display."
            : $"All saved {symbol} candle timeframes are hidden but remain on disk.";
        RefreshSymbolRows(symbol);
        RefreshRows();
        e.Handled = true;
    }

    private void TimeframeVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSymbol is null || sender is not CheckBox checkBox || checkBox.Tag is not string timeframe)
            return;
        bool visible = checkBox.IsChecked == true;
        _store.SetNativeCandleVisible(_connectorId, SelectedSymbol, timeframe, visible);
        VisibilityChanged = true;
        StatusText.Text = visible
            ? $"{SelectedSymbol} {timeframe} is enabled for display."
            : $"{SelectedSymbol} {timeframe} is hidden but its file remains saved.";
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        e.Handled = true;
    }

    private void ShowAllSymbols_Click(object sender, RoutedEventArgs e)
    {
        foreach (string symbol in _symbols)
            _store.SetAllNativeCandleVisibility(_connectorId, symbol, true);
        VisibilityChanged = true;
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        StatusText.Text = "All candle history folders are enabled for display.";
    }

    private void HideAllSymbols_Click(object sender, RoutedEventArgs e)
    {
        foreach (string symbol in _symbols)
            _store.SetAllNativeCandleVisibility(_connectorId, symbol, false);
        VisibilityChanged = true;
        RefreshSymbolRows(SelectedSymbol);
        RefreshRows();
        StatusText.Text = "All candle history folders are hidden. No files were deleted.";
    }

    private void SymbolItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void TimeframeRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private bool ConfirmDelete(string message, string target)
    {
        if (MessageBox.Show(this, message, "Permanent delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
        return new PinPromptWindow(target) { Owner = this }.ShowDialog() == true;
    }

    private void TryCopyFileDrop(string path, string description)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
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
            ReportFailure(exception, "copy_history_file_or_folder", path, SelectedRow?.Timeframe);
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
            ReportFailure(exception, "copy_history_path", text, SelectedRow?.Timeframe);
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
            ReportFailure(exception, "open_history_folder", path, SelectedRow?.Timeframe);
        }
    }

    private void ReportFailure(
        Exception exception,
        string stage,
        string? path,
        string? timeframe)
    {
        TickLabErrorEngine.Report(
            exception,
            new TickLabErrorContext(
                "Candle history",
                stage,
                "Copy diagnostics. Keep all other history files unchanged and retry only this action.",
                ErrorCode: "TL-HIST-FILE",
                Symbol: SelectedSymbol,
                Timeframe: timeframe,
                ConnectorId: _connectorId,
                FilePath: path),
            TickLabErrorSeverity.Error,
            this);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToConnections = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatTime(long unix) => unix <= 0
        ? "—"
        : DateTimeOffset.FromUnixTimeSeconds(unix).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed record SymbolRow(string Symbol, bool? IsVisible);

    private sealed record CandleRow(
        bool IsVisible,
        string Timeframe,
        string From,
        string To,
        string Records,
        string Size,
        string Status,
        string Path);
}
