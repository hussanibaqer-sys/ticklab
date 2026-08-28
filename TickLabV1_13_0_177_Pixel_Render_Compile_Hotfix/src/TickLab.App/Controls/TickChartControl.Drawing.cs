using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TickLab.Desktop.Controls;

// Raw Tick Chart owns only its Tick-specific Find marker. All user drawing,
// Measure, Crosshair, selection, quick-edit, inspector and undo/redo behavior
// is intentionally provided by the existing CandleChartControl drawing engine
// running as a transparent Tick drawing surface above this renderer.
public sealed partial class TickChartControl
{
    private long? _findMarkerMilliseconds;
    private bool _findMarkerSelected;

    public bool FindMarkerSelected => _findMarkerSelected;

    public void SetFindMarker(long? timeMilliseconds)
    {
        _findMarkerMilliseconds = timeMilliseconds;
        _findMarkerSelected = false;
        InvalidateVisual();
    }

    public void ClearFindMarker()
    {
        _findMarkerMilliseconds = null;
        _findMarkerSelected = false;
        InvalidateVisual();
    }

    private void DrawFindTickMarker(DrawingContext dc, TickLayout layout)
    {
        if (!_findMarkerMilliseconds.HasValue)
            return;

        int index = FindTickIndexByTimestamp(_findMarkerMilliseconds.Value);
        if (index < layout.FirstIndex || index >= layout.LastExclusive)
            return;

        double x = IndexToX(Ticks[index], index, layout);

        // Match the normal candle-chart Find Candle historical anchor exactly:
        // solid warm-yellow line, 2 px normally and 4 px while selected.
        // Tick mode must adapt only the X coordinate, not invent a separate
        // marker language or label treatment.
        var brush = new SolidColorBrush(Color.FromArgb(210, 250, 204, 21));
        if (brush.CanFreeze)
            brush.Freeze();
        var pen = new Pen(brush, _findMarkerSelected ? 4.0 : 2.0)
        {
            DashStyle = DashStyles.Solid,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        if (pen.CanFreeze)
            pen.Freeze();

        double lineX = Math.Round(x * 2.0) / 2.0;
        dc.DrawLine(pen, new Point(lineX, layout.Plot.Top), new Point(lineX, layout.Plot.Bottom));
    }

    private bool HitTestFindMarker(Point mouse, TickLayout layout)
    {
        if (!_findMarkerMilliseconds.HasValue)
            return false;
        int markerIndex = FindTickIndexByTimestamp(_findMarkerMilliseconds.Value);
        if (markerIndex < layout.FirstIndex || markerIndex >= layout.LastExclusive)
            return false;
        double x = IndexToX(Ticks[markerIndex], markerIndex, layout);
        return Math.Abs(mouse.X - x) <= 7 &&
               mouse.Y >= layout.Plot.Top && mouse.Y <= layout.Plot.Bottom;
    }

    // Called by the shared CandleChartControl drawing surface because that
    // transparent overlay receives pointer input while Tick mode is active.
    public bool HandleExternalFindMarkerMouseDown(Point mouse, bool deleteHit = false)
    {
        if (!TryCreateLayout(out TickLayout layout) || !HitTestFindMarker(mouse, layout))
        {
            if (_findMarkerSelected && !deleteHit)
            {
                _findMarkerSelected = false;
                InvalidateVisual();
            }
            return false;
        }

        if (deleteHit)
            ClearFindMarker();
        else
        {
            _findMarkerSelected = true;
            InvalidateVisual();
        }
        return true;
    }

    public bool HandleExternalFindMarkerKeyDown(KeyEventArgs e)
    {
        if (!_findMarkerSelected || e.Key is not (Key.Delete or Key.Back))
            return false;
        ClearFindMarker();
        e.Handled = true;
        return true;
    }

    public bool HandleExternalFindMarkerRightClick(Point mouse, UIElement placementTarget)
    {
        if (!TryCreateLayout(out TickLayout layout) || !HitTestFindMarker(mouse, layout))
            return false;

        _findMarkerSelected = true;
        InvalidateVisual();
        var menu = new ContextMenu { PlacementTarget = placementTarget };
        var remove = new MenuItem { Header = "Remove Find Tick marker" };
        remove.Click += (_, _) => ClearFindMarker();
        menu.Items.Add(remove);
        menu.IsOpen = true;
        return true;
    }
}
