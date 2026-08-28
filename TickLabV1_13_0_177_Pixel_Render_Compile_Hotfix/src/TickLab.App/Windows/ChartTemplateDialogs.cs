using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed class ChartTemplateNameDialog : Window
{
    private readonly TextBox _nameBox;

    public ChartTemplateNameDialog(string initialName = "")
    {
        Title = "Save chart template";
        Width = 420;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(7, 16, 27));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "Template name", FontSize = 17, FontWeight = FontWeights.SemiBold });
        _nameBox = new TextBox { Text = initialName, Margin = new Thickness(0, 12, 0, 0), MinHeight = 32, Padding = new Thickness(8, 5, 8, 5) };
        Grid.SetRow(_nameBox, 1);
        root.Children.Add(_nameBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 90 };
        var save = new Button { Content = "Save", Width = 90, Background = new SolidColorBrush(Color.FromRgb(47, 128, 237)), Foreground = Brushes.White };
        cancel.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(this, "Enter a template name.", "Template name", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
    }

    public string TemplateName => _nameBox.Text.Trim();
}

public sealed class ChartTemplatePickerDialog : Window
{
    private readonly ListBox _list;

    public ChartTemplatePickerDialog(string title, IReadOnlyList<ChartTemplateEntry> templates, string actionText)
    {
        Title = title;
        Width = 470;
        Height = 430;
        MinWidth = 400;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(7, 16, 27));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        _list = new ListBox { DisplayMemberPath = nameof(ChartTemplateEntry.Name), ItemsSource = templates, MinHeight = 220 };
        _list.MouseDoubleClick += (_, _) => Accept();
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90 };
        var action = new Button { Content = actionText, Width = 105, Background = new SolidColorBrush(Color.FromRgb(47, 128, 237)), Foreground = Brushes.White };
        cancel.Click += (_, _) => DialogResult = false;
        action.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(action);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { if (_list.Items.Count > 0) _list.SelectedIndex = 0; };
    }

    public ChartTemplateEntry? SelectedTemplate => _list.SelectedItem as ChartTemplateEntry;

    private void Accept()
    {
        if (SelectedTemplate is null)
            return;
        DialogResult = true;
    }
}
