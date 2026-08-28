using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TickLab.Desktop;

public sealed class DemoTradeValuePromptWindow : Window
{
    private readonly TextBox _valueBox;
    private readonly double _fallbackValue;
    private readonly double _step;
    private readonly int _digits;

    public DemoTradeValuePromptWindow(
        string title,
        string message,
        double? initialValue = null,
        double? fallbackValue = null,
        double step = 1.0,
        int digits = 8)
    {
        _step = double.IsFinite(step) && step > 0 ? step : 1.0;
        _digits = Math.Clamp(digits, 0, 10);
        _fallbackValue = fallbackValue.HasValue && double.IsFinite(fallbackValue.Value) && fallbackValue.Value > 0
            ? fallbackValue.Value
            : _step;

        Title = title;
        Width = 430;
        Height = 235;
        MinWidth = 380;
        MinHeight = 215;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(16, 27, 43));
        Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 251));

        var root = new Grid { Margin = new Thickness(15) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(231, 238, 247)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(messageText);

        var valueRow = new Grid();
        valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(47) });

        double startingValue = initialValue.HasValue && double.IsFinite(initialValue.Value) && initialValue.Value > 0
            ? initialValue.Value
            : _fallbackValue;
        _valueBox = new TextBox
        {
            Text = FormatValue(startingValue),
            Height = 36,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            CaretBrush = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(118, 137, 160)),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        valueRow.Children.Add(_valueBox);

        var arrowPanel = new Grid { Margin = new Thickness(6, 0, 0, 0) };
        arrowPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        arrowPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var up = CreateArrowButton("▲", 1, "Hold to increase continuously");
        var down = CreateArrowButton("▼", -1, "Hold to decrease continuously");
        Grid.SetRow(down, 1);
        arrowPanel.Children.Add(up);
        arrowPanel.Children.Add(down);
        Grid.SetColumn(arrowPanel, 1);
        valueRow.Children.Add(arrowPanel);

        Grid.SetRow(valueRow, 1);
        root.Children.Add(valueRow);

        var hint = new TextBlock
        {
            Text = $"Step: {FormatValue(_step)} · The arrows repeat while held.",
            Foreground = new SolidColorBrush(Color.FromRgb(169, 185, 204)),
            FontSize = 10,
            Margin = new Thickness(0, 7, 0, 0)
        };
        Grid.SetRow(hint, 2);
        root.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = CreateActionButton("Cancel", isPrimary: false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.IsCancel = true;
        var apply = CreateActionButton("Apply", isPrimary: true);
        apply.IsDefault = true;
        apply.Click += (_, _) => ApplyValue(title);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _valueBox.Focus();
            _valueBox.SelectAll();
        };
    }

    public double Value { get; private set; }

    private RepeatButton CreateArrowButton(string text, int direction, string toolTip)
    {
        var button = new RepeatButton
        {
            Content = text,
            Delay = 320,
            Interval = 11,
            Padding = new Thickness(0),
            Margin = direction > 0 ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(31, 58, 95)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 124, 165)),
            ToolTip = toolTip + " · hold repeats 5× faster",
            Focusable = false
        };
        button.Click += (_, _) => AdjustValue(direction);
        return button;
    }

    private static Button CreateActionButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            Width = 88,
            Height = 31,
            Background = new SolidColorBrush(isPrimary
                ? Color.FromRgb(36, 88, 214)
                : Color.FromRgb(235, 239, 244)),
            Foreground = isPrimary ? Brushes.White : Brushes.Black,
            BorderBrush = new SolidColorBrush(isPrimary
                ? Color.FromRgb(72, 117, 224)
                : Color.FromRgb(142, 154, 169))
        };
    }

    private void AdjustValue(int direction)
    {
        double current = double.TryParse(_valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                         double.IsFinite(parsed) && parsed > 0
            ? parsed
            : _fallbackValue;
        double adjusted = Math.Max(_step, current + direction * _step);
        adjusted = Math.Round(adjusted, _digits, MidpointRounding.AwayFromZero);
        _valueBox.Text = FormatValue(adjusted);
        _valueBox.CaretIndex = _valueBox.Text.Length;
    }

    private string FormatValue(double value) => _digits == 0
        ? Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture)
        : value.ToString($"F{_digits}", CultureInfo.InvariantCulture);

    private void ApplyValue(string title)
    {
        if (!double.TryParse(_valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value <= 0)
        {
            MessageBox.Show(this, "Enter a number greater than zero.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            _valueBox.Focus();
            _valueBox.SelectAll();
            return;
        }
        Value = value;
        DialogResult = true;
    }
}
