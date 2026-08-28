using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Alerts;

namespace TickLab.Desktop.Windows;

public sealed class AlertTriggeredWindow : Window
{
    private AlertBellPlayer? _bellPlayer;

    public AlertTriggeredWindow(string title, string message, bool repeatSound, string headingText)
    {
        Title = "TickLab Alert";
        Width = 460;
        MinHeight = 300;
        MaxHeight = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(10, 10, 10));
        Foreground = Brushes.White;
        Topmost = true;

        var root = new Grid { Margin = new Thickness(24, 22, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bell = new TextBlock
        {
            Text = "🔔",
            FontSize = 42,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(bell);

        var heading = new TextBlock
        {
            Text = headingText,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetRow(heading, 1);
        root.Children.Add(heading);

        var detail = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? message : $"{title}\n{message}",
            Foreground = new SolidColorBrush(Color.FromRgb(205, 210, 218)),
            FontSize = 14,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detailHost = new Border
        {
            MinHeight = 92,
            MaxHeight = 250,
            Padding = new Thickness(12, 16, 12, 16),
            Margin = new Thickness(4, 10, 4, 12),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = detail
            }
        };
        Grid.SetRow(detailHost, 2);
        root.Children.Add(detailHost);

        var ok = new Button
        {
            Content = "OK",
            Width = 92,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsDefault = true
        };
        ok.Click += (_, _) => Close();
        Grid.SetRow(ok, 3);
        root.Children.Add(ok);

        Content = root;

        Loaded += (_, _) =>
        {
            if (repeatSound)
            {
                try
                {
                    _bellPlayer = new AlertBellPlayer();
                    _bellPlayer.PlayLooping();
                }
                catch
                {
                    AlertBellPlayer.PlayOnce();
                }
            }
            ok.Focus();
        };
        Closed += (_, _) =>
        {
            _bellPlayer?.Stop();
            _bellPlayer?.Dispose();
            _bellPlayer = null;
        };
    }
}
