using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TickLab.Core.Market;

namespace TickLab.Desktop.Windows;

public partial class CandleMarkerWindow : Window
{
    private Candle? _selectedCandle;
    private Candle? _latestChartCandle;
    private IReadOnlyList<CandleMarker> _markers = Array.Empty<CandleMarker>();
    private IReadOnlyList<string> _symbolOptions = Array.Empty<string>();
    private static readonly IReadOnlyList<string> FindCandleTimeframes = new[]
    {
        "Tick", "1s", "15s", "30s", "45s"
    }.Concat(TimeframeDefinition.NativeMt5Timeframes).ToArray();
    private bool _allowClose;
    private string _lastAutoNavigatedMarkerId = string.Empty;
    private bool _markModeActive;
    private bool _findInProgress;

    public CandleMarkerWindow(bool receiveEnabled)
    {
        InitializeComponent();
        TimeframeBox.ItemsSource = FindCandleTimeframes;
        TimeframeBox.SelectedItem = "PERIOD_M1";
        ReceiveToggle.IsChecked = receiveEnabled;
        UpdateReceiveUi();
    }

    public event Action<bool>? ReceiveChanged;
    public event Action<MarkerDraft>? FindRequested;
    public event Action<bool, MarkerDraft?>? MarkModeChanged;
    public event Action<MarkerDraft>? ExportRequested;
    public event Action<CandleMarker>? GoToRequested;
    public event Action<CandleMarker>? RemoveRequested;
    public event Action? ClearExportedRequested;

    public void SetChartContext(
        string currentSymbol,
        string currentTimeframe,
        Candle? selectedCandle,
        Candle? latestChartCandle,
        IEnumerable<string> lastUsedSymbols,
        IEnumerable<string> historySymbols,
        IEnumerable<string> mt5Symbols)
    {
        _selectedCandle = selectedCandle;
        _latestChartCandle = latestChartCandle;

        _symbolOptions = new[] { currentSymbol }
            .Concat(lastUsedSymbols)
            .Concat(historySymbols)
            .Concat(mt5Symbols)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SymbolBox.ItemsSource = _symbolOptions;

        Candle? preferredCandle = selectedCandle ?? latestChartCandle;
        if (preferredCandle is not null)
        {
            FillFromCandle(preferredCandle, selectedCandle is not null);
            return;
        }

        string fallbackSymbol = !string.IsNullOrWhiteSpace(currentSymbol)
            ? currentSymbol
            : _symbolOptions.FirstOrDefault() ?? string.Empty;
        SymbolBox.Text = fallbackSymbol;

        string? matchingTimeframe = FindCandleTimeframes
            .FirstOrDefault(item => string.Equals(item, currentTimeframe, StringComparison.OrdinalIgnoreCase));
        if (matchingTimeframe is not null)
            TimeframeBox.SelectedItem = matchingTimeframe;

        if (string.IsNullOrWhiteSpace(fallbackSymbol))
            StatusText.Text = "No active or saved symbol was found. Select a symbol from the list after connecting MT5 or importing history.";
        else
            StatusText.Text = $"Using chart/fallback symbol: {fallbackSymbol}. Select a candle or enter its server date and time.";
    }

    public void SetSelectedCandle(Candle? candle)
    {
        _selectedCandle = candle;
        if (candle is null)
            return;

        FillFromCandle(candle, explicitlySelected: true);
    }

    public void SetMarkers(IReadOnlyList<CandleMarker> markers)
    {
        _markers = markers;
        MarkerList.ItemsSource = markers
            .OrderByDescending(item => item.StartUnix)
            .Select(item => new MarkerRow(item))
            .ToArray();
    }

    public void SetStatus(string status) => StatusText.Text = status;

    public void SetFindingState(bool finding)
    {
        _findInProgress = finding;
        FindButton.IsEnabled = !finding;
        FindButtonText.Text = finding ? "Finding…" : GetIdleFindButtonText();
        FindSpinner.Visibility = finding ? Visibility.Visible : Visibility.Collapsed;

        if (finding)
        {
            var animation = new DoubleAnimation(
                0.0,
                360.0,
                new Duration(TimeSpan.FromMilliseconds(850)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            FindSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
        }
        else
        {
            FindSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            FindSpinnerRotate.Angle = 0.0;
        }
    }

    private string GetIdleFindButtonText() =>
        string.Equals(TimeframeBox.SelectedItem?.ToString(), "Tick", StringComparison.OrdinalIgnoreCase)
            ? "Find tick"
            : "Find candle";

    private void TimeframeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_findInProgress && FindButtonText is not null)
            FindButtonText.Text = GetIdleFindButtonText();
    }

    private void SymbolBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_symbolOptions.Count > 0)
            SymbolBox.IsDropDownOpen = true;
    }

    private void ReceiveToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateReceiveUi();
        ReceiveChanged?.Invoke(ReceiveToggle.IsChecked == true);
    }

    private void UpdateReceiveUi()
    {
        bool enabled = ReceiveToggle.IsChecked == true;
        ReceiveDot.Fill = new SolidColorBrush(enabled
            ? Color.FromRgb(34, 197, 94)
            : Color.FromRgb(100, 116, 139));
        ReceiveText.Text = enabled ? "Receive ON" : "Receive OFF";
    }

    private void UseSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCandle is null)
        {
            StatusText.Text = "Double-click a candle on the TickLab chart first.";
            return;
        }

        FillFromCandle(_selectedCandle, true);
    }

    private void FillFromCandle(Candle candle, bool explicitlySelected)
    {
        SymbolBox.Text = candle.Symbol;
        DateBox.Text = candle.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeBox.Text = candle.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        string? matchingTimeframe = FindCandleTimeframes
            .FirstOrDefault(item => string.Equals(item, candle.Timeframe, StringComparison.OrdinalIgnoreCase));
        if (matchingTimeframe is not null)
        {
            TimeframeBox.SelectedItem = matchingTimeframe;
            StatusText.Text = explicitlySelected
                ? $"Selected candle: {candle.Symbol} {candle.Timeframe} {DateBox.Text} {TimeBox.Text}"
                : $"Using the latest visible chart candle: {candle.Symbol} {candle.Timeframe} {DateBox.Text} {TimeBox.Text}";
        }
        else
        {
            TimeframeBox.SelectedItem = null;
            StatusText.Text = $"{candle.Timeframe} is not available in Find Candle.";
        }
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        if (_findInProgress)
            return;

        if (TryReadDraft(out MarkerDraft? draft))
            FindRequested?.Invoke(draft!);
    }

    private void MarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_markModeActive)
        {
            SetMarkMode(false);
            MarkModeChanged?.Invoke(false, null);
            return;
        }

        if (!TryReadDraft(out MarkerDraft? draft))
            return;
        if (string.Equals(draft!.Timeframe, "Tick", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Tick is a Find-only target. Use Find candle to open and centre the Raw Tick chart.";
            return;
        }
        SetMarkMode(true);
        MarkModeChanged?.Invoke(true, draft);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDraft(out MarkerDraft? draft))
            return;
        MarkerDraft exportDraft = draft!;
        if (TryResolveFindTimeframe(exportDraft.Timeframe, out TimeframeDefinition exportTimeframe) &&
            exportTimeframe.Unit is TimeframeUnit.Tick or TimeframeUnit.Second)
        {
            StatusText.Text = "Tick/second timeframes are TickLab-local. Tick Find and second Find/Mark work here; MT5 export requires a native MT5 candle timeframe.";
            return;
        }
        ExportRequested?.Invoke(exportDraft);
    }

    public void SetMarkMode(bool enabled)
    {
        _markModeActive = enabled;
        MarkButton.Content = enabled ? "Unmark" : "Mark";
        MarkButton.Background = new SolidColorBrush(enabled
            ? Color.FromRgb(202, 138, 4)
            : Color.FromRgb(30, 41, 59));
        ExportButton.IsEnabled = enabled;
        if (!enabled)
            StatusText.Text = "Mark mode stopped.";
    }

    public void SetInteractiveMarker(Candle candle)
    {
        _selectedCandle = candle;
        FillFromCandle(candle, true);
        StatusText.Text = "Yellow selection line moved. Click Export to send this candle.";
    }

    private bool TryReadDraft(out MarkerDraft? draft)
    {
        draft = null;
        string symbol = (SymbolBox.Text ?? string.Empty).Trim();
        string timeframe = TimeframeBox.SelectedItem?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(symbol))
        {
            StatusText.Text = "Select or enter a symbol.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            StatusText.Text = "Select a timeframe.";
            return false;
        }
        if (!DateTime.TryParseExact(
                $"{DateBox.Text.Trim()} {TimeBox.Text.Trim()}",
                new[] { "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime timestamp))
        {
            StatusText.Text = "Enter server date as dd-MM-yyyy (or yyyy-MM-dd) and time as HH:mm:ss.";
            return false;
        }

        if (!TryResolveFindTimeframe(timeframe, out TimeframeDefinition resolvedTimeframe))
        {
            StatusText.Text = "Select a valid Find Candle timeframe.";
            return false;
        }

        long requestedUnix = new DateTimeOffset(
            DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)).ToUnixTimeSeconds();
        long exactCandleStart = resolvedTimeframe.GetBucketStartUnix(requestedUnix);
        string storedTimeframe = resolvedTimeframe.Unit == TimeframeUnit.Second
            ? resolvedTimeframe.DisplayText
            : resolvedTimeframe.NativeMt5Code ?? timeframe;

        // Keep both values: StartUnix is the candle bucket selected by the
        // Find Candle timeframe, while RequestedUnix is the exact date/time the
        // user typed. After the marker is placed, navigation is anchored to the
        // exact RequestedUnix so timeframe changes never lose seconds/minutes
        // merely because the initial lookup used a larger candle bucket.
        draft = new MarkerDraft(
            symbol,
            storedTimeframe,
            exactCandleStart,
            LabelBox.Text.Trim(),
            requestedUnix);
        return true;
    }

    private static bool TryResolveFindTimeframe(
        string? raw,
        out TimeframeDefinition timeframe)
    {
        timeframe = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string value = raw.Trim();
        if (string.Equals(value, "Tick", StringComparison.OrdinalIgnoreCase))
        {
            TimeframeDefinition? tick = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Tick);
            if (tick is not null)
            {
                timeframe = tick;
                return true;
            }
        }

        TimeframeDefinition? seconds = TimeframeDefinition.BuiltIns.FirstOrDefault(item =>
            item.Unit == TimeframeUnit.Second &&
            string.Equals(item.DisplayText, value, StringComparison.OrdinalIgnoreCase));
        if (seconds is not null)
        {
            timeframe = seconds;
            return true;
        }

        if (!TimeframeDefinition.NativeMt5Timeframes.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        timeframe = TimeframeDefinition.FromNativeMt5Code(value);
        return true;
    }

    private void MarkerList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MarkerList.SelectedItem is not MarkerRow row)
            return;

        SymbolBox.Text = row.Marker.Symbol;
        TimeframeBox.SelectedItem = row.Marker.Timeframe;
        DateBox.Text = row.Marker.NavigationTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeBox.Text = row.Marker.NavigationTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LabelBox.Text = row.Marker.Label;

        if (row.Marker.Source.StartsWith("MT5", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_lastAutoNavigatedMarkerId, row.Marker.Id, StringComparison.Ordinal))
        {
            _lastAutoNavigatedMarkerId = row.Marker.Id;
            StatusText.Text = "Opening the received MT5 candle…";
            GoToRequested?.Invoke(row.Marker);
        }
    }

    private void MarkerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerRow row)
            GoToRequested?.Invoke(row.Marker);
    }

    private void GoToButton_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerRow row)
            GoToRequested?.Invoke(row.Marker);
        else
            StatusText.Text = "Select a marker from the list first.";
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerRow row)
            RemoveRequested?.Invoke(row.Marker);
        else
            StatusText.Text = "Select a marker from the list first.";
    }

    private void ClearExportedButton_Click(object sender, RoutedEventArgs e) =>
        ClearExportedRequested?.Invoke();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    private sealed record MarkerRow(CandleMarker Marker)
    {
        public string Symbol => Marker.Symbol;
        public string Timeframe => Marker.Timeframe;
        public string DisplayTime => Marker.NavigationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public string Source => Marker.Source;
        public string Label => Marker.Label;
    }
}

public sealed record MarkerDraft(
    string Symbol,
    string Timeframe,
    long StartUnix,
    string Label,
    long? RequestedUnix = null);
