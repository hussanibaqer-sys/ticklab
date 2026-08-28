using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed class ChartPaneControl : Grid
{
    private readonly RowDefinition _indicatorSplitterRow;
    private readonly RowDefinition _indicatorRow;
    private readonly GridSplitter _indicatorSplitter;
    private readonly ContentControl _indicatorHost;

    public ChartPaneControl()
    {
        Background = Brushes.Transparent;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _indicatorSplitterRow = new RowDefinition { Height = new GridLength(0) };
        _indicatorRow = new RowDefinition { Height = new GridLength(0) };
        RowDefinitions.Add(_indicatorSplitterRow);
        RowDefinitions.Add(_indicatorRow);

        Chart = new CandleChartControl { AllowDrop = true };
        Children.Add(Chart);
        TickChart = new TickChartControl { Visibility = Visibility.Collapsed };
        Children.Add(TickChart);

        _indicatorSplitter = new GridSplitter
        {
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
            Cursor = Cursors.SizeNS,
            ShowsPreview = false,
            ToolTip = "Drag up or down to resize the indicator area",
            Visibility = Visibility.Collapsed
        };
        SetRow(_indicatorSplitter, 1);
        Children.Add(_indicatorSplitter);

        _indicatorHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        SetRow(_indicatorHost, 2);
        Children.Add(_indicatorHost);
    }

    public CandleChartControl Chart { get; }
    public TickChartControl TickChart { get; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;

    public void SetRawTickMode(bool enabled)
    {
        TickChart.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        // Keep the mature CandleChartControl drawing engine alive as a transparent
        // overlay while TickChartControl renders the raw market series underneath.
        Chart.Visibility = Visibility.Visible;
        Panel.SetZIndex(TickChart, 0);
        Panel.SetZIndex(Chart, enabled ? 1 : 0);
        if (enabled)
            HideIndicators();
    }

    public void AttachIndicators(IndicatorPaneStackControl stack)
    {
        bool wasHidden = _indicatorHost.Visibility != Visibility.Visible || _indicatorRow.Height.Value <= 0;
        RemoveFromVisualParent(stack);
        _indicatorHost.Content = stack;
        _indicatorHost.Visibility = Visibility.Visible;
        _indicatorSplitter.Visibility = Visibility.Visible;
        _indicatorSplitterRow.Height = new GridLength(9);
        if (wasHidden)
            _indicatorRow.Height = new GridLength(190);
    }

    public void HideIndicators()
    {
        _indicatorHost.Content = null;
        _indicatorHost.Visibility = Visibility.Collapsed;
        _indicatorSplitter.Visibility = Visibility.Collapsed;
        _indicatorSplitterRow.Height = new GridLength(0);
        _indicatorRow.Height = new GridLength(0);
    }

    public void UpdateChart(
        IReadOnlyList<Candle> candles, IReadOnlyList<ChartTimelineGap> timelineGaps, IReadOnlyList<CandleMarker> markers,
        ChartSettings settings, long? nativeBoundaryUnix, string nativeBoundaryLabel)
    {
        Chart.Settings = settings;
        Chart.NativeHistoryBoundaryUnix = nativeBoundaryUnix;
        Chart.HistoryBoundaryLabel = nativeBoundaryLabel;
        Chart.TimelineGaps = timelineGaps;
        Chart.Markers = markers;
        Chart.ReplaceDataKeepingViewport(candles);
    }

    private static void RemoveFromVisualParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case ContentControl content when ReferenceEquals(content.Content, element): content.Content = null; break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element): decorator.Child = null; break;
        }
    }
}
