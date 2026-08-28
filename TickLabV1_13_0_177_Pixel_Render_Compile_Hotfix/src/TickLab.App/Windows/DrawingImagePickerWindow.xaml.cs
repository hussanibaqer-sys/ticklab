using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TickLab.Desktop.Windows;

public partial class DrawingImagePickerWindow : Window
{
    private const long MaxImageBytes = 2L * 1024L * 1024L;
    private string _sourcePath = string.Empty;
    private double _aspectRatio = 1.0;

    public DrawingImagePickerWindow()
    {
        InitializeComponent();
    }

    public string SelectedImagePath { get; private set; } = string.Empty;
    public double SelectedOpacity => 1.0 - Math.Clamp(TransparencySlider.Value / 100.0, 0.0, 1.0);
    public double SelectedAspectRatio => _aspectRatio;

    private void ChooseImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose image",
            Filter = "Image files (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|WEBP (*.webp)|*.webp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > MaxImageBytes)
            {
                MessageBox.Show(this, "The selected image is larger than 2 MB.", "Image", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _sourcePath = info.FullName;
            SelectedFileText.Text = info.Name;
            LoadingOverlay.Visibility = Visibility.Visible;
            ChooseImagePrompt.Visibility = Visibility.Collapsed;
            OkButton.IsEnabled = false;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(info.FullName, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                PreviewImage.Source = bitmap;
                PreviewImage.Visibility = Visibility.Visible;
                ReplaceImageButton.Visibility = Visibility.Visible;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = true;
                if (bitmap.PixelHeight > 0)
                    _aspectRatio = Math.Clamp((double)bitmap.PixelWidth / bitmap.PixelHeight, 0.05, 20.0);
            }
            catch
            {
                // Keep WEBP available even when a Windows installation has no preview codec.
                // TickLab will still persist the file and try again when it is rendered.
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ChooseImagePrompt.Visibility = Visibility.Visible;
                ReplaceImageButton.Visibility = Visibility.Visible;
                OkButton.IsEnabled = true;
                SelectedFileText.Text = $"{info.Name} • preview unavailable";
                _aspectRatio = 1.0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open this image.\n\n{ex.Message}", "Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourcePath) || !File.Exists(_sourcePath))
            return;

        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TickLab",
                "DrawingImages");
            Directory.CreateDirectory(folder);
            string extension = Path.GetExtension(_sourcePath).ToLowerInvariant();
            string target = Path.Combine(folder, $"{Guid.NewGuid():N}{extension}");
            File.Copy(_sourcePath, target, overwrite: false);
            SelectedImagePath = target;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the chart image.\n\n{ex.Message}", "Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransparencyValueText is not null)
            TransparencyValueText.Text = $"{Math.Round(e.NewValue):0}%";
        if (PreviewImage is not null)
            PreviewImage.Opacity = 1.0 - Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
