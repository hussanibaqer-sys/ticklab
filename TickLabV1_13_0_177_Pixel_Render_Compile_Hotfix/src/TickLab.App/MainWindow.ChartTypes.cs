using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TickLab.Core.Market;
using TickLab.Core.Settings;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Core;
using TickLab.Desktop.Windows;
using TickLab.Gateway.FileBridge;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private static readonly (ChartVisualType Type, string Label)[] StandardChartTypes =
    {
        (ChartVisualType.Tick, "Tick"),
        (ChartVisualType.Candles, "Candles"),
        (ChartVisualType.HollowCandles, "Hollow Candles"),
        (ChartVisualType.Bars, "Bars"),
        (ChartVisualType.VolumeCandles, "Volume Candles"),
        (ChartVisualType.Line, "Line"),
        (ChartVisualType.LineWithMarkers, "Line with Markers"),
        (ChartVisualType.StepLine, "Step Line"),
        (ChartVisualType.Area, "Area"),
        (ChartVisualType.HlcArea, "HLC Area"),
        (ChartVisualType.Baseline, "Baseline"),
        (ChartVisualType.Columns, "Columns"),
        (ChartVisualType.HighLow, "High-Low")
    };

    private static readonly (ChartVisualType Type, string Label)[] SyntheticChartTypes =
    {
        (ChartVisualType.HeikinAshi, "Heikin Ashi"),
        (ChartVisualType.Renko, "Renko"),
        (ChartVisualType.LineBreak, "Line Break"),
        (ChartVisualType.Kagi, "Kagi"),
        (ChartVisualType.PointAndFigure, "Point & Figure"),
        (ChartVisualType.Range, "Range")
    };

    private static readonly (ChartVisualType Type, string Label)[] OrderFlowChartTypes =
    {
        (ChartVisualType.TimePriceOpportunity, "Time Price Opportunity (TPO)"),
        (ChartVisualType.SessionVolumeProfile, "Session Volume Profile"),
        (ChartVisualType.VolumeFootprint, "Volume Footprint")
    };

    private void ChartTypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        ChartRuntimeContext context = ActiveChartContext;
        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        AddChartTypeItems(menu, context, StandardChartTypes);
        menu.Items.Add(new Separator());

        bool syntheticLocked = AreSyntheticChartsLocked(context.Timeframe);
        var syntheticHeader = new MenuItem
        {
            Header = syntheticLocked ? "Synthetic charts (1m+)" : "Synthetic charts",
            IsEnabled = !syntheticLocked
        };
        AddChartTypeItems(syntheticHeader, context, SyntheticChartTypes, disableOnSeconds: true);
        menu.Items.Add(syntheticHeader);

        var syntheticSettings = new MenuItem
        {
            Header = "Synthetic chart settings…",
            IsEnabled = !syntheticLocked
        };
        syntheticSettings.Click += (_, _) => OpenSyntheticChartSettings();
        menu.Items.Add(syntheticSettings);
        menu.Items.Add(new Separator());

        var orderFlowHeader = new MenuItem { Header = "TPO and volume analysis" };
        AddChartTypeItems(orderFlowHeader, context, OrderFlowChartTypes);
        menu.Items.Add(orderFlowHeader);

        var orderFlowSettings = new MenuItem { Header = "TPO / volume settings…" };
        orderFlowSettings.Click += async (_, _) => await OpenOrderFlowSettingsAsync();
        menu.Items.Add(orderFlowSettings);

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void AddChartTypeItems(
        ItemsControl parent,
        ChartRuntimeContext context,
        IEnumerable<(ChartVisualType Type, string Label)> items,
        bool disableOnSeconds = false)
    {
        foreach ((ChartVisualType type, string label) in items)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = type == ChartVisualType.Tick
                    ? context.Timeframe.IsRawTickChart
                    : !context.Timeframe.IsRawTickChart && context.Settings.ChartType == type,
                StaysOpenOnClick = false,
                Tag = type,
                IsEnabled = !(disableOnSeconds && AreSyntheticChartsLocked(context.Timeframe))
            };
            item.Click += async (_, _) => await SetSelectedChartTypeAsync(type);
            parent.Items.Add(item);
        }
    }

    private async Task SetSelectedChartTypeAsync(ChartVisualType type)
    {
        ChartRuntimeContext context = ActiveChartContext;

        if (AreSyntheticChartsLocked(context.Timeframe) && SyntheticChartBuilder.IsSynthetic(type))
        {
            StatusText.Text = "Synthetic charts are locked on seconds timeframes. Use 1m or higher.";
            return;
        }

        if (type == ChartVisualType.Tick)
        {
            if (!context.Timeframe.IsRawTickChart)
            {
                context.LastCandleTimeframe = context.Timeframe;
                if (context.Settings.ChartType != ChartVisualType.Tick)
                    context.LastCandleChartType = context.Settings.ChartType;
            }

            context.Settings = context.Settings with { ChartType = ChartVisualType.Tick };
            context.Chart.Settings = context.Settings;
            context.TickChart.Settings = context.Settings;
            TimeframeDefinition tickTimeframe = TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Tick)!;
            await SelectTimeframeForActiveChartAsync(tickTimeframe);
            UpdateChartTypeButton();
            SaveWorkspace();
            StatusText.Text = $"Chart {context.PaneId} changed to Tick · raw Bid/Ask stream.";
            return;
        }

        if (context.Timeframe.IsRawTickChart)
        {
            TimeframeDefinition restore = context.LastCandleTimeframe.IsRawTickChart
                ? TimeframeDefinition.FindBuiltIn(1, TimeframeUnit.Minute)!
                : context.LastCandleTimeframe;
            context.Settings = context.Settings with { ChartType = context.LastCandleChartType };
            context.Chart.Settings = context.Settings;
            context.TickChart.Settings = context.Settings;
            await SelectTimeframeForActiveChartAsync(restore);
            context = ActiveChartContext;
        }

        if (RequiresRealVolume(type))
        {
            bool ready = await RefreshOrderFlowDataAsync(context, showErrors: true, debounce: false);
            if (!ready)
                return;
        }

        context.LastCandleChartType = type;
        context.Settings = context.Settings with { ChartType = type };
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        RefreshAllAppliedIndicators(force: true);
        UpdateChartTypeButton();
        SaveWorkspace();
        string label = GetChartTypeLabel(type);
        int count = context.Chart.Candles.Count;
        StatusText.Text = SyntheticChartBuilder.IsSynthetic(type)
            ? $"Chart {context.PaneId} changed to {label} ({count:N0} generated bars)."
            : type == ChartVisualType.TimePriceOpportunity
                ? $"Chart {context.PaneId} changed to {label}; TPO uses broker-session candle time and price."
                : RequiresRealVolume(type)
                    ? $"Chart {context.PaneId} changed to {label} using saved real-volume ticks."
                    : $"Chart {context.PaneId} changed to {label}.";
    }

    private static bool AreSyntheticChartsLocked(TimeframeDefinition timeframe) =>
        timeframe.Unit == TimeframeUnit.Second;

    private static ChartSettings EnforceSyntheticSecondsLock(
        ChartSettings settings,
        TimeframeDefinition timeframe) =>
        AreSyntheticChartsLocked(timeframe) && SyntheticChartBuilder.IsSynthetic(settings.ChartType)
            ? settings with { ChartType = ChartVisualType.Candles }
            : settings;

    private void OpenSyntheticChartSettings()
    {
        ChartRuntimeContext context = ActiveChartContext;
        var window = new SyntheticChartSettingsWindow(context.Settings) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        ChartSettings? result = window.Result;
        if (result is null)
            return;

        context.Settings = result;
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        RefreshAllAppliedIndicators(force: true);
        SaveWorkspace();
        UpdateChartTypeButton();
        StatusText.Text = $"Synthetic chart settings saved for chart {context.PaneId}.";
    }

    private async Task OpenOrderFlowSettingsAsync()
    {
        ChartRuntimeContext context = ActiveChartContext;
        var window = new OrderFlowSettingsWindow(context.Settings) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        ChartSettings? result = window.Result;
        if (result is null)
            return;

        context.Settings = result;
        context.Chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        if (RequiresRealVolume(context.Settings.ChartType))
            await RefreshOrderFlowDataAsync(context, showErrors: true, debounce: false);
        RefreshAllAppliedIndicators(force: true);
        SaveWorkspace();
        UpdateChartTypeButton();
        StatusText.Text = $"TPO and volume settings saved for chart {context.PaneId}.";
    }

    private void ScheduleOrderFlowRefresh(ChartRuntimeContext context)
    {
        if (!RequiresRealVolume(context.Settings.ChartType))
            return;
        _ = RefreshOrderFlowDataAsync(context, showErrors: false, debounce: true);
    }

    private async Task<bool> RefreshOrderFlowDataAsync(
        ChartRuntimeContext context,
        bool showErrors,
        bool debounce)
    {
        context.OrderFlowLoadCancellation?.Cancel();
        context.OrderFlowLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        context.OrderFlowLoadCancellation = cancellation;

        try
        {
            if (debounce)
                await Task.Delay(250, cancellation.Token);

            if (_selectedConnector is null || string.IsNullOrWhiteSpace(context.Symbol))
                return ShowOrderFlowError(context, "Open a connected chart before selecting a real-volume chart.", showErrors);

            IReadOnlyList<Candle> source = context.DisplayCandles.Count > 0
                ? context.DisplayCandles
                : context.SourceCandles.Count > 0
                    ? context.SourceCandles
                    : context.Chart.Candles;
            if (source.Count == 0)
                return ShowOrderFlowError(context, "Open a chart with saved history before selecting this chart type.", showErrors);

            (long startUnix, long endUnix) = GetOrderFlowVisibleRange(context, source);
            StatusText.Text = $"Loading saved real-volume ticks for {context.Symbol}…";
            CanonicalTickReadResult read = await Task.Run(
                () => _historyStore.ReadTicksForReplay(
                    _selectedConnector.ConnectorId,
                    context.Symbol,
                    startUnix * 1000,
                    endUnix * 1000 + 999,
                    maximumRecords: 2_000_000,
                    cancellationToken: cancellation.Token),
                cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            if (read.Ticks.Count == 0)
                return ShowOrderFlowError(context, "No saved tick data is available for the visible chart period.", showErrors);

            if (!read.Ticks.Any(tick => double.IsFinite(tick.VolumeReal) && tick.VolumeReal > 0))
            {
                return ShowOrderFlowError(
                    context,
                    "This chart requires real trade volume, but the connected broker is not providing it.",
                    showErrors);
            }

            Candle[] candles = source
                .Where(candle => candle.EndUnix > startUnix && candle.StartUnix <= endUnix)
                .OrderBy(candle => candle.StartUnix)
                .ToArray();
            if (candles.Length == 0)
                candles = source.TakeLast(Math.Min(source.Count, 1_500)).ToArray();

            OrderFlowProfileSnapshot snapshot = await Task.Run(
                () => OrderFlowProfileBuilder.Build(
                    candles,
                    read.Ticks,
                    context.Settings,
                    _selectedConnector.ServerUtcOffsetMinutes),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!snapshot.HasRealVolume)
                return ShowOrderFlowError(context, snapshot.StatusMessage, showErrors);

            if (read.HasMore)
            {
                snapshot = snapshot with
                {
                    StatusMessage = snapshot.StatusMessage + " The visible range exceeded 2,000,000 ticks; zoom in for complete footprint detail."
                };
            }
            context.Chart.OrderFlowProfile = snapshot;
            StatusText.Text = snapshot.StatusMessage;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            return ShowOrderFlowError(context, $"Unable to load saved real-volume ticks: {exception.Message}", showErrors);
        }
    }

    private static (long StartUnix, long EndUnix) GetOrderFlowVisibleRange(
        ChartRuntimeContext context,
        IReadOnlyList<Candle> source)
    {
        ChartViewportSnapshot? viewport = context.Chart.CaptureViewportSnapshot();
        IReadOnlyList<Candle> displayed = context.Chart.Candles;
        if (viewport is not null && displayed.Count > 0 && viewport.FirstIndex < displayed.Count)
        {
            int first = Math.Clamp(viewport.FirstIndex, 0, displayed.Count - 1);
            int last = Math.Clamp(Math.Max(first, viewport.LastExclusive - 1), first, displayed.Count - 1);
            long start = displayed[first].StartUnix;
            long end = displayed[last].EndUnix;
            return (start, Math.Max(start, end));
        }

        int fallbackFirst = Math.Max(0, source.Count - Math.Min(source.Count, 500));
        return (source[fallbackFirst].StartUnix, source[^1].EndUnix);
    }

    private bool ShowOrderFlowError(ChartRuntimeContext context, string message, bool showDialog)
    {
        context.Chart.OrderFlowProfile = OrderFlowProfileSnapshot.Empty with { StatusMessage = message };
        StatusText.Text = message;
        if (showDialog)
        {
            MessageBox.Show(
                this,
                message,
                "TickLab volume data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return false;
    }

    private static bool RequiresRealVolume(ChartVisualType type) => type is
        ChartVisualType.SessionVolumeProfile or
        ChartVisualType.VolumeFootprint;

    private void UpdateChartTypeButton()
    {
        if (ChartTypeButton is null)
            return;
        _chartContexts.TryGetValue(_activePricePaneId, out ChartRuntimeContext? context);
        ChartVisualType type = context?.Timeframe.IsRawTickChart == true
            ? ChartVisualType.Tick
            : context?.Settings.ChartType ?? ChartVisualType.Candles;
        Brush iconBrush = TryFindResource("TextBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(226, 232, 240));
        ChartTypeButton.Content = TopBarIconFactory.CreateChartType(type, 30, iconBrush);
        ChartTypeButton.ToolTip = $"Chart type: {GetChartTypeLabel(type)}";
    }

    private static string GetChartTypeLabel(ChartVisualType type)
    {
        foreach ((ChartVisualType candidate, string label) in StandardChartTypes.Concat(SyntheticChartTypes).Concat(OrderFlowChartTypes))
        {
            if (candidate == type)
                return label;
        }
        return "Candles";
    }
}
