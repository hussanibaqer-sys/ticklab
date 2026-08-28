using System.Windows;

namespace TickLab.Desktop.Settings;

/// <summary>
/// Marks visual subtrees whose colours are user data and must never be
/// rewritten by the application light/dark theme pass.
/// </summary>
public static class ThemeColorScope
{
    public static readonly DependencyProperty PreserveExactColorsProperty =
        DependencyProperty.RegisterAttached(
            "PreserveExactColors",
            typeof(bool),
            typeof(ThemeColorScope),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.Inherits));

    public static void SetPreserveExactColors(DependencyObject element, bool value) =>
        element.SetValue(PreserveExactColorsProperty, value);

    public static bool GetPreserveExactColors(DependencyObject element) =>
        (bool)element.GetValue(PreserveExactColorsProperty);
}
