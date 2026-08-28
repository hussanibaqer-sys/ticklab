using TickLab.Core.Market;

namespace TickLab.Desktop.Settings;

public sealed record CustomTimeframePreference
{
    public CustomTimeframePreference()
    {
    }

    public CustomTimeframePreference(int quantity, TimeframeUnit unit)
    {
        Quantity = quantity;
        Unit = unit;
    }

    public int Quantity { get; init; }
    public TimeframeUnit Unit { get; init; }
}
