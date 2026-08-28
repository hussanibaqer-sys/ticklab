using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TickLab.Desktop.Windows;
using TickLab.Desktop.Core.Recording;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private const int RecorderMaxPixelWidth = 1600;
    private const int RecorderMaxPixelHeight = 900;

    private ScreenRecorderWindow? _screenRecorderWindow;

    private void RecorderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screenRecorderWindow is null)
        {
            _screenRecorderWindow = new ScreenRecorderWindow(CaptureTickLabWindowFrame, CaptureTickLabWindowScreenshot)
            {
                Owner = this
            };
        }
        _screenRecorderWindow.ActivateRecorder();
    }

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BitmapSource? frame = CaptureTickLabWindowScreenshot();
            if (frame is null)
                return;

            Directory.CreateDirectory(ScreenRecorderWindow.RecordingsFolder);
            string path = Path.Combine(
                ScreenRecorderWindow.RecordingsFolder,
                $"TickLab_Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);

            RecordingMetadata.Save(path, string.Empty, "Screenshot");
            _screenRecorderWindow?.RefreshMediaList();
            StatusText.Text = $"Screenshot saved: {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Screenshot failed: {exception.Message}";
        }
    }

    private BitmapSource? CaptureTickLabWindowFrame()
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 1 || root.ActualHeight <= 1)
            return null;

        DpiScale dpi = VisualTreeHelper.GetDpi(root);
        int nativePixelWidth = Math.Max(2, (int)Math.Round(root.ActualWidth * dpi.DpiScaleX));
        int nativePixelHeight = Math.Max(2, (int)Math.Round(root.ActualHeight * dpi.DpiScaleY));
        double captureScale = Math.Min(
            1.0,
            Math.Min(
                RecorderMaxPixelWidth / (double)nativePixelWidth,
                RecorderMaxPixelHeight / (double)nativePixelHeight));
        int pixelWidth = Math.Max(2, (int)Math.Round(nativePixelWidth * captureScale));
        int pixelHeight = Math.Max(2, (int)Math.Round(nativePixelHeight * captureScale));

        // Keep small/normal windows on the cheapest direct WPF path. Only large
        // windows are downscaled through a VisualBrush to cap recorder pixel load.
        if (captureScale >= 0.999)
        {
            var direct = new RenderTargetBitmap(
                nativePixelWidth,
                nativePixelHeight,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            direct.Render(root);
            direct.Freeze();
            return direct;
        }

        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            var brush = new VisualBrush(root)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            drawing.DrawRectangle(brush, null, new Rect(0, 0, pixelWidth, pixelHeight));
        }

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private BitmapSource? CaptureTickLabWindowScreenshot()
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 1 || root.ActualHeight <= 1)
            return null;

        DpiScale dpi = VisualTreeHelper.GetDpi(root);
        int pixelWidth = Math.Max(2, (int)Math.Round(root.ActualWidth * dpi.DpiScaleX));
        int pixelHeight = Math.Max(2, (int)Math.Round(root.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private void ShutdownScreenRecorder()
    {
        if (_screenRecorderWindow is null)
            return;
        _screenRecorderWindow.CloseForShutdown();
        _screenRecorderWindow = null;
    }
}
