using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TickLab.Core.Market;

namespace TickLab.Desktop.Windows;

public enum HistoryOperationChoice
{
    ImportAll,
    ImportSpecificTimeframe,
    ImportM1Only,
    ImportTicksOnly,
    RefreshAll,
    RefreshSpecificTimeframe,
    RefreshCurrentTimeframeSource,
    RefreshM1Only,
    RefreshTicksOnly,
    VerifyAndRepair,
    RebuildGeneratedTimeframe,
    ManageCandleFiles
}

public partial class HistoryOperationOptionsWindow : Window
{
    private readonly bool _refresh;
    private readonly string _currentSourceTimeframe;
    private readonly List<RadioButton> _radios = new();
    private readonly ComboBox _timeframeBox = new();

    public HistoryOperationOptionsWindow(bool refresh, string currentSourceTimeframe)
    {
        InitializeComponent();
        _refresh = refresh;
        _currentSourceTimeframe = currentSourceTimeframe;
        TitleText.Text = refresh ? "Refresh and repair history" : "Import history";
        DescriptionText.Text = refresh
            ? "Choose exactly what TickLab should verify or refresh. Existing good history is preserved."
            : "Choose exactly what TickLab should import from MT5. Live capture continues during the operation.";
        BuildOptions();
    }

    public HistoryOperationChoice Choice { get; private set; }
    public string SelectedTimeframe { get; private set; } = "PERIOD_M1";
    public DateTime? StartDate { get; private set; }

    private void BuildOptions()
    {
        if (_refresh)
        {
            AddOption("Refresh all native candle history", HistoryOperationChoice.RefreshAll, true);
            AddOption("Refresh one native timeframe", HistoryOperationChoice.RefreshSpecificTimeframe);
            AddTimeframePicker();
            AddOption($"Refresh current chart source ({_currentSourceTimeframe})", HistoryOperationChoice.RefreshCurrentTimeframeSource);
            AddOption("Refresh M1 only", HistoryOperationChoice.RefreshM1Only);
            AddOption("Refresh raw ticks only", HistoryOperationChoice.RefreshTicksOnly);
            AddOption("Verify and repair candles plus raw ticks", HistoryOperationChoice.VerifyAndRepair);
            AddOption("Rebuild current generated timeframe from saved M1", HistoryOperationChoice.RebuildGeneratedTimeframe);
            AddOption("Open candle-file manager", HistoryOperationChoice.ManageCandleFiles);
        }
        else
        {
            AddOption("Import all native candles plus available raw ticks", HistoryOperationChoice.ImportAll, true);
            AddOption("Import one native timeframe", HistoryOperationChoice.ImportSpecificTimeframe);
            AddTimeframePicker();
            AddOption("Import M1 only", HistoryOperationChoice.ImportM1Only);
            AddOption("Import raw ticks only", HistoryOperationChoice.ImportTicksOnly);
        }
    }

    private void AddOption(string text, HistoryOperationChoice choice, bool selected = false)
    {
        var radio = new RadioButton
        {
            Content = text,
            Tag = choice,
            IsChecked = selected,
            Margin = new Thickness(0, 5, 0, 5),
            FontWeight = FontWeights.SemiBold
        };
        _radios.Add(radio);
        OptionsPanel.Children.Add(radio);
    }

    private void AddTimeframePicker()
    {
        _timeframeBox.ItemsSource = TimeframeDefinition.NativeMt5Timeframes;
        _timeframeBox.SelectedItem = "PERIOD_M1";
        _timeframeBox.Margin = new Thickness(24, 0, 0, 8);
        OptionsPanel.Children.Add(_timeframeBox);
    }

    private void StartDateToggle_Changed(object sender, RoutedEventArgs e) =>
        StartDateBox.IsEnabled = UseStartDateBox.IsChecked == true;

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        RadioButton? selected = _radios.FirstOrDefault(item => item.IsChecked == true);
        if (selected?.Tag is not HistoryOperationChoice choice)
        {
            StatusText.Text = "Choose an operation first.";
            return;
        }

        if (UseStartDateBox.IsChecked == true)
        {
            if (!DateTime.TryParseExact(
                    StartDateBox.Text.Trim(),
                    new[] { "dd-MM-yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime date))
            {
                StatusText.Text = "Enter the server date as dd-MM-yyyy.";
                return;
            }
            StartDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        }

        Choice = choice;
        SelectedTimeframe = _timeframeBox.SelectedItem?.ToString() ?? "PERIOD_M1";
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
