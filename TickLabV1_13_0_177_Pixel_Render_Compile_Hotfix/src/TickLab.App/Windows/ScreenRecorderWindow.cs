using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Channels;
using TickLab.Desktop.Core.Recording;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop.Windows;

public sealed class ScreenRecorderWindow : Window
{
    private const int FramesPerSecond = 15;
    private const long SafeAviSizeLimit = 1_850_000_000L;
    private const int FrameQueueCapacity = 2;

    private readonly Func<BitmapSource?> _captureFrame;
    private readonly Func<BitmapSource?> _captureScreenshot;
    private readonly DispatcherTimer _captureTimer;
    private readonly TextBlock _statusText;
    private readonly Button _recordButton;
    private readonly Button _pauseButton;
    private readonly Button _stopButton;
    private readonly ListBox _recordingsList;

    private MjpegAviWriter? _writer;
    private string? _activePath;
    private bool _paused;
    private bool _allowClose;
    private DateTime _startedUtc;
    private int _recordingWidth;
    private int _recordingHeight;
    private Channel<BitmapSource>? _frameChannel;
    private Task? _encoderTask;
    private Exception? _encoderFailure;
    private long _encodedFrameCount;
    private long _encodedBytes;
    private long _droppedFrameCount;
    private int _queuedFrameCount;
    private int _sizeLimitReached;

    public ScreenRecorderWindow(Func<BitmapSource?> captureFrame, Func<BitmapSource?>? captureScreenshot = null)
    {
        _captureFrame = captureFrame ?? throw new ArgumentNullException(nameof(captureFrame));
        _captureScreenshot = captureScreenshot ?? _captureFrame;
        Title = "TickLab Recorder";
        Width = 660;
        Height = 520;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _captureTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond)
        };
        _captureTimer.Tick += (_, _) => CaptureVideoFrame();

        _recordButton = MakeButton("● Record", 90, StartRecording);
        _pauseButton = MakeButton("Ⅱ Pause", 90, TogglePause);
        _stopButton = MakeButton("■ Stop", 90, StopRecording);
        Button screenshotButton = MakeButton("▣ Screenshot", 105, SaveScreenshot);
        Button openFolderButton = MakeButton("Open Recordings", 130, OpenRecordingsFolder);
        _pauseButton.IsEnabled = false;
        _stopButton.IsEnabled = false;

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controls.Children.Add(_recordButton);
        controls.Children.Add(_pauseButton);
        controls.Children.Add(_stopButton);
        controls.Children.Add(screenshotButton);
        controls.Children.Add(openFolderButton);

        _statusText = new TextBlock
        {
            Text = "Ready. Recorder captures the TickLab window only. Video: optimized MJPEG AVI · up to 15 FPS. Recording yields to chart rendering.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        _recordingsList = new ListBox
        {
            MinHeight = 220,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _recordingsList.MouseDoubleClick += (_, _) => PlaySelected();

        Button playButton = MakeButton("Play", 80, PlaySelected);
        Button revealButton = MakeButton("Show in Folder", 115, RevealSelected);
        Button deleteButton = MakeButton("Delete", 80, DeleteSelected);
        var fileButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        fileButtons.Children.Add(playButton);
        fileButtons.Children.Add(revealButton);
        fileButtons.Children.Add(deleteButton);

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Screen Recorder + Screenshots",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(heading, 0);
        Grid.SetRow(controls, 1);
        Grid.SetRow(_statusText, 2);
        Grid.SetRow(_recordingsList, 3);
        Grid.SetRow(fileButtons, 4);
        root.Children.Add(heading);
        root.Children.Add(controls);
        root.Children.Add(_statusText);
        root.Children.Add(_recordingsList);
        root.Children.Add(fileButtons);
        Content = root;

        Loaded += (_, _) =>
        {
            ApplicationThemeManager.ApplyToWindow(this);
            RefreshRecordings();
        };
        Closing += ScreenRecorderWindow_Closing;
        Directory.CreateDirectory(RecordingsFolder);
    }

    public static string RecordingsFolder
    {
        get
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(videos))
                videos = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(videos, "TickLab", "Recordings");
        }
    }

    public bool IsRecording => _writer is not null;

    public void ActivateRecorder()
    {
        if (!IsVisible)
            Show();
        Activate();
        RefreshRecordings();
    }

    public void RefreshMediaList() => RefreshRecordings();

    public void CloseForShutdown()
    {
        _allowClose = true;
        try
        {
            StopRecordingCore(promptToKeep: false, keepByDefault: true);
        }
        catch
        {
        }
        Close();
    }

    private static Button MakeButton(string text, double width, Action action)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Margin = new Thickness(0, 0, 7, 0),
            Padding = new Thickness(6, 2, 6, 2)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void StartRecording()
    {
        if (_writer is not null)
            return;

        try
        {
            BitmapSource? first = _captureFrame();
            if (first is null || first.PixelWidth <= 0 || first.PixelHeight <= 0)
            {
                _statusText.Text = "TickLab could not capture the window. Keep the main TickLab window visible and try again.";
                return;
            }

            Directory.CreateDirectory(RecordingsFolder);
            _activePath = Path.Combine(
                RecordingsFolder,
                $"TickLab_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.avi");
            _recordingWidth = first.PixelWidth;
            _recordingHeight = first.PixelHeight;
            _writer = new MjpegAviWriter(_activePath, _recordingWidth, _recordingHeight, FramesPerSecond);
            _frameChannel = Channel.CreateBounded<BitmapSource>(new BoundedChannelOptions(FrameQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            _encoderFailure = null;
            Interlocked.Exchange(ref _encodedFrameCount, 0);
            Interlocked.Exchange(ref _encodedBytes, 0);
            Interlocked.Exchange(ref _droppedFrameCount, 0);
            Interlocked.Exchange(ref _queuedFrameCount, 0);
            Volatile.Write(ref _sizeLimitReached, 0);

            MjpegAviWriter writer = _writer;
            ChannelReader<BitmapSource> reader = _frameChannel.Reader;
            _encoderTask = Task.Run(() => EncodeQueuedFramesAsync(writer, reader));

            _paused = false;
            _startedUtc = DateTime.UtcNow;
            EnqueueFrame(first);
            _captureTimer.Start();
            _recordButton.IsEnabled = false;
            _pauseButton.IsEnabled = true;
            _stopButton.IsEnabled = true;
            _pauseButton.Content = "Ⅱ Pause";
            UpdateRecordingStatus();
        }
        catch (Exception exception)
        {
            AbortRecordingFile();
            _statusText.Text = $"Recording could not start: {exception.Message}";
        }
    }

    private void TogglePause()
    {
        if (_writer is null)
            return;

        _paused = !_paused;
        _pauseButton.Content = _paused ? "▶ Resume" : "Ⅱ Pause";
        _statusText.Text = _paused
            ? "Recording paused. Press Resume to continue or Stop to finish."
            : "Recording resumed.";
    }

    private void StopRecording() => StopRecordingCore(promptToKeep: true, keepByDefault: false);

    private void StopRecordingCore(bool promptToKeep, bool keepByDefault)
    {
        if (_writer is null)
            return;

        _captureTimer.Stop();
        MjpegAviWriter writer = _writer;
        _writer = null;
        Channel<BitmapSource>? channel = _frameChannel;
        _frameChannel = null;
        Task? encoderTask = _encoderTask;
        _encoderTask = null;
        string? completedPath = _activePath;
        _activePath = null;
        _paused = false;

        try
        {
            channel?.Writer.TryComplete();
            try
            {
                encoderTask?.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _encoderFailure ??= exception;
            }

            writer.Dispose();
        }
        finally
        {
            _recordButton.IsEnabled = true;
            _pauseButton.IsEnabled = false;
            _stopButton.IsEnabled = false;
            _pauseButton.Content = "Ⅱ Pause";
        }

        if (string.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
        {
            _statusText.Text = "Recording stopped, but the output file was not found.";
            return;
        }

        if (_encoderFailure is not null)
        {
            _statusText.Text = $"Recording finalized after an encoder error: {_encoderFailure.Message}";
            keepByDefault = true;
        }

        bool keep = keepByDefault;
        string description = string.Empty;
        if (promptToKeep)
        {
            var disposition = new RecordingDispositionWindow("Recording complete", Path.GetFileName(completedPath))
            {
                Owner = this
            };
            disposition.ShowDialog();
            keep = disposition.KeepFile;
            description = disposition.Description;
        }

        if (!keep)
        {
            TryDeleteMedia(completedPath);
            _statusText.Text = "Recording deleted.";
        }
        else
        {
            RecordingMetadata.Save(completedPath, description, "Video");
            _statusText.Text = $"Saved: {Path.GetFileName(completedPath)}";
        }
        RefreshRecordings();
    }

    private void CaptureVideoFrame()
    {
        if (_writer is null || _paused)
            return;

        if (_encoderFailure is not null)
        {
            _statusText.Text = $"Recording stopped after an encoder error: {_encoderFailure.Message}";
            StopRecordingCore(promptToKeep: true, keepByDefault: true);
            return;
        }

        if (Volatile.Read(ref _sizeLimitReached) != 0)
        {
            _statusText.Text = "Recording reached the safe AVI size limit and was stopped automatically.";
            StopRecordingCore(promptToKeep: true, keepByDefault: false);
            return;
        }

        // Never build a backlog on the TickLab UI thread. If the background encoder
        // is still busy, drop this recording frame and give chart/tick rendering priority.
        Channel<BitmapSource>? channel = _frameChannel;
        if (channel is null || Volatile.Read(ref _queuedFrameCount) >= FrameQueueCapacity)
        {
            Interlocked.Increment(ref _droppedFrameCount);
            UpdateRecordingStatus();
            return;
        }

        try
        {
            BitmapSource? frame = _captureFrame();
            if (frame is null)
                return;
            EnqueueFrame(frame);
            UpdateRecordingStatus();
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Recording stopped after a capture error: {exception.Message}";
            StopRecordingCore(promptToKeep: true, keepByDefault: true);
        }
    }

    private void EnqueueFrame(BitmapSource frame)
    {
        Channel<BitmapSource>? channel = _frameChannel;
        if (channel is null)
            return;
        if (!frame.IsFrozen && frame.CanFreeze)
            frame.Freeze();
        if (channel.Writer.TryWrite(frame))
            Interlocked.Increment(ref _queuedFrameCount);
        else
            Interlocked.Increment(ref _droppedFrameCount);
    }

    private async Task EncodeQueuedFramesAsync(MjpegAviWriter writer, ChannelReader<BitmapSource> reader)
    {
        try
        {
            await foreach (BitmapSource frame in reader.ReadAllAsync())
            {
                Interlocked.Decrement(ref _queuedFrameCount);
                byte[] jpeg = EncodeJpegFrame(frame);
                writer.WriteJpegFrame(jpeg);
                Interlocked.Exchange(ref _encodedFrameCount, writer.FrameCount);
                Interlocked.Exchange(ref _encodedBytes, writer.Length);
                if (writer.Length >= SafeAviSizeLimit)
                {
                    Volatile.Write(ref _sizeLimitReached, 1);
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            _encoderFailure = exception;
        }
    }

    private byte[] EncodeJpegFrame(BitmapSource frame)
    {
        BitmapSource normalized = frame;
        if (_recordingWidth > 0 && _recordingHeight > 0 &&
            (frame.PixelWidth != _recordingWidth || frame.PixelHeight != _recordingHeight))
        {
            var transformed = new TransformedBitmap(
                frame,
                new ScaleTransform(
                    _recordingWidth / (double)Math.Max(1, frame.PixelWidth),
                    _recordingHeight / (double)Math.Max(1, frame.PixelHeight)));
            if (transformed.CanFreeze)
                transformed.Freeze();
            normalized = transformed;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
        encoder.Frames.Add(BitmapFrame.Create(normalized));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    private void SaveScreenshot()
    {
        try
        {
            BitmapSource? frame = _captureScreenshot();
            if (frame is null)
            {
                _statusText.Text = "TickLab could not capture a screenshot.";
                return;
            }

            Directory.CreateDirectory(RecordingsFolder);
            string path = Path.Combine(
                RecordingsFolder,
                $"TickLab_Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);

            RecordingMetadata.Save(path, string.Empty, "Screenshot");
            _statusText.Text = $"Screenshot saved: {Path.GetFileName(path)}";
            RefreshRecordings();
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Screenshot failed: {exception.Message}";
        }
    }

    private void UpdateRecordingStatus()
    {
        if (_writer is null)
            return;
        TimeSpan elapsed = DateTime.UtcNow - _startedUtc;
        long frames = Interlocked.Read(ref _encodedFrameCount);
        long bytes = Interlocked.Read(ref _encodedBytes);
        long dropped = Interlocked.Read(ref _droppedFrameCount);
        string droppedText = dropped > 0 ? $" · {dropped:N0} skipped to protect chart" : string.Empty;
        _statusText.Text = _paused
            ? $"Paused · {elapsed:hh\\:mm\\:ss}"
            : $"● Recording · {elapsed:hh\\:mm\\:ss} · {frames:N0} frames · {bytes / (1024.0 * 1024.0):N1} MB{droppedText}";
    }

    private void RefreshRecordings()
    {
        Directory.CreateDirectory(RecordingsFolder);
        string? selectedPath = (_recordingsList.SelectedItem as RecordingListItem)?.Path;
        var items = Directory
            .EnumerateFiles(RecordingsFolder)
            .Where(path => path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                RecordingMetadata? metadata = RecordingMetadata.TryLoad(path);
                return new RecordingListItem(
                    path,
                    metadata?.Description ?? string.Empty,
                    metadata?.Kind ?? (path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ? "Video" : "Screenshot"),
                    File.GetLastWriteTime(path));
            })
            .OrderByDescending(item => item.Created)
            .ToList();
        _recordingsList.ItemsSource = items;
        if (!string.IsNullOrWhiteSpace(selectedPath))
            _recordingsList.SelectedItem = items.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
    }

    private void PlaySelected()
    {
        if (_recordingsList.SelectedItem is not RecordingListItem item || !File.Exists(item.Path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = item.Path, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Could not open recording: {exception.Message}";
        }
    }

    private void RevealSelected()
    {
        if (_recordingsList.SelectedItem is not RecordingListItem item || !File.Exists(item.Path))
        {
            OpenRecordingsFolder();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.Path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Could not open folder: {exception.Message}";
        }
    }

    private void OpenRecordingsFolder()
    {
        try
        {
            Directory.CreateDirectory(RecordingsFolder);
            Process.Start(new ProcessStartInfo { FileName = RecordingsFolder, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Could not open Recordings folder: {exception.Message}";
        }
    }

    private void DeleteSelected()
    {
        if (_recordingsList.SelectedItem is not RecordingListItem item || !File.Exists(item.Path))
            return;
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Delete {Path.GetFileName(item.Path)}?",
            "Delete recording",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;
        TryDeleteMedia(item.Path);
        RefreshRecordings();
        _statusText.Text = "Selected file deleted.";
    }

    private static void TryDeleteMedia(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        try
        {
            string metadata = RecordingMetadata.MetadataPath(path);
            if (File.Exists(metadata)) File.Delete(metadata);
        }
        catch { }
    }

    private void AbortRecordingFile()
    {
        _captureTimer.Stop();
        try { _frameChannel?.Writer.TryComplete(); } catch { }
        try { _encoderTask?.GetAwaiter().GetResult(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _frameChannel = null;
        _encoderTask = null;
        _writer = null;
        if (!string.IsNullOrWhiteSpace(_activePath))
            TryDeleteMedia(_activePath);
        _activePath = null;
        _recordingWidth = 0;
        _recordingHeight = 0;
        _paused = false;
        Interlocked.Exchange(ref _queuedFrameCount, 0);
        _recordButton.IsEnabled = true;
        _pauseButton.IsEnabled = false;
        _stopButton.IsEnabled = false;
    }

    private void ScreenRecorderWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    private sealed record RecordingListItem(string Path, string Description, string Kind, DateTime Created)
    {
        public override string ToString()
        {
            string description = string.IsNullOrWhiteSpace(Description) ? "" : $" — {Description}";
            return $"{Created:yyyy-MM-dd HH:mm:ss}   [{Kind}]   {System.IO.Path.GetFileName(Path)}{description}";
        }
    }
}

internal sealed class RecordingDispositionWindow : Window
{
    private bool _decided;
    private readonly TextBox _descriptionBox;

    public RecordingDispositionWindow(string title, string fileName)
    {
        Title = title;
        Width = 500;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Recording finished",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        var file = new TextBlock
        {
            Text = fileName,
            Margin = new Thickness(0, 6, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        _descriptionBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 85,
            ToolTip = "Optional description saved beside this recording"
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var delete = new Button { Content = "Delete", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save", Width = 90, Height = 30, IsDefault = true };
        delete.Click += (_, _) => { _decided = true; KeepFile = false; DialogResult = false; };
        save.Click += (_, _) => { _decided = true; KeepFile = true; DialogResult = true; };
        buttons.Children.Add(delete);
        buttons.Children.Add(save);

        Grid.SetRow(heading, 0);
        Grid.SetRow(file, 1);
        Grid.SetRow(_descriptionBox, 2);
        Grid.SetRow(buttons, 3);
        root.Children.Add(heading);
        root.Children.Add(file);
        root.Children.Add(_descriptionBox);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) => ApplicationThemeManager.ApplyToWindow(this);
        Closing += (_, _) =>
        {
            if (!_decided)
                KeepFile = true; // closing the prompt never destroys a completed recording
        };
    }

    public bool KeepFile { get; private set; } = true;
    public string Description => _descriptionBox.Text.Trim();
}
