namespace TickLab.Core.Settings;

public enum ChartLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public enum ChartVisualType
{
    Candles,
    HollowCandles,
    Bars,
    VolumeCandles,
    Line,
    LineWithMarkers,
    StepLine,
    Area,
    HlcArea,
    Baseline,
    Columns,
    HighLow,
    HeikinAshi,
    Renko,
    LineBreak,
    Kagi,
    PointAndFigure,
    Range,
    TimePriceOpportunity,
    SessionVolumeProfile,
    VolumeFootprint,
    Tick
}

public sealed record ChartSettings
{
    public ChartVisualType ChartType { get; init; } = ChartVisualType.Candles;
    public int SyntheticBoxSizePoints { get; init; } = 100;
    public int RangeBarSizePoints { get; init; } = 100;
    public int KagiReversalPoints { get; init; } = 100;
    public int LineBreakCount { get; init; } = 3;
    public int PointAndFigureReversalBoxes { get; init; } = 3;
    public int RenkoReversalBoxes { get; init; } = 2;
    public int TpoBracketMinutes { get; init; } = 30;
    public int MarketProfileRows { get; init; } = 48;
    public int ProfileSessionStartHour { get; init; } = 0;
    public int FootprintPriceStepPoints { get; init; } = 10;
    public double VolumeProfileValueAreaPercent { get; init; } = 70.0;
    public bool ShowVolumeProfileValueArea { get; init; } = true;
    public bool ShowFootprintDelta { get; init; } = true;
    public ChartScrollWheelMode ScrollWheelMode { get; init; } = ChartScrollWheelMode.Zoom;
    public bool ShowCandleGrid { get; init; } = true;
    public bool ShowCandleCrosshair { get; init; } = true;
    public bool SnapCandleCrosshair { get; init; } = true;
    public bool ShowCrosshairLabels { get; init; } = true;
    public bool ShowTickGrid { get; init; } = true;
    public bool ShowBidLine { get; init; } = true;
    public bool ShowAskLine { get; init; } = true;
    public bool ShowMidLine { get; init; }
    public bool ShowTickPoints { get; init; }
    public bool ShowTickCrosshair { get; init; } = true;

    public string ChartBackgroundColor { get; init; } = "#080808";
    public string ChartTextColor { get; init; } = "#D8D8D8";
    public string SelectedCandleColor { get; init; } = "#D8B84A";
    public string HistoryBoundaryColor { get; init; } = "#8A8A8A";
    public string ReplayStartLineColor { get; init; } = "#FACC15";
    public string ReplayEndLineColor { get; init; } = "#EF4444";
    public double ReplayStartLineThickness { get; init; } = 1.0;
    public double ReplayEndLineThickness { get; init; } = 1.0;
    public string AlertLineColor { get; init; } = "#F5B83E";
    public string LatestButtonColor { get; init; } = "#5B86C4";
    public string LatestButtonTextColor { get; init; } = "#FFFFFF";
    public string PriceScaleBackgroundColor { get; init; } = "#080808";
    public string PriceScaleTextColor { get; init; } = "#B0B0B0";
    public string TimeScaleBackgroundColor { get; init; } = "#080808";
    public string TimeScaleTextColor { get; init; } = "#B0B0B0";
    public string GridColor { get; init; } = "#2A2A2A";
    public double GridOpacity { get; init; } = 1.0;
    public double GridThickness { get; init; } = 1.0;

    public string UpBodyColor { get; init; } = "#2FB889";
    public string UpBorderColor { get; init; } = "#53CFA4";
    public string UpWickColor { get; init; } = "#53CFA4";
    public string DownBodyColor { get; init; } = "#DF5C68";
    public string DownBorderColor { get; init; } = "#F07B85";
    public string DownWickColor { get; init; } = "#F07B85";
    public double CandleBorderThickness { get; init; } = 1.0;
    public double CandleWickThickness { get; init; } = 1.0;

    public string TickBidColor { get; init; } = "#2D7EF7";
    public string TickAskColor { get; init; } = "#EF5150";
    public double TickBidThickness { get; init; } = 1.6;
    public double TickAskThickness { get; init; } = 1.4;

    public bool ShowPriceLine { get; init; } = true;
    public string PriceLineColor { get; init; } = "#5B86C4";
    public string PriceLineTextColor { get; init; } = "#FFFFFF";
    public ChartLineStyle PriceLineStyle { get; init; } = ChartLineStyle.Dashed;
    public double PriceLineThickness { get; init; } = 1.0;

    public bool ShowAskPriceLine { get; init; } = true;
    public string AskPriceLineColor { get; init; } = "#D8A84A";
    public string AskPriceLineTextColor { get; init; } = "#111111";
    public ChartLineStyle AskPriceLineStyle { get; init; } = ChartLineStyle.Dotted;
    public double AskPriceLineThickness { get; init; } = 1.0;

    public bool ShowSpreadLine { get; init; }
    public string SpreadLineColor { get; init; } = "#D8A84A";
    public ChartLineStyle SpreadLineStyle { get; init; } = ChartLineStyle.Dotted;
    public double SpreadLineThickness { get; init; } = 1.0;
    public bool ShowSpreadLabel { get; init; } = true;
    public bool ShowSpreadFill { get; init; }
    public double SpreadFillOpacity { get; init; } = 0.10;

    public bool ShowCandleCountdown { get; init; }
    public string CrosshairColor { get; init; } = "#888888";
    public string CrosshairLabelBackgroundColor { get; init; } = "#303030";
    public string CrosshairLabelTextColor { get; init; } = "#FFFFFF";
    public ChartLineStyle CrosshairLineStyle { get; init; } = ChartLineStyle.Dashed;
    public double CrosshairThickness { get; init; } = 1.0;

    public static ChartSettings Default => new();
}
