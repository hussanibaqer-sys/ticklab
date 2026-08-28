using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TickLab.Desktop.Windows;

public sealed class AlertToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public AlertToastWindow(string title, string message)
    {
        Width = 430;
        MinHeight = 150;
        MaxHeight = 320;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 18, 16, 18)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 216, 226)),
            FontSize = 13.5,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(0, 9, 0, 1)
        });
        border.Child = stack;
        Content = border;

        Loaded += (_, _) =>
        {
            Rect work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 18;
            Top = work.Bottom - ActualHeight - 18;
        };
        MouseLeftButtonUp += (_, _) => Close();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        _timer.Start();
    }
}
