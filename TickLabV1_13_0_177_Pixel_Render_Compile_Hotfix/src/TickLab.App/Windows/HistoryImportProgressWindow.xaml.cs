using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace TickLab.Desktop.Windows;

public partial class HistoryImportProgressWindow : Window
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly List<string> _completed = new();
    private bool _allowClose;
    private bool _paused;

    public HistoryImportProgressWindow(string operation, string symbol, int totalPhases)
    {
        InitializeComponent();
        TitleText.Text = $"{operation} native MT5 history";
        SubtitleText.Text = $"{symbol}  •  {totalPhases:N0} sequential phases  •  oldest candle → latest closed candle";
    }

    public event EventHandler? QuickRefreshRequested;
    public event EventHandler? RetryChartLaunchRequested;
    public event Action<bool>? PauseChanged;
    public event EventHandler? RestartAllRequested;
    public event EventHandler? CompletionAcknowledged;

    public void BeginPhase(string timeframe)
    {
        CurrentTimeframeText.Text = FriendlyTimeframe(timeframe);
        CurrentStatusText.Text = "Preparing import";
        CurrentPercentText.Text = "0.0%";
        CurrentProgressBar.Value = 0;
        ChartLaunchProgressBar.Value = 0;
        ChartLaunchPercentText.Text = "0.0%";
        ChartLaunchStatusText.Text = "Waiting for MT5 import";
        ChartLaunchDetailText.Text =
            "After import reaches 100%, TickLab saves and verifies it first. Chart launch is separate and can never block the next timeframe.";
        CompletionButton.Visibility = Visibility.Collapsed;
        QuickRefreshButton.IsEnabled = true;
        PauseButton.IsEnabled = true;
        RestartAllButton.IsEnabled = true;
    }

    public void UpdateChartLaunch(
        string timeframe,
        double percent,
        double overallPercent,
        string status,
        string detail)
    {
        percent = Math.Clamp(percent, 0, 100);
        overallPercent = Math.Clamp(overallPercent, 0, 100);
        CurrentTimeframeText.Text = FriendlyTimeframe(timeframe);
        ChartLaunchProgressBar.Value = percent;
        ChartLaunchPercentText.Text = $"{percent:0.0}%";
        ChartLaunchStatusText.Text = status;
        ChartLaunchDetailText.Text = detail;
        OverallProgressBar.Value = overallPercent;
        OverallProgressText.Text = $"{overallPercent:0.0}%";
        CurrentStatusText.Text = status;
        MessageText.Text = detail;
        SpeedText.Text = "Local disk / chart";
    }

    public void SetChartLaunchDeferred(string timeframe, string message, int pendingCount)
    {
        CurrentTimeframeText.Text = FriendlyTimeframe(timeframe);
        CurrentStatusText.Text = "History safe — import continues";
        ChartLaunchStatusText.Text = "Chart launch deferred";
        ChartLaunchDetailText.Text = message;
        MessageText.Text = message;
        SpeedText.Text = "History queue continuing";
        RetryChartLaunchButton.IsEnabled = pendingCount > 0;
        RestartAllButton.IsEnabled = true;
    }

    public void SetPendingChartLaunchCount(int pendingCount)
    {
        RetryChartLaunchButton.IsEnabled = pendingCount > 0;
        RetryChartLaunchButton.Content = pendingCount > 0
            ? $"Retry Chart Launch ({pendingCount:N0})"
            : "Retry Chart Launch";
    }

    public void SetChartLaunchRetrySucceeded(string timeframe, int pendingCount)
    {
        CurrentTimeframeText.Text = FriendlyTimeframe(timeframe);
        ChartLaunchProgressBar.Value = 100;
        ChartLaunchPercentText.Text = "100.0%";
        ChartLaunchStatusText.Text = "Chart launch succeeded";
        ChartLaunchDetailText.Text = pendingCount == 0
            ? "The saved timeframe is visible. No chart launches remain pending."
            : $"The saved timeframe is visible. {pendingCount:N0} other chart launch(es) remain pending.";
        MessageText.Text = ChartLaunchDetailText.Text;
        SetPendingChartLaunchCount(pendingCount);
    }

    public void UpdateProgress(
        string timeframe,
        string status,
        double phasePercent,
        double overallPercent,
        long currentUnix,
        int exportedBars,
        int targetBars,
        double speedBarsPerSecond,
        long blockStartUnix,
        long blockEndUnix,
        int retries,
        string message,
        long serverFirstUnix = 0,
        long availableFirstUnix = 0,
        bool nativeRangePartial = false,
        string coverageReason = "",
        int lastErrorCode = 0,
        string failureCode = "",
        string failureStage = "",
        int failureExpectedBars = 0,
        int failureActualBars = 0,
        long failureExpectedFirstUnix = 0,
        long failureActualFirstUnix = 0,
        long failureExpectedLatestUnix = 0,
        long failureActualLatestUnix = 0,
        string failureFilePath = "")
    {
        phasePercent = Math.Clamp(phasePercent, 0, 100);
        overallPercent = Math.Clamp(overallPercent, 0, 100);

        CurrentTimeframeText.Text = FriendlyTimeframe(timeframe);
        CurrentStatusText.Text = FriendlyStatus(status);
        CurrentPercentText.Text = $"{phasePercent:0.0}%";
        CurrentProgressBar.Value = phasePercent;
        OverallProgressText.Text = $"{overallPercent:0.0}%";
        OverallProgressBar.Value = overallPercent;
        CurrentDateText.Text = FormatUnix(currentUnix);
        CandleCountText.Text = targetBars > 0
            ? $"{exportedBars:N0} / {targetBars:N0}"
            : $"{exportedBars:N0} copied";
        SpeedText.Text = speedBarsPerSecond > 0.01
            ? $"{speedBarsPerSecond:N0} candles/s"
            : FriendlyIdleSpeed(status);
        ElapsedText.Text = (DateTime.UtcNow - _startedUtc).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        BlockText.Text = blockStartUnix > 0 && blockEndUnix > 0
            ? $"{FormatUnix(blockStartUnix, true)} → {FormatUnix(blockEndUnix, true)}"
            : "Scanning boundaries";
        RetryText.Text = retries.ToString("N0", CultureInfo.CurrentCulture);

        if (availableFirstUnix > 0 || serverFirstUnix > 0)
        {
            string available = FormatUnix(availableFirstUnix > 0 ? availableFirstUnix : serverFirstUnix, true);
            string server = FormatUnix(serverFirstUnix, true);
            NativeCoverageText.Text = nativeRangePartial
                ? $"Native MT5: {available} → latest  •  server begins {server}  •  older gap uses smaller saved candles"
                : $"Native MT5: {available} → latest  •  full requested range";
            if (lastErrorCode != 0)
                NativeCoverageText.Text += $"  •  MT5 code {lastErrorCode}";
            NativeCoverageText.ToolTip = string.IsNullOrWhiteSpace(coverageReason)
                ? null
                : coverageReason;
        }
        else
        {
            NativeCoverageText.Text = "Native MT5 range: discovering…";
            NativeCoverageText.ToolTip = null;
        }

        string baseMessage = string.IsNullOrWhiteSpace(message)
            ? "TickLab is verifying the current MT5 timestamp block."
            : message;
        if (!string.IsNullOrWhiteSpace(failureCode) ||
            !string.IsNullOrWhiteSpace(failureStage))
        {
            string expectedRange = failureExpectedFirstUnix > 0 || failureExpectedLatestUnix > 0
                ? $"Expected range: {FormatUnix(failureExpectedFirstUnix)} → {FormatUnix(failureExpectedLatestUnix)}"
                : "Expected range: —";
            string actualRange = failureActualFirstUnix > 0 || failureActualLatestUnix > 0
                ? $"Actual range: {FormatUnix(failureActualFirstUnix)} → {FormatUnix(failureActualLatestUnix)}"
                : "Actual range: —";
            MessageText.Text =
                $"{baseMessage}\n\nERROR {failureCode}  •  Stage: {failureStage}\n" +
                $"Candles: expected {failureExpectedBars:N0}, actual {failureActualBars:N0}\n" +
                $"{expectedRange}\n{actualRange}" +
                (string.IsNullOrWhiteSpace(failureFilePath)
                    ? string.Empty
                    : $"\nFile: {failureFilePath}");
        }
        else
        {
            MessageText.Text = baseMessage;
        }
    }

    public void MarkPhaseCompleted(string timeframe)
    {
        string friendly = FriendlyTimeframe(timeframe);
        if (!_completed.Contains(friendly, StringComparer.OrdinalIgnoreCase))
            _completed.Add(friendly);
        CompletedTimeframesText.Text = _completed.Count == 0
            ? "Completed: none"
            : $"Completed ({_completed.Count:N0}): {string.Join("  •  ", _completed)}";
    }

    public void ResetForRestart()
    {
        _completed.Clear();
        CompletedTimeframesText.Text = "Completed: none — full rescan requested";
        OverallProgressBar.Value = 0;
        OverallProgressText.Text = "0.0%";
        ChartLaunchProgressBar.Value = 0;
        ChartLaunchPercentText.Text = "0.0%";
        ChartLaunchStatusText.Text = "Waiting for M1 import";
        ChartLaunchDetailText.Text = "Restarting sequential MT5 import from M1. Verified saves release the queue before chart launch.";
        MessageText.Text = "Restarting from M1. Existing correct permanent candles are retained and deduplicated.";
    }

    public void SetCompleted(string message, int pendingChartLaunches)
    {
        OverallProgressBar.Value = 100;
        OverallProgressText.Text = "100.0%";
        CurrentProgressBar.Value = 100;
        CurrentPercentText.Text = "100.0%";
        ChartLaunchProgressBar.Value = 100;
        ChartLaunchPercentText.Text = "100.0%";
        CurrentStatusText.Text = "History imported successfully";
        ChartLaunchStatusText.Text = pendingChartLaunches == 0
            ? "History saved, verified and committed"
            : $"History safe — {pendingChartLaunches:N0} chart launch(es) pending";
        ChartLaunchDetailText.Text = pendingChartLaunches == 0
            ? "All imported history is safe. Any remaining chart refresh or replay indexing continues separately and cannot block this confirmation."
            : "All imported history is safe. Retry Chart Launch can restore pending previews without repeating the MT5 import.";
        MessageText.Text = message;
        SpeedText.Text = "Completed";
        CompletionButton.Visibility = Visibility.Visible;
        CompletionButton.IsEnabled = true;
        QuickRefreshButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        RestartAllButton.IsEnabled = false;
        SetPendingChartLaunchCount(pendingChartLaunches);
        Activate();
    }

    public void SetFailed(string message)
    {
        CurrentStatusText.Text = "Paused — needs attention";
        ChartLaunchStatusText.Text = "Operation paused";
        ChartLaunchDetailText.Text = message;
        MessageText.Text = message;
        SpeedText.Text = "Stopped";
        QuickRefreshButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
        RestartAllButton.IsEnabled = true;
    }

    public void CloseAfterOperation()
    {
        _allowClose = true;
        Close();
    }

    private void QuickRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "Retry Current Stage requested. Completed blocks are preserved.";
        QuickRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RetryChartLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "Retrying a chart preview from already verified permanent history. MT5 import will not repeat.";
        RetryChartLaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "Resume" : "Pause";
        CurrentStatusText.Text = _paused ? "Paused" : "Resuming";
        PauseChanged?.Invoke(_paused);
    }

    private void RestartAllButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            "Restart the sequential import from M1? Correct permanent candles already saved will be kept and deduplicated.",
            "Restart All Import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _paused = false;
        PauseButton.Content = "Pause";
        ResetForRestart();
        RestartAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        string diagnostics = $"TickLab history diagnostics\nStarted UTC: {_startedUtc:O}\nTimeframe: {CurrentTimeframeText.Text}\nStatus: {CurrentStatusText.Text}\nChart stage: {ChartLaunchStatusText.Text}\nChart detail: {ChartLaunchDetailText.Text}\nMessage: {MessageText.Text}\nOverall: {OverallProgressText.Text}\nCurrent: {CurrentPercentText.Text}\nChart: {ChartLaunchPercentText.Text}";
        Clipboard.SetText(diagnostics);
        MessageText.Text = "Diagnostics copied to the clipboard.";
    }

    private void CompletionButton_Click(object sender, RoutedEventArgs e)
    {
        CompletionAcknowledged?.Invoke(this, EventArgs.Empty);
        CloseAfterOperation();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    private static string FriendlyIdleSpeed(string status) => status switch
    {
        "discovering_native_range" => "Discovering MT5 range",
        "waiting_for_latest_candle" => "Waiting for latest candle",
        "awaiting_desktop_commit" => "Saving to TickLab",
        "saving_to_ticklab" => "Validating file",
        "desktop_committed" => "Verified",
        "verifying" or "verifying_block" => "Verifying",
        "paused" => "Paused",
        "stuck_block" or "verification_failed" => "Stopped",
        _ => "Waiting for MT5"
    };

    private static string FriendlyStatus(string status) => status switch
    {
        "discovering_native_range" => "Discovering maximum native range",
        "scanning_first_candle" => "Scanning first candle",
        "waiting_for_latest_candle" => "Scanning latest candle",
        "importing" => "Importing oldest → newest",
        "waiting_for_mt5" => "Waiting for MT5",
        "waiting_for_publish" => "Publishing verified history",
        "awaiting_desktop_commit" => "Saving into TickLab",
        "saving_to_ticklab" => "Validating permanent file",
        "desktop_committed" => "Permanent file verified",
        "stuck_block" => "Paused — block needs attention",
        "verification_failed" => "Paused — verification failed",
        "verifying_block" => "Verifying block",
        "verifying" => "Final verification",
        "candles_ready" => "Candles completed",
        "exporting_ticks" => "Copying raw ticks",
        "paused" => "Paused",
        "rescan_required" => "Rescanning boundaries",
        "ready" => "Completed",
        "cancelled" => "Restarting",
        "error" => "Needs attention",
        _ => status.Replace('_', ' ')
    };

    private static string FriendlyTimeframe(string timeframe) => timeframe switch
    {
        "RAW_TICKS" => "Raw ticks",
        "PERIOD_MN1" => "Monthly",
        "PERIOD_W1" => "Weekly",
        "PERIOD_D1" => "Daily",
        _ when timeframe.StartsWith("PERIOD_", StringComparison.Ordinal) => timeframe[7..],
        _ => timeframe
    };

    private static string FormatUnix(long unix, bool dateOnly = false)
    {
        if (unix <= 0)
            return "—";
        try
        {
            DateTimeOffset value = DateTimeOffset.FromUnixTimeSeconds(unix);
            return value.ToString(dateOnly ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }
}
