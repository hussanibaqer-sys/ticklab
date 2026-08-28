using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Indicators;

namespace TickLab.Desktop.Windows;

public sealed class IndicatorRouteWindow : Window
{
    private readonly ComboBox _targets;

    public IndicatorRouteWindow(
        string indicatorName,
        IndicatorRouteAction action,
        IReadOnlyList<IndicatorRouteTarget> targets,
        string? descriptionOverride = null)
    {
        Title = $"{action} indicator";
        Width = 690;
        Height = 270;
        MinWidth = 560;
        MinHeight = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brush("#111111");
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = $"{action}: {indicatorName}",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        });

        var explanation = new TextBlock
        {
            Text = descriptionOverride ?? action switch
            {
                IndicatorRouteAction.Connect => "Connect keeps the indicator settings linked between the source and destination charts.",
                IndicatorRouteAction.Copy => "Copy creates an independent indicator on the destination chart.",
                IndicatorRouteAction.Move => "Move transfers the indicator to the destination chart and removes it from the source chart.",
                _ => string.Empty
            },
            Foreground = Brush("#B8B8B8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14)
        };
        Grid.SetRow(explanation, 1);
        root.Children.Add(explanation);

        var targetPanel = new StackPanel();
        targetPanel.Children.Add(new TextBlock
        {
            Text = "Destination: Workspace → Partition → Chart",
            Margin = new Thickness(0, 0, 0, 6)
        });
        _targets = new ComboBox
        {
            ItemsSource = targets,
            DisplayMemberPath = nameof(IndicatorRouteTarget.DisplayText),
            SelectedIndex = targets.Count > 0 ? 0 : -1,
            Height = 34
        };
        IndicatorAddressSelectorStyle.Apply(_targets);
        targetPanel.Children.Add(_targets);
        Grid.SetRow(targetPanel, 2);
        root.Children.Add(targetPanel);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Button apply = MakeButton(action.ToString(), "#2F6DB2");
        apply.IsEnabled = targets.Count > 0;
        apply.Click += (_, _) =>
        {
            if (_targets.SelectedItem is not IndicatorRouteTarget selected)
                return;
            SelectedTarget = selected;
            DialogResult = true;
            Close();
        };
        Button cancel = MakeButton("Cancel", "#292929");
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        footer.Children.Add(apply);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
    }

    public IndicatorRouteTarget? SelectedTarget { get; private set; }

    private static Button MakeButton(string text, string background) => new()
    {
        Content = text,
        MinWidth = 90,
        Height = 32,
        Margin = new Thickness(6, 0, 0, 0),
        Padding = new Thickness(10, 4, 10, 4),
        Background = Brush(background),
        Foreground = Brushes.White,
        BorderBrush = Brush("#555555"),
        FocusVisualStyle = null
    };

    private static SolidColorBrush Brush(string value)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch { return new SolidColorBrush(Colors.Black); }
    }
}
