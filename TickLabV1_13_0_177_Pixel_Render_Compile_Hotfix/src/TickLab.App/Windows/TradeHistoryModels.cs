using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TickLab.Desktop;

public sealed class TradeHistoryTrade
{
    public string Ticket { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public double Volume { get; set; }
    public DateTime OpenTime { get; set; }
    public DateTime CloseTime { get; set; }
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double Profit { get; set; }
    public double Commission { get; set; }
    public double Swap { get; set; }
    public double Fees { get; set; }
    public double? BalanceAfter { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string CloseReason { get; set; } = string.Empty;
    public double NetProfit => Profit + Commission + Swap + Fees;
    public TimeSpan Duration => CloseTime > OpenTime ? CloseTime - OpenTime : TimeSpan.Zero;
    public string Side => Direction;
}

public sealed class TradeHistoryCashFlow
{
    public DateTime Time { get; set; }
    public string Type { get; set; } = string.Empty;
    public double Amount { get; set; }
    public double? BalanceAfter { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class TradeHistoryReportData
{
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public double? StartingBalance { get; set; }
    public double? EndingBalance { get; set; }
    public List<TradeHistoryTrade> Trades { get; } = new();
    public List<TradeHistoryCashFlow> CashFlows { get; } = new();
    public string ParseNote { get; set; } = string.Empty;
    public double Deposits => CashFlows.Where(x => x.Amount > 0).Sum(x => x.Amount);
    public double Withdrawals => -CashFlows.Where(x => x.Amount < 0).Sum(x => x.Amount);
    public double NetTradingProfit => Trades.Sum(x => x.NetProfit);
}

public sealed record Mt5TradeProjectionRecord(
    TradeHistoryTrade Trade,
    int SourceIndex,
    long OpenUnix,
    long CloseUnix,
    long PrefixMaxCloseUnix);

public sealed class Mt5TradeHistoryFileEntry : INotifyPropertyChanged
{
    private bool _isProjected;
    private TradeHistoryReportData? _report;
    private Mt5TradeProjectionRecord[] _projectionIndex = Array.Empty<Mt5TradeProjectionRecord>();

    public string FilePath { get; init; } = string.Empty;
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public DateTime ModifiedUtc { get; init; }
    public long SizeBytes { get; init; }
    public TradeHistoryReportData? Report
    {
        get => _report;
        set
        {
            _report = value;
            BuildProjectionIndex();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TradeCountText));
            OnPropertyChanged(nameof(StatusText));
        }
    }
    public bool IsProjected
    {
        get => _isProjected;
        set { if (_isProjected == value) return; _isProjected = value; OnPropertyChanged(); }
    }
    public string TradeCountText => Report is null ? "Not loaded" : $"{Report.Trades.Count:N0} trades";
    public string StatusText => Report is null ? "Ready to parse" : string.IsNullOrWhiteSpace(Report.ParseNote) ? TradeCountText : $"{TradeCountText} · {Report.ParseNote}";

    public IReadOnlyList<Mt5TradeProjectionRecord> GetProjectionTrades(long windowStartUnix, long windowEndUnix)
    {
        if (_projectionIndex.Length == 0 || windowEndUnix < windowStartUnix)
            return Array.Empty<Mt5TradeProjectionRecord>();

        int upperExclusive = UpperBoundOpenUnix(windowEndUnix);
        if (upperExclusive <= 0)
            return Array.Empty<Mt5TradeProjectionRecord>();

        int lower = LowerBoundPrefixClose(windowStartUnix, upperExclusive);
        if (lower >= upperExclusive)
            return Array.Empty<Mt5TradeProjectionRecord>();

        var visible = new List<Mt5TradeProjectionRecord>(Math.Min(256, upperExclusive - lower));
        for (int i = lower; i < upperExclusive; i++)
        {
            Mt5TradeProjectionRecord item = _projectionIndex[i];
            if (item.CloseUnix >= windowStartUnix)
                visible.Add(item);
        }
        return visible;
    }

    private void BuildProjectionIndex()
    {
        if (_report is null || _report.Trades.Count == 0)
        {
            _projectionIndex = Array.Empty<Mt5TradeProjectionRecord>();
            return;
        }

        var sorted = _report.Trades
            .Select((trade, index) =>
            {
                long open = ToProjectionUnix(trade.OpenTime);
                long close = ToProjectionUnix(trade.CloseTime);
                if (close < open)
                    (open, close) = (close, open);
                return (Trade: trade, SourceIndex: index, OpenUnix: open, CloseUnix: close);
            })
            .OrderBy(item => item.OpenUnix)
            .ThenBy(item => item.CloseUnix)
            .ThenBy(item => item.SourceIndex)
            .ToArray();

        var indexItems = new Mt5TradeProjectionRecord[sorted.Length];
        long prefixMaxClose = long.MinValue;
        for (int i = 0; i < sorted.Length; i++)
        {
            prefixMaxClose = Math.Max(prefixMaxClose, sorted[i].CloseUnix);
            indexItems[i] = new Mt5TradeProjectionRecord(
                sorted[i].Trade, sorted[i].SourceIndex, sorted[i].OpenUnix, sorted[i].CloseUnix, prefixMaxClose);
        }
        _projectionIndex = indexItems;
    }

    private int UpperBoundOpenUnix(long value)
    {
        int low = 0;
        int high = _projectionIndex.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_projectionIndex[middle].OpenUnix <= value)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private int LowerBoundPrefixClose(long value, int upperExclusive)
    {
        int low = 0;
        int high = Math.Clamp(upperExclusive, 0, _projectionIndex.Length);
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_projectionIndex[middle].PrefixMaxCloseUnix < value)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static long ToProjectionUnix(DateTime time)
    {
        DateTime unspecified = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
