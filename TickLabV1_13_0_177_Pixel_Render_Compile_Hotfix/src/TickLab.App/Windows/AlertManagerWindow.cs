using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TickLab.Core.Alerts;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed class AlertManagerWindow : Window
{
    private readonly ListView _ruleList;
    private readonly ListView _logList;
    private bool _allowClose;

    public AlertManagerWindow()
    {
        Title = "TickLab Alerts";
        Width = 880;
        Height = 590;
        MinWidth = 720;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Loaded += (_, _) => ApplicationThemeManager.ApplyToWindow(this);

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tabs = new TabControl();
        tabs.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        tabs.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tabs.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        _ruleList = new ListView { Margin = new Thickness(6), SelectionMode = SelectionMode.Single };
        _ruleList.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        _ruleList.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _ruleList.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        _ruleList.ItemTemplate = CreateAlertRuleTemplate();
        _ruleList.MouseDoubleClick += (_, _) =>
        {
            if (_ruleList.SelectedItem is AlertRuleView view)
                EditRequested?.Invoke(view.Rule);
        };
        _logList = new ListView { Margin = new Thickness(6) };
        _logList.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        _logList.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _logList.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        var activeTab = new TabItem { Header = "Active alerts", Content = _ruleList };
        var logTab = new TabItem { Header = "Alert log", Content = _logList };
        activeTab.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        logTab.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tabs.Items.Add(activeTab);
        tabs.Items.Add(logTab);
        root.Children.Add(tabs);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var add = CreateButton("New alert", 88);
        add.Click += (_, _) => NewRequested?.Invoke();
        var edit = CreateButton("Edit", 70);
        edit.Click += (_, _) =>
        {
            if (_ruleList.SelectedItem is AlertRuleView view)
                EditRequested?.Invoke(view.Rule);
        };
        var toggle = CreateButton("Enable / disable", 112);
        toggle.Click += (_, _) =>
        {
            if (_ruleList.SelectedItem is AlertRuleView view)
                ToggleRequested?.Invoke(view.Rule);
        };
        var delete = CreateButton("Delete", 74);
        delete.Click += (_, _) =>
        {
            if (_ruleList.SelectedItem is AlertRuleView view)
                DeleteRequested?.Invoke(view.Rule);
        };
        var deleteSelected = CreateButton("Delete selected", 108);
        deleteSelected.ToolTip = "Tick several active alerts, then remove them together with one confirmation.";
        deleteSelected.Click += (_, _) =>
        {
            AlertRule[] checkedRules = _ruleList.Items
                .OfType<AlertRuleView>()
                .Where(view => view.IsChecked)
                .Select(view => view.Rule)
                .ToArray();
            if (checkedRules.Length == 0)
            {
                MessageBox.Show(this, "Tick one or more alerts first.", "Alerts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DeleteSelectedRequested?.Invoke(checkedRules);
        };
        var lineColor = CreateButton("Line colour", 92);
        lineColor.ToolTip = "Choose the chart line/ticket colour for the selected alert.";
        lineColor.Click += (_, _) =>
        {
            if (_ruleList.SelectedItem is AlertRuleView view)
                LineColorRequested?.Invoke(view.Rule);
        };
        var linePixels = CreateButton("Line pixels", 88);
        linePixels.ToolTip = "Choose chart alert-line thickness for the selected alert.";
        var pixelMenu = new ContextMenu { Background = Brushes.White, Foreground = Brushes.Black };
        foreach (double pixels in new[] { 1.0, 2.0, 3.0, 4.0, 5.0 })
        {
            var item = new MenuItem { Header = $"{pixels:0.#} px", Tag = pixels, Background = Brushes.White, Foreground = Brushes.Black };
            item.Click += (_, _) =>
            {
                if (_ruleList.SelectedItem is AlertRuleView view)
                    LineThicknessRequested?.Invoke(view.Rule, (double)item.Tag);
            };
            pixelMenu.Items.Add(item);
        }
        linePixels.ContextMenu = pixelMenu;
        linePixels.Click += (_, _) =>
        {
            if (_ruleList.SelectedItem is not AlertRuleView)
                return;
            linePixels.ContextMenu.PlacementTarget = linePixels;
            linePixels.ContextMenu.IsOpen = true;
        };
        var clearLog = CreateButton("Clear log", 82);
        clearLog.Click += (_, _) => ClearLogRequested?.Invoke();
        var close = CreateButton("Close", 70);
        close.Click += (_, _) => Hide();
        buttons.Children.Add(add);
        buttons.Children.Add(edit);
        buttons.Children.Add(toggle);
        buttons.Children.Add(delete);
        buttons.Children.Add(deleteSelected);
        buttons.Children.Add(lineColor);
        buttons.Children.Add(linePixels);
        buttons.Children.Add(clearLog);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
        ApplicationThemeManager.ApplyToWindow(this);

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;
            e.Cancel = true;
            Hide();
        };
    }

    public event Action? NewRequested;
    public event Action<AlertRule>? EditRequested;
    public event Action<AlertRule>? ToggleRequested;
    public event Action<AlertRule>? DeleteRequested;
    public event Action<IReadOnlyList<AlertRule>>? DeleteSelectedRequested;
    public event Action<AlertRule>? LineColorRequested;
    public event Action<AlertRule, double>? LineThicknessRequested;
    public event Action? ClearLogRequested;

    public void SetDocument(AlertDocument document)
    {
        _ruleList.ItemsSource = document.Rules
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.Symbol)
            .ThenBy(item => item.Name)
            .Select(item => new AlertRuleView(item))
            .ToArray();

        _logList.ItemsSource = document.Log
            .OrderByDescending(item => item.TriggeredUnix)
            .Select(item => new AlertLogView(item))
            .ToArray();
        _logList.DisplayMemberPath = nameof(AlertLogView.Display);
    }

    public AlertRule? SelectedRule => (_ruleList.SelectedItem as AlertRuleView)?.Rule;

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private static Button CreateButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        Height = 30,
        Margin = new Thickness(0, 0, 6, 0)
    };

    private static DataTemplate CreateAlertRuleTemplate()
    {
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(AlertRuleView.IsChecked))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(AlertRuleView.Display)));
        check.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 3, 2, 3));
        return new DataTemplate { VisualTree = check };
    }

    private sealed class AlertRuleView
    {
        public AlertRuleView(AlertRule rule) => Rule = rule;
        public AlertRule Rule { get; }
        public bool IsChecked { get; set; }
        public string Display =>
            $"{(Rule.Enabled ? "●" : "○")}  {Rule.Name}   |   Chart {Rule.ChartId} · {Rule.Symbol} · {Rule.Timeframe}   |   {Rule.Condition} {Rule.Threshold:G8}   |   {Rule.Frequency}   |   {Rule.LineThickness:0.#} px";
    }

    private sealed record AlertLogView(AlertLogEntry Entry)
    {
        public string Display =>
            $"{DateTimeOffset.FromUnixTimeSeconds(Entry.TriggeredUnix):yyyy-MM-dd HH:mm:ss}   {Entry.AlertName}   {Entry.Symbol} {Entry.Timeframe}   {Entry.Message}";
    }
}
