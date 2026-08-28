using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TickLab.Desktop.Windows;

public sealed class MarketReplayWindow : Window
{
    private readonly Grid _fullRoot;
    private readonly Border _compactRoot;
    private readonly DatePicker _datePicker;
    private readonly TextBox _timeBox;
    private readonly Button _speedButton;
    private readonly Button _compactSpeedButton;
    private readonly Button _playButton;
    private readonly Button _compactPlayButton;
    private readonly Button _reverseButton;
    private readonly Button _compactReverseButton;
    private readonly Button _forwardButton;
    private readonly Button _compactForwardButton;
    private readonly Button _startColorButton;
    private readonly Button _endColorButton;
    private readonly Button _startThicknessButton;
    private readonly Button _endThicknessButton;
    private readonly CheckBox _replayLineCheckBox;
    private readonly CheckBox _compactReplayLineCheckBox;
    private readonly CheckBox _replayRangeCheckBox;
    private readonly CheckBox _compactReplayRangeCheckBox;
    private readonly TextBlock _statusText;
    private readonly TextBlock _progressText;
    private bool _allowClose;
    private bool _synchronizingReplayLineCheckBox;
    private bool _synchronizingReplayRangeCheckBox;
    private bool _isCompact;
    private double _selectedSpeed = 1;
    private string _startLineColor = "#FACC15";
    private string _endLineColor = "#EF4444";
    private double _startLineThickness = 1.0;
    private double _endLineThickness = 1.0;
    private double _fullWidth = 760;
    private double _fullHeight = 420;

    public MarketReplayWindow(int chartId, string symbol, string timeframe, DateTime initialServerTime)
    {
        Title = $"Market Replay — Chart {chartId} · {symbol} · {timeframe}";
        Width = 760;
        Height = 420;
        MinWidth = 700;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(10, 10, 10));
        Foreground = Brushes.White;
        ShowInTaskbar = false;

        var shell = new Grid();

        _fullRoot = new Grid { Margin = new Thickness(16) };
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Tick-by-tick market replay",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _fullRoot.Children.Add(heading);

        var help = new TextBlock
        {
            Text = "Tick Replay line to place the selector inside the candles currently visible on the chart. Drag the yellow start line. Enable Replay range for a red end line. Moving the lines never starts replay; press Play when the selection is ready.",
            Foreground = new SolidColorBrush(Color.FromRgb(176, 184, 196)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(help, 1);
        _fullRoot.Children.Add(help);

        var timeGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _datePicker = new DatePicker
        {
            SelectedDate = initialServerTime.Date,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _timeBox = new TextBox
        {
            Text = initialServerTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Yellow replay-start broker-server time (HH:mm:ss)"
        };

        var selectorOptions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        _replayLineCheckBox = CreateReplayCheckBox("Replay line", "Tick to show the replay selector(s). Untick to remove them.");
        _replayRangeCheckBox = CreateReplayCheckBox("Replay range", "Show two selectors: yellow = start, red = end.");
        _replayRangeCheckBox.Margin = new Thickness(14, 0, 0, 0);
        selectorOptions.Children.Add(_replayLineCheckBox);
        selectorOptions.Children.Add(_replayRangeCheckBox);

        timeGrid.Children.Add(_datePicker);
        Grid.SetColumn(_timeBox, 1);
        timeGrid.Children.Add(_timeBox);
        Grid.SetColumn(selectorOptions, 2);
        timeGrid.Children.Add(selectorOptions);
        Grid.SetRow(timeGrid, 2);
        _fullRoot.Children.Add(timeGrid);

        var colorControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        colorControls.Children.Add(new TextBlock
        {
            Text = "Selector colours",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(184, 192, 204))
        });
        _startColorButton = CreateButton("Start colour", 104);
        _startColorButton.ToolTip = "Choose the yellow START selector colour.";
        _startColorButton.Click += (_, _) => ChooseReplayLineColor(isStart: true);
        _endColorButton = CreateButton("End colour", 104);
        _endColorButton.ToolTip = "Choose the red END selector colour.";
        _endColorButton.Click += (_, _) => ChooseReplayLineColor(isStart: false);
        _startThicknessButton = CreateThicknessButton("Start 1 px", isStart: true);
        _startThicknessButton.ToolTip = "Choose START selector thickness in pixels.";
        _endThicknessButton = CreateThicknessButton("End 1 px", isStart: false);
        _endThicknessButton.ToolTip = "Choose END selector thickness in pixels.";
        colorControls.Children.Add(_startColorButton);
        colorControls.Children.Add(_startThicknessButton);
        colorControls.Children.Add(_endColorButton);
        colorControls.Children.Add(_endThicknessButton);
        Grid.SetRow(colorControls, 3);
        _fullRoot.Children.Add(colorControls);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var loadButton = CreateButton("Load ticks", 92);
        loadButton.ToolTip = "Optional preload. Play also loads automatically from the yellow line.";
        loadButton.Click += (_, _) =>
        {
            if (TryGetSelectedServerTime(out DateTime value))
                LoadRequested?.Invoke(value);
        };
        _playButton = CreateButton("▶ Play", 78);
        _playButton.Click += (_, _) => PlayPauseRequested?.Invoke();
        _reverseButton = CreateButton("◀ Reverse", 88);
        _reverseButton.ToolTip = "Play backward through ticks already revealed in this replay.";
        _reverseButton.Click += (_, _) => ReverseRequested?.Invoke();
        _forwardButton = CreateButton("Forward ▶", 88);
        _forwardButton.ToolTip = "Play forward from the current replay position.";
        _forwardButton.Click += (_, _) => ForwardRequested?.Invoke();
        var tickButton = CreateButton("Step tick", 82);
        tickButton.Click += (_, _) => StepTickRequested?.Invoke();
        var candleButton = CreateButton("Step candle", 96);
        candleButton.Click += (_, _) => StepCandleRequested?.Invoke();
        _speedButton = CreateSpeedButton();
        controls.Children.Add(loadButton);
        controls.Children.Add(_playButton);
        controls.Children.Add(_reverseButton);
        controls.Children.Add(_forwardButton);
        controls.Children.Add(tickButton);
        controls.Children.Add(candleButton);
        controls.Children.Add(_speedButton);
        Grid.SetRow(controls, 4);
        _fullRoot.Children.Add(controls);

        var statusPanel = new Border
        {
            MinHeight = 74,
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var statusStack = new StackPanel();
        _statusText = new TextBlock
        {
            Text = "Tick Replay line to place a yellow start selector.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        _progressText = new TextBlock
        {
            Text = "Alerts are disabled during replay.",
            Foreground = new SolidColorBrush(Color.FromRgb(167, 176, 190)),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        statusStack.Children.Add(_statusText);
        statusStack.Children.Add(_progressText);
        statusPanel.Child = statusStack;
        Grid.SetRow(statusPanel, 5);
        _fullRoot.Children.Add(statusPanel);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var compactButton = CreateButton("▁ Compact", 94);
        compactButton.ToolTip = "Collapse Replay to the small control tab.";
        compactButton.Click += (_, _) => SetCompactMode(true);
        var endButton = CreateButton("End replay", 98);
        endButton.Click += (_, _) => StopRequested?.Invoke();
        var closeButton = CreateButton("Close", 72);
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(compactButton);
        footer.Children.Add(endButton);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 6);
        _fullRoot.Children.Add(footer);

        // Compact Replay tab. It uses the same replay state as the full window;
        // these are only mirrored controls, not a second replay instance.
        _compactPlayButton = CreateButton("▶ Play", 78);
        _compactPlayButton.Click += (_, _) => PlayPauseRequested?.Invoke();
        _compactReverseButton = CreateButton("◀ Reverse", 88);
        _compactReverseButton.Click += (_, _) => ReverseRequested?.Invoke();
        _compactForwardButton = CreateButton("Forward ▶", 88);
        _compactForwardButton.Click += (_, _) => ForwardRequested?.Invoke();
        _compactSpeedButton = CreateSpeedButton();
        _compactReplayLineCheckBox = CreateReplayCheckBox("Line", "Show/hide the yellow replay start selector.");
        _compactReplayRangeCheckBox = CreateReplayCheckBox("Range", "Turn yellow/red replay range selectors on or off.");
        _compactReplayLineCheckBox.Margin = new Thickness(8, 0, 0, 0);
        _compactReplayRangeCheckBox.Margin = new Thickness(10, 0, 0, 0);
        var compactEndButton = CreateButton("End Replay", 88);
        compactEndButton.ToolTip = "End replay and return this chart to the continuously updated live view.";
        compactEndButton.Click += (_, _) => StopRequested?.Invoke();
        var expandButton = CreateButton("⛶", 36);
        expandButton.FontSize = 16;
        expandButton.ToolTip = "Expand the full Replay window.";
        expandButton.Click += (_, _) => SetCompactMode(false);

        var compactControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        compactControls.Children.Add(_compactPlayButton);
        compactControls.Children.Add(_compactReverseButton);
        compactControls.Children.Add(_compactForwardButton);
        compactControls.Children.Add(_compactSpeedButton);
        compactControls.Children.Add(_compactReplayLineCheckBox);
        compactControls.Children.Add(_compactReplayRangeCheckBox);
        compactControls.Children.Add(compactEndButton);
        compactControls.Children.Add(expandButton);

        _compactRoot = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 52, 52)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 9, 8, 9),
            Margin = new Thickness(8),
            Child = compactControls
        };

        WireReplayCheckBoxes();
        SetReplayLineStyles(_startLineColor, _endLineColor, _startLineThickness, _endLineThickness);
        SetSelectedSpeed(1, raiseEvent: false);

        shell.Children.Add(_fullRoot);
        shell.Children.Add(_compactRoot);
        Content = shell;

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;
            e.Cancel = true;
            Hide();
        };
    }

    public event Action<bool>? ReplayLineChanged;
    public event Action<bool>? ReplayRangeChanged;
    public event Action<DateTime>? LoadRequested;
    public event Action? PlayPauseRequested;
    public event Action? ReverseRequested;
    public event Action? ForwardRequested;
    public event Action? StepTickRequested;
    public event Action? StepCandleRequested;
    public event Action? StopRequested;
    public event Action<double>? SpeedChanged;
    public event Action<string>? StartLineColorChanged;
    public event Action<string>? EndLineColorChanged;
    public event Action<double>? StartLineThicknessChanged;
    public event Action<double>? EndLineThicknessChanged;

    public bool ReplayRangeEnabled => _replayRangeCheckBox.IsChecked == true;
    public bool IsCompactMode => _isCompact;
    public double SelectedSpeed => _selectedSpeed;

    public void SetMarkerTime(DateTime serverTime)
    {
        _datePicker.SelectedDate = serverTime.Date;
        _timeBox.Text = serverTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public void SetReplayLineChecked(bool enabled)
    {
        _synchronizingReplayLineCheckBox = true;
        try
        {
            _replayLineCheckBox.IsChecked = enabled;
            _compactReplayLineCheckBox.IsChecked = enabled;
        }
        finally
        {
            _synchronizingReplayLineCheckBox = false;
        }
    }

    public void SetReplayRangeChecked(bool enabled)
    {
        _synchronizingReplayRangeCheckBox = true;
        try
        {
            _replayRangeCheckBox.IsChecked = enabled;
            _compactReplayRangeCheckBox.IsChecked = enabled;
        }
        finally
        {
            _synchronizingReplayRangeCheckBox = false;
        }
    }

    public void SetState(bool loaded, bool playing, string status, string progress)
    {
        _ = loaded;
        string playText = playing ? "❚❚ Pause" : "▶ Play";
        _playButton.IsEnabled = true;
        _compactPlayButton.IsEnabled = true;
        _playButton.Content = playText;
        _compactPlayButton.Content = playText;
        _statusText.Text = status;
        _progressText.Text = progress;
    }

    public void SetPlaybackDirection(bool reverse)
    {
        _reverseButton.FontWeight = reverse ? FontWeights.Bold : FontWeights.Normal;
        _compactReverseButton.FontWeight = reverse ? FontWeights.Bold : FontWeights.Normal;
        _forwardButton.FontWeight = reverse ? FontWeights.Normal : FontWeights.Bold;
        _compactForwardButton.FontWeight = reverse ? FontWeights.Normal : FontWeights.Bold;
    }

    public void SetCompactMode(bool compact)
    {
        if (_isCompact == compact)
            return;

        if (compact)
        {
            _fullWidth = Math.Max(700, ActualWidth > 0 ? ActualWidth : Width);
            _fullHeight = Math.Max(390, ActualHeight > 0 ? ActualHeight : Height);
            _fullRoot.Visibility = Visibility.Collapsed;
            _compactRoot.Visibility = Visibility.Visible;
            MinWidth = 650;
            MinHeight = 92;
            Width = 700;
            Height = 105;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            _compactRoot.Visibility = Visibility.Collapsed;
            _fullRoot.Visibility = Visibility.Visible;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 700;
            MinHeight = 390;
            Width = Math.Max(700, _fullWidth);
            Height = Math.Max(390, _fullHeight);
        }

        _isCompact = compact;
    }

    public void SetReplayLineColors(string startColor, string endColor) =>
        SetReplayLineStyles(startColor, endColor, _startLineThickness, _endLineThickness);

    public void SetReplayLineStyles(string startColor, string endColor, double startThickness, double endThickness)
    {
        _startLineColor = NormalizeColor(startColor, "#FACC15");
        _endLineColor = NormalizeColor(endColor, "#EF4444");
        _startLineThickness = NormalizeThickness(startThickness);
        _endLineThickness = NormalizeThickness(endThickness);
        ApplyColorButton(_startColorButton, _startLineColor, "Start colour");
        ApplyColorButton(_endColorButton, _endLineColor, "End colour");
        _startThicknessButton.Content = $"Start {FormatPixels(_startLineThickness)}";
        _endThicknessButton.Content = $"End {FormatPixels(_endLineThickness)}";
    }

    private void ChooseReplayLineColor(bool isStart)
    {
        string current = isStart ? _startLineColor : _endLineColor;
        var picker = new DrawingColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() != true)
            return;

        string selected = NormalizeColor(picker.SelectedColor, current);
        if (isStart)
        {
            _startLineColor = selected;
            ApplyColorButton(_startColorButton, selected, "Start colour");
            StartLineColorChanged?.Invoke(selected);
        }
        else
        {
            _endLineColor = selected;
            ApplyColorButton(_endColorButton, selected, "End colour");
            EndLineColorChanged?.Invoke(selected);
        }
    }

    private Button CreateThicknessButton(string text, bool isStart)
    {
        var button = CreateButton(text, 92);
        var menu = new ContextMenu
        {
            Background = Brushes.White,
            Foreground = Brushes.Black,
            Placement = PlacementMode.Bottom
        };
        foreach (double thickness in new[] { 1.0, 2.0, 3.0, 4.0, 5.0 })
        {
            var item = new MenuItem
            {
                Header = FormatPixels(thickness),
                Background = Brushes.White,
                Foreground = Brushes.Black,
                Tag = thickness
            };
            item.Click += (_, _) => SetSelectorThickness(isStart, (double)item.Tag);
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            if (button.ContextMenu is null)
                return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private void SetSelectorThickness(bool isStart, double thickness)
    {
        thickness = NormalizeThickness(thickness);
        if (isStart)
        {
            _startLineThickness = thickness;
            _startThicknessButton.Content = $"Start {FormatPixels(thickness)}";
            StartLineThicknessChanged?.Invoke(thickness);
        }
        else
        {
            _endLineThickness = thickness;
            _endThicknessButton.Content = $"End {FormatPixels(thickness)}";
            EndLineThicknessChanged?.Invoke(thickness);
        }
    }

    private static double NormalizeThickness(double thickness) =>
        Math.Clamp(double.IsFinite(thickness) ? thickness : 1.0, 1.0, 5.0);

    private static string FormatPixels(double thickness) =>
        $"{thickness:0.#} px";

    private static void ApplyColorButton(Button button, string colorText, string caption)
    {
        Color color;
        try
        {
            object? parsed = ColorConverter.ConvertFromString(colorText);
            color = parsed is Color value ? value : Colors.Gray;
        }
        catch
        {
            color = Colors.Gray;
        }
        button.Content = caption;
        button.Background = new SolidColorBrush(color);
        button.Foreground = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 >= 145
            ? Brushes.Black
            : Brushes.White;
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        try
        {
            object? parsed = ColorConverter.ConvertFromString(value.Trim());
            if (parsed is Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
        }
        return fallback;
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void WireReplayCheckBoxes()
    {
        _replayLineCheckBox.Checked += (_, _) => HandleReplayLineUiChange(true);
        _replayLineCheckBox.Unchecked += (_, _) => HandleReplayLineUiChange(false);
        _compactReplayLineCheckBox.Checked += (_, _) => HandleReplayLineUiChange(true);
        _compactReplayLineCheckBox.Unchecked += (_, _) => HandleReplayLineUiChange(false);

        _replayRangeCheckBox.Checked += (_, _) => HandleReplayRangeUiChange(true);
        _replayRangeCheckBox.Unchecked += (_, _) => HandleReplayRangeUiChange(false);
        _compactReplayRangeCheckBox.Checked += (_, _) => HandleReplayRangeUiChange(true);
        _compactReplayRangeCheckBox.Unchecked += (_, _) => HandleReplayRangeUiChange(false);
    }

    private void HandleReplayLineUiChange(bool enabled)
    {
        if (_synchronizingReplayLineCheckBox)
            return;

        SetReplayLineChecked(enabled);
        ReplayLineChanged?.Invoke(enabled);
    }

    private void HandleReplayRangeUiChange(bool enabled)
    {
        if (_synchronizingReplayRangeCheckBox)
            return;

        SetReplayRangeChecked(enabled);
        ReplayRangeChanged?.Invoke(enabled);
    }

    private Button CreateSpeedButton()
    {
        var button = CreateButton("Speed 1×", 104);
        button.Background = Brushes.White;
        button.Foreground = Brushes.Black;
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        button.FontWeight = FontWeights.SemiBold;
        button.ToolTip = "Replay speed. The selected speed is always shown on the button.";

        var menu = new ContextMenu
        {
            Background = Brushes.White,
            Foreground = Brushes.Black,
            Placement = PlacementMode.Bottom
        };
        foreach ((string label, double speed) in new[]
                 {
                     ("0.25×", 0.25),
                     ("0.5×", 0.5),
                     ("1×", 1.0),
                     ("2×", 2.0),
                     ("5×", 5.0),
                     ("10×", 10.0),
                     ("50×", 50.0),
                     ("100×", 100.0),
                     ("250×", 250.0),
                     ("500×", 500.0),
                     ("750×", 750.0),
                     ("1000×", 1000.0),
                     ("1250×", 1250.0),
                     ("1500×", 1500.0),
                     ("2000×", 2000.0),
                     ("5000×", 5000.0),
                     ("10000×", 10000.0),
                     ("15000×", 15000.0),
                     ("20000×", 20000.0),
                     ("25000×", 25000.0),
                     ("30000×", 30000.0)
                 })
        {
            var item = new MenuItem
            {
                Header = label,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                Tag = speed
            };
            item.Click += (_, _) => SetSelectedSpeed((double)item.Tag, raiseEvent: true);
            menu.Items.Add(item);
        }

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            if (button.ContextMenu is null)
                return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private void SetSelectedSpeed(double speed, bool raiseEvent)
    {
        _selectedSpeed = speed;
        string text = $"Speed {FormatSpeed(speed)}";
        if (_speedButton is not null)
            _speedButton.Content = text;
        if (_compactSpeedButton is not null)
            _compactSpeedButton.Content = text;
        if (raiseEvent)
            SpeedChanged?.Invoke(speed);
    }

    private static string FormatSpeed(double speed) => speed switch
    {
        0.25 => "0.25×",
        0.5 => "0.5×",
        1 => "1×",
        2 => "2×",
        5 => "5×",
        10 => "10×",
        50 => "50×",
        100 => "100×",
        250 => "250×",
        500 => "500×",
        750 => "750×",
        1000 => "1000×",
        1250 => "1250×",
        1500 => "1500×",
        2000 => "2000×",
        5000 => "5000×",
        10000 => "10000×",
        15000 => "15000×",
        20000 => "20000×",
        25000 => "25000×",
        30000 => "30000×",
        _ => $"{speed:G}×"
    };

    private bool TryGetSelectedServerTime(out DateTime value)
    {
        value = default;
        DateTime? date = _datePicker.SelectedDate;
        if (!date.HasValue ||
            !TimeSpan.TryParseExact(
                _timeBox.Text.Trim(),
                new[] { "hh\\:mm", "hh\\:mm\\:ss" },
                CultureInfo.InvariantCulture,
                out TimeSpan time))
        {
            MessageBox.Show(
                this,
                "Enter a valid broker-server date and time, for example 14:30:00.",
                "Market Replay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        value = date.Value.Date + time;
        return true;
    }

    private static CheckBox CreateReplayCheckBox(string text, string tooltip) => new()
    {
        Content = text,
        IsChecked = false,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = tooltip
    };

    private static Button CreateButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        Height = 28,
        Margin = new Thickness(0, 0, 4, 0),
        Padding = new Thickness(8, 2, 8, 2)
    };
}
