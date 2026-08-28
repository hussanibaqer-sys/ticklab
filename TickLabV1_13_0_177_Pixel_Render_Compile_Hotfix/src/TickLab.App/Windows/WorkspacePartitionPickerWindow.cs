using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TickLab.Desktop.Windows;

public sealed record WorkspacePartitionTarget(int WorkspaceId, int PartitionId, string OccupantTitle)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(OccupantTitle);
    public string DisplayText => IsEmpty
        ? $"Workspace {WorkspaceId} → Partition {PartitionId} — Empty"
        : $"Workspace {WorkspaceId} → Partition {PartitionId} — {OccupantTitle}";
}

public sealed class WorkspacePartitionPickerWindow : Window
{
    private readonly ComboBox _targets;

    public WorkspacePartitionPickerWindow(
        string title,
        string description,
        IReadOnlyList<WorkspacePartitionTarget> targets)
    {
        Title = title;
        Width = 690;
        Height = 280;
        MinWidth = 560;
        MinHeight = 240;
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
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        });

        var explanation = new TextBlock
        {
            Text = description,
            Foreground = Brush("#B8B8B8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14)
        };
        Grid.SetRow(explanation, 1);
        root.Children.Add(explanation);

        var targetPanel = new StackPanel();
        targetPanel.Children.Add(new TextBlock
        {
            Text = "Destination: Workspace → Partition",
            Margin = new Thickness(0, 0, 0, 6)
        });
        _targets = new ComboBox
        {
            ItemsSource = targets,
            DisplayMemberPath = nameof(WorkspacePartitionTarget.DisplayText),
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
        Button place = MakeButton("Place", "#2F6DB2");
        place.IsEnabled = targets.Count > 0;
        place.Click += (_, _) =>
        {
            if (_targets.SelectedItem is not WorkspacePartitionTarget selected)
                return;
            SelectedTarget = selected;
            DialogResult = true;
            Close();
        };
        Button cancel = MakeButton("Cancel", "#292929");
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        footer.Children.Add(place);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
    }

    public WorkspacePartitionTarget? SelectedTarget { get; private set; }

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
