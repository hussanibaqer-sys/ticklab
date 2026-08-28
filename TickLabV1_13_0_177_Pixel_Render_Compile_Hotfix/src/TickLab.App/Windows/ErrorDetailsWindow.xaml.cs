using System.Windows;
using TickLab.Core.Diagnostics;

namespace TickLab.Desktop.Windows;

public partial class ErrorDetailsWindow : Window
{
    private readonly TickLabErrorReport _report;

    public ErrorDetailsWindow(TickLabErrorReport report)
    {
        InitializeComponent();
        _report = report;
        SeverityText.Text = report.Severity.ToString().ToUpperInvariant();
        TitleText.Text = $"{report.Operation} failed at {report.Stage}";
        ErrorCodeText.Text = report.ReportId;
        MessageText.Text = report.Message;
        ActionText.Text = report.SuggestedAction;
        DiagnosticsBox.Text = report.ToDiagnosticText();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_report.ToDiagnosticText());
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        TickLabErrorEngine.OpenLogFolder();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
