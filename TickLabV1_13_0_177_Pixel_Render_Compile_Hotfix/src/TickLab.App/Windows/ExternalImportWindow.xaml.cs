using System.Globalization;
using System.IO;
using System.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop.Windows;

public partial class ExternalImportWindow : Window
{
    private readonly string _filePath;
    private readonly Mt5ConnectorSummary _connector;

    public ExternalImportWindow(
        string filePath,
        Mt5ConnectorSummary connector,
        string suggestedSymbol)
    {
        InitializeComponent();
        _filePath = filePath;
        _connector = connector;
        FileNameText.Text = Path.GetFileName(filePath);
        DatasetNameTextBox.Text = Path.GetFileNameWithoutExtension(filePath);
        SymbolTextBox.Text = suggestedSymbol;
        SourceTextBox.Text = string.IsNullOrWhiteSpace(connector.Broker)
            ? "External file"
            : connector.Broker;
        UtcOffsetTextBox.Text = connector.ServerUtcOffsetMinutes
            .ToString(CultureInfo.InvariantCulture);
    }

    public ExternalImportOptions? Options { get; private set; }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        string symbol = SymbolTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ValidationText.Text = "Enter the symbol represented by this file.";
            return;
        }

        if (!int.TryParse(
                UtcOffsetTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int offset) ||
            offset < -24 * 60 || offset > 24 * 60)
        {
            ValidationText.Text = "Enter the server UTC offset in minutes, for example 120 for UTC+2.";
            return;
        }

        if (!int.TryParse(
                PriorityTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int priority))
        {
            ValidationText.Text = "Whole-dataset priority must be an integer.";
            return;
        }

        Options = new ExternalImportOptions(
            _connector.ConnectorId,
            symbol,
            DatasetNameTextBox.Text.Trim(),
            SourceTextBox.Text.Trim(),
            _connector.Digits,
            _connector.Point,
            _connector.TickSize > 0 ? _connector.TickSize : _connector.Point,
            offset,
            TimeZoneVerifiedCheckBox.IsChecked == true,
            SourceMatchesBrokerCheckBox.IsChecked == true,
            priority);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
