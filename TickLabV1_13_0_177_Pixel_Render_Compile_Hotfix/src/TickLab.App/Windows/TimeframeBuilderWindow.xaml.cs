using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TickLab.Core.Market;

namespace TickLab.Desktop.Windows;

public partial class TimeframeBuilderWindow : Window
{
    private static readonly Regex DigitsOnly = new("^[0-9]+$");

    public TimeframeBuilderWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            QuantityTextBox.Focus();
            QuantityTextBox.SelectAll();
        };
    }

    public TimeframeDefinition? SelectedTimeframe { get; private set; }

    private void QuantityTextBox_PreviewTextInput(
        object sender,
        TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void GenerateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (!int.TryParse(QuantityTextBox.Text.Trim(), out int quantity) ||
            quantity < 1 ||
            quantity > 100_000)
        {
            ValidationText.Text = "Enter a whole number from 1 to 100,000.";
            return;
        }

        if (UnitComboBox.SelectedItem is not ComboBoxItem selectedItem ||
            !Enum.TryParse(
                selectedItem.Tag?.ToString(),
                ignoreCase: false,
                out TimeframeUnit unit))
        {
            ValidationText.Text = "Choose a timeframe unit.";
            return;
        }

        try
        {
            SelectedTimeframe =
                TimeframeDefinition.CreateCustom(quantity, unit);
            DialogResult = true;
        }
        catch (ArgumentOutOfRangeException)
        {
            ValidationText.Text = unit == TimeframeUnit.Month
                ? "Month timeframes support numbers from 1 to 1,200."
                : "Enter a whole number from 1 to 100,000.";
        }
    }
}
