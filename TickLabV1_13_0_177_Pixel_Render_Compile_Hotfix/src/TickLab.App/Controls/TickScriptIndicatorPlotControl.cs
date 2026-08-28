using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Indicators;
using TickLab.Core.Scripting;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed class TickScriptIndicatorPlotControl : FrameworkElement
{
    private const double IndicatorScaleWidth = 48.0;
    private TickScriptIndicatorResult? _result;
    private ChartViewportSnapshot? _viewport;
    private ChartSettings _chartSettings = ChartSettings.Default;
    private TickScriptIndicatorAppearance _appearance = TickScriptIndicatorAppearance.Default;
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

    public TickScriptIndicatorPlotControl()
    {
        Focusable = true;
        ClipToBounds = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    public TickScriptIndicatorResult? Result
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

    public TickScriptIndicatorAppearance Appearance
    {
        get => _appearance;
        set { _appearance = value ?? TickScriptIndicatorAppearance.Default; InvalidateVisual(); }
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
        _sharedCrosshairRatio = ratio.HasValue ? Math.Clamp(ratio.Value, 0.0, 1.0) : null;
        InvalidateVisual();
    }

    public void ResetVerticalScale() { _verticalAuto = true; InvalidateVisual(); }

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
        dc.DrawRectangle(Brush(_chartSettings.ChartBackgroundColor, Color.FromRgb(8, 8, 8)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        TickScriptIndicatorResult? result = _result;
        ChartViewportSnapshot? viewport = _viewport;
        if (result is null || result.Values.Count == 0 || viewport is null || !_appearance.Visible)
            return;

        double left = Math.Clamp(viewport.PlotLeft, 0, Math.Max(0, ActualWidth - 1));
        double right = Math.Min(left + Math.Max(1, viewport.PlotWidth), Math.Max(left + 1, ActualWidth - IndicatorScaleWidth));
        Rect plot = new(left, 6, Math.Max(1, right - left), Math.Max(1, ActualHeight - 12));
        int first = Math.Clamp(viewport.FirstIndex, 0, result.Values.Count);
        int last = Math.Clamp(viewport.LastExclusive, first, result.Values.Count);
        if (last <= first) return;

        var finite = result.Values.Skip(first).Take(last - first).Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).ToList();
        if (result.HorizontalUpper.HasValue) finite.Add(result.HorizontalUpper.Value);
        if (result.HorizontalLower.HasValue) finite.Add(result.HorizontalLower.Value);
        if (finite.Count == 0) return;
        double autoMinimum = finite.Min(), autoMaximum = finite.Max();
        if (autoMaximum - autoMinimum < 1e-12) { autoMaximum += 1; autoMinimum -= 1; }
        double padding = (autoMaximum - autoMinimum) * 0.06;
        autoMinimum -= padding; autoMaximum += padding;
        if (_verticalAuto || !double.IsFinite(_manualMinimum) || !double.IsFinite(_manualMaximum) || _manualMaximum <= _manualMinimum)
        {
            _manualMinimum = autoMinimum;
            _manualMaximum = autoMaximum;
        }
        double minimum = _manualMinimum, maximum = _manualMaximum;
        _lastMinimum = minimum; _lastMaximum = maximum; _lastPlot = plot;

        dc.DrawRectangle(Brush(_chartSettings.PriceScaleBackgroundColor, Color.FromRgb(8, 8, 8)), null, new Rect(plot.Right, 0, Math.Max(0, ActualWidth - plot.Right), ActualHeight));
        Pen grid = PenFrom(_chartSettings.GridColor, Math.Clamp(_chartSettings.GridThickness, 0.5, 3), _chartSettings.GridOpacity);
        Color scaleText = ColorFrom(_appearance.LabelColor, ColorFrom(_chartSettings.PriceScaleTextColor, Color.FromRgb(176, 176, 176)));
        for (int index = 0; index <= 4; index++)
        {
            double y = plot.Top + plot.Height * index / 4.0;
            if (_chartSettings.ShowCandleGrid) dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            double value = maximum - (maximum - minimum) * index / 4.0;
            DrawText(dc, value.ToString("0.########", CultureInfo.InvariantCulture), plot.Right + 6, y - 8, 10, scaleText);
        }
        dc.DrawLine(PenFrom(_chartSettings.GridColor, 1, 1), new Point(plot.Right, plot.Top), new Point(plot.Right, plot.Bottom));

        DrawLevel(dc, result.HorizontalUpper, _appearance.UpperLevelColor, minimum, maximum, plot);
        DrawLevel(dc, result.HorizontalLower, _appearance.LowerLevelColor, minimum, maximum, plot);

        dc.PushClip(new RectangleGeometry(plot));
        var segments = new List<List<Point>>();
        var current = new List<Point>();
        for (int index = first; index < last; index++)
        {
            double? value = result.Values[index];
            int visibleIndex = index - first;
            if (!value.HasValue || visibleIndex < 0 || visibleIndex >= viewport.VisibleSlots.Count)
            {
                if (current.Count > 0) { segments.Add(current); current = new List<Point>(); }
                continue;
            }
            double x = SlotCenter(viewport, plot, viewport.VisibleSlots[visibleIndex]);
            current.Add(new Point(x, ToY(value.Value, plot, minimum, maximum)));
        }
        if (current.Count > 0) segments.Add(current);

        foreach (List<Point> segment in segments)
        {
            if (_appearance.FillOpacity > 0 && segment.Count >= 2)
            {
                var fillGeometry = new StreamGeometry();
                using (StreamGeometryContext fillContext = fillGeometry.Open())
                {
                    fillContext.BeginFigure(new Point(segment[0].X, plot.Bottom), true, true);
                    fillContext.LineTo(segment[0], true, false);
                    foreach (Point point in segment.Skip(1))
                        fillContext.LineTo(point, true, false);
                    fillContext.LineTo(new Point(segment[^1].X, plot.Bottom), true, false);
                }
                fillGeometry.Freeze();
                Color fillColor = ColorFrom(_appearance.FillColor, ColorFrom(_appearance.LineColor, Colors.SteelBlue));
                fillColor.A = (byte)Math.Round(Math.Clamp(_appearance.FillOpacity, 0, 1) * 255);
                dc.DrawGeometry(new SolidColorBrush(fillColor), null, fillGeometry);
            }

            if (segment.Count == 1)
            {
                dc.DrawEllipse(Brush(_appearance.LineColor, Colors.SteelBlue), null, segment[0], Math.Max(1, _appearance.LineWidth), Math.Max(1, _appearance.LineWidth));
                continue;
            }
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(segment[0], false, false);
                foreach (Point point in segment.Skip(1))
                    context.LineTo(point, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, PenFrom(_appearance.LineColor, _appearance.LineWidth, 1, _appearance.LineStyle), geometry);
        }

        Point? mouse = _localMouse.HasValue && plot.Contains(_localMouse.Value) ? _localMouse : null;
        double? ratio = mouse.HasValue ? Math.Clamp((mouse.Value.X - plot.Left) / Math.Max(1, plot.Width), 0, 1) : _sharedCrosshairRatio;
        if (ratio.HasValue)
        {
            Pen cross = new(Brush(_chartSettings.CrosshairColor, Colors.Gray), Math.Clamp(_chartSettings.CrosshairThickness, 0.5, 4)) { DashStyle = DashStyles.Dash };
            double x = plot.Left + plot.Width * ratio.Value;
            dc.DrawLine(cross, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (mouse.HasValue) dc.DrawLine(cross, new Point(plot.Left, mouse.Value.Y), new Point(plot.Right, mouse.Value.Y));
        }
        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point mouse = e.GetPosition(this);
        if (e.ClickCount >= 2) { ResetVerticalScale(); e.Handled = true; return; }
        if (_lastPlot.Width <= 1) return;
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
        if (!_dragging) return;
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
            double span = Math.Max(1e-15, _dragStartMaximum - _dragStartMinimum);
            double dy = _localMouse.Value.Y - _dragStart.Y;
            if (_draggingScale)
            {
                double newSpan = span * Math.Exp(dy / 170.0);
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
                    int total = (int)Math.Round(dx / Math.Max(1, _lastPlot.Width) * viewport.VisibleCount);
                    int incremental = total - _lastHorizontalShift;
                    if (incremental != 0) { _lastHorizontalShift = total; HorizontalPanRequested?.Invoke(incremental); }
                }
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        double left = _viewport?.PlotLeft ?? 0;
        double width = _viewport?.PlotWidth ?? ActualWidth;
        CrosshairRatioChanged?.Invoke(Math.Clamp((_localMouse.Value.X - left) / Math.Max(1, width), 0, 1));
        Cursor = _lastPlot.Width > 1 && _localMouse.Value.X >= _lastPlot.Right ? Cursors.SizeNS : Cursors.Cross;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_lastPlot.Width <= 1) return;
        Point mouse = e.GetPosition(this);
        double ratio = Math.Clamp((mouse.X - _lastPlot.Left) / Math.Max(1, _lastPlot.Width), 0, 1);
        HorizontalWheelRequested?.Invoke(e.Delta, ratio);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging) return;
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
            Header = $"{Result?.Name ?? "TickScript indicator"} — {address}",
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
        double raw = Math.Max(1, rightPixels - leftPixels) / (double)slots;
        int slot = Math.Clamp(slotIndex, 0, slots - 1);
        int left = leftPixels + (int)Math.Round(slot * raw, MidpointRounding.AwayFromZero);
        int right = leftPixels + (int)Math.Round((slot + 1) * raw, MidpointRounding.AwayFromZero);
        if (right <= left) right = left + 1;
        return (left + right) / (2.0 * scale);
    }

    private void DrawLevel(DrawingContext dc, double? value, string color, double minimum, double maximum, Rect plot)
    {
        if (!value.HasValue) return;
        double y = ToY(value.Value, plot, minimum, maximum);
        Pen pen = PenFrom(color, _appearance.LevelWidth, 1, _appearance.LevelLineStyle);
        dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
    }

    private static double ToY(double value, Rect plot, double minimum, double maximum) => plot.Bottom - (value - minimum) / Math.Max(1e-15, maximum - minimum) * plot.Height;
    private static Pen PenFrom(string color, double width, double opacity, ChartLineStyle style = ChartLineStyle.Solid)
    {
        Color c = ColorFrom(color, Colors.White);
        c.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var p = new Pen(new SolidColorBrush(c), Math.Clamp(width, 0.5, 8));
        p.DashStyle = style switch
        {
            ChartLineStyle.Dashed => DashStyles.Dash,
            ChartLineStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };
        if (p.CanFreeze) p.Freeze();
        return p;
    }
    private static SolidColorBrush Brush(string color, Color fallback) { var b = new SolidColorBrush(ColorFrom(color, fallback)); if (b.CanFreeze) b.Freeze(); return b; }
    private static Color ColorFrom(string value, Color fallback) { try { object o = ColorConverter.ConvertFromString(value); return o is Color c ? c : fallback; } catch { return fallback; } }
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color) { var f = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1.0); dc.DrawText(f, new Point(x, y)); }
}
