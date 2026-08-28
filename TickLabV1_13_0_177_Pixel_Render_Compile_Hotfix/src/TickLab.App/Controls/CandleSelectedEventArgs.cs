using TickLab.Core.Market;

namespace TickLab.Desktop.Controls;

public sealed class CandleSelectedEventArgs : EventArgs
{
    public CandleSelectedEventArgs(Candle candle)
    {
        Candle = candle;
    }

    public Candle Candle { get; }
}
