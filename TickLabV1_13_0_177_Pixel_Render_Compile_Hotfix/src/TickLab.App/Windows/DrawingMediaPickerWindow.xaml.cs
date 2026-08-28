using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TickLab.Core.Drawing;
using TickLab.Desktop.Controls;

namespace TickLab.Desktop.Windows;

public partial class DrawingMediaPickerWindow : Window
{
    private const string RecentCategory = "Recently used";
    private const int EmojiColumns = 9;
    private static readonly List<string> RecentEmojiKeys = new();

    private string _emojiCategory = RecentCategory;
    private bool _stickersBuilt;
    private bool _iconsBuilt;
    private readonly FrameworkElement? _chartBoundsElement;
    private const double ChartTimeScaleReserve = 32.0;

    public DrawingMediaPickerWindow(string requestedToolId, FrameworkElement? chartBoundsElement = null)
    {
        _chartBoundsElement = chartBoundsElement;
        InitializeComponent();
        BuildEmojiCategoryButtons();
        MediaTabs.SelectedIndex = requestedToolId switch
        {
            "stickers" => 1,
            "icons" => 2,
            _ => 0
        };
        UpdateTabPresentation();

        Loaded += (_, _) =>
        {
            PositionInsideOwnerNearDrawingRail();
            BuildCurrentPanel(force: true);
        };
    }

    public event Action<string, string>? MediaSelected;

    private void PositionInsideOwnerNearDrawingRail()
    {
        if (Owner is null)
            return;

        double ownerWidth = Owner.ActualWidth > 1 ? Owner.ActualWidth : Owner.Width;
        double ownerHeight = Owner.ActualHeight > 1 ? Owner.ActualHeight : Owner.Height;
        if (!double.IsFinite(ownerWidth) || !double.IsFinite(ownerHeight) || ownerWidth <= 1 || ownerHeight <= 1)
            return;

        // Default safe area is the owner client frame. When the active chart is supplied,
        // constrain the flyout to the chart itself and stop at the TOP of the 32px time scale.
        // This keeps the picker inside TickLab and prevents it from covering/crossing the time axis.
        double safeLeft = Owner.Left + 4;
        double safeTop = Owner.Top + 4;
        double safeRight = Owner.Left + ownerWidth - 6;
        double safeBottom = Owner.Top + ownerHeight - 6;

        if (_chartBoundsElement is not null && _chartBoundsElement.IsLoaded && _chartBoundsElement.ActualWidth > 1 && _chartBoundsElement.ActualHeight > 1)
        {
            try
            {
                // PointToScreen is device-pixel based; convert back to WPF DIPs before
                // assigning Window.Left/Top so the clamp remains correct at 125/150% DPI.
                Point chartScreenPixels = _chartBoundsElement.PointToScreen(new Point(0, 0));
                PresentationSource? source = PresentationSource.FromVisual(_chartBoundsElement);
                Point chartOrigin = source?.CompositionTarget is not null
                    ? source.CompositionTarget.TransformFromDevice.Transform(chartScreenPixels)
                    : chartScreenPixels;
                safeLeft = chartOrigin.X + 4;
                safeTop = chartOrigin.Y + 4;
                safeRight = chartOrigin.X + _chartBoundsElement.ActualWidth - 6;
                safeBottom = chartOrigin.Y + Math.Max(40, _chartBoundsElement.ActualHeight - ChartTimeScaleReserve - 4);
            }
            catch (InvalidOperationException)
            {
                // Fall back to owner bounds if visual ancestry changes during workspace layout.
            }
        }

        double availableWidth = Math.Max(260, safeRight - safeLeft);
        double availableHeight = Math.Max(220, safeBottom - safeTop);
        double width = Math.Min(Width, availableWidth);
        double height = Math.Min(Height, availableHeight);
        Width = Math.Max(Math.Min(MinWidth, availableWidth), width);
        Height = Math.Max(Math.Min(MinHeight, availableHeight), height);

        // Anchor next to the left drawing rail, then clamp every edge into the safe chart rectangle.
        double desiredLeft = safeLeft + 43;
        double desiredTop = safeTop + 6;
        double maxLeft = Math.Max(safeLeft, safeRight - Width);
        double maxTop = Math.Max(safeTop, safeBottom - Height);
        Left = Math.Clamp(desiredLeft, safeLeft, maxLeft);
        Top = Math.Clamp(desiredTop, safeTop, maxTop);
    }

    private void BuildEmojiCategoryButtons()
    {
        EmojiCategoryButtonsPanel.Children.Clear();
        AddCategoryButton(RecentCategory, "🕘");
        string[] glyphs = { "😊", "🦄", "🍜", "⚽", "🚀", "💡", "❤️", "🏳️" };
        for (int i = 0; i < DrawingMediaCatalog.EmojiCategories.Count; i++)
            AddCategoryButton(DrawingMediaCatalog.EmojiCategories[i], glyphs[Math.Min(i, glyphs.Length - 1)]);
    }

    private void AddCategoryButton(string category, string glyph)
    {
        bool active = string.Equals(_emojiCategory, category, StringComparison.OrdinalIgnoreCase);
        var host = new Grid
        {
            Width = 50,
            Height = 38,
            Margin = new Thickness(0, 0, 2, 0),
            Tag = category
        };
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });

        var button = new Button
        {
            Width = 48,
            Height = 34,
            MinHeight = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Tag = category,
            Content = new Image
            {
                Source = EmojiVectorAssets.GetDrawingImageOrPlaceholder(glyph),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = category,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = active
                ? new SolidColorBrush(Color.FromRgb(41, 121, 255))
                : (TryFindResource("TextBrush") as Brush ?? Brushes.White)
        };
        button.Click += (_, _) => JumpToEmojiCategory(category);
        host.Children.Add(button);

        var underline = new Border
        {
            Height = active ? 3 : 0,
            Background = new SolidColorBrush(Color.FromRgb(41, 121, 255)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(underline, 1);
        host.Children.Add(underline);
        EmojiCategoryButtonsPanel.Children.Add(host);
    }

    private void JumpToEmojiCategory(string category)
    {
        _emojiCategory = category;
        BuildEmojiCategoryButtons();

        if (MediaTabs.SelectedIndex != 0)
        {
            MediaTabs.SelectedIndex = 0;
            return;
        }

        BuildEmojiRows(MediaSearchBox.Text.Trim());
    }

    private void BuildCurrentPanel(bool force = false)
    {
        string query = MediaSearchBox.Text.Trim();
        if (MediaTabs.SelectedItem is not TabItem selected || selected.Tag is not string type)
            return;

        switch (type)
        {
            case "emojis":
                BuildEmojiRows(query);
                break;
            case "stickers":
                if (force || !_stickersBuilt || !string.IsNullOrWhiteSpace(query))
                {
                    BuildPanel(StickersPanel, DrawingMediaCatalog.Stickers, query);
                    _stickersBuilt = string.IsNullOrWhiteSpace(query);
                }
                break;
            case "icons":
                if (force || !_iconsBuilt || !string.IsNullOrWhiteSpace(query))
                {
                    BuildPanel(IconsPanel, DrawingMediaCatalog.Icons, query);
                    _iconsBuilt = string.IsNullOrWhiteSpace(query);
                }
                break;
        }
        UpdateCount();
    }

    private void BuildEmojiRows(string query)
    {
        IEnumerable<DrawingMediaDefinition> visible;
        string heading;

        if (!string.IsNullOrWhiteSpace(query))
        {
            heading = "SEARCH RESULTS";
            visible = DrawingMediaCatalog.Emojis.Where(item =>
                item.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Mark.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(_emojiCategory, RecentCategory, StringComparison.OrdinalIgnoreCase))
        {
            heading = RecentCategory.ToUpperInvariant();
            visible = BuildRecentEmojiSequence();
        }
        else
        {
            heading = _emojiCategory.ToUpperInvariant();
            visible = DrawingMediaCatalog.Emojis.Where(item =>
                string.Equals(item.Category, _emojiCategory, StringComparison.OrdinalIgnoreCase));
        }

        DrawingMediaDefinition[] values = visible.ToArray();
        var rows = new List<IReadOnlyList<DrawingMediaDefinition>>((values.Length + EmojiColumns - 1) / EmojiColumns);
        for (int i = 0; i < values.Length; i += EmojiColumns)
            rows.Add(values.Skip(i).Take(EmojiColumns).ToArray());

        EmojiCurrentCategoryHeading.Text = heading;
        EmojiRowsList.ItemsSource = rows;

        // Reset to the top without forcing all row containers to be realized.
        if (rows.Count > 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try { EmojiRowsList.ScrollIntoView(rows[0]); }
                catch (InvalidOperationException) { }
            }));
        }
    }

    private static IEnumerable<DrawingMediaDefinition> BuildRecentEmojiSequence()
    {
        if (RecentEmojiKeys.Count > 0)
        {
            foreach (string key in RecentEmojiKeys)
            {
                DrawingMediaDefinition? item = DrawingMediaCatalog.Find("emojis", key);
                if (item is not null)
                    yield return item;
            }
            yield break;
        }

        // Useful first-open strip until real history exists.
        string[] starterMarks = { "😀", "✅", "⚽" };
        foreach (string mark in starterMarks)
        {
            DrawingMediaDefinition? item = DrawingMediaCatalog.Emojis.FirstOrDefault(candidate => candidate.Mark == mark);
            if (item is not null)
                yield return item;
        }
    }

    private void BuildPanel(Panel panel, IEnumerable<DrawingMediaDefinition> source, string query)
    {
        panel.Children.Clear();
        IEnumerable<DrawingMediaDefinition> visible = source;
        if (!string.IsNullOrWhiteSpace(query))
        {
            visible = visible.Where(item =>
                item.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Mark.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (DrawingMediaDefinition media in visible)
            panel.Children.Add(CreateMediaButton(media));
    }

    private Button CreateMediaButton(DrawingMediaDefinition media)
    {
        bool sticker = media.Type == "stickers";
        bool emoji = media.Type == "emojis";
        var button = new Button
        {
            Width = sticker ? 142 : emoji ? 52 : 88,
            Height = sticker ? 66 : emoji ? 48 : 88,
            MinHeight = sticker ? 66 : emoji ? 48 : 88,
            Margin = emoji ? new Thickness(1) : new Thickness(4),
            Padding = emoji ? new Thickness(0) : new Thickness(4),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = media,
            ToolTip = media.Label
        };
        button.Content = sticker
            ? CreateStickerPreview(media)
            : emoji
                ? CreateEmojiPreview(media)
                : CreateBadgePreview(media);
        button.Click += MediaButton_Click;
        return button;
    }

    private static UIElement CreateEmojiPreview(DrawingMediaDefinition media)
    {
        return new Image
        {
            Source = EmojiVectorAssets.GetDrawingImageOrPlaceholder(media.Mark),
            Width = 34,
            Height = 34,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static UIElement CreateStickerPreview(DrawingMediaDefinition media)
    {
        return new Border
        {
            Height = 34,
            MinWidth = 112,
            Padding = new Thickness(9, 0, 9, 0),
            CornerRadius = new CornerRadius(9),
            Background = BrushFrom(media.PrimaryColor),
            BorderBrush = BrushFrom(media.SecondaryColor),
            BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = media.Label,
                Foreground = Brushes.White,
                FontSize = media.Label.Length > 11 ? 9.5 : 11.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static UIElement CreateBadgePreview(DrawingMediaDefinition media)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Grid { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        badge.Children.Add(new Ellipse
        {
            Fill = BrushFrom(media.PrimaryColor),
            Stroke = BrushFrom(media.SecondaryColor),
            StrokeThickness = 2
        });
        badge.Children.Add(new Ellipse
        {
            Width = 36,
            Height = 36,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        badge.Children.Add(new TextBlock
        {
            Text = media.Mark,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = media.Mark.Length > 1 ? 15 : 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(badge);
        stack.Children.Add(new TextBlock
        {
            Text = media.Label,
            Foreground = new SolidColorBrush(Color.FromRgb(175, 192, 212)),
            FontSize = 9.5,
            Margin = new Thickness(0, 3, 0, 0),
            MaxWidth = 74,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return stack;
    }

    private void MediaButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DrawingMediaDefinition media })
            return;

        if (media.Type == "emojis")
        {
            RecentEmojiKeys.RemoveAll(key => string.Equals(key, media.Key, StringComparison.OrdinalIgnoreCase));
            RecentEmojiKeys.Insert(0, media.Key);
            if (RecentEmojiKeys.Count > 32)
                RecentEmojiKeys.RemoveRange(32, RecentEmojiKeys.Count - 32);
        }

        MediaSelected?.Invoke(media.Type, DrawingMediaCatalog.Encode(media));
        Close();
    }

    private void MediaSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
            BuildCurrentPanel(force: true);
    }

    private void MediaTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateTabPresentation();
        BuildCurrentPanel();
    }

    private void UpdateTabPresentation()
    {
        if (MediaTabs.SelectedItem is not TabItem selected || selected.Tag is not string type)
            return;
        bool emoji = type == "emojis";
        EmojiCategoryHost.Visibility = emoji ? Visibility.Visible : Visibility.Collapsed;
        EmojiCategoryRow.Height = emoji ? new GridLength(48) : new GridLength(0);
    }

    private void UpdateCount()
    {
        // Count display is intentionally hidden in the reference-style layout.
    }

    private static Brush BrushFrom(string value)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
                return new SolidColorBrush(color);
        }
        catch
        {
        }
        return Brushes.DodgerBlue;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsLoaded && IsVisible)
            Close();
    }
}
