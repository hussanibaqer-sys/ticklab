using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TickLab.Gateway.FileBridge;
using TickLab.Core.Diagnostics;

namespace TickLab.Desktop.Windows;

public enum HistoryManagementAction
{
    None,
    ApplyDisplay,
    RefreshSavedHistory,
    RecheckLatest60Days
}

public partial class HistoryManagementWindow : Window
{
    private readonly PersistentHistoryStore _store;
    private readonly ExternalHistoryStore _externalStore;
    private readonly Mt5ConnectorSummary _connector;
    private readonly string _connectorId;
    private readonly IReadOnlyList<Mt5SymbolInfo> _availableSymbols;
    private readonly ObservableCollection<InstrumentRow> _instrumentRows = new();
    private readonly ObservableCollection<DatasetRow> _datasetRows = new();
    private readonly ObservableCollection<SegmentRow> _segmentRows = new();
    private readonly ObservableCollection<ExternalDatasetRow> _externalRows = new();
    private ExternalDatasetManifest? _selectedExternal;

    public HistoryManagementWindow(
        PersistentHistoryStore store,
        ExternalHistoryStore externalStore,
        Mt5ConnectorSummary connector,
        IReadOnlyList<Mt5SymbolInfo> availableSymbols,
        HistoryDisplayMode currentMode,
        IReadOnlyList<string> currentSegments)
    {
        InitializeComponent();
        _store = store;
        _externalStore = externalStore;
        _connector = connector;
        _connectorId = connector.ConnectorId;
        _availableSymbols = availableSymbols;
        ConnectionText.Text = $"Connection: {_connectorId}";
        InstrumentComboBox.ItemsSource = availableSymbols.Select(item => item.Name).ToArray();
        SavedInstrumentsGrid.ItemsSource = _instrumentRows;
        DatasetsGrid.ItemsSource = _datasetRows;
        SegmentsGrid.ItemsSource = _segmentRows;
        ExternalDatasetsGrid.ItemsSource = _externalRows;

        RecentRadioButton.IsChecked = currentMode == HistoryDisplayMode.RecentThreeMonths;
        SelectedRadioButton.IsChecked = currentMode == HistoryDisplayMode.SelectedSegments;
        AllRadioButton.IsChecked = currentMode == HistoryDisplayMode.AllSavedHistory;
        CurrentSelectedSegments = currentSegments.ToArray();

        Loaded += (_, _) => RefreshAll();
    }

    public HistoryLoadSelection? SelectedDisplay { get; private set; }
    public HistoryDatasetSummary? SelectedDataset { get; private set; }
    public HistoryManagementAction RequestedAction { get; private set; }
    public string OperationSymbol => SelectedDataset?.Symbol ?? InstrumentComboBox.Text.Trim();
    public bool ExternalDataChanged { get; private set; }
    private IReadOnlyList<string> CurrentSelectedSegments { get; }

    private void RefreshAll()
    {
        _instrumentRows.Clear();
        foreach (SavedInstrumentState item in _store.GetSavedInstruments(_connectorId))
            _instrumentRows.Add(new InstrumentRow(item.Symbol, item.Enabled ? "Saving" : "Paused"));

        _datasetRows.Clear();
        foreach (HistoryDatasetSummary item in _store.GetDatasets(_connectorId))
        {
            _datasetRows.Add(new DatasetRow(
                item,
                item.Symbol,
                item.Timeframe,
                item.RecordCount.ToString("N0", CultureInfo.InvariantCulture),
                FormatBytes(item.SizeBytes),
                item.Segments.Count));
        }

        if (_datasetRows.Count > 0)
            DatasetsGrid.SelectedIndex = 0;

        _externalRows.Clear();
        foreach (ExternalDatasetManifest item in _externalStore.GetDatasets(_connectorId))
        {
            string range = item.EarliestUnix > 0 && item.LatestUnix > 0
                ? $"{DateTimeOffset.FromUnixTimeSeconds(item.EarliestUnix):yyyy-MM-dd} → {DateTimeOffset.FromUnixTimeSeconds(item.LatestUnix):yyyy-MM-dd}"
                : "Unknown";
            string status = item.Enabled
                ? item.SourceMatchesBroker && item.TimeZoneVerified
                    ? "Enabled · verified"
                    : "Enabled · external"
                : "Disabled";
            _externalRows.Add(new ExternalDatasetRow(
                item, item.DisplayName, item.Symbol,
                item.Kind == ExternalDataKind.RawTicks ? "Raw ticks" : "M1 candles",
                item.SourceName, range,
                item.AcceptedRecords.ToString("N0", CultureInfo.InvariantCulture),
                item.Priority, status));
        }

        if (_externalRows.Count > 0)
            ExternalDatasetsGrid.SelectedIndex = 0;
    }

    private void AddInstrumentButton_Click(object sender, RoutedEventArgs e)
    {
        string symbol = InstrumentComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        bool exists = _availableSymbols.Count == 0 ||
                      _availableSymbols.Any(item => string.Equals(
                          item.Name,
                          symbol,
                          StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            MessageBox.Show(this, "This instrument was not found in the connected MT5 terminal.",
                "Instrument not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store.SetInstrumentSaving(_connectorId, symbol, true);
        StatusText.Text = $"{symbol} added. Saving resumes whenever this instrument is active on the TickLab chart.";
        RefreshAll();
    }

    private void RemoveSavingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string symbol)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Stop saving new history for {symbol}? Already saved history will remain.",
            "Remove from saving mode",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        _store.SetInstrumentSaving(_connectorId, symbol, false);
        RefreshAll();
    }

    private void DatasetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DatasetsGrid.SelectedItem is not DatasetRow row)
            return;

        SelectedDataset = row.Source;
        DatasetTitleText.Text = $"{row.Symbol}  •  {row.Timeframe}";
        _segmentRows.Clear();

        foreach (HistorySegmentSummary segment in row.Source.Segments)
        {
            _segmentRows.Add(new SegmentRow(
                segment.Key,
                DateTimeOffset.FromUnixTimeSeconds(segment.EarliestUnix).ToString("yyyy-MM-dd"),
                DateTimeOffset.FromUnixTimeSeconds(segment.LatestUnix).ToString("yyyy-MM-dd"),
                FormatBytes(segment.SizeBytes),
                segment.Status,
                CurrentSelectedSegments.Contains(segment.Key, StringComparer.OrdinalIgnoreCase)));
        }

        SummaryText.Text = $"{row.Records} records  •  {row.Size}";
    }

    private void DisplayModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (SegmentsGrid is not null)
            SegmentsGrid.IsEnabled = SelectedRadioButton.IsChecked == true;
    }

    private void ApplyDisplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDataset is null)
        {
            StatusText.Text = "Select a saved dataset first.";
            return;
        }

        if (AllRadioButton.IsChecked == true)
        {
            SelectedDisplay = HistoryLoadSelection.All;
        }
        else if (SelectedRadioButton.IsChecked == true)
        {
            string[] selected = _segmentRows
                .Where(item => item.IsSelected)
                .Select(item => item.Key)
                .ToArray();

            if (selected.Length == 0)
            {
                MessageBox.Show(this, "Select at least one history folder.",
                    "No folders selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedDisplay = new HistoryLoadSelection(
                HistoryDisplayMode.SelectedSegments,
                selected);
        }
        else
        {
            SelectedDisplay = HistoryLoadSelection.RecentThreeMonths;
        }

        RequestedAction = HistoryManagementAction.ApplyDisplay;
        DialogResult = true;
    }

    private void RefreshSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OperationSymbol))
        {
            StatusText.Text = "Select a saved instrument or dataset first.";
            return;
        }

        RequestedAction = HistoryManagementAction.RefreshSavedHistory;
        DialogResult = true;
    }

    private void RecheckHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OperationSymbol))
        {
            StatusText.Text = "Select a saved instrument or dataset first.";
            return;
        }

        RequestedAction = HistoryManagementAction.RecheckLatest60Days;
        DialogResult = true;
    }

    private void DeleteSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDataset is null || SegmentsGrid.SelectedItem is not SegmentRow row)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Permanently delete history folder {row.Key}? This cannot be undone.",
            "Delete history folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _store.DeleteSegment(
            _connectorId,
            SelectedDataset.Symbol,
            SelectedDataset.Timeframe,
            row.Key);
        RefreshAll();
    }

    private void DeleteDatasetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDataset is null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Permanently delete all saved {SelectedDataset.Symbol} {SelectedDataset.Timeframe} history?",
            "Delete dataset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _store.DeleteDataset(
            _connectorId,
            SelectedDataset.Symbol,
            SelectedDataset.Timeframe);
        RefreshAll();
    }

    private void ExternalDatasetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExternalDatasetsGrid.SelectedItem is not ExternalDatasetRow row)
            return;

        _selectedExternal = row.SourceManifest;
        WholePriorityTextBox.Text = row.Priority.ToString(CultureInfo.InvariantCulture);
        RangePriorityTextBox.Text = row.Priority.ToString(CultureInfo.InvariantCulture);
        ToggleExternalButton.Content = row.SourceManifest.Enabled ? "Disable" : "Enable";
    }

    private void OpenExternalFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = _externalStore.GetConnectorExternalFolder(_connectorId);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private async void ImportExternalButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Import M1 candle or raw tick history",
            Filter = "History files (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(this) != true)
            return;

        string suggestedSymbol = !string.IsNullOrWhiteSpace(InstrumentComboBox.Text)
            ? InstrumentComboBox.Text.Trim()
            : _selectedExternal?.Symbol ?? string.Empty;
        var optionsWindow = new ExternalImportWindow(picker.FileName, _connector, suggestedSymbol)
        {
            Owner = this
        };

        if (optionsWindow.ShowDialog() != true)
            return;
        ExternalImportOptions? options = optionsWindow.Options;
        if (options is null)
            return;

        try
        {
            IsEnabled = false;
            StatusText.Text = "Validating and importing external history...";
            ExternalImportResult result = await Task.Run(() =>
                _externalStore.ImportDelimitedFile(picker.FileName, options));
            ExternalDataChanged = true;
            StatusText.Text = result.Message;
            RefreshAll();
        }
        catch (Exception exception)
        {
            TickLabErrorEngine.Report(
                exception,
                new TickLabErrorContext(
                    "External history import",
                    "validate_and_store_file",
                    "Copy diagnostics. Keep the source file unchanged, correct the reported format or access problem, then retry the import.",
                    ErrorCode: "TL-EXT-IMPORT",
                    Symbol: optionsWindow.Options.Symbol,
                    ConnectorId: _connectorId,
                    FilePath: picker.FileName),
                TickLabErrorSeverity.Error,
                this);
            StatusText.Text = "External history import paused. See the error window for exact details.";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void RescanExternalButton_Click(object sender, RoutedEventArgs e)
    {
        int rebound = _externalStore.RescanPortableDatasets(_connectorId);
        RefreshAll();
        ExternalDataChanged = true;
        StatusText.Text = rebound > 0
            ? $"Attached {rebound:N0} pasted external dataset(s) to this MT5 connection."
            : "External history folder rescanned.";
    }

    private void ToggleExternalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExternal is null)
            return;
        _externalStore.SetEnabled(_selectedExternal.DatasetId, !_selectedExternal.Enabled);
        ExternalDataChanged = true;
        RefreshAll();
    }

    private void ApplyWholePriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExternal is null ||
            !int.TryParse(WholePriorityTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority))
        {
            StatusText.Text = "Select an external dataset and enter an integer priority.";
            return;
        }

        _externalStore.SetPriority(_selectedExternal.DatasetId, priority);
        ExternalDataChanged = true;
        StatusText.Text = "Whole-dataset priority updated.";
        RefreshAll();
    }

    private void ApplyRangePriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExternal is null ||
            PriorityStartDatePicker.SelectedDate is not DateTime start ||
            PriorityEndDatePicker.SelectedDate is not DateTime end ||
            !int.TryParse(RangePriorityTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority))
        {
            StatusText.Text = "Select a dataset, start/end dates and an integer range priority.";
            return;
        }

        if (end.Date < start.Date)
        {
            StatusText.Text = "Range end date must not be before its start date.";
            return;
        }

        // External datasets are normalized into MT5 server-encoded wall-clock
        // seconds. Treat the selected date components as that same clock
        // domain instead of converting them back through a UTC offset.
        long startUnix = new DateTimeOffset(
                DateTime.SpecifyKind(start.Date, DateTimeKind.Utc))
            .ToUnixTimeSeconds();
        long endUnix = new DateTimeOffset(
                DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc))
            .ToUnixTimeSeconds();
        _externalStore.SetPriorityRule(_selectedExternal.DatasetId, startUnix, endUnix, priority);
        ExternalDataChanged = true;
        StatusText.Text = "Date-range priority updated.";
        RefreshAll();
    }

    private void ClearRangePriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExternal is null)
            return;
        _externalStore.ClearPriorityRules(_selectedExternal.DatasetId);
        ExternalDataChanged = true;
        StatusText.Text = "Date-range priority rules cleared.";
        RefreshAll();
    }

    private void DeleteExternalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExternal is null)
            return;

        MessageBoxResult answer = MessageBox.Show(this,
            $"Delete external dataset '{_selectedExternal.DisplayName}'? Permanent MT5 history will not be affected.",
            "Delete external dataset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _externalStore.DeleteDataset(_selectedExternal.DatasetId);
        _selectedExternal = null;
        ExternalDataChanged = true;
        RefreshAll();
    }

    private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            "Permanently delete all saved TickLab history for this MT5 connection? This cannot be undone.",
            "Delete all history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _store.DeleteAll(_connectorId);
        RefreshAll();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = _store.GetConnectorHistoryFolder(_connectorId);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void RescanFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        PortableHistoryScanResult result = _store.RescanPortableHistory(_connectorId);
        RefreshAll();
        MessageBox.Show(
            this,
            result.DiscoveredInstruments > 0
                ? $"Found and enabled {result.DiscoveredInstruments:N0} pasted history instrument(s)."
                : "No new valid M1 or canonical-tick history folders were found.",
            "History folder rescan",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed record InstrumentRow(string Symbol, string State);

    private sealed record DatasetRow(
        HistoryDatasetSummary Source,
        string Symbol,
        string Timeframe,
        string Records,
        string Size,
        int FolderCount);

    private sealed record ExternalDatasetRow(
        ExternalDatasetManifest SourceManifest,
        string Name,
        string Symbol,
        string Kind,
        string Source,
        string Range,
        string Records,
        int Priority,
        string Status);

    private sealed class SegmentRow
    {
        public SegmentRow(
            string key,
            string from,
            string to,
            string size,
            string status,
            bool isSelected)
        {
            Key = key;
            From = from;
            To = to;
            Size = size;
            Status = status;
            IsSelected = isSelected;
        }

        public string Key { get; }
        public string From { get; }
        public string To { get; }
        public string Size { get; }
        public string Status { get; }
        public bool IsSelected { get; set; }
    }
}
