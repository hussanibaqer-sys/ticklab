namespace TickLab.Gateway.FileBridge;

public sealed record Mt5SymbolInfo(
    string Name,
    string Description,
    string Path,
    bool IsSelectedInMarketWatch,
    bool IsVisible,
    bool IsCustom,
    int Digits)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Description)
            ? Name
            : $"{Name} — {Description}";
}
