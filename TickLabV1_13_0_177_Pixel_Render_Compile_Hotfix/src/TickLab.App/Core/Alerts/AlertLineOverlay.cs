namespace TickLab.Core.Alerts;

public sealed record AlertLineOverlay(
    string AlertId,
    double Price,
    string Label,
    bool Enabled = true,
    string Color = "#F5B83E",
    double Thickness = 1.25);
