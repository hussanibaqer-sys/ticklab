using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TickLab.Core.Drawing;
using TickLab.Core.Market;
using TickLab.Core.Settings;

namespace TickLab.Desktop.Controls;

public sealed partial class CandleChartControl
{
    private static readonly JsonSerializerOptions DrawingJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly List<ChartDrawing> _drawings = new();
    private readonly HashSet<string> _selectedDrawingIds = new(StringComparer.Ordinal);
    private readonly Stack<string> _drawingUndo = new();
    private readonly Stack<string> _drawingRedo = new();
    private bool _restoringEditHistory;
    private string? _pendingViewportUndoSnapshot;
    private readonly List<string> _favoriteDrawingToolIds = new();
    private readonly List<string> _recentDrawingToolIds = new();
    private readonly List<DrawingTemplate> _drawingTemplates = new();
    private readonly Dictionary<string, string> _nextDrawingMediaSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastDrawingMediaSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _drawingImageCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Emoji-folder emoji are cached as bundled vector DrawingImage objects.
    // The chart never depends on the installed Windows emoji font, so every
    // catalog entry stays full-colour and scalable on the user's PC.
    private readonly Dictionary<string, DrawingImage> _drawingEmojiImageCache =
        new(StringComparer.Ordinal);
    private string _nextDrawingImagePath = string.Empty;
    private double _nextDrawingImageOpacity = 1.0;
    private double _nextDrawingImageAspectRatio = 1.0;
    private string _activeDrawingToolId = "cursor-crosshair";
    private string _lastUsedDrawingToolId = "trend-line";
    private DrawingMagnetMode _drawingMagnetMode;
    private bool _snapDrawingsToIndicators;
    private bool _stayInDrawingMode;
    private bool _hideAllDrawings;
    private bool _lockAllDrawings;
    private DrawingSyncMode _defaultDrawingSyncMode = DrawingSyncMode.CurrentChart;
    private ChartDrawing? _workingDrawing;
    private DrawingAnchor? _previewDrawingAnchor;
    private bool _freehandDrawing;
    private Point? _freehandFilteredPoint;
    private Point? _freehandLastAcceptedPoint;
    private DrawingDragMode _drawingDragMode;
    private string _dragDrawingId = string.Empty;
    private int _dragAnchorIndex = -1;
    private IReadOnlyList<DrawingAnchor> _dragStartAnchors = Array.Empty<DrawingAnchor>();
    private DrawingAnchor? _dragStartMouseAnchor;
    private double _dragStartMediaScale = 1.0;
    private Point? _drawingSelectionBoxStart;
    private Rect? _drawingSelectionBox;
    private ChartDrawing? _copiedDrawing;
    private bool _drawingSystemInitialized;
    private bool _measureModeArmed;
    private bool _measureDragging;
    private DrawingAnchor? _measureStartAnchor;
    private DrawingAnchor? _measureEndAnchor;
    private string _measureLineColor = "#38BDF8";
    private double _measureOpacity = 1.0;

    // Folder 6 annotation editing state. Table cells are edited directly on the
    // chart: click a cell, then type. The state is transient; the actual contents
    // live in ChartDrawing.Text and therefore persist with the workspace.
    private string _activeTableDrawingId = string.Empty;
    private int _activeTableCellRow = -1;
    private int _activeTableCellColumn = -1;
    private bool _tableCellUndoCaptured;

    // Reference cursor-folder transient effects. These are deliberately NOT chart drawings:
    // Demonstration ink and Magic particles fade automatically, matching the audited video.
    private sealed record DemonstrationCursorSample(Point Point, DateTimeOffset CreatedAt, int StrokeId);
    private sealed record MagicCursorParticle(string Glyph, Point Origin, Vector Velocity, double Rotation, double Spin, double Size, DateTimeOffset CreatedAt);
    private readonly List<DemonstrationCursorSample> _demonstrationCursorSamples = new();
    private readonly List<MagicCursorParticle> _magicCursorParticles = new();
    private readonly Random _magicCursorRandom = new();
    private DispatcherTimer? _cursorEffectsTimer;
    private bool _demonstrationCursorDrawing;
    private Point? _lastDemonstrationPoint;
    private int _demonstrationStrokeId;
    private bool _cursorValuesTooltipOnLongPress = true;
    private DateTimeOffset? _cursorLongPressStartedAt;
    private Point? _cursorLongPressStartPoint;
    private bool _cursorValuesTooltipVisible;
    private Point _cursorValuesTooltipPoint;

    public event Action? DrawingWorkspaceChanged;
    public event Action<string>? ActiveDrawingToolChanged;
    public event Action<ChartDrawing>? DrawingSettingsRequested;
    public event Action<ChartDrawing?>? DrawingSelectionChanged;
    public event Action<bool>? MeasurementModeChanged;
    public event Action? DrawingObjectTreeRequested;
    public event Action<string>? DrawingStatusChanged;
    public event Action<ChartDrawing>? DrawingAlertRequested;
    public event Action<bool>? DrawingFavoritesProjectionRequested;

    public IReadOnlyList<ChartDrawing> ChartDrawings => _drawings.ToArray();
    public IReadOnlyCollection<string> SelectedDrawingIds => _selectedDrawingIds.ToArray();
    public IReadOnlyList<string> FavoriteDrawingToolIds => _favoriteDrawingToolIds.ToArray();
    public IReadOnlyList<string> RecentDrawingToolIds => _recentDrawingToolIds.ToArray();
    public IReadOnlyList<DrawingTemplate> DrawingTemplates => _drawingTemplates.ToArray();
    public string ActiveDrawingToolId => _activeDrawingToolId;
    public string LastUsedDrawingToolId => _lastUsedDrawingToolId;
    public DrawingMagnetMode DrawingMagnetMode => _drawingMagnetMode;
    public bool SnapDrawingsToIndicators => _snapDrawingsToIndicators;
    public Func<int, IReadOnlyList<double>>? IndicatorSnapValuesProvider { get; set; }
    public bool StayInDrawingMode => _stayInDrawingMode;
    public bool HideAllDrawings => _hideAllDrawings;
    public bool LockAllDrawings => _lockAllDrawings;
    public bool CursorValuesTooltipOnLongPress => _cursorValuesTooltipOnLongPress;
    public DrawingSyncMode DefaultDrawingSyncMode => _defaultDrawingSyncMode;
    public bool MeasureModeActive => _measureModeArmed || _measureDragging;
    public string MeasureLineColor => _measureLineColor;
    public double MeasureOpacity => _measureOpacity;
    public string DrawingOwnerId { get; set; } = "main-chart-1";
    public ChartDrawing? SelectedDrawing => _drawings.LastOrDefault(item => _selectedDrawingIds.Contains(item.Id));

    // When a real drawing tool is armed, chart overlays must never steal its first
    // construction click. This is deliberately narrower than ordinary cursor mode:
    // demo-trade lines, alerts and replay markers keep their normal priority while
    // the user is simply navigating/selecting the chart.
    private bool DrawingPointerInputHasPriority(ChartLayout layout, Point mouse)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(_activeDrawingToolId);
        bool armedDrawingTool = tool is not null && !tool.IsCursorTool;
        bool drawingGestureActive = _workingDrawing is not null || _freehandDrawing ||
                                    _drawingDragMode != DrawingDragMode.None ||
                                    _drawingSelectionBoxStart.HasValue;
        return layout.Plot.Contains(mouse) && (armedDrawingTool || drawingGestureActive);
    }

    /// <summary>
    /// Returns the selected drawing bounds in chart-control coordinates.  The main window uses
    /// this to place the contextual object toolbar beside the actual object, as in the audited
    /// reference, rather than pinning that toolbar to a fixed screen location.
    /// </summary>
    public Rect? GetSelectedDrawingVisualBounds()
    {
        ChartDrawing? drawing = SelectedDrawing;
        if (drawing is null || !TryCreateLayout(out ChartLayout layout))
            return null;

        Rect bounds = GetDrawingBounds(drawing, layout);
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top))
            return null;

        // Keep the anchor meaningful for infinite line tools whose hit bounds span the plot.
        Rect plot = layout.Plot;
        Rect clipped = Rect.Intersect(bounds, plot);
        return clipped.IsEmpty ? bounds : clipped;
    }

    private void InitializeDrawingSystem()
    {
        if (_drawingSystemInitialized)
            return;
        _drawingSystemInitialized = true;
    }

    public DrawingWorkspaceState CaptureDrawingWorkspace() => new()
    {
        Drawings = _drawings.ToArray(),
        FavoriteToolIds = _favoriteDrawingToolIds.ToArray(),
        RecentToolIds = _recentDrawingToolIds.ToArray(),
        LastUsedToolId = _lastUsedDrawingToolId,
        MagnetMode = _drawingMagnetMode,
        SnapToIndicators = _snapDrawingsToIndicators,
        StayInDrawingMode = _stayInDrawingMode,
        HideAllDrawings = _hideAllDrawings,
        LockAllDrawings = _lockAllDrawings,
        CursorValuesTooltipOnLongPress = _cursorValuesTooltipOnLongPress,
        DefaultSyncMode = _defaultDrawingSyncMode,
        Templates = _drawingTemplates.ToArray()
    };

    public string ExportDrawingWorkspaceJson() =>
        JsonSerializer.Serialize(CaptureDrawingWorkspace(), DrawingJsonOptions);

    public void ImportDrawingWorkspaceJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            DrawingWorkspaceState? state = JsonSerializer.Deserialize<DrawingWorkspaceState>(json, DrawingJsonOptions);
            if (state is not null)
                LoadDrawingWorkspace(state);
        }
        catch (JsonException)
        {
            DrawingStatusChanged?.Invoke("Saved drawings could not be read. TickLab kept the chart usable.");
        }
    }

    public void LoadDrawingWorkspace(DrawingWorkspaceState state)
    {
        // A loaded workspace is the new baseline, not an undoable user edit.
        _drawingUndo.Clear();
        _drawingRedo.Clear();
        _pendingViewportUndoSnapshot = null;
        _drawings.Clear();
        _drawings.AddRange(state.Drawings ?? Array.Empty<ChartDrawing>());
        _favoriteDrawingToolIds.Clear();
        _favoriteDrawingToolIds.AddRange((state.FavoriteToolIds ?? Array.Empty<string>())
            .Where(id => DrawingToolCatalog.Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        _recentDrawingToolIds.Clear();
        _recentDrawingToolIds.AddRange((state.RecentToolIds ?? Array.Empty<string>())
            .Where(id => DrawingToolCatalog.Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12));
        _drawingTemplates.Clear();
        _drawingTemplates.AddRange(state.Templates ?? Array.Empty<DrawingTemplate>());
        _lastUsedDrawingToolId = DrawingToolCatalog.Find(state.LastUsedToolId)?.Id ?? "trend-line";
        _drawingMagnetMode = state.MagnetMode;
        _snapDrawingsToIndicators = state.SnapToIndicators;
        // Drawing tools are one-shot by default. Do not restore a stale persisted
        // Stay-in-drawing-mode flag because it leaves the last tool armed and steals
        // the next click that should select/drag an existing drawing or edit handle.
        // The user can still explicitly enable Stay in drawing mode for the current session.
        _stayInDrawingMode = false;
        _hideAllDrawings = state.HideAllDrawings;
        // Global Lock All is an interaction state, not an object property. Restoring
        // a stale saved value here can make every drawing look selected/unlocked in
        // the quick bar while silently blocking body drag, anchor drag, middle-click
        // delete, and quick-menu Remove. Start each session with global Lock All off;
        // individually locked drawings keep their own IsLocked state and remain
        // protected. The user can explicitly enable Lock All again for this session.
        _lockAllDrawings = false;
        _cursorValuesTooltipOnLongPress = state.CursorValuesTooltipOnLongPress;
        _defaultDrawingSyncMode = state.DefaultSyncMode;
        _selectedDrawingIds.Clear();
        PublishDrawingSelectionChanged();
        InvalidateVisual();
        ActiveDrawingToolChanged?.Invoke(_activeDrawingToolId);
    }

    public void ReplaceSynchronizedDrawings(string sourceOwnerId, IReadOnlyList<ChartDrawing> synchronized)
    {
        if (string.IsNullOrWhiteSpace(sourceOwnerId))
            return;

        _drawings.RemoveAll(item => string.Equals(item.ChartId, sourceOwnerId, StringComparison.Ordinal));
        foreach (ChartDrawing drawing in synchronized)
        {
            if (_drawings.Any(item => string.Equals(item.Id, drawing.Id, StringComparison.Ordinal)))
                continue;
            _drawings.Add(drawing);
        }
        _selectedDrawingIds.RemoveWhere(id => _drawings.All(item => !string.Equals(item.Id, id, StringComparison.Ordinal)));
        InvalidateVisual();
    }

    public void SetDrawingTool(string toolId)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(toolId);
        if (tool is null)
            return;

        CancelWorkingDrawing(false);
        if (_measureModeArmed || _measureDragging || _measureStartAnchor is not null)
        {
            _measureModeArmed = false;
            _measureDragging = false;
            _measureStartAnchor = null;
            _measureEndAnchor = null;
            MeasurementModeChanged?.Invoke(false);
        }
        _activeDrawingToolId = tool.Id;
        if (!tool.IsCursorTool)
        {
            // Shared rendering recovery: selecting a drawing tool means the user
            // expects drawings to be visible. A stale per-chart Hide All state can
            // otherwise create valid Inspector objects that never paint on-chart.
            if (_hideAllDrawings)
                _hideAllDrawings = false;
            // If a stale global Lock All state survived in the workspace, selecting
            // an editing/drawing tool must not leave every newly placed drawing
            // impossible to move, resize or remove. Individual IsLocked flags are
            // not changed; only the global interaction gate is released.
            if (_lockAllDrawings)
                _lockAllDrawings = false;
            _lastUsedDrawingToolId = tool.Id;
            _recentDrawingToolIds.RemoveAll(id => string.Equals(id, tool.Id, StringComparison.OrdinalIgnoreCase));
            _recentDrawingToolIds.Insert(0, tool.Id);
            if (_recentDrawingToolIds.Count > 12)
                _recentDrawingToolIds.RemoveRange(12, _recentDrawingToolIds.Count - 12);
        }
        UpdateDrawingCursor(tool);
        ActiveDrawingToolChanged?.Invoke(tool.Id);
        DrawingWorkspaceChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetCursorValuesTooltipOnLongPress(bool enabled)
    {
        if (_cursorValuesTooltipOnLongPress == enabled)
            return;
        _cursorValuesTooltipOnLongPress = enabled;
        if (!enabled)
            CancelCursorLongPress();
        DrawingWorkspaceChanged?.Invoke();
        InvalidateVisual();
    }

    public void ActivateLastUsedDrawingTool() => SetDrawingTool(_lastUsedDrawingToolId);

    public void SetMeasureStyle(string lineColor, double opacity)
    {
        if (!string.IsNullOrWhiteSpace(lineColor))
            _measureLineColor = lineColor.Trim();
        _measureOpacity = Math.Clamp(opacity, 0.0, 1.0);
        InvalidateVisual();
    }

    public void SetMeasureMode(bool active)
    {
        if (_measureModeArmed == active && (!active || _measureStartAnchor is null))
            return;
        CancelWorkingDrawing(false);
        _measureModeArmed = active;
        _measureDragging = false;
        _measureStartAnchor = null;
        _measureEndAnchor = null;
        Cursor = active ? Cursors.Cross : Cursors.Cross;
        MeasurementModeChanged?.Invoke(active);
        DrawingStatusChanged?.Invoke(active
            ? "Measure mode: click point A, then click point B. Shift may be released after point A."
            : "Measure mode off.");
        InvalidateVisual();
    }

    public bool CancelActiveDrawingToolOrMeasurement()
    {
        bool cancelled = false;
        if (_measureModeArmed || _measureDragging || _measureStartAnchor is not null)
        {
            _measureModeArmed = false;
            _measureDragging = false;
            _measureStartAnchor = null;
            _measureEndAnchor = null;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            MeasurementModeChanged?.Invoke(false);
            cancelled = true;
        }

        if (_workingDrawing is not null)
        {
            CancelWorkingDrawing(true);
            cancelled = true;
        }

        DrawingToolDefinition? active = DrawingToolCatalog.Find(_activeDrawingToolId);
        if (active is not null && !active.IsCursorTool)
        {
            _activeDrawingToolId = "cursor-crosshair";
            UpdateDrawingCursor(DrawingToolCatalog.Find(_activeDrawingToolId)!);
            ActiveDrawingToolChanged?.Invoke(_activeDrawingToolId);
            cancelled = true;
        }

        if (cancelled)
        {
            DrawingStatusChanged?.Invoke("Drawing action cancelled.");
            InvalidateVisual();
        }
        return cancelled;
    }

    public void PreviewDrawing(ChartDrawing drawing)
    {
        int index = _drawings.FindIndex(item => item.Id == drawing.Id);
        if (index < 0)
            return;
        _drawings[index] = drawing;
        InvalidateVisual();
    }

    public void ClearDrawingSelection()
    {
        if (_selectedDrawingIds.Count == 0)
            return;
        _selectedDrawingIds.Clear();
        PublishDrawingSelectionChanged();
        InvalidateVisual();
    }

    public bool ToggleDrawingFavorite(string toolId)
    {
        if (DrawingToolCatalog.Find(toolId) is null)
            return false;
        int index = _favoriteDrawingToolIds.FindIndex(id => string.Equals(id, toolId, StringComparison.OrdinalIgnoreCase));
        bool added;
        if (index >= 0)
        {
            _favoriteDrawingToolIds.RemoveAt(index);
            added = false;
        }
        else
        {
            _favoriteDrawingToolIds.Add(toolId);
            added = true;
        }
        DrawingWorkspaceChanged?.Invoke();
        return added;
    }

    public bool IsDrawingFavorite(string toolId) =>
        _favoriteDrawingToolIds.Any(id => string.Equals(id, toolId, StringComparison.OrdinalIgnoreCase));

    public void MoveDrawingFavorite(string sourceId, string targetId)
    {
        int sourceIndex = _favoriteDrawingToolIds.FindIndex(id =>
            string.Equals(id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            return;
        string value = _favoriteDrawingToolIds[sourceIndex];
        _favoriteDrawingToolIds.RemoveAt(sourceIndex);
        int targetIndex = string.IsNullOrWhiteSpace(targetId)
            ? _favoriteDrawingToolIds.Count
            : _favoriteDrawingToolIds.FindIndex(id =>
                string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            targetIndex = _favoriteDrawingToolIds.Count;
        _favoriteDrawingToolIds.Insert(Math.Clamp(targetIndex, 0, _favoriteDrawingToolIds.Count), value);
        DrawingWorkspaceChanged?.Invoke();
    }

    public void SetNextDrawingMediaSymbol(string toolId, string symbol)
    {
        if (toolId is not ("icons" or "stickers" or "emojis") || string.IsNullOrWhiteSpace(symbol))
            return;
        _nextDrawingMediaSymbols[toolId] = symbol;
    }

    public void SetNextDrawingImage(string path, double opacity, double aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _nextDrawingImagePath = path;
        _nextDrawingImageOpacity = Math.Clamp(opacity, 0.05, 1.0);
        _nextDrawingImageAspectRatio = Math.Clamp(aspectRatio, 0.05, 20.0);
    }

    public bool PlaceImageDrawing(string path, double opacity, double aspectRatio)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find("image");
        if (tool is null || string.IsNullOrWhiteSpace(path) || !TryCreateLayout(out ChartLayout layout))
            return false;

        double aspect = Math.Clamp(aspectRatio, 0.05, 20.0);
        double width = Math.Clamp(layout.Plot.Width * 0.28, 130.0, Math.Max(130.0, layout.Plot.Width * 0.55));
        double height = width / aspect;
        double maxHeight = Math.Max(90.0, layout.Plot.Height * 0.42);
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * aspect;
        }
        width = Math.Min(width, Math.Max(80.0, layout.Plot.Width * 0.65));
        height = Math.Min(height, Math.Max(60.0, layout.Plot.Height * 0.55));

        Point center = new(layout.Plot.Left + layout.Plot.Width * 0.5, layout.Plot.Top + layout.Plot.Height * 0.5);
        Point p1 = new(center.X - width / 2.0, center.Y - height / 2.0);
        Point p2 = new(center.X + width / 2.0, center.Y + height / 2.0);
        DrawingAnchor a1 = CreateDrawingAnchor(p1, layout, constrain: false);
        DrawingAnchor a2 = CreateDrawingAnchor(p2, layout, constrain: false);

        PushDrawingUndo();
        if (_hideAllDrawings)
            _hideAllDrawings = false;
        if (_lockAllDrawings)
            _lockAllDrawings = false;
        SetNextDrawingImage(path, opacity, aspect);
        ChartDrawing drawing = CreateNewDrawing(tool, new[] { a1, a2 });
        _drawings.Add(drawing);
        _selectedDrawingIds.Clear();
        _selectedDrawingIds.Add(drawing.Id);
        _lastUsedDrawingToolId = tool.Id;
        _recentDrawingToolIds.RemoveAll(id => string.Equals(id, tool.Id, StringComparison.OrdinalIgnoreCase));
        _recentDrawingToolIds.Insert(0, tool.Id);
        if (_recentDrawingToolIds.Count > 12)
            _recentDrawingToolIds.RemoveRange(12, _recentDrawingToolIds.Count - 12);
        PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Image placed on chart.");
        return true;
    }

    public void SetDrawingMagnetMode(DrawingMagnetMode mode)
    {
        _drawingMagnetMode = mode;
        DrawingWorkspaceChanged?.Invoke();
        DrawingStatusChanged?.Invoke($"Magnet: {mode}");
    }

    public void SetSnapDrawingsToIndicators(bool value)
    {
        _snapDrawingsToIndicators = value;
        DrawingWorkspaceChanged?.Invoke();
    }

    public void SetStayInDrawingMode(bool value)
    {
        _stayInDrawingMode = value;
        DrawingWorkspaceChanged?.Invoke();
    }

    public void SetDefaultDrawingSyncMode(DrawingSyncMode mode)
    {
        if (_defaultDrawingSyncMode == mode) return;
        _defaultDrawingSyncMode = mode;
        DrawingWorkspaceChanged?.Invoke();
        DrawingStatusChanged?.Invoke($"Drawing synchronization: {mode}.");
    }

    public void SetHideAllDrawings(bool value)
    {
        _hideAllDrawings = value;
        DrawingWorkspaceChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetLockAllDrawings(bool value)
    {
        _lockAllDrawings = value;
        DrawingWorkspaceChanged?.Invoke();
        InvalidateVisual();
    }

    public void RemoveSelectedDrawings() => DeleteSelectedDrawings();

    public void ClearAllDrawings(bool includeLocked = false)
    {
        PushDrawingUndo();
        _drawings.RemoveAll(item => includeLocked || (!item.IsLocked && !_lockAllDrawings));
        _selectedDrawingIds.Clear();
        PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Drawings removed.");
    }

    public void SelectDrawingById(string id, bool additive = false)
    {
        if (!additive)
            _selectedDrawingIds.Clear();
        if (_drawings.Any(item => item.Id == id))
            _selectedDrawingIds.Add(id);
        PublishDrawingSelectionChanged();
        InvalidateVisual();
    }

    public void UpdateDrawing(ChartDrawing drawing)
    {
        int index = _drawings.FindIndex(item => item.Id == drawing.Id);
        if (index < 0)
            return;
        PushDrawingUndo();
        _drawings[index] = drawing with { UpdatedAt = DateTimeOffset.UtcNow };
        if (_selectedDrawingIds.Contains(drawing.Id))
            PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Drawing updated.");
    }

    public void RemoveDrawingById(string id, bool overrideLock = false)
    {
        int index = _drawings.FindIndex(item => item.Id == id);
        if (index < 0)
            return;
        ChartDrawing drawing = _drawings[index];
        if (!overrideLock && (drawing.IsLocked || _lockAllDrawings))
            return;
        PushDrawingUndo();
        _drawings.RemoveAt(index);
        _selectedDrawingIds.Remove(id);
        PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Drawing removed.");
    }

    public void ToggleDrawingHidden(string id)
    {
        MutateDrawing(id, item => item with { IsHidden = !item.IsHidden });
    }

    public void ToggleDrawingLocked(string id)
    {
        MutateDrawing(id, item => item with { IsLocked = !item.IsLocked });
    }

    public void MoveDrawingLayer(string id, int delta)
    {
        int index = _drawings.FindIndex(item => item.Id == id);
        if (index < 0)
            return;
        PushDrawingUndo();
        int target = Math.Clamp(index + delta, 0, _drawings.Count - 1);
        ChartDrawing item = _drawings[index];
        _drawings.RemoveAt(index);
        _drawings.Insert(target, item with { ZIndex = target, UpdatedAt = DateTimeOffset.UtcNow });
        NotifyDrawingChanged("Drawing order changed.");
    }

    public void BringDrawingToFront(string id) => MoveDrawingToBoundary(id, true);
    public void SendDrawingToBack(string id) => MoveDrawingToBoundary(id, false);

    public void CloneDrawingById(string id)
    {
        ChartDrawing? drawing = _drawings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (drawing is null)
            return;
        _selectedDrawingIds.Clear();
        _selectedDrawingIds.Add(id);
        CloneSelectedDrawings();
    }

    public void CopyDrawingById(string id)
    {
        _copiedDrawing = _drawings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        DrawingStatusChanged?.Invoke(_copiedDrawing is null ? "Drawing copy failed." : "Drawing copied.");
    }

    public void RequestDrawingAlertById(string id)
    {
        ChartDrawing? drawing = _drawings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (drawing is not null)
            DrawingAlertRequested?.Invoke(drawing);
    }

    public void SetDrawingVisibilityAllIntervals(string id) =>
        MutateDrawing(id, item => item with { Visibility = new DrawingVisibility() });

    public void SetDrawingVisibilityCurrentIntervalOnly(string id) => SetCurrentTimeframeVisibility(id);

    public void SetDrawingVisibilityCurrentAndAbove(string id)
    {
        string timeframe = DrawingCandles.LastOrDefault()?.Timeframe ?? string.Empty;
        MutateDrawing(id, item => item with
        {
            Visibility = new DrawingVisibility(MinimumTimeframe: timeframe, MaximumTimeframe: string.Empty)
        });
    }

    public void SetDrawingVisibilityCurrentAndBelow(string id)
    {
        string timeframe = DrawingCandles.LastOrDefault()?.Timeframe ?? string.Empty;
        MutateDrawing(id, item => item with
        {
            Visibility = new DrawingVisibility(MinimumTimeframe: string.Empty, MaximumTimeframe: timeframe)
        });
    }

    public int GetDrawingAnchorBarIndex(DrawingAnchor anchor)
    {
        if (DrawingCandles.Count == 0)
            return 0;
        return Math.Clamp(FindNearestDrawingCandleIndex(anchor), 0, DrawingCandles.Count - 1);
    }

    public long GetDrawingBarStartUnix(int barIndex, long fallbackUnix)
    {
        if (DrawingCandles.Count == 0)
            return fallbackUnix;
        int index = Math.Clamp(barIndex, 0, DrawingCandles.Count - 1);
        return DrawingCandles[index].StartUnix;
    }

    public double? GetDrawingScreenAngle(ChartDrawing drawing)
    {
        if (drawing.Anchors.Count < 2 || !TryCreateLayout(out ChartLayout layout))
            return null;
        Point p1 = AnchorToPoint(drawing.Anchors[0], layout);
        Point p2 = AnchorToPoint(drawing.Anchors[1], layout);
        if (Math.Abs(p2.X - p1.X) < 0.5)
            return 90.0;
        return Math.Atan2(-(p2.Y - p1.Y), p2.X - p1.X) * 180.0 / Math.PI;
    }

    public ChartDrawing WithDrawingScreenAngle(ChartDrawing drawing, double angleDegrees)
    {
        if (drawing.Anchors.Count < 2 || !TryCreateLayout(out ChartLayout layout))
            return drawing;
        Point p1 = AnchorToPoint(drawing.Anchors[0], layout);
        Point p2 = AnchorToPoint(drawing.Anchors[1], layout);
        double dx = p2.X - p1.X;
        if (Math.Abs(dx) < 0.5)
            return drawing;
        double radians = Math.Clamp(angleDegrees, -89.9, 89.9) * Math.PI / 180.0;
        double y2 = p1.Y - Math.Tan(radians) * dx;
        DrawingAnchor[] anchors = drawing.Anchors.ToArray();
        anchors[1] = anchors[1] with { Price = YToPrice(y2, layout) };
        return drawing with { Anchors = anchors, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public void RefreshDrawing(string id)
    {
        int index = _drawings.FindIndex(item => item.Id == id);
        if (index < 0)
            return;
        _drawings[index] = _drawings[index] with { UpdatedAt = DateTimeOffset.UtcNow };
        InvalidateVisual();
        DrawingStatusChanged?.Invoke("Drawing refreshed against the current chart frame.");
    }

    public void UndoDrawingChange()
    {
        CompleteViewportEditHistory();
        if (_drawingUndo.Count == 0)
        {
            DrawingStatusChanged?.Invoke("Nothing to undo.");
            return;
        }

        _drawingRedo.Push(SerializeEditSnapshot());
        RestoreEditSnapshot(_drawingUndo.Pop());
        PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Undo.", pushUndo: false, clearRedo: false);
    }

    public void RedoDrawingChange()
    {
        CompleteViewportEditHistory();
        if (_drawingRedo.Count == 0)
        {
            DrawingStatusChanged?.Invoke("Nothing to redo.");
            return;
        }

        _drawingUndo.Push(SerializeEditSnapshot());
        RestoreEditSnapshot(_drawingRedo.Pop());
        PublishDrawingSelectionChanged();
        NotifyDrawingChanged("Redo.", pushUndo: false, clearRedo: false);
    }

    public DrawingTemplate SaveDrawingTemplate(string drawingId, string name, bool makeDefault)
    {
        ChartDrawing? drawing = _drawings.FirstOrDefault(item => item.Id == drawingId);
        if (drawing is null)
            throw new InvalidOperationException("Drawing not found.");
        if (makeDefault)
        {
            for (int i = 0; i < _drawingTemplates.Count; i++)
            {
                DrawingTemplate template = _drawingTemplates[i];
                if (template.ToolId == drawing.ToolId && template.IsDefault)
                    _drawingTemplates[i] = template with { IsDefault = false };
            }
        }
        var saved = new DrawingTemplate(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(name) ? $"{drawing.DisplayName} template" : name.Trim(),
            drawing.ToolId,
            drawing.Style,
            drawing.Levels,
            drawing.NumericOptions,
            drawing.TextOptions,
            makeDefault);
        _drawingTemplates.Add(saved);
        DrawingWorkspaceChanged?.Invoke();
        return saved;
    }

    public void DeleteDrawingTemplate(string templateId)
    {
        int index = _drawingTemplates.FindIndex(item => item.Id == templateId);
        if (index < 0)
            return;
        _drawingTemplates.RemoveAt(index);
        DrawingWorkspaceChanged?.Invoke();
    }

    public void SetDefaultDrawingTemplate(string templateId)
    {
        DrawingTemplate? selected = _drawingTemplates.FirstOrDefault(item => item.Id == templateId);
        if (selected is null)
            return;
        for (int i = 0; i < _drawingTemplates.Count; i++)
        {
            DrawingTemplate template = _drawingTemplates[i];
            if (template.ToolId == selected.ToolId)
                _drawingTemplates[i] = template with { IsDefault = template.Id == selected.Id };
        }
        DrawingWorkspaceChanged?.Invoke();
    }

    public void ApplyDrawingTemplate(string drawingId, string templateId)
    {
        DrawingTemplate? template = _drawingTemplates.FirstOrDefault(item => item.Id == templateId);
        if (template is null)
            return;
        MutateDrawing(drawingId, item => item.ToolId == template.ToolId
            ? item with
            {
                Style = template.Style,
                Levels = template.Levels,
                NumericOptions = template.NumericOptions,
                TextOptions = template.TextOptions
            }
            : item);
    }

    private void MutateDrawing(string id, Func<ChartDrawing, ChartDrawing> mutation)
    {
        int index = _drawings.FindIndex(item => item.Id == id);
        if (index < 0)
            return;
        PushDrawingUndo();
        _drawings[index] = mutation(_drawings[index]) with { UpdatedAt = DateTimeOffset.UtcNow };
        NotifyDrawingChanged("Drawing updated.");
    }

    private void MoveDrawingToBoundary(string id, bool front)
    {
        int index = _drawings.FindIndex(item => item.Id == id);
        if (index < 0)
            return;
        PushDrawingUndo();
        ChartDrawing item = _drawings[index];
        _drawings.RemoveAt(index);
        if (front)
            _drawings.Add(item with { ZIndex = _drawings.Count });
        else
            _drawings.Insert(0, item with { ZIndex = 0 });
        NotifyDrawingChanged(front ? "Drawing moved to front." : "Drawing moved to back.");
    }

    private void NotifyDrawingChanged(string status, bool pushUndo = false, bool clearRedo = true)
    {
        if (pushUndo)
            PushDrawingUndo();
        if (clearRedo)
            _drawingRedo.Clear();
        DrawingWorkspaceChanged?.Invoke();
        DrawingStatusChanged?.Invoke(status);
        InvalidateVisual();
    }

    private void PublishDrawingSelectionChanged() =>
        DrawingSelectionChanged?.Invoke(SelectedDrawing);

    private void PushDrawingUndo()
    {
        PushEditUndoSnapshot();
    }

    private string SerializeDrawings() => JsonSerializer.Serialize(_drawings, DrawingJsonOptions);

    private string SerializeEditSnapshot() =>
        JsonSerializer.Serialize(
            new ChartEditHistorySnapshot(_drawings.ToArray(), CaptureViewport()),
            DrawingJsonOptions);

    private void RestoreDrawingsSnapshot(string json)
    {
        IReadOnlyList<ChartDrawing>? drawings = JsonSerializer.Deserialize<IReadOnlyList<ChartDrawing>>(json, DrawingJsonOptions);
        _drawings.Clear();
        _drawings.AddRange(drawings ?? Array.Empty<ChartDrawing>());
        _selectedDrawingIds.RemoveWhere(id => _drawings.All(item => item.Id != id));
    }

    private void RestoreEditSnapshot(string json)
    {
        ChartEditHistorySnapshot? snapshot =
            JsonSerializer.Deserialize<ChartEditHistorySnapshot>(json, DrawingJsonOptions);
        if (snapshot is null)
            return;

        _restoringEditHistory = true;
        try
        {
            _drawings.Clear();
            _drawings.AddRange(snapshot.Drawings ?? Array.Empty<ChartDrawing>());
            _selectedDrawingIds.RemoveWhere(id => _drawings.All(item => item.Id != id));
            RestoreViewport(snapshot.Viewport);
            PublishViewportChanged();
        }
        finally
        {
            _restoringEditHistory = false;
        }
    }

    private void PushEditUndoSnapshot()
    {
        if (_restoringEditHistory)
            return;
        string snapshot = SerializeEditSnapshot();
        if (_drawingUndo.Count == 0 || !string.Equals(_drawingUndo.Peek(), snapshot, StringComparison.Ordinal))
            _drawingUndo.Push(snapshot);
        _drawingRedo.Clear();
    }

    public void PushViewportUndoSnapshot() => PushEditUndoSnapshot();

    public void BeginViewportEditHistory()
    {
        if (_restoringEditHistory || _pendingViewportUndoSnapshot is not null)
            return;
        _pendingViewportUndoSnapshot = SerializeEditSnapshot();
    }

    public void CompleteViewportEditHistory()
    {
        if (_pendingViewportUndoSnapshot is null)
            return;

        string before = _pendingViewportUndoSnapshot;
        _pendingViewportUndoSnapshot = null;
        string after = SerializeEditSnapshot();
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        if (_drawingUndo.Count == 0 || !string.Equals(_drawingUndo.Peek(), before, StringComparison.Ordinal))
            _drawingUndo.Push(before);
        _drawingRedo.Clear();
    }

    public void CancelViewportEditHistory() => _pendingViewportUndoSnapshot = null;

    public void CommitEditHistoryCheckpoint()
    {
        CompleteViewportEditHistory();
        _drawingUndo.Clear();
        _drawingRedo.Clear();
        _pendingViewportUndoSnapshot = null;
        DrawingStatusChanged?.Invoke("Undo history locked at this point.");
    }

    public bool CanUndoEditHistory => _drawingUndo.Count > 0;
    public bool CanRedoEditHistory => _drawingRedo.Count > 0;

    private sealed record ChartEditHistorySnapshot(
        IReadOnlyList<ChartDrawing> Drawings,
        ChartViewportState Viewport);

    private void UpdateDrawingCursor(DrawingToolDefinition tool)
    {
        Cursor = tool.Id switch
        {
            "cursor-crosshair" => Cursors.Cross,
            "cursor-dot" => Cursors.None,
            "cursor-arrow" or "selection" => Cursors.Arrow,
            "cursor-demo" or "cursor-magic" or "eraser" => Cursors.None,
            "text" or "note" or "anchored-note" or "comment" => Cursors.IBeam,
            _ => Cursors.Cross
        };
    }

    private bool HandleDrawingKeyDown(KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(_activeTableDrawingId) && _selectedDrawingIds.Contains(_activeTableDrawingId))
        {
            if (e.Key == Key.Back)
            {
                HandleTableCellBackspace();
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.Tab)
            {
                MoveActiveTableCell(nextRow: false);
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.Enter)
            {
                MoveActiveTableCell(nextRow: true);
                e.Handled = true;
                return true;
            }
        }

        if (e.Key == Key.Escape)
        {
            _activeTableDrawingId = string.Empty;
            _activeTableCellRow = -1;
            _activeTableCellColumn = -1;
            _tableCellUndoCaptured = false;
            if (_oneShotZoomInMode)
            {
                CancelOneShotZoomIn();
                e.Handled = true;
                return true;
            }
            if (CancelActiveDrawingToolOrMeasurement())
            {
                e.Handled = true;
                return true;
            }
            _selectedDrawingIds.Clear();
            PublishDrawingSelectionChanged();
            InvalidateVisual();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Enter && _workingDrawing is not null)
        {
            CompleteWorkingDrawing();
            e.Handled = true;
            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z)
        {
            UndoDrawingChange();
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            (e.Key == Key.R || e.Key == Key.Y ||
             (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.Z)))
        {
            RedoDrawingChange();
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F)
        {
            CommitEditHistoryCheckpoint();
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            _copiedDrawing = _drawings.LastOrDefault(item => _selectedDrawingIds.Contains(item.Id));
            DrawingStatusChanged?.Invoke(_copiedDrawing is null ? "No drawing selected." : "Drawing copied.");
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            PasteCopiedDrawing();
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D)
        {
            CloneSelectedDrawings();
            e.Handled = true;
            return true;
        }
        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedDrawings();
            e.Handled = true;
            return true;
        }
        if ((e.Key is Key.Left or Key.Right or Key.Up or Key.Down) && _selectedDrawingIds.Count > 0)
        {
            NudgeSelectedDrawings(e.Key);
            e.Handled = true;
            return true;
        }
        return false;
    }

    private bool HandleDrawingTextInput(string text) => HandleTableCellTextInput(text);

    private bool HandleDrawingMouseMove(Point mouse, MouseEventArgs e, ChartLayout layout)
    {
        UpdateCursorLongPress(mouse);

        if (_demonstrationCursorDrawing)
        {
            if (e.LeftButton == MouseButtonState.Pressed && layout.Plot.Contains(mouse))
            {
                AppendDemonstrationCursorSample(mouse);
                e.Handled = true;
                return true;
            }
            EndDemonstrationCursorStroke();
        }

        if (_measureDragging && _measureStartAnchor is not null)
        {
            Point constrained = ConstrainPointToPlot(mouse, layout.Plot);
            _measureEndAnchor = CreateDrawingAnchor(constrained, layout, constrain: false);
            InvalidateVisual();
            return true;
        }

        if (_freehandDrawing && _workingDrawing is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            Point raw = ConstrainPointToPlot(mouse, layout.Plot);
            DrawingToolDefinition? freehandTool = DrawingToolCatalog.Find(_workingDrawing.ToolId);
            double response = freehandTool?.Geometry == DrawingGeometryKind.Highlighter ? 0.24 : 0.34;
            Point filtered = _freehandFilteredPoint is Point previousFiltered
                ? previousFiltered + (raw - previousFiltered) * response
                : raw;
            _freehandFilteredPoint = filtered;

            if (_freehandLastAcceptedPoint is null ||
                Distance(_freehandLastAcceptedPoint.Value, filtered) >= 0.55)
            {
                DrawingAnchor anchor = CreateDrawingAnchor(filtered, layout, constrain: false);
                _workingDrawing = _workingDrawing with
                {
                    Anchors = _workingDrawing.Anchors.Append(anchor).ToArray()
                };
                _freehandLastAcceptedPoint = filtered;
            }

            InvalidateVisual();
            return true;
        }

        if (_drawingDragMode != DrawingDragMode.None && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyDrawingDrag(mouse, layout);
            InvalidateVisual();
            return true;
        }

        if (_drawingSelectionBoxStart.HasValue && e.LeftButton == MouseButtonState.Pressed)
        {
            _drawingSelectionBox = CreateNormalizedRect(_drawingSelectionBoxStart.Value, mouse);
            InvalidateVisual();
            return true;
        }

        if (_workingDrawing is not null)
        {
            _previewDrawingAnchor = CreateDrawingAnchor(mouse, layout, constrain: true);
            InvalidateVisual();
            return true;
        }
        return false;
    }

    private DrawingAnchor NormalizeConstructionAnchor(DrawingToolDefinition tool, DrawingAnchor anchor, int anchorIndex)
    {
        // TradingView's Parallel Channel stores point #3 as a price offset from
        // point #2, not as an independent time coordinate. Locking its time to
        // point #2 prevents the opposite rail from skewing/jumping when the user
        // places the third point at a different horizontal mouse position.
        if (tool.Id == "parallel-channel" && anchorIndex == 2 && _workingDrawing?.Anchors.Count >= 2)
            return WithDrawingAnchorTime(anchor, _workingDrawing.Anchors[1]);

        // TradingView Disjoint Channel: points #1-#2 establish the first
        // angle. Point #3 moves vertically only; its time is locked to point #2.
        // The rendered second rail mirrors the first rail's angle exactly.
        if (tool.Id == "disjoint-channel" && anchorIndex == 2 && _workingDrawing?.Anchors.Count >= 2)
            return WithDrawingAnchorTime(anchor, _workingDrawing.Anchors[1]);

        return anchor;
    }

    private DrawingAnchor ConstructionDisplayAnchor(DrawingToolDefinition? tool, DrawingAnchor anchor, int anchorIndex)
    {
        if (tool?.Id == "parallel-channel" && anchorIndex == 2 && _workingDrawing?.Anchors.Count >= 2)
            return WithDrawingAnchorTime(anchor, _workingDrawing.Anchors[1]);
        if (tool?.Id == "disjoint-channel" && anchorIndex == 2 && _workingDrawing?.Anchors.Count >= 2)
            return WithDrawingAnchorTime(anchor, _workingDrawing.Anchors[1]);
        return anchor;
    }

    private bool HandleDrawingMouseLeftDown(MouseButtonEventArgs e, ChartLayout layout, Point mouse)
    {
        DrawingToolDefinition tool = DrawingToolCatalog.Find(_activeDrawingToolId)
            ?? DrawingToolCatalog.Find("cursor-crosshair")!;

        bool shiftMeasureShortcut = tool.IsCursorTool &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool measurementComplete = !_measureModeArmed && !_measureDragging &&
            _measureStartAnchor is not null && _measureEndAnchor is not null;

        // A completed temporary measurement disappears on the next chart click.
        // The same click may immediately start a new Shift measurement.
        if (measurementComplete)
        {
            _measureStartAnchor = null;
            _measureEndAnchor = null;
            InvalidateVisual();
        }

        if (_measureDragging && _measureStartAnchor is not null && layout.Plot.Contains(mouse))
        {
            _measureEndAnchor = CreateDrawingAnchor(mouse, layout, constrain: false);
            _measureDragging = false;
            _measureModeArmed = false;
            MeasurementModeChanged?.Invoke(false);
            DrawingStatusChanged?.Invoke("Measurement complete. Click elsewhere to clear it, or Shift-click to start another.");
            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        if ((_measureModeArmed || shiftMeasureShortcut) && layout.Plot.Contains(mouse))
        {
            _measureStartAnchor = CreateDrawingAnchor(mouse, layout, constrain: false);
            _measureEndAnchor = _measureStartAnchor;
            _measureModeArmed = true;
            _measureDragging = true;
            MeasurementModeChanged?.Invoke(true);
            DrawingStatusChanged?.Invoke("Point A placed. Release Shift if desired, then click point B.");
            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        if (tool.IsCursorTool && layout.Plot.Contains(mouse))
        {
            if (tool.Id == "cursor-magic")
            {
                SpawnMagicCursorBurst(mouse, layout.Plot);
                e.Handled = true;
                return true;
            }

            if (tool.Id == "cursor-demo" && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                BeginDemonstrationCursorStroke(mouse);
                e.Handled = true;
                return true;
            }

            if (tool.Id is "cursor-crosshair" or "cursor-dot" or "cursor-arrow" or "selection")
                BeginCursorLongPress(mouse);
        }

        DrawingHitInfo? hit = HitTestDrawing(layout, mouse);
        if (tool.Geometry == DrawingGeometryKind.Eraser)
        {
            if (hit is DrawingHitInfo eraserHit)
                RemoveDrawingById(eraserHit.Drawing.Id);
            e.Handled = true;
            return true;
        }

        bool isDrawingTool = !tool.IsCursorTool;
        if (!isDrawingTool)
        {
            if (hit is DrawingHitInfo selectionHit)
            {
                BeginDrawingSelectionOrDrag(selectionHit, mouse, layout, e);
                e.Handled = true;
                return true;
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _drawingSelectionBoxStart = mouse;
                _drawingSelectionBox = new Rect(mouse, mouse);
                CaptureMouse();
                e.Handled = true;
                return true;
            }
            _selectedDrawingIds.Clear();
            PublishDrawingSelectionChanged();
            InvalidateVisual();
            return false;
        }

        if (!layout.Plot.Contains(mouse))
            return false;

        if (tool.Geometry is DrawingGeometryKind.Brush or DrawingGeometryKind.Highlighter)
        {
            PushDrawingUndo();
            DrawingAnchor anchor = CreateDrawingAnchor(mouse, layout, constrain: false);
            _workingDrawing = CreateNewDrawing(tool, new[] { anchor });
            _freehandDrawing = true;
            _freehandFilteredPoint = mouse;
            _freehandLastAcceptedPoint = mouse;
            CaptureMouse();
            e.Handled = true;
            return true;
        }

        // Long/Short Position parity: one chart click places a complete default
        // position. The three internal anchors are still retained so Entry,
        // Target and Stop can be dragged independently after placement.
        if (_workingDrawing is null && (tool.Id is "long-position" or "short-position"))
        {
            PushDrawingUndo();
            IReadOnlyList<DrawingAnchor> defaultAnchors = CreateDefaultPositionAnchors(tool, mouse, layout);
            _workingDrawing = CreateNewDrawing(tool, defaultAnchors);
            CompleteWorkingDrawing();
            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        DrawingAnchor next = CreateDrawingAnchor(mouse, layout, constrain: true);
        int nextAnchorIndex = _workingDrawing?.Anchors.Count ?? 0;
        next = NormalizeConstructionAnchor(tool, next, nextAnchorIndex);
        if (_workingDrawing is null)
        {
            PushDrawingUndo();
            _workingDrawing = CreateNewDrawing(tool, new[] { next });
            _previewDrawingAnchor = next;
            if (tool.MaximumAnchors == 1)
                CompleteWorkingDrawing();
        }
        else
        {
            // TradingView Curve: only the two endpoints are clicked. The middle
            // shaping handle is created automatically at the midpoint and remains
            // independently draggable after completion.
            if (tool.Id == "curve" && _workingDrawing.Anchors.Count == 1)
            {
                DrawingAnchor start = _workingDrawing.Anchors[0];
                Point startPoint = AnchorToPoint(start, layout);
                Point endPoint = AnchorToPoint(next, layout);
                Point middlePoint = new((startPoint.X + endPoint.X) * 0.5, (startPoint.Y + endPoint.Y) * 0.5);
                DrawingAnchor middle = CreateDrawingAnchor(middlePoint, layout, constrain: false);
                _workingDrawing = _workingDrawing with { Anchors = new[] { start, middle, next } };
                CompleteWorkingDrawing();
            }
            // TradingView Double Curve: two endpoint clicks complete the tool. Two
            // intermediate Bezier controls are generated automatically at 1/3 and
            // 2/3 of the endpoint span, then can be dragged independently.
            else if (tool.Id == "double-curve" && _workingDrawing.Anchors.Count == 1)
            {
                DrawingAnchor start = _workingDrawing.Anchors[0];
                Point startPoint = AnchorToPoint(start, layout);
                Point endPoint = AnchorToPoint(next, layout);
                Vector span = endPoint - startPoint;
                DrawingAnchor control1 = CreateDrawingAnchor(startPoint + span * (1.0 / 3.0), layout, constrain: false);
                DrawingAnchor control2 = CreateDrawingAnchor(startPoint + span * (2.0 / 3.0), layout, constrain: false);
                _workingDrawing = _workingDrawing with { Anchors = new[] { start, control1, control2, next } };
                CompleteWorkingDrawing();
            }
            // Ghost Feed and Path both use TradingView-style variable construction:
            // every single click commits another turning point and a double-click
            // finishes. WPF sends the second MouseDown of a double-click at the same
            // location, so never store that duplicate endpoint before completion.
            else if ((tool.Id is "ghost-feed" or "path") && tool.VariableAnchors && e.ClickCount >= 2 &&
                _workingDrawing.Anchors.Count >= tool.MinimumAnchors)
            {
                Point lastPoint = AnchorToPoint(_workingDrawing.Anchors[^1], layout);
                if (Distance(lastPoint, mouse) > 4.0)
                    _workingDrawing = _workingDrawing with { Anchors = _workingDrawing.Anchors.Append(next).ToArray() };
                CompleteWorkingDrawing();
            }
            else
            {
                IReadOnlyList<DrawingAnchor> anchors = _workingDrawing.Anchors.Append(next).ToArray();
                _workingDrawing = _workingDrawing with { Anchors = anchors };
                if (!tool.VariableAnchors && anchors.Count >= tool.MaximumAnchors)
                    CompleteWorkingDrawing();
                else if (tool.VariableAnchors && e.ClickCount >= 2 && anchors.Count >= tool.MinimumAnchors)
                    CompleteWorkingDrawing();
            }
        }
        e.Handled = true;
        InvalidateVisual();
        return true;
    }

    private bool HandleDrawingMouseLeftUp(MouseButtonEventArgs e, ChartLayout layout, Point mouse)
    {
        EndCursorLongPress();
        if (_demonstrationCursorDrawing)
        {
            if (layout.Plot.Contains(mouse))
                AppendDemonstrationCursorSample(mouse);
            EndDemonstrationCursorStroke();
            e.Handled = true;
            return true;
        }

        if (_freehandDrawing)
        {
            _freehandDrawing = false;
            ReleaseMouseCapture();
            if (_workingDrawing is not null)
            {
                Point finalPoint = ConstrainPointToPlot(mouse, layout.Plot);
                if (_freehandLastAcceptedPoint is null ||
                    Distance(_freehandLastAcceptedPoint.Value, finalPoint) >= 0.5)
                {
                    _workingDrawing = _workingDrawing with
                    {
                        Anchors = _workingDrawing.Anchors
                            .Append(CreateDrawingAnchor(finalPoint, layout, constrain: false))
                            .ToArray()
                    };
                }
            }

            if (_workingDrawing is not null && _workingDrawing.Anchors.Count >= 2)
            {
                DrawingToolDefinition? freehandTool = DrawingToolCatalog.Find(_workingDrawing.ToolId);
                bool highlighter = freehandTool?.Geometry == DrawingGeometryKind.Highlighter;
                double tolerance = highlighter ? 0.62 : 0.42;
                IReadOnlyList<DrawingAnchor> simplified =
                    SimplifyFreehandAnchors(_workingDrawing.Anchors, layout, tolerance);
                _workingDrawing = _workingDrawing with
                {
                    Anchors = SmoothFreehandAnchors(
                        simplified,
                        layout,
                        highlighter ? 5 : 4)
                };
                _freehandFilteredPoint = null;
                _freehandLastAcceptedPoint = null;
                CompleteWorkingDrawing();
            }
            else
                CancelWorkingDrawing(true);
            e.Handled = true;
            return true;
        }

        if (_drawingDragMode != DrawingDragMode.None)
        {
            _drawingDragMode = DrawingDragMode.None;
            _dragDrawingId = string.Empty;
            _dragAnchorIndex = -1;
            _dragStartAnchors = Array.Empty<DrawingAnchor>();
            _dragStartMouseAnchor = null;
            ReleaseMouseCapture();
            NotifyDrawingChanged("Drawing moved.");
            e.Handled = true;
            return true;
        }

        if (_drawingSelectionBoxStart.HasValue)
        {
            Rect selection = _drawingSelectionBox ?? new Rect(_drawingSelectionBoxStart.Value, mouse);
            _drawingSelectionBoxStart = null;
            _drawingSelectionBox = null;
            ReleaseMouseCapture();
            SelectDrawingsInBox(selection, layout);
            e.Handled = true;
            return true;
        }
        return false;
    }

    private static readonly string[] MagicCursorGlyphs =
    {
        "🍒", "🍓", "🍉", "🍍", "🍋", "🍎", "🍊", "🥝",
        "💵", "💸", "💰", "🎁", "💚", "💖", "❤️", "🌸",
        "🌺", "🌼", "🍀", "⭐", "✨", "💎", "🎀", "🟩"
    };

    private void EnsureCursorEffectsTimer()
    {
        if (_cursorEffectsTimer is null)
        {
            _cursorEffectsTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _cursorEffectsTimer.Tick += (_, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                _demonstrationCursorSamples.RemoveAll(sample => (now - sample.CreatedAt).TotalSeconds > 3.25);
                // Magic particles never evaporate while they are still falling. Keep them alive long
                // enough to leave the bottom/side of the plot naturally; the hard age cap is only a
                // safety cleanup for a particle that somehow remains off-screen.
                _magicCursorParticles.RemoveAll(particle => (now - particle.CreatedAt).TotalSeconds > 10.0);

                if (_cursorLongPressStartedAt is DateTimeOffset started &&
                    _cursorValuesTooltipOnLongPress &&
                    Mouse.LeftButton == MouseButtonState.Pressed &&
                    (now - started).TotalMilliseconds >= 520)
                {
                    _cursorValuesTooltipVisible = true;
                    if (_cursorLongPressStartPoint is Point start)
                        _cursorValuesTooltipPoint = start;
                }

                bool work = _demonstrationCursorSamples.Count > 0 ||
                            _magicCursorParticles.Count > 0 ||
                            _cursorLongPressStartedAt.HasValue ||
                            _cursorValuesTooltipVisible ||
                            _demonstrationCursorDrawing;
                if (!work)
                    _cursorEffectsTimer.Stop();
                InvalidateVisual();
            };
        }
        if (!_cursorEffectsTimer.IsEnabled)
            _cursorEffectsTimer.Start();
    }

    private void BeginDemonstrationCursorStroke(Point point)
    {
        _demonstrationCursorDrawing = true;
        _demonstrationStrokeId++;
        _lastDemonstrationPoint = null;
        AppendDemonstrationCursorSample(point);
        CaptureMouse();
        EnsureCursorEffectsTimer();
    }

    private void AppendDemonstrationCursorSample(Point point)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_lastDemonstrationPoint is not Point previous)
        {
            _demonstrationCursorSamples.Add(new DemonstrationCursorSample(point, now, _demonstrationStrokeId));
            _lastDemonstrationPoint = point;
            EnsureCursorEffectsTimer();
            InvalidateVisual();
            return;
        }

        double distance = Distance(previous, point);
        if (distance < 0.65)
            return;

        // Interpolate between sparse MouseMove events so even a very fast gesture remains
        // one continuous demonstration stroke with no circular spots and no blank gaps.
        int steps = Math.Max(1, (int)Math.Ceiling(distance / 3.0));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            Point samplePoint = new(
                previous.X + (point.X - previous.X) * t,
                previous.Y + (point.Y - previous.Y) * t);
            _demonstrationCursorSamples.Add(new DemonstrationCursorSample(samplePoint, now, _demonstrationStrokeId));
        }
        _lastDemonstrationPoint = point;
        EnsureCursorEffectsTimer();
        InvalidateVisual();
    }

    private void EndDemonstrationCursorStroke()
    {
        _demonstrationCursorDrawing = false;
        _lastDemonstrationPoint = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        EnsureCursorEffectsTimer();
        InvalidateVisual();
    }

    private void SpawnMagicCursorBurst(Point point, Rect plot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        const int count = 76;
        for (int i = 0; i < count; i++)
        {
            // Give most particles a destination across the entire visible plot so the sprinkle
            // spreads broadly before gravity brings it down. A small core stays near the wand.
            double targetX = plot.Left + _magicCursorRandom.NextDouble() * Math.Max(1.0, plot.Width);
            double travelTime = 0.70 + _magicCursorRandom.NextDouble() * 0.85;
            double xVelocity = (targetX - point.X) / travelTime + (_magicCursorRandom.NextDouble() - 0.5) * 110.0;
            double yVelocity = -430.0 + _magicCursorRandom.NextDouble() * 245.0;
            if (i < 12)
            {
                xVelocity *= 0.28;
                yVelocity *= 0.70;
            }

            var velocity = new Vector(xVelocity, yVelocity);
            var origin = new Point(
                point.X + (_magicCursorRandom.NextDouble() - 0.5) * 15.0,
                point.Y + (_magicCursorRandom.NextDouble() - 0.5) * 11.0);
            string glyph = MagicCursorGlyphs[_magicCursorRandom.Next(MagicCursorGlyphs.Length)];
            double rotation = (_magicCursorRandom.NextDouble() - 0.5) * 48.0;
            double spin = (_magicCursorRandom.NextDouble() - 0.5) * 92.0;
            double size = 18.0 + _magicCursorRandom.NextDouble() * 9.0;
            _magicCursorParticles.Add(new MagicCursorParticle(glyph, origin, velocity, rotation, spin, size, now));
        }
        EnsureCursorEffectsTimer();
        InvalidateVisual();
    }

    private void BeginCursorLongPress(Point point)
    {
        if (!_cursorValuesTooltipOnLongPress)
            return;
        _cursorLongPressStartedAt = DateTimeOffset.UtcNow;
        _cursorLongPressStartPoint = point;
        _cursorValuesTooltipVisible = false;
        _cursorValuesTooltipPoint = point;
        EnsureCursorEffectsTimer();
    }

    private void UpdateCursorLongPress(Point point)
    {
        if (_cursorLongPressStartPoint is not Point start)
            return;
        if (Distance(start, point) > 5.0)
            CancelCursorLongPress();
    }

    private void EndCursorLongPress()
    {
        _cursorLongPressStartedAt = null;
        _cursorLongPressStartPoint = null;
        _cursorValuesTooltipVisible = false;
        InvalidateVisual();
    }

    private void CancelCursorLongPress() => EndCursorLongPress();

    private void HandleCursorMouseLeave()
    {
        CancelCursorLongPress();
        if (_demonstrationCursorDrawing)
            EndDemonstrationCursorStroke();
    }

    private void DrawCursorModeOverlay(DrawingContext drawingContext, ChartLayout layout)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_demonstrationCursorSamples.Count > 0)
        {
            for (int i = 1; i < _demonstrationCursorSamples.Count; i++)
            {
                DemonstrationCursorSample previous = _demonstrationCursorSamples[i - 1];
                DemonstrationCursorSample sample = _demonstrationCursorSamples[i];
                if (sample.StrokeId != previous.StrokeId)
                    continue;

                double age = (now - sample.CreatedAt).TotalSeconds;
                double opacity = age <= 1.45 ? 0.55 : Math.Clamp((3.25 - age) / 1.80, 0.0, 1.0) * 0.55;
                if (opacity <= 0.001)
                    continue;

                var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), 235, 75, 150));
                if (brush.CanFreeze) brush.Freeze();
                var pen = new Pen(brush, 22.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    DashCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                if (pen.CanFreeze) pen.Freeze();
                drawingContext.DrawLine(pen, previous.Point, sample.Point);
            }
        }

        if (_magicCursorParticles.Count > 0)
        {
            foreach (MagicCursorParticle particle in _magicCursorParticles)
            {
                double age = Math.Max(0.0, (now - particle.CreatedAt).TotalSeconds);
                if (age > 10.0)
                    continue;

                // Opaque colour artwork: no alpha fade and no glyph-outline rendering.
                // Gravity is intentionally strong enough that the whole burst visibly falls
                // through the chart before it is cleaned up.
                double x = particle.Origin.X + particle.Velocity.X * age;
                double y = particle.Origin.Y + particle.Velocity.Y * age + 0.5 * 330.0 * age * age;
                if (x < layout.Plot.Left - 70 || x > layout.Plot.Right + 70 || y < layout.Plot.Top - 90 || y > layout.Plot.Bottom + 90)
                    continue;

                double scale = 0.88 + Math.Min(age / 0.17, 1.0) * 0.17;
                DrawMagicColourEmoji(
                    drawingContext,
                    particle.Glyph,
                    new Point(x, y),
                    particle.Size * scale,
                    particle.Rotation + particle.Spin * age);
            }
        }

        if (_mousePosition is Point mouse && layout.Plot.Contains(mouse))
        {
            if (_activeDrawingToolId == "cursor-demo")
            {
                var fill = new SolidColorBrush(Color.FromArgb(205, 238, 80, 155));
                var ring = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.2);
                drawingContext.DrawEllipse(fill, ring, mouse, 8.5, 8.5);
                drawingContext.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(130, 230, 54, 140)), 1.0), mouse, 12.0, 12.0);
            }
            else if (_activeDrawingToolId == "cursor-magic")
            {
                DrawColourMagicWandCursor(drawingContext, mouse);
            }
            else if (_activeDrawingToolId == "eraser")
            {
                drawingContext.PushTransform(new RotateTransform(-38, mouse.X, mouse.Y));
                var body = new SolidColorBrush(Color.FromRgb(249, 250, 251));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(60, 64, 70)), 1.2);
                drawingContext.DrawRoundedRectangle(body, pen, new Rect(mouse.X - 8, mouse.Y - 5, 16, 10), 1.5, 1.5);
                drawingContext.DrawLine(pen, new Point(mouse.X, mouse.Y - 5), new Point(mouse.X, mouse.Y + 5));
                drawingContext.Pop();
            }
        }

        if (_cursorValuesTooltipVisible)
            DrawCursorValuesTooltip(drawingContext, layout, _cursorValuesTooltipPoint);
    }

    private static SolidColorBrush MagicBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static readonly Brush MagicRed = MagicBrush(236, 52, 72);
    private static readonly Brush MagicDeepRed = MagicBrush(194, 35, 54);
    private static readonly Brush MagicPink = MagicBrush(244, 86, 158);
    private static readonly Brush MagicHotPink = MagicBrush(234, 48, 121);
    private static readonly Brush MagicOrange = MagicBrush(246, 137, 32);
    private static readonly Brush MagicYellow = MagicBrush(250, 200, 52);
    private static readonly Brush MagicGold = MagicBrush(237, 170, 38);
    private static readonly Brush MagicGreen = MagicBrush(47, 174, 83);
    private static readonly Brush MagicDarkGreen = MagicBrush(28, 121, 61);
    private static readonly Brush MagicLime = MagicBrush(113, 196, 75);
    private static readonly Brush MagicCyan = MagicBrush(55, 190, 222);
    private static readonly Brush MagicBrown = MagicBrush(142, 91, 54);
    private static readonly Brush MagicDark = MagicBrush(45, 47, 53);
    private static readonly Brush MagicWhite = MagicBrush(255, 255, 255);

    private static StreamGeometry MagicPolygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, true);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry MagicHeart(double s)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext c = geometry.Open())
        {
            c.BeginFigure(new Point(0, s * 0.38), true, true);
            c.BezierTo(new Point(-s * 0.72, -s * 0.05), new Point(-s * 0.58, -s * 0.58), new Point(-s * 0.20, -s * 0.48), true, true);
            c.BezierTo(new Point(-s * 0.04, -s * 0.44), new Point(0, -s * 0.25), new Point(0, -s * 0.19), true, true);
            c.BezierTo(new Point(0, -s * 0.25), new Point(s * 0.04, -s * 0.44), new Point(s * 0.20, -s * 0.48), true, true);
            c.BezierTo(new Point(s * 0.58, -s * 0.58), new Point(s * 0.72, -s * 0.05), new Point(0, s * 0.38), true, true);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry MagicStar(double outer, double inner)
    {
        Point[] pts = new Point[10];
        for (int i = 0; i < pts.Length; i++)
        {
            double radius = (i & 1) == 0 ? outer : inner;
            double a = -Math.PI / 2 + i * Math.PI / 5;
            pts[i] = new Point(Math.Cos(a) * radius, Math.Sin(a) * radius);
        }
        return MagicPolygon(pts);
    }

    private void DrawColourMagicWandCursor(DrawingContext dc, Point mouse)
    {
        dc.PushTransform(new RotateTransform(-43, mouse.X, mouse.Y));
        var handle = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(98, 69, 219), 0),
                new GradientStop(Color.FromRgb(236, 70, 169), 0.56),
                new GradientStop(Color.FromRgb(66, 181, 234), 1)
            }, new Point(0, 0), new Point(1, 1));
        if (handle.CanFreeze) handle.Freeze();
        var outline = new Pen(MagicDark, 1.2);
        dc.DrawRoundedRectangle(handle, outline, new Rect(mouse.X - 3.8, mouse.Y - 2.2, 24, 7), 3, 3);
        dc.Pop();

        Point starCenter = mouse + new Vector(16, -17);
        dc.PushTransform(new TranslateTransform(starCenter.X, starCenter.Y));
        dc.DrawGeometry(MagicYellow, new Pen(MagicGold, 0.9), MagicStar(7.2, 3.0));
        dc.Pop();
        dc.DrawEllipse(MagicCyan, null, mouse + new Vector(25, -7), 2.4, 2.4);
        dc.DrawEllipse(MagicPink, null, mouse + new Vector(7, -24), 2.0, 2.0);
        dc.DrawEllipse(MagicYellow, null, mouse + new Vector(28, -23), 1.6, 1.6);
    }

    private void DrawMagicColourEmoji(DrawingContext dc, string glyph, Point center, double size, double rotation)
    {
        // WPF's FormattedText path often renders Segoe UI Emoji as monochrome outlines.
        // Draw a compact opaque vector equivalent instead so every particle is genuinely
        // coloured and particles do not show through one another.
        double r = size * 0.48;
        dc.PushTransform(new RotateTransform(rotation, center.X, center.Y));
        dc.PushTransform(new TranslateTransform(center.X, center.Y));
        var darkPen = new Pen(MagicDark, Math.Max(0.7, size * 0.035));

        switch (glyph)
        {
            case "🍒":
                dc.DrawLine(new Pen(MagicDarkGreen, size * 0.06), new Point(-r * .25, -r * .72), new Point(0, -r * 1.18));
                dc.DrawLine(new Pen(MagicDarkGreen, size * 0.06), new Point(r * .42, -r * .55), new Point(0, -r * 1.18));
                dc.DrawEllipse(MagicRed, darkPen, new Point(-r * .35, r * .10), r * .52, r * .52);
                dc.DrawEllipse(MagicDeepRed, darkPen, new Point(r * .42, r * .16), r * .50, r * .50);
                break;
            case "🍓":
                dc.DrawEllipse(MagicRed, darkPen, new Point(0, r * .10), r * .82, r * .95);
                dc.DrawGeometry(MagicGreen, null, MagicPolygon(new Point(-r * .65,-r * .62), new Point(0,-r * 1.08), new Point(r * .65,-r * .62), new Point(0,-r * .48)));
                for (int i=0;i<6;i++) { double a=i*Math.PI/3; dc.DrawEllipse(MagicYellow,null,new Point(Math.Cos(a)*r*.45, Math.Sin(a)*r*.45+r*.13),r*.07,r*.10); }
                break;
            case "🍉":
                dc.DrawEllipse(MagicGreen, darkPen, new Point(0,0), r, r);
                dc.DrawEllipse(MagicRed, null, new Point(0,0), r*.78, r*.78);
                for (int i=0;i<5;i++){ double a=i*Math.PI*2/5; dc.DrawEllipse(MagicDark,null,new Point(Math.Cos(a)*r*.43,Math.Sin(a)*r*.43),r*.055,r*.11);}
                break;
            case "🍍":
                dc.DrawEllipse(MagicYellow, darkPen, new Point(0,r*.10), r*.74, r*.90);
                for (int i=-1;i<=1;i++) dc.DrawLine(new Pen(MagicOrange,1),new Point(-r*.55,r*(i*.28+.05)),new Point(r*.55,r*(i*.28+.05)));
                dc.DrawGeometry(MagicDarkGreen,null,MagicPolygon(new Point(-r*.50,-r*.62),new Point(-r*.18,-r*1.22),new Point(0,-r*.62),new Point(r*.18,-r*1.28),new Point(r*.48,-r*.62)));
                break;
            case "🍋":
                dc.DrawEllipse(MagicYellow, darkPen, new Point(0,0), r, r*.68);
                dc.DrawEllipse(MagicWhite, null, new Point(-r*.35,-r*.18), r*.10, r*.10);
                break;
            case "🍎":
                dc.DrawEllipse(MagicRed, darkPen, new Point(0,r*.08), r*.82, r*.78);
                dc.DrawLine(new Pen(MagicBrown,size*.055), new Point(0,-r*.58), new Point(r*.05,-r*1.0));
                dc.DrawEllipse(MagicGreen,null,new Point(r*.32,-r*.72),r*.30,r*.15);
                break;
            case "🍊":
                dc.DrawEllipse(MagicOrange, darkPen, new Point(0,0), r*.88, r*.88);
                dc.DrawEllipse(MagicGreen,null,new Point(r*.20,-r*.78),r*.25,r*.12);
                break;
            case "🥝":
                dc.DrawEllipse(MagicBrown, darkPen, new Point(0,0), r*.95, r*.95);
                dc.DrawEllipse(MagicLime, null, new Point(0,0), r*.73, r*.73);
                dc.DrawEllipse(MagicWhite, null, new Point(0,0), r*.16, r*.16);
                for (int i=0;i<8;i++){double a=i*Math.PI/4;dc.DrawEllipse(MagicDark,null,new Point(Math.Cos(a)*r*.43,Math.Sin(a)*r*.43),r*.04,r*.08);}
                break;
            case "💵": case "💸":
                dc.DrawRoundedRectangle(MagicGreen, darkPen, new Rect(-r*.95,-r*.58,r*1.9,r*1.16),r*.12,r*.12);
                dc.DrawRoundedRectangle(MagicLime, null, new Rect(-r*.72,-r*.38,r*1.44,r*.76),r*.10,r*.10);
                dc.DrawEllipse(MagicDarkGreen,null,new Point(0,0),r*.23,r*.23);
                if (glyph=="💸") { dc.DrawGeometry(MagicCyan,null,MagicPolygon(new Point(r*.70,-r*.50),new Point(r*1.25,-r*.82),new Point(r*1.08,-r*.18))); }
                break;
            case "💰":
                dc.DrawEllipse(MagicGold, darkPen, new Point(0,r*.18), r*.78, r*.88);
                dc.DrawRoundedRectangle(MagicBrown,null,new Rect(-r*.48,-r*.88,r*.96,r*.28),r*.08,r*.08);
                dc.DrawLine(new Pen(MagicDarkGreen,size*.065),new Point(0,-r*.15),new Point(0,r*.48));
                dc.DrawLine(new Pen(MagicDarkGreen,size*.065),new Point(-r*.22,r*.02),new Point(r*.22,r*.02));
                break;
            case "🎁":
                dc.DrawRoundedRectangle(MagicRed,darkPen,new Rect(-r*.88,-r*.64,r*1.76,r*1.45),r*.10,r*.10);
                dc.DrawRectangle(MagicYellow,null,new Rect(-r*.13,-r*.64,r*.26,r*1.45));
                dc.DrawRectangle(MagicYellow,null,new Rect(-r*.88,-r*.15,r*1.76,r*.25));
                dc.DrawEllipse(MagicPink,null,new Point(-r*.23,-r*.70),r*.28,r*.20);
                dc.DrawEllipse(MagicPink,null,new Point(r*.23,-r*.70),r*.28,r*.20);
                break;
            case "💚":
                dc.DrawGeometry(MagicGreen,darkPen,MagicHeart(r)); break;
            case "💖":
                dc.DrawGeometry(MagicPink,darkPen,MagicHeart(r)); dc.DrawGeometry(MagicYellow,null,MagicStar(r*.28,r*.11)); break;
            case "❤️":
                dc.DrawGeometry(MagicRed,darkPen,MagicHeart(r)); break;
            case "🌸": case "🌺": case "🌼":
                { Brush petal = glyph=="🌼"?MagicYellow:glyph=="🌺"?MagicHotPink:MagicPink;
                  for(int i=0;i<5;i++){double a=-Math.PI/2+i*Math.PI*2/5;dc.DrawEllipse(petal,darkPen,new Point(Math.Cos(a)*r*.48,Math.Sin(a)*r*.48),r*.38,r*.28);}
                  dc.DrawEllipse(MagicYellow,darkPen,new Point(0,0),r*.28,r*.28); }
                break;
            case "🍀":
                dc.DrawEllipse(MagicGreen,darkPen,new Point(-r*.30,-r*.25),r*.42,r*.42);
                dc.DrawEllipse(MagicGreen,darkPen,new Point(r*.30,-r*.25),r*.42,r*.42);
                dc.DrawEllipse(MagicGreen,darkPen,new Point(-r*.30,r*.30),r*.42,r*.42);
                dc.DrawEllipse(MagicGreen,darkPen,new Point(r*.30,r*.30),r*.42,r*.42);
                dc.DrawLine(new Pen(MagicDarkGreen,size*.055),new Point(0,r*.45),new Point(r*.40,r*1.05)); break;
            case "⭐":
                dc.DrawGeometry(MagicYellow,darkPen,MagicStar(r,r*.44)); break;
            case "✨":
                dc.DrawGeometry(MagicYellow,darkPen,MagicStar(r*.85,r*.22));
                dc.DrawGeometry(MagicCyan,null,MagicStar(r*.32,r*.08)); break;
            case "💎":
                dc.DrawGeometry(MagicCyan,darkPen,MagicPolygon(new Point(0,r),new Point(-r*.95,-r*.20),new Point(-r*.52,-r*.86),new Point(r*.52,-r*.86),new Point(r*.95,-r*.20)));
                dc.DrawLine(new Pen(MagicWhite,1),new Point(-r*.50,-r*.72),new Point(0,r*.72)); break;
            case "🎀":
                dc.DrawGeometry(MagicPink,darkPen,MagicPolygon(new Point(-r*.10,0),new Point(-r*.95,-r*.62),new Point(-r*.82,r*.55),new Point(-r*.08,r*.22)));
                dc.DrawGeometry(MagicPink,darkPen,MagicPolygon(new Point(r*.10,0),new Point(r*.95,-r*.62),new Point(r*.82,r*.55),new Point(r*.08,r*.22)));
                dc.DrawEllipse(MagicHotPink,darkPen,new Point(0,0),r*.25,r*.25); break;
            default:
                dc.DrawRoundedRectangle(MagicGreen,darkPen,new Rect(-r*.75,-r*.75,r*1.5,r*1.5),r*.12,r*.12);
                break;
        }
        dc.Pop();
        dc.Pop();
    }

    private void DrawCursorValuesTooltip(DrawingContext drawingContext, ChartLayout layout, Point point)
    {
        int? index = HitTestCandle(layout, point.X);
        if (index is null || index.Value < 0 || index.Value >= DrawingCandles.Count)
            return;
        Candle candle = DrawingCandles[index.Value];
        int digits = Math.Clamp(candle.Digits, 0, 10);
        string F(double value) => value.ToString($"F{digits}", CultureInfo.InvariantCulture);
        double change = candle.Close - candle.Open;
        double pct = Math.Abs(candle.Open) > 1e-15 ? change / candle.Open * 100.0 : 0.0;
        string[] lines =
        {
            candle.StartTime.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss", CultureInfo.CurrentCulture),
            $"O  {F(candle.Open)}    H  {F(candle.High)}",
            $"L  {F(candle.Low)}    C  {F(candle.Close)}",
            $"Change  {change:+0.########;-0.########;0}  ({pct:+0.##;-0.##;0}%)",
            $"Volume  {candle.TickVolume:N0}"
        };
        var texts = lines.Select((line, i) => CreateText(line, i == 0 ? 11.2 : 10.7, new SolidColorBrush(Color.FromRgb(30, 32, 36)))).ToArray();
        double width = Math.Max(205.0, texts.Max(t => t.Width) + 22.0);
        double height = texts.Sum(t => t.Height) + 16.0;
        double left = Math.Clamp(point.X + 14.0, layout.Plot.Left + 4.0, Math.Max(layout.Plot.Left + 4.0, layout.Plot.Right - width - 4.0));
        double top = Math.Clamp(point.Y + 14.0, layout.Plot.Top + 4.0, Math.Max(layout.Plot.Top + 4.0, layout.Plot.Bottom - height - 4.0));
        var rect = new Rect(left, top, width, height);
        var bg = new SolidColorBrush(Color.FromArgb(248, 255, 255, 255));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(207, 211, 218)), 1.0);
        drawingContext.DrawRoundedRectangle(bg, border, rect, 4, 4);
        double y = top + 8;
        foreach (FormattedText text in texts)
        {
            drawingContext.DrawText(text, new Point(left + 11, y));
            y += text.Height;
        }
    }

    private bool TryOpenDrawingContextMenu(MouseButtonEventArgs e, ChartLayout layout, Point click)
    {
        DrawingHitInfo? hit = HitTestDrawing(layout, click);
        if (hit is not DrawingHitInfo contextHit)
            return false;

        if (!_selectedDrawingIds.Contains(contextHit.Drawing.Id))
        {
            _selectedDrawingIds.Clear();
            _selectedDrawingIds.Add(contextHit.Drawing.Id);
            PublishDrawingSelectionChanged();
        }
        ContextMenu menu = BuildDrawingContextMenu(contextHit.Drawing);
        ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
        InvalidateVisual();
        return true;
    }

    private void AppendDrawingChartContextMenu(ContextMenu menu)
    {
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Paste drawing", PasteCopiedDrawing, _copiedDrawing is not null));
        menu.Items.Add(Item("Object Tree", () => DrawingObjectTreeRequested?.Invoke()));

        var favoritesTabs = new MenuItem { Header = "Favorites Tabs" };
        favoritesTabs.Items.Add(Item("On", () => DrawingFavoritesProjectionRequested?.Invoke(true)));
        favoritesTabs.Items.Add(Item("Off", () => DrawingFavoritesProjectionRequested?.Invoke(false)));
        menu.Items.Add(favoritesTabs);

        var magnet = new MenuItem { Header = $"Magnet ({_drawingMagnetMode})" };
        foreach (DrawingMagnetMode mode in Enum.GetValues<DrawingMagnetMode>())
        {
            DrawingMagnetMode captured = mode;
            var item = Item(mode.ToString(), () => SetDrawingMagnetMode(captured));
            item.IsCheckable = true;
            item.IsChecked = _drawingMagnetMode == mode;
            magnet.Items.Add(item);
        }
        menu.Items.Add(magnet);

        var stay = Item("Stay in drawing mode", () => SetStayInDrawingMode(!_stayInDrawingMode));
        stay.IsCheckable = true;
        stay.IsChecked = _stayInDrawingMode;
        menu.Items.Add(stay);

        var lockAll = Item("Lock all drawings", () => SetLockAllDrawings(!_lockAllDrawings));
        lockAll.IsCheckable = true;
        lockAll.IsChecked = _lockAllDrawings;
        menu.Items.Add(lockAll);

        var hideAll = Item("Hide all drawings", () => SetHideAllDrawings(!_hideAllDrawings));
        hideAll.IsCheckable = true;
        hideAll.IsChecked = _hideAllDrawings;
        menu.Items.Add(hideAll);

        var remove = new MenuItem { Header = "Remove" };
        remove.Items.Add(Item("Selected drawings", DeleteSelectedDrawings, _selectedDrawingIds.Count > 0));
        remove.Items.Add(Item("All unlocked drawings", () => ClearAllDrawings(false), _drawings.Any(item => !item.IsLocked)));
        remove.Items.Add(Item("All drawings including locked", () => ClearAllDrawings(true), _drawings.Count > 0));
        menu.Items.Add(remove);
    }

    private ContextMenu BuildDrawingContextMenu(ChartDrawing drawing)
    {
        if (DrawingToolCatalog.Find(drawing.ToolId)?.Category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann)
            return BuildReferenceLineObjectContextMenu(drawing);

        var menu = new ContextMenu();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item("Settings…", () => DrawingSettingsRequested?.Invoke(drawing)));
        if (DrawingToolCatalog.Find(drawing.ToolId)?.SupportsText == true)
            menu.Items.Add(Item("Edit text…", () => DrawingSettingsRequested?.Invoke(drawing)));
        menu.Items.Add(Item("Coordinates…", () => DrawingSettingsRequested?.Invoke(drawing)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Clone", CloneSelectedDrawings));
        menu.Items.Add(Item("Copy", () => _copiedDrawing = drawing));
        menu.Items.Add(Item("Paste", PasteCopiedDrawing, _copiedDrawing is not null));
        menu.Items.Add(Item("Add alert…", () => DrawingAlertRequested?.Invoke(drawing)));
        menu.Items.Add(Item(IsDrawingFavorite(drawing.ToolId) ? "Remove tool from favorites" : "Add tool to favorites",
            () => ToggleDrawingFavorite(drawing.ToolId)));

        var templateMenu = new MenuItem { Header = "Templates" };
        templateMenu.Items.Add(Item("Save as template…", () => DrawingSettingsRequested?.Invoke(drawing)));
        DrawingTemplate[] templates = _drawingTemplates.Where(item => item.ToolId == drawing.ToolId).ToArray();
        if (templates.Length > 0)
        {
            templateMenu.Items.Add(new Separator());
            foreach (DrawingTemplate template in templates)
                templateMenu.Items.Add(Item(template.Name, () => ApplyDrawingTemplate(drawing.Id, template.Id)));
        }
        menu.Items.Add(templateMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(string.IsNullOrWhiteSpace(drawing.Name) ? "Rename…" : $"Rename ({drawing.Name})…",
            () => DrawingSettingsRequested?.Invoke(drawing)));
        menu.Items.Add(Item(drawing.IsLocked ? "Unlock" : "Lock", () => ToggleDrawingLocked(drawing.Id)));
        menu.Items.Add(Item(drawing.IsHidden ? "Show" : "Hide", () => ToggleDrawingHidden(drawing.Id)));
        menu.Items.Add(Item(drawing.VisualLayer == DrawingVisualLayer.BelowCandles ? "Remove from background" : "Place as background",
            () => MutateDrawing(drawing.Id, item => item with
            {
                VisualLayer = item.VisualLayer == DrawingVisualLayer.BelowCandles
                    ? DrawingVisualLayer.AboveCandles
                    : DrawingVisualLayer.BelowCandles
            })));

        var visibility = new MenuItem { Header = "Interval visibility" };
        visibility.Items.Add(Item("All intervals", () => MutateDrawing(drawing.Id, item => item with { Visibility = new DrawingVisibility() })));
        visibility.Items.Add(Item("Current interval only", () => SetCurrentTimeframeVisibility(drawing.Id)));
        visibility.Items.Add(Item("Customize…", () => DrawingSettingsRequested?.Invoke(drawing)));
        menu.Items.Add(visibility);

        var order = new MenuItem { Header = "Visual order" };
        order.Items.Add(Item("Bring to front", () => BringDrawingToFront(drawing.Id)));
        order.Items.Add(Item("Bring forward", () => MoveDrawingLayer(drawing.Id, 1)));
        order.Items.Add(Item("Send backward", () => MoveDrawingLayer(drawing.Id, -1)));
        order.Items.Add(Item("Send to back", () => SendDrawingToBack(drawing.Id)));
        order.Items.Add(new Separator());
        order.Items.Add(Item("Below candles", () => MutateDrawing(drawing.Id, item => item with { VisualLayer = DrawingVisualLayer.BelowCandles })));
        order.Items.Add(Item("Above candles", () => MutateDrawing(drawing.Id, item => item with { VisualLayer = DrawingVisualLayer.AboveCandles })));
        order.Items.Add(Item("Top drawing layer", () => MutateDrawing(drawing.Id, item => item with { VisualLayer = DrawingVisualLayer.AboveIndicators })));
        menu.Items.Add(order);

        var group = new MenuItem { Header = "Group" };
        group.Items.Add(Item("Group selected", GroupSelectedDrawings, _selectedDrawingIds.Count > 1));
        group.Items.Add(Item("Ungroup", () => UngroupDrawing(drawing.Id), !string.IsNullOrWhiteSpace(drawing.GroupId)));
        menu.Items.Add(group);

        menu.Items.Add(Item("Open in Object Tree", () => DrawingObjectTreeRequested?.Invoke()));

        var sync = new MenuItem { Header = "Synchronize" };
        foreach (DrawingSyncMode mode in Enum.GetValues<DrawingSyncMode>())
        {
            DrawingSyncMode captured = mode;
            sync.Items.Add(Item(mode.ToString(), () => MutateDrawing(drawing.Id, item => item with { SyncMode = captured })));
        }
        menu.Items.Add(sync);
        menu.Items.Add(Item("Refresh", () => RefreshDrawing(drawing.Id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove", () => RemoveDrawingById(drawing.Id), !drawing.IsLocked && !_lockAllDrawings));
        return menu;
    }

    private ContextMenu BuildReferenceLineObjectContextMenu(ChartDrawing drawing)
    {
        var menu = new ContextMenu
        {
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 40, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 221, 227)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3)
        };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(37, 40, 46)),
                Padding = new Thickness(10, 5, 10, 5)
            };
            item.Click += (_, _) => action();
            return item;
        }

        var order = new MenuItem { Header = "Visual order", Background = Brushes.White, Foreground = menu.Foreground };
        order.Items.Add(Item("Bring to front", () => BringDrawingToFront(drawing.Id)));
        order.Items.Add(Item("Bring forward", () => MoveDrawingLayer(drawing.Id, 1)));
        order.Items.Add(Item("Send backward", () => MoveDrawingLayer(drawing.Id, -1)));
        order.Items.Add(Item("Send to back", () => SendDrawingToBack(drawing.Id)));
        menu.Items.Add(order);

        var intervals = new MenuItem { Header = "Visibility on intervals", Background = Brushes.White, Foreground = menu.Foreground };
        intervals.Items.Add(Item("Current interval and above", () => SetDrawingVisibilityCurrentAndAbove(drawing.Id)));
        intervals.Items.Add(Item("Current interval and below", () => SetDrawingVisibilityCurrentAndBelow(drawing.Id)));
        intervals.Items.Add(Item("Current interval only", () => SetDrawingVisibilityCurrentIntervalOnly(drawing.Id)));
        intervals.Items.Add(Item("All intervals", () => SetDrawingVisibilityAllIntervals(drawing.Id)));
        menu.Items.Add(intervals);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Clone", () => CloneDrawingById(drawing.Id)));
        menu.Items.Add(Item("Copy", () => CopyDrawingById(drawing.Id)));
        menu.Items.Add(Item(drawing.IsHidden ? "Show" : "Hide", () => ToggleDrawingHidden(drawing.Id)));
        menu.Items.Add(Item(drawing.VisualLayer == DrawingVisualLayer.BelowCandles ? "Remove from background" : "Place as background",
            () => MutateDrawing(drawing.Id, item => item with
            {
                VisualLayer = item.VisualLayer == DrawingVisualLayer.BelowCandles
                    ? DrawingVisualLayer.AboveCandles
                    : DrawingVisualLayer.BelowCandles
            })));
        return menu;
    }

    private void SetCurrentTimeframeVisibility(string id)
    {
        string timeframe = DrawingCandles.LastOrDefault()?.Timeframe ?? string.Empty;
        DrawingVisibility visibility = TimeframeBucket(timeframe) switch
        {
            "Seconds" => new DrawingVisibility(false, false, false, false, false, false) with { Seconds = true },
            "Minutes" => new DrawingVisibility(false, false, false, false, false, false) with { Minutes = true },
            "Hours" => new DrawingVisibility(false, false, false, false, false, false) with { Hours = true },
            "Daily" => new DrawingVisibility(false, false, false, false, false, false) with { Daily = true },
            "Weekly" => new DrawingVisibility(false, false, false, false, false, false) with { Weekly = true },
            "Monthly" => new DrawingVisibility(false, false, false, false, false, false) with { Monthly = true },
            _ => new DrawingVisibility()
        };
        MutateDrawing(id, item => item with { Visibility = visibility });
    }

    private static string TimeframeBucket(string timeframe)
    {
        string value = timeframe.ToUpperInvariant();
        if (value.Contains("TICK")) return "Ticks";
        if (value.Contains("RANGE")) return "Ranges";
        if (value.Contains("MN") || value.Contains("MONTH")) return "Monthly";
        if (value.Contains("W1") || value.Contains("WEEK")) return "Weekly";
        if (value.Contains("D1") || value.Contains("DAY")) return "Daily";
        if (value.Contains("H") || value.Contains("HOUR")) return "Hours";
        if (value.Contains("S") || value.Contains("SECOND")) return "Seconds";
        return "Minutes";
    }

    private static bool DrawingVisibilityOption(ChartDrawing drawing, string key, bool fallback) =>
        drawing.NumericOptions.TryGetValue(key, out double value) ? value >= 0.5 : fallback;

    private static bool IsWithinDrawingVisibilityBucketRange(ChartDrawing drawing, string timeframe, string bucket)
    {
        string? keyPrefix = bucket switch
        {
            "Seconds" => "VisibilitySeconds",
            "Minutes" => "VisibilityMinutes",
            "Hours" => "VisibilityHours",
            "Daily" => "VisibilityDays",
            "Weekly" => "VisibilityWeeks",
            "Monthly" => "VisibilityMonths",
            _ => null
        };
        if (keyPrefix is null) return true;
        double seconds = DrawingTimeframeSeconds(timeframe);
        if (seconds <= 0) return true;
        double unitSeconds = bucket switch
        {
            "Seconds" => 1.0,
            "Minutes" => 60.0,
            "Hours" => 3600.0,
            "Daily" => 86400.0,
            "Weekly" => 7.0 * 86400.0,
            "Monthly" => 30.0 * 86400.0,
            _ => 1.0
        };
        double quantity = seconds / unitSeconds;
        double minimum = drawing.NumericOptions.TryGetValue(keyPrefix + "Min", out double minValue) ? minValue : 1.0;
        double maximum = drawing.NumericOptions.TryGetValue(keyPrefix + "Max", out double maxValue) ? maxValue : double.MaxValue;
        return quantity + 1e-9 >= Math.Max(1.0, minimum) && quantity <= Math.Max(Math.Max(1.0, minimum), maximum) + 1e-9;
    }

    private bool TryActivateTableCell(ChartDrawing drawing, Point mouse, ChartLayout layout)
    {
        if (drawing.ToolId != "table" || drawing.Anchors.Count < 2)
            return false;
        Point a = AnchorToPoint(drawing.Anchors[0], layout);
        Point b = AnchorToPoint(drawing.Anchors[1], layout);
        Rect rect = CreateNormalizedRect(a, b);
        if (!rect.Contains(mouse) || rect.Width < 8 || rect.Height < 8)
            return false;

        // Borders/corner handles remain drag targets for moving/resizing the table.
        const double borderBand = 6.0;
        if (Math.Abs(mouse.X - rect.Left) <= borderBand || Math.Abs(mouse.X - rect.Right) <= borderBand ||
            Math.Abs(mouse.Y - rect.Top) <= borderBand || Math.Abs(mouse.Y - rect.Bottom) <= borderBand)
            return false;

        string[][] cells = ParseParityTableCells(drawing.Text);
        int rows = Math.Max(1, cells.Length);
        int columns = Math.Max(1, cells.Max(row => row.Length));
        int row = Math.Clamp((int)((mouse.Y - rect.Top) / Math.Max(1, rect.Height) * rows), 0, rows - 1);
        int column = Math.Clamp((int)((mouse.X - rect.Left) / Math.Max(1, rect.Width) * columns), 0, columns - 1);
        _activeTableDrawingId = drawing.Id;
        _activeTableCellRow = row;
        _activeTableCellColumn = column;
        _tableCellUndoCaptured = false;
        Focus();
        DrawingStatusChanged?.Invoke($"Table cell {row + 1},{column + 1} selected — type to edit.");
        InvalidateVisual();
        return true;
    }

    private bool HandleTableCellTextInput(string text)
    {
        if (string.IsNullOrEmpty(_activeTableDrawingId) || _activeTableCellRow < 0 || _activeTableCellColumn < 0 || string.IsNullOrEmpty(text))
            return false;
        int index = _drawings.FindIndex(item => item.Id == _activeTableDrawingId && item.ToolId == "table");
        if (index < 0 || !_selectedDrawingIds.Contains(_activeTableDrawingId))
            return false;
        if (!_tableCellUndoCaptured)
        {
            PushDrawingUndo();
            _tableCellUndoCaptured = true;
        }
        ChartDrawing drawing = _drawings[index];
        string[][] cells = ParseParityTableCells(drawing.Text);
        if (_activeTableCellRow >= cells.Length || _activeTableCellColumn >= cells[_activeTableCellRow].Length)
            return false;
        cells[_activeTableCellRow][_activeTableCellColumn] += text;
        _drawings[index] = drawing with { Text = SerializeParityTableCells(cells), UpdatedAt = DateTimeOffset.UtcNow };
        NotifyDrawingChanged("Table cell edited.", clearRedo: true);
        return true;
    }

    private bool HandleTableCellBackspace()
    {
        if (string.IsNullOrEmpty(_activeTableDrawingId) || _activeTableCellRow < 0 || _activeTableCellColumn < 0)
            return false;
        int index = _drawings.FindIndex(item => item.Id == _activeTableDrawingId && item.ToolId == "table");
        if (index < 0 || !_selectedDrawingIds.Contains(_activeTableDrawingId))
            return false;
        ChartDrawing drawing = _drawings[index];
        string[][] cells = ParseParityTableCells(drawing.Text);
        if (_activeTableCellRow >= cells.Length || _activeTableCellColumn >= cells[_activeTableCellRow].Length)
            return false;
        string current = cells[_activeTableCellRow][_activeTableCellColumn];
        if (current.Length == 0) return true;
        if (!_tableCellUndoCaptured)
        {
            PushDrawingUndo();
            _tableCellUndoCaptured = true;
        }
        cells[_activeTableCellRow][_activeTableCellColumn] = current[..^1];
        _drawings[index] = drawing with { Text = SerializeParityTableCells(cells), UpdatedAt = DateTimeOffset.UtcNow };
        NotifyDrawingChanged("Table cell edited.", clearRedo: true);
        return true;
    }

    private bool MoveActiveTableCell(bool nextRow)
    {
        if (string.IsNullOrEmpty(_activeTableDrawingId)) return false;
        ChartDrawing? drawing = _drawings.FirstOrDefault(item => item.Id == _activeTableDrawingId && item.ToolId == "table");
        if (drawing is null) return false;
        string[][] cells = ParseParityTableCells(drawing.Text);
        int rows = Math.Max(1, cells.Length);
        int columns = Math.Max(1, cells.Max(row => row.Length));
        if (nextRow)
        {
            _activeTableCellRow = (_activeTableCellRow + 1 + rows) % rows;
        }
        else
        {
            int flat = Math.Max(0, _activeTableCellRow) * columns + Math.Max(0, _activeTableCellColumn);
            flat = (flat + 1) % (rows * columns);
            _activeTableCellRow = flat / columns;
            _activeTableCellColumn = flat % columns;
        }
        _tableCellUndoCaptured = false;
        InvalidateVisual();
        return true;
    }

    public void AddSelectedTableRow()
    {
        ChartDrawing? drawing = SelectedDrawing;
        if (drawing is null || drawing.ToolId != "table") return;
        MutateDrawing(drawing.Id, item =>
        {
            string[][] cells = ParseParityTableCells(item.Text);
            int columns = Math.Max(1, cells.Max(row => row.Length));
            var list = cells.Select(row => row.ToArray()).ToList();
            list.Add(Enumerable.Repeat(string.Empty, columns).ToArray());
            return item with { Text = SerializeParityTableCells(list.ToArray()) };
        });
        _activeTableDrawingId = drawing.Id;
        _activeTableCellRow = Math.Max(0, ParseParityTableCells(SelectedDrawing?.Text ?? string.Empty).Length - 1);
        _activeTableCellColumn = 0;
        _tableCellUndoCaptured = false;
    }

    public void AddSelectedTableColumn()
    {
        ChartDrawing? drawing = SelectedDrawing;
        if (drawing is null || drawing.ToolId != "table") return;
        MutateDrawing(drawing.Id, item =>
        {
            string[][] cells = ParseParityTableCells(item.Text);
            string[][] expanded = cells.Select(row => row.Concat(new[] { string.Empty }).ToArray()).ToArray();
            return item with { Text = SerializeParityTableCells(expanded) };
        });
        _activeTableDrawingId = drawing.Id;
        _activeTableCellRow = Math.Max(0, _activeTableCellRow);
        _activeTableCellColumn = ParseParityTableCells(SelectedDrawing?.Text ?? string.Empty).Max(row => row.Length) - 1;
        _tableCellUndoCaptured = false;
    }

    private void BeginDrawingSelectionOrDrag(DrawingHitInfo hit, Point mouse, ChartLayout layout, MouseButtonEventArgs e)
    {
        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (additive && _selectedDrawingIds.Contains(hit.Drawing.Id))
            _selectedDrawingIds.Remove(hit.Drawing.Id);
        else
        {
            if (!additive)
                _selectedDrawingIds.Clear();
            _selectedDrawingIds.Add(hit.Drawing.Id);
        }
        PublishDrawingSelectionChanged();
        Focus();

        if (hit.Drawing.ToolId != "table")
        {
            _activeTableDrawingId = string.Empty;
            _activeTableCellRow = -1;
            _activeTableCellColumn = -1;
            _tableCellUndoCaptured = false;
        }
        else if (hit.AnchorIndex < 0 && TryActivateTableCell(hit.Drawing, mouse, layout))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            DrawingSettingsRequested?.Invoke(hit.Drawing);
            return;
        }

        if (hit.Drawing.IsLocked || _lockAllDrawings)
            return;

        PushDrawingUndo();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _selectedDrawingIds.Count == 1)
        {
            ChartDrawing clone = CloneDrawing(hit.Drawing);
            _drawings.Add(clone);
            _selectedDrawingIds.Clear();
            _selectedDrawingIds.Add(clone.Id);
            hit = hit with { Drawing = clone };
            PublishDrawingSelectionChanged();
        }

        _drawingDragMode = hit.AnchorIndex >= 0 ? DrawingDragMode.Anchor : DrawingDragMode.Body;
        _dragDrawingId = hit.Drawing.Id;
        _dragAnchorIndex = hit.AnchorIndex;
        _dragStartAnchors = hit.Drawing.Anchors.ToArray();
        _dragStartMouseAnchor = CreateDrawingAnchor(mouse, layout, constrain: false);
        _dragStartMediaScale = GetDrawingMediaScale(hit.Drawing);
        CaptureMouse();
    }

    private void ApplyDrawingDrag(Point mouse, ChartLayout layout)
    {
        int index = _drawings.FindIndex(item => item.Id == _dragDrawingId);
        if (index < 0 || _dragStartMouseAnchor is null)
            return;
        ChartDrawing drawing = _drawings[index];
        Point dragMouse = ConstrainPointToPlot(mouse, layout.Plot);

        // For whole-object drags, constrain finite visual bounds as a unit so the
        // object itself (not merely the pointer) cannot be dragged into the right
        // price scale or bottom time scale. Infinite/full-plot tools remain movable
        // and are visually protected by the plot clip in OnRender.
        if (_drawingDragMode == DrawingDragMode.Body &&
            drawing.ToolId != "bars-pattern" && _dragStartAnchors.Count > 0)
        {
            ChartDrawing startDrawing = drawing with { Anchors = _dragStartAnchors.ToArray() };
            Rect startBounds = GetDrawingBounds(startDrawing, layout);
            if (!startBounds.IsEmpty &&
                startBounds.Width < layout.Plot.Width - 1.0 &&
                startBounds.Height < layout.Plot.Height - 1.0)
            {
                Point startMouse = AnchorToPoint(_dragStartMouseAnchor, layout);
                double dx = dragMouse.X - startMouse.X;
                double dy = dragMouse.Y - startMouse.Y;
                dx = Math.Clamp(dx, layout.Plot.Left - startBounds.Left, layout.Plot.Right - startBounds.Right);
                dy = Math.Clamp(dy, layout.Plot.Top - startBounds.Top, layout.Plot.Bottom - startBounds.Bottom);
                dragMouse = new Point(startMouse.X + dx, startMouse.Y + dy);
            }
        }

        DrawingAnchor current = CreateDrawingAnchor(dragMouse, layout, constrain: false);

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 400 and <= 407 &&
            DrawingToolCatalog.Find(drawing.ToolId)?.Geometry == DrawingGeometryKind.Rectangle &&
            _dragStartAnchors.Count >= 2)
        {
            Point first = AnchorToPoint(_dragStartAnchors[0], layout);
            Point second = AnchorToPoint(_dragStartAnchors[1], layout);
            Rect rect = CreateNormalizedRect(first, second);
            Point currentPoint = dragMouse;
            Point a;
            Point b;
            switch (_dragAnchorIndex - 400)
            {
                case 0: // top-left, bottom-right fixed
                    a = currentPoint; b = rect.BottomRight; break;
                case 1: // top-right, bottom-left fixed
                    a = rect.BottomLeft; b = currentPoint; break;
                case 2: // bottom-right, top-left fixed
                    a = rect.TopLeft; b = currentPoint; break;
                case 3: // bottom-left, top-right fixed
                    a = currentPoint; b = rect.TopRight; break;
                case 4: // top wall: vertical resize only
                    a = new Point(rect.Left, currentPoint.Y); b = rect.BottomRight; break;
                case 5: // right wall: horizontal resize only
                    a = rect.TopLeft; b = new Point(currentPoint.X, rect.Bottom); break;
                case 6: // bottom wall: vertical resize only
                    a = rect.TopLeft; b = new Point(rect.Right, currentPoint.Y); break;
                default: // left wall: horizontal resize only
                    a = new Point(currentPoint.X, rect.Top); b = rect.BottomRight; break;
            }
            Rect candidate = CreateNormalizedRect(a, b);
            if (candidate.Width >= 8.0 && candidate.Height >= 8.0)
            {
                _drawings[index] = drawing with
                {
                    Anchors = new[]
                    {
                        CreateDrawingAnchor(a, layout, constrain: false),
                        CreateDrawingAnchor(b, layout, constrain: false)
                    },
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            }
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 500 and <= 507 &&
            DrawingToolCatalog.Find(drawing.ToolId)?.Geometry == DrawingGeometryKind.RotatedRectangle &&
            _dragStartAnchors.Count >= 3)
        {
            Point[] startPoints = _dragStartAnchors.Select(anchor => AnchorToPoint(anchor, layout)).ToArray();
            if (!TryGetRotatedRectangleGeometry(startPoints, out Point[] corners, out Point[] handlePoints,
                    out Vector axisX, out Vector axisY, out double width, out double height))
                return;

            Point c0 = corners[0];
            Point c1 = corners[1];
            Point c2 = corners[2];
            Point c3 = corners[3];
            Point p0 = c0;
            Point p1 = c1;
            Point p2 = c3;
            int handle = _dragAnchorIndex - 500;
            Point m = dragMouse;

            switch (handle)
            {
                case 0: // c0, c2 fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(c2 - m, axisX));
                    double newHeight = PreserveSignedSize(Vector.Multiply(c2 - m, axisY), height);
                    p0 = c2 - axisX * newWidth - axisY * newHeight;
                    p1 = p0 + axisX * newWidth;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                case 1: // c1, c3 fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(m - c3, axisX));
                    double newHeight = PreserveSignedSize(Vector.Multiply(c3 - m, axisY), height);
                    p0 = c3 - axisY * newHeight;
                    p1 = p0 + axisX * newWidth;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                case 2: // c2, c0 fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(m - c0, axisX));
                    double newHeight = PreserveSignedSize(Vector.Multiply(m - c0, axisY), height);
                    p0 = c0;
                    p1 = p0 + axisX * newWidth;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                case 3: // c3, c1 fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(c1 - m, axisX));
                    double newHeight = PreserveSignedSize(Vector.Multiply(m - c1, axisY), height);
                    p0 = c1 - axisX * newWidth;
                    p1 = c1;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                case 4: // base/top wall: perpendicular only; opposite wall fixed
                {
                    double delta = Vector.Multiply(m - handlePoints[4], axisY);
                    double newHeight = PreserveSignedSize(height - delta, height);
                    p0 = c3 - axisY * newHeight;
                    p1 = p0 + axisX * width;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                case 5: // right wall: local horizontal only; left wall fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(m - c0, axisX));
                    p0 = c0;
                    p1 = p0 + axisX * newWidth;
                    p2 = p0 + axisY * height;
                    break;
                }
                case 6: // opposite/bottom wall: perpendicular only; base wall fixed
                {
                    double newHeight = PreserveSignedSize(Vector.Multiply(m - handlePoints[4], axisY), height);
                    p0 = c0;
                    p1 = c1;
                    p2 = p0 + axisY * newHeight;
                    break;
                }
                default: // left wall: local horizontal only; right wall fixed
                {
                    double newWidth = PreservePositiveSize(Vector.Multiply(c1 - m, axisX));
                    p0 = c1 - axisX * newWidth;
                    p1 = c1;
                    p2 = p0 + axisY * height;
                    break;
                }
            }

            Point candidateC2 = p1 + (p2 - p0);
            Point[] candidateCorners = { p0, p1, candidateC2, p2 };
            if (!CornersStayInsidePlot(candidateCorners, layout.Plot))
                return;

            _drawings[index] = drawing with
            {
                Anchors = new[]
                {
                    CreateDrawingAnchor(p0, layout, constrain: false),
                    CreateDrawingAnchor(p1, layout, constrain: false),
                    CreateDrawingAnchor(p2, layout, constrain: false)
                },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 200 and <= 203 &&
            drawing.ToolId == "table" && _dragStartAnchors.Count >= 2)
        {
            Point firstScreen = AnchorToPoint(_dragStartAnchors[0], layout);
            Point secondScreen = AnchorToPoint(_dragStartAnchors[1], layout);
            Rect tableRect = CreateNormalizedRect(firstScreen, secondScreen);
            Point oppositeScreen = _dragAnchorIndex switch
            {
                200 => tableRect.BottomRight,
                201 => tableRect.BottomLeft,
                202 => tableRect.TopLeft,
                _ => tableRect.TopRight
            };
            DrawingAnchor opposite = CreateDrawingAnchor(oppositeScreen, layout, constrain: false);
            _drawings[index] = drawing with
            {
                Anchors = new[] { opposite, current },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 100 and <= 103 &&
            drawing.ToolId == "gann-square-fixed" && _dragStartAnchors.Count >= 2)
        {
            Point firstScreen = AnchorToPoint(_dragStartAnchors[0], layout);
            Point secondScreen = AnchorToPoint(_dragStartAnchors[1], layout);
            Point displaySecond = GetGannDisplaySecondPoint(drawing, firstScreen, secondScreen);
            Rect fixedRect = Bounds(new[] { firstScreen, displaySecond });
            Point oppositeScreen = _dragAnchorIndex switch
            {
                100 => fixedRect.BottomRight,
                101 => fixedRect.BottomLeft,
                102 => fixedRect.TopLeft,
                _ => fixedRect.TopRight
            };
            DrawingAnchor opposite = CreateDrawingAnchor(oppositeScreen, layout, constrain: false);
            _drawings[index] = drawing with
            {
                Anchors = new[] { opposite, current },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 100 and <= 103 &&
            DrawingToolCatalog.Find(drawing.ToolId)?.Geometry == DrawingGeometryKind.Image &&
            _dragStartAnchors.Count >= 2)
        {
            long minTime = _dragStartAnchors.Min(anchor => anchor.StartUnix);
            long maxTime = _dragStartAnchors.Max(anchor => anchor.StartUnix);
            double minPrice = _dragStartAnchors.Min(anchor => anchor.Price);
            double maxPrice = _dragStartAnchors.Max(anchor => anchor.Price);
            DrawingAnchor opposite = _dragAnchorIndex switch
            {
                100 => new DrawingAnchor(maxTime, minPrice),   // top-left -> bottom-right fixed
                101 => new DrawingAnchor(minTime, minPrice),   // top-right -> bottom-left fixed
                102 => new DrawingAnchor(minTime, maxPrice),   // bottom-right -> top-left fixed
                _ => new DrawingAnchor(maxTime, maxPrice)      // bottom-left -> top-right fixed
            };
            _drawings[index] = drawing with
            {
                Anchors = new[] { opposite, current },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex is >= 300 and <= 303 &&
            DrawingToolCatalog.Find(drawing.ToolId)?.Geometry == DrawingGeometryKind.Icon &&
            _dragStartAnchors.Count >= 1 &&
            DrawingMediaCatalog.TryDecode(drawing.Text, out DrawingMediaDefinition? media) && media is not null)
        {
            Point startCenter = AnchorToPoint(_dragStartAnchors[0], layout);
            Rect baseBounds = GetTickLabMediaBounds(startCenter, media);
            double startScale = Math.Clamp(_dragStartMediaScale, MinimumDrawingMediaScale, MaximumDrawingMediaScale);
            Rect startBounds = new(
                startCenter.X - baseBounds.Width * startScale / 2.0,
                startCenter.Y - baseBounds.Height * startScale / 2.0,
                baseBounds.Width * startScale,
                baseBounds.Height * startScale);

            Point opposite = _dragAnchorIndex switch
            {
                300 => startBounds.BottomRight,
                301 => startBounds.BottomLeft,
                302 => startBounds.TopLeft,
                _ => startBounds.TopRight
            };
            double signX = _dragAnchorIndex is 300 or 303 ? -1.0 : 1.0;
            double signY = _dragAnchorIndex is 300 or 301 ? -1.0 : 1.0;

            // Use whichever diagonal component asks for the larger uniform size.
            // Width and height are never edited independently, so the graphic cannot
            // be stretched or squashed horizontally/vertically.
            double widthScale = Math.Abs(dragMouse.X - opposite.X) / Math.Max(1.0, baseBounds.Width);
            double heightScale = Math.Abs(dragMouse.Y - opposite.Y) / Math.Max(1.0, baseBounds.Height);
            double scale = Math.Clamp(
                Math.Max(widthScale, heightScale),
                MinimumDrawingMediaScale,
                MaximumDrawingMediaScale);

            double width = baseBounds.Width * scale;
            double height = baseBounds.Height * scale;
            Point movedCorner = new(opposite.X + signX * width, opposite.Y + signY * height);
            Point center = new((opposite.X + movedCorner.X) * 0.5, (opposite.Y + movedCorner.Y) * 0.5);
            DrawingAnchor movedCenter = CreateDrawingAnchor(center, layout, constrain: false);

            _drawings[index] = drawing with
            {
                Anchors = new[] { movedCenter },
                NumericOptions = WithDrawingMediaScale(drawing.NumericOptions, scale),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_drawingDragMode == DrawingDragMode.Anchor && _dragAnchorIndex >= 0 && _dragAnchorIndex < _dragStartAnchors.Count)
        {
            DrawingAnchor[] anchors = _dragStartAnchors.ToArray();
            DrawingAnchor replacement = current;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && anchors.Length > 1)
            {
                DrawingAnchor previous = anchors[Math.Max(0, _dragAnchorIndex - 1)];
                replacement = ConstrainDrawingAnchor(previous, current, layout);
            }
            if (drawing.ToolId == "parallel-channel" && anchors.Length >= 3)
            {
                if (_dragAnchorIndex == 2)
                    replacement = WithDrawingAnchorTime(replacement, anchors[1]);
                else if (_dragAnchorIndex == 1)
                    anchors[2] = WithDrawingAnchorTime(anchors[2], replacement);
            }
            anchors[_dragAnchorIndex] = replacement;
            _drawings[index] = drawing with { Anchors = anchors, UpdatedAt = DateTimeOffset.UtcNow };
            return;
        }

        // In Raw Tick mode body movement uses tick-index delta, not wall-clock
        // seconds. This preserves exact millisecond anchors and keeps drawings
        // aligned when many ticks share the same second or older pages are prepended.
        if (_rawTickDrawingSurface && _drawingDragMode == DrawingDragMode.Body &&
            _dragStartMouseAnchor is not null)
        {
            int currentIndex = FindNearestRawTickIndex(DrawingAnchorMilliseconds(current));
            int startIndex = FindNearestRawTickIndex(DrawingAnchorMilliseconds(_dragStartMouseAnchor));
            int slotDelta = currentIndex >= 0 && startIndex >= 0 ? currentIndex - startIndex : 0;
            double rawPriceDelta = current.Price - _dragStartMouseAnchor.Price;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double xDistance = currentIndex >= 0 && startIndex >= 0
                    ? Math.Abs(RawTickIndexToX(currentIndex, layout) - RawTickIndexToX(startIndex, layout))
                    : 0.0;
                double yDistance = Math.Abs(
                    PriceToY(current.Price, layout) -
                    PriceToY(_dragStartMouseAnchor.Price, layout));
                if (xDistance >= yDistance)
                    rawPriceDelta = 0;
                else
                    slotDelta = 0;
            }

            DrawingAnchor[] rawShifted = _dragStartAnchors.ToArray();
            if (drawing.ToolId == "bars-pattern" && rawShifted.Length >= 3)
            {
                rawShifted[2] = ShiftRawTickAnchorBySlots(rawShifted[2], slotDelta, rawPriceDelta);
            }
            else
            {
                for (int anchorIndex = 0; anchorIndex < rawShifted.Length; anchorIndex++)
                    rawShifted[anchorIndex] = ShiftRawTickAnchorBySlots(rawShifted[anchorIndex], slotDelta, rawPriceDelta);
            }
            _drawings[index] = drawing with { Anchors = rawShifted, UpdatedAt = DateTimeOffset.UtcNow };
            return;
        }

        // Bars Pattern owns two immutable source-range anchors plus one placement
        // anchor. Dragging the visible copied pattern must move the projection only;
        // otherwise the source candle range changes and the copied shape mutates.
        if (_drawingDragMode == DrawingDragMode.Body && drawing.ToolId == "bars-pattern" &&
            _dragStartAnchors.Count >= 3)
        {
            long projectionTimeDelta = current.StartUnix - _dragStartMouseAnchor.StartUnix;
            double projectionPriceDelta = current.Price - _dragStartMouseAnchor.Price;
            DrawingAnchor[] anchors = _dragStartAnchors.ToArray();
            anchors[2] = anchors[2] with
            {
                StartUnix = SafeTimestampOffset(anchors[2].StartUnix, projectionTimeDelta),
                Price = anchors[2].Price + projectionPriceDelta
            };
            _drawings[index] = drawing with { Anchors = anchors, UpdatedAt = DateTimeOffset.UtcNow };
            return;
        }

        long timeDelta = current.StartUnix - _dragStartMouseAnchor.StartUnix;
        double priceDelta = current.Price - _dragStartMouseAnchor.Price;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double xDistance = Math.Abs(
                DrawingTimestampToTimelineSlot(current.StartUnix) -
                DrawingTimestampToTimelineSlot(_dragStartMouseAnchor.StartUnix));
            double yDistance = Math.Abs(
                PriceToY(current.Price, layout) -
                PriceToY(_dragStartMouseAnchor.Price, layout));
            if (xDistance >= yDistance)
                priceDelta = 0;
            else
                timeDelta = 0;
        }

        DrawingAnchor[] shifted = _dragStartAnchors.Select(anchor =>
            anchor with
            {
                StartUnix = SafeTimestampOffset(anchor.StartUnix, timeDelta),
                Price = anchor.Price + priceDelta
            }).ToArray();
        _drawings[index] = drawing with { Anchors = shifted, UpdatedAt = DateTimeOffset.UtcNow };
    }

    private void SelectDrawingsInBox(Rect selection, ChartLayout layout)
    {
        _selectedDrawingIds.Clear();
        foreach (ChartDrawing drawing in VisibleDrawings())
        {
            Rect bounds = GetDrawingBounds(drawing, layout);
            if (!bounds.IsEmpty && selection.IntersectsWith(bounds))
                _selectedDrawingIds.Add(drawing.Id);
        }
        PublishDrawingSelectionChanged();
        InvalidateVisual();
    }

    private ChartDrawing CreateNewDrawing(DrawingToolDefinition tool, IReadOnlyList<DrawingAnchor> anchors)
    {
        Candle? candle = DrawingCandles.LastOrDefault();
        DrawingTemplate? defaultTemplate = _drawingTemplates.LastOrDefault(item => item.ToolId == tool.Id && item.IsDefault);
        DrawingStyle style = defaultTemplate?.Style ?? DrawingToolCatalog.DefaultStyle(tool);
        if (defaultTemplate is null && tool.Id == "emojis")
            style = style with { FontFamily = "Segoe UI Emoji", FontSize = 26, LineWidth = 1.25 };
        else if (defaultTemplate is null && tool.Id == "stickers")
            style = style with { FontFamily = "Segoe UI Semibold", FontSize = 14, Bold = true };
        IReadOnlyList<DrawingLevel> levels = defaultTemplate?.Levels ??
            (tool.SupportsLevels ? DrawingParityDefaults.LevelsForTool(tool.Id) : Array.Empty<DrawingLevel>());
        string defaultText = tool.Id switch
        {
            "text" => "Add text",
            "note" => "Add text",
            "price-note" => string.Empty,
            "pin" => "Add text",
            "table" => "||\n||",
            "callout" => "Add text",
            "comment" => "Add text",
            "price-label" => string.Empty,
            "signpost" => "Add text",
            "flag-mark" => string.Empty,
            "post" => "Post",
            "idea" => "Idea",
            _ => tool.Geometry switch
            {
                DrawingGeometryKind.Text => "Add text",
                DrawingGeometryKind.Note => "Add text",
                DrawingGeometryKind.Callout => "Add text",
                DrawingGeometryKind.Flag => string.Empty,
                DrawingGeometryKind.Icon => ConsumeDrawingMediaSymbol(tool.Id),
                _ => string.Empty
            }
        };
        IReadOnlyDictionary<string, string> textOptions = defaultTemplate?.TextOptions ?? new Dictionary<string, string>();
        if (tool.Geometry == DrawingGeometryKind.Image && !string.IsNullOrWhiteSpace(_nextDrawingImagePath))
        {
            var imageTextOptions = textOptions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            imageTextOptions["ImagePath"] = _nextDrawingImagePath;
            imageTextOptions["ImageAspectRatio"] = _nextDrawingImageAspectRatio.ToString("0.########", CultureInfo.InvariantCulture);
            textOptions = imageTextOptions;
            style = style with { Opacity = _nextDrawingImageOpacity };
            _nextDrawingImagePath = string.Empty;
            _nextDrawingImageOpacity = 1.0;
            _nextDrawingImageAspectRatio = 1.0;
        }

        return new ChartDrawing
        {
            ToolId = tool.Id,
            DisplayName = tool.DisplayName,
            Symbol = candle?.Symbol ?? string.Empty,
            Timeframe = candle?.Timeframe ?? string.Empty,
            ChartId = DrawingOwnerId,
            Anchors = anchors,
            Style = style,
            Levels = levels,
            Text = defaultText,
            Name = tool.DisplayName,
            ZIndex = _drawings.Count,
            SyncMode = _defaultDrawingSyncMode,
            NumericOptions = defaultTemplate?.NumericOptions ?? DefaultNumericOptions(tool),
            TextOptions = textOptions
        };
    }

    private string ConsumeDrawingMediaSymbol(string toolId)
    {
        if (_nextDrawingMediaSymbols.Remove(toolId, out string? symbol) &&
            !string.IsNullOrWhiteSpace(symbol))
        {
            _lastDrawingMediaSymbols[toolId] = symbol;
            return symbol;
        }

        DrawingMediaDefinition? fallback = toolId switch
        {
            "emojis" => DrawingMediaCatalog.Emojis.FirstOrDefault(),
            "stickers" => DrawingMediaCatalog.Find("stickers", "buy"),
            _ => DrawingMediaCatalog.Find("icons", "star")
        };
        return fallback is null ? "★" : DrawingMediaCatalog.Encode(fallback);
    }

    private static IReadOnlyDictionary<string, double> DefaultNumericOptions(DrawingToolDefinition tool) =>
        DrawingParityDefaults.NumericOptions(tool.Id);

    private void CompleteWorkingDrawing()
    {
        if (_workingDrawing is null)
            return;
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(_workingDrawing.ToolId);
        if (tool is null || _workingDrawing.Anchors.Count < tool.MinimumAnchors)
        {
            CancelWorkingDrawing(true);
            return;
        }
        _drawings.Add(_workingDrawing with { UpdatedAt = DateTimeOffset.UtcNow });
        _selectedDrawingIds.Clear();
        _selectedDrawingIds.Add(_workingDrawing.Id);
        PublishDrawingSelectionChanged();
        string completedTool = _workingDrawing.ToolId;
        _workingDrawing = null;
        _previewDrawingAnchor = null;
        _freehandDrawing = false;
        _freehandFilteredPoint = null;
        _freehandLastAcceptedPoint = null;
        // Media tools follow the same confirmed one-select -> one-draw lifecycle as
        // every other drawing tool. Stay-in-drawing-mode remains the single global
        // opt-in mechanism for users who deliberately want repeated placement.
        if (!_stayInDrawingMode)
        {
            _activeDrawingToolId = "cursor-crosshair";
            UpdateDrawingCursor(DrawingToolCatalog.Find(_activeDrawingToolId)!);
            ActiveDrawingToolChanged?.Invoke(_activeDrawingToolId);
        }
        NotifyDrawingChanged($"{DrawingToolCatalog.Find(completedTool)?.DisplayName ?? "Drawing"} created.");
    }

    private void CancelWorkingDrawing(bool restoreUndo)
    {
        _workingDrawing = null;
        _previewDrawingAnchor = null;
        _freehandDrawing = false;
        _freehandFilteredPoint = null;
        _freehandLastAcceptedPoint = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (restoreUndo && _drawingUndo.Count > 0)
            _drawingUndo.Pop();
        InvalidateVisual();
    }


    private IReadOnlyList<DrawingAnchor> CreateDefaultPositionAnchors(DrawingToolDefinition tool, Point mouse, ChartLayout layout)
    {
        Point entryPoint = ConstrainPointToPlot(mouse, layout.Plot);
        double horizontalSpan = Math.Clamp(layout.Plot.Width * 0.18, 110.0, 220.0);
        double availableRight = layout.Plot.Right - entryPoint.X;
        double zoneX = availableRight >= 70.0
            ? Math.Min(layout.Plot.Right - 4.0, entryPoint.X + horizontalSpan)
            : Math.Max(layout.Plot.Left + 4.0, entryPoint.X - horizontalSpan);

        double verticalSpan = Math.Clamp(layout.Plot.Height * 0.12, 52.0, 96.0);
        bool shortPosition = tool.Id == "short-position";
        double targetY = entryPoint.Y + (shortPosition ? verticalSpan : -verticalSpan);
        double stopY = entryPoint.Y + (shortPosition ? -verticalSpan : verticalSpan);
        targetY = Math.Clamp(targetY, layout.Plot.Top + 3.0, layout.Plot.Bottom - 3.0);
        stopY = Math.Clamp(stopY, layout.Plot.Top + 3.0, layout.Plot.Bottom - 3.0);

        DrawingAnchor entry = CreateDrawingAnchor(entryPoint, layout, constrain: false);
        DrawingAnchor target = CreateDrawingAnchor(new Point(zoneX, targetY), layout, constrain: false);
        DrawingAnchor stop = CreateDrawingAnchor(new Point(zoneX, stopY), layout, constrain: false);
        return new[] { entry, target, stop };
    }

    private DrawingAnchor CreateDrawingAnchor(Point point, ChartLayout layout, bool constrain)
    {
        Point constrainedPoint = ConstrainPointToPlot(point, layout.Plot);
        double price = YToPrice(constrainedPoint.Y, layout);

        DrawingAnchor anchor;
        if (_rawTickDrawingSurface && RawTickDrawingTicks.Count > 0)
        {
            int rawIndex = RawTickIndexFromPlotX(constrainedPoint.X, layout);
            anchor = CreateRawTickAnchorAtIndex(rawIndex, price);
        }
        else
        {
            double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
            double localSlot =
                (constrainedPoint.X - layout.Plot.Left) / Math.Max(0.0001, slotWidth) - 0.5;
            double timelineSlot = layout.TimelineFirst + localSlot;
            long timestamp = DrawingTimelineSlotToTimestamp(timelineSlot);
            anchor = new DrawingAnchor(timestamp, price);
        }

        if (TryGetDrawingMagnetSnap(constrainedPoint, layout, out int snappedIndex, out double snappedPrice))
        {
            anchor = _rawTickDrawingSurface
                ? CreateRawTickAnchorAtIndex(snappedIndex, snappedPrice)
                : CreateDrawingAnchorAtIndex(snappedIndex, snappedPrice);
        }

        if (constrain && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
            _workingDrawing?.Anchors.LastOrDefault() is DrawingAnchor previous)
        {
            return ConstrainDrawingAnchor(previous, anchor, layout);
        }
        return anchor;
    }

    private static Point ConstrainPointToPlot(Point point, Rect plot) =>
        new(
            Math.Clamp(point.X, plot.Left, plot.Right),
            Math.Clamp(point.Y, plot.Top, plot.Bottom));

    private bool TryGetDrawingMagnetSnap(
        Point point,
        ChartLayout layout,
        out int candleIndex,
        out double snappedPrice)
    {
        candleIndex = -1;
        snappedPrice = double.NaN;
        DrawingMagnetMode effective = EffectiveMagnetMode();
        if (effective == DrawingMagnetMode.Off || DrawingCandles.Count == 0)
            return false;

        int? candidate = _rawTickDrawingSurface
            ? RawTickIndexFromPlotX(point.X, layout)
            : HitTestNearestCandle(layout, point.X);
        if (candidate is null || candidate.Value < 0 || candidate.Value >= DrawingCandles.Count)
            return false;

        candleIndex = candidate.Value;
        double pointerPrice = YToPrice(point.Y, layout);
        if (_rawTickDrawingSurface && candleIndex < RawTickDrawingTicks.Count)
        {
            MarketTick tick = RawTickDrawingTicks[candleIndex];
            double bid = tick.Bid > 0 ? tick.Bid : tick.DisplayPrice;
            double ask = tick.Ask > 0 ? tick.Ask : bid;
            double display = tick.DisplayPrice;
            double[] candidates = { bid, ask, display };
            snappedPrice = candidates.OrderBy(value => Math.Abs(PriceToY(value, layout) - point.Y)).First();
            Point rawTargetPoint = AnchorToPoint(CreateRawTickAnchorAtIndex(candleIndex, snappedPrice), layout);
            double rawPointerDistance = Distance(point, rawTargetPoint);
            return effective != DrawingMagnetMode.Weak || rawPointerDistance <= 14.0;
        }

        Candle candle = DrawingCandles[candleIndex];
        bool bullish = candle.Close >= candle.Open;
        double lowerBody = Math.Min(candle.Open, candle.Close);
        double upperBody = Math.Max(candle.Open, candle.Close);

        double ohlcTarget;
        if (pointerPrice <= candle.Low)
            ohlcTarget = candle.Low;
        else if (pointerPrice >= candle.High)
            ohlcTarget = candle.High;
        else if (pointerPrice < lowerBody)
            ohlcTarget = bullish ? candle.Open : candle.Close;
        else if (pointerPrice > upperBody)
            ohlcTarget = bullish ? candle.Close : candle.Open;
        else
        {
            double openDistance = Math.Abs(PriceToY(candle.Open, layout) - point.Y);
            double closeDistance = Math.Abs(PriceToY(candle.Close, layout) - point.Y);
            ohlcTarget = openDistance <= closeDistance ? candle.Open : candle.Close;
        }

        double bestTarget = ohlcTarget;
        double bestVerticalDistance = Math.Abs(PriceToY(bestTarget, layout) - point.Y);
        if (_snapDrawingsToIndicators && IndicatorSnapValuesProvider is not null)
        {
            try
            {
                foreach (double value in IndicatorSnapValuesProvider(candleIndex).Where(double.IsFinite))
                {
                    double distance = Math.Abs(PriceToY(value, layout) - point.Y);
                    if (distance < bestVerticalDistance)
                    {
                        bestVerticalDistance = distance;
                        bestTarget = value;
                    }
                }
            }
            catch
            {
                // A stale indicator frame must never block normal OHLC magnet snapping.
            }
        }

        Point targetPoint = AnchorToPoint(new DrawingAnchor(candle.StartUnix, bestTarget), layout);
        double pointerDistance = Distance(point, targetPoint);
        if (effective == DrawingMagnetMode.Weak && pointerDistance > 14.0)
            return false;

        snappedPrice = bestTarget;
        return true;
    }

    private DrawingMagnetMode EffectiveMagnetMode()
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!ctrl)
            return _drawingMagnetMode;
        return _drawingMagnetMode == DrawingMagnetMode.Off
            ? DrawingMagnetMode.Strong
            : DrawingMagnetMode.Off;
    }

    private DrawingAnchor ConstrainDrawingAnchor(DrawingAnchor previous, DrawingAnchor current, ChartLayout layout)
    {
        Point p1 = AnchorToPoint(previous, layout);
        Point p2 = AnchorToPoint(current, layout);
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.0001)
            return current;
        double angle = Math.Atan2(dy, dx);
        double increment = Math.PI / 4.0;
        double snapped = Math.Round(angle / increment) * increment;
        Point target = new(p1.X + Math.Cos(snapped) * length, p1.Y + Math.Sin(snapped) * length);

        DrawingToolDefinition? tool = DrawingToolCatalog.Find(_workingDrawing?.ToolId);
        if (tool?.Geometry is DrawingGeometryKind.Rectangle or DrawingGeometryKind.Ellipse)
        {
            double size = Math.Max(Math.Abs(target.X - p1.X), Math.Abs(target.Y - p1.Y));
            target = new Point(p1.X + Math.Sign(target.X - p1.X) * size, p1.Y + Math.Sign(target.Y - p1.Y) * size);
        }
        return CreateAnchorWithoutConstraints(target, layout);
    }

    private DrawingAnchor CreateAnchorWithoutConstraints(Point point, ChartLayout layout)
    {
        Point constrainedPoint = ConstrainPointToPlot(point, layout.Plot);
        double price = YToPrice(constrainedPoint.Y, layout);
        if (_rawTickDrawingSurface && RawTickDrawingTicks.Count > 0)
            return CreateRawTickAnchorAtIndex(RawTickIndexFromPlotX(constrainedPoint.X, layout), price);

        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        double localSlot =
            (constrainedPoint.X - layout.Plot.Left) / Math.Max(0.0001, slotWidth) - 0.5;
        long timestamp = DrawingTimelineSlotToTimestamp(layout.TimelineFirst + localSlot);
        return new DrawingAnchor(timestamp, price);
    }

    private long GetDrawingSlotSeconds()
    {
        if (DrawingCandles.Count < 2)
            return 60;

        int start = Math.Max(1, DrawingCandles.Count - 128);
        var differences = new List<long>(DrawingCandles.Count - start);
        for (int index = start; index < DrawingCandles.Count; index++)
        {
            long difference = DrawingCandles[index].StartUnix - DrawingCandles[index - 1].StartUnix;
            if (difference > 0)
                differences.Add(difference);
        }

        if (differences.Count == 0)
            return 60;
        differences.Sort();
        return Math.Max(1, differences[differences.Count / 2]);
    }

    private long DrawingTimelineSlotToTimestamp(double timelineSlot)
    {
        if (DrawingCandles.Count == 0)
            return 0;

        double firstSlot = GetCandleTimelineSlot(0);
        double lastSlot = GetCandleTimelineSlot(DrawingCandles.Count - 1);
        long interval = GetDrawingSlotSeconds();
        if (timelineSlot <= firstSlot)
            return SafeTimestampOffset(DrawingCandles[0].StartUnix, (timelineSlot - firstSlot) * interval);
        if (timelineSlot >= lastSlot)
            return SafeTimestampOffset(DrawingCandles[^1].StartUnix, (timelineSlot - lastSlot) * interval);

        int nextIndex = Math.Clamp(
            FindFirstCandleAtOrAfterTimelineSlot((int)Math.Ceiling(timelineSlot)),
            1,
            DrawingCandles.Count - 1);
        int previousIndex = nextIndex - 1;
        double previousSlot = GetCandleTimelineSlot(previousIndex);
        double nextSlot = GetCandleTimelineSlot(nextIndex);
        if (nextSlot <= previousSlot)
            return DrawingCandles[previousIndex].StartUnix;

        double ratio = Math.Clamp(
            (timelineSlot - previousSlot) / (nextSlot - previousSlot),
            0.0,
            1.0);
        double timestamp = DrawingCandles[previousIndex].StartUnix +
            (DrawingCandles[nextIndex].StartUnix - DrawingCandles[previousIndex].StartUnix) * ratio;
        return ClampTimestamp(timestamp);
    }

    private double DrawingTimestampToTimelineSlot(long timestamp)
    {
        if (_rawTickDrawingSurface)
            return RawTickTimestampToTimelineSlot(checked(timestamp * 1000L));
        if (DrawingCandles.Count == 0)
            return 0.0;

        double firstSlot = GetCandleTimelineSlot(0);
        double lastSlot = GetCandleTimelineSlot(DrawingCandles.Count - 1);
        long interval = GetDrawingSlotSeconds();
        if (timestamp <= DrawingCandles[0].StartUnix)
            return firstSlot + (timestamp - DrawingCandles[0].StartUnix) / (double)interval;
        if (timestamp >= DrawingCandles[^1].StartUnix)
            return lastSlot + (timestamp - DrawingCandles[^1].StartUnix) / (double)interval;

        int low = 0;
        int high = DrawingCandles.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (DrawingCandles[middle].StartUnix < timestamp)
                low = middle + 1;
            else
                high = middle;
        }

        int nextIndex = Math.Clamp(low, 1, DrawingCandles.Count - 1);
        int previousIndex = nextIndex - 1;
        long previousTime = DrawingCandles[previousIndex].StartUnix;
        long nextTime = DrawingCandles[nextIndex].StartUnix;
        if (nextTime <= previousTime)
            return GetCandleTimelineSlot(previousIndex);

        double ratio = Math.Clamp(
            (timestamp - previousTime) / (double)(nextTime - previousTime),
            0.0,
            1.0);
        double previousSlot = GetCandleTimelineSlot(previousIndex);
        double nextSlot = GetCandleTimelineSlot(nextIndex);
        return previousSlot + (nextSlot - previousSlot) * ratio;
    }

    private static long SafeTimestampOffset(long timestamp, double offset)
    {
        if (!double.IsFinite(offset))
            return timestamp;
        return ClampTimestamp(timestamp + offset);
    }

    private static long ClampTimestamp(double timestamp)
    {
        if (timestamp >= long.MaxValue)
            return long.MaxValue;
        if (timestamp <= long.MinValue)
            return long.MinValue;
        return (long)Math.Round(timestamp);
    }

    private Point AnchorToPoint(DrawingAnchor anchor, ChartLayout layout)
    {
        if (_rawTickDrawingSurface)
        {
            int rawIndex = FindNearestRawTickIndex(DrawingAnchorMilliseconds(anchor));
            double rawX = rawIndex >= 0 ? RawTickIndexToX(rawIndex, layout) : layout.Plot.Left;
            return new Point(rawX, PriceToY(anchor.Price, layout));
        }

        double timelineSlot = DrawingTimestampToTimelineSlot(anchor.StartUnix);
        double slotWidth = layout.Plot.Width / Math.Max(1, layout.SlotCount);
        double x = layout.Plot.Left +
            slotWidth * (timelineSlot - layout.TimelineFirst + 0.5);
        return new Point(x, PriceToY(anchor.Price, layout));
    }

    private bool IsDrawingVisible(ChartDrawing drawing)
    {
        if (_hideAllDrawings || drawing.IsHidden)
            return false;
        Candle? candle = DrawingCandles.LastOrDefault();
        if (candle is null)
            return false;
        if (!string.IsNullOrWhiteSpace(drawing.Symbol) && !string.Equals(drawing.Symbol, candle.Symbol, StringComparison.OrdinalIgnoreCase) &&
            drawing.SyncMode is DrawingSyncMode.CurrentChart or DrawingSyncMode.SameSymbol or DrawingSyncMode.SameSymbolAndTimeframe)
            return false;
        if (drawing.SyncMode == DrawingSyncMode.SameSymbolAndTimeframe &&
            !string.Equals(drawing.Timeframe, candle.Timeframe, StringComparison.OrdinalIgnoreCase))
            return false;
        string bucket = TimeframeBucket(candle.Timeframe);
        bool bucketVisible = bucket switch
        {
            "Ticks" => DrawingVisibilityOption(drawing, "VisibilityTicks", true),
            "Seconds" => drawing.Visibility.Seconds,
            "Minutes" => drawing.Visibility.Minutes,
            "Hours" => drawing.Visibility.Hours,
            "Daily" => drawing.Visibility.Daily,
            "Weekly" => drawing.Visibility.Weekly,
            "Monthly" => drawing.Visibility.Monthly,
            "Ranges" => DrawingVisibilityOption(drawing, "VisibilityRanges", true),
            _ => true
        };
        if (!bucketVisible)
            return false;
        if (!IsWithinDrawingVisibilityBucketRange(drawing, candle.Timeframe, bucket))
            return false;

        double currentSeconds = DrawingTimeframeSeconds(candle.Timeframe);
        double minimumSeconds = DrawingTimeframeSeconds(drawing.Visibility.MinimumTimeframe);
        double maximumSeconds = DrawingTimeframeSeconds(drawing.Visibility.MaximumTimeframe);
        if (minimumSeconds > 0 && currentSeconds > 0 && currentSeconds < minimumSeconds)
            return false;
        if (maximumSeconds > 0 && currentSeconds > 0 && currentSeconds > maximumSeconds)
            return false;
        return true;
    }

    private static double DrawingTimeframeSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        string value = raw.Trim();
        if (value.Equals("Tick", StringComparison.OrdinalIgnoreCase)) return 0.001;
        string upper = value.ToUpperInvariant();
        if (upper.StartsWith("PERIOD_", StringComparison.Ordinal))
        {
            string code = upper[7..];
            if (code.StartsWith("MN", StringComparison.Ordinal) && int.TryParse(code[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int months)) return Math.Max(1, months) * 30d * 86400d;
            if (code.StartsWith("W", StringComparison.Ordinal) && int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weeks)) return Math.Max(1, weeks) * 7d * 86400d;
            if (code.StartsWith("D", StringComparison.Ordinal) && int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days)) return Math.Max(1, days) * 86400d;
            if (code.StartsWith("H", StringComparison.Ordinal) && int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours)) return Math.Max(1, hours) * 3600d;
            if (code.StartsWith("M", StringComparison.Ordinal) && int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)) return Math.Max(1, minutes) * 60d;
            if (code.StartsWith("S", StringComparison.Ordinal) && int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)) return Math.Max(1, seconds);
        }

        char suffix = value[^1];
        string numberPart = value[..^1];
        double quantity = string.IsNullOrWhiteSpace(numberPart) ? 1 :
            (double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0);
        if (quantity <= 0) return 0;
        return suffix switch
        {
            's' or 'S' => quantity,
            'm' => quantity * 60d,
            'h' or 'H' => quantity * 3600d,
            'd' or 'D' => quantity * 86400d,
            'w' or 'W' => quantity * 7d * 86400d,
            'M' => quantity * 30d * 86400d,
            _ => 0
        };
    }

    private IEnumerable<ChartDrawing> VisibleDrawings() =>
        _drawings.Where(IsDrawingVisible).OrderBy(item => item.ZIndex).ThenBy(item => item.CreatedAt);

    private void DrawDrawingLayer(DrawingContext dc, ChartLayout layout, DrawingVisualLayer layer)
    {
        foreach (ChartDrawing drawing in VisibleDrawings().Where(item => item.VisualLayer == layer))
            DrawOneDrawing(dc, layout, drawing, preview: false);

        if (layer == DrawingVisualLayer.AboveCandles && _workingDrawing is not null)
        {
            ChartDrawing preview = _workingDrawing;
            if (_previewDrawingAnchor is not null && !_freehandDrawing)
                preview = preview with { Anchors = preview.Anchors.Append(_previewDrawingAnchor).ToArray() };
            DrawOneDrawing(dc, layout, preview, preview: true);
        }

        if (layer == DrawingVisualLayer.AboveCandles)
        {
            DrawDrawingSelection(dc, layout);
            if (_drawingSelectionBox.HasValue)
            {
                var boxPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 96, 165, 250)), 1)
                {
                    DashStyle = DashStyles.Dash
                };
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(32, 96, 165, 250)), boxPen, _drawingSelectionBox.Value);
            }
        }
    }

    private void DrawWorkingDrawingOverlay(DrawingContext dc, ChartLayout layout)
    {
        if (_workingDrawing is null || _workingDrawing.Anchors.Count == 0)
            return;

        // Reference construction handles are hollow white circles with a blue
        // outline; using the same treatment as selected handles also makes the
        // first committed point unmistakable immediately after click #1.
        var committedFill = Brushes.White;
        var committedOutline = new Pen(new SolidColorBrush(Color.FromRgb(41, 98, 255)), 1.5);
        var previewOutline = new Pen(new SolidColorBrush(Color.FromRgb(41, 98, 255)), 1.5);
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(190, 41, 98, 255)), 1.05)
        {
            DashStyle = DashStyles.Dash
        };
        if (committedFill.CanFreeze) committedFill.Freeze();
        if (committedOutline.CanFreeze) committedOutline.Freeze();
        if (previewOutline.CanFreeze) previewOutline.Freeze();
        if (guidePen.CanFreeze) guidePen.Freeze();

        Point? lastCommitted = null;
        DrawingToolDefinition? workingTool = DrawingToolCatalog.Find(_workingDrawing.ToolId);
        bool denseFreehand = workingTool?.Geometry is DrawingGeometryKind.Brush or DrawingGeometryKind.Highlighter;
        IEnumerable<DrawingAnchor> visibleConstructionAnchors = denseFreehand && _workingDrawing.Anchors.Count > 1
            ? new[] { _workingDrawing.Anchors[0], _workingDrawing.Anchors[^1] }
            : _workingDrawing.Anchors;
        foreach (DrawingAnchor anchor in visibleConstructionAnchors)
        {
            Point point = AnchorToPoint(anchor, layout);
            if (!layout.Plot.Contains(point))
                continue;
            dc.DrawEllipse(committedFill, committedOutline, point, 5.0, 5.0);
            lastCommitted = point;
        }

        if (!_freehandDrawing && _previewDrawingAnchor is DrawingAnchor previewAnchor)
        {
            int previewIndex = _workingDrawing.Anchors.Count;
            DrawingAnchor displayPreview = ConstructionDisplayAnchor(workingTool, previewAnchor, previewIndex);
            Point moving = AnchorToPoint(displayPreview, layout);
            if (layout.Plot.Contains(moving))
            {
                // Once enough points exist for the actual tool geometry to render,
                // that geometry is the live preview. Keep the dashed helper only
                // for the early stage of 3+ point tools so we do not draw a second,
                // visually incorrect line over the reference preview.
                bool needsGuide = workingTool is not null &&
                    _workingDrawing.Anchors.Count + 1 < workingTool.MinimumAnchors;
                if (needsGuide && lastCommitted is Point committed && Distance(committed, moving) > 0.5)
                    dc.DrawLine(guidePen, committed, moving);
                dc.DrawEllipse(Brushes.White, previewOutline, moving, 4.25, 4.25);
            }
        }
    }

    private void DrawOneDrawing(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, bool preview)
    {
        DrawingToolDefinition? tool = DrawingToolCatalog.Find(drawing.ToolId);
        if (tool is null || drawing.Anchors.Count == 0)
            return;

        Point[] points = drawing.Anchors.Select(anchor => AnchorToPoint(anchor, layout)).ToArray();
        Pen pen = CreateDrawingPen(drawing.Style, preview ? 0.90 : 1.0);
        Brush fill = CreateDrawingBrush(drawing.Style.FillColor, drawing.Style.FillOpacity * (preview ? 0.90 : 1.0));
        Brush annotationFill = CreateDrawingBrush(drawing.Style.BackgroundColor, drawing.Style.FillOpacity * (preview ? 0.90 : 1.0));
        Brush textBrush = CreateDrawingBrush(drawing.Style.TextColor, drawing.Style.Opacity);

        // TradingView-parity layer owns the specialised geometry/settings for
        // tools whose behaviour differs from the older generic renderer.
        if (DrawTradingViewParityDrawing(dc, layout, drawing, tool, points, pen, fill, textBrush, preview))
            return;

        switch (tool.Geometry)
        {
            case DrawingGeometryKind.Line:
                if (points.Length >= 2)
                {
                    DrawLineWithOptions(dc, layout.Plot, points[0], points[1], pen, drawing.Style);
                    if (drawing.ToolId == "info-line" || drawing.ToolId == "trend-angle")
                        DrawLineStatistics(dc, layout, drawing, points[0], points[1], textBrush);
                }
                break;
            case DrawingGeometryKind.ArrowLine:
                if (points.Length >= 2) { dc.DrawLine(pen, points[0], points[1]); DrawArrowHead(dc, pen, points[0], points[1]); }
                break;
            case DrawingGeometryKind.Ray:
                if (points.Length >= 2) DrawRay(dc, layout.Plot, points[0], points[1], pen, false);
                break;
            case DrawingGeometryKind.ExtendedLine:
                if (points.Length >= 2) DrawRay(dc, layout.Plot, points[0], points[1], pen, true);
                break;
            case DrawingGeometryKind.HorizontalLine:
                DrawHorizontal(dc, layout.Plot, points[0], pen, true, true);
                DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, points[0], textBrush);
                break;
            case DrawingGeometryKind.HorizontalRay:
                DrawHorizontal(dc, layout.Plot, points[0], pen, false, true);
                DrawPriceLabel(dc, layout, drawing.Anchors[0].Price, points[0], textBrush);
                break;
            case DrawingGeometryKind.VerticalLine:
                dc.DrawLine(pen, new Point(points[0].X, layout.Plot.Top), new Point(points[0].X, layout.Plot.Bottom));
                break;
            case DrawingGeometryKind.CrossLine:
                dc.DrawLine(pen, new Point(layout.Plot.Left, points[0].Y), new Point(layout.Plot.Right, points[0].Y));
                dc.DrawLine(pen, new Point(points[0].X, layout.Plot.Top), new Point(points[0].X, layout.Plot.Bottom));
                break;
            case DrawingGeometryKind.Channel:
                if (points.Length >= 3) DrawChannel(dc, layout.Plot, points, pen, fill, drawing.Style);
                break;
            case DrawingGeometryKind.Regression:
                if (points.Length >= 2) DrawRegression(dc, layout, drawing, pen, fill);
                break;
            case DrawingGeometryKind.AnchoredVwap:
                DrawAnchoredVwap(dc, layout, drawing, pen);
                break;
            case DrawingGeometryKind.Fibonacci:
            case DrawingGeometryKind.FibonacciExtension:
                if (points.Length >= 2) DrawFibonacci(dc, layout, drawing, points, pen, fill, tool.Geometry == DrawingGeometryKind.FibonacciExtension);
                break;
            case DrawingGeometryKind.FibonacciChannel:
                if (points.Length >= 3) DrawFibonacciChannel(dc, layout, drawing, points, pen, fill);
                break;
            case DrawingGeometryKind.FibonacciTime:
                if (points.Length >= 2) DrawFibonacciTime(dc, layout, drawing, points, pen);
                break;
            case DrawingGeometryKind.FibonacciFan:
            case DrawingGeometryKind.GannFan:
                if (points.Length >= 2) DrawFan(dc, layout.Plot, drawing, points, pen);
                break;
            case DrawingGeometryKind.FibonacciCircles:
                if (points.Length >= 2) DrawFibCircles(dc, drawing, points, pen);
                break;
            case DrawingGeometryKind.FibonacciSpiral:
                if (points.Length >= 2) DrawSpiral(dc, points[0], points[1], pen);
                break;
            case DrawingGeometryKind.FibonacciWedge:
                if (points.Length >= 3) DrawFibWedge(dc, drawing, points, pen);
                break;
            case DrawingGeometryKind.FibonacciArcs:
                if (points.Length >= 2) DrawFibArcs(dc, drawing, points, pen);
                break;
            case DrawingGeometryKind.Pitchfork:
                if (points.Length >= 3) DrawPitchfork(dc, layout.Plot, drawing, points, pen);
                break;
            case DrawingGeometryKind.GannBox:
                if (points.Length >= 2) DrawGannBox(dc, points[0], points[1], pen, fill);
                break;
            case DrawingGeometryKind.Brush:
            case DrawingGeometryKind.Highlighter:
                DrawSmoothFreehand(dc, points, pen);
                break;
            case DrawingGeometryKind.Polyline:
                DrawPolyline(dc, points, pen, false);
                if (drawing.ToolId == "path" && points.Length >= 2)
                    DrawArrowHead(dc, pen, points[^2], points[^1]);
                break;
            case DrawingGeometryKind.Pattern:
                DrawPolyline(dc, points, pen, false);
                DrawPatternLabels(dc, drawing, points, textBrush);
                break;
            case DrawingGeometryKind.Rectangle:
                if (points.Length >= 2) DrawRectangleDrawing(dc, drawing, points[0], points[1], pen, fill, textBrush);
                break;
            case DrawingGeometryKind.RotatedRectangle:
                if (points.Length >= 3) DrawRotatedRectangle(dc, drawing, points, pen, fill, textBrush);
                break;
            case DrawingGeometryKind.Ellipse:
                if (points.Length >= 2) DrawEllipseDrawing(dc, drawing, points[0], points[1], pen, fill, textBrush);
                break;
            case DrawingGeometryKind.Triangle:
                if (points.Length >= 3) DrawPolygon(dc, points.Take(3).ToArray(), pen, fill);
                break;
            case DrawingGeometryKind.Curve:
                DrawCurve(dc, points, pen);
                break;
            case DrawingGeometryKind.DoubleCurve:
                DrawDoubleCurve(dc, points, pen);
                break;
            case DrawingGeometryKind.Arc:
                if (points.Length >= 3) DrawArc(dc, points, pen, fill);
                break;
            case DrawingGeometryKind.Image:
                if (points.Length >= 2) DrawImageDrawing(dc, drawing, points[0], points[1], preview);
                break;
            case DrawingGeometryKind.Text:
            case DrawingGeometryKind.Note:
            case DrawingGeometryKind.Callout:
            case DrawingGeometryKind.PriceLabel:
            case DrawingGeometryKind.Flag:
            case DrawingGeometryKind.Icon:
                DrawAnnotation(dc, layout, drawing, points, pen, annotationFill, textBrush, tool.Geometry);
                break;
            case DrawingGeometryKind.ArrowMarker:
                DrawArrowMarker(dc, drawing, points, pen, fill);
                break;
            case DrawingGeometryKind.Cycles:
                if (points.Length >= 2) DrawCycles(dc, layout.Plot, drawing, points, pen);
                break;
            case DrawingGeometryKind.Sine:
                if (points.Length >= 2) DrawSine(dc, points[0], points[1], pen);
                break;
            case DrawingGeometryKind.Position:
                if (points.Length >= 3) DrawPosition(dc, layout, drawing, points, pen, textBrush);
                break;
            case DrawingGeometryKind.Range:
                if (points.Length >= 2) DrawRange(dc, layout, drawing, points[0], points[1], pen, fill, textBrush);
                break;
            case DrawingGeometryKind.BarsPattern:
            case DrawingGeometryKind.GhostFeed:
                if (points.Length >= 3) DrawBarsPattern(dc, layout, drawing, points, pen, tool.Geometry == DrawingGeometryKind.GhostFeed);
                break;
            case DrawingGeometryKind.Sector:
                if (points.Length >= 3) DrawSector(dc, points, pen, fill);
                break;
            case DrawingGeometryKind.VolumeProfile:
                DrawVolumeProfile(dc, layout, drawing, points, pen, fill);
                break;
        }
    }

    private static Pen CreateDrawingPen(DrawingStyle style, double opacityMultiplier)
    {
        Brush brush = CreateDrawingBrush(style.LineColor, style.Opacity * opacityMultiplier);
        var pen = new Pen(brush, Math.Clamp(style.LineWidth, 0.5, 20));
        pen.DashStyle = style.LineStyle switch
        {
            DrawingLineStyle.Dashed => DashStyles.Dash,
            DrawingLineStyle.Dotted => DashStyles.Dot,
            _ => DashStyles.Solid
        };
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.DashCap = PenLineCap.Round;
        pen.LineJoin = PenLineJoin.Round;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static Brush CreateDrawingBrush(string value, double opacity)
    {
        Color color = Color.FromRgb(59, 130, 246);
        try
        {
            object? converted = ColorConverter.ConvertFromString(value);
            if (converted is Color parsed)
                color = parsed;
        }
        catch
        {
            // Keep the TickLab default drawing colour for invalid persisted values.
        }
        color.A = (byte)Math.Clamp((int)Math.Round(255 * Math.Clamp(opacity, 0, 1)), 0, 255);
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private IReadOnlyList<DrawingAnchor> SimplifyFreehandAnchors(
        IReadOnlyList<DrawingAnchor> anchors,
        ChartLayout layout,
        double tolerance)
    {
        if (anchors.Count <= 3)
            return anchors.ToArray();

        Point[] points = anchors.Select(anchor => AnchorToPoint(anchor, layout)).ToArray();
        bool[] keep = new bool[points.Length];
        keep[0] = true;
        keep[^1] = true;
        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, points.Length - 1));

        while (pending.Count > 0)
        {
            (int start, int end) = pending.Pop();
            double maximum = 0;
            int index = -1;
            for (int i = start + 1; i < end; i++)
            {
                double distance = DistanceToSegment(points[i], points[start], points[end]);
                if (distance > maximum)
                {
                    maximum = distance;
                    index = i;
                }
            }
            if (index >= 0 && maximum > tolerance)
            {
                keep[index] = true;
                pending.Push((start, index));
                pending.Push((index, end));
            }
        }

        return anchors.Where((_, index) => keep[index]).ToArray();
    }

    private IReadOnlyList<DrawingAnchor> SmoothFreehandAnchors(
        IReadOnlyList<DrawingAnchor> anchors,
        ChartLayout layout,
        int passes)
    {
        if (anchors.Count < 3 || passes <= 0)
            return anchors.ToArray();

        Point[] points = anchors.Select(anchor => AnchorToPoint(anchor, layout)).ToArray();
        for (int pass = 0; pass < passes; pass++)
        {
            Point[] next = points.ToArray();
            for (int index = 1; index < points.Length - 1; index++)
            {
                next[index] = new Point(
                    (points[index - 1].X + points[index].X * 2.0 + points[index + 1].X) / 4.0,
                    (points[index - 1].Y + points[index].Y * 2.0 + points[index + 1].Y) / 4.0);
            }
            points = next;
        }

        return points.Select(point => CreateAnchorWithoutConstraints(point, layout)).ToArray();
    }

    private static void DrawSmoothFreehand(DrawingContext dc, Point[] points, Pen pen)
    {
        if (points.Length < 2)
            return;
        if (points.Length == 2)
        {
            dc.DrawLine(pen, points[0], points[1]);
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (int index = 1; index < points.Length - 1; index++)
            {
                Point midpoint = new(
                    (points[index].X + points[index + 1].X) / 2.0,
                    (points[index].Y + points[index + 1].Y) / 2.0);
                context.QuadraticBezierTo(points[index], midpoint, true, false);
            }
            context.QuadraticBezierTo(points[^2], points[^1], true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawLineWithOptions(DrawingContext dc, Rect plot, Point p1, Point p2, Pen pen, DrawingStyle style)
    {
        if (style.ExtendLeft || style.ExtendRight)
        {
            if (style.ExtendLeft && style.ExtendRight) DrawRay(dc, plot, p1, p2, pen, true);
            else if (style.ExtendRight) DrawRay(dc, plot, p1, p2, pen, false);
            else DrawRay(dc, plot, p2, p1, pen, false);
        }
        else dc.DrawLine(pen, p1, p2);
        if (style.ArrowStart) DrawArrowHead(dc, pen, p2, p1);
        if (style.ArrowEnd) DrawArrowHead(dc, pen, p1, p2);
    }

    private static void DrawRay(DrawingContext dc, Rect plot, Point p1, Point p2, Pen pen, bool bothDirections)
    {
        Vector direction = p2 - p1;
        if (direction.LengthSquared < 0.0001)
            return;
        direction.Normalize();
        double extent = Math.Max(plot.Width, plot.Height) * 4;
        Point start = bothDirections ? p1 - direction * extent : p1;
        Point end = p1 + direction * extent;
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawLine(pen, start, end);
        dc.Pop();
    }

    private static void DrawHorizontal(DrawingContext dc, Rect plot, Point point, Pen pen, bool left, bool right)
    {
        double x1 = left ? plot.Left : point.X;
        double x2 = right ? plot.Right : point.X;
        dc.DrawLine(pen, new Point(x1, point.Y), new Point(x2, point.Y));
    }

    private static void DrawArrowHead(DrawingContext dc, Pen pen, Point from, Point tip)
    {
        Vector v = from - tip;
        if (v.LengthSquared < 0.001) return;
        v.Normalize();
        Vector normal = new(-v.Y, v.X);
        double size = Math.Max(7, pen.Thickness * 3.5);
        Point a = tip + v * size + normal * size * 0.45;
        Point b = tip + v * size - normal * size * 0.45;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(tip, true, true);
            context.LineTo(a, true, false);
            context.LineTo(b, true, false);
        }
        dc.DrawGeometry(pen.Brush, null, geometry);
    }

    private static void DrawChannel(DrawingContext dc, Rect plot, Point[] points, Pen pen, Brush fill, DrawingStyle style)
    {
        Vector offset = points[2] - points[0];
        Point p3 = points[0] + offset;
        Point p4 = points[1] + offset;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            ctx.LineTo(points[1], true, false);
            ctx.LineTo(p4, true, false);
            ctx.LineTo(p3, true, false);
        }
        dc.DrawGeometry(fill, null, geometry);
        DrawLineWithOptions(dc, plot, points[0], points[1], pen, style);
        DrawLineWithOptions(dc, plot, p3, p4, pen, style);
        if (style.ShowMiddleLine)
            DrawLineWithOptions(dc, plot, Mid(points[0], p3), Mid(points[1], p4), pen, style);
    }

    private void DrawRegression(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen, Brush fill)
    {
        int a = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int b = FindNearestDrawingCandleIndex(drawing.Anchors[1]);
        int start = Math.Min(a, b);
        int end = Math.Max(a, b);
        if (end - start < 1) return;
        int n = end - start + 1;
        double sx = n * (n - 1) / 2.0;
        double sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            double y = DrawingCandles[start + i].Close;
            sy += y; sxx += i * i; sxy += i * y;
        }
        double denominator = n * sxx - sx * sx;
        double slope = Math.Abs(denominator) < 1e-12 ? 0 : (n * sxy - sx * sy) / denominator;
        double intercept = (sy - slope * sx) / n;
        double variance = 0;
        for (int i = 0; i < n; i++)
        {
            double residual = DrawingCandles[start + i].Close - (intercept + slope * i);
            variance += residual * residual;
        }
        double std = Math.Sqrt(variance / Math.Max(1, n));
        DrawingAnchor first = CreateDrawingAnchorAtIndex(start, intercept);
        DrawingAnchor last = CreateDrawingAnchorAtIndex(end, intercept + slope * (n - 1));
        Point p1 = AnchorToPoint(first, layout);
        Point p2 = AnchorToPoint(last, layout);
        Point u1 = AnchorToPoint(first with { Price = first.Price + std }, layout);
        Point u2 = AnchorToPoint(last with { Price = last.Price + std }, layout);
        Point l1 = AnchorToPoint(first with { Price = first.Price - std }, layout);
        Point l2 = AnchorToPoint(last with { Price = last.Price - std }, layout);
        var area = new StreamGeometry();
        using (StreamGeometryContext ctx = area.Open())
        {
            ctx.BeginFigure(u1, true, true); ctx.LineTo(u2, true, false); ctx.LineTo(l2, true, false); ctx.LineTo(l1, true, false);
        }
        dc.DrawGeometry(fill, null, area);
        dc.DrawLine(pen, p1, p2); dc.DrawLine(pen, u1, u2); dc.DrawLine(pen, l1, l2);
    }

    private void DrawAnchoredVwap(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Pen pen)
    {
        int start = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int end = Math.Min(DrawingCandles.Count - 1, layout.LastExclusive - 1);
        if (start >= end) return;
        double cumulativeVolume = 0;
        double cumulativeValue = 0;
        double cumulativeSquared = 0;
        var main = new StreamGeometry();
        var upper1 = new StreamGeometry();
        var lower1 = new StreamGeometry();
        using StreamGeometryContext mainCtx = main.Open();
        using StreamGeometryContext upCtx = upper1.Open();
        using StreamGeometryContext lowCtx = lower1.Open();
        bool begun = false;
        for (int i = start; i <= end; i++)
        {
            Candle candle = DrawingCandles[i];
            double typical = (candle.High + candle.Low + candle.Close) / 3.0;
            double volume = Math.Max(1, candle.TickVolume);
            cumulativeVolume += volume;
            cumulativeValue += typical * volume;
            cumulativeSquared += typical * typical * volume;
            double vwap = cumulativeValue / cumulativeVolume;
            double variance = Math.Max(0, cumulativeSquared / cumulativeVolume - vwap * vwap);
            double std = Math.Sqrt(variance);
            Point p = AnchorToPoint(CreateDrawingAnchorAtIndex(i, vwap), layout);
            Point u = AnchorToPoint(CreateDrawingAnchorAtIndex(i, vwap + std), layout);
            Point l = AnchorToPoint(CreateDrawingAnchorAtIndex(i, vwap - std), layout);
            if (!begun)
            {
                mainCtx.BeginFigure(p, false, false); upCtx.BeginFigure(u, false, false); lowCtx.BeginFigure(l, false, false); begun = true;
            }
            else
            {
                mainCtx.LineTo(p, true, false); upCtx.LineTo(u, true, false); lowCtx.LineTo(l, true, false);
            }
        }
        dc.PushClip(new RectangleGeometry(layout.Plot));
        dc.DrawGeometry(null, pen, main);
        var bandPen = new Pen(CreateDrawingBrush(drawing.Style.LineColor, drawing.Style.Opacity * 0.55), Math.Max(1, pen.Thickness * 0.75));
        dc.DrawGeometry(null, bandPen, upper1); dc.DrawGeometry(null, bandPen, lower1);
        dc.Pop();
    }

    private void DrawFibonacci(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen basePen, Brush fill, bool extension)
    {
        double startPrice = drawing.Anchors[0].Price;
        double endPrice = drawing.Anchors[1].Price;
        double x1 = Math.Min(points[0].X, points[1].X);
        double x2 = extension || drawing.Style.ExtendRight ? layout.Plot.Right : Math.Max(points[0].X, points[1].X);
        DrawingLevel[] levels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingToolCatalog.DefaultFibonacciLevels()).Where(item => item.Enabled).ToArray();
        for (int i = 0; i < levels.Length; i++)
        {
            DrawingLevel level = levels[i];
            double price = startPrice + (endPrice - startPrice) * level.Value;
            double y = PriceToY(price, layout);
            Pen pen = new(CreateDrawingBrush(level.Color, drawing.Style.Opacity), Math.Max(0.5, level.Width))
            {
                DashStyle = level.LineStyle switch { DrawingLineStyle.Dashed => DashStyles.Dash, DrawingLineStyle.Dotted => DashStyles.Dot, _ => DashStyles.Solid }
            };
            dc.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            if (level.ShowValue || level.ShowPrice)
            {
                string label = (level.ShowValue ? level.Label : string.Empty) +
                    (level.ShowValue && level.ShowPrice ? "  " : string.Empty) +
                    (level.ShowPrice ? price.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
                DrawSmallLabel(dc, label, new Point(x1 + 4, y - 14), CreateDrawingBrush(level.Color, 1));
            }
            if (i + 1 < levels.Length && drawing.Style.FillOpacity > 0)
            {
                double nextPrice = startPrice + (endPrice - startPrice) * levels[i + 1].Value;
                double nextY = PriceToY(nextPrice, layout);
                string zoneColor = string.IsNullOrWhiteSpace(level.FillColor)
                    ? drawing.Style.FillColor
                    : level.FillColor;
                double zoneOpacity = level.FillOpacity >= 0
                    ? Math.Clamp(level.FillOpacity, 0, 1)
                    : Math.Clamp(drawing.Style.FillOpacity, 0, 1);
                dc.DrawRectangle(
                    CreateDrawingBrush(zoneColor, zoneOpacity),
                    null,
                    new Rect(new Point(x1, Math.Min(y, nextY)), new Point(x2, Math.Max(y, nextY))));
            }
        }
        dc.DrawLine(basePen, points[0], points[1]);
    }

    private void DrawFibonacciChannel(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        Vector offset = points[2] - points[0];
        foreach (DrawingLevel level in (drawing.Levels.Count > 0 ? drawing.Levels : DrawingToolCatalog.DefaultFibonacciLevels()).Where(item => item.Enabled))
        {
            Point a = points[0] + offset * level.Value;
            Point b = points[1] + offset * level.Value;
            dc.DrawLine(new Pen(CreateDrawingBrush(level.Color, drawing.Style.Opacity), level.Width), a, b);
            DrawSmallLabel(dc, level.Label, a + new Vector(4, -14), CreateDrawingBrush(level.Color, 1));
        }
        DrawChannel(dc, layout.Plot, points, pen, fill, drawing.Style);
    }

    private void DrawFibonacciTime(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen)
    {
        double span = points[1].X - points[0].X;
        foreach (DrawingLevel level in (drawing.Levels.Count > 0 ? drawing.Levels : DrawingToolCatalog.DefaultFibonacciLevels()).Where(item => item.Enabled && item.Value >= 0))
        {
            double x = points[0].X + span * level.Value;
            Pen levelPen = new(CreateDrawingBrush(level.Color, drawing.Style.Opacity), level.Width) { DashStyle = DashStyles.Dash };
            dc.DrawLine(levelPen, new Point(x, layout.Plot.Top), new Point(x, layout.Plot.Bottom));
            DrawSmallLabel(dc, level.Label, new Point(x + 3, layout.Plot.Top + 3), CreateDrawingBrush(level.Color, 1));
        }
    }

    private static void DrawFan(DrawingContext dc, Rect plot, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Point origin = points[0];
        Vector vector = points[1] - points[0];
        double[] ratios = { 0.25, 0.382, 0.5, 0.618, 0.75, 1, 1.5, 2 };
        foreach (double ratio in ratios)
        {
            Point target = new(points[1].X, points[0].Y + vector.Y * ratio);
            DrawRay(dc, plot, origin, target, pen, false);
        }
    }

    private static void DrawFibCircles(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Vector radius = points[1] - points[0];
        double rx = Math.Abs(radius.X);
        double ry = Math.Abs(radius.Y);
        double[] levels = { 0.236, 0.382, 0.5, 0.618, 1, 1.618 };
        foreach (double level in levels)
            dc.DrawEllipse(null, pen, points[0], Math.Max(1, rx * level), Math.Max(1, ry * level));
    }

    private static void DrawSpiral(DrawingContext dc, Point center, Point end, Pen pen)
    {
        double maxRadius = Distance(center, end);
        double startAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            const int steps = 160;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double angle = startAngle + t * Math.PI * 4.0;
                double radius = maxRadius * t;
                Point p = new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, false);
            }
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawFibWedge(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Point center = points[0];
        Vector a = points[1] - center;
        Vector b = points[2] - center;
        double[] ratios = { 0.236, 0.382, 0.5, 0.618, 1 };
        foreach (double ratio in ratios)
        {
            Point p1 = center + a * ratio;
            Point p2 = center + b * ratio;
            dc.DrawLine(pen, center, p1);
            dc.DrawLine(pen, center, p2);
            dc.DrawLine(pen, p1, p2);
        }
    }

    private static void DrawFibArcs(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen)
    {
        double radius = Distance(points[0], points[1]);
        foreach (double level in new[] { 0.382, 0.5, 0.618, 1, 1.618 })
            dc.DrawEllipse(null, pen, points[0], radius * level, radius * level);
    }

    private static void DrawPitchfork(DrawingContext dc, Rect plot, ChartDrawing drawing, Point[] points, Pen pen)
    {
        Point midpoint = Mid(points[1], points[2]);
        DrawRay(dc, plot, points[0], midpoint, pen, false);
        Vector direction = midpoint - points[0];
        DrawRay(dc, plot, points[1], points[1] + direction, pen, false);
        DrawRay(dc, plot, points[2], points[2] + direction, pen, false);
        foreach (double ratio in new[] { 0.25, 0.5, 0.75 })
        {
            Point start = points[1] + (points[2] - points[1]) * ratio;
            DrawRay(dc, plot, start, start + direction, new Pen(pen.Brush, Math.Max(0.5, pen.Thickness * 0.65)) { DashStyle = DashStyles.Dash }, false);
        }
    }

    private static void DrawGannBox(DrawingContext dc, Point p1, Point p2, Pen pen, Brush fill)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        dc.DrawRectangle(fill, pen, rect);
        for (int i = 1; i < 8; i++)
        {
            double x = rect.Left + rect.Width * i / 8.0;
            double y = rect.Top + rect.Height * i / 8.0;
            dc.DrawLine(new Pen(pen.Brush, Math.Max(0.5, pen.Thickness * 0.55)), new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(new Pen(pen.Brush, Math.Max(0.5, pen.Thickness * 0.55)), new Point(rect.Left, y), new Point(rect.Right, y));
        }
        dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
        dc.DrawLine(pen, rect.BottomLeft, rect.TopRight);
    }

    private static void DrawPolyline(DrawingContext dc, Point[] points, Pen pen, bool close)
    {
        if (points.Length < 2) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, close);
            for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawRectangleDrawing(DrawingContext dc, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush fill, Brush textBrush)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        dc.DrawRectangle(fill, pen, rect);
        if (drawing.Style.ShowMiddleLine)
            dc.DrawLine(pen, new Point(rect.Left, rect.Top + rect.Height / 2), new Point(rect.Right, rect.Top + rect.Height / 2));
        DrawTextInsideShape(dc, drawing, rect, textBrush);
    }

    private void DrawRotatedRectangle(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen, Brush fill, Brush textBrush)
    {
        Vector width = points[1] - points[0];
        Vector normal = new(-width.Y, width.X);
        if (normal.LengthSquared < 0.001) return;
        normal.Normalize();
        double height = Vector.Multiply(points[2] - points[0], normal);
        Vector offset = normal * height;
        Point[] polygon = { points[0], points[1], points[1] + offset, points[0] + offset };
        DrawPolygon(dc, polygon, pen, fill);
        Rect bounds = Bounds(polygon);
        DrawTextInsideShape(dc, drawing, bounds, textBrush);
    }

    private void DrawEllipseDrawing(DrawingContext dc, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush fill, Brush textBrush)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
        DrawTextInsideShape(dc, drawing, rect, textBrush);
    }

    private static void DrawPolygon(DrawingContext dc, Point[] points, Pen pen, Brush fill)
    {
        if (points.Length < 3) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
        }
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawCurve(DrawingContext dc, Point[] points, Pen pen)
    {
        if (points.Length < 3) { DrawPolyline(dc, points, pen, false); return; }
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            if (points.Length >= 4)
                ctx.BezierTo(points[1], points[2], points[3], true, false);
            else
                ctx.QuadraticBezierTo(points[1], points[2], true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawDoubleCurve(DrawingContext dc, Point[] points, Pen pen)
    {
        // TradingView Double Curve is one cubic Bezier controlled by two generated
        // middle handles. The old TickLab implementation rendered the same curve a
        // second time with an offset, which looked like a doubled line.
        if (points.Length < 4) { DrawCurve(dc, points, pen); return; }
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.BezierTo(points[1], points[2], points[3], true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private static StreamGeometry CreateArcGeometry(Point[] points)
    {
        var geometry = new StreamGeometry();
        if (points.Length < 3)
            return geometry;
        using (StreamGeometryContext ctx = geometry.Open())
        {
            // TradingView Arc: endpoint -> shaping point -> endpoint, with the
            // straight endpoint chord closing the filled segment.
            ctx.BeginFigure(points[0], true, true);
            ctx.QuadraticBezierTo(points[1], points[2], true, false);
        }
        return geometry;
    }

    private static void DrawArc(DrawingContext dc, Point[] points, Pen pen, Brush fill)
    {
        StreamGeometry geometry = CreateArcGeometry(points);
        dc.DrawGeometry(fill, pen, geometry);
    }

    private void DrawImageDrawing(DrawingContext dc, ChartDrawing drawing, Point p1, Point p2, bool preview)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        if (rect.Width < 1 || rect.Height < 1)
            return;

        if (!drawing.TextOptions.TryGetValue("ImagePath", out string? path) || string.IsNullOrWhiteSpace(path))
        {
            DrawMissingImagePlaceholder(dc, rect, drawing.Style.Opacity);
            return;
        }

        BitmapSource? bitmap = TryGetDrawingImage(path);
        if (bitmap is null)
        {
            DrawMissingImagePlaceholder(dc, rect, drawing.Style.Opacity);
            return;
        }

        dc.PushOpacity(Math.Clamp(drawing.Style.Opacity * (preview ? 0.65 : 1.0), 0.05, 1.0));
        dc.DrawImage(bitmap, rect);
        dc.Pop();
    }

    private BitmapSource? TryGetDrawingImage(string path)
    {
        if (_drawingImageCache.TryGetValue(path, out BitmapSource? cached))
            return cached;
        try
        {
            if (!System.IO.File.Exists(path))
                return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _drawingImageCache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawMissingImagePlaceholder(DrawingContext dc, Rect rect, double opacity)
    {
        Brush background = new SolidColorBrush(Color.FromArgb((byte)(180 * Math.Clamp(opacity, 0, 1)), 15, 23, 42));
        Brush line = new SolidColorBrush(Color.FromArgb((byte)(220 * Math.Clamp(opacity, 0, 1)), 100, 116, 139));
        dc.DrawRectangle(background, new Pen(line, 1), rect);
        dc.DrawLine(new Pen(line, 1), rect.TopLeft, rect.BottomRight);
        dc.DrawLine(new Pen(line, 1), rect.TopRight, rect.BottomLeft);
    }

    private void DrawAnnotation(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill, Brush textBrush, DrawingGeometryKind geometry)
    {
        Point anchor = points[0];
        string text = string.IsNullOrWhiteSpace(drawing.Text) ? drawing.DisplayName : drawing.Text;
        if (geometry == DrawingGeometryKind.PriceLabel)
            text = string.IsNullOrWhiteSpace(drawing.Text)
                ? drawing.Anchors[0].Price.ToString("0.########", CultureInfo.InvariantCulture)
                : drawing.Text;
        if (geometry == DrawingGeometryKind.Flag)
        {
            dc.DrawLine(pen, anchor, anchor + new Vector(0, -32));
            var flag = new StreamGeometry();
            using (StreamGeometryContext ctx = flag.Open())
            {
                Point top = anchor + new Vector(0, -32);
                ctx.BeginFigure(top, true, true);
                ctx.LineTo(top + new Vector(24, 7), true, false);
                ctx.LineTo(top + new Vector(0, 14), true, false);
            }
            dc.DrawGeometry(fill, pen, flag);
            return;
        }
        if (geometry == DrawingGeometryKind.Icon)
        {
            if (DrawingMediaCatalog.TryDecode(text, out DrawingMediaDefinition? media) && media is not null)
            {
                DrawTickLabMedia(dc, anchor, media, drawing.Style.Opacity, GetDrawingMediaScale(drawing));
                return;
            }

            FormattedText icon = CreateDrawingText(
                text,
                drawing.Style,
                textBrush,
                Math.Max(18, drawing.Style.FontSize * 1.6));
            dc.DrawText(icon, anchor - new Vector(icon.Width / 2, icon.Height / 2));
            return;
        }

        FormattedText formatted = CreateDrawingText(text, drawing.Style, textBrush, drawing.Style.FontSize);
        double padding = geometry == DrawingGeometryKind.Text ? 0 : 7;
        Rect box = new(anchor.X, anchor.Y - formatted.Height - padding * 2, formatted.Width + padding * 2, formatted.Height + padding * 2);
        if (geometry is DrawingGeometryKind.Note or DrawingGeometryKind.Callout or DrawingGeometryKind.PriceLabel)
        {
            dc.DrawRoundedRectangle(fill, pen, box, 4, 4);
            if (geometry == DrawingGeometryKind.Callout && points.Length >= 2)
                dc.DrawLine(pen, points[1], new Point(box.Left + box.Width / 2, box.Bottom));
        }
        dc.DrawText(formatted, new Point(box.Left + padding, box.Top + padding));
    }

    private const string DrawingMediaScaleOption = "MediaScale";
    private const double MinimumDrawingMediaScale = 0.20;
    private const double MaximumDrawingMediaScale = 12.0;

    private static double GetDrawingMediaScale(ChartDrawing drawing)
    {
        if (drawing.NumericOptions.TryGetValue(DrawingMediaScaleOption, out double value) &&
            double.IsFinite(value) && value > 0)
        {
            return Math.Clamp(value, MinimumDrawingMediaScale, MaximumDrawingMediaScale);
        }
        return 1.0;
    }

    private static IReadOnlyDictionary<string, double> WithDrawingMediaScale(
        IReadOnlyDictionary<string, double> source,
        double scale)
    {
        var options = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, double value) in source)
            options[key] = value;
        options[DrawingMediaScaleOption] = Math.Clamp(scale, MinimumDrawingMediaScale, MaximumDrawingMediaScale);
        return options;
    }

    private void DrawTickLabMedia(
        DrawingContext dc,
        Point anchor,
        DrawingMediaDefinition media,
        double opacity,
        double scale)
    {
        double safeScale = Math.Clamp(
            double.IsFinite(scale) && scale > 0 ? scale : 1.0,
            MinimumDrawingMediaScale,
            MaximumDrawingMediaScale);

        // All Emoji-folder graphics are vector/text visuals and are scaled through the
        // WPF drawing transform. This keeps the aspect ratio locked and avoids bitmap
        // stretching/pixel break-up at large or small sizes.
        dc.PushTransform(new ScaleTransform(safeScale, safeScale, anchor.X, anchor.Y));
        try
        {
            if (string.Equals(media.Type, "emojis", StringComparison.OrdinalIgnoreCase))
            {
                DrawColorEmojiVisual(dc, anchor, media.Mark, opacity);
                return;
            }

            Brush primary = CreateDrawingBrush(media.PrimaryColor, opacity);
            Brush secondary = CreateDrawingBrush(media.SecondaryColor, opacity);
            var outline = new Pen(secondary, 1.6)
            {
                LineJoin = PenLineJoin.Round
            };

            if (string.Equals(media.Type, "stickers", StringComparison.OrdinalIgnoreCase))
            {
                FormattedText label = CreateText(media.Label, media.Label.Length > 11 ? 9.5 : 11.5, Brushes.White);
                double width = Math.Clamp(label.Width + 24.0, 78.0, 158.0);
                Rect shadow = new(anchor.X - width / 2.0 + 2.0, anchor.Y - 15.0 + 3.0, width, 32.0);
                Rect body = new(anchor.X - width / 2.0, anchor.Y - 16.0, width, 32.0);
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb((byte)(70 * Math.Clamp(opacity, 0, 1)), 0, 0, 0)),
                    null,
                    shadow,
                    9,
                    9);
                dc.DrawRoundedRectangle(primary, outline, body, 9, 9);
                dc.DrawText(
                    label,
                    new Point(
                        body.Left + (body.Width - label.Width) / 2.0,
                        body.Top + (body.Height - label.Height) / 2.0));
                return;
            }

            const double radius = 24.0;
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb((byte)(70 * Math.Clamp(opacity, 0, 1)), 0, 0, 0)),
                null,
                anchor + new Vector(2, 3),
                radius,
                radius);
            dc.DrawEllipse(primary, outline, anchor, radius, radius);
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb((byte)(38 * Math.Clamp(opacity, 0, 1)), 255, 255, 255)),
                null,
                anchor - new Vector(5, 6),
                13,
                11);

            FormattedText mark = CreateText(
                media.Mark,
                media.Mark.Length > 1 ? 15 : 22,
                Brushes.White);
            dc.DrawText(
                mark,
                new Point(
                    anchor.X - mark.Width / 2.0,
                    anchor.Y - mark.Height / 2.0));
        }
        finally
        {
            dc.Pop();
        }
    }

    private void DrawColorEmojiVisual(DrawingContext dc, Point anchor, string mark, double opacity)
    {
        DrawingImage emojiImage = GetColorEmojiImage(mark);
        Size size = GetColorEmojiBaseSize(mark);
        Rect destination = new(
            anchor.X - size.Width / 2.0,
            anchor.Y - size.Height / 2.0,
            size.Width,
            size.Height);

        dc.PushOpacity(Math.Clamp(opacity, 0.05, 1.0));
        dc.DrawImage(emojiImage, destination);
        dc.Pop();
    }

    private DrawingImage GetColorEmojiImage(string mark)
    {
        if (_drawingEmojiImageCache.TryGetValue(mark, out DrawingImage? cached))
            return cached;

        // Every Emoji-folder mark is resolved from TickLab's bundled offline
        // vector pack. Never fall back to Segoe UI Emoji: older Windows builds
        // show unsupported sequences as empty/tofu rectangles.
        DrawingImage image = EmojiVectorAssets.GetDrawingImageOrPlaceholder(mark);
        _drawingEmojiImageCache[mark] = image;
        return image;
    }

    private Size GetColorEmojiBaseSize(string mark)
    {
        DrawingImage image = GetColorEmojiImage(mark);
        Rect bounds = image.Drawing?.Bounds ?? Rect.Empty;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return new Size(48.0, 48.0);
        }

        const double maxDimension = 48.0;
        double aspect = bounds.Width / bounds.Height;
        if (!double.IsFinite(aspect) || aspect <= 0)
            return new Size(48.0, 48.0);

        return aspect >= 1.0
            ? new Size(maxDimension, maxDimension / aspect)
            : new Size(maxDimension * aspect, maxDimension);
    }

    private static bool IsCartoonFace(string key) =>
        key is "happy" or "laugh" or "wink" or "cool" or "thinking" or
            "surprised" or "sad" or "angry" or "tired" or "party" or
            "love" or "confident";

    private void DrawCartoonFace(
        DrawingContext dc,
        Point center,
        string key,
        double opacity)
    {
        Brush ink = CreateDrawingBrush("#102033", opacity);
        var inkPen = new Pen(ink, 2.1)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        if (key == "cool")
        {
            Rect leftLens = new(center.X - 15, center.Y - 8, 12, 8);
            Rect rightLens = new(center.X + 3, center.Y - 8, 12, 8);
            dc.DrawRoundedRectangle(ink, inkPen, leftLens, 2, 2);
            dc.DrawRoundedRectangle(ink, inkPen, rightLens, 2, 2);
            dc.DrawLine(inkPen, new Point(center.X - 3, center.Y - 5), new Point(center.X + 3, center.Y - 5));
        }
        else if (key == "love")
        {
            FormattedText hearts = CreateText("♥  ♥", 12, CreateDrawingBrush("#EF476F", opacity));
            dc.DrawText(hearts, new Point(center.X - hearts.Width / 2, center.Y - 13));
        }
        else
        {
            if (key == "wink")
            {
                dc.DrawLine(inkPen, new Point(center.X - 13, center.Y - 6), new Point(center.X - 5, center.Y - 6));
                dc.DrawEllipse(ink, null, new Point(center.X + 9, center.Y - 6), 2.1, 2.1);
            }
            else if (key == "tired")
            {
                dc.DrawLine(inkPen, new Point(center.X - 13, center.Y - 6), new Point(center.X - 5, center.Y - 6));
                dc.DrawLine(inkPen, new Point(center.X + 5, center.Y - 6), new Point(center.X + 13, center.Y - 6));
            }
            else if (key == "angry")
            {
                dc.DrawLine(inkPen, new Point(center.X - 14, center.Y - 10), new Point(center.X - 5, center.Y - 6));
                dc.DrawLine(inkPen, new Point(center.X + 5, center.Y - 6), new Point(center.X + 14, center.Y - 10));
                dc.DrawEllipse(ink, null, new Point(center.X - 8, center.Y - 4), 1.8, 1.8);
                dc.DrawEllipse(ink, null, new Point(center.X + 8, center.Y - 4), 1.8, 1.8);
            }
            else
            {
                dc.DrawEllipse(ink, null, new Point(center.X - 8, center.Y - 6), 2.2, 2.2);
                dc.DrawEllipse(ink, null, new Point(center.X + 8, center.Y - 6), 2.2, 2.2);
            }
        }

        if (key is "sad" or "angry" or "tired")
        {
            var mouth = new StreamGeometry();
            using (StreamGeometryContext context = mouth.Open())
            {
                context.BeginFigure(new Point(center.X - 9, center.Y + 11), false, false);
                context.QuadraticBezierTo(
                    new Point(center.X, center.Y + 3),
                    new Point(center.X + 9, center.Y + 11),
                    true,
                    false);
            }
            dc.DrawGeometry(null, inkPen, mouth);
        }
        else if (key == "surprised" || key == "thinking")
        {
            dc.DrawEllipse(null, inkPen, new Point(center.X, center.Y + 9), 4.0, 5.0);
        }
        else if (key == "laugh")
        {
            Rect mouth = new(center.X - 10, center.Y + 4, 20, 12);
            dc.DrawRoundedRectangle(ink, inkPen, mouth, 7, 7);
            dc.DrawLine(
                new Pen(CreateDrawingBrush("#F8FAFC", opacity), 1.2),
                new Point(mouth.Left + 4, mouth.Top + 3),
                new Point(mouth.Right - 4, mouth.Top + 3));
        }
        else
        {
            var mouth = new StreamGeometry();
            using (StreamGeometryContext context = mouth.Open())
            {
                context.BeginFigure(new Point(center.X - 10, center.Y + 5), false, false);
                context.QuadraticBezierTo(
                    new Point(center.X, center.Y + 15),
                    new Point(center.X + 10, center.Y + 5),
                    true,
                    false);
            }
            dc.DrawGeometry(null, inkPen, mouth);
        }

        if (key == "party")
        {
            var hat = new StreamGeometry();
            using (StreamGeometryContext context = hat.Open())
            {
                context.BeginFigure(new Point(center.X - 12, center.Y - 19), true, true);
                context.LineTo(new Point(center.X + 2, center.Y - 36), true, false);
                context.LineTo(new Point(center.X + 8, center.Y - 17), true, false);
            }
            dc.DrawGeometry(
                CreateDrawingBrush("#846EF6", opacity),
                new Pen(CreateDrawingBrush("#D8D1FF", opacity), 1.2),
                hat);
        }
    }

    private Rect GetTickLabMediaBounds(Point anchor, DrawingMediaDefinition media, ChartDrawing? drawing = null)
    {
        Rect baseBounds;
        if (string.Equals(media.Type, "emojis", StringComparison.OrdinalIgnoreCase))
        {
            Size emojiSize = GetColorEmojiBaseSize(media.Mark);
            baseBounds = new Rect(
                anchor.X - emojiSize.Width / 2.0,
                anchor.Y - emojiSize.Height / 2.0,
                emojiSize.Width,
                emojiSize.Height);
        }
        else if (string.Equals(media.Type, "stickers", StringComparison.OrdinalIgnoreCase))
        {
            FormattedText label = CreateText(media.Label, media.Label.Length > 11 ? 9.5 : 11.5, Brushes.White);
            double width = Math.Clamp(label.Width + 24.0, 78.0, 158.0);
            baseBounds = new Rect(anchor.X - width / 2.0, anchor.Y - 16.0, width, 35.0);
        }
        else
        {
            baseBounds = new Rect(anchor.X - 26.0, anchor.Y - 26.0, 52.0, 52.0);
        }

        double scale = drawing is null ? 1.0 : GetDrawingMediaScale(drawing);
        if (Math.Abs(scale - 1.0) < 0.000001)
            return baseBounds;

        return new Rect(
            anchor.X - baseBounds.Width * scale / 2.0,
            anchor.Y - baseBounds.Height * scale / 2.0,
            baseBounds.Width * scale,
            baseBounds.Height * scale);
    }

    private static Point[] BuildArrowMarkerPolygon(Point start, Point end)
    {
        Vector direction = end - start;
        double length = direction.Length;
        if (length < 1.0)
            return Array.Empty<Point>();
        direction.Normalize();
        Vector normal = new(-direction.Y, direction.X);
        double headLength = Math.Clamp(length * 0.32, 12.0, 42.0);
        double shaftHalf = Math.Clamp(length * 0.065, 3.0, 12.0);
        double headHalf = Math.Clamp(length * 0.18, 8.0, 28.0);
        Point neck = end - direction * headLength;
        return new[]
        {
            start + normal * shaftHalf,
            neck + normal * shaftHalf,
            neck + normal * headHalf,
            end,
            neck - normal * headHalf,
            neck - normal * shaftHalf,
            start - normal * shaftHalf
        };
    }

    private static void DrawArrowMarker(DrawingContext dc, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        string id = drawing.ToolId;
        if (id == "arrow-marker" && points.Length >= 2)
        {
            Point[] polygon = BuildArrowMarkerPolygon(points[0], points[1]);
            if (polygon.Length >= 3)
                DrawPolygon(dc, polygon, pen, fill);
            return;
        }

        // Arrow mark up/down remain one-click fixed-direction markers; only the
        // generic Arrow Marker becomes the two-point scalable/rotatable object.
        Point point = points[0];
        Vector direction = id.Contains("left") ? new Vector(-1, 0) :
            id.Contains("right") ? new Vector(1, 0) :
            id.Contains("down") ? new Vector(0, 1) : new Vector(0, -1);
        Point from = point - direction * 18;
        dc.DrawLine(pen, from, point);
        DrawArrowHead(dc, pen, from, point);
    }

    private void DrawPatternLabels(DrawingContext dc, ChartDrawing drawing, Point[] points, Brush textBrush)
    {
        string[] labels = drawing.ToolId switch
        {
            "xabcd-pattern" or "cypher-pattern" => new[] { "X", "A", "B", "C", "D" },
            "abcd-pattern" => new[] { "A", "B", "C", "D" },
            "elliott-impulse" => new[] { "0", "1", "2", "3", "4", "5" },
            "elliott-triangle" => new[] { "0", "A", "B", "C", "D", "E" },
            "elliott-correction" => new[] { "0", "A", "B", "C" },
            "elliott-double-combo" => new[] { "0", "W", "X", "Y" },
            "elliott-triple-combo" => new[] { "W", "X", "Y", "X", "Z", "" },
            _ => Enumerable.Range(1, points.Length).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()
        };
        for (int i = 0; i < points.Length && i < labels.Length; i++)
            DrawSmallLabel(dc, labels[i], points[i] + new Vector(4, -18), textBrush);
    }

    private static void DrawCycles(DrawingContext dc, Rect plot, ChartDrawing drawing, Point[] points, Pen pen)
    {
        double spacing = Math.Abs(points[1].X - points[0].X);
        if (spacing < 4) return;
        if (drawing.ToolId == "time-cycles")
        {
            double radius = spacing / 2;
            for (double x = points[0].X; x <= plot.Right + spacing; x += spacing)
                dc.DrawEllipse(null, pen, new Point(x, points[0].Y), radius, Math.Min(radius, plot.Height / 2));
        }
        else
        {
            for (double x = points[0].X; x <= plot.Right + spacing; x += spacing)
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            for (double x = points[0].X - spacing; x >= plot.Left - spacing; x -= spacing)
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private static void DrawSine(DrawingContext dc, Point p1, Point p2, Pen pen)
    {
        double width = p2.X - p1.X;
        double amplitude = Math.Abs(p2.Y - p1.Y);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            const int steps = 120;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                Point p = new(p1.X + width * t, p1.Y + Math.Sin(t * Math.PI * 4) * amplitude);
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, false);
            }
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawPosition(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush textBrush)
    {
        Point entry = points[0];
        Point target = points[1];
        Point stop = points[2];
        double right = Math.Max(entry.X, Math.Max(target.X, stop.X));
        Brush profit = CreateDrawingBrush("#22C55E", 0.18);
        Brush loss = CreateDrawingBrush("#EF4444", 0.18);
        bool isShort = drawing.ToolId == "short-position";
        Rect targetRect = new(new Point(entry.X, Math.Min(entry.Y, target.Y)), new Point(right, Math.Max(entry.Y, target.Y)));
        Rect stopRect = new(new Point(entry.X, Math.Min(entry.Y, stop.Y)), new Point(right, Math.Max(entry.Y, stop.Y)));
        dc.DrawRectangle(isShort ? loss : profit, pen, targetRect);
        dc.DrawRectangle(isShort ? profit : loss, pen, stopRect);
        dc.DrawLine(pen, new Point(entry.X, entry.Y), new Point(right, entry.Y));
        double risk = Math.Abs(drawing.Anchors[0].Price - drawing.Anchors[2].Price);
        double reward = Math.Abs(drawing.Anchors[1].Price - drawing.Anchors[0].Price);
        double rr = risk <= 0 ? 0 : reward / risk;
        string label = $"Entry {drawing.Anchors[0].Price:0.#####}  Target {drawing.Anchors[1].Price:0.#####}  Stop {drawing.Anchors[2].Price:0.#####}  R:R {rr:0.##}";
        DrawSmallLabel(dc, label, new Point(entry.X + 4, entry.Y - 18), textBrush);
    }

    private void DrawRange(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Pen pen, Brush fill, Brush textBrush)
    {
        Rect rect = CreateNormalizedRect(p1, p2);
        dc.DrawRectangle(fill, pen, rect);
        double priceChange = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
        double percent = drawing.Anchors[0].Price == 0 ? 0 : priceChange / drawing.Anchors[0].Price * 100;
        int i1 = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int i2 = FindNearestDrawingCandleIndex(drawing.Anchors[1]);
        int bars = Math.Abs(i2 - i1);
        TimeSpan elapsed = TimeSpan.FromSeconds(Math.Abs(drawing.Anchors[1].StartUnix - drawing.Anchors[0].StartUnix));
        string text = drawing.ToolId switch
        {
            "date-range" => $"{bars} bars  {FormatDuration(elapsed)}",
            "price-range" => $"{priceChange:+0.########;-0.########;0}  {percent:+0.##;-0.##;0}%",
            _ => $"{priceChange:+0.########;-0.########;0}  {percent:+0.##;-0.##;0}%  {bars} bars  {FormatDuration(elapsed)}"
        };
        DrawSmallLabel(dc, text, new Point(rect.Left + 4, rect.Top + 4), textBrush);
    }

    private void DrawBarsPattern(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, bool ghost)
    {
        int start = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int end = FindNearestDrawingCandleIndex(drawing.Anchors[1]);
        if (start > end) (start, end) = (end, start);
        if (end - start < 1) return;
        double sourceBase = DrawingCandles[start].Close;
        double destinationPrice = drawing.Anchors[2].Price;
        int destinationIndex = FindNearestDrawingCandleIndex(drawing.Anchors[2]);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = start; i <= end; i++)
            {
                int offset = i - start;
                int targetIndex = Math.Clamp(destinationIndex + offset, 0, DrawingCandles.Count - 1);
                double price = destinationPrice + (DrawingCandles[i].Close - sourceBase);
                Point p = AnchorToPoint(CreateDrawingAnchorAtIndex(targetIndex, price), layout);
                if (i == start) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, false);
            }
        }
        Pen actual = ghost ? new Pen(CreateDrawingBrush(drawing.Style.LineColor, 0.45), pen.Thickness) { DashStyle = DashStyles.Dash } : pen;
        dc.DrawGeometry(null, actual, geometry);
    }

    private static void DrawSector(DrawingContext dc, Point[] points, Pen pen, Brush fill)
    {
        Point center = points[0];
        double radius = Distance(center, points[1]);
        double endAngle = Math.Atan2(points[2].Y - center.Y, points[2].X - center.X);
        double startAngle = Math.Atan2(points[1].Y - center.Y, points[1].X - center.X);
        Point end = new(center.X + Math.Cos(endAngle) * radius, center.Y + Math.Sin(endAngle) * radius);
        bool large = Math.Abs(endAngle - startAngle) > Math.PI;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(center, true, true);
            ctx.LineTo(points[1], true, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, large, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(fill, pen, geometry);
    }

    private void DrawVolumeProfile(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point[] points, Pen pen, Brush fill)
    {
        int start = FindNearestDrawingCandleIndex(drawing.Anchors[0]);
        int end = drawing.Anchors.Count >= 2 ? FindNearestDrawingCandleIndex(drawing.Anchors[1]) : layout.LastExclusive - 1;
        if (start > end) (start, end) = (end, start);
        start = Math.Clamp(start, 0, DrawingCandles.Count - 1); end = Math.Clamp(end, 0, DrawingCandles.Count - 1);
        int rows = (int)Math.Clamp(drawing.NumericOptions.TryGetValue("Rows", out double value) ? value : 24, 8, 100);
        double min = DrawingCandles.Skip(start).Take(end - start + 1).Min(c => c.Low);
        double max = DrawingCandles.Skip(start).Take(end - start + 1).Max(c => c.High);
        if (max <= min) return;
        double[] volumes = new double[rows];
        foreach (Candle candle in DrawingCandles.Skip(start).Take(end - start + 1))
        {
            double price = (candle.High + candle.Low + candle.Close) / 3.0;
            int row = Math.Clamp((int)((price - min) / (max - min) * rows), 0, rows - 1);
            volumes[row] += Math.Max(1, candle.TickVolume);
        }
        double maximum = Math.Max(1, volumes.Max());
        double left = points[0].X;
        double maxWidth = Math.Min(160, layout.Plot.Right - left);
        for (int i = 0; i < rows; i++)
        {
            double pLow = min + (max - min) * i / rows;
            double pHigh = min + (max - min) * (i + 1) / rows;
            double y1 = PriceToY(pHigh, layout); double y2 = PriceToY(pLow, layout);
            double width = maxWidth * volumes[i] / maximum;
            dc.DrawRectangle(fill, null, new Rect(left, Math.Min(y1, y2), width, Math.Max(1, Math.Abs(y2 - y1))));
        }
        dc.DrawLine(pen, new Point(left, PriceToY(min, layout)), new Point(left, PriceToY(max, layout)));
    }

    private void DrawLineStatistics(DrawingContext dc, ChartLayout layout, ChartDrawing drawing, Point p1, Point p2, Brush textBrush)
    {
        double priceDifference = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
        double percent = drawing.Anchors[0].Price == 0 ? 0 : priceDifference / drawing.Anchors[0].Price * 100;
        int bars = Math.Abs(FindNearestDrawingCandleIndex(drawing.Anchors[1]) - FindNearestDrawingCandleIndex(drawing.Anchors[0]));
        double angle = Math.Atan2(-(p2.Y - p1.Y), p2.X - p1.X) * 180 / Math.PI;
        string text = drawing.ToolId == "trend-angle"
            ? $"{angle:0.##}°"
            : $"{priceDifference:+0.########;-0.########;0}  {percent:+0.##;-0.##;0}%  {bars} bars";
        DrawSmallLabel(dc, text, Mid(p1, p2) + new Vector(4, -18), textBrush);
    }

    private void DrawPriceLabel(DrawingContext dc, ChartLayout layout, double price, Point point, Brush textBrush)
    {
        DrawSmallLabel(dc, price.ToString("0.########", CultureInfo.InvariantCulture), new Point(layout.Plot.Right + 4, point.Y - 8), textBrush);
    }

    private void DrawTextInsideShape(DrawingContext dc, ChartDrawing drawing, Rect rect, Brush textBrush)
    {
        if (string.IsNullOrWhiteSpace(drawing.Text)) return;
        FormattedText text = CreateDrawingText(drawing.Text, drawing.Style, textBrush, drawing.Style.FontSize);
        const double pad = 4;
        double x = drawing.Style.HorizontalTextAlignment.Trim().ToLowerInvariant() switch
        {
            "left" => rect.Left + pad,
            "right" => rect.Right - text.Width - pad,
            _ => rect.Left + (rect.Width - text.Width) / 2.0
        };
        double y = drawing.Style.VerticalTextAlignment.Trim().ToLowerInvariant() switch
        {
            "top" => rect.Top + pad,
            "bottom" => rect.Bottom - text.Height - pad,
            _ => rect.Top + (rect.Height - text.Height) / 2.0
        };
        dc.DrawText(text, new Point(Math.Clamp(x, rect.Left + pad, Math.Max(rect.Left + pad, rect.Right - text.Width - pad)),
            Math.Clamp(y, rect.Top + pad, Math.Max(rect.Top + pad, rect.Bottom - text.Height - pad))));
    }

    private FormattedText CreateDrawingText(string text, DrawingStyle style, Brush brush, double size)
    {
        Typeface typeface = new(new FontFamily(style.FontFamily), style.Italic ? FontStyles.Italic : FontStyles.Normal,
            style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            Math.Clamp(size, 8, 72), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static void DrawSmallLabel(DrawingContext dc, string text, Point point, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, brush, 1.0);
        dc.DrawText(formatted, point);
    }

    private void DrawMeasurementOverlay(DrawingContext dc, ChartLayout layout)
    {
        if (_measureStartAnchor is not DrawingAnchor start || _measureEndAnchor is not DrawingAnchor end)
            return;

        Point a = AnchorToPoint(start, layout);
        Point b = AnchorToPoint(end, layout);
        Rect range = CreateNormalizedRect(a, b);
        Point center = new(range.Left + range.Width / 2.0, range.Top + range.Height / 2.0);

        Brush measureBrush = CreateDrawingBrush(_measureLineColor, _measureOpacity);
        var linePen = new Pen(measureBrush, 1.35)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (linePen.CanFreeze) linePen.Freeze();

        // TradingView-style temporary Measure geometry: the selected range keeps
        // its translucent colour fill but deliberately has NO outline. The actual
        // distance geometry is the centred horizontal/vertical double-arrow cross.
        // The existing Measure colour + transparency control drives both elements.
        Brush measureFill = CreateDrawingBrush(_measureLineColor, _measureOpacity * 0.18);
        dc.DrawRectangle(measureFill, null, range);

        Point horizontalLeft = new(range.Left, center.Y);
        Point horizontalRight = new(range.Right, center.Y);
        Point verticalTop = new(center.X, range.Top);
        Point verticalBottom = new(center.X, range.Bottom);

        dc.DrawLine(linePen, horizontalLeft, horizontalRight);
        dc.DrawLine(linePen, verticalTop, verticalBottom);
        DrawMeasurementArrowHead(dc, measureBrush, horizontalLeft, new Vector(-1, 0));
        DrawMeasurementArrowHead(dc, measureBrush, horizontalRight, new Vector(1, 0));
        DrawMeasurementArrowHead(dc, measureBrush, verticalTop, new Vector(0, -1));
        DrawMeasurementArrowHead(dc, measureBrush, verticalBottom, new Vector(0, 1));

        int startIndex = FindNearestDrawingCandleIndex(start);
        int endIndex = FindNearestDrawingCandleIndex(end);
        int horizontalCount;
        string horizontalUnit;
        string duration;
        if (_rawTickDrawingSurface)
        {
            horizontalCount = startIndex >= 0 && endIndex >= 0
                ? Math.Abs(endIndex - startIndex)
                : 0;
            horizontalUnit = horizontalCount == 1 ? "tick" : "ticks";
            duration = FormatRawTickMeasurementDuration(
                Math.Abs(DrawingAnchorMilliseconds(end) - DrawingAnchorMilliseconds(start)));
        }
        else
        {
            horizontalCount = (int)Math.Min(
                int.MaxValue,
                Math.Round(Math.Abs(
                    DrawingTimestampToTimelineSlot(end.StartUnix) -
                    DrawingTimestampToTimelineSlot(start.StartUnix))));
            horizontalUnit = horizontalCount == 1 ? "bar" : "bars";
            duration = FormatMeasurementDuration(Math.Abs(end.StartUnix - start.StartUnix));
        }

        double difference = end.Price - start.Price;
        double percent = Math.Abs(start.Price) < 0.0000000001 ? 0 : difference / start.Price * 100.0;
        Candle? reference = DrawingCandles.Count > 0 ? DrawingCandles[Math.Clamp(endIndex, 0, DrawingCandles.Count - 1)] : null;
        int digits = reference?.Digits ?? 5;
        double pointSize = reference is not null && reference.Point > 0 ? reference.Point : Math.Pow(10, -digits);
        double pipSize = digits is 3 or 5 ? pointSize * 10.0 : pointSize;
        double points = pointSize > 0 ? difference / pointSize : 0;
        double pips = pipSize > 0 ? difference / pipSize : 0;
        string label = $"{difference:+0.########;-0.########;0}  •  {pips:+0.##;-0.##;0} pips  •  {points:+0.##;-0.##;0} points  •  {percent:+0.##;-0.##;0}%  •  {horizontalCount} {horizontalUnit}  •  {duration}";
        FormattedText text = CreateText(label, 11, measureBrush);
        double labelX = Math.Clamp(center.X - (text.Width + 20) / 2.0, layout.Plot.Left + 4,
            Math.Max(layout.Plot.Left + 4, layout.Plot.Right - text.Width - 24));
        double labelY = Math.Clamp(range.Top - text.Height - 14, layout.Plot.Top + 6,
            Math.Max(layout.Plot.Top + 6, layout.Plot.Bottom - text.Height - 14));
        Rect labelBox = new(labelX, labelY, text.Width + 20, text.Height + 12);
        byte backgroundAlpha = (byte)Math.Clamp((int)Math.Round(220 * _measureOpacity), 0, 220);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(backgroundAlpha, 10, 18, 32)),
            null, labelBox, 7, 7);
        dc.DrawText(text, new Point(labelBox.Left + 10, labelBox.Top + 6));
    }

    private static void DrawMeasurementArrowHead(DrawingContext dc, Brush brush, Point tip, Vector direction)
    {
        if (direction.LengthSquared < 0.000001)
            return;

        direction.Normalize();
        Vector normal = new(-direction.Y, direction.X);
        const double arrowLength = 6.5;
        const double halfWidth = 3.2;
        Point baseCenter = tip - direction * arrowLength;
        Point first = baseCenter + normal * halfWidth;
        Point second = baseCenter - normal * halfWidth;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(first, isStroked: true, isSmoothJoin: true);
            context.LineTo(second, isStroked: true, isSmoothJoin: true);
        }
        if (geometry.CanFreeze) geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static string FormatMeasurementDuration(long totalSeconds)
    {
        TimeSpan value = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (value.TotalDays >= 1)
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        return $"{value.Seconds}s";
    }

    private static Point[] GetRectangleEightHandles(Rect rect) =>
        new[]
        {
            rect.TopLeft,
            rect.TopRight,
            rect.BottomRight,
            rect.BottomLeft,
            new Point(rect.Left + rect.Width / 2.0, rect.Top),
            new Point(rect.Right, rect.Top + rect.Height / 2.0),
            new Point(rect.Left + rect.Width / 2.0, rect.Bottom),
            new Point(rect.Left, rect.Top + rect.Height / 2.0)
        };

    private static bool TryGetRotatedRectangleGeometry(
        Point[] points,
        out Point[] corners,
        out Point[] handles,
        out Vector axisX,
        out Vector axisY,
        out double width,
        out double height)
    {
        corners = Array.Empty<Point>();
        handles = Array.Empty<Point>();
        axisX = default;
        axisY = default;
        width = 0;
        height = 0;
        if (points.Length < 3)
            return false;

        axisX = points[1] - points[0];
        width = axisX.Length;
        if (!double.IsFinite(width) || width < 0.001)
            return false;
        axisX.Normalize();
        axisY = new Vector(-axisX.Y, axisX.X);
        height = Vector.Multiply(points[2] - points[0], axisY);
        if (!double.IsFinite(height) || Math.Abs(height) < 0.001)
            return false;

        Vector offset = axisY * height;
        Point c0 = points[0];
        Point c1 = points[0] + axisX * width;
        Point c2 = c1 + offset;
        Point c3 = c0 + offset;
        corners = new[] { c0, c1, c2, c3 };
        handles = new[]
        {
            c0, c1, c2, c3,
            Midpoint(c0, c1),
            Midpoint(c1, c2),
            Midpoint(c2, c3),
            Midpoint(c3, c0)
        };
        return true;
    }

    private static Point Midpoint(Point a, Point b) =>
        new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static double PreservePositiveSize(double value, double minimum = 8.0) =>
        Math.Max(minimum, value);

    private static double PreserveSignedSize(double value, double reference, double minimum = 8.0)
    {
        double sign = reference < 0 ? -1.0 : 1.0;
        return sign * Math.Max(minimum, value * sign);
    }

    private static bool CornersStayInsidePlot(Point[] corners, Rect plot, double inset = 0.75)
    {
        double left = plot.Left + inset;
        double right = plot.Right - inset;
        double top = plot.Top + inset;
        double bottom = plot.Bottom - inset;
        return corners.All(point =>
            point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom);
    }

    private void DrawDrawingSelection(DrawingContext dc, ChartLayout layout)
    {
        foreach (string id in _selectedDrawingIds)
        {
            ChartDrawing? drawing = _drawings.FirstOrDefault(item => item.Id == id &&
                IsDrawingVisible(item));
            if (drawing is null) continue;
            Point[] points = drawing.Anchors
                .Select(anchor => IsFolder6AnnotationTool(drawing.ToolId)
                    ? Folder6AnnotationSafePoint(anchor, layout)
                    : AnchorToPoint(anchor, layout))
                .ToArray();
            Rect bounds = GetDrawingBounds(drawing, layout);
            bool locked = drawing.IsLocked || _lockAllDrawings;
            Point[] handles = points;
            DrawingGeometryKind geometry = DrawingToolCatalog.Find(drawing.ToolId)?.Geometry ?? DrawingGeometryKind.Line;
            if (drawing.ToolId == "parallel-channel" && points.Length >= 3)
            {
                handles = new[] { points[0], points[1], new Point(points[1].X, points[2].Y) };
            }
            if (drawing.ToolId == "disjoint-channel" && points.Length >= 3)
            {
                handles = new[] { points[0], points[1], new Point(points[1].X, points[2].Y) };
            }
            if (geometry == DrawingGeometryKind.Rectangle && points.Length >= 2)
            {
                handles = GetRectangleEightHandles(CreateNormalizedRect(points[0], points[1]));
            }
            else if (geometry == DrawingGeometryKind.RotatedRectangle &&
                     TryGetRotatedRectangleGeometry(points, out _, out Point[] rotatedHandles, out _, out _, out _, out _))
            {
                handles = rotatedHandles;
            }
            if (drawing.ToolId == "gann-square-fixed" && points.Length >= 2)
            {
                Point displaySecond = GetGannDisplaySecondPoint(drawing, points[0], points[1]);
                Rect fixedRect = Bounds(new[] { points[0], displaySecond });
                handles = new[] { fixedRect.TopLeft, fixedRect.TopRight, fixedRect.BottomRight, fixedRect.BottomLeft };
            }
            else if (drawing.ToolId == "gann-square" && points.Length >= 2)
            {
                handles = new[] { points[0], points[1] };
            }
            if (geometry == DrawingGeometryKind.Regression &&
                TryGetParityRegressionGeometry(drawing, layout, out Point regressionStart, out Point regressionEnd,
                    out _, out _, out _, out _, out _))
            {
                // Regression placement is time-based; show its editable endpoints on
                // the rendered base line rather than at arbitrary click-price Y values.
                handles = new[] { regressionStart, regressionEnd };
            }
            if (drawing.ToolId == "table" && points.Length >= 2)
            {
                Rect tableBounds = CreateNormalizedRect(points[0], points[1]);
                handles = new[] { tableBounds.TopLeft, tableBounds.TopRight, tableBounds.BottomRight, tableBounds.BottomLeft };
            }
            else if (drawing.ToolId == "text")
            {
                // TradingView selects plain Text with a lightweight text box rather
                // than a detached circular anchor handle.
                handles = Array.Empty<Point>();
                if (!bounds.IsEmpty)
                {
                    var textSelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(41, 98, 255)), 1.15);
                    dc.DrawRectangle(null, textSelectionPen, Inflate(bounds, 2));
                }
            }
            else if (drawing.ToolId == "signpost" && points.Length >= 1)
            {
                DrawingStyle signStyle = drawing.Style;
                string signValue = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
                FormattedText signText = CreateDrawingText(signValue, signStyle, Brushes.White, signStyle.FontSize);
                double signWidth = Math.Max(92, signText.Width + 20);
                double signHeight = signText.Height + 14;
                handles = new[] { new Point(points[0].X, points[0].Y - signHeight - 18) };
            }
            else if (geometry == DrawingGeometryKind.Icon && points.Length >= 1 && !bounds.IsEmpty)
            {
                // Emoji / Icon / Sticker objects move by dragging the body. The four
                // corner handles are resize-only and always preserve the visual's
                // original aspect ratio. No side/stretch handles are exposed.
                Rect mediaBounds = DrawingMediaCatalog.TryDecode(drawing.Text, out DrawingMediaDefinition? selectedMedia) && selectedMedia is not null
                    ? GetTickLabMediaBounds(points[0], selectedMedia, drawing)
                    : bounds;
                handles = new[]
                {
                    mediaBounds.TopLeft, mediaBounds.TopRight,
                    mediaBounds.BottomRight, mediaBounds.BottomLeft
                };
                dc.DrawRectangle(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(180, 41, 98, 255)), 1.0),
                    mediaBounds);
            }
            else if (geometry == DrawingGeometryKind.Image && points.Length >= 2 && !bounds.IsEmpty)
            {
                Rect imageBounds = CreateNormalizedRect(points[0], points[1]);
                handles = new[] { imageBounds.TopLeft, imageBounds.TopRight, imageBounds.BottomRight, imageBounds.BottomLeft };
            }
            for (int i = 0; i < handles.Length; i++)
            {
                Brush fill = locked
                    ? new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    : Brushes.White;
                Brush outline = locked
                    ? new SolidColorBrush(Color.FromRgb(71, 85, 105))
                    : new SolidColorBrush(Color.FromRgb(41, 98, 255));
                dc.DrawEllipse(fill, new Pen(outline, 1.25), handles[i], 3.5, 3.5);
            }
            if (locked && !bounds.IsEmpty)
                DrawSmallLabel(dc, "LOCK", bounds.TopRight + new Vector(-29, 3), new SolidColorBrush(Color.FromRgb(148, 163, 184)));
        }
    }

    private DrawingHitInfo? HitTestDrawing(ChartLayout layout, Point point)
    {
        foreach (ChartDrawing drawing in VisibleDrawings().Reverse())
        {
            Point[] anchors = drawing.Anchors
                .Select(anchor => IsFolder6AnnotationTool(drawing.ToolId)
                    ? Folder6AnnotationSafePoint(anchor, layout)
                    : AnchorToPoint(anchor, layout))
                .ToArray();
            DrawingGeometryKind geometry = DrawingToolCatalog.Find(drawing.ToolId)?.Geometry ?? DrawingGeometryKind.Line;
            Point[] hitHandles = anchors;
            if (drawing.ToolId == "parallel-channel" && anchors.Length >= 3)
                hitHandles = new[] { anchors[0], anchors[1], new Point(anchors[1].X, anchors[2].Y) };
            if (drawing.ToolId == "disjoint-channel" && anchors.Length >= 3)
                hitHandles = new[] { anchors[0], anchors[1], new Point(anchors[1].X, anchors[2].Y) };
            if (drawing.ToolId == "gann-square" && anchors.Length >= 2)
                hitHandles = new[] { anchors[0], anchors[1] };
            if (drawing.ToolId == "gann-square-fixed" && anchors.Length >= 2)
            {
                Point displaySecond = GetGannDisplaySecondPoint(drawing, anchors[0], anchors[1]);
                Rect fixedRect = Bounds(new[] { anchors[0], displaySecond });
                Point[] fixedHandles = { fixedRect.TopLeft, fixedRect.TopRight, fixedRect.BottomRight, fixedRect.BottomLeft };
                for (int i = 0; i < fixedHandles.Length; i++)
                {
                    if (Distance(point, fixedHandles[i]) <= 8)
                        return new DrawingHitInfo(drawing, 100 + i, 0);
                }
                hitHandles = Array.Empty<Point>();
            }
            if (geometry == DrawingGeometryKind.Rectangle && anchors.Length >= 2)
            {
                Point[] rectangleHandles = GetRectangleEightHandles(CreateNormalizedRect(anchors[0], anchors[1]));
                for (int i = 0; i < rectangleHandles.Length; i++)
                {
                    if (Distance(point, rectangleHandles[i]) <= 9)
                        return new DrawingHitInfo(drawing, 400 + i, 0);
                }
                hitHandles = Array.Empty<Point>();
            }
            else if (geometry == DrawingGeometryKind.RotatedRectangle &&
                     TryGetRotatedRectangleGeometry(anchors, out _, out Point[] rotatedHandles, out _, out _, out _, out _))
            {
                for (int i = 0; i < rotatedHandles.Length; i++)
                {
                    if (Distance(point, rotatedHandles[i]) <= 9)
                        return new DrawingHitInfo(drawing, 500 + i, 0);
                }
                hitHandles = Array.Empty<Point>();
            }
            if (geometry == DrawingGeometryKind.Regression &&
                TryGetParityRegressionGeometry(drawing, layout, out Point regressionStart, out Point regressionEnd,
                    out _, out _, out _, out _, out _))
            {
                hitHandles = new[] { regressionStart, regressionEnd };
            }
            if (drawing.ToolId == "table" && anchors.Length >= 2)
            {
                Rect tableBounds = CreateNormalizedRect(anchors[0], anchors[1]);
                Point[] tableHandles = { tableBounds.TopLeft, tableBounds.TopRight, tableBounds.BottomRight, tableBounds.BottomLeft };
                for (int i = 0; i < tableHandles.Length; i++)
                {
                    if (Distance(point, tableHandles[i]) <= 8)
                        return new DrawingHitInfo(drawing, 200 + i, 0);
                }
                hitHandles = Array.Empty<Point>();
            }
            else if (geometry == DrawingGeometryKind.Icon && anchors.Length >= 1)
            {
                Rect mediaBounds = DrawingMediaCatalog.TryDecode(drawing.Text, out DrawingMediaDefinition? hitMedia) && hitMedia is not null
                    ? GetTickLabMediaBounds(anchors[0], hitMedia, drawing)
                    : GetDrawingBounds(drawing, layout);
                Point[] mediaHandles =
                {
                    mediaBounds.TopLeft, mediaBounds.TopRight,
                    mediaBounds.BottomRight, mediaBounds.BottomLeft
                };
                for (int i = 0; i < mediaHandles.Length; i++)
                {
                    if (Distance(point, mediaHandles[i]) <= 9)
                        return new DrawingHitInfo(drawing, 300 + i, 0);
                }
                // The stored center anchor is not a visible resize handle for media.
                // Clicking the visible item body below remains a normal whole-object drag.
                hitHandles = Array.Empty<Point>();
            }
            else if (geometry == DrawingGeometryKind.Image && anchors.Length >= 2)
            {
                Rect imageBounds = CreateNormalizedRect(anchors[0], anchors[1]);
                Point[] imageHandles = { imageBounds.TopLeft, imageBounds.TopRight, imageBounds.BottomRight, imageBounds.BottomLeft };
                for (int i = 0; i < imageHandles.Length; i++)
                {
                    if (Distance(point, imageHandles[i]) <= 8)
                        return new DrawingHitInfo(drawing, 100 + i, 0);
                }
            }
            else
            {
                for (int i = 0; i < hitHandles.Length; i++)
                {
                    if (Distance(point, hitHandles[i]) <= 8)
                        return new DrawingHitInfo(drawing, i, 0);
                }
            }
            double distance = DistanceToDrawing(drawing, anchors, point, layout);
            double hitTolerance = Math.Max(9.0, drawing.Style.LineWidth + 6.0);
            if (distance <= hitTolerance)
                return new DrawingHitInfo(drawing, -1, distance);
        }
        return null;
    }

    private double DistanceToDrawing(ChartDrawing drawing, Point[] points, Point point, ChartLayout layout)
    {
        DrawingGeometryKind geometry = DrawingToolCatalog.Find(drawing.ToolId)?.Geometry ?? DrawingGeometryKind.Line;
        if (points.Length == 0) return double.MaxValue;
        if (geometry is DrawingGeometryKind.Rectangle or DrawingGeometryKind.Ellipse or DrawingGeometryKind.RotatedRectangle or
            DrawingGeometryKind.Triangle or DrawingGeometryKind.Range or DrawingGeometryKind.Position or DrawingGeometryKind.GannBox or DrawingGeometryKind.Sector or DrawingGeometryKind.Image)
        {
            Rect bounds = GetDrawingBounds(drawing, layout);
            // Step-1 Prediction/Measurement parity: labels, arrows and filled bodies
            // belong to the parent object.  Give the visible label/arrow margin the
            // same selection target so users never have to hunt for an anchor point.
            if (drawing.ToolId is "long-position" or "short-position" or "position-forecast")
                bounds = Inflate(bounds, 58);
            else if (drawing.ToolId is "price-range" or "date-range" or "date-price-range")
                bounds = Inflate(bounds, 48);
            if (bounds.Contains(point)) return 0;
            return DistanceToRect(point, bounds);
        }
        if (geometry == DrawingGeometryKind.HorizontalLine)
            return Math.Abs(point.Y - points[0].Y);
        if (geometry == DrawingGeometryKind.HorizontalRay)
            return point.X + 8 >= points[0].X ? Math.Abs(point.Y - points[0].Y) : Distance(point, points[0]);
        if (geometry == DrawingGeometryKind.VerticalLine)
            return Math.Abs(point.X - points[0].X);
        if (geometry == DrawingGeometryKind.CrossLine)
            return Math.Min(Math.Abs(point.X - points[0].X), Math.Abs(point.Y - points[0].Y));
        if (geometry is DrawingGeometryKind.Text or DrawingGeometryKind.Note or DrawingGeometryKind.Callout or DrawingGeometryKind.PriceLabel or DrawingGeometryKind.Flag or DrawingGeometryKind.Icon)
            return GetDrawingBounds(drawing, layout).Contains(point) ? 0 : double.MaxValue;

        if (points.Length >= 2)
        {
            if (geometry == DrawingGeometryKind.Ray)
                return DistanceToRay(point, points[0], points[1]);
            if (geometry == DrawingGeometryKind.ExtendedLine)
                return DistanceToInfiniteLine(point, points[0], points[1]);
            if (geometry == DrawingGeometryKind.Line && (drawing.Style.ExtendLeft || drawing.Style.ExtendRight))
            {
                if (drawing.Style.ExtendLeft && drawing.Style.ExtendRight)
                    return DistanceToInfiniteLine(point, points[0], points[1]);
                if (drawing.Style.ExtendRight)
                    return DistanceToRay(point, points[0], points[1]);
                return DistanceToRay(point, points[1], points[0]);
            }
        }

        if (drawing.ToolId == "parallel-channel" && points.Length >= 3)
        {
            Vector offset = new(0, points[2].Y - points[1].Y);
            Point c = points[0] + offset;
            Point d = points[1] + offset;
            double railDistance = Math.Min(
                DistanceToSegment(point, points[0], points[1]),
                DistanceToSegment(point, c, d));
            if (railDistance <= 14) return railDistance;
            if (drawing.Style.FillOpacity > 0.001 && Inflate(Bounds(new[] { points[0], points[1], d, c }), 5).Contains(point))
                return 4.0;
            return railDistance;
        }

        if (drawing.ToolId == "disjoint-channel" && points.Length >= 3)
        {
            Point a = points[0];
            Point b = points[1];
            Point c = new(b.X, points[2].Y);
            Vector first = b - a;
            Point d = c + new Vector(first.X, -first.Y);
            double railDistance = Math.Min(DistanceToSegment(point, a, b), DistanceToSegment(point, c, d));
            if (railDistance <= 14) return railDistance;
            Rect body = Inflate(Bounds(new[] { a, b, c, d }), 6);
            if (drawing.Style.FillOpacity > 0.001 && body.Contains(point)) return 4.0;
            return railDistance;
        }

        if (geometry == DrawingGeometryKind.Regression &&
            TryGetParityRegressionGeometry(drawing, layout, out Point r1, out Point r2,
                out Point ru1, out Point ru2, out Point rl1, out Point rl2, out _))
        {
            double bestRegression = Math.Min(
                DistanceToSegment(point, r1, r2),
                Math.Min(DistanceToSegment(point, ru1, ru2), DistanceToSegment(point, rl1, rl2)));
            Rect regressionBody = Inflate(Bounds(new[] { ru1, ru2, rl1, rl2 }), 6);
            if (bestRegression <= 14)
                return bestRegression;
            if (drawing.Style.FillOpacity > 0.001 && regressionBody.Contains(point))
                return 4.0;
            return bestRegression;
        }

        if (geometry == DrawingGeometryKind.Pattern && (drawing.ToolId is
            "xabcd-pattern" or "cypher-pattern" or "abcd-pattern" or "triangle-pattern" or "three-drives-pattern" or "head-shoulders"))
        {
            double patternBest = double.MaxValue;
            for (int i = 1; i < points.Length; i++)
                patternBest = Math.Min(patternBest, DistanceToSegment(point, points[i - 1], points[i]));

            if (drawing.ToolId is "xabcd-pattern" or "cypher-pattern")
            {
                if (points.Length >= 3) patternBest = Math.Min(patternBest, DistanceToSegment(point, points[0], points[2]));
                if (points.Length >= 4) patternBest = Math.Min(patternBest, DistanceToSegment(point, points[1], points[3]));
                if (points.Length >= 5)
                {
                    patternBest = Math.Min(patternBest, DistanceToSegment(point, points[2], points[4]));
                    patternBest = Math.Min(patternBest, DistanceToSegment(point, points[0], points[4]));
                }
                if (drawing.Style.FillOpacity > 0.001 && Inflate(Bounds(points), 4).Contains(point))
                    return Math.Min(patternBest, 4.0);
            }
            else if (drawing.ToolId == "abcd-pattern")
            {
                if (points.Length >= 3) patternBest = Math.Min(patternBest, DistanceToSegment(point, points[0], points[2]));
                if (points.Length >= 4) patternBest = Math.Min(patternBest, DistanceToSegment(point, points[1], points[3]));
            }
            else if (drawing.ToolId == "triangle-pattern" && points.Length >= 4 &&
                PatternTryLineIntersection(points[0], points[2], points[1], points[3], out Point patternApex))
            {
                patternBest = Math.Min(patternBest, DistanceToSegment(point, points[0], patternApex));
                patternBest = Math.Min(patternBest, DistanceToSegment(point, points[1], patternApex));
                if (drawing.Style.FillOpacity > 0.001 && Inflate(Bounds(new[] { points[0], points[1], patternApex }), 4).Contains(point))
                    return Math.Min(patternBest, 4.0);
            }
            else if (drawing.ToolId == "head-shoulders" && points.Length >= 5)
            {
                Point neckA = points[2];
                Point neckB = points[4];
                double neckDx = neckB.X - neckA.X;
                double neckSlope = Math.Abs(neckDx) < 0.5 ? 0 : (neckB.Y - neckA.Y) / neckDx;
                double nx1 = points[0].X;
                double nx2 = points[Math.Min(points.Length - 1, 6)].X;
                Point n1 = new(nx1, neckA.Y + (nx1 - neckA.X) * neckSlope);
                Point n2 = new(nx2, neckA.Y + (nx2 - neckA.X) * neckSlope);
                patternBest = Math.Min(patternBest, DistanceToSegment(point, n1, n2));
            }

            return patternBest;
        }

        if (drawing.ToolId == "bars-pattern")
        {
            IReadOnlyList<Point> projected = GetBarsPatternProjectedPoints(drawing, layout);
            double patternDistance = double.MaxValue;
            for (int i = 1; i < projected.Count; i++)
                patternDistance = Math.Min(patternDistance, DistanceToSegment(point, projected[i - 1], projected[i]));
            return patternDistance;
        }

        if (drawing.ToolId == "ghost-feed")
        {
            IReadOnlyList<GhostFeedVisualBar> bars = BuildGhostFeedVisualBars(drawing, layout);
            double ghostDistance = double.MaxValue;
            foreach (GhostFeedVisualBar bar in bars)
            {
                // The complete visible body is a selection target, not just the
                // original construction anchors. Wick clicks resolve to the same
                // parent Ghost Feed object as body clicks.
                Rect hitBody = Inflate(bar.Body, 3.5);
                if (hitBody.Contains(point))
                    return 0;
                ghostDistance = Math.Min(ghostDistance, DistanceToSegment(point, bar.WickTop, bar.WickBottom));
                ghostDistance = Math.Min(ghostDistance, DistanceToRect(point, hitBody));
            }
            return ghostDistance;
        }

        if (geometry == DrawingGeometryKind.AnchoredVwap)
        {
            AnchoredVwapVisual? visual = BuildParityAnchoredVwapVisual(layout, drawing);
            if (visual is null)
                return double.PositiveInfinity;
            double bestVwap = double.PositiveInfinity;
            for (int i = 1; i < visual.Main.Count; i++)
                bestVwap = Math.Min(bestVwap, DistanceToSegment(point, visual.Main[i - 1], visual.Main[i]));
            foreach (var band in visual.Bands)
            {
                for (int i = 1; i < band.Up.Count; i++)
                    bestVwap = Math.Min(bestVwap, DistanceToSegment(point, band.Up[i - 1], band.Up[i]));
                for (int i = 1; i < band.Down.Count; i++)
                    bestVwap = Math.Min(bestVwap, DistanceToSegment(point, band.Down[i - 1], band.Down[i]));
            }
            return bestVwap;
        }

        if (geometry == DrawingGeometryKind.VolumeProfile)
        {
            ParityVolumeProfileVisual? visual = BuildParityVolumeProfileVisual(layout, drawing);
            if (visual is null)
                return double.PositiveInfinity;
            double bestProfile = double.PositiveInfinity;
            DrawingLevel hitUp = ResolveVolumeProfileLevel(drawing, "Up Volume", 0);
            DrawingLevel hitDown = ResolveVolumeProfileLevel(drawing, "Down Volume", 1);
            DrawingLevel hitValueUp = ResolveVolumeProfileLevel(drawing, "Value Area Up", 2);
            DrawingLevel hitValueDown = ResolveVolumeProfileLevel(drawing, "Value Area Down", 3);
            DrawingLevel hitVah = ResolveVolumeProfileLevel(drawing, "VAH", 4);
            DrawingLevel hitVal = ResolveVolumeProfileLevel(drawing, "VAL", 5);
            DrawingLevel hitPoc = ResolveVolumeProfileLevel(drawing, "POC", 6);
            DrawingLevel hitDevelopingPoc = ResolveVolumeProfileLevel(drawing, "Developing POC", 7);
            DrawingLevel hitDevelopingVa = ResolveVolumeProfileLevel(drawing, "Developing VA", 8);
            DrawingLevel hitHistogram = ResolveVolumeProfileLevel(drawing, "Histogram Box", 9);
            bool anyProfileFillVisible = hitUp.Enabled || hitDown.Enabled || hitValueUp.Enabled || hitValueDown.Enabled;
            if (ParityFlag(drawing, "ShowProfile", true) && anyProfileFillVisible)
            {
                foreach (ParityVolumeProfileRowVisual row in visual.Rows)
                {
                    Rect hitRect = Inflate(row.TotalRect, 3.0);
                    if (hitRect.Contains(point))
                        return 0;
                    bestProfile = Math.Min(bestProfile, DistanceToRect(point, hitRect));
                }
            }
            if (ParityFlag(drawing, "ShowPOC", true) && hitPoc.Enabled)
                bestProfile = Math.Min(bestProfile, DistanceToSegment(point, new Point(visual.GuideLeft, visual.PocY), new Point(visual.GuideRight, visual.PocY)));
            if (ParityFlag(drawing, "ShowVAH", ParityFlag(drawing, "ShowValueArea", true)) && hitVah.Enabled)
                bestProfile = Math.Min(bestProfile, DistanceToSegment(point, new Point(visual.GuideLeft, visual.VahY), new Point(visual.GuideRight, visual.VahY)));
            if (ParityFlag(drawing, "ShowVAL", ParityFlag(drawing, "ShowValueArea", true)) && hitVal.Enabled)
                bestProfile = Math.Min(bestProfile, DistanceToSegment(point, new Point(visual.GuideLeft, visual.ValY), new Point(visual.GuideRight, visual.ValY)));
            if (ParityFlag(drawing, "ShowDevelopingPOC", false) && hitDevelopingPoc.Enabled)
                for (int i = 1; i < visual.DevelopingPoc.Count; i++)
                    bestProfile = Math.Min(bestProfile, DistanceToSegment(point, visual.DevelopingPoc[i - 1], visual.DevelopingPoc[i]));
            if (ParityFlag(drawing, "ShowDevelopingVA", false) && hitDevelopingVa.Enabled)
            {
                for (int i = 1; i < visual.DevelopingVah.Count; i++)
                    bestProfile = Math.Min(bestProfile, DistanceToSegment(point, visual.DevelopingVah[i - 1], visual.DevelopingVah[i]));
                for (int i = 1; i < visual.DevelopingVal.Count; i++)
                    bestProfile = Math.Min(bestProfile, DistanceToSegment(point, visual.DevelopingVal[i - 1], visual.DevelopingVal[i]));
            }
            if (ParityFlag(drawing, "ShowHistogramBox", false) && hitHistogram.Enabled)
                bestProfile = Math.Min(bestProfile, DistanceToRect(point, Inflate(visual.HistogramBounds, 2.0)));
            return bestProfile;
        }

        if (geometry == DrawingGeometryKind.Cycles && points.Length >= 2)
        {
            double spacing = Math.Abs(points[1].X - points[0].X);
            if (spacing < 0.75)
                return double.MaxValue;

            double nearestIndex = Math.Round((point.X - points[0].X) / spacing);
            if (drawing.ToolId == "time-cycles")
            {
                double radiusX = spacing / 2.0;
                double radiusY = Math.Max(4, Math.Min(radiusX, layout.Plot.Height / 2.0));
                double cycleBest = double.MaxValue;
                // Adjacent ellipses can overlap the pointer horizontally, so test the
                // nearest center and its neighbours rather than requiring anchor hits.
                for (int offset = -2; offset <= 2; offset++)
                {
                    double centerX = points[0].X + (nearestIndex + offset) * spacing;
                    cycleBest = Math.Min(cycleBest, DistanceToEllipseStroke(
                        point, new Point(centerX, points[0].Y), radiusX, radiusY));
                }
                return cycleBest;
            }

            double nearestX = points[0].X + nearestIndex * spacing;
            return Math.Abs(point.X - nearestX);
        }

        if (geometry == DrawingGeometryKind.Sine && points.Length >= 2)
        {
            double anchorSpan = points[1].X - points[0].X;
            if (Math.Abs(anchorSpan) < 0.75)
                return double.MaxValue;
            int cycles = (int)Math.Clamp(ParityOption(drawing, "Cycles", 2), 1, 64);
            double period = Math.Abs(anchorSpan) / cycles;
            if (period < 0.75)
                return double.MaxValue;

            double amplitude = Math.Max(1, Math.Abs(points[1].Y - points[0].Y));
            double direction = Math.Sign(anchorSpan);
            if (direction == 0) direction = 1;

            // Test a small horizontal neighbourhood so steep portions of the sine are
            // just as easy to select as crests/troughs. Any visible curve segment now
            // selects the one parent Sine Line object and restores quick edit controls.
            double sineBest = double.MaxValue;
            for (int dx = -8; dx <= 8; dx += 2)
            {
                double sampleX = point.X + dx;
                double phase = ((sampleX - points[0].X) / period) * Math.PI * 2.0 * direction;
                double sampleY = points[0].Y + Math.Sin(phase) * amplitude;
                sineBest = Math.Min(sineBest, Math.Sqrt(dx * dx + Math.Pow(point.Y - sampleY, 2)));
            }
            return sineBest;
        }

        if (geometry == DrawingGeometryKind.Curve && points.Length >= 3)
        {
            Point[] samples = points.Length >= 4
                ? SampleCubicBezier(points[0], points[1], points[2], points[3], 64)
                : SampleQuadraticBezier(points[0], points[1], points[2], 56);
            return DistanceToPolylineSamples(point, samples);
        }

        if (geometry == DrawingGeometryKind.DoubleCurve && points.Length >= 4)
        {
            Point[] samples = SampleCubicBezier(points[0], points[1], points[2], points[3], 72);
            return DistanceToPolylineSamples(point, samples);
        }

        if (geometry == DrawingGeometryKind.Arc && points.Length >= 3)
        {
            StreamGeometry arcGeometry = CreateArcGeometry(points);
            if (drawing.Style.FillOpacity > 0.001 && arcGeometry.FillContains(point))
                return 0;
            Point[] samples = SampleQuadraticBezier(points[0], points[1], points[2], 64);
            double curveDistance = DistanceToPolylineSamples(point, samples);
            double chordDistance = DistanceToSegment(point, points[0], points[2]);
            return Math.Min(curveDistance, chordDistance);
        }

        if (geometry == DrawingGeometryKind.ArrowMarker)
        {
            if (drawing.ToolId == "arrow-marker" && points.Length >= 2)
            {
                Point[] polygon = BuildArrowMarkerPolygon(points[0], points[1]);
                if (polygon.Length >= 3)
                {
                    if (PointInPolygon(point, polygon)) return 0;
                    double arrowBest = double.MaxValue;
                    for (int i = 0; i < polygon.Length; i++)
                        arrowBest = Math.Min(arrowBest, DistanceToSegment(point, polygon[i], polygon[(i + 1) % polygon.Length]));
                    return arrowBest;
                }
            }

            // One-click directional markers also select from any visible shaft or
            // arrowhead segment, not only from their hidden anchor point.
            Point tip = points[0];
            Vector markerDirection = drawing.ToolId.Contains("left") ? new Vector(-1, 0) :
                drawing.ToolId.Contains("right") ? new Vector(1, 0) :
                drawing.ToolId.Contains("down") ? new Vector(0, 1) : new Vector(0, -1);
            Point tail = tip - markerDirection * 18.0;
            GetArrowHeadPoints(tail, tip, out Point markerWingA, out Point markerWingB);
            return Math.Min(
                DistanceToSegment(point, tail, tip),
                Math.Min(DistanceToSegment(point, tip, markerWingA), DistanceToSegment(point, tip, markerWingB)));
        }

        if (drawing.ToolId == "path" && points.Length >= 2)
        {
            double pathBest = double.MaxValue;
            for (int i = 1; i < points.Length; i++)
                pathBest = Math.Min(pathBest, DistanceToSegment(point, points[i - 1], points[i]));
            GetArrowHeadPoints(points[^2], points[^1], out Point arrowA, out Point arrowB);
            pathBest = Math.Min(pathBest, DistanceToSegment(point, points[^1], arrowA));
            pathBest = Math.Min(pathBest, DistanceToSegment(point, points[^1], arrowB));
            return pathBest;
        }

        if (DrawingToolCatalog.Find(drawing.ToolId)?.Category == DrawingToolCategory.FibonacciGann)
        {
            double folder2Distance = DistanceToFibonacciGannDrawing(drawing, points, point, layout);
            if (!double.IsPositiveInfinity(folder2Distance))
                return folder2Distance;
        }

        if (geometry is DrawingGeometryKind.Channel or DrawingGeometryKind.Pitchfork)
        {
            Rect visibleBounds = Inflate(GetDrawingBounds(drawing, layout), 7);
            if (visibleBounds.Contains(point))
            {
                double segmentBest = double.MaxValue;
                for (int i = 1; i < points.Length; i++)
                    segmentBest = Math.Min(segmentBest, DistanceToSegment(point, points[i - 1], points[i]));
                if (segmentBest <= 14 || drawing.Style.FillOpacity > 0.001)
                    return Math.Min(segmentBest, 4.0);
            }
        }

        double best = double.MaxValue;
        for (int i = 1; i < points.Length; i++)
            best = Math.Min(best, DistanceToSegment(point, points[i - 1], points[i]));
        return best;
    }

    private static Point[] SampleQuadraticBezier(Point p0, Point p1, Point p2, int segments)
    {
        segments = Math.Max(4, segments);
        var samples = new Point[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1.0 - t;
            samples[i] = new Point(
                u * u * p0.X + 2.0 * u * t * p1.X + t * t * p2.X,
                u * u * p0.Y + 2.0 * u * t * p1.Y + t * t * p2.Y);
        }
        return samples;
    }

    private static Point[] SampleCubicBezier(Point p0, Point p1, Point p2, Point p3, int segments)
    {
        segments = Math.Max(4, segments);
        var samples = new Point[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1.0 - t;
            double uu = u * u;
            double tt = t * t;
            samples[i] = new Point(
                uu * u * p0.X + 3.0 * uu * t * p1.X + 3.0 * u * tt * p2.X + tt * t * p3.X,
                uu * u * p0.Y + 3.0 * uu * t * p1.Y + 3.0 * u * tt * p2.Y + tt * t * p3.Y);
        }
        return samples;
    }

    private static double DistanceToPolylineSamples(Point point, Point[] samples)
    {
        double best = double.MaxValue;
        for (int i = 1; i < samples.Length; i++)
            best = Math.Min(best, DistanceToSegment(point, samples[i - 1], samples[i]));
        return best;
    }

    private static bool PointInPolygon(Point point, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Point pi = polygon[i];
            Point pj = polygon[j];
            bool crosses = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static void GetArrowHeadPoints(Point from, Point to, out Point wingA, out Point wingB)
    {
        Vector direction = to - from;
        if (direction.LengthSquared < 0.001)
        {
            wingA = to;
            wingB = to;
            return;
        }
        direction.Normalize();
        Vector normal = new(-direction.Y, direction.X);
        double size = 9.0;
        wingA = to - direction * size + normal * (size * 0.52);
        wingB = to - direction * size - normal * (size * 0.52);
    }

    private double DistanceToFibonacciGannDrawing(ChartDrawing drawing, Point[] points, Point point, ChartLayout layout)
    {
        if (points.Length < 2) return double.PositiveInfinity;
        IReadOnlyList<DrawingLevel> rawLevels = drawing.Levels.Count > 0
            ? drawing.Levels
            : DrawingParityDefaults.LevelsForTool(drawing.ToolId);
        DrawingLevel[] levels = rawLevels.Where(level => level.Enabled).ToArray();
        double best = double.MaxValue;

        if (drawing.ToolId == "fib-retracement")
        {
            double startPrice = drawing.Anchors[0].Price;
            double endPrice = drawing.Anchors[1].Price;
            if (ParityFlag(drawing, "Reverse", false)) (startPrice, endPrice) = (endPrice, startPrice);
            double left = Math.Min(points[0].X, points[1].X);
            double right = Math.Max(points[0].X, points[1].X);
            if (drawing.Style.ExtendLeft || ParityFlag(drawing, "ExtendLeft", false)) left = layout.Plot.Left;
            if (drawing.Style.ExtendRight || ParityFlag(drawing, "ExtendRight", false)) right = layout.Plot.Right;
            foreach (DrawingLevel level in levels)
            {
                double y = PriceToY(startPrice + (endPrice - startPrice) * level.Value, layout);
                best = Math.Min(best, DistanceToSegment(point, new Point(left, y), new Point(right, y)));
            }
            return best;
        }

        if (drawing.ToolId == "trend-fib-extension" && points.Length >= 3)
        {
            double move = drawing.Anchors[1].Price - drawing.Anchors[0].Price;
            if (ParityFlag(drawing, "Reverse", false)) move = -move;
            double originPrice = drawing.Anchors[2].Price;
            double direction = Math.Sign(points[2].X - points[1].X);
            if (Math.Abs(direction) < 0.5) direction = 1;
            double naturalWidth = Math.Max(90, Math.Abs(points[1].X - points[0].X));
            double x2 = points[2].X + direction * naturalWidth;
            double left = Math.Min(points[2].X, x2);
            double right = Math.Max(points[2].X, x2);
            if (drawing.Style.ExtendLeft || ParityFlag(drawing, "ExtendLeft", false)) left = layout.Plot.Left;
            if (drawing.Style.ExtendRight || ParityFlag(drawing, "ExtendRight", false)) right = layout.Plot.Right;
            foreach (DrawingLevel level in levels)
            {
                double y = PriceToY(originPrice + move * level.Value, layout);
                best = Math.Min(best, DistanceToSegment(point, new Point(left, y), new Point(right, y)));
            }
            return best;
        }

        if (drawing.ToolId == "fib-channel" && points.Length >= 3)
        {
            Vector offset = points[2] - points[0];
            bool reverse = ParityFlag(drawing, "Reverse", false);
            foreach (DrawingLevel level in levels)
            {
                double ratio = reverse ? -level.Value : level.Value;
                Point a = points[0] + offset * ratio;
                Point b = points[1] + offset * ratio;
                best = Math.Min(best, DistanceToSegment(point, a, b));
            }
            return best;
        }

        if (drawing.ToolId is "fib-time-zone" or "trend-fib-time")
        {
            Point start = points[0];
            Point end = points[1];
            double span = end.X - start.X;
            if (ParityFlag(drawing, "Reverse", false)) span = -span;
            double origin = drawing.ToolId == "trend-fib-time" && points.Length >= 3 ? points[2].X : start.X;
            foreach (DrawingLevel level in levels.Where(level => level.Value >= 0))
            {
                double x = origin + span * level.Value;
                if (x >= layout.Plot.Left - 10 && x <= layout.Plot.Right + 10)
                    best = Math.Min(best, Math.Abs(point.X - x));
            }
            return best;
        }

        if (drawing.ToolId == "fib-circles")
        {
            Vector radius = points[1] - points[0];
            double rx = Math.Max(1, Math.Abs(radius.X));
            double ry = Math.Max(1, Math.Abs(radius.Y));
            foreach (DrawingLevel level in levels.Where(level => level.Value > 0))
                best = Math.Min(best, DistanceToEllipseStroke(point, points[0], rx * level.Value, ry * level.Value));
            return best;
        }

        if (drawing.ToolId == "fib-speed-arcs")
        {
            Vector direction = points[1] - points[0];
            double baseRadius = direction.Length;
            if (baseRadius < 0.5) return Distance(point, points[0]);
            direction.Normalize();
            Vector relative = point - points[0];
            if (Vector.Multiply(relative, direction) < -10) return double.MaxValue;
            double radial = relative.Length;
            foreach (DrawingLevel level in levels.Where(level => level.Value > 0))
                best = Math.Min(best, Math.Abs(radial - baseRadius * level.Value));
            return best;
        }

        if (drawing.ToolId is "fib-speed-fan" or "gann-fan")
        {
            Point origin = points[0];
            Vector vector = points[1] - points[0];
            foreach (DrawingLevel level in levels.Where(level => Math.Abs(level.Value) > 1e-12))
            {
                Point target = drawing.ToolId == "gann-fan"
                    ? GannFanTarget(origin, points[1], level.Value, ParityFlag(drawing, "Reverse", false))
                    : new Point(points[1].X, points[0].Y + vector.Y * level.Value);
                best = Math.Min(best, DistanceToRay(point, origin, target));
            }
            return best;
        }

        if (drawing.ToolId == "pitchfan" && points.Length >= 3)
        {
            Vector span = points[2] - points[1];
            foreach (DrawingLevel level in levels)
                best = Math.Min(best, DistanceToRay(point, points[0], points[1] + span * level.Value));
            return best;
        }

        if (drawing.ToolId == "fib-wedge" && points.Length >= 3)
        {
            Point center = points[0];
            Vector a = points[1] - center;
            Vector b = points[2] - center;
            double baseRadius = Math.Max(a.Length, b.Length);
            if (baseRadius < 0.5) return Distance(point, center);
            if (!PointWithinSmallSector(center, points[1], points[2], point))
                return Math.Min(DistanceToRay(point, center, points[1]), DistanceToRay(point, center, points[2]));
            double radial = Distance(center, point);
            foreach (DrawingLevel level in levels.Where(level => level.Value > 0))
                best = Math.Min(best, Math.Abs(radial - baseRadius * level.Value));
            return Math.Min(best, Math.Min(DistanceToRay(point, center, points[1]), DistanceToRay(point, center, points[2])));
        }

        if (drawing.ToolId == "gann-box")
        {
            Rect rect = Bounds(new[] { points[0], points[1] });
            if (!Inflate(rect, 12).Contains(point)) return double.PositiveInfinity;

            best = Math.Min(best, Math.Abs(point.X - rect.Left));
            best = Math.Min(best, Math.Abs(point.X - rect.Right));
            best = Math.Min(best, Math.Abs(point.Y - rect.Top));
            best = Math.Min(best, Math.Abs(point.Y - rect.Bottom));

            DrawingLevel[] gridLevels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool("gann-box"))
                .Where(level => level.Enabled && level.Value >= 0 && level.Value <= 1)
                .OrderBy(level => level.Value).ToArray();
            foreach (DrawingLevel level in gridLevels.Where(level => level.Value > 0.000001 && level.Value < 0.999999))
            {
                double x = rect.Left + rect.Width * level.Value;
                double y = rect.Bottom - rect.Height * level.Value;
                best = Math.Min(best, Math.Abs(point.X - x));
                best = Math.Min(best, Math.Abs(point.Y - y));
            }

            // Gann Box parity: hit-test only the geometry actually rendered
            // by the ratio matrix (outer box + horizontal/vertical levels).
            return best;
        }

        if (drawing.ToolId is "gann-square" or "gann-square-fixed")
        {
            Point p1 = points[0];
            Point p2 = GetGannDisplaySecondPoint(drawing, p1, points[1]);
            Rect rect = Bounds(new[] { p1, p2 });
            if (!Inflate(rect, 12).Contains(point)) return double.PositiveInfinity;

            best = Math.Min(best, Math.Abs(point.X - rect.Left));
            best = Math.Min(best, Math.Abs(point.X - rect.Right));
            best = Math.Min(best, Math.Abs(point.Y - rect.Top));
            best = Math.Min(best, Math.Abs(point.Y - rect.Bottom));

            DrawingLevel[] gridLevels = (drawing.Levels.Count > 0 ? drawing.Levels : DrawingParityDefaults.LevelsForTool(drawing.ToolId))
                .Where(level => level.Enabled && level.Value >= 0 && level.Value <= 1)
                .OrderBy(level => level.Value).ToArray();

            foreach (DrawingLevel level in gridLevels.Where(level => level.Value > 0.000001 && level.Value < 0.999999))
            {
                double x = rect.Left + rect.Width * level.Value;
                double y = rect.Bottom - rect.Height * level.Value;
                best = Math.Min(best, Math.Abs(point.X - x));
                best = Math.Min(best, Math.Abs(point.Y - y));
            }

            bool gannReverse = ParityFlag(drawing, "Reverse", false);
            Point squareOrigin = gannReverse ? rect.BottomRight : rect.BottomLeft;
            if (ParityFlag(drawing, "Fan", true))
            {
                (double X, double Y)[] fans = { (2, 1), (1, 1), (1, 2) };
                foreach ((double fx, double fy) in fans)
                {
                    double max = Math.Max(fx, fy);
                    double nx = fx / max;
                    double ny = fy / max;
                    double sx = gannReverse ? -1 : 1;
                    Point target = new(squareOrigin.X + sx * rect.Width * nx, squareOrigin.Y - rect.Height * ny);
                    best = Math.Min(best, DistanceToSegment(point, squareOrigin, target));
                }
            }
            if (ParityFlag(drawing, "Arcs", true))
            {
                (double X, double Y)[] arcs =
                {
                    (1, 0), (1, 1), (1.5, 0), (2, 0), (2, 1),
                    (3, 0), (3, 1), (4, 0), (4, 1), (5, 0), (5, 1)
                };
                foreach ((double ax, double ay) in arcs)
                {
                    double normalizedRadius = Math.Sqrt(ax * ax + ay * ay) / 5.0;
                    best = Math.Min(best, DistanceToGannQuarterEllipse(point, rect, squareOrigin, normalizedRadius, gannReverse));
                }
            }
            return best;
        }

        if (drawing.ToolId == "fib-spiral")
        {
            Point center = points[0];
            double baseRadius = Math.Max(1, Distance(center, points[1]));
            double startAngle = Math.Atan2(points[1].Y - center.Y, points[1].X - center.X);
            double sign = ParityFlag(drawing, "Reverse", false) ? -1 : 1;
            const int steps = 140;
            const double turns = 4.25;
            const double phi = 1.618033988749895;
            double growth = Math.Log(phi) / (Math.PI / 2.0);
            double maxRaw = Math.Exp(growth * turns * Math.PI * 2.0);
            Point previous = center;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double theta = sign * t * turns * Math.PI * 2.0;
                double radius = baseRadius * Math.Exp(growth * Math.Abs(theta)) / maxRaw;
                double angle = startAngle + theta;
                Point current = new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                if (i > 0) best = Math.Min(best, DistanceToSegment(point, previous, current));
                previous = current;
            }
            return best;
        }

        return double.PositiveInfinity;
    }

    private static double DistanceToEllipseStroke(Point point, Point center, double rx, double ry)
    {
        if (rx < 0.001 || ry < 0.001) return Distance(point, center);
        double nx = (point.X - center.X) / rx;
        double ny = (point.Y - center.Y) / ry;
        double normalized = Math.Sqrt(nx * nx + ny * ny);
        return Math.Abs(normalized - 1.0) * Math.Min(rx, ry);
    }

    private static bool PointWithinSmallSector(Point center, Point first, Point second, Point point)
    {
        Vector a = first - center;
        Vector b = second - center;
        Vector p = point - center;
        if (a.LengthSquared < 0.001 || b.LengthSquared < 0.001 || p.LengthSquared < 0.001) return true;
        double crossAB = a.X * b.Y - a.Y * b.X;
        double crossAP = a.X * p.Y - a.Y * p.X;
        double crossPB = p.X * b.Y - p.Y * b.X;
        return crossAB >= 0 ? crossAP >= -0.001 && crossPB >= -0.001 : crossAP <= 0.001 && crossPB <= 0.001;
    }

    private static double DistanceToInfiniteLine(Point point, Point origin, Point through)
    {
        Vector direction = through - origin;
        if (direction.LengthSquared < 0.000001)
            return Distance(point, origin);
        Vector offset = point - origin;
        return Math.Abs(direction.X * offset.Y - direction.Y * offset.X) / Math.Sqrt(direction.LengthSquared);
    }

    private static double DistanceToRay(Point point, Point origin, Point through)
    {
        Vector direction = through - origin;
        double lengthSquared = direction.LengthSquared;
        if (lengthSquared < 0.000001)
            return Distance(point, origin);
        Vector offset = point - origin;
        double projection = Vector.Multiply(offset, direction) / lengthSquared;
        if (projection < 0)
            return Distance(point, origin);
        Point projected = origin + direction * projection;
        return Distance(point, projected);
    }

    private static Point GetGannDisplaySecondPoint(ChartDrawing drawing, Point p1, Point p2)
    {
        if (drawing.ToolId == "gann-square-fixed")
        {
            double side = Math.Max(24, Math.Max(Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y)));
            double sx = p2.X >= p1.X ? 1 : -1;
            double sy = p2.Y >= p1.Y ? 1 : -1;
            return new Point(p1.X + side * sx, p1.Y + side * sy);
        }
        return p2;
    }

    private static double DistanceToGannQuarterEllipse(Point point, Rect rect, Point origin, double normalizedRadius, bool reverse)
    {
        double radius = Math.Max(0.0001, normalizedRadius);
        double sx = reverse ? -1 : 1;
        const int samples = 48;
        Point? previous = null;
        double best = double.PositiveInfinity;
        Rect clip = Inflate(rect, 4);
        for (int i = 0; i <= samples; i++)
        {
            double theta = (Math.PI / 2.0) * i / samples;
            Point current = new(
                origin.X + sx * rect.Width * radius * Math.Cos(theta),
                origin.Y - rect.Height * radius * Math.Sin(theta));
            if (previous is Point prior)
            {
                Rect segmentBounds = Bounds(new[] { prior, current });
                if (clip.Contains(prior) || clip.Contains(current) || segmentBounds.IntersectsWith(clip))
                    best = Math.Min(best, DistanceToSegment(point, prior, current));
            }
            previous = current;
        }
        return best;
    }

    private Point Folder6AnnotationSafePoint(DrawingAnchor anchor, ChartLayout layout)
    {
        Point point = AnchorToPoint(anchor, layout);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return new Point(layout.Plot.Left + layout.Plot.Width * 0.5, layout.Plot.Top + layout.Plot.Height * 0.5);

        // Fresh anchors are already inside the plot and are returned unchanged.
        // Only stale/corrupt coordinates are pulled back to the nearest visible edge.
        return new Point(
            Math.Clamp(point.X, layout.Plot.Left + 2, Math.Max(layout.Plot.Left + 2, layout.Plot.Right - 2)),
            Math.Clamp(point.Y, layout.Plot.Top + 2, Math.Max(layout.Plot.Top + 2, layout.Plot.Bottom - 2)));
    }

    private static bool IsFolder6AnnotationTool(string toolId) =>
        toolId is
            "text" or "note" or "price-note" or "pin" or "table" or "callout" or
            "comment" or "price-label" or "signpost" or "anchored-note" or "flag-mark" or
            "image" or "post" or "idea" ||
        DrawingToolCatalog.Find(toolId)?.Category == DrawingToolCategory.Annotation;

    private Rect GetFolder6AnnotationBounds(ChartDrawing drawing, ChartLayout layout)
    {
        Point[] points = drawing.Anchors.Select(anchor => Folder6AnnotationSafePoint(anchor, layout)).ToArray();
        if (points.Length == 0) return Rect.Empty;
        DrawingStyle style = drawing.Style;
        string value = string.IsNullOrWhiteSpace(drawing.Text) ? "Add text" : drawing.Text;
        FormattedText text = CreateDrawingText(value, style, CreateDrawingBrush(style.TextColor, style.Opacity), style.FontSize);
        Point a = points[0];
        switch (drawing.ToolId)
        {
            case "text":
            {
                Point at = ParityTextOrigin(a, text.Width, text.Height, ParityAnnotationAnchor(drawing), 7);
                return new Rect(at.X, at.Y, Math.Max(8, text.Width), Math.Max(8, text.Height));
            }
            case "note":
            {
                Point b = points.Length > 1 ? points[1] : new Point(a.X + 110, a.Y - 48);
                double width = Math.Max(90, text.Width + 18);
                double height = text.Height + 12;
                Point topLeft = ParityTextOrigin(b, width, height,
                    ParityAnnotationAnchor(drawing) == "Auto" ? "Right" : ParityAnnotationAnchor(drawing), 7);
                return Bounds(new[] { a, b, topLeft, new Point(topLeft.X + width, topLeft.Y + height) });
            }
            case "price-note":
            {
                Point b = points.Length > 1 ? points[1] : new Point(a.X + 110, a.Y - 48);
                string price = FormatParityPrice(drawing.Anchors[0]);
                FormattedText priceText = CreateDrawingText(price, style, CreateDrawingBrush(style.TextColor, style.Opacity), style.FontSize);
                double width = priceText.Width + 20;
                double height = priceText.Height + 12;
                Point topLeft = ParityTextOrigin(b, width, height,
                    ParityAnnotationAnchor(drawing) == "Auto" ? "Right" : ParityAnnotationAnchor(drawing), 7);
                return Bounds(new[] { a, b, topLeft, new Point(topLeft.X + width, topLeft.Y + height) });
            }
            case "pin":
            {
                double width = Math.Max(82, text.Width + 18);
                double height = text.Height + 12;
                return new Rect(a.X - width / 2 - 4, a.Y - 58 - height, width + 8, 65 + height);
            }
            case "table":
                return points.Length >= 2 ? CreateNormalizedRect(points[0], points[1]) : CreateNormalizedRect(a, new Point(a.X + 210, a.Y + 120));
            case "callout":
            {
                Point b = points.Length > 1 ? points[1] : new Point(a.X + 110, a.Y - 48);
                double width = Math.Max(86, text.Width + 20);
                double height = text.Height + 14;
                Point topLeft = ParityTextOrigin(b, width, height, "Right", 8);
                return Bounds(new[] { a, b, topLeft, new Point(topLeft.X + width, topLeft.Y + height) });
            }
            case "comment":
                return new Rect(a.X - 4, a.Y - (text.Height + 14) / 2 - 4, Math.Max(76, text.Width + 20) + 18, text.Height + 22);
            case "price-label":
            {
                string price = FormatParityPrice(drawing.Anchors[0]);
                FormattedText priceText = CreateDrawingText(price, style, CreateDrawingBrush(style.TextColor, style.Opacity), style.FontSize);
                return new Rect(a.X - 4, a.Y - priceText.Height / 2 - 10, priceText.Width + 38, priceText.Height + 20);
            }
            case "signpost":
            {
                double width = Math.Max(92, text.Width + 20);
                double height = text.Height + 14;
                return new Rect(a.X - width / 2 - 4, a.Y - height - 24, width + 8, Math.Max(height + 28, layout.Plot.Bottom - (a.Y - height - 24)));
            }
            case "flag-mark":
                return new Rect(a.X - 5, a.Y - 36, 38, 44);
            default:
                return Bounds(points);
        }
    }

    private Rect GetDrawingBounds(ChartDrawing drawing, ChartLayout layout)
    {
        Point[] points = drawing.Anchors.Select(anchor => AnchorToPoint(anchor, layout)).ToArray();
        if (points.Length == 0) return Rect.Empty;
        if (IsFolder6AnnotationTool(drawing.ToolId))
            return Inflate(GetFolder6AnnotationBounds(drawing, layout), 6);
        Rect bounds = Bounds(points);
        DrawingGeometryKind geometry = DrawingToolCatalog.Find(drawing.ToolId)?.Geometry ?? DrawingGeometryKind.Line;
        if (drawing.ToolId == "bars-pattern")
        {
            IReadOnlyList<Point> projected = GetBarsPatternProjectedPoints(drawing, layout);
            if (projected.Count > 0)
                bounds = Bounds(projected);
        }
        else if (drawing.ToolId == "ghost-feed")
        {
            IReadOnlyList<GhostFeedVisualBar> bars = BuildGhostFeedVisualBars(drawing, layout);
            if (bars.Count > 0)
            {
                var visualPoints = new List<Point>(bars.Count * 4);
                foreach (GhostFeedVisualBar bar in bars)
                {
                    visualPoints.Add(bar.WickTop);
                    visualPoints.Add(bar.WickBottom);
                    visualPoints.Add(bar.Body.TopLeft);
                    visualPoints.Add(bar.Body.BottomRight);
                }
                bounds = Bounds(visualPoints);
            }
        }
        else if (drawing.ToolId == "anchored-vwap")
        {
            AnchoredVwapVisual? visual = BuildParityAnchoredVwapVisual(layout, drawing);
            if (visual is not null)
            {
                var visualPoints = new List<Point>(visual.Main);
                foreach (var band in visual.Bands)
                {
                    visualPoints.AddRange(band.Up);
                    visualPoints.AddRange(band.Down);
                }
                if (visualPoints.Count > 0)
                    bounds = Bounds(visualPoints);
            }
        }
        else if (drawing.ToolId is "fixed-volume-profile" or "anchored-volume-profile")
        {
            ParityVolumeProfileVisual? visual = BuildParityVolumeProfileVisual(layout, drawing);
            if (visual is not null)
            {
                var visualPoints = new List<Point>
                {
                    visual.HistogramBounds.TopLeft,
                    visual.HistogramBounds.BottomRight,
                    new Point(visual.GuideLeft, visual.PocY),
                    new Point(visual.GuideRight, visual.PocY),
                    new Point(visual.GuideLeft, visual.VahY),
                    new Point(visual.GuideRight, visual.ValY)
                };
                visualPoints.AddRange(visual.DevelopingPoc);
                visualPoints.AddRange(visual.DevelopingVah);
                visualPoints.AddRange(visual.DevelopingVal);
                bounds = Bounds(visualPoints);
            }
        }
        if (geometry == DrawingGeometryKind.RotatedRectangle &&
            TryGetRotatedRectangleGeometry(points, out Point[] rotatedCorners, out _, out _, out _, out _, out _))
        {
            bounds = Bounds(rotatedCorners);
        }
        if (drawing.ToolId is "gann-square" or "gann-square-fixed" && points.Length >= 2)
        {
            Point adjusted = GetGannDisplaySecondPoint(drawing, points[0], points[1]);
            bounds = Bounds(new[] { points[0], adjusted });
        }
        if (drawing.ToolId == "parallel-channel" && points.Length >= 3)
        {
            Vector channelOffset = new(0, points[2].Y - points[1].Y);
            Point channelC = points[0] + channelOffset;
            Point channelD = points[1] + channelOffset;
            bounds = Bounds(new[] { points[0], points[1], channelD, channelC });
        }
        if (geometry == DrawingGeometryKind.Regression &&
            TryGetParityRegressionGeometry(drawing, layout, out Point r1, out Point r2,
                out Point ru1, out Point ru2, out Point rl1, out Point rl2, out _))
        {
            bounds = Bounds(new[] { r1, r2, ru1, ru2, rl1, rl2 });
        }
        if (drawing.ToolId == "circle" && points.Length >= 2)
        {
            double radius = Math.Max(1, Distance(points[0], points[1]));
            bounds = new Rect(points[0].X - radius, points[0].Y - radius, radius * 2, radius * 2);
        }
        else if (drawing.ToolId == "pin")
            bounds = new Rect(points[0].X - 12, points[0].Y - 24, 24, 30);
        else if (geometry is DrawingGeometryKind.HorizontalLine or DrawingGeometryKind.CrossLine)
            bounds = new Rect(layout.Plot.Left, points[0].Y - 4, layout.Plot.Width, 8);
        else if (geometry == DrawingGeometryKind.VerticalLine)
            bounds = new Rect(points[0].X - 4, layout.Plot.Top, 8, layout.Plot.Height);
        else if (geometry is DrawingGeometryKind.Text or DrawingGeometryKind.Note or DrawingGeometryKind.Callout or DrawingGeometryKind.PriceLabel)
        {
            DrawingStyle style = drawing.Style;
            FormattedText text = CreateDrawingText(string.IsNullOrWhiteSpace(drawing.Text) ? drawing.DisplayName : drawing.Text,
                style, Brushes.White, style.FontSize);
            bounds = new Rect(points[0].X, points[0].Y - text.Height - 14, text.Width + 14, text.Height + 14);
        }
        else if (geometry == DrawingGeometryKind.Icon)
        {
            if (DrawingMediaCatalog.TryDecode(drawing.Text, out DrawingMediaDefinition? media) && media is not null)
                bounds = GetTickLabMediaBounds(points[0], media, drawing);
            else
                bounds = new Rect(points[0].X - 24, points[0].Y - 24, 48, 48);
        }
        return Inflate(bounds, 5);
    }

    private void CloneSelectedDrawings()
    {
        ChartDrawing[] selected = _drawings.Where(item => _selectedDrawingIds.Contains(item.Id)).ToArray();
        if (selected.Length == 0) return;
        PushDrawingUndo();
        _selectedDrawingIds.Clear();
        foreach (ChartDrawing source in selected)
        {
            ChartDrawing clone = CloneDrawing(source);
            _drawings.Add(clone);
            _selectedDrawingIds.Add(clone.Id);
        }
        NotifyDrawingChanged("Drawing cloned.");
    }

    private ChartDrawing CloneDrawing(ChartDrawing source)
    {
        long timeShift = GetDrawingSlotSeconds() * 3;
        DrawingAnchor[] anchors = source.Anchors.Select(anchor =>
            anchor with { StartUnix = SafeTimestampOffset(anchor.StartUnix, timeShift) }).ToArray();
        return source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Anchors = anchors,
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.DisplayName + " copy" : source.Name + " copy",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ZIndex = _drawings.Count
        };
    }

    private void PasteCopiedDrawing()
    {
        if (_copiedDrawing is null) return;
        PushDrawingUndo();
        ChartDrawing clone = CloneDrawing(_copiedDrawing);
        _drawings.Add(clone);
        _selectedDrawingIds.Clear();
        _selectedDrawingIds.Add(clone.Id);
        NotifyDrawingChanged("Drawing pasted.");
    }

    private void DeleteSelectedDrawings()
    {
        if (_selectedDrawingIds.Count == 0) return;
        PushDrawingUndo();
        _drawings.RemoveAll(item => _selectedDrawingIds.Contains(item.Id) && !item.IsLocked && !_lockAllDrawings);
        _selectedDrawingIds.Clear();
        NotifyDrawingChanged("Selected drawings removed.");
    }

    private void NudgeSelectedDrawings(Key key)
    {
        if (_selectedDrawingIds.Count == 0 || DrawingCandles.Count == 0) return;
        PushDrawingUndo();
        double pointSize = Math.Max(1e-10, DrawingCandles.Last().Point);
        for (int i = 0; i < _drawings.Count; i++)
        {
            ChartDrawing drawing = _drawings[i];
            if (!_selectedDrawingIds.Contains(drawing.Id) || drawing.IsLocked || _lockAllDrawings) continue;
            long horizontalShift = key == Key.Left
                ? -GetDrawingSlotSeconds()
                : key == Key.Right
                    ? GetDrawingSlotSeconds()
                    : 0;
            DrawingAnchor[] anchors = drawing.Anchors.Select(anchor =>
            {
                double price = anchor.Price +
                    (key == Key.Up ? pointSize : key == Key.Down ? -pointSize : 0);
                return anchor with
                {
                    StartUnix = SafeTimestampOffset(anchor.StartUnix, horizontalShift),
                    Price = price
                };
            }).ToArray();
            _drawings[i] = drawing with { Anchors = anchors, UpdatedAt = DateTimeOffset.UtcNow };
        }
        NotifyDrawingChanged("Drawing moved one step.");
    }

    private void GroupSelectedDrawings()
    {
        if (_selectedDrawingIds.Count < 2) return;
        PushDrawingUndo();
        string groupId = Guid.NewGuid().ToString("N");
        for (int i = 0; i < _drawings.Count; i++)
        {
            if (_selectedDrawingIds.Contains(_drawings[i].Id))
                _drawings[i] = _drawings[i] with { GroupId = groupId };
        }
        NotifyDrawingChanged("Drawings grouped.");
    }

    private void UngroupDrawing(string id)
    {
        ChartDrawing? drawing = _drawings.FirstOrDefault(item => item.Id == id);
        if (drawing is null || string.IsNullOrWhiteSpace(drawing.GroupId)) return;
        PushDrawingUndo();
        string group = drawing.GroupId;
        for (int i = 0; i < _drawings.Count; i++)
        {
            if (_drawings[i].GroupId == group)
                _drawings[i] = _drawings[i] with { GroupId = string.Empty };
        }
        NotifyDrawingChanged("Drawings ungrouped.");
    }

    private static Rect CreateNormalizedRect(Point a, Point b) =>
        new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    private static Rect Bounds(IEnumerable<Point> points)
    {
        Point[] values = points.ToArray();
        if (values.Length == 0) return Rect.Empty;
        double left = values.Min(p => p.X), right = values.Max(p => p.X), top = values.Min(p => p.Y), bottom = values.Max(p => p.Y);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        if (rect.IsEmpty) return rect;
        rect.Inflate(amount, amount);
        return rect;
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    private static double Distance(Point a, Point b) => (a - b).Length;

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        Vector ab = b - a;
        if (ab.LengthSquared < 0.0001) return Distance(p, a);
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / ab.LengthSquared, 0, 1);
        Point projection = a + ab * t;
        return Distance(p, projection);
    }

    private static double DistanceToRect(Point p, Rect rect)
    {
        double dx = Math.Max(rect.Left - p.X, Math.Max(0, p.X - rect.Right));
        double dy = Math.Max(rect.Top - p.Y, Math.Max(0, p.Y - rect.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{value.TotalDays:0.##} days";
        if (value.TotalHours >= 1) return $"{value.TotalHours:0.##} hours";
        if (value.TotalMinutes >= 1) return $"{value.TotalMinutes:0.##} min";
        return $"{value.TotalSeconds:0.##} sec";
    }

    private readonly record struct DrawingHitInfo(ChartDrawing Drawing, int AnchorIndex, double Distance);

    private enum DrawingDragMode
    {
        None,
        Anchor,
        Body
    }
}
