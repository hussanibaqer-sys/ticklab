using System.Text.Json.Serialization;

namespace TickLab.Core.Drawing;

public enum DrawingToolCategory
{
    Cursor,
    TrendLine,
    FibonacciGann,
    Pattern,
    PredictionMeasurement,
    Geometry,
    Annotation,
    IconsMedia
}

public enum DrawingGeometryKind
{
    Cursor,
    Eraser,
    Line,
    ArrowLine,
    Ray,
    ExtendedLine,
    HorizontalLine,
    HorizontalRay,
    VerticalLine,
    CrossLine,
    Channel,
    Regression,
    AnchoredVwap,
    Fibonacci,
    FibonacciExtension,
    FibonacciChannel,
    FibonacciTime,
    FibonacciFan,
    FibonacciCircles,
    FibonacciSpiral,
    FibonacciWedge,
    FibonacciArcs,
    Pitchfork,
    GannBox,
    GannFan,
    Brush,
    Highlighter,
    Rectangle,
    RotatedRectangle,
    Ellipse,
    Triangle,
    Polyline,
    Curve,
    DoubleCurve,
    Arc,
    Text,
    Note,
    Callout,
    PriceLabel,
    ArrowMarker,
    Flag,
    Pattern,
    Cycles,
    Sine,
    Position,
    Range,
    BarsPattern,
    GhostFeed,
    Sector,
    VolumeProfile,
    Image,
    Icon
}

public enum DrawingLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public enum DrawingMagnetMode
{
    Off,
    Weak,
    Strong
}

public enum DrawingVisualLayer
{
    BelowCandles,
    AboveCandles,
    AboveIndicators
}

public enum DrawingSyncMode
{
    CurrentChart,
    SameSymbol,
    SameSymbolAndTimeframe,
    CurrentLayout,
    Global
}

public sealed record DrawingToolDefinition(
    string Id,
    string DisplayName,
    DrawingToolCategory Category,
    string Icon,
    DrawingGeometryKind Geometry,
    int MinimumAnchors,
    int MaximumAnchors,
    bool VariableAnchors = false,
    bool SupportsText = false,
    bool SupportsFill = false,
    bool SupportsLevels = false,
    bool IsCursorTool = false);

public sealed record DrawingAnchor(
    long StartUnix,
    double Price,
    double? IndicatorValue = null)
{
    // Optional exact raw-tick timestamp. Existing candle drawings continue to
    // serialize/use StartUnix exactly as before; Tick-chart drawings keep the
    // millisecond identity here so several ticks inside the same second remain
    // distinct while using the very same ChartDrawing/drawing-engine model.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? StartMilliseconds { get; init; }
}


public sealed record DrawingVisibility(
    bool Seconds = true,
    bool Minutes = true,
    bool Hours = true,
    bool Daily = true,
    bool Weekly = true,
    bool Monthly = true,
    string MinimumTimeframe = "",
    string MaximumTimeframe = "");

public sealed record DrawingLevel(
    double Value,
    string Label,
    bool Enabled = true,
    string Color = "#94A3B8",
    double Width = 1.0,
    DrawingLineStyle LineStyle = DrawingLineStyle.Solid,
    bool ShowPrice = true,
    bool ShowValue = true,
    string FillColor = "",
    double FillOpacity = -1);

public sealed record DrawingStyle
{
    public string LineColor { get; init; } = "#3B82F6";
    public string FillColor { get; init; } = "#3B82F6";
    public string TextColor { get; init; } = "#F8FAFC";
    public string BackgroundColor { get; init; } = "#111827";
    public double LineWidth { get; init; } = 2.0;
    public double Opacity { get; init; } = 1.0;
    public double FillOpacity { get; init; } = 0.12;
    public DrawingLineStyle LineStyle { get; init; } = DrawingLineStyle.Solid;
    public bool ExtendLeft { get; init; }
    public bool ExtendRight { get; init; }
    public bool ShowPriceLabels { get; init; } = true;
    public bool ShowTimeLabels { get; init; }
    public bool ShowStatistics { get; init; } = true;
    public bool ShowMiddleLine { get; init; }
    public bool ArrowStart { get; init; }
    public bool ArrowEnd { get; init; }
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 12;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public string HorizontalTextAlignment { get; init; } = "Center";
    public string VerticalTextAlignment { get; init; } = "Center";
}

public sealed record ChartDrawing
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ToolId { get; init; } = "trend-line";
    public string DisplayName { get; init; } = "Trend Line";
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string ChartId { get; init; } = "main-chart-1";
    public string PaneId { get; init; } = "price";
    public IReadOnlyList<DrawingAnchor> Anchors { get; init; } = Array.Empty<DrawingAnchor>();
    public DrawingStyle Style { get; init; } = new();
    public DrawingVisibility Visibility { get; init; } = new();
    public IReadOnlyList<DrawingLevel> Levels { get; init; } = Array.Empty<DrawingLevel>();
    public string Text { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsLocked { get; init; }
    public bool IsHidden { get; init; }
    public int ZIndex { get; init; }
    public DrawingVisualLayer VisualLayer { get; init; } = DrawingVisualLayer.AboveCandles;
    public DrawingSyncMode SyncMode { get; init; } = DrawingSyncMode.CurrentChart;
    public string GroupId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, double> NumericOptions { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> TextOptions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record DrawingTemplate(
    string Id,
    string Name,
    string ToolId,
    DrawingStyle Style,
    IReadOnlyList<DrawingLevel> Levels,
    IReadOnlyDictionary<string, double> NumericOptions,
    IReadOnlyDictionary<string, string> TextOptions,
    bool IsDefault = false);

public sealed record DrawingWorkspaceState
{
    public IReadOnlyList<ChartDrawing> Drawings { get; init; } = Array.Empty<ChartDrawing>();
    public IReadOnlyList<string> FavoriteToolIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecentToolIds { get; init; } = Array.Empty<string>();
    public string LastUsedToolId { get; init; } = "trend-line";
    public DrawingMagnetMode MagnetMode { get; init; } = DrawingMagnetMode.Off;
    public bool SnapToIndicators { get; init; }
    public bool StayInDrawingMode { get; init; }
    public bool HideAllDrawings { get; init; }
    public bool LockAllDrawings { get; init; }
    public bool CursorValuesTooltipOnLongPress { get; init; } = true;
    public DrawingSyncMode DefaultSyncMode { get; init; } = DrawingSyncMode.CurrentChart;
    public IReadOnlyList<DrawingTemplate> Templates { get; init; } = Array.Empty<DrawingTemplate>();
}
