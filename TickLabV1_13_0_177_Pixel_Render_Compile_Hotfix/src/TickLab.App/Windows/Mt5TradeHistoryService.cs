using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TickLab.Desktop;

public static class Mt5TradeHistoryService
{
    private static readonly string[] SupportedExtensions = { ".html", ".htm", ".csv", ".txt", ".tsv" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string HistoryFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TickLab", "MT5 Trade History");

    private static string ProjectionSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickLab", "DemoTrading", "mt5-history-projection.json");

    public static IReadOnlyList<Mt5TradeHistoryFileEntry> ScanFolder()
    {
        try
        {
            Directory.CreateDirectory(HistoryFolder);
            HashSet<string> projected = LoadProjectedPaths();
            return Directory.EnumerateFiles(HistoryFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => new Mt5TradeHistoryFileEntry
                {
                    FilePath = path,
                    ModifiedUtc = File.GetLastWriteTimeUtc(path),
                    SizeBytes = new FileInfo(path).Length,
                    IsProjected = projected.Contains(NormalizePath(path))
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<Mt5TradeHistoryFileEntry>();
        }
    }

    public static void SaveProjectedPaths(IEnumerable<Mt5TradeHistoryFileEntry> entries)
    {
        try
        {
            string? folder = Path.GetDirectoryName(ProjectionSettingsPath);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            string[] paths = entries.Where(x => x.IsProjected).Select(x => NormalizePath(x.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            File.WriteAllText(ProjectionSettingsPath, JsonSerializer.Serialize(paths, JsonOptions));
        }
        catch { }
    }

    private static HashSet<string> LoadProjectedPaths()
    {
        try
        {
            if (!File.Exists(ProjectionSettingsPath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[]? paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(ProjectionSettingsPath));
            return new HashSet<string>((paths ?? Array.Empty<string>()).Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
        catch { return path.Trim().ToUpperInvariant(); }
    }

    public static TradeHistoryReportData ParseFile(string path)
    {
        var report = new TradeHistoryReportData { Name = Path.GetFileNameWithoutExtension(path), SourcePath = path };
        try
        {
            string text = File.ReadAllText(path, DetectEncoding(path));
            ExtractReportMetadata(text, report);
            List<string[]> rows = LooksLikeHtml(text) ? ExtractHtmlRows(text) : ExtractTextRows(text);
            ParseRows(rows, report);
            FinalizeBalances(report);
            if (report.Trades.Count == 0)
                report.ParseNote = "No completed position rows recognized";
        }
        catch (Exception ex)
        {
            report.ParseNote = $"Read error: {ex.Message}";
        }
        return report;
    }

    private static Encoding DetectEncoding(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length >= 2)
        {
            int b1 = stream.ReadByte(); int b2 = stream.ReadByte();
            if (b1 == 0xFF && b2 == 0xFE) return Encoding.Unicode;
            if (b1 == 0xFE && b2 == 0xFF) return Encoding.BigEndianUnicode;
        }
        return new UTF8Encoding(false, false);
    }

    private static bool LooksLikeHtml(string text) => text.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0;

    private static List<string[]> ExtractHtmlRows(string html)
    {
        var rows = new List<string[]>();
        foreach (Match rowMatch in Regex.Matches(html, "<tr\\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var cells = new List<string>();
            foreach (Match cellMatch in Regex.Matches(rowMatch.Groups[1].Value, "<t[dh]\\b[^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string cell = Regex.Replace(cellMatch.Groups[1].Value, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
                cell = Regex.Replace(cell, "<[^>]+>", " ");
                cell = WebUtility.HtmlDecode(cell);
                cells.Add(CleanCell(cell));
            }
            if (cells.Count > 0) rows.Add(cells.ToArray());
        }
        return rows;
    }

    private static List<string[]> ExtractTextRows(string text)
    {
        var rows = new List<string[]>();
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        char delimiter = DetectDelimiter(lines);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string[] cells = delimiter == '\0' ? Regex.Split(line, "\\s{2,}") : SplitDelimited(line, delimiter).ToArray();
            if (cells.Length > 1) rows.Add(cells.Select(CleanCell).ToArray());
        }
        return rows;
    }

    private static char DetectDelimiter(IEnumerable<string> lines)
    {
        foreach (string line in lines.Where(x => !string.IsNullOrWhiteSpace(x)).Take(20))
        {
            if (line.Count(c => c == '\t') >= 2) return '\t';
            if (line.Count(c => c == ';') >= 2) return ';';
            if (line.Count(c => c == ',') >= 3) return ',';
        }
        return '\0';
    }

    private static IEnumerable<string> SplitDelimited(string line, char delimiter)
    {
        var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == delimiter && !quoted) { yield return cell.ToString(); cell.Clear(); }
            else cell.Append(c);
        }
        yield return cell.ToString();
    }

    private static string CleanCell(string value) => Regex.Replace(value.Replace('\u00A0', ' '), "\\s+", " ").Trim();
    private static string Key(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);

    private static void ParseRows(List<string[]> rows, TradeHistoryReportData report)
    {
        var dealEvents = new List<DealEvent>();
        for (int headerIndex = 0; headerIndex < rows.Count; headerIndex++)
        {
            string[] header = rows[headerIndex];
            string[] keys = header.Select(Key).ToArray();
            if (!LooksLikeTradeHeader(keys)) continue;

            int end = headerIndex + 1;
            while (end < rows.Count)
            {
                string[] candidate = rows[end];
                if (candidate.Length == 0) break;
                string joined = string.Join(" ", candidate).Trim();
                if (end > headerIndex + 1 && (LooksLikeTradeHeader(candidate.Select(Key).ToArray()) || IsSectionHeading(joined))) break;
                TryParseTradeOrCashFlow(keys, candidate, report, dealEvents);
                end++;
            }
            headerIndex = Math.Max(headerIndex, end - 1);
        }

        // Some MT5 reports have a separate balance/deposit table without Symbol.
        for (int headerIndex = 0; headerIndex < rows.Count; headerIndex++)
        {
            string[] keys = rows[headerIndex].Select(Key).ToArray();
            if (!keys.Any(k => k.Contains("time")) || !keys.Any(k => k is "type" or "operation") || !keys.Any(k => k is "profit" or "amount" or "balance")) continue;
            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                if (rows[i].Select(Key).SequenceEqual(keys)) break;
                TryParseCashFlow(keys, rows[i], report);
            }
        }

        PairDealEvents(dealEvents, report);
        report.Trades.Sort((a, b) => a.CloseTime.CompareTo(b.CloseTime));
        report.CashFlows.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static bool LooksLikeTradeHeader(string[] keys)
    {
        bool hasSymbol = keys.Any(k => k.Contains("symbol"));
        bool hasType = keys.Any(k => k is "type" or "side" or "direction" or "ordertype");
        bool hasPrice = keys.Count(k => k.Contains("price") && k is not "slprice" and not "tpprice") >= 1;
        bool hasTime = keys.Any(k => k.Contains("time"));
        bool hasProfit = keys.Any(k => k.Contains("profit") || k == "pl" || k == "result");
        return hasSymbol && hasType && hasPrice && hasTime && hasProfit;
    }

    private static bool IsSectionHeading(string text)
    {
        string key = Key(text);
        return key is "orders" or "deals" or "positions" or "results" or "summary" or "workingorders";
    }

    private static void TryParseTradeOrCashFlow(string[] keys, string[] row, TradeHistoryReportData report, List<DealEvent> dealEvents)
    {
        if (row.Length < 3) return;
        string symbol = Cell(keys, row, "symbol");
        string side = FindSide(keys, row);
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(side))
        {
            TryParseCashFlow(keys, row, report);
            return;
        }

        List<int> timeIndices = Enumerable.Range(0, keys.Length).Where(i => keys[i].Contains("time") && !keys[i].Contains("duration")).ToList();
        List<int> priceIndices = Enumerable.Range(0, keys.Length).Where(i => keys[i].Contains("price") && !keys[i].Contains("sl") && !keys[i].Contains("tp")).ToList();
        int openTimeIndex = FindIndex(keys, "opentime", "timeopen", "time");
        int closeTimeIndex = FindIndex(keys, "closetime", "timeclose");
        if (closeTimeIndex < 0 && timeIndices.Count >= 2) closeTimeIndex = timeIndices[1];
        if (openTimeIndex < 0 && timeIndices.Count >= 1) openTimeIndex = timeIndices[0];
        int entryPriceIndex = FindIndex(keys, "openprice", "priceopen", "entryprice");
        int exitPriceIndex = FindIndex(keys, "closeprice", "priceclose", "exitprice");
        if (entryPriceIndex < 0 && priceIndices.Count >= 1) entryPriceIndex = priceIndices[0];
        if (exitPriceIndex < 0 && priceIndices.Count >= 2) exitPriceIndex = priceIndices[1];

        if (!TryDate(Cell(row, openTimeIndex), out DateTime openTime) || !TryDate(Cell(row, closeTimeIndex), out DateTime closeTime) ||
            !TryNumber(Cell(row, entryPriceIndex), out double entryPrice) || !TryNumber(Cell(row, exitPriceIndex), out double exitPrice))
        {
            TryParseDealEvent(keys, row, dealEvents);
            return;
        }

        TryNumber(Cell(keys, row, "volume", "lots", "lot"), out double volume);
        TryNumber(Cell(keys, row, "sl", "stoploss"), out double sl);
        TryNumber(Cell(keys, row, "tp", "takeprofit"), out double tp);
        TryNumber(Cell(keys, row, "profit", "pl", "result"), out double profit);
        TryNumber(Cell(keys, row, "commission"), out double commission);
        TryNumber(Cell(keys, row, "swap"), out double swap);
        TryNumber(Cell(keys, row, "fee", "fees"), out double fees);
        double? balanceAfter = TryNumber(Cell(keys, row, "balance"), out double balance) ? balance : null;
        string ticket = Cell(keys, row, "position", "ticket", "order", "deal");
        string comment = Cell(keys, row, "comment", "reason");

        report.Trades.Add(new TradeHistoryTrade
        {
            Ticket = ticket,
            Symbol = symbol,
            Direction = side,
            Volume = volume,
            OpenTime = openTime,
            CloseTime = closeTime,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            StopLoss = sl,
            TakeProfit = tp,
            Profit = profit,
            Commission = commission,
            Swap = swap,
            Fees = fees,
            BalanceAfter = balanceAfter,
            Comment = comment,
            CloseReason = comment
        });
    }

    private sealed class DealEvent
    {
        public DateTime Time { get; init; }
        public string Deal { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty;
        public string Flow { get; init; } = string.Empty;
        public double Volume { get; init; }
        public double RemainingVolume { get; set; }
        public double Price { get; init; }
        public double Commission { get; init; }
        public double Swap { get; init; }
        public double Fees { get; init; }
        public double Profit { get; init; }
        public double? BalanceAfter { get; init; }
        public string Comment { get; init; } = string.Empty;
    }

    private static void TryParseDealEvent(string[] keys, string[] row, List<DealEvent> deals)
    {
        int timeIndex = FindIndex(keys, "time", "dealtime");
        int priceIndex = FindIndex(keys, "price", "dealprice");
        if (!TryDate(Cell(row, timeIndex), out DateTime time) || !TryNumber(Cell(row, priceIndex), out double price)) return;
        string symbol = Cell(keys, row, "symbol");
        string side = FindSide(keys, row);
        string flow = Cell(keys, row, "direction", "entry").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(side) ||
            !(flow.Contains("IN") || flow.Contains("OUT"))) return;
        TryNumber(Cell(keys, row, "volume", "lots", "lot"), out double volume);
        if (volume <= 0) return;
        TryNumber(Cell(keys, row, "commission"), out double commission);
        TryNumber(Cell(keys, row, "swap"), out double swap);
        TryNumber(Cell(keys, row, "fee", "fees"), out double fees);
        TryNumber(Cell(keys, row, "profit", "pl", "result"), out double profit);
        double? balanceAfter = TryNumber(Cell(keys, row, "balance"), out double balance) ? balance : null;
        deals.Add(new DealEvent
        {
            Time = time,
            Deal = Cell(keys, row, "deal", "ticket", "order"),
            Symbol = symbol,
            Side = side,
            Flow = flow,
            Volume = volume,
            RemainingVolume = volume,
            Price = price,
            Commission = commission,
            Swap = swap,
            Fees = fees,
            Profit = profit,
            BalanceAfter = balanceAfter,
            Comment = Cell(keys, row, "comment")
        });
    }

    private static void PairDealEvents(List<DealEvent> deals, TradeHistoryReportData report)
    {
        var open = new List<DealEvent>();
        foreach (DealEvent deal in deals.OrderBy(x => x.Time))
        {
            bool isOut = deal.Flow.Contains("OUT", StringComparison.OrdinalIgnoreCase) && !deal.Flow.StartsWith("IN", StringComparison.OrdinalIgnoreCase);
            bool isIn = deal.Flow.Contains("IN", StringComparison.OrdinalIgnoreCase) && !isOut;
            if (isIn)
            {
                open.Add(deal);
                continue;
            }
            if (!isOut) continue;
            double remainingClose = deal.Volume;
            foreach (DealEvent entry in open.Where(x => x.RemainingVolume > 1e-12 && string.Equals(x.Symbol, deal.Symbol, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                if (remainingClose <= 1e-12) break;
                double matched = Math.Min(remainingClose, entry.RemainingVolume);
                double entryShare = matched / Math.Max(entry.Volume, 1e-12);
                double exitShare = matched / Math.Max(deal.Volume, 1e-12);
                report.Trades.Add(new TradeHistoryTrade
                {
                    Ticket = string.IsNullOrWhiteSpace(entry.Deal) ? deal.Deal : entry.Deal,
                    Symbol = entry.Symbol,
                    Direction = entry.Side,
                    Volume = matched,
                    OpenTime = entry.Time,
                    CloseTime = deal.Time,
                    EntryPrice = entry.Price,
                    ExitPrice = deal.Price,
                    Commission = entry.Commission * entryShare + deal.Commission * exitShare,
                    Swap = entry.Swap * entryShare + deal.Swap * exitShare,
                    Fees = entry.Fees * entryShare + deal.Fees * exitShare,
                    Profit = deal.Profit * exitShare,
                    BalanceAfter = deal.BalanceAfter,
                    Comment = deal.Comment,
                    CloseReason = deal.Comment
                });
                entry.RemainingVolume -= matched;
                remainingClose -= matched;
            }
        }
    }

    private static void TryParseCashFlow(string[] keys, string[] row, TradeHistoryReportData report)
    {
        string type = Cell(keys, row, "type", "operation");
        string lower = type.ToLowerInvariant();
        if (!(lower.Contains("balance") || lower.Contains("deposit") || lower.Contains("withdraw") || lower.Contains("credit"))) return;
        int timeIndex = FindIndex(keys, "time", "closetime", "opentime");
        if (!TryDate(Cell(row, timeIndex), out DateTime time)) time = DateTime.MinValue;
        string amountText = Cell(keys, row, "profit", "amount", "value");
        if (!TryNumber(amountText, out double amount)) return;
        double? balanceAfter = TryNumber(Cell(keys, row, "balance"), out double balance) ? balance : null;
        report.CashFlows.Add(new TradeHistoryCashFlow
        {
            Time = time,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            Comment = Cell(keys, row, "comment")
        });
    }

    private static string FindSide(string[] keys, string[] row)
    {
        foreach (string name in new[] { "type", "side", "direction", "ordertype" })
        {
            string value = Cell(keys, row, name).Trim().ToUpperInvariant();
            if (value.Contains("BUY")) return "BUY";
            if (value.Contains("SELL")) return "SELL";
        }
        foreach (string value in row)
        {
            string upper = value.Trim().ToUpperInvariant();
            if (upper is "BUY" or "BUY LIMIT" or "BUY STOP") return "BUY";
            if (upper is "SELL" or "SELL LIMIT" or "SELL STOP") return "SELL";
        }
        return string.Empty;
    }

    private static int FindIndex(string[] keys, params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            int exact = Array.FindIndex(keys, k => k == alias);
            if (exact >= 0) return exact;
        }
        return -1;
    }

    private static string Cell(string[] keys, string[] row, params string[] aliases)
    {
        int index = FindIndex(keys, aliases);
        return Cell(row, index);
    }

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : string.Empty;

    private static bool TryDate(string text, out DateTime value)
    {
        text = text.Trim();
        string[] formats =
        {
            "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm",
            "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm"
        };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified); return true;
        }
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value) ||
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified); return true;
        }
        return false;
    }

    private static bool TryNumber(string text, out double value)
    {
        text = WebUtility.HtmlDecode(text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-") { value = 0; return false; }
        text = text.Replace("\u00A0", string.Empty).Replace(" ", string.Empty).Replace("$", string.Empty).Replace("€", string.Empty).Replace("£", string.Empty);
        bool parenthetical = text.StartsWith('(') && text.EndsWith(')');
        if (parenthetical) text = text[1..^1];
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
        {
            if (parenthetical) value = -Math.Abs(value);
            return true;
        }
        // Accept decimal comma exports when no decimal point is present.
        if (!text.Contains('.') && text.Count(c => c == ',') == 1 && double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            if (parenthetical) value = -Math.Abs(value);
            return true;
        }
        value = 0; return false;
    }

    private static void ExtractReportMetadata(string text, TradeHistoryReportData report)
    {
        string plain = LooksLikeHtml(text) ? WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " ")) : text;
        plain = Regex.Replace(plain, "\\s+", " ");
        Match account = Regex.Match(plain, "(?:Account|Login)\\s*[:#]?\\s*([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase);
        if (account.Success) report.AccountName = account.Groups[1].Value;
        Match currency = Regex.Match(plain, "(?:Currency|Deposit currency)\\s*[:]?\\s*([A-Z]{3})", RegexOptions.IgnoreCase);
        if (currency.Success) report.Currency = currency.Groups[1].Value.ToUpperInvariant();
        Match initial = Regex.Match(plain, "(?:Initial Deposit|Initial Balance|Starting Balance)\\s*[:]?\\s*([-+()0-9., ]+)", RegexOptions.IgnoreCase);
        if (initial.Success && TryNumber(initial.Groups[1].Value, out double start)) report.StartingBalance = start;
    }

    private static void FinalizeBalances(TradeHistoryReportData report)
    {
        var events = report.Trades.Select(t => new { t.CloseTime, Delta = t.NetProfit, t.BalanceAfter })
            .Concat(report.CashFlows.Select(c => new { CloseTime = c.Time, Delta = c.Amount, c.BalanceAfter }))
            .Where(x => x.CloseTime != DateTime.MinValue)
            .OrderBy(x => x.CloseTime).ToList();
        if (events.Count > 0)
        {
            var firstKnown = events.FirstOrDefault(x => x.BalanceAfter.HasValue);
            if (!report.StartingBalance.HasValue && firstKnown is not null)
                report.StartingBalance = firstKnown.BalanceAfter!.Value - firstKnown.Delta;
            var lastKnown = events.LastOrDefault(x => x.BalanceAfter.HasValue);
            if (lastKnown is not null) report.EndingBalance = lastKnown.BalanceAfter;
            else if (report.StartingBalance.HasValue) report.EndingBalance = report.StartingBalance + events.Sum(x => x.Delta);
        }
    }
}
