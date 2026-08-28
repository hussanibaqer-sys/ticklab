using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TickLab.Core.Alerts;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl
{
    private void DrawAlertLines(DrawingContext drawingContext, ChartLayout layout)
    {
        if (_alertLines.Count == 0)
            return;

        foreach (AlertLineOverlay line in _alertLines)
        {
            string colorText = string.IsNullOrWhiteSpace(line.Color)
                ? Settings.AlertLineColor
                : line.Color;
            Color lineColor = ColorFrom(colorText, Color.FromRgb(245, 184, 62));
            Pen pen = CreatePen(colorText, Math.Clamp(line.Thickness, 0.5, 8.0), ChartLineStyle.Dashed);
            var fillColor = Color.FromArgb(225, lineColor.R, lineColor.G, lineColor.B);
            Brush fill = new SolidColorBrush(fillColor);
            Brush textBrush = (lineColor.R * 299 + lineColor.G * 587 + lineColor.B * 114) / 1000 >= 145
                ? Brushes.Black
                : Brushes.White;
            if (fill.CanFreeze)
                fill.Freeze();
            double price = string.Equals(line.AlertId, _draggingAlertLineId, StringComparison.Ordinal)
                ? _draggingAlertPrice ?? line.Price
                : line.Price;
            if (!double.IsFinite(price))
                continue;

            double y = PriceToY(price, layout);
            if (y < layout.Plot.Top - 1 || y > layout.Plot.Bottom + 1)
                continue;

            y = SnapStrokeCoordinate(y, pen.Thickness);
            drawingContext.DrawLine(
                pen,
                new Point(layout.Plot.Left, y),
                new Point(layout.Plot.Right, y));

            string priceText = price.ToString("G10", CultureInfo.InvariantCulture);
            string label = string.IsNullOrWhiteSpace(line.Label)
                ? $"🔔 {priceText}"
                : $"🔔 {line.Label}  {priceText}";
            FormattedText text = CreateText(label, 10, textBrush);
            double height = Math.Max(18, text.Height + 4);
            double width = Math.Min(Math.Max(72, text.Width + 12), Math.Max(72, layout.Plot.Width * 0.42));
            double left = Math.Max(layout.Plot.Left + 2, layout.Plot.Right - width - 3);
            double top = Math.Clamp(y - height / 2, layout.Plot.Top + 1, layout.Plot.Bottom - height - 1);
            var ticket = new Rect(left, top, width, height);
            drawingContext.DrawRoundedRectangle(fill, null, ticket, 3, 3);
            drawingContext.PushClip(new RectangleGeometry(ticket));
            drawingContext.DrawText(text, new Point(ticket.Left + 6, ticket.Top + 2));
            drawingContext.Pop();
        }
    }

    private AlertLineOverlay? HitTestAlertLine(ChartLayout layout, Point mouse)
    {
        if (!layout.Plot.Contains(mouse) || _alertLines.Count == 0)
            return null;

        const double tolerance = 7.0;
        AlertLineOverlay? best = null;
        double bestDistance = double.MaxValue;
        foreach (AlertLineOverlay line in _alertLines)
        {
            double price = string.Equals(line.AlertId, _draggingAlertLineId, StringComparison.Ordinal)
                ? _draggingAlertPrice ?? line.Price
                : line.Price;
            double y = PriceToY(price, layout);
            double distance = Math.Abs(mouse.Y - y);
            if (distance <= tolerance && distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
        }
        return best;
    }
}
