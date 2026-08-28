using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Windows;

public sealed class OrderFlowSettingsWindow : Window
{
    private readonly ChartSettings _original;
    private readonly TextBox _tpoBracket;
    private readonly TextBox _profileRows;
    private readonly TextBox _sessionHour;
    private readonly TextBox _footprintStep;
    private readonly TextBox _valueArea;
    private readonly CheckBox _showValueArea;
    private readonly CheckBox _showDelta;

    public OrderFlowSettingsWindow(ChartSettings settings)
    {
        _original = settings;
        Title = "TPO and Volume Profile Settings";
        Width = 500;
        Height = 510;
        MinWidth = 460;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Market profile and footprint parameters",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var panel = new Grid { Margin = new Thickness(0, 48, 0, 12) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (int index = 0; index < 8; index++)
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _tpoBracket = AddField(panel, 0, "TPO bracket duration (minutes)", settings.TpoBracketMinutes.ToString(CultureInfo.InvariantCulture));
        _profileRows = AddField(panel, 1, "TPO price rows", settings.MarketProfileRows.ToString(CultureInfo.InvariantCulture));
        _sessionHour = AddField(panel, 2, "Session start hour (broker time, 0–23)", settings.ProfileSessionStartHour.ToString(CultureInfo.InvariantCulture));
        _footprintStep = AddField(panel, 3, "Footprint price step (MT5 points)", settings.FootprintPriceStepPoints.ToString(CultureInfo.InvariantCulture));
        _valueArea = AddField(panel, 4, "Volume-profile value area (%)", settings.VolumeProfileValueAreaPercent.ToString("0.##", CultureInfo.InvariantCulture));
        _showValueArea = AddCheck(panel, 5, "Show session value-area shading", settings.ShowVolumeProfileValueArea);
        _showDelta = AddCheck(panel, 6, "Show candle footprint delta", settings.ShowFootprintDelta);

        var note = new TextBlock
        {
            Text = "TPO uses candle time and price only. Session Volume Profile and Volume Footprint require saved ticks with real traded volume. Changes affect only the selected chart.",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 184, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(note, 7);
        Grid.SetColumnSpan(note, 2);
        panel.Children.Add(note);
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", Width = 90, Height = 32, IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    public ChartSettings? Result { get; private set; }

    private static TextBox AddField(Grid grid, int row, string label, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 7, 14, 7)
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        var box = new TextBox
        {
            Text = value,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            HorizontalContentAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return box;
    }

    private static CheckBox AddCheck(Grid grid, int row, string label, bool value)
    {
        var check = new CheckBox
        {
            Content = label,
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(check, row);
        Grid.SetColumnSpan(check, 2);
        grid.Children.Add(check);
        return check;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadInt(_tpoBracket, 1, 240, "TPO bracket", out int bracket) ||
            !TryReadInt(_profileRows, 12, 200, "TPO rows", out int rows) ||
            !TryReadInt(_sessionHour, 0, 23, "Session start hour", out int hour) ||
            !TryReadInt(_footprintStep, 1, 1_000_000, "Footprint price step", out int step) ||
            !TryReadDouble(_valueArea, 1.0, 99.0, "Value area", out double valueArea))
        {
            return;
        }

        Result = _original with
        {
            TpoBracketMinutes = bracket,
            MarketProfileRows = rows,
            ProfileSessionStartHour = hour,
            FootprintPriceStepPoints = step,
            VolumeProfileValueAreaPercent = valueArea,
            ShowVolumeProfileValueArea = _showValueArea.IsChecked == true,
            ShowFootprintDelta = _showDelta.IsChecked == true
        };
        DialogResult = true;
    }

    private bool TryReadInt(TextBox box, int minimum, int maximum, string name, out int value)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum)
        {
            return true;
        }

        MessageBox.Show(this, $"{name} must be between {minimum:N0} and {maximum:N0}.",
            "TPO and Volume Profile Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }

    private bool TryReadDouble(TextBox box, double minimum, double maximum, string name, out double value)
    {
        if (double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= minimum && value <= maximum)
        {
            return true;
        }

        MessageBox.Show(this, $"{name} must be between {minimum:0.##} and {maximum:0.##}.",
            "TPO and Volume Profile Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }
}
