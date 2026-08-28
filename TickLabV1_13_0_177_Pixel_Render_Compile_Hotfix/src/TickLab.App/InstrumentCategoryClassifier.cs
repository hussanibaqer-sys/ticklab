namespace TickLab.Gateway.FileBridge;

public static class InstrumentCategoryClassifier
{
    public static readonly string[] Filters =
    [
        "Favorites",
        "All",
        "Stocks",
        "Funds",
        "Futures",
        "Forex",
        "Crypto",
        "Commodities",
        "Indices",
        "Bonds",
        "Economy",
        "Options"
    ];

    public static string Classify(Mt5SymbolInfo symbol)
    {
        string text = $"{symbol.Path} {symbol.Description} {symbol.Name}".ToLowerInvariant();

        if (ContainsAny(text, "option", "options"))
            return "Options";
        if (ContainsAny(text, "economy", "economic", "macro", "calendar"))
            return "Economy";
        if (ContainsAny(text, "bond", "bonds", "treasury", "yield"))
            return "Bonds";
        if (ContainsAny(text, "indices", "index", "indexes"))
            return "Indices";
        if (ContainsAny(text, "crypto", "cryptocurrency", "digital asset", "bitcoin", "ethereum"))
            return "Crypto";
        if (ContainsAny(text, "future", "futures"))
            return "Futures";
        if (ContainsAny(text, "fund", "funds", "etf"))
            return "Funds";
        if (ContainsAny(text, "stock", "stocks", "equity", "equities", "share", "shares"))
            return "Stocks";
        if (ContainsAny(text, "commodity", "commodities", "metal", "metals", "energy", "energies",
            "gold", "silver", "oil", "gas", "xau", "xag", "brent", "wti"))
            return "Commodities";
        if (ContainsAny(text, "forex", "fx", "currency", "currencies"))
            return "Forex";

        // Most MT5 OTC currency symbols are grouped by broker folder. When no
        // category wording exists, a short six-letter alphabetic symbol is a
        // useful final fallback for Forex without misclassifying longer symbols.
        string compact = new(symbol.Name.Where(char.IsLetter).ToArray());
        if (compact.Length == 6 && symbol.Name.Length <= 12)
            return "Forex";

        return "Other";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
