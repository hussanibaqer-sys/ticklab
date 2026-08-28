using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Windows;

public sealed class SyntheticChartSettingsWindow : Window
{
    private readonly ChartSettings _original;
    private readonly TextBox _boxSize;
    private readonly TextBox _rangeSize;
    private readonly TextBox _kagiReversal;
    private readonly TextBox _lineBreakCount;
    private readonly TextBox _pointFigureReversal;
    private readonly TextBox _renkoReversal;

    public SyntheticChartSettingsWindow(ChartSettings settings)
    {
        _original = settings;
        Title = "Synthetic Chart Settings";
        Width = 470;
        Height = 455;
        MinWidth = 430;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Synthetic chart parameters",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(heading);

        var panel = new Grid { Margin = new Thickness(0, 46, 0, 12) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        for (int index = 0; index < 7; index++)
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _boxSize = AddField(panel, 0, "Renko / Point & Figure box size (points)", settings.SyntheticBoxSizePoints);
        _rangeSize = AddField(panel, 1, "Range-bar size (points)", settings.RangeBarSizePoints);
        _kagiReversal = AddField(panel, 2, "Kagi reversal amount (points)", settings.KagiReversalPoints);
        _lineBreakCount = AddField(panel, 3, "Line Break count", settings.LineBreakCount);
        _pointFigureReversal = AddField(panel, 4, "Point & Figure reversal boxes", settings.PointAndFigureReversalBoxes);
        _renkoReversal = AddField(panel, 5, "Renko reversal boxes", settings.RenkoReversalBoxes);

        var note = new TextBlock
        {
            Text = "One point equals the symbol's MT5 point size. Changes affect only the selected chart.",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 184, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(note, 6);
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

    private static TextBox AddField(Grid grid, int row, string label, int value)
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
            Text = value.ToString(CultureInfo.InvariantCulture),
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(_boxSize, 1, 1_000_000, "Box size", out int boxSize) ||
            !TryRead(_rangeSize, 1, 1_000_000, "Range size", out int rangeSize) ||
            !TryRead(_kagiReversal, 1, 1_000_000, "Kagi reversal", out int kagi) ||
            !TryRead(_lineBreakCount, 1, 10, "Line Break count", out int lineBreak) ||
            !TryRead(_pointFigureReversal, 1, 10, "Point & Figure reversal", out int pnf) ||
            !TryRead(_renkoReversal, 1, 10, "Renko reversal", out int renko))
        {
            return;
        }

        Result = _original with
        {
            SyntheticBoxSizePoints = boxSize,
            RangeBarSizePoints = rangeSize,
            KagiReversalPoints = kagi,
            LineBreakCount = lineBreak,
            PointAndFigureReversalBoxes = pnf,
            RenkoReversalBoxes = renko
        };
        DialogResult = true;
    }

    private bool TryRead(TextBox box, int minimum, int maximum, string name, out int value)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum)
        {
            return true;
        }

        MessageBox.Show(this, $"{name} must be between {minimum:N0} and {maximum:N0}.",
            "Synthetic Chart Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }
}
