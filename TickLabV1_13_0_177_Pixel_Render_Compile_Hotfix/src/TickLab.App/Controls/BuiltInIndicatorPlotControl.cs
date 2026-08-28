using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Indicators;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed class BuiltInIndicatorPlotControl : FrameworkElement
{
    private const double IndicatorScaleWidth = 48.0;
    private const double TopMargin = 6.0;
    private const double BottomMargin = 6.0;

    private BuiltInIndicatorResult? _result;
    private ChartViewportSnapshot? _viewport;
    private ChartSettings _chartSettings = ChartSettings.Default;
    private double? _sharedCrosshairRatio;
    private Point? _localMouse;

    private bool _verticalAuto = true;
    private double _manualMinimum;
    private double _manualMaximum;
    private double _lastMinimum;
    private double _lastMaximum;
    private Rect _lastPlot;

    private bool _dragging;
    private bool _draggingScale;
    private Point _dragStart;
    private double _dragStartMinimum;
    private double _dragStartMaximum;
    private int _lastHorizontalShift;

    public BuiltInIndicatorPlotControl()
    {
        Focusable = true;
        ClipToBounds = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Cross;
    }

    public BuiltInIndicatorResult? Result
    {
        get => _result;
        set { _result = value; InvalidateVisual(); }
    }

    public ChartViewportSnapshot? Viewport
    {
        get => _viewport;
        set { _viewport = value; InvalidateVisual(); }
    }

    public ChartSettings ChartSettings
    {
        get => _chartSettings;
        set { _chartSettings = value ?? ChartSettings.Default; InvalidateVisual(); }
    }

    public Func<string>? PlacementAddressProvider { get; set; }

    public event Action? RefreshRequested;
    public event Action? EditRequested;
    public event Action? MoveToWindowRequested;
    public event Action? MoveToChartRequested;
    public event Action? RemoveRequested;
    public event Action<double?>? CrosshairRatioChanged;
    public event Action<int, double>? HorizontalWheelRequested;
    public event Action<int>? HorizontalPanRequested;

    public void SetSharedCrosshairRatio(double? ratio)
    {
        _sharedCrosshairRatio = ratio.HasValue ? Math.Clamp(ratio.Value, 0, 1) : null;
        InvalidateVisual();
    }

    public bool AllowManualFixedRangeOverride { get; set; }

    public void ResetVerticalScale()
    {
        _verticalAuto = true;
        InvalidateVisual();
    }

    public void ApplyLinkedVerticalAction(ChartVerticalSyncAction action)
    {
        if (action.Kind == ChartVerticalSyncActionKind.Reset)
        {
            ResetVerticalScale();
            return;
        }

        if (!EnsureLinkedManualRange())
            return;

        double span = Math.Max(1e-15, _manualMaximum - _manualMinimum);
        if (action.Kind == ChartVerticalSyncActionKind.Zoom)
        {
            double factor = Math.Clamp(action.Amount, 0.01, 100.0);
            double anchorRatio = Math.Clamp(action.AnchorRatio, 0.0, 1.0);
            double anchorValue = _manualMaximum - anchorRatio * span;
            double newSpan = Math.Clamp(span * factor, span / 1_000_000.0, span * 1_000_000.0);
            _manualMaximum = anchorValue + anchorRatio * newSpan;
            _manualMinimum = _manualMaximum - newSpan;
        }
        else if (action.Kind == ChartVerticalSyncActionKind.Pan)
        {
            double shift = span * action.Amount;
            _manualMinimum += shift;
            _manualMaximum += shift;
        }
        InvalidateVisual();
    }

    private bool EnsureLinkedManualRange()
    {
        bool useDisplayedRange = _verticalAuto ||
            !double.IsFinite(_manualMinimum) ||
            !double.IsFinite(_manualMaximum) ||
            _manualMaximum <= _manualMinimum;
        double minimum = useDisplayedRange ? _lastMinimum : _manualMinimum;
        double maximum = useDisplayedRange ? _lastMaximum : _manualMaximum;
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
            return false;
        _verticalAuto = false;
        _manualMinimum = minimum;
        _manualMaximum = maximum;
        return true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Brush background = BrushFrom(_chartSettings.ChartBackgroundColor, Color.FromRgb(8, 8, 8));
        dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        BuiltInIndicatorResult? result = _result;
        ChartViewportSnapshot? viewport = _viewport;
        if (result is null || viewport is null || result.Series.Count == 0)
            return;

        Rect plot = CreatePlot(viewport);
        if (plot.Width <= 1 || plot.Height <= 1)
            return;

        if (!TryGetVisibleRange(result, viewport, out double autoMinimum, out double autoMaximum))
            return;

        if (_verticalAuto || !double.IsFinite(_manualMinimum) || !double.IsFinite(_manualMaximum) || _manualMaximum <= _manualMinimum)
        {
            _manualMinimum = autoMinimum;
            _manualMaximum = autoMaximum;
        }

        bool useFixedRange = !AllowManualFixedRangeOverride || _verticalAuto;
        double minimum = useFixedRange ? result.FixedMinimum ?? _manualMinimum : _manualMinimum;
        double maximum = useFixedRange ? result.FixedMaximum ?? _manualMaximum : _manualMaximum;
        if (maximum <= minimum)
            maximum = minimum + 1;

        _lastMinimum = minimum;
        _lastMaximum = maximum;
        _lastPlot = plot;

        DrawScaleBackgrounds(dc, plot);
        DrawGridAndScale(dc, plot, minimum, maximum);
        foreach (IndicatorLevelSetting level in result.Levels)
            DrawLevel(dc, level, plot, minimum, maximum);

        dc.PushClip(new RectangleGeometry(plot));
        foreach (IndicatorSeriesResult series in result.Series.Where(item => item.Style.Visible))
            DrawSeries(dc, result, series, plot, minimum, maximum, viewport);
        DrawCrosshair(dc, plot, minimum, maximum);
        dc.Pop();
    }

    private Rect CreatePlot(ChartViewportSnapshot viewport)
    {
        double left = Math.Clamp(viewport.PlotLeft, 0, Math.Max(0, ActualWidth - 1));
        double maximumRight = Math.Max(left + 1, ActualWidth - IndicatorScaleWidth);
        double right = Math.Min(left + Math.Max(1, viewport.PlotWidth), maximumRight);
        return new Rect(left, TopMargin, Math.Max(1, right - left), Math.Max(1, ActualHeight - TopMargin - BottomMargin));
    }

    private bool TryGetVisibleRange(BuiltInIndicatorResult result, ChartViewportSnapshot viewport, out double minimum, out double maximum)
    {
        var finite = new List<double>();
        int first = Math.Max(0, viewport.FirstIndex);
        int last = Math.Max(first, viewport.LastExclusive);
        foreach (IndicatorSeriesResult series in result.Series.Where(item => item.Style.Visible))
        {
            int seriesFirst = Math.Max(0, first - series.Shift);
            int seriesLast = Math.Min(series.Values.Count, last - series.Shift);
            for (int index = seriesFirst; index < seriesLast; index++)
            {
                double? value = series.Values[index];
                if (value.HasValue && double.IsFinite(value.Value))
                    finite.Add(value.Value);
            }
        }
        finite.AddRange(result.Levels.Select(item => item.Value).Where(double.IsFinite));
        if (finite.Count == 0)
        {
            minimum = maximum = 0;
            return false;
        }

        minimum = result.FixedMinimum ?? finite.Min();
        maximum = result.FixedMaximum ?? finite.Max();
        if (maximum - minimum < 1e-12)
        {
            maximum += 1;
            minimum -= 1;
        }
        double padding = (maximum - minimum) * 0.06;
        if (!result.FixedMinimum.HasValue) minimum -= padding;
        if (!result.FixedMaximum.HasValue) maximum += padding;
        return true;
    }

    private void DrawScaleBackgrounds(DrawingContext dc, Rect plot)
    {
        dc.DrawRectangle(
            BrushFrom(_chartSettings.PriceScaleBackgroundColor, Color.FromRgb(8, 8, 8)),
            null,
            new Rect(plot.Right, 0, Math.Max(0, ActualWidth - plot.Right), ActualHeight));
    }

    private void DrawGridAndScale(DrawingContext dc, Rect plot, double minimum, double maximum)
    {
        Pen grid = MakePen(_chartSettings.GridColor, Math.Clamp(_chartSettings.GridThickness, 0.5, 3), ChartLineStyle.Solid, _chartSettings.GridOpacity);
        Color scaleText = ParseColor(_chartSettings.PriceScaleTextColor, Color.FromRgb(176, 176, 176));
        for (int index = 0; index <= 4; index++)
        {
            double y = plot.Top + plot.Height * index / 4.0;
            if (_chartSettings.ShowCandleGrid)
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            double value = maximum - (maximum - minimum) * index / 4.0;
            DrawText(dc, value.ToString("0.########", CultureInfo.InvariantCulture), plot.Right + 6, y - 8, 10, scaleText);
        }
        dc.DrawLine(MakePen(_chartSettings.GridColor, 1, ChartLineStyle.Solid, 0.9), new Point(plot.Right, plot.Top), new Point(plot.Right, plot.Bottom));
    }

    private void DrawCrosshair(DrawingContext dc, Rect plot, double minimum, double maximum)
    {
        Point? mouse = _localMouse.HasValue && plot.Contains(_localMouse.Value) ? _localMouse : null;
        double? ratio = mouse.HasValue
            ? Math.Clamp((mouse.Value.X - plot.Left) / Math.Max(1, plot.Width), 0, 1)
            : _sharedCrosshairRatio;
        if (!ratio.HasValue)
            return;

        Pen crosshair = MakePen(_chartSettings.CrosshairColor, Math.Clamp(_chartSettings.CrosshairThickness, 0.5, 4), _chartSettings.CrosshairLineStyle);
        double x = plot.Left + plot.Width * ratio.Value;
        dc.DrawLine(crosshair, new Point(x, plot.Top), new Point(x, plot.Bottom));
        if (mouse.HasValue)
        {
            dc.DrawLine(crosshair, new Point(plot.Left, mouse.Value.Y), new Point(plot.Right, mouse.Value.Y));
            double value = maximum - (mouse.Value.Y - plot.Top) / Math.Max(1, plot.Height) * (maximum - minimum);
            Brush label = BrushFrom(_chartSettings.CrosshairLabelBackgroundColor, Color.FromRgb(48, 48, 48));
            FormattedText text = CreateText(value.ToString("0.########", CultureInfo.InvariantCulture), 10, BrushFrom(_chartSettings.CrosshairLabelTextColor, Colors.White));
            Rect labelRect = new(plot.Right, Math.Clamp(mouse.Value.Y - text.Height / 2 - 3, plot.Top, Math.Max(plot.Top, plot.Bottom - text.Height - 6)), Math.Max(1, ActualWidth - plot.Right), text.Height + 6);
            dc.DrawRectangle(label, null, labelRect);
            dc.DrawText(text, new Point(labelRect.Left + 5, labelRect.Top + 3));
        }
    }

    private void DrawLevel(DrawingContext dc, IndicatorLevelSetting level, Rect plot, double minimum, double maximum)
    {
        if (level.Value < minimum || level.Value > maximum) return;
        double y = ToY(level.Value, plot, minimum, maximum);
        Pen pen = MakePen(level.Color, level.Width, level.LineStyle);
        dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        if (!string.IsNullOrWhiteSpace(level.Label))
            DrawText(dc, level.Label, plot.Left + 4, y - 14, 9, ParseColor(level.Color, Colors.Gray));
    }

    private void DrawSeries(DrawingContext dc, BuiltInIndicatorResult result, IndicatorSeriesResult series, Rect plot, double minimum, double maximum, ChartViewportSnapshot viewport)
    {
        IndicatorStyleSetting style = series.Style;
        if (style.DrawMode == IndicatorSeriesDrawMode.Histogram)
        {
            DrawHistogram(dc, series, plot, minimum, maximum, viewport);
            return;
        }
        if (style.DrawMode is IndicatorSeriesDrawMode.Dots or IndicatorSeriesDrawMode.ArrowUp or IndicatorSeriesDrawMode.ArrowDown)
        {
            DrawSymbols(dc, series, plot, minimum, maximum, viewport);
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            bool started = false;
            int sourceStart = Math.Max(0, viewport.FirstIndex - series.Shift - 2);
            int sourceEnd = Math.Min(series.Values.Count, viewport.LastExclusive - series.Shift + 2);
            for (int sourceIndex = sourceStart; sourceIndex < sourceEnd; sourceIndex++)
            {
                int targetIndex = sourceIndex + series.Shift;
                int visibleIndex = targetIndex - viewport.FirstIndex;
                if (visibleIndex < 0 || visibleIndex >= viewport.VisibleSlots.Count)
                {
                    started = false;
                    continue;
                }
                double? value = series.Values[sourceIndex];
                if (!value.HasValue || !double.IsFinite(value.Value)) { started = false; continue; }
                double x = SlotCenter(viewport, plot, viewport.VisibleSlots[visibleIndex]);
                double y = ToY(value.Value, plot, minimum, maximum);
                if (!started) { context.BeginFigure(new Point(x, y), false, false); started = true; }
                else context.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, MakePen(style.Color, style.Width, style.LineStyle), geometry);

        if (!string.IsNullOrWhiteSpace(series.FillToSeriesKey))
        {
            IndicatorSeriesResult? partner = result.Series.FirstOrDefault(item => string.Equals(item.Key, series.FillToSeriesKey, StringComparison.OrdinalIgnoreCase));
            if (partner is not null)
                DrawCloud(dc, series, partner, plot, minimum, maximum, viewport);
        }
    }

    private void DrawCloud(DrawingContext dc, IndicatorSeriesResult top, IndicatorSeriesResult bottom, Rect plot, double minimum, double maximum, ChartViewportSnapshot viewport)
    {
        var upper = new List<Point>();
        var lower = new List<Point>();
        int sourceStart = Math.Max(0, viewport.FirstIndex - Math.Max(top.Shift, bottom.Shift));
        int sourceEnd = Math.Min(Math.Min(top.Values.Count, bottom.Values.Count), viewport.LastExclusive - Math.Min(top.Shift, bottom.Shift));
        for (int index = sourceStart; index < sourceEnd; index++)
        {
            int target = index + top.Shift;
            int visible = target - viewport.FirstIndex;
            if (visible < 0 || visible >= viewport.VisibleSlots.Count) continue;
            double? a = top.Values[index], b = bottom.Values[index];
            if (!a.HasValue || !b.HasValue || !double.IsFinite(a.Value) || !double.IsFinite(b.Value)) continue;
            double x = SlotCenter(viewport, plot, viewport.VisibleSlots[visible]);
            upper.Add(new Point(x, ToY(a.Value, plot, minimum, maximum)));
            lower.Add(new Point(x, ToY(b.Value, plot, minimum, maximum)));
        }
        if (upper.Count < 2 || lower.Count != upper.Count) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(upper[0], true, true);
            for (int i = 1; i < upper.Count; i++) context.LineTo(upper[i], true, false);
            for (int i = lower.Count - 1; i >= 0; i--) context.LineTo(lower[i], true, false);
        }
        geometry.Freeze();
        Color color = ParseColor(top.Style.FillColor, ParseColor(top.Style.Color, Color.FromRgb(83, 217, 138)));
        color.A = (byte)Math.Round(Math.Clamp(top.Style.FillOpacity, 0, 1) * 255);
        if (color.A > 0)
            dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private void DrawHistogram(DrawingContext dc, IndicatorSeriesResult series, Rect plot, double minimum, double maximum, ChartViewportSnapshot viewport)
    {
        double zeroY = ToY(Math.Clamp(0, minimum, maximum), plot, minimum, maximum);
        int sourceStart = Math.Max(0, viewport.FirstIndex - series.Shift);
        int sourceEnd = Math.Min(series.Values.Count, viewport.LastExclusive - series.Shift);
        for (int sourceIndex = sourceStart; sourceIndex < sourceEnd; sourceIndex++)
        {
            int target = sourceIndex + series.Shift;
            int visible = target - viewport.FirstIndex;
            if (visible < 0 || visible >= viewport.VisibleSlots.Count) continue;
            double? value = series.Values[sourceIndex];
            if (!value.HasValue || !double.IsFinite(value.Value)) continue;
            bool negative = series.Style.ColorBySign && value.Value < 0;
            if (series.Style.ColorBySlope && sourceIndex > 0 && series.Values[sourceIndex - 1].HasValue)
                negative = value.Value < series.Values[sourceIndex - 1]!.Value;
            Brush brush = BrushFrom(negative ? series.Style.NegativeColor : series.Style.Color, negative ? Color.FromRgb(240, 123, 133) : Color.FromRgb(83, 207, 164));
            double x = SlotCenter(viewport, plot, viewport.VisibleSlots[visible]);
            double y = ToY(value.Value, plot, minimum, maximum);
            double barWidth = Math.Max(1, SlotWidth(viewport, plot, viewport.VisibleSlots[visible]) * 0.72);
            dc.DrawRectangle(brush, null, new Rect(x - barWidth / 2, Math.Min(y, zeroY), barWidth, Math.Max(1, Math.Abs(y - zeroY))));
        }
    }

    private void DrawSymbols(DrawingContext dc, IndicatorSeriesResult series, Rect plot, double minimum, double maximum, ChartViewportSnapshot viewport)
    {
        Brush brush = BrushFrom(series.Style.Color, Colors.White);
        int start = Math.Max(0, viewport.FirstIndex - series.Shift);
        int end = Math.Min(series.Values.Count, viewport.LastExclusive - series.Shift);
        for (int sourceIndex = start; sourceIndex < end; sourceIndex++)
        {
            int target = sourceIndex + series.Shift;
            int visible = target - viewport.FirstIndex;
            if (visible < 0 || visible >= viewport.VisibleSlots.Count) continue;
            double? value = series.Values[sourceIndex];
            if (!value.HasValue || !double.IsFinite(value.Value)) continue;
            double x = SlotCenter(viewport, plot, viewport.VisibleSlots[visible]);
            double y = ToY(value.Value, plot, minimum, maximum);
            if (series.Style.DrawMode == IndicatorSeriesDrawMode.Dots)
                dc.DrawEllipse(brush, null, new Point(x, y), Math.Max(1.5, series.Style.Width), Math.Max(1.5, series.Style.Width));
            else
            {
                double direction = series.Style.DrawMode == IndicatorSeriesDrawMode.ArrowUp ? -1 : 1;
                var geometry = new StreamGeometry();
                using StreamGeometryContext context = geometry.Open();
                context.BeginFigure(new Point(x, y), true, true);
                context.LineTo(new Point(x - 4, y + direction * 7), true, false);
                context.LineTo(new Point(x + 4, y + direction * 7), true, false);
                geometry.Freeze();
                dc.DrawGeometry(brush, null, geometry);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point mouse = e.GetPosition(this);
        if (e.ClickCount >= 2)
        {
            ResetVerticalScale();
            e.Handled = true;
            return;
        }
        if (_lastPlot.Width <= 1 || _lastPlot.Height <= 1)
            return;

        _verticalAuto = false;
        _dragging = true;
        _draggingScale = mouse.X >= _lastPlot.Right;
        _dragStart = mouse;
        _dragStartMinimum = _lastMinimum;
        _dragStartMaximum = _lastMaximum;
        _lastHorizontalShift = 0;
        CaptureMouse();
        Cursor = _draggingScale ? Cursors.SizeNS : Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;
        _dragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Cross;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _localMouse = e.GetPosition(this);
        if (_dragging)
        {
            double dy = _localMouse.Value.Y - _dragStart.Y;
            double span = Math.Max(1e-15, _dragStartMaximum - _dragStartMinimum);
            if (_draggingScale)
            {
                double factor = Math.Exp(dy / 170.0);
                double newSpan = Math.Clamp(span * factor, span / 1_000_000.0, span * 1_000_000.0);
                double center = (_dragStartMinimum + _dragStartMaximum) / 2.0;
                _manualMinimum = center - newSpan / 2.0;
                _manualMaximum = center + newSpan / 2.0;
            }
            else
            {
                double shift = dy / Math.Max(1, _lastPlot.Height) * span;
                _manualMinimum = _dragStartMinimum + shift;
                _manualMaximum = _dragStartMaximum + shift;

                if (_viewport is ChartViewportSnapshot viewport)
                {
                    double dx = _localMouse.Value.X - _dragStart.X;
                    int totalShift = (int)Math.Round(dx / Math.Max(1, _lastPlot.Width) * viewport.VisibleCount);
                    int incremental = totalShift - _lastHorizontalShift;
                    if (incremental != 0)
                    {
                        _lastHorizontalShift = totalShift;
                        HorizontalPanRequested?.Invoke(incremental);
                    }
                }
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        ChartViewportSnapshot? currentViewport = _viewport;
        double left = currentViewport?.PlotLeft ?? 0;
        double width = currentViewport?.PlotWidth ?? ActualWidth;
        CrosshairRatioChanged?.Invoke(Math.Clamp((_localMouse.Value.X - left) / Math.Max(1, width), 0, 1));
        Cursor = _lastPlot.Width > 1 && _localMouse.Value.X >= _lastPlot.Right ? Cursors.SizeNS : Cursors.Cross;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_lastPlot.Width <= 1)
            return;
        Point mouse = e.GetPosition(this);
        double ratio = Math.Clamp((mouse.X - _lastPlot.Left) / Math.Max(1, _lastPlot.Width), 0, 1);
        HorizontalWheelRequested?.Invoke(e.Delta, ratio);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging)
            return;
        _localMouse = null;
        CrosshairRatioChanged?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var menu = new ContextMenu { PlacementTarget = this };
        string address = PlacementAddressProvider?.Invoke() ?? "Current chart";
        menu.Items.Add(new MenuItem
        {
            Header = $"{Result?.Name ?? "Built-in indicator"} — {address}",
            IsEnabled = false
        });
        menu.Items.Add(new Separator());

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        var properties = new MenuItem { Header = "Properties…" };
        properties.Click += (_, _) => EditRequested?.Invoke();
        var moveWindow = new MenuItem { Header = "Move to Window…" };
        moveWindow.Click += (_, _) => MoveToWindowRequested?.Invoke();
        var moveChart = new MenuItem { Header = "Move to Chart…" };
        moveChart.Click += (_, _) => MoveToChartRequested?.Invoke();
        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) => RemoveRequested?.Invoke();

        menu.Items.Add(refresh);
        menu.Items.Add(properties);
        menu.Items.Add(moveWindow);
        menu.Items.Add(moveChart);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private double SlotCenter(ChartViewportSnapshot viewport, Rect plot, int slotIndex)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scale = Math.Max(0.01, dpi.DpiScaleX);
        int leftPixels = (int)Math.Round(plot.Left * scale, MidpointRounding.AwayFromZero);
        int rightPixels = (int)Math.Round(plot.Right * scale, MidpointRounding.AwayFromZero);
        int slots = Math.Max(1, viewport.SlotCount);
        double rawWidth = Math.Max(1, rightPixels - leftPixels) / (double)slots;
        int slot = Math.Clamp(slotIndex, 0, slots - 1);
        int left = leftPixels + (int)Math.Round(slot * rawWidth, MidpointRounding.AwayFromZero);
        int right = leftPixels + (int)Math.Round((slot + 1) * rawWidth, MidpointRounding.AwayFromZero);
        if (right <= left) right = left + 1;
        return (left + right) / (2.0 * scale);
    }

    private double SlotWidth(ChartViewportSnapshot viewport, Rect plot, int slotIndex)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scale = Math.Max(0.01, dpi.DpiScaleX);
        int leftPixels = (int)Math.Round(plot.Left * scale, MidpointRounding.AwayFromZero);
        int rightPixels = (int)Math.Round(plot.Right * scale, MidpointRounding.AwayFromZero);
        int slots = Math.Max(1, viewport.SlotCount);
        double rawWidth = Math.Max(1, rightPixels - leftPixels) / (double)slots;
        int slot = Math.Clamp(slotIndex, 0, slots - 1);
        int left = leftPixels + (int)Math.Round(slot * rawWidth, MidpointRounding.AwayFromZero);
        int right = leftPixels + (int)Math.Round((slot + 1) * rawWidth, MidpointRounding.AwayFromZero);
        return Math.Max(1, right - left) / scale;
    }

    private static double ToY(double value, Rect plot, double minimum, double maximum) =>
        plot.Bottom - (value - minimum) / Math.Max(1e-15, maximum - minimum) * plot.Height;

    private static Pen MakePen(string color, double width, ChartLineStyle style, double opacity = 1.0)
    {
        Color parsed = ParseColor(color, Colors.White);
        parsed.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var pen = new Pen(new SolidColorBrush(parsed), Math.Clamp(width, 0.5, 8));
        pen.DashStyle = style switch { ChartLineStyle.Dashed => DashStyles.Dash, ChartLineStyle.Dotted => DashStyles.Dot, _ => DashStyles.Solid };
        pen.StartLineCap = PenLineCap.Flat;
        pen.EndLineCap = PenLineCap.Flat;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static SolidColorBrush BrushFrom(string value, Color fallback)
    {
        var brush = new SolidColorBrush(ParseColor(value, fallback));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            object converted = ColorConverter.ConvertFromString(value);
            return converted is Color color ? color : fallback;
        }
        catch { return fallback; }
    }

    private static FormattedText CreateText(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        dc.DrawText(CreateText(text, size, new SolidColorBrush(color)), new Point(x, y));
    }
}
