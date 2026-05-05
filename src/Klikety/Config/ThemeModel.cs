namespace Klikety.Config;

/// <summary>
/// Theme configuration for the overlay grid appearance.
/// Deserialized from .theme.json files.
/// </summary>
public sealed class ThemeModel {
    // Label text
    public string LabelFontFamily { get; init; } = "Segoe UI";
    public double LabelFontSize { get; init; } = 14.0;
    public string LabelColor { get; init; } = "#FFFFFF";
    public string LabelFontWeight { get; init; } = "Normal";
    public string LabelOutlineColor { get; init; } = "#000000";
    public double LabelOutlineThickness { get; init; } = 1.5;

    // Cell borders
    public string CellBorderColor { get; init; } = "#555555";
    public double CellBorderThickness { get; init; } = 1.0;

    // Normal cell background
    public string CellBackgroundColor { get; init; } = "#000000";
    public double CellBackgroundOpacity { get; init; } = 0.4;

    // Dimmed cell overlay (applied to non-matching cells after first key)
    public string DimmedOverlayColor { get; init; } = "#000000";
    public double DimmedOverlayOpacity { get; init; } = 0.6;

    // Highlighted column (matching column after first key)
    public string HighlightedColumnBackground { get; init; } = "#FFCC00";
    public string HighlightedColumnBorderColor { get; init; } = "#FFCC00";

    // Subgrid distinct styling
    public string SubgridBorderColor { get; init; } = "#00AAFF";
    public string SubgridLabelColor { get; init; } = "#00CCFF";

    // External labels (when subgrid cells are too small for inline labels)
    public string ExternalLabelColor { get; init; } = "#FFCC00";
    public string ExternalRowLabelColor { get; init; } = "#CCE066";
    public string ConnectorLineColor { get; init; } = "#FFCC00";
    public double ConnectorLineThickness { get; init; } = 1.0;

    // LogGrid small-cell tint (cells below external-label threshold)
    public string SmallCellBackgroundColor { get; init; } = "#1A4A7A";
    public double SmallCellBackgroundOpacity { get; init; } = 0.5;
}
