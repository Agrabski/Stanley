namespace Stanley.Editing;

/// <summary>A one-click page layout: rows of equal height, each split into a number of equal-width panels.</summary>
public sealed record PanelLayoutPreset(string Name, IReadOnlyList<int> ColumnsPerRow);

/// <summary>The classic comic page grids, for filling a page in one click before fine-tuning by dragging gutters.</summary>
public static class PanelLayoutPresets
{
    public static IReadOnlyList<PanelLayoutPreset> All { get; } =
    [
        new("Splash", [1]),
        new("Two tiers", [1, 1]),
        new("Three tiers", [1, 1, 1]),
        new("Four panels", [2, 2]),
        new("Wide top", [1, 2]),
        new("Wide bottom", [2, 1]),
        new("Five panels", [2, 1, 2]),
        new("Six panels", [2, 2, 2]),
        new("Seven panels", [2, 3, 2]),
        new("Nine panels", [3, 3, 3]),
    ];
}
