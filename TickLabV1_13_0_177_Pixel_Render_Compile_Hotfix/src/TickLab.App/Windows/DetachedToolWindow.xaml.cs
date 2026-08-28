using System.Windows;

namespace TickLab.Desktop.Windows;

public partial class DetachedToolWindow : Window
{
    public event EventHandler? DockRequested;

    public DetachedToolWindow(
        string title,
        FrameworkElement content)
    {
        InitializeComponent();
        UpdateTitle(title);
        ToolContent.Content = content;
    }

    public FrameworkElement? ReleaseContent()
    {
        FrameworkElement? content =
            ToolContent.Content as FrameworkElement;
        ToolContent.Content = null;
        return content;
    }

    public void UpdateTitle(string title)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title)
            ? "TickLab Tool"
            : title;

        DetachedTitleText.Text = safeTitle;
        Title = $"TickLab — {safeTitle}";
    }

    private void DockButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DockRequested?.Invoke(this, EventArgs.Empty);
    }
}
