namespace TickLab.Core.Drawing;

public static class DrawingToolCatalog
{
    private static readonly IReadOnlyList<DrawingToolDefinition> Definitions = Build();
    private static readonly IReadOnlyDictionary<string, DrawingToolDefinition> ById =
        Definitions.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DrawingToolDefinition> All => Definitions;

    public static DrawingToolDefinition? Find(string? id) =>
        !string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id, out DrawingToolDefinition? value)
            ? value
            : null;

    public static IReadOnlyList<DrawingToolDefinition> InCategory(DrawingToolCategory category) =>
        Definitions
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.Category == category && IsVisibleInReferencePalette(pair.item))
            .OrderBy(pair => ReferencePaletteOrder(pair.item))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.item)
            .ToArray();

    private static bool IsVisibleInReferencePalette(DrawingToolDefinition tool) => tool.Id switch
    {
        // Kept in the model for old workspaces/templates, but the audited TradingView palette
        // exposes Brush/Highlighter and only the up/down arrow marks shown in the recording.
        "pen" or "arrow-mark-left" or "arrow-mark-right" or "anchored-note" or "selection" => false,
        _ => true
    };

    private static int ReferencePaletteOrder(DrawingToolDefinition tool) => tool.Category switch
    {
        DrawingToolCategory.TrendLine => tool.Id switch
        {
            "parallel-channel" or "regression-trend" or "flat-top-bottom" or "disjoint-channel" => 100,
            "pitchfork" or "schiff-pitchfork" or "modified-schiff-pitchfork" or "inside-pitchfork" => 200,
            _ => 0
        },
        DrawingToolCategory.FibonacciGann => tool.Id switch
        {
            "fib-retracement" => 0,
            "trend-fib-extension" => 1,
            "fib-channel" => 2,
            "fib-time-zone" => 3,
            "fib-speed-fan" => 4,
            "trend-fib-time" => 5,
            "fib-circles" => 6,
            "fib-spiral" => 7,
            "fib-speed-arcs" => 8,
            "fib-wedge" => 9,
            "pitchfan" => 10,
            "gann-box" => 100,
            "gann-square-fixed" => 101,
            "gann-square" => 102,
            "gann-fan" => 103,
            _ => 50
        },
        DrawingToolCategory.Geometry => tool.Id switch
        {
            "brush" => 0,
            "highlighter" => 1,
            "arrow-marker" => 100,
            "arrow" => 101,
            "arrow-mark-up" => 102,
            "arrow-mark-down" => 103,
            "rectangle" => 200,
            "rotated-rectangle" => 201,
            "path" => 202,
            "circle" => 203,
            "ellipse" => 204,
            "polyline" => 205,
            "triangle" => 206,
            "arc" => 207,
            "curve" => 208,
            "double-curve" => 209,
            _ => 250
        },
        DrawingToolCategory.Pattern => tool.Id switch
        {
            "elliott-impulse" or "elliott-triangle" or "elliott-triple-combo" or "elliott-correction" or "elliott-double-combo" => 100,
            "cyclic-lines" or "time-cycles" or "sine-line" => 200,
            _ => 0
        },
        DrawingToolCategory.PredictionMeasurement => tool.Id switch
        {
            "anchored-vwap" or "fixed-volume-profile" or "anchored-volume-profile" => 100,
            "price-range" or "date-range" or "date-price-range" => 200,
            _ => 0
        },
        DrawingToolCategory.Annotation => tool.Id is "image" or "post" or "idea" ? 100 : 0,
        _ => 0
    };

    public static string CategoryName(DrawingToolCategory category) => category switch
    {
        DrawingToolCategory.Cursor => "Cursor tools",
        DrawingToolCategory.TrendLine => "Trend and line tools",
        DrawingToolCategory.FibonacciGann => "Fibonacci and Gann tools",
        DrawingToolCategory.Geometry => "Geometric shapes",
        DrawingToolCategory.Annotation => "Text and annotations",
        DrawingToolCategory.Pattern => "Pattern tools",
        DrawingToolCategory.PredictionMeasurement => "Prediction and measurement",
        DrawingToolCategory.IconsMedia => "Icons, stickers and emoji",
        _ => category.ToString()
    };

    public static string CategoryIcon(DrawingToolCategory category) => category switch
    {
        DrawingToolCategory.Cursor => "＋",
        DrawingToolCategory.TrendLine => "╱",
        DrawingToolCategory.FibonacciGann => "F",
        DrawingToolCategory.Geometry => "▭",
        DrawingToolCategory.Annotation => "T",
        DrawingToolCategory.Pattern => "W",
        DrawingToolCategory.PredictionMeasurement => "↕",
        DrawingToolCategory.IconsMedia => "☺",
        _ => "•"
    };

    public static IReadOnlyList<DrawingLevel> DefaultFibonacciLevels() =>
        new[]
        {
            new DrawingLevel(0, "0", Color: "#F05261", FillColor: "#6C2632", FillOpacity: 0.18),
            new DrawingLevel(0.236, "0.236", Color: "#FF7A59", FillColor: "#6F3428", FillOpacity: 0.17),
            new DrawingLevel(0.382, "0.382", Color: "#F5B544", FillColor: "#685020", FillOpacity: 0.17),
            new DrawingLevel(0.5, "0.5", Color: "#A3CC52", FillColor: "#3D5726", FillOpacity: 0.16),
            new DrawingLevel(0.618, "0.618", Color: "#22C97A", FillColor: "#15543A", FillOpacity: 0.16),
            new DrawingLevel(0.786, "0.786", Color: "#32B6D8", FillColor: "#174A5B", FillOpacity: 0.16),
            new DrawingLevel(1, "1", Color: "#2F80ED", FillColor: "#173B68", FillOpacity: 0.16),
            new DrawingLevel(1.272, "1.272", Color: "#846EF6", FillColor: "#352C65", FillOpacity: 0.16),
            new DrawingLevel(1.618, "1.618", Color: "#B36BEA", FillColor: "#47285F", FillOpacity: 0.16)
        };

    public static DrawingStyle DefaultStyle(DrawingToolDefinition tool)
    {
        if (tool.Geometry == DrawingGeometryKind.Highlighter)
        {
            return new DrawingStyle
            {
                LineColor = "#F5B544",
                FillColor = "#F5B544",
                LineWidth = 18,
                Opacity = 0.30,
                FillOpacity = 0.16
            };
        }

        if (tool.Geometry == DrawingGeometryKind.Brush)
        {
            return new DrawingStyle
            {
                LineColor = "#46A5FF",
                FillColor = "#46A5FF",
                LineWidth = 2.5,
                Opacity = 0.96,
                FillOpacity = 0
            };
        }

        if (tool.Id == "arrow-marker")
        {
            // TradingView-style two-point marker: a solid, scalable arrow whose
            // direction and size are defined by the two construction points.
            return new DrawingStyle
            {
                LineColor = "#2962FF",
                FillColor = "#2962FF",
                LineWidth = 1.5,
                Opacity = 1.0,
                FillOpacity = 1.0
            };
        }

        if (tool.Id == "arc")
        {
            return new DrawingStyle
            {
                LineColor = "#D84A78",
                FillColor = "#D84A78",
                LineWidth = 2.0,
                Opacity = 1.0,
                FillOpacity = 0.18
            };
        }

        if (tool.Category == DrawingToolCategory.Annotation && tool.Id is
            "text" or "note" or "price-note" or "pin" or "table" or "callout" or "comment" or "price-label" or "signpost" or "flag-mark")
        {
            return tool.Id switch
            {
                "text" => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#FFFFFF", TextColor = "#1F2937", BackgroundColor = "#FFFFFF",
                    LineWidth = 1.0, Opacity = 1.0, FillOpacity = 0.0, FontSize = 14
                },
                "note" => new DrawingStyle
                {
                    LineColor = "#64748B", FillColor = "#FFFFFF", TextColor = "#1F2937", BackgroundColor = "#FFFFFF",
                    LineWidth = 1.2, Opacity = 1.0, FillOpacity = 0.94, FontSize = 14
                },
                "price-note" => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#2962FF", TextColor = "#FFFFFF", BackgroundColor = "#2962FF",
                    LineWidth = 1.2, Opacity = 1.0, FillOpacity = 1.0, FontSize = 13
                },
                "pin" => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#2962FF", TextColor = "#1F2937", BackgroundColor = "#FFFFFF",
                    LineWidth = 1.2, Opacity = 1.0, FillOpacity = 1.0, FontSize = 14
                },
                "table" => new DrawingStyle
                {
                    LineColor = "#94A3B8", FillColor = "#FFFFFF", TextColor = "#1F2937", BackgroundColor = "#FFFFFF",
                    LineWidth = 1.0, Opacity = 1.0, FillOpacity = 0.08, FontSize = 14
                },
                "callout" => new DrawingStyle
                {
                    LineColor = "#089981", FillColor = "#089981", TextColor = "#FFFFFF", BackgroundColor = "#089981",
                    LineWidth = 1.4, Opacity = 1.0, FillOpacity = 0.96, FontSize = 14
                },
                "comment" => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#2962FF", TextColor = "#FFFFFF", BackgroundColor = "#2962FF",
                    LineWidth = 1.0, Opacity = 1.0, FillOpacity = 1.0, FontSize = 16
                },
                "price-label" => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#2962FF", TextColor = "#FFFFFF", BackgroundColor = "#2962FF",
                    LineWidth = 1.0, Opacity = 1.0, FillOpacity = 1.0, FontSize = 14
                },
                "signpost" => new DrawingStyle
                {
                    LineColor = "#64748B", FillColor = "#FFFFFF", TextColor = "#1F2937", BackgroundColor = "#FFFFFF",
                    LineWidth = 1.2, Opacity = 1.0, FillOpacity = 0.94, FontSize = 14
                },
                _ => new DrawingStyle
                {
                    LineColor = "#2962FF", FillColor = "#2962FF", TextColor = "#FFFFFF", BackgroundColor = "#2962FF",
                    LineWidth = 1.0, Opacity = 1.0, FillOpacity = 1.0, FontSize = 14
                }
            };
        }

        if (tool.Geometry is DrawingGeometryKind.Text or DrawingGeometryKind.Note or DrawingGeometryKind.Callout)
        {
            return new DrawingStyle
            {
                LineColor = "#46A5FF",
                FillColor = "#0B1726",
                TextColor = "#F2F6FC",
                BackgroundColor = "#0B1726",
                LineWidth = 1.2,
                Opacity = 1,
                FillOpacity = 0.90,
                FontSize = 13
            };
        }

        if (tool.Geometry == DrawingGeometryKind.Image)
        {
            return new DrawingStyle
            {
                LineColor = "#94A3B8",
                FillColor = "#FFFFFF",
                LineWidth = 1.0,
                Opacity = 1.0,
                FillOpacity = 0
            };
        }

        if (tool.Geometry == DrawingGeometryKind.Position)
        {
            if (tool.Id == "position-forecast")
            {
                return new DrawingStyle
                {
                    LineColor = "#2962FF",
                    FillColor = "#2962FF",
                    TextColor = "#FFFFFF",
                    BackgroundColor = "#2962FF",
                    LineWidth = 2.0,
                    Opacity = 1.0,
                    FillOpacity = 0.08,
                    FontSize = 12
                };
            }

            return new DrawingStyle
            {
                LineColor = "#7C4DFF",
                FillColor = "#2962FF",
                TextColor = "#FFFFFF",
                BackgroundColor = "#4338CA",
                LineWidth = 1.25,
                Opacity = 1.0,
                FillOpacity = 0.18,
                ShowPriceLabels = true,
                FontSize = 12
            };
        }

        if (tool.Geometry == DrawingGeometryKind.Range)
        {
            return new DrawingStyle
            {
                LineColor = "#2962FF",
                FillColor = "#2962FF",
                TextColor = "#FFFFFF",
                BackgroundColor = "#374151",
                LineWidth = 1.5,
                Opacity = 1.0,
                FillOpacity = 0.10,
                ShowStatistics = true,
                FontSize = 12
            };
        }


        if (tool.Geometry == DrawingGeometryKind.AnchoredVwap)
        {
            return new DrawingStyle
            {
                LineColor = "#2962FF",
                FillColor = "#2962FF",
                LineWidth = 1.6,
                Opacity = 1.0,
                FillOpacity = 0.0,
                ShowPriceLabels = false
            };
        }

        if (tool.Geometry == DrawingGeometryKind.VolumeProfile)
        {
            return new DrawingStyle
            {
                LineColor = "#787B86",
                FillColor = "#26A69A",
                TextColor = "#787B86",
                LineWidth = 1.0,
                Opacity = 1.0,
                FillOpacity = 0.72
            };
        }

        if (tool.Geometry == DrawingGeometryKind.BarsPattern)
        {
            return new DrawingStyle
            {
                LineColor = "#2962FF",
                FillColor = "#2962FF",
                LineWidth = 2.0,
                Opacity = 1.0,
                FillOpacity = 0.0
            };
        }

        if (tool.Geometry == DrawingGeometryKind.GhostFeed)
        {
            return new DrawingStyle
            {
                LineColor = "#2962FF",
                FillColor = "#2962FF",
                LineWidth = 1.2,
                Opacity = 1.0,
                FillOpacity = 0.0
            };
        }

        if (tool.Category == DrawingToolCategory.TrendLine)
        {
            bool pitchfork = tool.Geometry == DrawingGeometryKind.Pitchfork;
            bool filled = tool.Geometry is DrawingGeometryKind.Channel or DrawingGeometryKind.Regression or DrawingGeometryKind.Pitchfork;
            return new DrawingStyle
            {
                // TradingView pitchforks use a red median while the additional
                // lines keep their own per-level palette. Other Folder-1 tools
                // retain the standard TradingView blue.
                LineColor = pitchfork ? "#F23645" : "#2962FF",
                FillColor = pitchfork ? "#089981" : "#2962FF",
                LineWidth = pitchfork ? 2.0 : 1.25,
                Opacity = 1.0,
                FillOpacity = filled ? 0.16 : 0.0,
                ShowPriceLabels = tool.Id is "horizontal-line" or "horizontal-ray"
            };
        }

        if (tool.Category == DrawingToolCategory.Pattern && (tool.Id is
            "xabcd-pattern" or "cypher-pattern" or "abcd-pattern" or "triangle-pattern" or "three-drives-pattern" or "head-shoulders"))
        {
            // Folder 3 / Patterns: TradingView uses a distinct visual identity per
            // pattern family. Labels/ratio tags are rendered from LineColor so any
            // user color edit automatically recolors every A/B/C/D/1/2/... marking.
            string line = tool.Id switch
            {
                "abcd-pattern" => "#8E44D7",
                "triangle-pattern" => "#5B5BD6",
                "three-drives-pattern" => "#4C3F91",
                "head-shoulders" => "#1B5E20",
                _ => "#2962FF"
            };
            bool shaded = tool.Id is "xabcd-pattern" or "cypher-pattern" or "triangle-pattern";
            return new DrawingStyle
            {
                LineColor = line,
                FillColor = line,
                TextColor = line,
                LineWidth = 1.7,
                Opacity = 1.0,
                FillOpacity = shaded ? 0.14 : 0.0,
                FontSize = 11
            };
        }

        if (tool.Category == DrawingToolCategory.FibonacciGann)
        {
            bool filled = tool.SupportsFill || tool.Geometry is DrawingGeometryKind.GannBox;
            return new DrawingStyle
            {
                LineColor = "#787B86",
                FillColor = "#2962FF",
                LineWidth = 1.0,
                Opacity = 1.0,
                FillOpacity = filled ? 0.12 : 0.0,
                ShowPriceLabels = tool.Id is "fib-retracement" or "trend-fib-extension"
            };
        }

        if (tool.SupportsLevels)
        {
            return new DrawingStyle
            {
                LineColor = "#A8D5FF",
                FillColor = "#2F80ED",
                LineWidth = 1.2,
                Opacity = 0.96,
                FillOpacity = 0.16,
                ShowPriceLabels = true
            };
        }

        if (tool.SupportsFill)
        {
            return new DrawingStyle
            {
                LineColor = "#46A5FF",
                FillColor = "#2F80ED",
                LineWidth = 1.6,
                Opacity = 0.96,
                FillOpacity = 0.12
            };
        }

        return new DrawingStyle
        {
            LineColor = "#46A5FF",
            FillColor = "#2F80ED",
            LineWidth = 1.7,
            Opacity = 0.98,
            FillOpacity = 0.12
        };
    }

    private static IReadOnlyList<DrawingToolDefinition> Build()
    {
        var list = new List<DrawingToolDefinition>();
        void Add(string id, string name, DrawingToolCategory category, string icon,
            DrawingGeometryKind geometry, int min, int max, bool variable = false,
            bool text = false, bool fill = false, bool levels = false, bool cursor = false) =>
            list.Add(new DrawingToolDefinition(id, name, category, icon, geometry,
                min, max, variable, text, fill, levels, cursor));

        // Cursor modes and utility tools.
        Add("cursor-crosshair", "Cross", DrawingToolCategory.Cursor, "＋", DrawingGeometryKind.Cursor, 0, 0, cursor: true);
        Add("cursor-dot", "Dot", DrawingToolCategory.Cursor, "•", DrawingGeometryKind.Cursor, 0, 0, cursor: true);
        Add("cursor-arrow", "Arrow", DrawingToolCategory.Cursor, "➤", DrawingGeometryKind.Cursor, 0, 0, cursor: true);
        Add("cursor-demo", "Demonstration", DrawingToolCategory.Cursor, "◎", DrawingGeometryKind.Cursor, 0, 0, cursor: true);
        Add("cursor-magic", "Magic", DrawingToolCategory.Cursor, "✦", DrawingGeometryKind.Cursor, 0, 0, cursor: true);
        Add("eraser", "Eraser", DrawingToolCategory.Cursor, "⌫", DrawingGeometryKind.Eraser, 0, 0, cursor: true);
        Add("selection", "Object Selection", DrawingToolCategory.Cursor, "↖", DrawingGeometryKind.Cursor, 0, 0, cursor: true);

        // Trend and line tools.
        Add("trend-line", "Trendline", DrawingToolCategory.TrendLine, "╱", DrawingGeometryKind.Line, 2, 2, text: true);
        Add("arrow", "Arrow", DrawingToolCategory.Geometry, "↗", DrawingGeometryKind.ArrowLine, 2, 2);
        Add("ray", "Ray", DrawingToolCategory.TrendLine, "↗", DrawingGeometryKind.Ray, 2, 2, text: true);
        Add("info-line", "Info line", DrawingToolCategory.TrendLine, "i", DrawingGeometryKind.Line, 2, 2, text: true);
        Add("extended-line", "Extended line", DrawingToolCategory.TrendLine, "⟷", DrawingGeometryKind.ExtendedLine, 2, 2, text: true);
        Add("trend-angle", "Trend angle", DrawingToolCategory.TrendLine, "∠", DrawingGeometryKind.Line, 2, 2, text: true);
        Add("horizontal-line", "Horizontal line", DrawingToolCategory.TrendLine, "━", DrawingGeometryKind.HorizontalLine, 1, 1, text: true);
        Add("horizontal-ray", "Horizontal ray", DrawingToolCategory.TrendLine, "→", DrawingGeometryKind.HorizontalRay, 1, 1, text: true);
        Add("vertical-line", "Vertical line", DrawingToolCategory.TrendLine, "│", DrawingGeometryKind.VerticalLine, 1, 1, text: true);
        Add("cross-line", "Crossline", DrawingToolCategory.TrendLine, "┼", DrawingGeometryKind.CrossLine, 1, 1, text: true);
        Add("parallel-channel", "Parallel channel", DrawingToolCategory.TrendLine, "∥", DrawingGeometryKind.Channel, 3, 3, text: true, fill: true, levels: true);
        Add("regression-trend", "Regression trend", DrawingToolCategory.TrendLine, "R", DrawingGeometryKind.Regression, 2, 2, fill: true, levels: true);
        Add("flat-top-bottom", "Flat top/bottom", DrawingToolCategory.TrendLine, "▱", DrawingGeometryKind.Channel, 3, 3, text: true, fill: true, levels: true);
        Add("disjoint-channel", "Disjoint channel", DrawingToolCategory.TrendLine, "≠", DrawingGeometryKind.Channel, 3, 3, text: true, fill: true, levels: true);

        // Fibonacci and Gann tools.
        Add("fib-retracement", "Fib retracement", DrawingToolCategory.FibonacciGann, "F", DrawingGeometryKind.Fibonacci, 2, 2, fill: true, levels: true);
        Add("trend-fib-extension", "Trend-based fib extension", DrawingToolCategory.FibonacciGann, "Fx", DrawingGeometryKind.FibonacciExtension, 3, 3, fill: true, levels: true);
        Add("pitchfork", "Pitchfork", DrawingToolCategory.TrendLine, "Ψ", DrawingGeometryKind.Pitchfork, 3, 3, levels: true);
        Add("schiff-pitchfork", "Schiff pitchfork", DrawingToolCategory.TrendLine, "Ψ", DrawingGeometryKind.Pitchfork, 3, 3, levels: true);
        Add("modified-schiff-pitchfork", "Modified Schiff pitchfork", DrawingToolCategory.TrendLine, "Ψ", DrawingGeometryKind.Pitchfork, 3, 3, levels: true);
        Add("inside-pitchfork", "Inside pitchfork", DrawingToolCategory.TrendLine, "Ψ", DrawingGeometryKind.Pitchfork, 3, 3, levels: true);
        Add("fib-channel", "Fib channel", DrawingToolCategory.FibonacciGann, "F∥", DrawingGeometryKind.FibonacciChannel, 3, 3, fill: true, levels: true);
        Add("fib-time-zone", "Fib time zone", DrawingToolCategory.FibonacciGann, "F│", DrawingGeometryKind.FibonacciTime, 2, 2, levels: true);
        Add("gann-box", "Gann box", DrawingToolCategory.FibonacciGann, "G", DrawingGeometryKind.GannBox, 2, 2, fill: true, levels: true);
        Add("gann-square-fixed", "Gann square fixed", DrawingToolCategory.FibonacciGann, "G□", DrawingGeometryKind.GannBox, 2, 2, fill: true, levels: true);
        Add("gann-square", "Gann square", DrawingToolCategory.FibonacciGann, "G□", DrawingGeometryKind.GannBox, 2, 2, fill: true, levels: true);
        Add("gann-fan", "Gann fan", DrawingToolCategory.FibonacciGann, "G⌁", DrawingGeometryKind.GannFan, 2, 2, levels: true);
        Add("fib-speed-fan", "Fib speed resistance fan", DrawingToolCategory.FibonacciGann, "F⌁", DrawingGeometryKind.FibonacciFan, 2, 2, levels: true);
        Add("trend-fib-time", "Trend-based fib time", DrawingToolCategory.FibonacciGann, "F↔", DrawingGeometryKind.FibonacciTime, 3, 3, levels: true);
        Add("fib-circles", "Fib circles", DrawingToolCategory.FibonacciGann, "F○", DrawingGeometryKind.FibonacciCircles, 2, 2, levels: true);
        Add("pitchfan", "Pitchfan", DrawingToolCategory.FibonacciGann, "Ψ⌁", DrawingGeometryKind.FibonacciFan, 3, 3, levels: true);
        Add("fib-spiral", "Fib spiral", DrawingToolCategory.FibonacciGann, "F◎", DrawingGeometryKind.FibonacciSpiral, 2, 2, levels: true);
        Add("fib-speed-arcs", "Fib speed resistance arcs", DrawingToolCategory.FibonacciGann, "F⌒", DrawingGeometryKind.FibonacciArcs, 2, 2, levels: true);
        Add("fib-wedge", "Fib wedge", DrawingToolCategory.FibonacciGann, "F◁", DrawingGeometryKind.FibonacciWedge, 3, 3, levels: true);

        // Geometric shapes.
        Add("pen", "Pen", DrawingToolCategory.Geometry, "✒", DrawingGeometryKind.Brush, 2, 8192, variable: true);
        Add("brush", "Brush", DrawingToolCategory.Geometry, "✎", DrawingGeometryKind.Brush, 2, 8192, variable: true);
        Add("highlighter", "Highlighter", DrawingToolCategory.Geometry, "▰", DrawingGeometryKind.Highlighter, 2, 4096, variable: true);
        Add("rectangle", "Rectangle", DrawingToolCategory.Geometry, "▭", DrawingGeometryKind.Rectangle, 2, 2, text: true, fill: true);
        Add("circle", "Circle", DrawingToolCategory.Geometry, "○", DrawingGeometryKind.Ellipse, 2, 2, text: true, fill: true);
        Add("ellipse", "Ellipse", DrawingToolCategory.Geometry, "⬭", DrawingGeometryKind.Ellipse, 2, 2, text: true, fill: true);
        Add("path", "Path", DrawingToolCategory.Geometry, "⌁", DrawingGeometryKind.Polyline, 2, 256, variable: true);
        Add("curve", "Curve", DrawingToolCategory.Geometry, "∿", DrawingGeometryKind.Curve, 3, 4, variable: true);
        Add("polyline", "Polyline", DrawingToolCategory.Geometry, "⌁", DrawingGeometryKind.Polyline, 2, 256, variable: true);
        Add("triangle", "Triangle", DrawingToolCategory.Geometry, "△", DrawingGeometryKind.Triangle, 3, 3, text: true, fill: true);
        Add("rotated-rectangle", "Rotated rectangle", DrawingToolCategory.Geometry, "◇", DrawingGeometryKind.RotatedRectangle, 3, 3, text: true, fill: true);
        Add("arc", "Arc", DrawingToolCategory.Geometry, "⌒", DrawingGeometryKind.Arc, 3, 3, fill: true);
        Add("double-curve", "Double Curve", DrawingToolCategory.Geometry, "≈", DrawingGeometryKind.DoubleCurve, 4, 4);

        // Annotation tools. Keep the menu order aligned with the audited toolbar.
        Add("text", "Text", DrawingToolCategory.Annotation, "T", DrawingGeometryKind.Text, 1, 1, text: true);
        Add("note", "Note", DrawingToolCategory.Annotation, "▣", DrawingGeometryKind.Note, 2, 2, text: true, fill: true);
        Add("price-note", "Price note", DrawingToolCategory.Annotation, "Pn", DrawingGeometryKind.PriceLabel, 2, 2, text: true, fill: true);
        Add("pin", "Pin", DrawingToolCategory.Annotation, "●", DrawingGeometryKind.Note, 1, 1, text: true, fill: true);
        Add("table", "Table", DrawingToolCategory.Annotation, "▦", DrawingGeometryKind.Rectangle, 2, 2, text: true, fill: true);
        Add("callout", "Callout", DrawingToolCategory.Annotation, "☁", DrawingGeometryKind.Callout, 2, 2, text: true, fill: true);
        Add("comment", "Comment", DrawingToolCategory.Annotation, "☵", DrawingGeometryKind.Note, 1, 1, text: true, fill: true);
        Add("price-label", "Price label", DrawingToolCategory.Annotation, "P", DrawingGeometryKind.PriceLabel, 1, 1, text: true, fill: true);
        Add("signpost", "Signpost", DrawingToolCategory.Annotation, "⚑", DrawingGeometryKind.Callout, 1, 1, text: true, fill: true);
        Add("anchored-note", "Anchored Note", DrawingToolCategory.Annotation, "▣", DrawingGeometryKind.Note, 1, 1, text: true, fill: true);
        Add("arrow-marker", "Arrow marker", DrawingToolCategory.Geometry, "➤", DrawingGeometryKind.ArrowMarker, 2, 2, fill: true);
        Add("arrow-mark-left", "Arrow Mark Left", DrawingToolCategory.Geometry, "←", DrawingGeometryKind.ArrowMarker, 1, 1);
        Add("arrow-mark-right", "Arrow Mark Right", DrawingToolCategory.Geometry, "→", DrawingGeometryKind.ArrowMarker, 1, 1);
        Add("arrow-mark-up", "Arrow mark up", DrawingToolCategory.Geometry, "↑", DrawingGeometryKind.ArrowMarker, 1, 1);
        Add("arrow-mark-down", "Arrow mark down", DrawingToolCategory.Geometry, "↓", DrawingGeometryKind.ArrowMarker, 1, 1);
        Add("flag-mark", "Flag mark", DrawingToolCategory.Annotation, "⚑", DrawingGeometryKind.Flag, 1, 1, text: true);
        Add("image", "Image", DrawingToolCategory.Annotation, "▧", DrawingGeometryKind.Image, 2, 2);
        Add("post", "Post", DrawingToolCategory.Annotation, "↗", DrawingGeometryKind.Note, 1, 1, text: true, fill: true);
        Add("idea", "Idea", DrawingToolCategory.Annotation, "◉", DrawingGeometryKind.Note, 1, 1, text: true, fill: true);

        // Pattern tools.
        Add("xabcd-pattern", "XABCD Pattern", DrawingToolCategory.Pattern, "X", DrawingGeometryKind.Pattern, 5, 5, text: true, fill: true);
        Add("cypher-pattern", "Cypher Pattern", DrawingToolCategory.Pattern, "C", DrawingGeometryKind.Pattern, 5, 5, text: true, fill: true);
        Add("abcd-pattern", "ABCD Pattern", DrawingToolCategory.Pattern, "A", DrawingGeometryKind.Pattern, 4, 4, text: true);
        Add("triangle-pattern", "Triangle Pattern", DrawingToolCategory.Pattern, "△", DrawingGeometryKind.Pattern, 4, 4, text: true, fill: true);
        Add("three-drives-pattern", "Three Drives Pattern", DrawingToolCategory.Pattern, "3", DrawingGeometryKind.Pattern, 6, 6, text: true);
        Add("head-shoulders", "Head and Shoulders", DrawingToolCategory.Pattern, "H", DrawingGeometryKind.Pattern, 7, 7, text: true);
        Add("elliott-impulse", "Elliott Impulse Wave", DrawingToolCategory.Pattern, "12345", DrawingGeometryKind.Pattern, 6, 6, text: true);
        Add("elliott-triangle", "Elliott Triangle Wave", DrawingToolCategory.Pattern, "ABCDE", DrawingGeometryKind.Pattern, 6, 6, text: true);
        Add("elliott-triple-combo", "Elliott Triple Combo Wave", DrawingToolCategory.Pattern, "WXYZ", DrawingGeometryKind.Pattern, 6, 6, text: true);
        Add("elliott-correction", "Elliott Correction Wave", DrawingToolCategory.Pattern, "ABC", DrawingGeometryKind.Pattern, 4, 4, text: true);
        Add("elliott-double-combo", "Elliott Double Combo Wave", DrawingToolCategory.Pattern, "WXY", DrawingGeometryKind.Pattern, 4, 4, text: true);
        Add("cyclic-lines", "Cyclic Lines", DrawingToolCategory.Pattern, "|||", DrawingGeometryKind.Cycles, 2, 2);
        Add("time-cycles", "Time Cycles", DrawingToolCategory.Pattern, "○○", DrawingGeometryKind.Cycles, 2, 2);
        Add("sine-line", "Sine Line", DrawingToolCategory.Pattern, "∿", DrawingGeometryKind.Sine, 2, 2);

        // Prediction and measurement.  Ordering mirrors the audited reference flyout.
        // FORECASTING
        Add("long-position", "Long position", DrawingToolCategory.PredictionMeasurement, "L", DrawingGeometryKind.Position, 3, 3, text: true, fill: true, levels: true);
        Add("short-position", "Short position", DrawingToolCategory.PredictionMeasurement, "S", DrawingGeometryKind.Position, 3, 3, text: true, fill: true, levels: true);
        Add("position-forecast", "Position forecast", DrawingToolCategory.PredictionMeasurement, "P", DrawingGeometryKind.Position, 2, 2, text: true, fill: true);
        Add("bars-pattern", "Bars pattern", DrawingToolCategory.PredictionMeasurement, "▥", DrawingGeometryKind.BarsPattern, 3, 3);
        Add("ghost-feed", "Ghost feed", DrawingToolCategory.PredictionMeasurement, "👻", DrawingGeometryKind.GhostFeed, 2, 256, variable: true);
        Add("sector", "Sector", DrawingToolCategory.PredictionMeasurement, "◔", DrawingGeometryKind.Sector, 3, 3, fill: true);
        // VOLUME BASED
        Add("anchored-vwap", "Anchored VWAP", DrawingToolCategory.PredictionMeasurement, "V", DrawingGeometryKind.AnchoredVwap, 1, 1, levels: true);
        Add("fixed-volume-profile", "Fixed range volume profile", DrawingToolCategory.PredictionMeasurement, "VP", DrawingGeometryKind.VolumeProfile, 2, 2, fill: true, levels: true);
        Add("anchored-volume-profile", "Anchored volume profile", DrawingToolCategory.PredictionMeasurement, "AV", DrawingGeometryKind.VolumeProfile, 1, 1, fill: true, levels: true);
        // MEASURES
        Add("price-range", "Price range", DrawingToolCategory.PredictionMeasurement, "↕", DrawingGeometryKind.Range, 2, 2, text: true, fill: true);
        Add("date-range", "Date range", DrawingToolCategory.PredictionMeasurement, "↔", DrawingGeometryKind.Range, 2, 2, text: true, fill: true);
        Add("date-price-range", "Date and price range", DrawingToolCategory.PredictionMeasurement, "↕↔", DrawingGeometryKind.Range, 2, 2, text: true, fill: true);

        // Collections are represented as selectable tools with a symbol picker in settings.
        Add("icons", "Icons", DrawingToolCategory.IconsMedia, "★", DrawingGeometryKind.Icon, 1, 1, text: true);
        Add("stickers", "Stickers", DrawingToolCategory.IconsMedia, "☀", DrawingGeometryKind.Icon, 1, 1, text: true);
        Add("emojis", "Emojis", DrawingToolCategory.IconsMedia, "☺", DrawingGeometryKind.Icon, 1, 1, text: true);

        return list;
    }
}
