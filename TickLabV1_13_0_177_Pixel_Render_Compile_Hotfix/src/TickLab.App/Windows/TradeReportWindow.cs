using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop;

public sealed class TradeReportWindow : Window
{
    private const double PageWidth = 1160.0;
    private readonly TradeHistoryReportData _report;
    private readonly ReportStats _stats;
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly TextBlock _zoomText;
    private readonly TabControl _tabs;
    private double _zoom = 1.0;

    public TradeReportWindow(TradeHistoryReportData report)
    {
        _report = report;
        _stats = ReportStats.Create(report);

        Title = $"TickLab Trading Report — {report.Name}";
        Width = 1320;
        Height = 860;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new DockPanel { LastChildFill = true };
        Content = root;

        Border toolbar = BuildToolbar(out _zoomText);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        _tabs = new TabControl
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        root.Children.Add(_tabs);

        AddPage("Summary", BuildSummaryPage());
        AddPage("Profit & Loss", BuildProfitLossPage());
        AddPage("Long & Short", BuildLongShortPage());
        AddPage("Symbols", BuildSymbolsPage());
        AddPage("Risks", BuildRisksPage());
        AddPage("Trades", BuildTradesPage());

        Loaded += (_, _) =>
        {
            ApplicationThemeManager.ApplyToWindow(this);
            InvalidateReportCharts();
        };
        Activated += (_, _) => InvalidateReportCharts();
    }

    private Border BuildToolbar(out TextBlock zoomText)
    {
        var toolbar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10, 14, 10)
        };
        toolbar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        toolbar.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Child = grid;

        var identity = new StackPanel();
        var title = new TextBlock
        {
            Text = _report.Name,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        identity.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = BuildSubtitle(_report),
            FontSize = 10.5,
            Margin = new Thickness(0, 3, 0, 0)
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        identity.Children.Add(subtitle);
        grid.Children.Add(identity);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);

        tools.Children.Add(MakeToolbarButton("−", "Zoom out", (_, _) => SetZoom(_zoom - 0.1)));
        tools.Children.Add(MakeToolbarButton("100%", "Reset zoom", (_, _) => SetZoom(1.0)));
        tools.Children.Add(MakeToolbarButton("+", "Zoom in", (_, _) => SetZoom(_zoom + 0.1)));
        zoomText = new TextBlock
        {
            Text = "100%",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 44,
            TextAlignment = TextAlignment.Right
        };
        zoomText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        tools.Children.Add(zoomText);
        return toolbar;
    }

    private static Button MakeToolbarButton(string text, string toolTip, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = toolTip,
            Height = 30,
            MinWidth = text.Length <= 1 ? 34 : 54,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2)
        };
        button.SetResourceReference(Control.BackgroundProperty, "PanelAltBrush");
        button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        button.Click += handler;
        return button;
    }

    private void AddPage(string title, UIElement content)
    {
        var tab = new TabItem
        {
            Header = title,
            Content = content,
            Padding = new Thickness(14, 7, 14, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        tab.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tab.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        _tabs.Items.Add(tab);
    }

    private ScrollViewer CreatePage(out StackPanel panel)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            CanContentScroll = false,
            PanningMode = System.Windows.Controls.PanningMode.Both,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0)
        };
        scroll.SetResourceReference(Control.BackgroundProperty, "WindowBrush");

        // Keep a clearly visible draggable scrollbar on the right in both
        // Demo and imported MT5 reports. The report used to scroll by wheel,
        // but the theme could make the actual bar/thumb effectively invisible.
        var reportScrollBarStyle = new Style(typeof(ScrollBar));
        reportScrollBarStyle.Setters.Add(new Setter(ScrollBar.WidthProperty, 15.0));
        reportScrollBarStyle.Setters.Add(new Setter(ScrollBar.MinWidthProperty, 15.0));
        reportScrollBarStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(38, 42, 48))));
        reportScrollBarStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(132, 145, 160))));
        reportScrollBarStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
        scroll.Resources[typeof(ScrollBar)] = reportScrollBarStyle;

        scroll.PreviewMouseWheel += ReportScroll_PreviewMouseWheel;

        panel = new StackPanel
        {
            Width = PageWidth,
            Margin = new Thickness(18),
            LayoutTransform = _scale
        };
        scroll.Content = panel;
        return scroll;
    }

    private UIElement BuildSummaryPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Performance overview", "A compact view of account growth, profitability, efficiency and risk.");

        var hero = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
        AddMetricCard(hero, "Net P/L", Money(_stats.NetProfit), _stats.NetProfit >= 0 ? MetricTone.Positive : MetricTone.Negative);
        AddMetricCard(hero, "Ending balance", MoneyOrNa(_report.EndingBalance), MetricTone.Accent);
        AddMetricCard(hero, "Win rate", $"{_stats.WinRate:0.00}%", MetricTone.Accent);
        AddMetricCard(hero, "Profit factor", FormatFactor(_stats.ProfitFactor), _stats.ProfitFactor >= 1 ? MetricTone.Positive : MetricTone.Negative);
        AddMetricCard(hero, "Max drawdown", $"{_stats.MaxDrawdownPercent:0.00}%", MetricTone.Negative);
        AddMetricCard(hero, "Trades", _stats.Trades.Count.ToString("N0", CultureInfo.CurrentCulture), MetricTone.Neutral);
        page.Children.Add(hero);

        var overviewGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        overviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        overviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border balanceCard = CreateGraphCard("Balance / realized equity", "Closed-trade balance progression including balance operations when available.", BuildBalanceCurve(_stats.Trades, _report.CashFlows, _report.StartingBalance), ReportGraphMode.Line, false, 330);
        overviewGrid.Children.Add(balanceCard);

        var outcomeCard = CreateCard();
        Grid.SetColumn(outcomeCard, 1);
        outcomeCard.Margin = new Thickness(10, 0, 0, 0);
        var outcomeStack = new StackPanel();
        outcomeStack.Children.Add(CreateCardTitle("Trade outcomes", "Wins, losses and breakeven trades."));
        outcomeStack.Children.Add(new DonutReportControl(_stats.Wins, _stats.Losses, _stats.Breakeven, "Win rate")
        {
            Height = 245,
            Margin = new Thickness(0, 4, 0, 4)
        });
        AddPercentMeter(outcomeStack, "Winning trades", _stats.WinRate, $"{_stats.Wins:N0} / {_stats.Trades.Count:N0}");
        outcomeCard.Child = outcomeStack;
        overviewGrid.Children.Add(outcomeCard);
        page.Children.Add(overviewGrid);

        AddSectionHeading(page, "Account & execution");
        var accountMetrics = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
        AddMetricCard(accountMetrics, "Starting balance", MoneyOrNa(_report.StartingBalance), MetricTone.Neutral);
        AddMetricCard(accountMetrics, "Deposits", Money(_report.Deposits), MetricTone.Positive);
        AddMetricCard(accountMetrics, "Withdrawals", Money(_report.Withdrawals), MetricTone.Negative);
        AddMetricCard(accountMetrics, "Volume", _stats.Volume.ToString("0.00", CultureInfo.CurrentCulture), MetricTone.Neutral);
        AddMetricCard(accountMetrics, "Avg hold", FormatDuration(_stats.AverageDurationMinutes), MetricTone.Neutral);
        AddMetricCard(accountMetrics, "Gross profit", Money(_stats.GrossProfit), MetricTone.Positive);
        AddMetricCard(accountMetrics, "Gross loss", Money(_stats.GrossLoss), MetricTone.Negative);
        AddMetricCard(accountMetrics, "Commission", Money(_stats.Commission), MetricTone.Neutral);
        AddMetricCard(accountMetrics, "Swap", Money(_stats.Swap), MetricTone.Neutral);
        AddMetricCard(accountMetrics, "Fees", Money(_stats.Fees), MetricTone.Neutral);
        page.Children.Add(accountMetrics);

        if (!string.IsNullOrWhiteSpace(_report.ParseNote))
            page.Children.Add(CreateInfoCallout(_report.ParseNote));
        return scroll;
    }

    private UIElement BuildProfitLossPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Profit & Loss", "Profit concentration over time, daily/monthly behavior and trade-level distribution.");

        var metrics = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
        AddMetricCard(metrics, "Net P/L", Money(_stats.NetProfit), _stats.NetProfit >= 0 ? MetricTone.Positive : MetricTone.Negative);
        AddMetricCard(metrics, "Gross profit", Money(_stats.GrossProfit), MetricTone.Positive);
        AddMetricCard(metrics, "Gross loss", Money(_stats.GrossLoss), MetricTone.Negative);
        AddMetricCard(metrics, "Avg win", Money(_stats.AverageWin), MetricTone.Positive);
        AddMetricCard(metrics, "Avg loss", Money(_stats.AverageLoss), MetricTone.Negative);
        AddMetricCard(metrics, "Expected payoff", Money(_stats.ExpectedPayoff), _stats.ExpectedPayoff >= 0 ? MetricTone.Positive : MetricTone.Negative);
        page.Children.Add(metrics);

        page.Children.Add(CreateGraphCard("Balance / realized equity", "Account path from completed trades and balance operations.", BuildBalanceCurve(_stats.Trades, _report.CashFlows, _report.StartingBalance), ReportGraphMode.Line, false, 300));
        page.Children.Add(CreateGraphCard("Daily profit / loss", "Net realized P/L grouped by closing date.", BuildDailyProfit(_stats.Trades), ReportGraphMode.Bar, false, 270));
        page.Children.Add(CreateGraphCard("Monthly profit / loss", "Net realized P/L grouped by month.", BuildMonthlyProfit(_stats.Trades), ReportGraphMode.Bar, false, 270));
        page.Children.Add(CreateGraphCard("Trade-by-trade net P/L", "Every completed trade in chronological close order.", BuildTradeProfitSequence(_stats.Trades), ReportGraphMode.Bar, false, 270));
        return scroll;
    }

    private UIElement BuildLongShortPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Long & Short", "Directional behavior, win rates and realized contribution by side.");

        var upper = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var donutCard = CreateCard();
        var donutStack = new StackPanel();
        donutStack.Children.Add(CreateCardTitle("Direction mix", "Share of completed BUY and SELL trades."));
        donutStack.Children.Add(new DirectionDonutControl(_stats.BuyCount, _stats.SellCount) { Height = 280 });
        donutCard.Child = donutStack;
        upper.Children.Add(donutCard);

        var sideMetrics = new UniformGrid { Columns = 2, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(sideMetrics, 1);
        AddMetricCard(sideMetrics, "BUY trades", _stats.BuyCount.ToString("N0"), MetricTone.Accent);
        AddMetricCard(sideMetrics, "SELL trades", _stats.SellCount.ToString("N0"), MetricTone.Accent);
        AddMetricCard(sideMetrics, "BUY win rate", $"{_stats.BuyWinRate:0.00}%", _stats.BuyWinRate >= 50 ? MetricTone.Positive : MetricTone.Neutral);
        AddMetricCard(sideMetrics, "SELL win rate", $"{_stats.SellWinRate:0.00}%", _stats.SellWinRate >= 50 ? MetricTone.Positive : MetricTone.Neutral);
        AddMetricCard(sideMetrics, "BUY net P/L", Money(_stats.BuyNetProfit), _stats.BuyNetProfit >= 0 ? MetricTone.Positive : MetricTone.Negative);
        AddMetricCard(sideMetrics, "SELL net P/L", Money(_stats.SellNetProfit), _stats.SellNetProfit >= 0 ? MetricTone.Positive : MetricTone.Negative);
        upper.Children.Add(sideMetrics);
        page.Children.Add(upper);

        page.Children.Add(CreateGraphCard("BUY performance by month", "Monthly net P/L from long positions.", BuildMonthlyProfit(_stats.Trades.Where(t => string.Equals(t.Direction, "BUY", StringComparison.OrdinalIgnoreCase))), ReportGraphMode.Bar, false, 270));
        page.Children.Add(CreateGraphCard("SELL performance by month", "Monthly net P/L from short positions.", BuildMonthlyProfit(_stats.Trades.Where(t => string.Equals(t.Direction, "SELL", StringComparison.OrdinalIgnoreCase))), ReportGraphMode.Bar, false, 270));
        return scroll;
    }

    private UIElement BuildSymbolsPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Symbols", "Which markets generated the most activity, profit and risk.");

        page.Children.Add(CreateGraphCard("Net profit by symbol", "Total net realized P/L for every traded symbol.", BuildSymbolPerformance(_stats.Trades), ReportGraphMode.Bar, false, 300));
        page.Children.Add(CreateGraphCard("Trades by symbol", "Completed trade count per symbol.", BuildSymbolTradeCount(_stats.Trades), ReportGraphMode.Bar, false, 270));
        page.Children.Add(CreateGraphCard("Win rate by symbol", "Winning trades as a percentage of completed trades.", BuildSymbolWinRate(_stats.Trades), ReportGraphMode.Bar, true, 270));

        AddSectionHeading(page, "Symbol detail");
        var grid = BuildSymbolGrid();
        page.Children.Add(grid);
        return scroll;
    }

    private UIElement BuildRisksPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Risks", "Drawdown, streaks, expectancy and stability metrics from realized results.");

        var metrics = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
        AddMetricCard(metrics, "Max drawdown", Money(_stats.MaxDrawdown), MetricTone.Negative);
        AddMetricCard(metrics, "Drawdown %", $"{_stats.MaxDrawdownPercent:0.00}%", MetricTone.Negative);
        AddMetricCard(metrics, "Recovery factor", _stats.RecoveryFactorText, MetricTone.Accent);
        AddMetricCard(metrics, "Sharpe", _stats.SharpeText, MetricTone.Accent);
        AddMetricCard(metrics, "Max win streak", _stats.MaxWinStreak.ToString("N0"), MetricTone.Positive);
        AddMetricCard(metrics, "Max loss streak", _stats.MaxLossStreak.ToString("N0"), MetricTone.Negative);
        AddMetricCard(metrics, "Best trade", Money(_stats.LargestWin), MetricTone.Positive);
        AddMetricCard(metrics, "Worst trade", Money(_stats.LargestLoss), MetricTone.Negative);
        AddMetricCard(metrics, "Best day", Money(_stats.BestDay), MetricTone.Positive);
        AddMetricCard(metrics, "Worst day", Money(_stats.WorstDay), MetricTone.Negative);
        AddMetricCard(metrics, "Profitable days", _stats.ProfitableDays.ToString("N0"), MetricTone.Positive);
        AddMetricCard(metrics, "Losing days", _stats.LosingDays.ToString("N0"), MetricTone.Negative);
        page.Children.Add(metrics);

        page.Children.Add(CreateGraphCard("Drawdown", "Peak-to-current decline of the realized balance curve.", BuildDrawdownPercentCurve(BuildBalanceCurve(_stats.Trades, _report.CashFlows, _report.StartingBalance)), ReportGraphMode.Line, true, 320));
        page.Children.Add(CreateGraphCard("Trades per day", "Daily trading intensity and concentration.", BuildDailyTradeCount(_stats.Trades), ReportGraphMode.Bar, false, 260));

        var meters = CreateCard();
        var meterStack = new StackPanel();
        meterStack.Children.Add(CreateCardTitle("Risk balance", "Percentages make directional and outcome concentration immediately visible."));
        AddPercentMeter(meterStack, "Winning trades", _stats.WinRate, $"{_stats.Wins:N0} wins");
        AddPercentMeter(meterStack, "BUY share", _stats.Trades.Count > 0 ? _stats.BuyCount * 100.0 / _stats.Trades.Count : 0, $"{_stats.BuyCount:N0} BUY");
        AddPercentMeter(meterStack, "SELL share", _stats.Trades.Count > 0 ? _stats.SellCount * 100.0 / _stats.Trades.Count : 0, $"{_stats.SellCount:N0} SELL");
        meters.Child = meterStack;
        page.Children.Add(meters);
        return scroll;
    }

    private UIElement BuildTradesPage()
    {
        ScrollViewer scroll = CreatePage(out StackPanel page);
        AddPageHeading(page, "Trades", "Search, sort and inspect the complete history without affecting chart projection performance.");

        var searchCard = CreateCard();
        var searchStack = new StackPanel();
        searchStack.Children.Add(CreateCardTitle("Complete trade history", $"{_stats.Trades.Count:N0} completed trades"));
        var search = new TextBox
        {
            Height = 32,
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(9, 4, 9, 4),
            ToolTip = "Search ticket, symbol, side, close reason or comment"
        };
        search.SetResourceReference(Control.BackgroundProperty, "PanelAltBrush");
        search.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        search.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        searchStack.Children.Add(search);

        var items = new ObservableCollection<TradeHistoryTrade>(_stats.Trades.OrderByDescending(t => t.CloseTime));
        ICollectionView view = CollectionViewSource.GetDefaultView(items);
        search.TextChanged += (_, _) =>
        {
            string term = search.Text.Trim();
            view.Filter = item => item is TradeHistoryTrade trade && (term.Length == 0 ||
                trade.Ticket.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                trade.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                trade.Direction.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                trade.Comment.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                trade.CloseReason.Contains(term, StringComparison.OrdinalIgnoreCase));
            view.Refresh();
        };
        DataGrid grid = BuildTradeGrid();
        grid.ItemsSource = view;
        searchStack.Children.Add(grid);
        searchCard.Child = searchStack;
        page.Children.Add(searchCard);

        if (_report.CashFlows.Count > 0)
        {
            AddSectionHeading(page, "Deposits / withdrawals / balance operations");
            page.Children.Add(BuildCashFlowGrid());
        }
        return scroll;
    }

    private void ReportScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        SetZoom(_zoom + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, 0.60, 2.25);
        _scale.ScaleX = _zoom;
        _scale.ScaleY = _zoom;
        _zoomText.Text = $"{_zoom:P0}";
    }

    private void InvalidateReportCharts()
    {
        foreach (ReportGraphControl chart in FindVisualChildren<ReportGraphControl>(this))
            chart.InvalidateVisual();
        foreach (DonutReportControl chart in FindVisualChildren<DonutReportControl>(this))
            chart.InvalidateVisual();
        foreach (DirectionDonutControl chart in FindVisualChildren<DirectionDonutControl>(this))
            chart.InvalidateVisual();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private static Border CreateCard()
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    private static StackPanel CreateCardTitle(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var titleText = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(titleText);
        var subtitleText = new TextBlock { Text = subtitle, FontSize = 10, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        stack.Children.Add(subtitleText);
        return stack;
    }

    private static void AddPageHeading(Panel page, string title, string subtitle)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        page.Children.Add(titleText);
        var subtitleText = new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 16)
        };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        page.Children.Add(subtitleText);
    }

    private static void AddSectionHeading(Panel page, string title)
    {
        var text = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 7, 0, 8)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        page.Children.Add(text);
    }

    private static void AddMetricCard(Panel parent, string label, string value, MetricTone tone)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 10, 11, 10),
            Margin = new Thickness(4),
            MinHeight = 72
        };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, tone == MetricTone.Accent ? "AccentBrush" : "BorderBrush");

        var stack = new StackPanel();
        var labelText = new TextBlock { Text = label, FontSize = 9.5 };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        stack.Children.Add(labelText);
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (tone == MetricTone.Positive)
            valueText.Foreground = new SolidColorBrush(Color.FromRgb(46, 181, 125));
        else if (tone == MetricTone.Negative)
            valueText.Foreground = new SolidColorBrush(Color.FromRgb(235, 94, 112));
        else if (tone == MetricTone.Accent)
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrightBrush");
        else
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(valueText);
        card.Child = stack;
        parent.Children.Add(card);
    }

    private static Border CreateInfoCallout(string message)
    {
        var border = CreateCard();
        border.BorderThickness = new Thickness(3, 1, 1, 1);
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 10.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        border.Child = text;
        return border;
    }

    private static void AddPercentMeter(Panel parent, string label, double percentage, string detail)
    {
        percentage = Math.Clamp(double.IsFinite(percentage) ? percentage : 0, 0, 100);
        var row = new Grid { Margin = new Thickness(0, 7, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var labelText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 10.5 };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        row.Children.Add(labelText);
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = percentage,
            Height = 8,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        progress.SetResourceReference(Control.ForegroundProperty, "AccentBrightBrush");
        progress.SetResourceReference(Control.BackgroundProperty, "PanelAltBrush");
        Grid.SetColumn(progress, 1);
        row.Children.Add(progress);
        var detailText = new TextBlock
        {
            Text = $"{percentage:0.0}% · {detail}",
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9.5
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetColumn(detailText, 2);
        row.Children.Add(detailText);
        parent.Children.Add(row);
    }

    private Border CreateGraphCard(string title, string subtitle, IReadOnlyList<GraphPoint> points, ReportGraphMode mode, bool percentage, double height)
    {
        Border card = CreateCard();
        var stack = new StackPanel();
        stack.Children.Add(CreateCardTitle(title, subtitle));
        stack.Children.Add(new ReportGraphControl(points, mode, percentage)
        {
            Height = height,
            MinWidth = PageWidth - 60
        });
        card.Child = stack;
        return card;
    }

    private DataGrid BuildTradeGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            Height = 500,
            RowHeight = 28,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "PanelBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "PanelAltBrush");

        void Add(string header, string property, double width, string? format = null)
        {
            var binding = new Binding(property);
            if (format is not null)
                binding.StringFormat = format;
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, Width = new DataGridLength(width) });
        }
        Add("Ticket", nameof(TradeHistoryTrade.Ticket), 90);
        Add("Symbol", nameof(TradeHistoryTrade.Symbol), 90);
        Add("Side", nameof(TradeHistoryTrade.Direction), 64);
        Add("Lot", nameof(TradeHistoryTrade.Volume), 65, "0.00");
        Add("Open time", nameof(TradeHistoryTrade.OpenTime), 150, "g");
        Add("Entry", nameof(TradeHistoryTrade.EntryPrice), 95, "G10");
        Add("SL", nameof(TradeHistoryTrade.StopLoss), 90, "G10");
        Add("TP", nameof(TradeHistoryTrade.TakeProfit), 90, "G10");
        Add("Close time", nameof(TradeHistoryTrade.CloseTime), 150, "g");
        Add("Exit", nameof(TradeHistoryTrade.ExitPrice), 95, "G10");
        Add("Profit", nameof(TradeHistoryTrade.Profit), 90, "N2");
        Add("Commission", nameof(TradeHistoryTrade.Commission), 95, "N2");
        Add("Swap", nameof(TradeHistoryTrade.Swap), 80, "N2");
        Add("Fees", nameof(TradeHistoryTrade.Fees), 80, "N2");
        Add("Net P/L", nameof(TradeHistoryTrade.NetProfit), 95, "N2");
        Add("Close reason", nameof(TradeHistoryTrade.CloseReason), 130);
        Add("Comment", nameof(TradeHistoryTrade.Comment), 180);
        return grid;
    }

    private DataGrid BuildCashFlowGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            Height = Math.Min(340, 70 + _report.CashFlows.Count * 29),
            MinHeight = 140,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        grid.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "PanelBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "PanelAltBrush");
        grid.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding(nameof(TradeHistoryCashFlow.Time)) { StringFormat = "g" }, Width = new DataGridLength(170) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(TradeHistoryCashFlow.Type)), Width = new DataGridLength(150) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Amount", Binding = new Binding(nameof(TradeHistoryCashFlow.Amount)) { StringFormat = "N2" }, Width = new DataGridLength(120) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Balance after", Binding = new Binding(nameof(TradeHistoryCashFlow.BalanceAfter)) { StringFormat = "N2" }, Width = new DataGridLength(140) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Comment", Binding = new Binding(nameof(TradeHistoryCashFlow.Comment)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.ItemsSource = _report.CashFlows.OrderByDescending(x => x.Time).ToArray();
        return grid;
    }

    private DataGrid BuildSymbolGrid()
    {
        var rows = _stats.Trades
            .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SymbolReportRow
            {
                Symbol = g.Key,
                Trades = g.Count(),
                Volume = g.Sum(t => t.Volume),
                NetProfit = g.Sum(t => t.NetProfit),
                GrossProfit = g.Where(t => t.NetProfit > 0).Sum(t => t.NetProfit),
                GrossLoss = g.Where(t => t.NetProfit < 0).Sum(t => t.NetProfit),
                WinRate = g.Count() > 0 ? g.Count(t => t.NetProfit > 0) * 100.0 / g.Count() : 0
            })
            .OrderByDescending(x => x.NetProfit)
            .ToArray();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            Height = Math.Min(420, 75 + rows.Length * 29),
            MinHeight = 150,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        grid.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "PanelBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "PanelAltBrush");
        grid.Columns.Add(new DataGridTextColumn { Header = "Symbol", Binding = new Binding(nameof(SymbolReportRow.Symbol)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Trades", Binding = new Binding(nameof(SymbolReportRow.Trades)) { StringFormat = "N0" }, Width = new DataGridLength(100) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Volume", Binding = new Binding(nameof(SymbolReportRow.Volume)) { StringFormat = "N2" }, Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Win rate", Binding = new Binding(nameof(SymbolReportRow.WinRate)) { StringFormat = "0.00'%'" }, Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Gross profit", Binding = new Binding(nameof(SymbolReportRow.GrossProfit)) { StringFormat = "N2" }, Width = new DataGridLength(125) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Gross loss", Binding = new Binding(nameof(SymbolReportRow.GrossLoss)) { StringFormat = "N2" }, Width = new DataGridLength(125) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Net P/L", Binding = new Binding(nameof(SymbolReportRow.NetProfit)) { StringFormat = "N2" }, Width = new DataGridLength(125) });
        grid.ItemsSource = rows;
        return grid;
    }

    private static string BuildSubtitle(TradeHistoryReportData report)
    {
        DateTime? start = report.Trades.Count > 0 ? report.Trades.Min(t => t.OpenTime) : null;
        DateTime? end = report.Trades.Count > 0 ? report.Trades.Max(t => t.CloseTime) : null;
        string range = start.HasValue && end.HasValue ? $"{start.Value:g} — {end.Value:g}" : "No completed trade range";
        string account = string.IsNullOrWhiteSpace(report.AccountName) ? string.Empty : $" · Account {report.AccountName}";
        string currency = string.IsNullOrWhiteSpace(report.Currency) ? "USD" : report.Currency.Trim().ToUpperInvariant();
        return $"{range}{account} · {report.Trades.Count:N0} completed trades · {currency}";
    }

    private string Money(double value)
    {
        string currency = string.IsNullOrWhiteSpace(_report.Currency) ? "USD" : _report.Currency.Trim().ToUpperInvariant();
        return $"{currency} {value:N2}";
    }

    private string MoneyOrNa(double? value) => value.HasValue ? Money(value.Value) : "N/A";
    private static string FormatFactor(double value) => double.IsPositiveInfinity(value) ? "∞" : value.ToString("0.00", CultureInfo.CurrentCulture);

    private static List<GraphPoint> BuildBalanceCurve(IReadOnlyList<TradeHistoryTrade> trades, IReadOnlyList<TradeHistoryCashFlow> cashFlows, double? startingBalance)
    {
        var events = trades.Select(t => new BalanceEvent(t.CloseTime, t.NetProfit, t.BalanceAfter, t.CloseTime.ToString("g")))
            .Concat(cashFlows.Select(c => new BalanceEvent(c.Time, c.Amount, c.BalanceAfter, c.Time.ToString("g"))))
            .Where(x => x.Time != DateTime.MinValue)
            .OrderBy(x => x.Time)
            .ToList();
        double value = startingBalance ?? 0;
        var points = new List<GraphPoint> { new(value, "Start") };
        foreach (BalanceEvent item in events)
        {
            value = item.BalanceAfter ?? (value + item.Delta);
            points.Add(new GraphPoint(value, item.Label));
        }
        return points;
    }

    private static List<GraphPoint> BuildDrawdownPercentCurve(IReadOnlyList<GraphPoint> balance)
    {
        double peak = double.NegativeInfinity;
        var result = new List<GraphPoint>(balance.Count);
        foreach (GraphPoint point in balance)
        {
            peak = Math.Max(peak, point.Value);
            double percent = peak > 0 ? Math.Max(0, (peak - point.Value) / peak * 100.0) : 0;
            result.Add(new GraphPoint(percent, point.Label));
        }
        return result;
    }

    private static List<GraphPoint> BuildDailyProfit(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => t.CloseTime.Date).OrderBy(g => g.Key)
        .Select(g => new GraphPoint(g.Sum(t => t.NetProfit), g.Key.ToString("yyyy-MM-dd"))).ToList();

    private static List<GraphPoint> BuildDailyTradeCount(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => t.CloseTime.Date).OrderBy(g => g.Key)
        .Select(g => new GraphPoint(g.Count(), g.Key.ToString("yyyy-MM-dd"))).ToList();

    private static List<GraphPoint> BuildSymbolPerformance(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Sum(t => t.NetProfit))
        .Select(g => new GraphPoint(g.Sum(t => t.NetProfit), g.Key)).ToList();

    private static List<GraphPoint> BuildSymbolTradeCount(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
        .Select(g => new GraphPoint(g.Count(), g.Key)).ToList();

    private static List<GraphPoint> BuildSymbolWinRate(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key)
        .Select(g => new GraphPoint(g.Count() > 0 ? g.Count(t => t.NetProfit > 0) * 100.0 / g.Count() : 0, g.Key)).ToList();

    private static List<GraphPoint> BuildMonthlyProfit(IEnumerable<TradeHistoryTrade> trades) => trades
        .GroupBy(t => new DateTime(t.CloseTime.Year, t.CloseTime.Month, 1)).OrderBy(g => g.Key)
        .Select(g => new GraphPoint(g.Sum(t => t.NetProfit), g.Key.ToString("yyyy-MM"))).ToList();

    private static List<GraphPoint> BuildTradeProfitSequence(IEnumerable<TradeHistoryTrade> trades) => trades
        .OrderBy(t => t.CloseTime).Select((t, i) => new GraphPoint(t.NetProfit, $"#{i + 1}")).ToList();

    private static int MaxStreak(IEnumerable<TradeHistoryTrade> trades, bool positive)
    {
        int max = 0;
        int current = 0;
        foreach (TradeHistoryTrade trade in trades.OrderBy(t => t.CloseTime))
        {
            bool match = positive ? trade.NetProfit > 0 : trade.NetProfit < 0;
            current = match ? current + 1 : 0;
            max = Math.Max(max, current);
        }
        return max;
    }

    private static double ComputeTradeSharpe(IReadOnlyList<TradeHistoryTrade> trades)
    {
        if (trades.Count < 2)
            return double.NaN;
        double mean = trades.Average(t => t.NetProfit);
        double variance = trades.Sum(t => Math.Pow(t.NetProfit - mean, 2)) / (trades.Count - 1);
        double std = Math.Sqrt(Math.Max(0, variance));
        return std > 0 ? mean / std * Math.Sqrt(trades.Count) : double.NaN;
    }

    private static double ComputeMaxDrawdown(IReadOnlyList<GraphPoint> balance, out double percent)
    {
        double peak = double.NegativeInfinity;
        double max = 0;
        double maxPct = 0;
        foreach (GraphPoint point in balance)
        {
            peak = Math.Max(peak, point.Value);
            double drawdown = peak - point.Value;
            max = Math.Max(max, drawdown);
            if (peak > 0)
                maxPct = Math.Max(maxPct, drawdown / peak * 100.0);
        }
        percent = maxPct;
        return max;
    }

    private static string FormatDuration(double minutes)
    {
        if (!double.IsFinite(minutes) || minutes <= 0)
            return "0m";
        if (minutes < 60)
            return $"{minutes:0.#}m";
        if (minutes < 1440)
            return $"{minutes / 60.0:0.##}h";
        return $"{minutes / 1440.0:0.##}d";
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private readonly record struct GraphPoint(double Value, string Label);
    private readonly record struct BalanceEvent(DateTime Time, double Delta, double? BalanceAfter, string Label);
    private enum ReportGraphMode { Line, Bar }
    private enum MetricTone { Neutral, Accent, Positive, Negative }

    private sealed class SymbolReportRow
    {
        public string Symbol { get; set; } = string.Empty;
        public int Trades { get; set; }
        public double Volume { get; set; }
        public double WinRate { get; set; }
        public double GrossProfit { get; set; }
        public double GrossLoss { get; set; }
        public double NetProfit { get; set; }
    }

    private sealed class ReportStats
    {
        public required List<TradeHistoryTrade> Trades { get; init; }
        public double NetProfit { get; init; }
        public double GrossProfit { get; init; }
        public double GrossLoss { get; init; }
        public int Wins { get; init; }
        public int Losses { get; init; }
        public int Breakeven { get; init; }
        public double WinRate { get; init; }
        public double ProfitFactor { get; init; }
        public double AverageWin { get; init; }
        public double AverageLoss { get; init; }
        public double LargestWin { get; init; }
        public double LargestLoss { get; init; }
        public double Volume { get; init; }
        public double Commission { get; init; }
        public double Swap { get; init; }
        public double Fees { get; init; }
        public double ExpectedPayoff { get; init; }
        public double AverageDurationMinutes { get; init; }
        public int MaxWinStreak { get; init; }
        public int MaxLossStreak { get; init; }
        public int BuyCount { get; init; }
        public int SellCount { get; init; }
        public double BuyWinRate { get; init; }
        public double SellWinRate { get; init; }
        public double BuyNetProfit { get; init; }
        public double SellNetProfit { get; init; }
        public double BestDay { get; init; }
        public double WorstDay { get; init; }
        public int ProfitableDays { get; init; }
        public int LosingDays { get; init; }
        public double MaxDrawdown { get; init; }
        public double MaxDrawdownPercent { get; init; }
        public string RecoveryFactorText { get; init; } = "N/A";
        public string SharpeText { get; init; } = "N/A";

        public static ReportStats Create(TradeHistoryReportData report)
        {
            List<TradeHistoryTrade> trades = report.Trades.OrderBy(t => t.CloseTime).ToList();
            double grossProfit = trades.Where(t => t.NetProfit > 0).Sum(t => t.NetProfit);
            double grossLoss = trades.Where(t => t.NetProfit < 0).Sum(t => t.NetProfit);
            int wins = trades.Count(t => t.NetProfit > 0);
            int losses = trades.Count(t => t.NetProfit < 0);
            int buyCount = trades.Count(t => string.Equals(t.Direction, "BUY", StringComparison.OrdinalIgnoreCase));
            int sellCount = trades.Count(t => string.Equals(t.Direction, "SELL", StringComparison.OrdinalIgnoreCase));
            double net = trades.Sum(t => t.NetProfit);
            List<IGrouping<DateTime, TradeHistoryTrade>> daily = trades.GroupBy(t => t.CloseTime.Date).ToList();
            List<GraphPoint> balance = BuildBalanceCurve(trades, report.CashFlows, report.StartingBalance);
            double maxDrawdown = ComputeMaxDrawdown(balance, out double maxDrawdownPercent);
            double sharpe = ComputeTradeSharpe(trades);
            return new ReportStats
            {
                Trades = trades,
                NetProfit = net,
                GrossProfit = grossProfit,
                GrossLoss = grossLoss,
                Wins = wins,
                Losses = losses,
                Breakeven = trades.Count - wins - losses,
                WinRate = trades.Count > 0 ? wins * 100.0 / trades.Count : 0,
                ProfitFactor = grossLoss < 0 ? grossProfit / Math.Abs(grossLoss) : grossProfit > 0 ? double.PositiveInfinity : 0,
                AverageWin = wins > 0 ? grossProfit / wins : 0,
                AverageLoss = losses > 0 ? grossLoss / losses : 0,
                LargestWin = trades.Count > 0 ? trades.Max(t => t.NetProfit) : 0,
                LargestLoss = trades.Count > 0 ? trades.Min(t => t.NetProfit) : 0,
                Volume = trades.Sum(t => t.Volume),
                Commission = trades.Sum(t => t.Commission),
                Swap = trades.Sum(t => t.Swap),
                Fees = trades.Sum(t => t.Fees),
                ExpectedPayoff = trades.Count > 0 ? net / trades.Count : 0,
                AverageDurationMinutes = trades.Count > 0 ? trades.Average(t => t.Duration.TotalMinutes) : 0,
                MaxWinStreak = MaxStreak(trades, true),
                MaxLossStreak = MaxStreak(trades, false),
                BuyCount = buyCount,
                SellCount = sellCount,
                BuyWinRate = buyCount > 0 ? trades.Count(t => string.Equals(t.Direction, "BUY", StringComparison.OrdinalIgnoreCase) && t.NetProfit > 0) * 100.0 / buyCount : 0,
                SellWinRate = sellCount > 0 ? trades.Count(t => string.Equals(t.Direction, "SELL", StringComparison.OrdinalIgnoreCase) && t.NetProfit > 0) * 100.0 / sellCount : 0,
                BuyNetProfit = trades.Where(t => string.Equals(t.Direction, "BUY", StringComparison.OrdinalIgnoreCase)).Sum(t => t.NetProfit),
                SellNetProfit = trades.Where(t => string.Equals(t.Direction, "SELL", StringComparison.OrdinalIgnoreCase)).Sum(t => t.NetProfit),
                BestDay = daily.Count > 0 ? daily.Max(g => g.Sum(t => t.NetProfit)) : 0,
                WorstDay = daily.Count > 0 ? daily.Min(g => g.Sum(t => t.NetProfit)) : 0,
                ProfitableDays = daily.Count(g => g.Sum(t => t.NetProfit) > 0),
                LosingDays = daily.Count(g => g.Sum(t => t.NetProfit) < 0),
                MaxDrawdown = maxDrawdown,
                MaxDrawdownPercent = maxDrawdownPercent,
                RecoveryFactorText = maxDrawdown > 0 ? (net / maxDrawdown).ToString("0.00", CultureInfo.CurrentCulture) : "N/A",
                SharpeText = double.IsFinite(sharpe) ? sharpe.ToString("0.00", CultureInfo.CurrentCulture) : "N/A"
            };
        }
    }

    private sealed class ReportGraphControl : FrameworkElement
    {
        private readonly IReadOnlyList<GraphPoint> _points;
        private readonly ReportGraphMode _mode;
        private readonly bool _percentage;
        private int _hoverIndex = -1;

        public ReportGraphControl(IReadOnlyList<GraphPoint> points, ReportGraphMode mode, bool percentage)
        {
            _points = points;
            _mode = mode;
            _percentage = percentage;
            SnapsToDevicePixels = true;
            MouseMove += OnMouseMove;
            MouseLeave += (_, _) => { _hoverIndex = -1; ToolTip = null; InvalidateVisual(); };
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_points.Count == 0 || ActualWidth <= 90)
                return;
            Rect plot = GetPlotRect();
            Point mouse = e.GetPosition(this);
            if (!plot.Contains(mouse))
                return;
            double ratio = Math.Clamp((mouse.X - plot.Left) / Math.Max(1, plot.Width), 0, 1);
            int index = (int)Math.Round(ratio * Math.Max(0, _points.Count - 1));
            index = Math.Clamp(index, 0, _points.Count - 1);
            if (_hoverIndex == index)
                return;
            _hoverIndex = index;
            GraphPoint point = _points[index];
            ToolTip = $"{point.Label}\n{FormatValue(point.Value, _percentage)}";
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Brush background = ResourceBrush("PanelBrush", Brushes.White);
            Brush textBrush = ResourceBrush("TextBrush", Brushes.Black);
            Brush muted = ResourceBrush("MutedTextBrush", Brushes.DimGray);
            Brush gridBrush = ResourceBrush("BorderBrush", Brushes.LightGray);
            Brush accent = ResourceBrush("AccentBrightBrush", Brushes.DodgerBlue);
            Color positiveColor = Color.FromRgb(46, 181, 125);
            Color negativeColor = Color.FromRgb(235, 94, 112);
            Brush positive = new SolidColorBrush(positiveColor);
            Brush negative = new SolidColorBrush(negativeColor);

            dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            Rect plot = GetPlotRect();
            if (_points.Count == 0)
            {
                DrawText(dc, "No data available", new Point(plot.Left + 12, plot.Top + 18), muted, 11);
                return;
            }

            double min = _points.Min(p => p.Value);
            double max = _points.Max(p => p.Value);
            if (_mode == ReportGraphMode.Bar)
            {
                min = Math.Min(0, min);
                max = Math.Max(0, max);
            }
            if (_percentage)
            {
                min = Math.Min(0, min);
                max = Math.Max(100, max);
            }
            if (Math.Abs(max - min) < 1e-9)
            {
                max += 1;
                min -= 1;
            }
            double pad = Math.Max(1e-9, (max - min) * 0.06);
            max += pad;
            min -= pad;

            var gridPen = new Pen(gridBrush, 0.75) { DashStyle = DashStyles.Dash };
            var axisPen = new Pen(gridBrush, 1.0);
            for (int i = 0; i <= 5; i++)
            {
                double y = plot.Top + plot.Height * i / 5.0;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                double value = max - (max - min) * i / 5.0;
                DrawText(dc, FormatValue(value, _percentage), new Point(2, y - 7), muted, 9);
            }
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
            double Y(double value) => plot.Bottom - (value - min) / (max - min) * plot.Height;
            double zeroY = Y(0);
            if (zeroY >= plot.Top && zeroY <= plot.Bottom)
                dc.DrawLine(axisPen, new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));

            if (_mode == ReportGraphMode.Line)
            {
                var linePen = new Pen(accent, 2.0);
                Point? previous = null;
                for (int i = 0; i < _points.Count; i++)
                {
                    double x = XFor(i, plot);
                    Point point = new(x, Y(_points[i].Value));
                    if (previous.HasValue)
                        dc.DrawLine(linePen, previous.Value, point);
                    previous = point;
                }
            }
            else
            {
                double step = plot.Width / Math.Max(1, _points.Count);
                double barWidth = Math.Max(1.5, Math.Min(26, step * 0.72));
                for (int i = 0; i < _points.Count; i++)
                {
                    double x = plot.Left + step * i + (step - barWidth) / 2;
                    double y = Y(_points[i].Value);
                    double baseline = zeroY >= plot.Top && zeroY <= plot.Bottom ? zeroY : plot.Bottom;
                    double top = Math.Min(y, baseline);
                    double height = Math.Max(1, Math.Abs(baseline - y));
                    Brush barBrush = _points[i].Value >= 0 ? positive : negative;
                    dc.DrawRoundedRectangle(barBrush, null, new Rect(x, top, barWidth, height), 2, 2);
                }
            }

            int labelEvery = Math.Max(1, (int)Math.Ceiling(_points.Count / 8.0));
            for (int i = 0; i < _points.Count; i += labelEvery)
            {
                double x = XFor(i, plot);
                string label = _points[i].Label.Length > 13 ? _points[i].Label[..13] : _points[i].Label;
                DrawText(dc, label, new Point(Math.Max(plot.Left, x - 28), plot.Bottom + 7), muted, 8.5);
            }

            if (_hoverIndex >= 0 && _hoverIndex < _points.Count)
            {
                double x = XFor(_hoverIndex, plot);
                var hoverPen = new Pen(accent, 1.0) { DashStyle = DashStyles.Dot };
                dc.DrawLine(hoverPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                Point point = new(x, Y(_points[_hoverIndex].Value));
                dc.DrawEllipse(background, new Pen(accent, 2.2), point, 4.2, 4.2);
            }
        }

        private Rect GetPlotRect() => new(72, 12, Math.Max(10, ActualWidth - 88), Math.Max(10, ActualHeight - 45));
        private double XFor(int index, Rect plot) => _points.Count == 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * index / Math.Max(1, _points.Count - 1);
        private static string FormatValue(double value, bool percentage) => percentage ? $"{value:0.##}%" : Math.Abs(value) >= 1_000_000 ? $"{value / 1_000_000:0.#}M" : Math.Abs(value) >= 1000 ? $"{value / 1000:0.#}K" : $"{value:0.##}";
        private static void DrawText(DrawingContext dc, string text, Point point, Brush brush, double size)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
            dc.DrawText(formatted, point);
        }
    }

    private sealed class DonutReportControl : FrameworkElement
    {
        private readonly int _wins;
        private readonly int _losses;
        private readonly int _breakeven;
        private readonly string _caption;

        public DonutReportControl(int wins, int losses, int breakeven, string caption)
        {
            _wins = wins;
            _losses = losses;
            _breakeven = breakeven;
            _caption = caption;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Brush text = ResourceBrush("TextBrush", Brushes.Black);
            Brush muted = ResourceBrush("MutedTextBrush", Brushes.DimGray);
            Brush track = ResourceBrush("PanelAltBrush", Brushes.LightGray);
            Brush win = new SolidColorBrush(Color.FromRgb(46, 181, 125));
            Brush loss = new SolidColorBrush(Color.FromRgb(235, 94, 112));
            Brush flat = ResourceBrush("AccentBrightBrush", Brushes.DodgerBlue);
            double total = Math.Max(1, _wins + _losses + _breakeven);
            Point center = new(ActualWidth / 2, ActualHeight / 2 - 8);
            double radius = Math.Max(30, Math.Min(ActualWidth, ActualHeight) * 0.29);
            double thickness = Math.Max(10, radius * 0.17);
            var trackPen = new Pen(track, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawEllipse(null, trackPen, center, radius, radius);
            double start = -90;
            start = DrawArc(dc, center, radius, thickness, start, 360.0 * _wins / total, win);
            start = DrawArc(dc, center, radius, thickness, start, 360.0 * _losses / total, loss);
            DrawArc(dc, center, radius, thickness, start, 360.0 * _breakeven / total, flat);
            double winRate = (_wins + _losses + _breakeven) > 0 ? _wins * 100.0 / (_wins + _losses + _breakeven) : 0;
            DrawCentered(dc, $"{winRate:0.0}%", center.X, center.Y - 11, text, 22, FontWeights.SemiBold);
            DrawCentered(dc, _caption, center.X, center.Y + 18, muted, 10, FontWeights.Normal);
            DrawLegend(dc, center, radius, win, loss, flat, muted);
        }

        private void DrawLegend(DrawingContext dc, Point center, double radius, Brush win, Brush loss, Brush flat, Brush text)
        {
            double y = center.Y + radius + 28;
            double x = Math.Max(6, center.X - radius);
            DrawLegendItem(dc, x, y, win, $"Wins {_wins:N0}", text);
            DrawLegendItem(dc, x + radius * 0.78, y, loss, $"Losses {_losses:N0}", text);
            if (_breakeven > 0)
                DrawLegendItem(dc, x + radius * 1.55, y, flat, $"Flat {_breakeven:N0}", text);
        }
    }

    private sealed class DirectionDonutControl : FrameworkElement
    {
        private readonly int _buy;
        private readonly int _sell;
        public DirectionDonutControl(int buy, int sell) { _buy = buy; _sell = sell; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Brush text = ResourceBrush("TextBrush", Brushes.Black);
            Brush muted = ResourceBrush("MutedTextBrush", Brushes.DimGray);
            Brush track = ResourceBrush("PanelAltBrush", Brushes.LightGray);
            Brush buyBrush = new SolidColorBrush(Color.FromRgb(67, 145, 246));
            Brush sellBrush = new SolidColorBrush(Color.FromRgb(235, 103, 76));
            double total = Math.Max(1, _buy + _sell);
            Point center = new(ActualWidth / 2, ActualHeight / 2 - 8);
            double radius = Math.Max(30, Math.Min(ActualWidth, ActualHeight) * 0.29);
            double thickness = Math.Max(10, radius * 0.17);
            dc.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
            double start = -90;
            start = DrawArc(dc, center, radius, thickness, start, 360.0 * _buy / total, buyBrush);
            DrawArc(dc, center, radius, thickness, start, 360.0 * _sell / total, sellBrush);
            double buyPct = (_buy + _sell) > 0 ? _buy * 100.0 / (_buy + _sell) : 0;
            DrawCentered(dc, $"{buyPct:0.0}%", center.X, center.Y - 11, text, 22, FontWeights.SemiBold);
            DrawCentered(dc, "BUY share", center.X, center.Y + 18, muted, 10, FontWeights.Normal);
            double y = center.Y + radius + 28;
            DrawLegendItem(dc, Math.Max(6, center.X - radius), y, buyBrush, $"BUY {_buy:N0}", muted);
            DrawLegendItem(dc, center.X + 8, y, sellBrush, $"SELL {_sell:N0}", muted);
        }
    }

    private static double DrawArc(DrawingContext dc, Point center, double radius, double thickness, double startAngle, double sweepAngle, Brush brush)
    {
        if (sweepAngle <= 0.1)
            return startAngle + sweepAngle;
        double safeSweep = Math.Min(359.999, sweepAngle);
        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + safeSweep);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, safeSweep > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        dc.DrawGeometry(null, pen, geometry);
        return startAngle + sweepAngle;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private static void DrawCentered(DrawingContext dc, string text, double centerX, double y, Brush brush, double size, FontWeight weight)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0);
        dc.DrawText(formatted, new Point(centerX - formatted.Width / 2, y - formatted.Height / 2));
    }

    private static void DrawLegendItem(DrawingContext dc, double x, double y, Brush color, string text, Brush textBrush)
    {
        dc.DrawEllipse(color, null, new Point(x + 4, y + 5), 4, 4);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, textBrush, 1.0);
        dc.DrawText(formatted, new Point(x + 12, y - 2));
    }
}
