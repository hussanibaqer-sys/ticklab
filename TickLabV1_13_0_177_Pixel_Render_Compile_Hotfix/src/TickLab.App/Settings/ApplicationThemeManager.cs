using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop.Settings;

public static class ApplicationThemeManager
{
    public static string CurrentTheme { get; private set; } = "Dark";

    public static void Apply(string? theme)
    {
        Application? app = Application.Current;
        if (app is null)
            return;

        CurrentTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        bool light = CurrentTheme == "Light";

        UpdateResource(app, "WindowBrush", light ? "#F4F4F4" : "#000000");
        UpdateResource(app, "PanelBrush", light ? "#FFFFFF" : "#080808");
        UpdateResource(app, "PanelAltBrush", light ? "#ECECEC" : "#101010");
        UpdateResource(app, "ElevatedBrush", light ? "#FFFFFF" : "#171717");
        UpdateResource(app, "BorderBrush", light ? "#C7C7C7" : "#2A2A2A");
        UpdateResource(app, "BorderStrongBrush", light ? "#909090" : "#454545");
        UpdateResource(app, "TextBrush", light ? "#181818" : "#F2F2F2");
        UpdateResource(app, "MutedTextBrush", light ? "#555555" : "#B0B0B0");
        UpdateResource(app, "SubtleTextBrush", light ? "#777777" : "#858585");
        UpdateResource(app, "DarkHeaderBrush", light ? "#E5E5E5" : "#000000");
        UpdateResource(app, "ControlBrush", light ? "#FFFFFF" : "#101010");
        UpdateResource(app, "ControlHoverBrush", light ? "#E9EEF5" : "#171717");
        UpdateResource(app, "ControlPressedBrush", light ? "#DDE6F1" : "#0B0B0B");
        UpdateResource(app, "SelectionBrush", light ? "#DCEBFF" : "#243247");
        UpdateResource(app, "SelectionTextBrush", light ? "#111111" : "#F8FAFC");
        UpdateResource(app, "MenuBrush", light ? "#FFFFFF" : "#101010");
        UpdateResource(app, "MenuHoverBrush", light ? "#E9EEF5" : "#1B2430");
        UpdateResource(app, "GridLineBrush", light ? "#D8DDE5" : "#242424");
        UpdateResource(app, "DisabledTextBrush", light ? "#8A8A8A" : "#737373");

        foreach (Window window in app.Windows)
            ApplyToWindow(window);
    }

    public static void ApplyToWindow(Window window)
    {
        bool light = CurrentTheme == "Light";
        window.Background = Brush(light ? "#F4F4F4" : "#000000");
        window.Foreground = Brush(light ? "#181818" : "#F2F2F2");
        if (window.Content is DependencyObject root)
            ApplyRecursive(root, light);
    }

    private static void ApplyRecursive(DependencyObject element, bool light)
    {
        // User-selected swatches and previews are data, not theme surfaces.
        // Returning here also protects every visual child in that swatch.
        if (ThemeColorScope.GetPreserveExactColors(element))
            return;

        // Chart colours are independent from the application theme.
        if (element is CandleChartControl or TickChartControl)
            return;

        Brush text = Brush(light ? "#181818" : "#F2F2F2");
        Brush muted = Brush(light ? "#555555" : "#B0B0B0");
        Brush panel = Brush(light ? "#FFFFFF" : "#080808");
        Brush alt = Brush(light ? "#ECECEC" : "#101010");
        Brush border = Brush(light ? "#C7C7C7" : "#2A2A2A");
        Brush selection = Brush(light ? "#DCEBFF" : "#243247");
        Brush selectionText = Brush(light ? "#111111" : "#F8FAFC");

        switch (element)
        {
            case TextBlock textBlock when ShouldThemeText(textBlock.Foreground):
                // Text inside a selectable/highlightable container must inherit
                // that container's live foreground.  A local text colour here
                // would override the hover/selected contrast state.
                if (HasStatefulSelectionAncestor(textBlock))
                    textBlock.ClearValue(TextBlock.ForegroundProperty);
                else
                    textBlock.Foreground = textBlock.Opacity < 0.85 ? muted : text;
                break;
            case Border borderControl:
                if (ShouldThemeSurface(borderControl.Background))
                    borderControl.Background = panel;
                if (ShouldThemeSurface(borderControl.BorderBrush))
                    borderControl.BorderBrush = border;
                break;
            case Grid grid when ShouldThemeSurface(grid.Background):
                grid.Background = Brushes.Transparent;
                break;
            case Button button:
                // Small tagged buttons are colour swatches and must preserve their exact colour.
                if (button.Tag is string && button.Width <= 50 && button.Height <= 34)
                    break;
                if (ShouldThemeText(button.Foreground))
                    button.Foreground = text;
                if (button.Background != Brushes.Transparent && ShouldThemeSurface(button.Background))
                    button.Background = alt;
                if (ShouldThemeSurface(button.BorderBrush))
                    button.BorderBrush = border;
                break;
            case TextBox textBox:
                textBox.Foreground = text;
                textBox.Background = panel;
                textBox.BorderBrush = border;
                break;
            case ComboBox comboBox:
                comboBox.Foreground = text;
                comboBox.Background = panel;
                comboBox.BorderBrush = border;
                break;
            case ListBox listBox:
                listBox.Foreground = text;
                if (ShouldThemeSurface(listBox.Background))
                    listBox.Background = panel;
                listBox.BorderBrush = border;
                break;
            case DataGrid dataGrid:
                dataGrid.Foreground = text;
                dataGrid.Background = panel;
                dataGrid.BorderBrush = border;
                dataGrid.RowBackground = panel;
                dataGrid.AlternatingRowBackground = alt;
                break;
            case TabItem tabItem:
                // Let App.xaml triggers own hover/selection colours.  Local
                // values here used to block those triggers after theme changes.
                if (ShouldThemeText(tabItem.Foreground))
                    tabItem.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(tabItem.Background))
                    tabItem.ClearValue(Control.BackgroundProperty);
                break;
            case ComboBoxItem comboItem:
                if (ShouldThemeText(comboItem.Foreground))
                    comboItem.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(comboItem.Background))
                    comboItem.ClearValue(Control.BackgroundProperty);
                break;
            case ListBoxItem listItem:
                if (ShouldThemeText(listItem.Foreground))
                    listItem.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(listItem.Background))
                    listItem.ClearValue(Control.BackgroundProperty);
                break;
            case DataGridCell cell:
                if (ShouldThemeText(cell.Foreground))
                    cell.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(cell.Background))
                    cell.ClearValue(Control.BackgroundProperty);
                break;
            case DataGridRow row:
                if (ShouldThemeText(row.Foreground))
                    row.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(row.Background))
                    row.ClearValue(Control.BackgroundProperty);
                break;
            case DataGridColumnHeader header:
                header.Foreground = muted;
                header.Background = alt;
                header.BorderBrush = border;
                break;
            case ContextMenu menu:
                menu.Foreground = text;
                menu.Background = panel;
                menu.BorderBrush = border;
                break;
            case MenuItem menuItem:
                if (ShouldThemeText(menuItem.Foreground))
                    menuItem.ClearValue(Control.ForegroundProperty);
                if (ShouldThemeSurface(menuItem.Background))
                    menuItem.ClearValue(Control.BackgroundProperty);
                break;
            case ToolTip toolTip:
                toolTip.Foreground = text;
                toolTip.Background = panel;
                toolTip.BorderBrush = border;
                break;
            case CheckBox checkBox when ShouldThemeText(checkBox.Foreground):
                checkBox.Foreground = text;
                break;
            case RadioButton radioButton when ShouldThemeText(radioButton.Foreground):
                radioButton.Foreground = text;
                break;
        }

        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < count; index++)
            ApplyRecursive(VisualTreeHelper.GetChild(element, index), light);
    }

    private static bool HasStatefulSelectionAncestor(DependencyObject element)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is MenuItem or ComboBoxItem or ListBoxItem or
                DataGridCell or DataGridRow or TabItem)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static bool ShouldThemeSurface(Brush? brush)
    {
        if (brush is null || brush == Brushes.Transparent)
            return true;
        if (brush is not SolidColorBrush solid)
            return false;
        Color c = solid.Color;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        double saturation = max == 0 ? 0 : (max - min) / (double)max;
        return saturation < 0.20;
    }

    private static bool ShouldThemeText(Brush? brush)
    {
        if (brush is null)
            return true;
        if (brush is not SolidColorBrush solid)
            return false;
        Color c = solid.Color;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        double saturation = max == 0 ? 0 : (max - min) / (double)max;
        return saturation < 0.25;
    }

    private static void UpdateResource(Application app, string key, string color) =>
        app.Resources[key] = Brush(color);

    private static SolidColorBrush Brush(string color)
    {
        object? converted = ColorConverter.ConvertFromString(color);
        return converted is Color parsed
            ? new SolidColorBrush(parsed)
            : new SolidColorBrush(Colors.Transparent);
    }
}
