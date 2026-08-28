using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TickLab.Desktop.Windows;

public enum WorkspaceCloseDecision
{
    Cancel,
    DetachItems,
    CloseAll
}

public enum OccupiedPartitionDecision
{
    Cancel,
    Swap,
    Replace
}

public static class WorkspaceDecisionDialog
{
    public static WorkspaceCloseDecision ShowWorkspaceClose(Window owner, int workspaceId, int itemCount)
    {
        var dialog = BuildDialog(owner, $"Close Workspace {workspaceId}",
            itemCount == 0
                ? "Do you really want to close this workspace?"
                : $"Workspace {workspaceId} contains {itemCount} attached item(s). What should TickLab do?");
        WorkspaceCloseDecision result = WorkspaceCloseDecision.Cancel;
        AddButton(dialog.Panel, "Cancel", false, () => { result = WorkspaceCloseDecision.Cancel; dialog.Window.DialogResult = false; });
        if (itemCount > 0)
            AddButton(dialog.Panel, "Detach items", false, () => { result = WorkspaceCloseDecision.DetachItems; dialog.Window.DialogResult = true; });
        AddButton(dialog.Panel, itemCount > 0 ? "Close all" : "Yes", true, () => { result = WorkspaceCloseDecision.CloseAll; dialog.Window.DialogResult = true; });
        dialog.Window.ShowDialog();
        return result;
    }

    public static OccupiedPartitionDecision ShowOccupiedPartition(Window owner, string existingTitle, string incomingTitle)
    {
        var dialog = BuildDialog(owner, "Partition occupied",
            $"This partition already contains “{existingTitle}”. Choose what to do with “{incomingTitle}”.");
        OccupiedPartitionDecision result = OccupiedPartitionDecision.Cancel;
        AddButton(dialog.Panel, "Cancel", false, () => { result = OccupiedPartitionDecision.Cancel; dialog.Window.DialogResult = false; });
        AddButton(dialog.Panel, "Swap", false, () => { result = OccupiedPartitionDecision.Swap; dialog.Window.DialogResult = true; });
        AddButton(dialog.Panel, "Replace", true, () => { result = OccupiedPartitionDecision.Replace; dialog.Window.DialogResult = true; });
        dialog.Window.ShowDialog();
        return result;
    }

    private static (Window Window, StackPanel Panel) BuildDialog(Window owner, string title, string message)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = 430,
            Height = 190,
            MinWidth = 390,
            MinHeight = 175,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(9, 18, 30)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(235, 242, 250))
        });
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 14),
            Foreground = new SolidColorBrush(Color.FromRgb(177, 193, 212)),
            FontSize = 12
        };
        Grid.SetRow(text, 1);
        root.Children.Add(text);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(panel, 2);
        root.Children.Add(panel);
        window.Content = root;
        return (window, panel);
    }

    private static void AddButton(StackPanel panel, string text, bool danger, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            Height = 31,
            Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(12, 3, 12, 3),
            Foreground = Brushes.White,
            Background = danger
                ? new SolidColorBrush(Color.FromRgb(152, 43, 55))
                : new SolidColorBrush(Color.FromRgb(21, 40, 64)),
            BorderBrush = danger
                ? new SolidColorBrush(Color.FromRgb(220, 72, 87))
                : new SolidColorBrush(Color.FromRgb(48, 72, 103))
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
}
