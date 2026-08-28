using System.Windows;
using System.Windows.Input;

namespace TickLab.Desktop.Windows;

public partial class PinPromptWindow : Window
{
    public PinPromptWindow(string target)
    {
        InitializeComponent();
        TargetText.Text = target;
        Loaded += (_, _) => PinBox.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        TryConfirm();
        e.Handled = true;
    }

    private void TryConfirm()
    {
        if (PinBox.Password == "159")
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "Incorrect PIN. Nothing was deleted.", "Delete blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
        PinBox.Clear();
        PinBox.Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
