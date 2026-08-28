using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TickLab.Core.Market;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private const double DemoInitialBalance = 1_000_000.0;
    private readonly ObservableCollection<DemoPosition> _demoOpenPositions = new();
    private readonly ObservableCollection<DemoPendingOrder> _demoPendingOrders = new();
    private readonly ObservableCollection<DemoTradeRecord> _demoTradeHistory = new();
    private readonly ObservableCollection<Mt5TradeHistoryFileEntry> _mt5TradeHistoryFiles = new();
    private readonly JsonSerializerOptions _demoJsonOptions = new() { WriteIndented = true };
    private DispatcherTimer? _demoTradeTimer;
    private DemoAccountDocument _demoAccount = new();
    private int? _demoSlPresetPoints;
    private int? _demoTpPresetPoints;
    private int? _demoEditingPendingOrderId;
    private Point _demoSlideDragStart;
    private bool _demoSlideDragging;
    private bool _demoSlideMoved;
    private double _demoSlideStartPanelWidth;
    private bool _demoPanelOpen;
    private bool _demoTradingInitialized;
    private bool _refreshingMt5HistoryLibrary;
    private bool _historyProjectionRefreshQueued;
    private const double DemoPreferredPanelWidth = 647.0;
    private const double DemoHandleWidth = 19.0;

    private static string DemoTradingFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TickLab",
        "DemoTrading");

    private static string DemoTradingPath => Path.Combine(DemoTradingFolder, "demo-account.json");

    private void InitializeDemoTrading()
    {
        LoadDemoTradingState();
        DemoOpenPositionsGrid.ItemsSource = _demoOpenPositions;
        DemoPendingOrdersGrid.ItemsSource = _demoPendingOrders;
        DemoTradeHistoryGrid.ItemsSource = _demoTradeHistory;
        DemoMt5TradeHistoryList.ItemsSource = _mt5TradeHistoryFiles;
        RefreshMt5TradeHistoryLibrary();
        DemoShowHistoryOnChartCheckBox.IsChecked = _demoAccount.ShowHistoryOnChart;
        DemoOrderTypeCombo.ItemsSource = DemoPendingOrderTypes;
        DemoOrderTypeCombo.SelectedIndex = 0;
        DemoExpirationModeCombo.ItemsSource = new[] { "Good till cancelled", "Specified time" };
        DemoExpirationModeCombo.SelectedIndex = 0;
        DemoExpirationDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        UpdateDemoPresetVisuals();
        SetDemoPanelWidth(0, updateOpenState: true);
        SizeChanged += (_, _) => ClampDemoPanelToWindow();
        _demoTradeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _demoTradeTimer.Tick += (_, _) => RefreshDemoTradingMarket();
        _demoTradeTimer.Start();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        UpdateDemoOrderPreview();
        _demoTradingInitialized = true;
        _ = EnsureProjectedMt5ReportsLoadedAsync();
    }

    private void ShutdownDemoTrading()
    {
        _demoTradeTimer?.Stop();
        SaveDemoTradingState();
    }

    private static readonly string[] DemoPendingOrderTypes =
    {
        "Buy Limit", "Sell Limit", "Buy Stop", "Sell Stop", "Buy Stop Limit", "Sell Stop Limit"
    };

    private double GetDemoPanelMaximumWidth()
    {
        double available = Math.Max(80.0, ActualWidth - DemoHandleWidth - 36.0);
        return Math.Min(DemoPreferredPanelWidth, available);
    }

    private void ClampDemoPanelToWindow()
    {
        if (DemoTradePanel is null)
            return;
        if (_demoPanelOpen)
            SetDemoPanelWidth(GetDemoPanelMaximumWidth(), updateOpenState: false);
        else
            SetDemoPanelWidth(0, updateOpenState: false);
    }

    private void SetDemoPanelWidth(double width, bool updateOpenState)
    {
        if (DemoTradePanel is null)
            return;
        double maximum = GetDemoPanelMaximumWidth();
        double clamped = Math.Clamp(width, 0, maximum);
        bool visible = clamped >= 1.0;
        DemoTradeDock.Width = clamped;
        DemoTradeDock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DemoTradePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DemoTradePanel.Width = double.NaN;
        DemoTradePanel.IsHitTestVisible = clamped >= 40;
        DemoTradeColumn.Width = new GridLength(clamped);
        if (updateOpenState)
            _demoPanelOpen = clamped >= maximum * 0.45;
        DemoTradeSlideButton.ToolTip = clamped >= maximum * 0.45
            ? "Click or drag right to close Demo Trading"
            : "Click or drag left to open Demo Trading";
    }

    private void SetDemoPanelOpen(bool open)
    {
        _demoPanelOpen = open;
        SetDemoPanelWidth(open ? GetDemoPanelMaximumWidth() : 0, updateOpenState: false);
        if (open)
        {
            RefreshDemoTradingMarket();
            RefreshDemoTradingUi();
            UpdateDemoOrderPreview();
            RefreshMt5TradeHistoryLibrary();
            _ = EnsureProjectedMt5ReportsLoadedAsync();
        }
    }

    private void DemoTradeSlideButton_Click(object sender, RoutedEventArgs e)
    {
        if (_demoSlideMoved)
            return;
        SetDemoPanelOpen(!_demoPanelOpen);
    }

    private void DemoTradeSlideButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, DemoTradeSlideButton) || e.ChangedButton != MouseButton.Left)
            return;
        CancelOtherRightHandleInteractions(DemoTradeSlideButton);
        _demoSlideDragging = true;
        _demoSlideMoved = false;
        _demoSlideDragStart = e.GetPosition(this);
        _demoSlideStartPanelWidth = DemoTradePanel.Visibility == Visibility.Visible
            ? Math.Max(0.0, DemoTradeColumn.ActualWidth)
            : 0.0;
    }

    private void DemoTradeSlideButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_demoSlideDragging || sender is not UIElement handle)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDemoTradeHandleInteraction(handle);
            return;
        }

        double deltaX = e.GetPosition(this).X - _demoSlideDragStart.X;
        if (!_demoSlideMoved && Math.Abs(deltaX) >= SystemParameters.MinimumHorizontalDragDistance)
        {
            _demoSlideMoved = true;
            Mouse.Capture(handle, CaptureMode.Element);
        }
        if (!_demoSlideMoved)
            return;

        SetDemoPanelWidth(_demoSlideStartPanelWidth - deltaX, updateOpenState: false);
        e.Handled = true;
    }

    private void DemoTradeSlideButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_demoSlideDragging || sender is not UIElement handle || e.ChangedButton != MouseButton.Left)
            return;
        if (_demoSlideMoved)
        {
            FinishDemoTradeHandleInteraction(handle);
            e.Handled = true;
        }
        else
        {
            // Leave an ordinary click unhandled so Button.Click performs the
            // toggle exactly once. Preview handlers are reserved for dragging.
            _demoSlideDragging = false;
            _demoSlideMoved = false;
        }
    }

    private void DemoTradeSlideButton_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_demoSlideDragging && _demoSlideMoved)
            FinishDemoTradeHandleInteraction(sender as UIElement);
        else
        {
            _demoSlideDragging = false;
            _demoSlideMoved = false;
        }
    }

    private void FinishDemoTradeHandleInteraction(UIElement? handle)
    {
        _demoSlideDragging = false;
        bool moved = _demoSlideMoved;
        _demoSlideMoved = false;
        if (handle?.IsMouseCaptured == true)
            Mouse.Capture(null);
        if (!moved)
            return;

        double maximum = GetDemoPanelMaximumWidth();
        SetDemoPanelOpen(DemoTradeColumn.ActualWidth >= maximum * 0.35);
    }

    private void DemoTradeSlideButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            SetDemoPanelOpen(!_demoPanelOpen);
            e.Handled = true;
        }
    }

    private void DemoTradeCloseButton_Click(object sender, RoutedEventArgs e) => SetDemoPanelOpen(false);

    private void DemoShowHistoryOnChartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // XAML can raise Checked while InitializeComponent is still constructing
        // controls that appear later in the file. Do not mutate or save the demo
        // account until LoadDemoTradingState and the complete demo UI are ready.
        if (!_demoTradingInitialized || !IsInitialized || DemoShowHistoryOnChartCheckBox is null)
            return;
        _demoAccount.ShowHistoryOnChart = DemoShowHistoryOnChartCheckBox.IsChecked == true;
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        SetDemoStatus(_demoAccount.ShowHistoryOnChart
            ? "Saved trade history is visible on all matching-symbol charts."
            : "Saved trade history is hidden from charts.", false);
    }

    private void DemoTradeOpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        var report = new TradeHistoryReportData
        {
            Name = "TickLab Demo Trading History",
            AccountName = "TickLab Demo",
            Currency = "USD",
            StartingBalance = DemoInitialBalance,
            EndingBalance = _demoAccount.Balance
        };
        foreach (DemoTradeRecord trade in _demoTradeHistory.OrderBy(item => item.ClosedUtc))
        {
            report.Trades.Add(new TradeHistoryTrade
            {
                Ticket = trade.Id.ToString(CultureInfo.InvariantCulture),
                Symbol = trade.Symbol,
                Direction = trade.Direction,
                Volume = trade.Volume,
                OpenTime = trade.OpenedUtc,
                CloseTime = trade.ClosedUtc,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                StopLoss = trade.StopLoss,
                TakeProfit = trade.TakeProfit,
                Profit = trade.Profit,
                CloseReason = trade.CloseReason,
                Comment = trade.CloseReason
            });
        }
        var window = new TradeReportWindow(report) { Owner = this };
        window.Show();
    }

    private void DemoMt5HistoryOpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Mt5TradeHistoryService.HistoryFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Mt5TradeHistoryService.HistoryFolder}\"") { UseShellExecute = true });
            DemoMt5HistoryStatusText.Text = "MT5 Trade History folder opened. Paste an MT5 HTML, CSV, TSV or TXT history/report file there, then press Refresh.";
        }
        catch (Exception ex)
        {
            DemoMt5HistoryStatusText.Text = $"Could not open MT5 history folder: {ex.Message}";
        }
    }

    private void DemoMt5HistoryRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMt5TradeHistoryLibrary();
        _ = EnsureProjectedMt5ReportsLoadedAsync();
    }

    private async void DemoMt5HistoryProjectionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_demoTradingInitialized || _refreshingMt5HistoryLibrary || sender is not CheckBox checkBox || checkBox.DataContext is not Mt5TradeHistoryFileEntry entry)
            return;
        entry.IsProjected = checkBox.IsChecked == true;
        Mt5TradeHistoryService.SaveProjectedPaths(_mt5TradeHistoryFiles);
        if (entry.IsProjected && entry.Report is null)
        {
            DemoMt5HistoryStatusText.Text = $"Loading {entry.Name} for chart projection…";
            TradeHistoryReportData parsed = await Task.Run(() => Mt5TradeHistoryService.ParseFile(entry.FilePath));
            if (!_mt5TradeHistoryFiles.Contains(entry)) return;
            entry.Report = parsed;
        }
        RefreshDemoTradeLines();
        DemoMt5HistoryStatusText.Text = entry.IsProjected
            ? $"{entry.Name}: projection ON ({(entry.Report?.Trades.Count ?? 0):N0} completed trades recognized)."
            : $"{entry.Name}: projection OFF.";
    }

    private async void DemoMt5HistoryOpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Mt5TradeHistoryFileEntry entry }) return;
        if (entry.Report is null)
        {
            DemoMt5HistoryStatusText.Text = $"Reading {entry.Name}…";
            entry.Report = await Task.Run(() => Mt5TradeHistoryService.ParseFile(entry.FilePath));
        }
        if (entry.Report.Trades.Count == 0 && entry.Report.CashFlows.Count == 0)
        {
            MessageBox.Show(this,
                $"TickLab could not recognize completed trades or balance operations in this file.\n\n{entry.Report.ParseNote}\n\nSupported input: standard MT5 HTML reports and copied/exported CSV, TSV or TXT trade-history tables.",
                "MT5 Trade History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DemoMt5HistoryStatusText.Text = $"Opened report: {entry.Name}.";
        new TradeReportWindow(entry.Report) { Owner = this }.Show();
    }

    private void RefreshMt5TradeHistoryLibrary()
    {
        Dictionary<string, Mt5TradeHistoryFileEntry> previous = _mt5TradeHistoryFiles
            .ToDictionary(item => item.FilePath, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<Mt5TradeHistoryFileEntry> scanned = Mt5TradeHistoryService.ScanFolder();
        _refreshingMt5HistoryLibrary = true;
        try
        {
            _mt5TradeHistoryFiles.Clear();
            foreach (Mt5TradeHistoryFileEntry item in scanned)
            {
                if (previous.TryGetValue(item.FilePath, out Mt5TradeHistoryFileEntry? old) && old.ModifiedUtc == item.ModifiedUtc && old.SizeBytes == item.SizeBytes)
                    item.Report = old.Report;
                _mt5TradeHistoryFiles.Add(item);
            }
        }
        finally
        {
            _refreshingMt5HistoryLibrary = false;
        }
        if (DemoMt5HistoryStatusText is not null)
            DemoMt5HistoryStatusText.Text = _mt5TradeHistoryFiles.Count == 0
                ? $"No MT5 history files found. Use Open Folder and paste files into {Mt5TradeHistoryService.HistoryFolder}."
                : $"{_mt5TradeHistoryFiles.Count:N0} MT5 history file(s) found. Tick Project on chart or press Open Report.";
    }

    private async Task EnsureProjectedMt5ReportsLoadedAsync()
    {
        Mt5TradeHistoryFileEntry[] targets = _mt5TradeHistoryFiles.Where(item => item.IsProjected && item.Report is null && File.Exists(item.FilePath)).ToArray();
        if (targets.Length == 0) return;
        foreach (Mt5TradeHistoryFileEntry entry in targets)
        {
            TradeHistoryReportData parsed = await Task.Run(() => Mt5TradeHistoryService.ParseFile(entry.FilePath));
            if (_mt5TradeHistoryFiles.Contains(entry)) entry.Report = parsed;
        }
        RefreshDemoTradeLines();
    }

    private void DemoOrderInput_Changed(object? sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateDemoOrderPreview();
    }

    private void DemoSlPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text })
            _demoSlPresetPoints = string.Equals(text, "NONE", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(text, CultureInfo.InvariantCulture);
        DemoManualSlBox.Clear();
        UpdateDemoPresetVisuals();
        UpdateDemoOrderPreview();
    }

    private void DemoTpPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text })
            _demoTpPresetPoints = string.Equals(text, "NONE", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(text, CultureInfo.InvariantCulture);
        DemoManualTpBox.Clear();
        UpdateDemoPresetVisuals();
        UpdateDemoOrderPreview();
    }

    private void UpdateDemoPresetVisuals()
    {
        UpdatePresetPanel(DemoSlPresetPanel, _demoSlPresetPoints);
        UpdatePresetPanel(DemoTpPresetPanel, _demoTpPresetPoints);
        DemoSelectedSlText.Text = _demoSlPresetPoints.HasValue ? $"SL: {_demoSlPresetPoints.Value} points" : "SL: none";
        DemoSelectedTpText.Text = _demoTpPresetPoints.HasValue ? $"TP: {_demoTpPresetPoints.Value} points" : "TP: none";
    }

    private static void UpdatePresetPanel(Panel panel, int? selected)
    {
        foreach (Button button in panel.Children.OfType<Button>())
        {
            string tag = button.Tag as string ?? string.Empty;
            bool isSelected = selected.HasValue
                ? int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value == selected.Value
                : string.Equals(tag, "NONE", StringComparison.OrdinalIgnoreCase);
            button.Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(23, 63, 112))
                : Brushes.White;
            button.Foreground = isSelected ? Brushes.White : Brushes.Black;
            button.FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal;
            button.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(13, 44, 82))
                : new SolidColorBrush(Color.FromRgb(145, 156, 170));
        }
    }

    private void DemoBuyButton_Click(object sender, RoutedEventArgs e) => OpenDemoPosition("BUY");
    private void DemoSellButton_Click(object sender, RoutedEventArgs e) => OpenDemoPosition("SELL");

    private void OpenDemoPosition(string direction)
    {
        if (!TryGetActiveDemoMarket(out DemoMarketSnapshot market))
        {
            SetDemoStatus("Select a chart with live market data first.", true);
            return;
        }
        if (!TryReadDemoVolume(out double volume, out string volumeError))
        {
            SetDemoStatus(volumeError, true);
            return;
        }

        double entry = direction == "BUY" ? market.Ask : market.Bid;
        if (!TryResolveDemoLevels(direction, entry, market, out double sl, out double tp, out string levelError))
        {
            SetDemoStatus(levelError, true);
            return;
        }

        DemoPosition position = CreateDemoPosition(direction, volume, entry, sl, tp, market);
        _demoOpenPositions.Add(position);
        DemoOpenPositionsGrid.SelectedItem = position;
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        SetDemoStatus($"Market {direction} #{position.Id} executed at {position.EntryPrice.ToString($"F{position.Digits}", CultureInfo.InvariantCulture)}.", false);
    }

    private DemoPosition CreateDemoPosition(string direction, double volume, double entry, double sl, double tp, DemoMarketSnapshot market)
    {
        DemoPosition position = new()
        {
            Id = _demoAccount.NextTradeId++,
            ChartPaneId = market.PaneId,
            Symbol = market.Symbol,
            Timeframe = market.Timeframe,
            Direction = direction,
            Volume = volume,
            EntryPrice = RoundPrice(entry, market.Digits),
            OpenBid = RoundPrice(market.Bid, market.Digits),
            OpenAsk = RoundPrice(market.Ask, market.Digits),
            CurrentBid = RoundPrice(market.Bid, market.Digits),
            CurrentAsk = RoundPrice(market.Ask, market.Digits),
            CurrentPrice = RoundPrice(direction == "BUY" ? market.Bid : market.Ask, market.Digits),
            StopLoss = sl > 0 ? RoundPrice(sl, market.Digits) : 0,
            TakeProfit = tp > 0 ? RoundPrice(tp, market.Digits) : 0,
            Point = market.Point,
            TickSize = market.TickSize,
            TickValuePerLot = market.TickValuePerLot,
            Digits = market.Digits,
            ContractSize = market.ContractSize,
            OpenedUtc = DateTime.UtcNow,
            OpenedServerUnix = Mt5ServerClock.ServerNowUnix(GetDemoServerUtcOffsetMinutes())
        };
        position.InitializeEditValues();
        position.RecalculateProfit();
        return position;
    }

    private bool TryReadDemoVolume(out double volume, out string error)
    {
        error = string.Empty;
        if (!double.TryParse(DemoLotBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out volume) || volume <= 0 || volume > 1000)
        {
            error = "Enter a valid demo lot size greater than 0 and no more than 1000.";
            return false;
        }
        volume = Math.Round(volume, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private bool TryResolveDemoLevels(string direction, double referenceEntry, DemoMarketSnapshot market, out double stopLoss, out double takeProfit, out string error)
    {
        error = string.Empty;
        stopLoss = 0;
        takeProfit = 0;

        if (!string.IsNullOrWhiteSpace(DemoManualSlBox.Text))
        {
            if (!double.TryParse(DemoManualSlBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double manualSl) || manualSl <= 0)
            {
                error = "Enter a valid exact SL price or clear the field to use the selected preset.";
                return false;
            }
            stopLoss = manualSl;
        }
        else if (_demoSlPresetPoints.HasValue)
        {
            stopLoss = direction == "BUY" ? referenceEntry - _demoSlPresetPoints.Value * market.Point : referenceEntry + _demoSlPresetPoints.Value * market.Point;
        }

        if (!string.IsNullOrWhiteSpace(DemoManualTpBox.Text))
        {
            if (!double.TryParse(DemoManualTpBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double manualTp) || manualTp <= 0)
            {
                error = "Enter a valid exact TP price or clear the field to use the selected preset.";
                return false;
            }
            takeProfit = manualTp;
        }
        else if (_demoTpPresetPoints.HasValue)
        {
            takeProfit = direction == "BUY" ? referenceEntry + _demoTpPresetPoints.Value * market.Point : referenceEntry - _demoTpPresetPoints.Value * market.Point;
        }

        stopLoss = stopLoss > 0 ? RoundPrice(stopLoss, market.Digits) : 0;
        takeProfit = takeProfit > 0 ? RoundPrice(takeProfit, market.Digits) : 0;

        if (direction == "BUY")
        {
            if (stopLoss > 0 && stopLoss >= referenceEntry) { error = "Buy SL must be below the entry price."; return false; }
            if (takeProfit > 0 && takeProfit <= referenceEntry) { error = "Buy TP must be above the entry price."; return false; }
        }
        else
        {
            if (stopLoss > 0 && stopLoss <= referenceEntry) { error = "Sell SL must be above the entry price."; return false; }
            if (takeProfit > 0 && takeProfit >= referenceEntry) { error = "Sell TP must be below the entry price."; return false; }
        }
        return true;
    }

    private void UpdateDemoOrderPreview()
    {
        if (DemoOrderPreviewText is null)
            return;
        if (!TryGetActiveDemoMarket(out DemoMarketSnapshot market))
        {
            DemoOrderPreviewText.Text = "Select a chart to preview market and pending-order prices.";
            return;
        }

        string direction = (DemoOrderTypeCombo.SelectedItem as string)?.StartsWith("Sell", StringComparison.OrdinalIgnoreCase) == true ? "SELL" : "BUY";
        double marketEntry = direction == "BUY" ? market.Ask : market.Bid;
        double referenceEntry = marketEntry;
        if (double.TryParse(DemoPendingEntryBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pendingEntry) && pendingEntry > 0)
            referenceEntry = pendingEntry;
        _ = TryResolveDemoLevels(direction, referenceEntry, market, out double sl, out double tp, out _);
        string f = $"F{market.Digits}";
        DemoOrderPreviewText.Text =
            $"Bid {market.Bid.ToString(f, CultureInfo.InvariantCulture)} · Ask {market.Ask.ToString(f, CultureInfo.InvariantCulture)} · " +
            $"Entry {referenceEntry.ToString(f, CultureInfo.InvariantCulture)} · SL {(sl > 0 ? sl.ToString(f, CultureInfo.InvariantCulture) : "none")} · TP {(tp > 0 ? tp.ToString(f, CultureInfo.InvariantCulture) : "none")}";
    }

    private void DemoPlacePendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveDemoMarket(out DemoMarketSnapshot market))
        {
            SetDemoStatus("Select a chart with live market data first.", true);
            return;
        }
        if (!TryReadDemoVolume(out double volume, out string volumeError))
        {
            SetDemoStatus(volumeError, true);
            return;
        }
        string orderType = DemoOrderTypeCombo.SelectedItem as string ?? "Buy Limit";
        if (!double.TryParse(DemoPendingEntryBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double entry) || entry <= 0)
        {
            SetDemoStatus("Enter a valid pending-order entry price.", true);
            return;
        }
        double stopLimit = 0;
        if (orderType.Contains("Stop Limit", StringComparison.OrdinalIgnoreCase) &&
            (!double.TryParse(DemoStopLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out stopLimit) || stopLimit <= 0))
        {
            SetDemoStatus("A Stop Limit order requires a valid stop-limit price.", true);
            return;
        }
        if (!ValidatePendingPlacement(orderType, entry, stopLimit, market, out string placementError))
        {
            SetDemoStatus(placementError, true);
            return;
        }
        string direction = orderType.StartsWith("Buy", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
        double plannedExecution = stopLimit > 0 ? stopLimit : entry;
        if (!TryResolveDemoLevels(direction, plannedExecution, market, out double sl, out double tp, out string levelError))
        {
            SetDemoStatus(levelError, true);
            return;
        }
        DateTime? expiration = ResolveDemoExpiration(out string expirationError);
        if (!string.IsNullOrEmpty(expirationError))
        {
            SetDemoStatus(expirationError, true);
            return;
        }

        DemoPendingOrder? existingOrder = _demoEditingPendingOrderId.HasValue
            ? _demoPendingOrders.FirstOrDefault(item => item.Id == _demoEditingPendingOrderId.Value)
            : null;
        if (existingOrder is not null)
        {
            DemoPendingOrder order = existingOrder;
            order.OrderType = orderType;
            order.ChartPaneId = market.PaneId;
            order.Symbol = market.Symbol;
            order.Timeframe = market.Timeframe;
            order.Volume = volume;
            order.EntryPrice = RoundPrice(entry, market.Digits);
            order.StopLimitPrice = stopLimit > 0 ? RoundPrice(stopLimit, market.Digits) : 0;
            order.StopLoss = sl;
            order.TakeProfit = tp;
            order.ExpirationUtc = expiration?.ToUniversalTime();
            order.Point = market.Point;
            order.Digits = market.Digits;
            order.IsStopLimitActivated = false;
            order.Status = "Waiting";
            order.NotifyAll();
            SetDemoStatus($"Pending order #{order.Id} modified.", false);
        }
        else
        {
            DemoPendingOrder order = new()
            {
                Id = _demoAccount.NextOrderId++,
                ChartPaneId = market.PaneId,
                Symbol = market.Symbol,
                Timeframe = market.Timeframe,
                OrderType = orderType,
                Volume = volume,
                EntryPrice = RoundPrice(entry, market.Digits),
                StopLimitPrice = stopLimit > 0 ? RoundPrice(stopLimit, market.Digits) : 0,
                StopLoss = sl,
                TakeProfit = tp,
                Point = market.Point,
                Digits = market.Digits,
                CreatedUtc = DateTime.UtcNow,
                ExpirationUtc = expiration?.ToUniversalTime(),
                Status = "Waiting"
            };
            _demoPendingOrders.Add(order);
            SetDemoStatus($"Pending order #{order.Id} placed.", false);
        }
        ClearDemoPendingEdit();
        SaveDemoTradingState();
        RefreshDemoTradingUi();
    }

    private static bool ValidatePendingPlacement(string type, double entry, double stopLimit, DemoMarketSnapshot market, out string error)
    {
        error = string.Empty;
        bool valid = type switch
        {
            "Buy Limit" => entry < market.Ask,
            "Sell Limit" => entry > market.Bid,
            "Buy Stop" => entry > market.Ask,
            "Sell Stop" => entry < market.Bid,
            "Buy Stop Limit" => entry > market.Ask && stopLimit <= entry,
            "Sell Stop Limit" => entry < market.Bid && stopLimit >= entry,
            _ => false
        };
        if (valid)
            return true;
        error = type switch
        {
            "Buy Limit" => "Buy Limit entry must be below the current Ask.",
            "Sell Limit" => "Sell Limit entry must be above the current Bid.",
            "Buy Stop" => "Buy Stop entry must be above the current Ask.",
            "Sell Stop" => "Sell Stop entry must be below the current Bid.",
            "Buy Stop Limit" => "Buy Stop Limit trigger must be above Ask and its limit price at or below the trigger.",
            "Sell Stop Limit" => "Sell Stop Limit trigger must be below Bid and its limit price at or above the trigger.",
            _ => "Unsupported pending-order type."
        };
        return false;
    }

    private DateTime? ResolveDemoExpiration(out string error)
    {
        error = string.Empty;
        if (DemoExpirationModeCombo.SelectedIndex != 1)
            return null;
        if (!DemoExpirationDatePicker.SelectedDate.HasValue)
        {
            error = "Choose a pending-order expiration date.";
            return null;
        }
        TimeSpan time = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(DemoExpirationTimeBox.Text) && !TimeSpan.TryParse(DemoExpirationTimeBox.Text, CultureInfo.InvariantCulture, out time))
        {
            error = "Enter expiration time as HH:mm.";
            return null;
        }
        DateTime local = DemoExpirationDatePicker.SelectedDate.Value.Date + time;
        if (local <= DateTime.Now)
        {
            error = "Pending-order expiration must be in the future.";
            return null;
        }
        return local;
    }

    private void DemoEditPendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DemoPendingOrder order })
            return;
        _demoEditingPendingOrderId = order.Id;
        DemoOrderTypeCombo.SelectedItem = order.OrderType;
        DemoLotBox.Text = order.Volume.ToString("0.00", CultureInfo.InvariantCulture);
        DemoPendingEntryBox.Text = order.EntryPrice.ToString($"F{order.Digits}", CultureInfo.InvariantCulture);
        DemoStopLimitBox.Text = order.StopLimitPrice > 0 ? order.StopLimitPrice.ToString($"F{order.Digits}", CultureInfo.InvariantCulture) : string.Empty;
        DemoManualSlBox.Text = order.StopLoss > 0 ? order.StopLoss.ToString($"F{order.Digits}", CultureInfo.InvariantCulture) : string.Empty;
        DemoManualTpBox.Text = order.TakeProfit > 0 ? order.TakeProfit.ToString($"F{order.Digits}", CultureInfo.InvariantCulture) : string.Empty;
        _demoSlPresetPoints = null;
        _demoTpPresetPoints = null;
        if (order.ExpirationUtc.HasValue)
        {
            DateTime local = order.ExpirationUtc.Value.ToLocalTime();
            DemoExpirationModeCombo.SelectedIndex = 1;
            DemoExpirationDatePicker.SelectedDate = local.Date;
            DemoExpirationTimeBox.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            DemoExpirationModeCombo.SelectedIndex = 0;
        }
        DemoPlacePendingButton.Content = "Modify selected pending order";
        DemoCancelPendingEditButton.Visibility = Visibility.Visible;
        UpdateDemoPresetVisuals();
        UpdateDemoOrderPreview();
    }

    private void DemoCancelPendingEditButton_Click(object sender, RoutedEventArgs e) => ClearDemoPendingEdit();

    private void ClearDemoPendingEdit()
    {
        _demoEditingPendingOrderId = null;
        DemoPlacePendingButton.Content = "Place pending order";
        DemoCancelPendingEditButton.Visibility = Visibility.Collapsed;
    }

    private void DemoCancelPendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DemoPendingOrder order })
            return;
        _demoPendingOrders.Remove(order);
        if (_demoEditingPendingOrderId == order.Id)
            ClearDemoPendingEdit();
        SaveDemoTradingState();
        RefreshDemoTradingUi();
        SetDemoStatus($"Pending order #{order.Id} cancelled.", false);
    }

    private void DemoBreakevenButton_Click(object sender, RoutedEventArgs e)
    {
        if (DemoOpenPositionsGrid.SelectedItem is not DemoPosition position || !TryGetDemoMarket(position, out DemoMarketSnapshot market))
        {
            SetDemoStatus("Select an open position first.", true);
            return;
        }
        double requested = position.Direction == "BUY"
            ? position.EntryPrice + 10 * position.Point
            : position.EntryPrice - 10 * position.Point;
        if (!TryApplyPositionLevels(position, requested, position.TakeProfit, market, out string error))
        {
            SetDemoStatus(error, true);
            return;
        }
        CommitPositionLevelChange(position, "Breakeven +10 applied");
    }

    private void DemoApplyPositionLevelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DemoPosition position } || !TryGetDemoMarket(position, out DemoMarketSnapshot market))
            return;
        double sl = ParsePositionLevel(position.EditSlPriceText, position.EditSlPointsText, position, market, true);
        double tp = ParsePositionLevel(position.EditTpPriceText, position.EditTpPointsText, position, market, false);
        if (!TryApplyPositionLevels(position, sl, tp, market, out string error))
        {
            SetDemoStatus(error, true);
            position.InitializeEditValues();
            return;
        }
        CommitPositionLevelChange(position, "SL/TP updated");
    }

    private static double ParsePositionLevel(string priceText, string pointsText, DemoPosition position, DemoMarketSnapshot market, bool stop)
    {
        if (double.TryParse(pointsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && points > 0)
        {
            bool buy = position.Direction == "BUY";
            if (stop)
                return buy ? market.Bid - points * market.Point : market.Ask + points * market.Point;
            return buy ? market.Bid + points * market.Point : market.Ask - points * market.Point;
        }
        return double.TryParse(priceText, NumberStyles.Float, CultureInfo.InvariantCulture, out double exact) && exact > 0
            ? exact
            : 0;
    }

    private void DemoRemovePositionSlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DemoPosition position })
        {
            position.StopLoss = 0;
            CommitPositionLevelChange(position, "SL removed");
        }
    }

    private void DemoRemovePositionTpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DemoPosition position })
        {
            position.TakeProfit = 0;
            CommitPositionLevelChange(position, "TP removed");
        }
    }

    private bool TryApplyPositionLevels(DemoPosition position, double sl, double tp, DemoMarketSnapshot market, out string error)
    {
        error = string.Empty;
        sl = sl > 0 ? RoundPrice(sl, position.Digits) : 0;
        tp = tp > 0 ? RoundPrice(tp, position.Digits) : 0;
        if (position.Direction == "BUY")
        {
            if (sl > 0 && sl >= market.Bid) { error = "Buy SL must be below the current Bid."; return false; }
            if (tp > 0 && tp <= market.Bid) { error = "Buy TP must be above the current Bid."; return false; }
        }
        else
        {
            if (sl > 0 && sl <= market.Ask) { error = "Sell SL must be above the current Ask."; return false; }
            if (tp > 0 && tp >= market.Ask) { error = "Sell TP must be below the current Ask."; return false; }
        }
        position.StopLoss = sl;
        position.TakeProfit = tp;
        return true;
    }

    private void CommitPositionLevelChange(DemoPosition position, string message)
    {
        position.InitializeEditValues();
        position.NotifyAll();
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        SetDemoStatus($"Position #{position.Id}: {message}.", false);
    }

    private void DemoCloseSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DemoOpenPositionsGrid.SelectedItem is DemoPosition position)
            CloseDemoPositionAtMarket(position, "Manual");
        else
            SetDemoStatus("Select an open position first.", true);
    }

    private void DemoClosePositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DemoPosition position })
            CloseDemoPositionAtMarket(position, "Manual");
    }

    private void DemoCloseAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (DemoPosition position in _demoOpenPositions.ToArray())
            CloseDemoPositionAtMarket(position, "Manual close all");
    }

    private void DemoDeleteSelectedHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DemoTradeHistoryGrid.SelectedItem is not DemoTradeRecord trade)
        {
            SetDemoStatus("Select one closed demo trade in Trade history first.", true);
            return;
        }
        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete closed demo trade #{trade.Id} and all of its entry/exit/SL/TP chart markings?",
            "Delete Demo Trade History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;
        _demoTradeHistory.Remove(trade);
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        SetDemoStatus($"Closed trade #{trade.Id} and its chart markings were deleted.", false);
    }

    private void DemoDeleteAllHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_demoTradeHistory.Count == 0)
        {
            SetDemoStatus("There is no completed demo history to delete.", false);
            return;
        }
        MessageBoxResult result = MessageBox.Show(
            this,
            "Delete ALL completed demo trade history and ALL related entry/exit/SL/TP chart markings? Open positions and pending orders will remain unchanged.",
            "Delete All Demo History and Markings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;
        _demoTradeHistory.Clear();
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        SetDemoStatus("All completed demo history and chart markings were deleted. Open positions and pending orders were kept.", false);
    }

    private void CloseDemoPositionAtMarket(DemoPosition position, string reason)
    {
        if (!TryGetDemoMarket(position, out DemoMarketSnapshot market))
        {
            SetDemoStatus($"No live price is available to close position #{position.Id}.", true);
            return;
        }
        double exit = position.Direction == "BUY" ? market.Bid : market.Ask;
        CloseDemoPosition(position, exit, reason);
    }

    private void DemoResetButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            this,
            "Reset the demo account to $1,000,000? This clears open positions, pending orders and demo history.",
            "Reset Demo Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        _demoOpenPositions.Clear();
        _demoPendingOrders.Clear();
        _demoTradeHistory.Clear();
        _demoAccount = new DemoAccountDocument
        {
            Balance = DemoInitialBalance,
            NextTradeId = 1,
            NextOrderId = 1,
            ShowHistoryOnChart = DemoShowHistoryOnChartCheckBox.IsChecked == true
        };
        ClearDemoPendingEdit();
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        SetDemoStatus("Demo account reset to $1,000,000.", false);
    }

    private void RefreshDemoTradingMarket()
    {
        if (_isClosing)
            return;

        if (TryGetActiveDemoMarket(out DemoMarketSnapshot active))
        {
            DemoCurrentSymbolText.Text = $"Chart {active.PaneId} · {active.Symbol} · {active.Timeframe}";
            string format = $"F{active.Digits}";
            DemoCurrentPriceText.Text = $"Bid {active.Bid.ToString(format, CultureInfo.InvariantCulture)}   Ask {active.Ask.ToString(format, CultureInfo.InvariantCulture)}";
        }
        else
        {
            DemoCurrentSymbolText.Text = "Select a chart";
            DemoCurrentPriceText.Text = "No market price";
        }

        ProcessDemoPendingOrders();
        foreach (DemoPosition position in _demoOpenPositions.ToArray())
        {
            if (!TryGetDemoMarket(position, out DemoMarketSnapshot market))
                continue;
            double mark = position.Direction == "BUY" ? market.Bid : market.Ask;
            position.CurrentBid = RoundPrice(market.Bid, position.Digits);
            position.CurrentAsk = RoundPrice(market.Ask, position.Digits);
            position.CurrentPrice = RoundPrice(mark, position.Digits);
            position.RecalculateProfit();

            bool stopHit = position.StopLoss > 0 && (position.Direction == "BUY"
                ? market.Bid <= position.StopLoss
                : market.Ask >= position.StopLoss);
            bool targetHit = position.TakeProfit > 0 && (position.Direction == "BUY"
                ? market.Bid >= position.TakeProfit
                : market.Ask <= position.TakeProfit);
            if (stopHit)
                CloseDemoPosition(position, mark, "Stop loss");
            else if (targetHit)
                CloseDemoPosition(position, mark, "Take profit");
        }

        RefreshDemoTradingUi();
        if (_demoOpenPositions.Count > 0)
            RefreshDemoTradeLines();
        UpdateDemoOrderPreview();
    }

    private void ProcessDemoPendingOrders()
    {
        foreach (DemoPendingOrder order in _demoPendingOrders.ToArray())
        {
            if (order.ExpirationUtc.HasValue && DateTime.UtcNow >= order.ExpirationUtc.Value)
            {
                _demoPendingOrders.Remove(order);
                SetDemoStatus($"Pending order #{order.Id} expired.", false);
                SaveDemoTradingState();
                continue;
            }
            if (!TryGetDemoMarket(order.Symbol, order.Timeframe, out DemoMarketSnapshot market))
                continue;

            bool triggered = false;
            string direction = order.OrderType.StartsWith("Buy", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
            switch (order.OrderType)
            {
                case "Buy Limit": triggered = market.Ask <= order.EntryPrice; break;
                case "Sell Limit": triggered = market.Bid >= order.EntryPrice; break;
                case "Buy Stop": triggered = market.Ask >= order.EntryPrice; break;
                case "Sell Stop": triggered = market.Bid <= order.EntryPrice; break;
                case "Buy Stop Limit":
                    if (!order.IsStopLimitActivated && market.Ask >= order.EntryPrice)
                    {
                        order.IsStopLimitActivated = true;
                        order.Status = "Limit active";
                        order.NotifyAll();
                        SaveDemoTradingState();
                    }
                    triggered = order.IsStopLimitActivated && market.Ask <= order.StopLimitPrice;
                    break;
                case "Sell Stop Limit":
                    if (!order.IsStopLimitActivated && market.Bid <= order.EntryPrice)
                    {
                        order.IsStopLimitActivated = true;
                        order.Status = "Limit active";
                        order.NotifyAll();
                        SaveDemoTradingState();
                    }
                    triggered = order.IsStopLimitActivated && market.Bid >= order.StopLimitPrice;
                    break;
            }
            if (!triggered)
                continue;

            double execution = direction == "BUY" ? market.Ask : market.Bid;
            DemoPosition position = CreateDemoPosition(direction, order.Volume, execution, order.StopLoss, order.TakeProfit, market);
            _demoOpenPositions.Add(position);
            _demoPendingOrders.Remove(order);
            SaveDemoTradingState();
            RefreshDemoTradeLines();
            SetDemoStatus($"Pending order #{order.Id} triggered into {direction} position #{position.Id} at {position.EntryPrice.ToString($"F{position.Digits}", CultureInfo.InvariantCulture)}.", false);
        }
    }

    private void CloseDemoPosition(DemoPosition position, double exitPrice, string reason)
    {
        if (!_demoOpenPositions.Contains(position))
            return;
        position.CurrentPrice = RoundPrice(exitPrice, position.Digits);
        position.RecalculateProfit();
        double profit = position.FloatingProfit;
        _demoAccount.Balance += profit;
        _demoOpenPositions.Remove(position);
        _demoTradeHistory.Insert(0, new DemoTradeRecord
        {
            Id = position.Id,
            ChartPaneId = position.ChartPaneId,
            Symbol = position.Symbol,
            Timeframe = position.Timeframe,
            Direction = position.Direction,
            Volume = position.Volume,
            EntryPrice = position.EntryPrice,
            ExitPrice = position.CurrentPrice,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit,
            OpenedUtc = position.OpenedUtc,
            ClosedUtc = DateTime.UtcNow,
            OpenedServerUnix = ResolveDemoPositionOpenedServerUnix(position),
            ClosedServerUnix = Mt5ServerClock.ServerNowUnix(GetDemoServerUtcOffsetMinutes()),
            Profit = profit,
            CloseReason = reason
        });
        SaveDemoTradingState();
        RefreshDemoTradeLines();
        RefreshDemoTradingUi();
        SetDemoStatus($"Position #{position.Id} closed: {reason}, P/L {profit.ToString("C2", CultureInfo.GetCultureInfo("en-US"))}.", false);
    }

    private void RefreshDemoTradingUi()
    {
        if (DemoBalanceText is null)
            return;
        double floating = _demoOpenPositions.Sum(position => position.FloatingProfit);
        double realized = _demoTradeHistory.Sum(trade => trade.Profit);
        double margin = _demoOpenPositions.Sum(position => Math.Abs(position.EntryPrice * position.Volume * position.ContractSize) / 100.0);
        int profitable = _demoTradeHistory.Count(trade => trade.Profit > 0.0000001);
        int losing = _demoTradeHistory.Count(trade => trade.Profit < -0.0000001);
        int breakeven = _demoTradeHistory.Count - profitable - losing;
        double winningTotal = _demoTradeHistory.Where(trade => trade.Profit > 0).Sum(trade => trade.Profit);
        double losingTotal = _demoTradeHistory.Where(trade => trade.Profit < 0).Sum(trade => trade.Profit);
        CultureInfo usd = CultureInfo.GetCultureInfo("en-US");

        DemoBalanceText.Text = _demoAccount.Balance.ToString("C2", usd);
        DemoEquityText.Text = (_demoAccount.Balance + floating).ToString("C2", usd);
        DemoFloatingText.Text = floating.ToString("C2", usd);
        DemoRealizedText.Text = realized.ToString("C2", usd);
        DemoMarginText.Text = margin.ToString("C2", usd);
        DemoOpenPositionCountText.Text = _demoOpenPositions.Count == 1 ? "1 open position" : $"{_demoOpenPositions.Count} open positions";
        DemoPendingOrderCountText.Text = _demoPendingOrders.Count == 1 ? "1 pending order" : $"{_demoPendingOrders.Count} pending orders";
        DemoHistoryTotalText.Text = _demoTradeHistory.Count.ToString(CultureInfo.InvariantCulture);
        DemoHistoryWinsText.Text = profitable.ToString(CultureInfo.InvariantCulture);
        DemoHistoryLossesText.Text = losing.ToString(CultureInfo.InvariantCulture);
        DemoHistoryBreakevenText.Text = breakeven.ToString(CultureInfo.InvariantCulture);
        DemoHistoryWinningTotalText.Text = winningTotal.ToString("C2", usd);
        DemoHistoryLosingTotalText.Text = losingTotal.ToString("C2", usd);
        DemoHistoryNetText.Text = realized.ToString("C2", usd);
    }

    private void SetDemoStatus(string message, bool error)
    {
        // Startup and teardown events can run before/after named XAML controls
        // are available. Status reporting must never crash the chart window.
        if (DemoTradeStatusText is not null)
        {
            DemoTradeStatusText.Text = message;
            DemoTradeStatusText.Foreground = error
                ? new SolidColorBrush(Color.FromRgb(160, 25, 38))
                : new SolidColorBrush(Color.FromRgb(20, 112, 67));
        }
        if (StatusText is not null)
            StatusText.Text = message;
    }

    private bool TryGetActiveDemoMarket(out DemoMarketSnapshot market)
    {
        market = default;
        return ActiveChartContext is not null && TryGetDemoMarket(ActiveChartContext, out market);
    }

    private bool TryGetDemoMarket(DemoPosition position, out DemoMarketSnapshot market)
    {
        market = default;
        if (position.ChartPaneId > 0 &&
            _chartContexts.TryGetValue(position.ChartPaneId, out ChartRuntimeContext? exact) &&
            exact is not null &&
            DemoSymbolsMatch(position.Symbol, exact.Symbol) &&
            TryGetDemoMarket(exact, out market))
        {
            return true;
        }

        if (!TryGetDemoMarket(position.Symbol, position.Timeframe, out market))
            return false;
        return true;
    }

    private bool TryGetDemoMarket(string symbol, string timeframe, out DemoMarketSnapshot market)
    {
        market = default;
        // Timeframe is retained in the saved trade record for audit/history, but a live
        // position is symbol-scoped. Any loaded chart of the same symbol can supply Bid/Ask.
        if (_chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? active) &&
            active is not null && DemoSymbolsMatch(symbol, active.Symbol) &&
            TryGetDemoMarket(active, out market))
        {
            return true;
        }

        foreach (ChartRuntimeContext context in _chartContexts.Values.OrderBy(item => item.PaneId))
        {
            if (!DemoSymbolsMatch(symbol, context.Symbol))
                continue;
            if (TryGetDemoMarket(context, out market))
                return true;
        }
        return false;
    }

    private static bool DemoSymbolsMatch(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetDemoMarket(ChartRuntimeContext context, out DemoMarketSnapshot market)
    {
        market = default;
        // The rendered chart owns the newest in-place live candle. DisplayCandles can be
        // an older copied page, so always read Chart.Candles first for execution/P&L.
        Candle? candle = context.Chart.Candles.LastOrDefault() ?? context.DisplayCandles.LastOrDefault();
        if (candle is null || candle.Close <= 0 || string.IsNullOrWhiteSpace(context.Symbol))
            return false;
        double point = candle.Point > 0 ? candle.Point : Math.Pow(10, -Math.Max(0, candle.Digits));
        if (!double.IsFinite(point) || point <= 0)
            return false;
        double bid = candle.Close;
        double spreadPoints = Math.Max(0, candle.Spread);
        double ask = bid + spreadPoints * point;
        if (!double.IsFinite(bid) || !double.IsFinite(ask) || bid <= 0 || ask < bid)
            return false;
        double contractSize = ResolveDemoContractSize(context.Symbol);
        double tickSize = point;
        double tickValuePerLot = tickSize * contractSize;
        market = new DemoMarketSnapshot(
            context.PaneId,
            context.Symbol,
            context.Timeframe.DisplayText,
            candle.Digits,
            point,
            bid,
            ask,
            tickSize,
            tickValuePerLot,
            contractSize);
        return true;
    }

    private void RefreshDemoTradeLines()
    {
        foreach (ChartRuntimeContext context in _chartContexts.Values)
        {
            List<DemoTradeLineOverlay> lines = new();

            // Active positions and their levels are account/symbol objects, not pane or
            // timeframe objects. Render the same synchronized overlays on every chart
            // currently showing that symbol.
            foreach (DemoPosition position in _demoOpenPositions.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol)))
            {
                bool isBuy = position.Direction == "BUY";
                lines.Add(new DemoTradeLineOverlay(
                    DemoLineId(position.Id, "entry"), position.Id, DemoTradeLineKind.Entry,
                    position.EntryPrice,
                    $"{position.Direction} {position.Volume:0.00} lot · {FormatDemoUsdAmount(position.FloatingProfit)}",
                    true,
                    isBuy,
                    EntryPrice: position.EntryPrice,
                    Volume: position.Volume,
                    TickSize: position.TickSize,
                    TickValuePerLot: position.TickValuePerLot,
                    ContractSize: position.ContractSize));
                if (position.StopLoss > 0)
                    lines.Add(new DemoTradeLineOverlay(
                        DemoLineId(position.Id, "sl"), position.Id, DemoTradeLineKind.StopLoss,
                        position.StopLoss,
                        $"SL · {FormatDemoUsdAmount(CalculateDemoProfitAtPrice(position, position.StopLoss))}",
                        true,
                        isBuy,
                        EntryPrice: position.EntryPrice,
                        Volume: position.Volume,
                        TickSize: position.TickSize,
                        TickValuePerLot: position.TickValuePerLot,
                        ContractSize: position.ContractSize));
                if (position.TakeProfit > 0)
                    lines.Add(new DemoTradeLineOverlay(
                        DemoLineId(position.Id, "tp"), position.Id, DemoTradeLineKind.TakeProfit,
                        position.TakeProfit,
                        $"TP · {FormatDemoUsdAmount(CalculateDemoProfitAtPrice(position, position.TakeProfit))}",
                        true,
                        isBuy,
                        EntryPrice: position.EntryPrice,
                        Volume: position.Volume,
                        TickSize: position.TickSize,
                        TickValuePerLot: position.TickValuePerLot,
                        ContractSize: position.ContractSize));
            }

            // Saved history is also symbol-scoped. The original opening timeframe remains
            // stored in the history table, while its entry/exit path is projected onto the
            // candle boundaries of every current timeframe for the same symbol.
            if (_demoAccount.ShowHistoryOnChart)
            {
                TryGetHistoryProjectionWindow(context, out long historyWindowStart, out long historyWindowEnd);
                foreach (DemoTradeRecord trade in _demoTradeHistory.Where(item =>
                             DemoSymbolsMatch(item.Symbol, context.Symbol) &&
                             TradeOverlapsProjectionWindow(ResolveDemoTradeOpenedServerUnix(item), ResolveDemoTradeClosedServerUnix(item), historyWindowStart, historyWindowEnd)))
                {
                    bool isBuy = trade.Direction == "BUY";
                    long openUnix = ResolveDemoTradeOpenedServerUnix(trade);
                    long closeUnix = ResolveDemoTradeClosedServerUnix(trade);
                    string profit = trade.Profit.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
                    string openTime = trade.OpenedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                    string closeTime = trade.ClosedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                    string slText = trade.StopLoss > 0 ? trade.StopLoss.ToString("G10", CultureInfo.InvariantCulture) : "None";
                    string tpText = trade.TakeProfit > 0 ? trade.TakeProfit.ToString("G10", CultureInfo.InvariantCulture) : "None";
                    string historyToolTip =
                        $"{trade.Direction} #{trade.Id} · {trade.Volume:0.00} lot\n" +
                        $"Entry: {trade.EntryPrice:G10} · {openTime}\n" +
                        $"Exit: {trade.ExitPrice:G10} · {closeTime}\n" +
                        $"Realized P/L: {profit}\n" +
                        $"Close reason: {trade.CloseReason}\n" +
                        $"Historical SL: {slText}\nHistorical TP: {tpText}";
                    lines.Add(new DemoTradeLineOverlay(
                        DemoHistoryLineId(trade.Id, "entry"), trade.Id, DemoTradeLineKind.HistoryEntry,
                        trade.EntryPrice,
                        historyToolTip,
                        false, isBuy, true, openUnix, closeUnix, false));
                    lines.Add(new DemoTradeLineOverlay(
                        DemoHistoryLineId(trade.Id, "exit"), trade.Id, DemoTradeLineKind.HistoryExit,
                        trade.ExitPrice,
                        historyToolTip,
                        false, isBuy, true, openUnix, closeUnix, false));
                    if (trade.StopLoss > 0)
                    {
                        lines.Add(new DemoTradeLineOverlay(
                            DemoHistoryLineId(trade.Id, "sl"), trade.Id, DemoTradeLineKind.HistoryStopLoss,
                            trade.StopLoss, $"Historical SL #{trade.Id}", false, isBuy, true, openUnix, closeUnix, false));
                    }
                    if (trade.TakeProfit > 0)
                    {
                        lines.Add(new DemoTradeLineOverlay(
                            DemoHistoryLineId(trade.Id, "tp"), trade.Id, DemoTradeLineKind.HistoryTakeProfit,
                            trade.TakeProfit, $"Historical TP #{trade.Id}", false, isBuy, true, openUnix, closeUnix, false));
                    }
                }
            }
            AppendProjectedMt5HistoryLines(context, lines);
            context.Chart.DemoTradeLines = lines;
        }

        // Keep legacy detached mirror windows in sync immediately when the history
        // checkbox or any running/closed trade state changes.
        if (!_isClosing)
        {
            foreach (var chartWindow in _detachedChartWindows.ToArray())
            {
                if (chartWindow.IsLoaded)
                    chartWindow.UpdateDemoTradeLines(CandleChart.DemoTradeLines);
            }
        }
    }

    private void AppendProjectedMt5HistoryLines(ChartRuntimeContext context, List<DemoTradeLineOverlay> lines)
    {
        TryGetHistoryProjectionWindow(context, out long historyWindowStart, out long historyWindowEnd);
        foreach (Mt5TradeHistoryFileEntry file in _mt5TradeHistoryFiles.Where(item => item.IsProjected && item.Report is not null))
        {
            Mt5TradeProjectionRecord[] visible = file
                .GetProjectionTrades(historyWindowStart, historyWindowEnd)
                .Where(item => DemoSymbolsMatch(item.Trade.Symbol, context.Symbol) && item.Trade.EntryPrice > 0 && item.Trade.ExitPrice > 0)
                .ToArray();
            bool denseProjection = visible.Length > 600;

            foreach (Mt5TradeProjectionRecord indexed in visible)
            {
                TradeHistoryTrade trade = indexed.Trade;
                int tradeIndex = indexed.SourceIndex;
                long openUnix = indexed.OpenUnix;
                long closeUnix = indexed.CloseUnix;
                bool isBuy = string.Equals(trade.Direction, "BUY", StringComparison.OrdinalIgnoreCase);
                int projectionId = StableMt5ProjectionId(file.FilePath, trade.Ticket, tradeIndex);
                string ticket = string.IsNullOrWhiteSpace(trade.Ticket) ? $"row {tradeIndex + 1}" : trade.Ticket;
                string toolTip =
                    $"MT5 · {file.Name}\n" +
                    $"{trade.Direction} #{ticket} · {trade.Volume:0.00} lot\n" +
                    $"Entry: {trade.EntryPrice:G10} · {trade.OpenTime:g}\n" +
                    $"Exit: {trade.ExitPrice:G10} · {trade.CloseTime:g}\n" +
                    $"Profit: {trade.Profit:0.00} · Net: {trade.NetProfit:0.00} {file.Report!.Currency}\n" +
                    $"Commission: {trade.Commission:0.00} · Swap: {trade.Swap:0.00} · Fees: {trade.Fees:0.00}";
                string prefix = $"mt5hist:{Math.Abs(projectionId)}";
                lines.Add(new DemoTradeLineOverlay($"{prefix}:entry", projectionId, DemoTradeLineKind.HistoryEntry,
                    trade.EntryPrice, toolTip, false, isBuy, true, openUnix, closeUnix, false));
                lines.Add(new DemoTradeLineOverlay($"{prefix}:exit", projectionId, DemoTradeLineKind.HistoryExit,
                    trade.ExitPrice, toolTip, false, isBuy, true, openUnix, closeUnix, false));
                if (!denseProjection && trade.StopLoss > 0)
                    lines.Add(new DemoTradeLineOverlay($"{prefix}:sl", projectionId, DemoTradeLineKind.HistoryStopLoss,
                        trade.StopLoss, $"MT5 historical SL · {file.Name} · #{ticket}", false, isBuy, true, openUnix, closeUnix, false));
                if (!denseProjection && trade.TakeProfit > 0)
                    lines.Add(new DemoTradeLineOverlay($"{prefix}:tp", projectionId, DemoTradeLineKind.HistoryTakeProfit,
                        trade.TakeProfit, $"MT5 historical TP · {file.Name} · #{ticket}", false, isBuy, true, openUnix, closeUnix, false));
            }
        }
    }

    private void ScheduleHistoryProjectionRefresh()
    {
        if (_isClosing || !_demoTradingInitialized || _historyProjectionRefreshQueued)
            return;
        if (!_demoAccount.ShowHistoryOnChart && !_mt5TradeHistoryFiles.Any(item => item.IsProjected))
            return;

        _historyProjectionRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _historyProjectionRefreshQueued = false;
            if (!_isClosing)
                RefreshDemoTradeLines();
        }));
    }

    private static bool TryGetHistoryProjectionWindow(ChartRuntimeContext context, out long startUnix, out long endUnix)
    {
        startUnix = long.MinValue;
        endUnix = long.MaxValue;
        IReadOnlyList<Candle> candles = context.Chart.Candles;
        ChartViewportSnapshot? snapshot = context.Chart.CaptureViewportSnapshot();
        if (snapshot is null || candles.Count == 0 || snapshot.LastExclusive <= snapshot.FirstIndex)
            return false;

        int first = Math.Clamp(snapshot.FirstIndex, 0, candles.Count - 1);
        int last = Math.Clamp(snapshot.LastExclusive - 1, first, candles.Count - 1);
        long visibleStart = candles[first].StartUnix;
        long visibleEnd = Math.Max(candles[last].EndUnix, candles[last].StartUnix + 1);
        long span = Math.Max(1, visibleEnd - visibleStart);
        // A modest prefetch buffer keeps markers stable while the user pans without
        // ever materialising a whole multi-month/year history on the visual tree.
        long buffer = Math.Clamp(span / 4, 60, 7 * 24 * 60 * 60);
        startUnix = visibleStart > long.MinValue + buffer ? visibleStart - buffer : long.MinValue;
        endUnix = visibleEnd < long.MaxValue - buffer ? visibleEnd + buffer : long.MaxValue;
        return true;
    }

    private static bool TradeOverlapsProjectionWindow(long openUnix, long closeUnix, long windowStart, long windowEnd)
    {
        if (closeUnix < openUnix)
            (openUnix, closeUnix) = (closeUnix, openUnix);
        return closeUnix >= windowStart && openUnix <= windowEnd;
    }

    private static long ReportTimeToServerUnix(DateTime time)
    {
        DateTime unspecified = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static int StableMt5ProjectionId(string filePath, string ticket, int index)
    {
        unchecked
        {
            uint hash = 2166136261;
            string key = filePath + "|" + ticket + "|" + index.ToString(CultureInfo.InvariantCulture);
            foreach (char c in key)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 16777619;
            }
            return -(1_000_000 + (int)(hash & 0x3FFFFFFF));
        }
    }

    private void MoveDemoTradeLine(ChartRuntimeContext context, string lineId, double requestedPrice)
    {
        string[] parts = lineId.Split(':');
        if (parts.Length != 3 || !string.Equals(parts[0], "demo", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int positionId))
            return;
        DemoPosition? position = _demoOpenPositions.FirstOrDefault(item => item.Id == positionId);
        if (position is null || !DemoSymbolsMatch(position.Symbol, context.Symbol) ||
            !TryGetDemoMarket(context, out DemoMarketSnapshot market))
        {
            RefreshDemoTradeLines();
            return;
        }
        double price = RoundPrice(requestedPrice, position.Digits);
        string kind = parts[2].ToLowerInvariant();
        if (kind == "entry")
        {
            double minimumMove = Math.Max(position.Point, Math.Pow(10, -Math.Max(0, position.Digits)));
            if (Math.Abs(price - position.EntryPrice) < minimumMove * 0.5)
            {
                RefreshDemoTradeLines();
                return;
            }

            // Dragging away from entry creates or replaces the appropriate level.
            // BUY: below entry = SL, above entry = TP. SELL is the reverse.
            bool createsStopLoss = position.Direction == "BUY"
                ? price < position.EntryPrice
                : price > position.EntryPrice;
            double createdSl = createsStopLoss ? price : position.StopLoss;
            double createdTp = createsStopLoss ? position.TakeProfit : price;
            if (!TryApplyPositionLevels(position, createdSl, createdTp, market, out string createError))
            {
                SetDemoStatus(createError, true);
                RefreshDemoTradeLines();
                return;
            }
            CommitPositionLevelChange(position,
                $"{(createsStopLoss ? "SL" : "TP")} created from entry drag at {price.ToString($"F{position.Digits}", CultureInfo.InvariantCulture)}");
            return;
        }

        double sl = kind == "sl" ? price : position.StopLoss;
        double tp = kind == "tp" ? price : position.TakeProfit;
        if (!TryApplyPositionLevels(position, sl, tp, market, out string error))
        {
            SetDemoStatus(error, true);
            RefreshDemoTradeLines();
            return;
        }
        CommitPositionLevelChange(position, $"{kind.ToUpperInvariant()} moved to {price.ToString($"F{position.Digits}", CultureInfo.InvariantCulture)}");
    }

    private void OpenDemoTradeLineContextMenu(ChartRuntimeContext context, DemoTradeLineOverlay line)
    {
        DemoPosition? position = _demoOpenPositions.FirstOrDefault(item => item.Id == line.PositionId);
        if (position is null)
            return;

        var menu = new ContextMenu { PlacementTarget = context.Chart, Background = Brushes.White, Foreground = Brushes.Black };
        menu.Items.Add(new MenuItem
        {
            Header = $"{position.Direction} #{position.Id} · {position.Symbol} · {position.Volume:0.00} lot",
            IsEnabled = false
        });
        menu.Items.Add(new Separator());

        if (line.Kind is DemoTradeLineKind.StopLoss)
        {
            AddDemoLevelMenuItems(menu, position, isStopLoss: true, isExisting: true);
        }
        else if (line.Kind is DemoTradeLineKind.TakeProfit)
        {
            AddDemoLevelMenuItems(menu, position, isStopLoss: false, isExisting: true);
        }
        else
        {
            AddDemoLevelMenuItems(menu, position, isStopLoss: true, isExisting: position.StopLoss > 0);
            menu.Items.Add(new Separator());
            AddDemoLevelMenuItems(menu, position, isStopLoss: false, isExisting: position.TakeProfit > 0);
        }

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Close position now" };
        close.Click += (_, _) => CloseDemoPositionAtMarket(position, "Manual");
        menu.Items.Add(close);
        context.Chart.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void AddDemoLevelMenuItems(ContextMenu menu, DemoPosition position, bool isStopLoss, bool isExisting)
    {
        string shortName = isStopLoss ? "SL" : "TP";
        string action = isExisting ? "Modify" : "Add";
        var exact = new MenuItem { Header = $"{action} {shortName} — modify price ▲ / ▼…" };
        exact.Click += (_, _) => PromptAndApplyDemoPositionLevel(position, isStopLoss);
        menu.Items.Add(exact);

        if (isExisting)
        {
            var remove = new MenuItem { Header = $"Remove / cancel {shortName}" };
            remove.Click += (_, _) =>
            {
                if (isStopLoss)
                    position.StopLoss = 0;
                else
                    position.TakeProfit = 0;
                CommitPositionLevelChange(position, $"{shortName} removed");
            };
            menu.Items.Add(remove);
        }
    }

    private void PromptAndApplyDemoPositionLevel(DemoPosition position, bool isStopLoss)
    {
        if (!TryGetDemoMarket(position, out DemoMarketSnapshot market))
        {
            SetDemoStatus($"No live Bid/Ask is available for position #{position.Id}.", true);
            return;
        }

        string shortName = isStopLoss ? "SL" : "TP";
        double currentLevel = isStopLoss ? position.StopLoss : position.TakeProfit;
        double initial = currentLevel > 0 ? currentLevel : position.EntryPrice;
        double point = position.Point > 0
            ? position.Point
            : Math.Pow(10, -Math.Max(0, position.Digits));
        double arrowStep = Math.Max(point, point * 50.0);
        var prompt = new DemoTradeValuePromptWindow(
            $"{shortName} for position #{position.Id}",
            $"Enter the exact {shortName} price. One ▲ / ▼ click moves 50 symbol points; holding an arrow repeats five times faster.",
            initial,
            position.EntryPrice,
            arrowStep,
            position.Digits)
        {
            Owner = this
        };
        if (prompt.ShowDialog() != true)
            return;

        double requested = prompt.Value;
        double sl = isStopLoss ? requested : position.StopLoss;
        double tp = isStopLoss ? position.TakeProfit : requested;
        if (!TryApplyPositionLevels(position, sl, tp, market, out string error))
        {
            SetDemoStatus(error, true);
            return;
        }
        CommitPositionLevelChange(position, $"{shortName} updated");
    }

    private int GetDemoServerUtcOffsetMinutes() =>
        _selectedConnector?.ServerUtcOffsetMinutes ?? 0;

    private long ResolveDemoPositionOpenedServerUnix(DemoPosition position)
    {
        if (position.OpenedServerUnix > 0)
            return position.OpenedServerUnix;
        position.OpenedServerUnix = UtcDateTimeToDemoServerUnix(position.OpenedUtc);
        return position.OpenedServerUnix;
    }

    private long ResolveDemoTradeOpenedServerUnix(DemoTradeRecord trade)
    {
        if (trade.OpenedServerUnix > 0)
            return trade.OpenedServerUnix;
        trade.OpenedServerUnix = UtcDateTimeToDemoServerUnix(trade.OpenedUtc);
        return trade.OpenedServerUnix;
    }

    private long ResolveDemoTradeClosedServerUnix(DemoTradeRecord trade)
    {
        if (trade.ClosedServerUnix > 0)
            return trade.ClosedServerUnix;
        trade.ClosedServerUnix = UtcDateTimeToDemoServerUnix(trade.ClosedUtc);
        return trade.ClosedServerUnix;
    }

    private long UtcDateTimeToDemoServerUnix(DateTime value)
    {
        DateTime utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return Mt5ServerClock.UtcToServerUnix(
            new DateTimeOffset(utc),
            GetDemoServerUtcOffsetMinutes());
    }

    private static string DemoLineId(int positionId, string kind) => $"demo:{positionId.ToString(CultureInfo.InvariantCulture)}:{kind}";
    private static string DemoHistoryLineId(int tradeId, string kind) => $"demo-history:{tradeId.ToString(CultureInfo.InvariantCulture)}:{kind}";

    private static double CalculateDemoProfitAtPrice(DemoPosition position, double valuationPrice)
    {
        double difference = position.Direction == "BUY"
            ? valuationPrice - position.EntryPrice
            : position.EntryPrice - valuationPrice;
        double point = position.Point > 0
            ? position.Point
            : Math.Pow(10, -Math.Max(0, position.Digits));
        double tickSize = position.TickSize > 0 ? position.TickSize : point;
        double tickValue = position.TickValuePerLot > 0
            ? position.TickValuePerLot
            : tickSize * Math.Max(0, position.ContractSize);
        return tickSize > 0
            ? difference / tickSize * tickValue * position.Volume
            : difference * position.Volume * position.ContractSize;
    }

    private static string FormatDemoUsdAmount(double amount)
    {
        if (!double.IsFinite(amount))
            return "$0.00";
        return amount > 0.0000001
            ? $"+${amount.ToString("N2", CultureInfo.InvariantCulture)}"
            : amount < -0.0000001
                ? $"-${Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture)}"
                : "$0.00";
    }

    private static double ResolveDemoContractSize(string symbol)
    {
        string upper = symbol.ToUpperInvariant();
        if (upper.Contains("XAU") || upper.Contains("XAG")) return 100.0;
        if (upper.Contains("BTC") || upper.Contains("ETH") || upper.Contains("CRYPTO")) return 1.0;
        if (upper.Contains("US30") || upper.Contains("NAS") || upper.Contains("SPX") || upper.Contains("GER") || upper.Contains("DAX")) return 1.0;
        return 100_000.0;
    }

    private static double RoundPrice(double price, int digits) => Math.Round(price, Math.Clamp(digits, 0, 10), MidpointRounding.AwayFromZero);

    private DemoAccountDocument? TryReadDemoAccountDocument(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<DemoAccountDocument>(File.ReadAllText(path), _demoJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void LoadDemoTradingState()
    {
        try
        {
            Directory.CreateDirectory(DemoTradingFolder);
            DemoAccountDocument? restored = TryReadDemoAccountDocument(DemoTradingPath)
                ?? TryReadDemoAccountDocument(DemoTradingPath + ".bak")
                ?? TryReadDemoAccountDocument(DemoTradingPath + ".tmp");
            if (restored is not null)
                _demoAccount = restored;
        }
        catch
        {
            _demoAccount = new DemoAccountDocument();
        }
        if (_demoAccount.Balance <= 0 || !double.IsFinite(_demoAccount.Balance))
            _demoAccount.Balance = DemoInitialBalance;
        _demoAccount.NextTradeId = Math.Max(1, _demoAccount.NextTradeId);
        _demoAccount.NextOrderId = Math.Max(1, _demoAccount.NextOrderId);
        foreach (DemoPosition position in _demoAccount.OpenPositions ?? new List<DemoPosition>())
        {
            if (position.Point <= 0)
                position.Point = Math.Pow(10, -Math.Max(0, position.Digits));
            if (position.ContractSize <= 0)
                position.ContractSize = ResolveDemoContractSize(position.Symbol);
            if (position.TickSize <= 0)
                position.TickSize = position.Point;
            if (position.TickValuePerLot <= 0)
                position.TickValuePerLot = position.TickSize * position.ContractSize;
            if (position.CurrentBid <= 0)
                position.CurrentBid = position.Direction == "BUY" ? position.CurrentPrice : Math.Min(position.CurrentPrice, position.EntryPrice);
            if (position.CurrentAsk <= 0)
                position.CurrentAsk = position.Direction == "SELL" ? position.CurrentPrice : Math.Max(position.CurrentPrice, position.EntryPrice);
            ResolveDemoPositionOpenedServerUnix(position);
            position.InitializeEditValues();
            position.RecalculateProfit();
            _demoOpenPositions.Add(position);
        }
        foreach (DemoPendingOrder order in _demoAccount.PendingOrders ?? new List<DemoPendingOrder>())
            _demoPendingOrders.Add(order);
        foreach (DemoTradeRecord trade in (_demoAccount.History ?? new List<DemoTradeRecord>()).OrderByDescending(item => item.ClosedUtc))
        {
            ResolveDemoTradeOpenedServerUnix(trade);
            ResolveDemoTradeClosedServerUnix(trade);
            _demoTradeHistory.Add(trade);
        }
    }

    private void SaveDemoTradingState()
    {
        // Prevent XAML initialization events from overwriting the saved account
        // with the default in-memory document before LoadDemoTradingState runs.
        if (!_demoTradingInitialized)
            return;
        try
        {
            Directory.CreateDirectory(DemoTradingFolder);
            _demoAccount.OpenPositions = _demoOpenPositions.ToList();
            _demoAccount.PendingOrders = _demoPendingOrders.ToList();
            _demoAccount.History = _demoTradeHistory.ToList();
            string temporary = DemoTradingPath + ".tmp";
            string backup = DemoTradingPath + ".bak";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_demoAccount, _demoJsonOptions));
            if (File.Exists(DemoTradingPath))
                File.Copy(DemoTradingPath, backup, overwrite: true);
            File.Move(temporary, DemoTradingPath, overwrite: true);
        }
        catch
        {
            // Demo persistence must never interrupt charting or MT5 data capture.
        }
    }

    private readonly record struct DemoMarketSnapshot(
        int PaneId,
        string Symbol,
        string Timeframe,
        int Digits,
        double Point,
        double Bid,
        double Ask,
        double TickSize,
        double TickValuePerLot,
        double ContractSize);

    public sealed class DemoAccountDocument
    {
        public double Balance { get; set; } = DemoInitialBalance;
        public int NextTradeId { get; set; } = 1;
        public int NextOrderId { get; set; } = 1;
        public bool ShowHistoryOnChart { get; set; } = true;
        public List<DemoPosition> OpenPositions { get; set; } = new();
        public List<DemoPendingOrder> PendingOrders { get; set; } = new();
        public List<DemoTradeRecord> History { get; set; } = new();
    }

    public sealed class DemoPosition : INotifyPropertyChanged
    {
        private double _currentPrice;
        private double _currentBid;
        private double _currentAsk;
        private double _stopLoss;
        private double _takeProfit;
        private double _floatingProfit;
        private double _pointsMoved;
        private string _editSlPriceText = string.Empty;
        private string _editTpPriceText = string.Empty;
        private string _editSlPointsText = string.Empty;
        private string _editTpPointsText = string.Empty;
        public int Id { get; set; }
        public int ChartPaneId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string Direction { get; set; } = "BUY";
        public double Volume { get; set; }
        public double EntryPrice { get; set; }
        public double OpenBid { get; set; }
        public double OpenAsk { get; set; }
        public double CurrentBid { get => _currentBid; set { _currentBid = value; OnPropertyChanged(); } }
        public double CurrentAsk { get => _currentAsk; set { _currentAsk = value; OnPropertyChanged(); } }
        public double CurrentPrice { get => _currentPrice; set { _currentPrice = value; OnPropertyChanged(); } }
        public double StopLoss { get => _stopLoss; set { _stopLoss = value; OnPropertyChanged(); OnPropertyChanged(nameof(StopLossDisplay)); } }
        public double TakeProfit { get => _takeProfit; set { _takeProfit = value; OnPropertyChanged(); OnPropertyChanged(nameof(TakeProfitDisplay)); } }
        public double Point { get; set; }
        public double TickSize { get; set; }
        public double TickValuePerLot { get; set; }
        public int Digits { get; set; }
        public double ContractSize { get; set; }
        public DateTime OpenedUtc { get; set; }
        public long OpenedServerUnix { get; set; }
        public double FloatingProfit { get => _floatingProfit; private set { _floatingProfit = value; OnPropertyChanged(); } }
        public double PointsMoved { get => _pointsMoved; private set { _pointsMoved = value; OnPropertyChanged(); } }
        public string StopLossDisplay => StopLoss > 0 ? StopLoss.ToString($"F{Digits}", CultureInfo.InvariantCulture) : "None";
        public string TakeProfitDisplay => TakeProfit > 0 ? TakeProfit.ToString($"F{Digits}", CultureInfo.InvariantCulture) : "None";
        public string EditSlPriceText { get => _editSlPriceText; set { _editSlPriceText = value; OnPropertyChanged(); } }
        public string EditTpPriceText { get => _editTpPriceText; set { _editTpPriceText = value; OnPropertyChanged(); } }
        public string EditSlPointsText { get => _editSlPointsText; set { _editSlPointsText = value; OnPropertyChanged(); } }
        public string EditTpPointsText { get => _editTpPointsText; set { _editTpPointsText = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void RecalculateProfit()
        {
            double difference = Direction == "BUY" ? CurrentPrice - EntryPrice : EntryPrice - CurrentPrice;
            double point = Point > 0 ? Point : Math.Pow(10, -Math.Max(0, Digits));
            PointsMoved = point > 0 ? difference / point : 0;
            double tickSize = TickSize > 0 ? TickSize : point;
            double tickValue = TickValuePerLot > 0 ? TickValuePerLot : tickSize * Math.Max(0, ContractSize);
            FloatingProfit = tickSize > 0
                ? difference / tickSize * tickValue * Volume
                : difference * Volume * ContractSize;
        }
        public void InitializeEditValues()
        {
            EditSlPriceText = StopLoss > 0 ? StopLoss.ToString($"F{Digits}", CultureInfo.InvariantCulture) : string.Empty;
            EditTpPriceText = TakeProfit > 0 ? TakeProfit.ToString($"F{Digits}", CultureInfo.InvariantCulture) : string.Empty;
            EditSlPointsText = string.Empty;
            EditTpPointsText = string.Empty;
        }
        public void NotifyAll()
        {
            OnPropertyChanged(nameof(StopLoss)); OnPropertyChanged(nameof(TakeProfit)); OnPropertyChanged(nameof(CurrentPrice));
            OnPropertyChanged(nameof(CurrentBid)); OnPropertyChanged(nameof(CurrentAsk)); OnPropertyChanged(nameof(PointsMoved));
            OnPropertyChanged(nameof(FloatingProfit)); OnPropertyChanged(nameof(StopLossDisplay)); OnPropertyChanged(nameof(TakeProfitDisplay));
        }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class DemoPendingOrder : INotifyPropertyChanged
    {
        private string _status = "Waiting";
        private bool _isStopLimitActivated;
        public int Id { get; set; }
        public int ChartPaneId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string OrderType { get; set; } = "Buy Limit";
        public double Volume { get; set; }
        public double EntryPrice { get; set; }
        public double StopLimitPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public double Point { get; set; }
        public int Digits { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpirationUtc { get; set; }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public bool IsStopLimitActivated { get => _isStopLimitActivated; set { _isStopLimitActivated = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyAll()
        {
            foreach (string property in new[] { nameof(OrderType), nameof(Volume), nameof(EntryPrice), nameof(StopLimitPrice), nameof(StopLoss), nameof(TakeProfit), nameof(ExpirationUtc), nameof(Status), nameof(IsStopLimitActivated) })
                OnPropertyChanged(property);
        }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class DemoTradeRecord
    {
        public int Id { get; set; }
        public int ChartPaneId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public double Volume { get; set; }
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public DateTime OpenedUtc { get; set; }
        public DateTime ClosedUtc { get; set; }
        public long OpenedServerUnix { get; set; }
        public long ClosedServerUnix { get; set; }
        public double Profit { get; set; }
        public string CloseReason { get; set; } = string.Empty;
    }
}
