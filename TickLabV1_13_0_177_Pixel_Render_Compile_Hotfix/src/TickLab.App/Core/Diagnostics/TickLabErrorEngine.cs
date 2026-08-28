using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using TickLab.Desktop.Windows;

namespace TickLab.Core.Diagnostics;

public static class TickLabErrorEngine
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };
    private static readonly Dictionary<string, DateTimeOffset> RecentPopups =
        new(StringComparer.Ordinal);

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TickLab",
        "Logs");

    public static TickLabErrorReport Report(
        Exception exception,
        TickLabErrorContext context,
        TickLabErrorSeverity severity = TickLabErrorSeverity.Error,
        Window? owner = null,
        bool showPopup = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        string code = string.IsNullOrWhiteSpace(context.ErrorCode)
            ? BuildErrorCode(context.Operation, context.Stage)
            : context.ErrorCode.Trim();
        string reportId = $"{code}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var report = new TickLabErrorReport(
            reportId,
            DateTimeOffset.UtcNow,
            severity,
            context.Operation,
            context.Stage,
            GetUsefulMessage(exception),
            exception.ToString(),
            context.SuggestedAction,
            context.Symbol,
            context.Timeframe,
            context.ConnectorId,
            context.RequestId,
            context.FilePath,
            context.BlockStartUnix,
            context.BlockEndUnix,
            context.ExpectedRecords,
            context.ActualRecords,
            context.ExpectedFirstUnix,
            context.ActualFirstUnix,
            context.ExpectedLatestUnix,
            context.ActualLatestUnix,
            context.Mt5ErrorCode,
            context.AdditionalData ?? new Dictionary<string, string>());

        WriteLog(report);
        if (showPopup)
            Show(report, owner);
        return report;
    }

    public static TickLabErrorReport ReportMessage(
        string message,
        TickLabErrorContext context,
        TickLabErrorSeverity severity = TickLabErrorSeverity.Error,
        Window? owner = null,
        bool showPopup = true) =>
        Report(new InvalidOperationException(message), context, severity, owner, showPopup);

    public static void Show(TickLabErrorReport report, Window? owner = null)
    {
        Application? app = Application.Current;
        if (app is null)
            return;

        void ShowCore()
        {
            string signature = $"{report.Operation}|{report.Stage}|{report.Message}";
            lock (Sync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (string key in RecentPopups
                             .Where(item => now - item.Value > TimeSpan.FromMinutes(2))
                             .Select(item => item.Key)
                             .ToArray())
                {
                    RecentPopups.Remove(key);
                }

                if (RecentPopups.TryGetValue(signature, out DateTimeOffset last) &&
                    now - last < TimeSpan.FromSeconds(60))
                {
                    return;
                }
                RecentPopups[signature] = now;
            }

            var window = new ErrorDetailsWindow(report);
            Window? resolvedOwner = owner ?? app.Windows
                .OfType<Window>()
                .FirstOrDefault(item => item.IsActive && item.IsVisible);
            if (resolvedOwner is not null && resolvedOwner != window)
                window.Owner = resolvedOwner;
            window.ShowDialog();
        }

        if (app.Dispatcher.CheckAccess())
            ShowCore();
        else
            app.Dispatcher.Invoke(ShowCore);
    }

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = LogDirectory,
            UseShellExecute = true
        });
    }

    private static void WriteLog(TickLabErrorReport report)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                string jsonPath = Path.Combine(LogDirectory, $"errors-{report.OccurredUtc:yyyyMMdd}.jsonl");
                File.AppendAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

                string latestPath = Path.Combine(LogDirectory, "latest-error.txt");
                string temporaryPath = latestPath + ".tmp";
                File.WriteAllText(temporaryPath, report.ToDiagnosticText());
                File.Move(temporaryPath, latestPath, true);
            }
        }
        catch
        {
            // Error reporting must never create a second application failure.
        }
    }

    private static string GetUsefulMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null &&
               (current is AggregateException ||
                string.IsNullOrWhiteSpace(current.Message)))
        {
            current = current.InnerException;
        }
        return string.IsNullOrWhiteSpace(current.Message)
            ? current.GetType().Name
            : current.Message;
    }

    private static string BuildErrorCode(string operation, string stage)
    {
        static string Normalize(string value)
        {
            string compact = new(value
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .Take(10)
                .ToArray());
            return string.IsNullOrWhiteSpace(compact) ? "CORE" : compact;
        }

        return $"TL-{Normalize(operation)}-{Normalize(stage)}";
    }
}
