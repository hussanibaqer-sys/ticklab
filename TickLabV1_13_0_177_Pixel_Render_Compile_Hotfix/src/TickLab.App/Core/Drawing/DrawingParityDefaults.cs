namespace TickLab.Core.Drawing;

/// <summary>
/// TickLab-owned defaults for TradingView-style drawing behaviour.  The values
/// are stored on each drawing so templates/workspaces remain deterministic.
/// </summary>
public static class DrawingParityDefaults
{
    public static IReadOnlyDictionary<string, double> NumericOptions(string? toolId)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, double value) => values[name] = value;
        string id = toolId ?? string.Empty;

        switch (id)
        {
            case "trend-line":
            case "ray":
            case "extended-line":
            case "info-line":
            case "trend-angle":
                Add("MiddlePoint", 0);
                Add("AngleLabel", id == "trend-angle" ? 1 : 0);
                Add("AlwaysShowStats", id == "info-line" ? 1 : 0);
                Add("ShowDistance", id == "info-line" ? 1 : 0);
                Add("ShowBars", id == "info-line" ? 1 : 0);
                break;
            case "parallel-channel":
                Add("MiddleLine", 0);
                break;
            case "flat-top-bottom":
                Add("FlatSide", 0); // 0 auto, 1 top, -1 bottom
                Add("MiddleLine", 0);
                break;
            case "disjoint-channel":
                Add("MiddleLine", 0);
                break;
            case "regression-trend":
                Add("UpperDeviation", 2.0);
                Add("LowerDeviation", 2.0);
                Add("ShowUpper", 1);
                Add("ShowLower", 1);
                Add("ShowBase", 1);
                Add("PearsonsR", 1);
                Add("ExtendRight", 0);
                Add("Source", 3); // 0 open,1 high,2 low,3 close,4 hl2,5 hlc3,6 ohlc4
                break;
            case "anchored-vwap":
                Add("Source", 5); // HLC3
                Add("Band1", 1); // TradingView reference: one visible upper/lower deviation pair by default
                Add("Band1Multiplier", 1);
                Add("Band2", 0);
                Add("Band2Multiplier", 2);
                Add("Band3", 0);
                Add("Band3Multiplier", 3);
                break;
            case "fib-retracement":
            case "trend-fib-extension":
            case "fib-channel":
                Add("Reverse", 0);
                Add("UseOneColor", 0);
                Add("LabelsPercent", 0);
                Add("LabelsLeft", 0); // legacy compatibility
                Add("ShowLevelReadings", 1);
                Add("ShowLevelPrices", 1);
                Add("LabelsOutside", 0);
                Add("LabelHorizontal", 1); // -1 left, 0 centre, 1 right
                Add("LabelVertical", -1);  // -1 above, 0 middle, 1 below
                Add("Bands", 1);
                Add("ExtendLeft", 0);
                Add("ExtendRight", 0);
                break;
            case "fib-time-zone":
            case "trend-fib-time":
            case "fib-circles":
            case "fib-spiral":
            case "fib-speed-arcs":
            case "fib-wedge":
            case "fib-speed-fan":
            case "pitchfan":
                Add("Reverse", 0);
                Add("UseOneColor", 0);
                Add("ShowLevelReadings", 1);
                Add("ShowLevelPrices", 1);
                Add("LabelsOutside", 0);
                Add("LabelHorizontal", 1);
                Add("LabelVertical", -1);
                Add("Bands", 1);
                break;
            case "pitchfork":
            case "schiff-pitchfork":
            case "modified-schiff-pitchfork":
            case "inside-pitchfork":
                Add("ExtendRight", 0);
                Add("UseOneColor", 0);
                Add("Background", 1);
                break;
            case "gann-box":
                Add("Levels", 5);
                // Keep legacy keys for workspace/template compatibility.
                Add("TimeLevels", 5);
                Add("PriceLevels", 5);
                Add("Fan", 1);
                Add("Angles", 1);
                Add("Arcs", 1);
                Add("Reverse", 0);
                Add("UseOneColor", 0);
                Add("ShowLevelReadings", 1);
                Add("ShowLevelPrices", 0);
                Add("Bands", 1);
                break;
            case "gann-square":
            case "gann-square-fixed":
                Add("Levels", 5);
                Add("Fan", 1);
                Add("Arcs", 1);
                Add("Reverse", 0);
                Add("UseOneColor", 0);
                Add("ShowLevelReadings", 1);
                Add("ShowLevelPrices", 1);
                Add("Bands", 1);
                break;
            case "gann-fan":
                Add("Reverse", 0);
                Add("UseOneColor", 0);
                Add("ShowLevelReadings", 1);
                Add("ShowLevelPrices", 0);
                Add("Bands", 1);
                break;
            case "long-position":
            case "short-position":
                Add("AccountSize", 10000);
                Add("LotSize", 1);
                Add("Risk", 1);
                Add("RiskIsPercent", 1);
                Add("Leverage", 1);
                Add("PointValue", 1);
                Add("QtyPrecision", 2);
                Add("CompactStats", 0);
                Add("AlwaysShowStats", 0); // details are selection-only; clean zones remain when deselected
                Add("StatsMode", 0); // 0 full, 1 compact, 2 hidden
                Add("PriceLabels", 1);
                break;
            case "position-forecast":
                Add("ShowStats", 1);
                Add("ShowStartLabel", 1);
                Add("ShowEndLabel", 1);
                Add("ShowStatus", 1);
                Add("Success", 0); // runtime market-touch logic promotes Waiting -> SUCCESS
                break;
            case "price-range":
                Add("ShowPriceChange", 1);
                Add("ShowPercent", 1);
                Add("ShowPoints", 1);
                break;
            case "date-range":
                Add("ShowBars", 1);
                Add("ShowDuration", 1);
                Add("ShowVolume", 1);
                break;
            case "date-price-range":
                Add("ShowPriceChange", 1);
                Add("ShowPercent", 1);
                Add("ShowPoints", 1);
                Add("ShowBars", 1);
                Add("ShowDuration", 1);
                Add("ShowVolume", 1);
                break;
            case "fixed-volume-profile":
                Add("RowsLayout", 0); // 0 = number of rows, 1 = ticks/price-step mode
                Add("RowSize", 24);
                Add("VolumeMode", 0); // 0 = Up/Down, 1 = Total
                Add("ValueAreaPercent", 70);
                Add("ExtendRight", 0);
                Add("ShowProfile", 1);
                Add("ShowValues", 0);
                Add("ValuesOpacity", 0.92);
                Add("WidthPercent", 30);
                Add("Placement", -1); // -1 = Left, +1 = Right
                Add("ShowVAH", 1);
                Add("ShowVAL", 1);
                Add("ShowPOC", 1);
                Add("ShowDevelopingPOC", 0);
                Add("ShowDevelopingVA", 0);
                Add("ShowHistogramBox", 0);
                // Legacy keys stay present for old workspaces/templates.
                Add("Rows", 24);
                Add("ShowValueArea", 1);
                Add("UpDownVolume", 1);
                break;
            case "anchored-volume-profile":
                Add("RowsLayout", 0);
                Add("RowSize", 24);
                Add("VolumeMode", 0);
                Add("ValueAreaPercent", 70);
                Add("ExtendRight", 0);
                Add("ShowProfile", 1);
                Add("ShowValues", 0);
                Add("ValuesOpacity", 0.92);
                Add("WidthPercent", 30);
                Add("Placement", 1); // anchored profile defaults to the right, like the reference
                Add("ShowVAH", 1);
                Add("ShowVAL", 1);
                Add("ShowPOC", 1);
                Add("ShowDevelopingPOC", 0);
                Add("ShowDevelopingVA", 0);
                Add("ShowHistogramBox", 0);
                Add("Rows", 24);
                Add("ShowValueArea", 1);
                Add("UpDownVolume", 1);
                break;
            case "bars-pattern":
                Add("Mirror", 0);
                Add("Flip", 0);
                Add("Mode", 4); // TradingView reference: connected close-price pattern by default
                break;
            case "ghost-feed":
                Add("Opacity", 0.38);
                Add("Mode", 0);
                break;
            case "cyclic-lines":
                Add("Count", 64);
                break;
            case "time-cycles":
                Add("Count", 32);
                break;
            case "sine-line":
                Add("Cycles", 2);
                break;
            case "sector":
                Add("ShowAngle", 1);
                Add("ShowRadius", 1);
                break;
        }

        return values;
    }

    public static IReadOnlyList<DrawingLevel> LevelsForTool(string? toolId)
    {
        string id = toolId ?? string.Empty;
        if (id is "parallel-channel" or "flat-top-bottom" or "disjoint-channel")
        {
            // TradingView-style channel level stack. 0 and 1 are the two
            // principal rails; 0.5 is the default dashed middle level.
            return new[]
            {
                new DrawingLevel(-0.25, "-0.25", false, "#9BAEF9", 1.0),
                new DrawingLevel(0, "0", true, "#2962FF", 1.25),
                new DrawingLevel(0.25, "0.25", false, "#9BAEF9", 1.0),
                new DrawingLevel(0.5, "0.5", true, "#2962FF", 1.0, DrawingLineStyle.Dashed),
                new DrawingLevel(0.75, "0.75", false, "#9BAEF9", 1.0),
                new DrawingLevel(1, "1", true, "#2962FF", 1.25),
                new DrawingLevel(1.25, "1.25", false, "#9BAEF9", 1.0)
            };
        }

        if (id == "regression-trend")
        {
            return new[]
            {
                new DrawingLevel(0, "Base", true, "#F23645", 1.0, DrawingLineStyle.Dashed),
                new DrawingLevel(1, "Upper", true, "#2962FF", 1.25, FillColor: "#2962FF", FillOpacity: 0.14),
                new DrawingLevel(-1, "Lower", true, "#2962FF", 1.25, FillColor: "#F23645", FillOpacity: 0.14)
            };
        }

        if (id == "anchored-vwap")
        {
            return new[]
            {
                new DrawingLevel(1, "Band #1", true, "#6A9F58", 1.0, FillColor: "#6A9F58", FillOpacity: 0.92),
                new DrawingLevel(2, "Band #2", false, "#26A69A", 1.0, FillColor: "#26A69A", FillOpacity: 0.90),
                new DrawingLevel(3, "Band #3", false, "#FF9800", 1.0, FillColor: "#FF9800", FillOpacity: 0.88)
            };
        }

        if (id is "long-position" or "short-position")
        {
            return new[]
            {
                // Target and Stop are independent style roles, matching the reference
                // Position tools.  Old workspaces using Profit/Loss still work because
                // the renderer falls back to the original level ordering.
                new DrawingLevel(1, "Target", true, "#089981", 1.2, FillColor: "#089981", FillOpacity: 0.24),
                new DrawingLevel(-1, "Stop", true, "#F23645", 1.2, FillColor: "#F23645", FillOpacity: 0.24),
                new DrawingLevel(0, "Entry", true, "#7C4DFF", 1.1)
            };
        }

        if (id is "fixed-volume-profile" or "anchored-volume-profile")
        {
            // TradingView FRVP/Anchored Volume Profile style roles. FillOpacity is
            // deliberately stored per role so colour and transparency are edited independently.
            return new[]
            {
                new DrawingLevel(1, "Up Volume", true, "#6B6CCB", 1.0, FillColor: "#6B6CCB", FillOpacity: 0.72),
                new DrawingLevel(-1, "Down Volume", true, "#D85A78", 1.0, FillColor: "#D85A78", FillOpacity: 0.72),
                new DrawingLevel(0.7, "Value Area Up", true, "#22B8A7", 1.0, FillColor: "#22B8A7", FillOpacity: 0.88),
                new DrawingLevel(-0.7, "Value Area Down", true, "#D84B91", 1.0, FillColor: "#D84B91", FillOpacity: 0.88),
                new DrawingLevel(2, "VAH", true, "#787B86", 1.25, FillColor: "#787B86", FillOpacity: 1.0),
                new DrawingLevel(-2, "VAL", true, "#787B86", 1.25, FillColor: "#787B86", FillOpacity: 1.0),
                new DrawingLevel(0, "POC", true, "#787B86", 1.6, FillColor: "#787B86", FillOpacity: 1.0),
                new DrawingLevel(3, "Developing POC", false, "#8B8B8B", 1.0, FillColor: "#8B8B8B", FillOpacity: 0.85),
                new DrawingLevel(4, "Developing VA", false, "#26A69A", 1.0, FillColor: "#26A69A", FillOpacity: 0.70),
                new DrawingLevel(5, "Histogram Box", false, "#787B86", 1.0, FillColor: "#787B86", FillOpacity: 0.30)
            };
        }

        if (id.Contains("pitchfork", StringComparison.OrdinalIgnoreCase))
        {
            // TradingView reference stack (all four pitchfork variants):
            // Median is handled by DrawingStyle.LineColor (#F23645). The
            // additional levels deliberately keep separate colours; only 0.5
            // and 1 are enabled by default in the supplied reference video.
            return new[]
            {
                new DrawingLevel(0.25,  "0.25",  false, "#FF9800", 1.0, FillColor: "#FF9800", FillOpacity: 0.10),
                new DrawingLevel(0.382, "0.382", false, "#4CAF50", 1.0, FillColor: "#4CAF50", FillOpacity: 0.10),
                new DrawingLevel(0.5,   "0.5",   true,  "#089981", 1.0, FillColor: "#089981", FillOpacity: 0.14),
                new DrawingLevel(0.618, "0.618", false, "#26A69A", 1.0, FillColor: "#26A69A", FillOpacity: 0.10),
                new DrawingLevel(0.75,  "0.75",  false, "#26C6DA", 1.0, FillColor: "#26C6DA", FillOpacity: 0.10),
                new DrawingLevel(1.0,   "1",     true,  "#2962FF", 1.25, FillColor: "#2962FF", FillOpacity: 0.14),
                new DrawingLevel(1.5,   "1.5",   false, "#7E57C2", 1.0, FillColor: "#7E57C2", FillOpacity: 0.10),
                new DrawingLevel(1.75,  "1.75",  false, "#AB47BC", 1.0, FillColor: "#AB47BC", FillOpacity: 0.10),
                new DrawingLevel(2.0,   "2",     false, "#EC407A", 1.0, FillColor: "#EC407A", FillOpacity: 0.10)
            };
        }

        if (id is "fib-retracement" or "fib-channel")
        {
            // TradingView reference palette: warm low ratios transition through
            // green/cyan into blue/violet.  These colours are intentionally
            // stored per level instead of flattened to one generic line colour.
            return new[]
            {
                new DrawingLevel(0, "0", true, "#F23645", 1.0, FillColor: "#F23645", FillOpacity: 0.10),
                new DrawingLevel(0.236, "0.236", true, "#FF9800", 1.0, FillColor: "#FF9800", FillOpacity: 0.10),
                new DrawingLevel(0.382, "0.382", true, "#FDD835", 1.0, FillColor: "#FDD835", FillOpacity: 0.10),
                new DrawingLevel(0.5, "0.5", true, "#4CAF50", 1.0, FillColor: "#4CAF50", FillOpacity: 0.10),
                new DrawingLevel(0.618, "0.618", true, "#089981", 1.0, FillColor: "#089981", FillOpacity: 0.10),
                new DrawingLevel(0.786, "0.786", true, "#00BCD4", 1.0, FillColor: "#00BCD4", FillOpacity: 0.10),
                new DrawingLevel(1, "1", true, "#2962FF", 1.15, FillColor: "#2962FF", FillOpacity: 0.10),
                new DrawingLevel(1.272, "1.272", false, "#7E57C2", 1.0, FillColor: "#7E57C2", FillOpacity: 0.09),
                new DrawingLevel(1.618, "1.618", true, "#9C27B0", 1.0, FillColor: "#9C27B0", FillOpacity: 0.09)
            };
        }

        if (id == "trend-fib-extension")
        {
            return new[]
            {
                new DrawingLevel(0, "0", true, "#787B86", 1.0),
                new DrawingLevel(0.236, "0.236", true, "#F23645", 1.0, FillColor: "#F23645", FillOpacity: 0.09),
                new DrawingLevel(0.382, "0.382", true, "#FF9800", 1.0, FillColor: "#FF9800", FillOpacity: 0.09),
                new DrawingLevel(0.5, "0.5", true, "#FDD835", 1.0, FillColor: "#FDD835", FillOpacity: 0.09),
                new DrawingLevel(0.618, "0.618", true, "#4CAF50", 1.0, FillColor: "#4CAF50", FillOpacity: 0.09),
                new DrawingLevel(0.786, "0.786", true, "#089981", 1.0, FillColor: "#089981", FillOpacity: 0.09),
                new DrawingLevel(1, "1", true, "#2962FF", 1.15, FillColor: "#2962FF", FillOpacity: 0.09),
                new DrawingLevel(1.272, "1.272", true, "#5B8FF9", 1.0, FillColor: "#5B8FF9", FillOpacity: 0.08),
                new DrawingLevel(1.618, "1.618", true, "#7E57C2", 1.0, FillColor: "#7E57C2", FillOpacity: 0.08),
                new DrawingLevel(2, "2", false, "#9C27B0", 1.0, FillColor: "#9C27B0", FillOpacity: 0.08),
                new DrawingLevel(2.618, "2.618", false, "#E040FB", 1.0, FillColor: "#E040FB", FillOpacity: 0.08),
                new DrawingLevel(3.618, "3.618", false, "#EC407A", 1.0, FillColor: "#EC407A", FillOpacity: 0.08),
                new DrawingLevel(4.236, "4.236", false, "#F23645", 1.0, FillColor: "#F23645", FillOpacity: 0.08)
            };
        }

        if (id is "fib-time-zone" or "trend-fib-time")
        {
            return new[]
            {
                new DrawingLevel(0, "0", true, "#787B86"),
                new DrawingLevel(1, "1", true, "#F23645"),
                new DrawingLevel(2, "2", true, "#FF9800"),
                new DrawingLevel(3, "3", true, "#FDD835"),
                new DrawingLevel(5, "5", true, "#4CAF50"),
                new DrawingLevel(8, "8", true, "#089981"),
                new DrawingLevel(13, "13", true, "#00BCD4"),
                new DrawingLevel(21, "21", true, "#2962FF"),
                new DrawingLevel(34, "34", true, "#7E57C2"),
                new DrawingLevel(55, "55", false, "#9C27B0"),
                new DrawingLevel(89, "89", false, "#EC407A")
            };
        }

        if (id is "fib-speed-fan" or "fib-speed-arcs" or "fib-wedge" or "pitchfan")
        {
            return new[]
            {
                new DrawingLevel(0.236, "0.236", true, "#F23645", FillColor: "#F23645", FillOpacity: 0.08),
                new DrawingLevel(0.382, "0.382", true, "#FF9800", FillColor: "#FF9800", FillOpacity: 0.08),
                new DrawingLevel(0.5, "0.5", true, "#FDD835", FillColor: "#FDD835", FillOpacity: 0.08),
                new DrawingLevel(0.618, "0.618", true, "#089981", FillColor: "#089981", FillOpacity: 0.08),
                new DrawingLevel(0.786, "0.786", true, "#00BCD4", FillColor: "#00BCD4", FillOpacity: 0.08),
                new DrawingLevel(1, "1", true, "#2962FF", 1.15, FillColor: "#2962FF", FillOpacity: 0.08)
            };
        }

        if (id == "gann-box")
        {
            // TradingView Gann Box reference: ratio grid only.  This is NOT a
            // Gann Square and therefore has no square fan/arcs in its default
            // construction.  Ratios are shown on all four sides.
            return new[]
            {
                new DrawingLevel(0,     "0",     true, "#787B86", 1.0, FillColor: "#FF9800", FillOpacity: 0.10),
                new DrawingLevel(0.25,  "0.25",  true, "#F57C00", 1.0, FillColor: "#F57C00", FillOpacity: 0.10),
                new DrawingLevel(0.382, "0.382", true, "#26A69A", 1.0, FillColor: "#26A69A", FillOpacity: 0.10),
                new DrawingLevel(0.5,   "0.5",   true, "#558B2F", 1.0, FillColor: "#9CCC65", FillOpacity: 0.10),
                new DrawingLevel(0.618, "0.618", true, "#388E3C", 1.0, FillColor: "#66BB6A", FillOpacity: 0.10),
                new DrawingLevel(0.75,  "0.75",  true, "#5C6BC0", 1.0, FillColor: "#42A5F5", FillOpacity: 0.10),
                new DrawingLevel(1,     "1",     true, "#424242", 1.1, FillColor: "#B0BEC5", FillOpacity: 0.06)
            };
        }

        if (id is "gann-square" or "gann-square-fixed")
        {
            // Reference Gann Square grid: five equal subdivisions.  Arc and fan
            // defaults are separate geometry and are rendered independently.
            return new[]
            {
                new DrawingLevel(0, "0", true, "#808080", 1.0, FillColor: "#808080", FillOpacity: 0.08),
                new DrawingLevel(0.2, "0.2", true, "#FF9800", 1.0, FillColor: "#FF9800", FillOpacity: 0.08),
                new DrawingLevel(0.4, "0.4", true, "#00BCD4", 1.0, FillColor: "#00BCD4", FillOpacity: 0.08),
                new DrawingLevel(0.6, "0.6", true, "#4CAF50", 1.0, FillColor: "#4CAF50", FillOpacity: 0.08),
                new DrawingLevel(0.8, "0.8", true, "#089981", 1.0, FillColor: "#089981", FillOpacity: 0.08),
                new DrawingLevel(1, "1", true, "#808080", 1.1, FillColor: "#808080", FillOpacity: 0.08)
            };
        }

        if (id == "fib-circles")
        {
            return new[]
            {
                new DrawingLevel(0.236, "0.236", true, "#F05261", FillColor: "#F05261"),
                new DrawingLevel(0.382, "0.382", true, "#FF7A59", FillColor: "#FF7A59"),
                new DrawingLevel(0.5, "0.5", true, "#F5B544", FillColor: "#F5B544"),
                new DrawingLevel(0.618, "0.618", true, "#A3CC52", FillColor: "#A3CC52"),
                new DrawingLevel(0.786, "0.786", true, "#22C97A", FillColor: "#22C97A"),
                new DrawingLevel(1, "1", true, "#32B6D8", FillColor: "#32B6D8"),
                new DrawingLevel(1.272, "1.272", true, "#2F80ED", FillColor: "#2F80ED"),
                new DrawingLevel(1.618, "1.618", true, "#846EF6", FillColor: "#846EF6"),
                new DrawingLevel(2, "2", false, "#B36BEA", FillColor: "#B36BEA"),
                new DrawingLevel(2.618, "2.618", false, "#B36BEA", FillColor: "#B36BEA"),
                new DrawingLevel(3.618, "3.618", false, "#B36BEA", FillColor: "#B36BEA")
            };
        }

        if (id == "gann-fan")
        {
            // Reference fan uses a separate visible colour for every ratio.
            return new[]
            {
                new DrawingLevel(0.125, "1/8", true, "#F23645", 1.0, FillColor: "#F23645"),
                new DrawingLevel(0.25,  "1/4", true, "#FF9800", 1.0, FillColor: "#FF9800"),
                new DrawingLevel(0.3333333333, "1/3", true, "#FDD835", 1.0, FillColor: "#FDD835"),
                new DrawingLevel(0.5,   "1/2", true, "#4CAF50", 1.0, FillColor: "#4CAF50"),
                new DrawingLevel(1,     "1/1", true, "#089981", 1.6, FillColor: "#089981"),
                new DrawingLevel(2,     "2/1", true, "#00BCD4", 1.0, FillColor: "#00BCD4"),
                new DrawingLevel(3,     "3/1", true, "#2962FF", 1.0, FillColor: "#2962FF"),
                new DrawingLevel(4,     "4/1", true, "#7E57C2", 1.0, FillColor: "#7E57C2"),
                new DrawingLevel(8,     "8/1", true, "#EC407A", 1.0, FillColor: "#EC407A")
            };
        }

        return DrawingToolCatalog.DefaultFibonacciLevels();
    }


    public static string OptionDescription(string name) => name switch
    {
        "Source" => "Price source: 0 Open, 1 High, 2 Low, 3 Close, 4 HL2, 5 HLC3, 6 OHLC4",
        "MiddlePoint" => "1 shows the midpoint handle; 0 hides it",
        "AngleLabel" => "1 displays the line angle",
        "AlwaysShowStats" => "1 keeps statistics visible when the drawing is not selected",
        "UpperDeviation" => "Regression upper deviation multiplier",
        "LowerDeviation" => "Regression lower deviation multiplier",
        "PearsonsR" => "1 shows Pearson's R on Regression Trend",
        "Band1" or "Band2" or "Band3" => "1 enables this Anchored VWAP deviation band",
        "Band1Multiplier" or "Band2Multiplier" or "Band3Multiplier" => "Standard-deviation multiplier",
        "Reverse" => "1 reverses the tool direction",
        "UseOneColor" => "1 uses the main line colour for every level",
        "LabelsPercent" => "1 displays Fibonacci ratios as percentages",
        "LabelsLeft" => "Legacy left-label switch",
        "ShowLevelReadings" => "1 shows ratio/level readings (for example 0.5, 0.618, 1/4)",
        "ShowLevelPrices" => "1 shows price values for enabled levels where the geometry has a price coordinate",
        "LabelsOutside" => "1 places readings outside the tool boundary",
        "LabelHorizontal" => "Reading position: -1 left, 0 centre, 1 right",
        "LabelVertical" => "Reading position: -1 above line, 0 on line, 1 below line",
        "Bands" => "1 shows coloured level background bands",
        "AccountSize" => "Account value used by Long/Short Position calculations",
        "LotSize" => "Contract/lot size used by position calculations",
        "Risk" => "Risk amount or percentage depending on RiskIsPercent",
        "RiskIsPercent" => "1 = Risk is percent of account; 0 = absolute money",
        "Leverage" => "Leverage used to cap calculated quantity",
        "PointValue" => "Money value per one price unit per lot",
        "QtyPrecision" => "Number of decimal places shown for quantity",
        "CompactStats" => "1 shows compact position statistics",
        "StatsMode" => "Position stats: 0 full, 1 compact, 2 hidden",
        "ShowStartLabel" => "1 shows the forecast start price/time label",
        "ShowEndLabel" => "1 shows the forecast result label",
        "ShowStatus" => "1 shows the forecast status bar",
        "Success" => "Legacy compatibility only; Position Forecast success is now determined by whether market price has reached point B",
        "ShowPriceChange" => "1 shows exact price movement in the measurement label",
        "ShowPercent" => "1 shows percentage movement in the measurement label",
        "ShowPoints" => "1 shows point/pip movement in the measurement label",
        "ShowBars" => "1 shows number of bars in the measured date range",
        "ShowDuration" => "1 shows elapsed time in the measured date range",
        "ShowVolume" => "1 shows real volume when available, otherwise tick volume",
        "Rows" => "Legacy volume-profile row count",
        "RowsLayout" => "Rows layout: 0 = Number of Rows, 1 = Price/Tick step",
        "RowSize" => "Row size / number of rows (TradingView default reference: 24)",
        "VolumeMode" => "Volume: 0 = Up/Down, 1 = Total",
        "ValueAreaPercent" => "Value Area Volume percentage",
        "ExtendRight" => "1 extends profile guide lines to the chart's right edge",
        "ShowProfile" => "1 shows the volume histogram",
        "ShowValues" => "1 shows volume values beside sufficiently wide profile rows",
        "ValuesOpacity" => "Transparency for volume-profile value text: 0 transparent, 1 solid",
        "ShowVAH" => "1 displays Value Area High",
        "ShowVAL" => "1 displays Value Area Low",
        "ShowPOC" => "1 displays Point of Control",
        "ShowDevelopingPOC" => "1 displays the developing Point of Control path",
        "ShowDevelopingVA" => "1 displays developing Value Area High/Low paths",
        "ShowHistogramBox" => "1 draws the histogram boundary box",
        "ShowValueArea" => "Legacy switch for value-area highlighting",
        "UpDownVolume" => "Legacy switch for Up/Down volume mode",
        "WidthPercent" => "Volume-profile width as percent of the selected range box",
        "Placement" => "Profile placement: -1 = Left, +1 = Right",
        "Mirror" => "1 mirrors Bars Pattern candle order",
        "Flip" => "1 vertically flips Bars Pattern",
        "Mode" => "Bars Pattern source: 0/4 Close, 1 Open, 2 High, 3 Low, 5 HL2, 6 HLC3",
        "Count" => "Number of repeated cycle guides",
        "Cycles" => "Number of sine-wave cycles between the two anchors",
        "ShowAngle" => "1 displays the sector angle",
        "ShowRadius" => "1 displays the sector radius",
        _ => "Tool-specific numeric option; 1 = on and 0 = off for toggle options"
    };

    public static IReadOnlyDictionary<string, double> MergeOptions(
        string? toolId,
        IReadOnlyDictionary<string, double>? current)
    {
        var merged = new Dictionary<string, double>(NumericOptions(toolId), StringComparer.OrdinalIgnoreCase);
        if (current is not null)
        {
            foreach ((string key, double value) in current)
                merged[key] = value;
        }
        return merged;
    }
}
