using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Windows;

public enum ConnectionWindowAction
{
    None,
    Connect,
    ImportHistory,
    RefreshHistory,
    OpenCandleHistory,
    OpenTickHistory
}

public partial class ConnectionsWindow : Window
{
    private readonly Mt5FileBridgeClient _bridgeClient;
    private readonly string _mainChartSymbol;
    private readonly string _mainChartTimeframe;
    private readonly System.Windows.Threading.DispatcherTimer _autoDetectTimer;

    public ConnectionsWindow(
        Mt5FileBridgeClient bridgeClient,
        string mainChartSymbol,
        string mainChartTimeframe)
    {
        InitializeComponent();
        _bridgeClient = bridgeClient;
        _mainChartSymbol = mainChartSymbol;
        _mainChartTimeframe = mainChartTimeframe;
        ChartInstrumentText.Text = string.IsNullOrWhiteSpace(mainChartSymbol)
            ? "Main chart instrument: not selected"
            : $"Main chart instrument: {mainChartSymbol}  •  {mainChartTimeframe}";
        UpdateBridgeFolderText();

        _autoDetectTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autoDetectTimer.Tick += (_, _) => RefreshConnectors(quiet: true);

        Loaded += (_, _) =>
        {
            RefreshConnectors();
            _autoDetectTimer.Start();
        };
        Closed += (_, _) => _autoDetectTimer.Stop();
    }

    public Mt5ConnectorSummary? SelectedConnector { get; private set; }
    public ConnectionWindowAction RequestedAction { get; private set; }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        RefreshConnectors();

    private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        _bridgeClient.SetManualBridgeFolder(null);
        UpdateBridgeFolderText();
        RefreshConnectors();
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select MT5 Common Files, TickLab, Connections, or connector folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (!_bridgeClient.SetManualBridgeFolder(dialog.FolderName))
        {
            MessageBox.Show(
                this,
                "That folder could not be used. Select MT5 Common Files, the TickLab folder, the Connections folder, or a connector folder.",
                "Invalid bridge folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        UpdateBridgeFolderText();
        RefreshConnectors();
    }

    private void UpdateBridgeFolderText()
    {
        BridgeFolderText.Text = string.IsNullOrWhiteSpace(_bridgeClient.ManualConnectionsRoot)
            ? "Automatic detection is active"
            : _bridgeClient.ManualConnectionsRoot;
    }

    private void ConnectorsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        Mt5ConnectorSummary? connector =
            ConnectorsGrid.SelectedItem as Mt5ConnectorSummary;

        bool workerOnline = connector is not null &&
                            _bridgeClient.IsHistoryWorkerOnline(connector.ConnectorId);

        SelectedText.Text = connector is null
            ? "No supported TickLab bridge selected"
            : $"Selected: {connector.DisplayName} — Live Channel online {(workerOnline ? "• History Worker online" : "• History Worker offline")}";

        bool usable = connector?.CanConnect == true;
        ConnectButton.IsEnabled = usable;
        // Saved local history remains available even when MT5/bridge is offline.
        HistoryButton.IsEnabled = true;
        ImportHistoryButton.IsEnabled = usable && workerOnline && !string.IsNullOrWhiteSpace(_mainChartSymbol);
        RefreshHistoryButton.IsEnabled = true;
    }

    private void ConnectorsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        Complete(ConnectionWindowAction.Connect);

    private void ConnectButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ConnectionWindowAction.Connect);

    private void ImportHistoryButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ConnectionWindowAction.ImportHistory);

    private void RefreshHistoryButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ConnectionWindowAction.RefreshHistory);

    private void HistoryButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ConnectionWindowAction.OpenTickHistory);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = ConnectionWindowAction.None;
        DialogResult = false;
    }

    private void Complete(ConnectionWindowAction action)
    {
        if (action is ConnectionWindowAction.RefreshHistory or ConnectionWindowAction.OpenCandleHistory or ConnectionWindowAction.OpenTickHistory)
        {
            SelectedConnector = ConnectorsGrid.SelectedItem as Mt5ConnectorSummary;
            RequestedAction = action;
            DialogResult = true;
            return;
        }

        Mt5ConnectorSummary? connector =
            ConnectorsGrid.SelectedItem as Mt5ConnectorSummary;

        if (connector?.CanConnect != true)
            return;

        SelectedConnector = connector;
        RequestedAction = action;
        DialogResult = true;
    }

    private void RefreshConnectors(bool quiet = false)
    {
        string? selectedId =
            (ConnectorsGrid.SelectedItem as Mt5ConnectorSummary)?.ConnectorId;

        if (!quiet)
        {
            StatusText.Text = "Searching for a live TickLab heartbeat...";
            ConnectButton.IsEnabled = false;
            ImportHistoryButton.IsEnabled = false;
            RefreshHistoryButton.IsEnabled = true;
            HistoryButton.IsEnabled = true;
        }

        IReadOnlyList<Mt5ConnectorSummary> active =
            _bridgeClient.DiscoverConnectors()
                .Where(item => item.CanConnect)
                .OrderByDescending(item => item.UpdatedUnix)
                .ToArray();

        // A quiet background rescan never clears an already visible connector
        // for one transient filesystem miss. The main window performs the real
        // stable offline transition with its own grace period.
        if (quiet && active.Count == 0 && ConnectorsGrid.Items.Count > 0)
            return;

        ConnectorsGrid.ItemsSource = active;

        StatusText.Text = active.Count > 0
            ? active.Count == 1
                ? "1 TickLab connector detected. The selected row shows Live and History Worker status."
                : $"{active.Count} live MT5 bridges detected. The newest is selected."
            : string.IsNullOrWhiteSpace(_bridgeClient.ManualConnectionsRoot)
                ? "No live heartbeat found. Attach TickLabLiveBridge_V300 on one chart and TickLabHistoryBridge_V305 on a second chart."
                : "No live heartbeat found in the selected folder. Try Auto Detect or choose the correct MT5 Common Files folder.";

        Mt5ConnectorSummary? preferred = active.FirstOrDefault(item =>
            string.Equals(item.ConnectorId, selectedId, StringComparison.Ordinal))
            ?? active.FirstOrDefault();

        if (preferred is not null)
            ConnectorsGrid.SelectedItem = preferred;
        else
            SelectedText.Text = "No connection selected";
    }

}
