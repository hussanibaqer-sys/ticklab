using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TickLab.Desktop.Windows;

public sealed record IndicatorPlaceAddress(
    int WorkspaceId,
    int PartitionId,
    int? PriceChartPaneId,
    string Label)
{
    public int? IndicatorWorkspacePaneId { get; init; }
    public bool IsPriceChart => PriceChartPaneId.HasValue;
    public bool IsIndicatorWorkspace => IndicatorWorkspacePaneId.HasValue;
    public override string ToString() => Label;
}

public sealed record IndicatorConnectionAddress(int? PriceChartPaneId, string Label)
{
    public override string ToString() => Label;
}

public sealed record IndicatorPlacementOptions(
    IReadOnlyList<IndicatorPlaceAddress> PlaceAddresses,
    IReadOnlyList<IndicatorConnectionAddress> ConnectionAddresses,
    IndicatorPlaceAddress InitialPlaceAddress,
    IndicatorConnectionAddress InitialConnectionAddress,
    bool InitialSyncWithPriceChart);

public sealed record IndicatorPlacementResult(
    IndicatorPlaceAddress PlaceAddress,
    IndicatorConnectionAddress ConnectAddress,
    bool SyncWithPriceChart);

internal sealed class IndicatorPlacementEditor
{
    private readonly ComboBox _placeAddress;
    private readonly ComboBox _connectAddress;
    private readonly CheckBox _sync;
    private bool _updating;

    public IndicatorPlacementEditor(IndicatorPlacementOptions options)
    {
        _placeAddress = CreateCombo(options.PlaceAddresses.Cast<object>().ToArray());
        _connectAddress = CreateCombo(options.ConnectionAddresses.Cast<object>().ToArray());
        _sync = new CheckBox
        {
            Content = "Sync with Price Chart",
            IsChecked = options.InitialSyncWithPriceChart,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "On: horizontal zoom, drag, scroll, visible timestamps, crosshair and replay follow the connected chart. Off: data stays connected, but navigation is independent."
        };

        _placeAddress.SelectedItem = options.InitialPlaceAddress;
        _connectAddress.SelectedItem = options.InitialConnectionAddress;
        _placeAddress.SelectionChanged += PlaceAddress_SelectionChanged;
        _connectAddress.SelectionChanged += (_, _) => UpdateSyncAvailability();
        ApplyPlaceAddressRules();
    }

    public FrameworkElement BuildView()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Placement",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(FormRow("Place Address", _placeAddress));
        panel.Children.Add(new TextBlock
        {
            Text = "The chart or empty workspace partition where this indicator will appear.",
            Foreground = Brush("#A8A8A8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(220, -4, 0, 8)
        });
        panel.Children.Add(FormRow("Connect Address", _connectAddress));
        panel.Children.Add(new TextBlock
        {
            Text = "Optional chart that supplies historical and live market data. Leave Not connected for an empty indicator workspace.",
            Foreground = Brush("#A8A8A8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(220, -4, 0, 8)
        });
        panel.Children.Add(FormRow(string.Empty, _sync));
        panel.Children.Add(new TextBlock
        {
            Text = "Sync is navigation only. Turning it off does not disconnect market data.",
            Foreground = Brush("#A8A8A8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(220, -4, 0, 0)
        });
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    public IndicatorPlacementResult Capture()
    {
        if (_placeAddress.SelectedItem is not IndicatorPlaceAddress place)
            throw new InvalidOperationException("Choose a Place Address.");
        IndicatorConnectionAddress connection = _connectAddress.SelectedItem as IndicatorConnectionAddress
            ?? new IndicatorConnectionAddress(null, "Not connected");
        return new IndicatorPlacementResult(place, connection, _sync.IsChecked == true);
    }

    private void PlaceAddress_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating)
            return;
        ApplyPlaceAddressRules();
    }

    private void ApplyPlaceAddressRules()
    {
        if (_placeAddress.SelectedItem is not IndicatorPlaceAddress place)
        {
            UpdateSyncAvailability();
            return;
        }

        if (place.IsPriceChart)
        {
            IndicatorConnectionAddress? matching = _connectAddress.Items
                .OfType<IndicatorConnectionAddress>()
                .FirstOrDefault(item => item.PriceChartPaneId == place.PriceChartPaneId);
            if (matching is not null)
            {
                _updating = true;
                _connectAddress.SelectedItem = matching;
                _updating = false;
            }
            _connectAddress.IsEnabled = false;
            _sync.IsChecked = true;
            _sync.IsEnabled = false;
            return;
        }

        _connectAddress.IsEnabled = true;
        UpdateSyncAvailability();
    }

    private void UpdateSyncAvailability()
    {
        if (_placeAddress.SelectedItem is IndicatorPlaceAddress place && place.IsPriceChart)
            return;
        bool connected = (_connectAddress.SelectedItem as IndicatorConnectionAddress)?.PriceChartPaneId.HasValue == true;
        _sync.IsEnabled = connected;
        if (!connected)
            _sync.IsChecked = false;
    }

    private static ComboBox CreateCombo(IReadOnlyList<object> items)
    {
        var combo = new ComboBox
        {
            ItemsSource = items,
            Height = 32,
            MinWidth = 390
        };
        IndicatorAddressSelectorStyle.Apply(combo);
        if (items.Count > 0)
            combo.SelectedIndex = 0;
        return combo;
    }

    private static Grid FormRow(string label, UIElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private static SolidColorBrush Brush(string value)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch { return new SolidColorBrush(Colors.Black); }
    }
}

internal static class IndicatorAddressSelectorStyle
{
    private static readonly SolidColorBrush White = new(Colors.White);
    private static readonly SolidColorBrush Black = new(Colors.Black);
    private static readonly SolidColorBrush Border = new(Color.FromRgb(110, 110, 110));
    private static readonly SolidColorBrush Hover = new(Color.FromRgb(225, 238, 252));
    private static readonly SolidColorBrush Selected = new(Color.FromRgb(199, 224, 249));

    public static void Apply(ComboBox combo)
    {
        combo.Background = White;
        combo.Foreground = Black;
        combo.BorderBrush = Border;
        combo.Resources[SystemColors.WindowBrushKey] = White;
        combo.Resources[SystemColors.WindowTextBrushKey] = Black;
        combo.Resources[SystemColors.ControlBrushKey] = White;
        combo.Resources[SystemColors.ControlTextBrushKey] = Black;
        combo.Resources[SystemColors.HighlightBrushKey] = Selected;
        combo.Resources[SystemColors.HighlightTextBrushKey] = Black;

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, White));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Black));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Hover));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Black));
        itemStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Selected));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Black));
        itemStyle.Triggers.Add(selected);
        combo.ItemContainerStyle = itemStyle;
    }
}
