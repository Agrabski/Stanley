namespace Stanley.ProjectModel.Characters;

/// <summary>Ready-made body types - one click sets every <see cref="BodyShape"/> value, then the sliders fine-tune.</summary>
public enum BodyPreset
{
    Toddler,
    Child,
    Teen,
    Adult,
    Heavy,
    Strong,
    Heroic,
    Elderly,
    Chibi
}

/// <summary>The same enum-plus-static-lookup shape as <c>BubbleStylePresets</c> and <c>MetricPaperSizes</c>.</summary>
public static class BodyPresets
{
    public static IReadOnlyList<BodyPreset> All { get; } = Enum.GetValues<BodyPreset>();

    public static BodyShape Shape(BodyPreset preset) => preset switch
    {
        BodyPreset.Toddler => new BodyShape(0.5, 0.45, 0.0, 4.2, 0.55),
        BodyPreset.Child => new BodyShape(0.72, 0.3, 0.1, 5.5, 0.5),
        BodyPreset.Teen => new BodyShape(0.93, 0.2, 0.2, 6.8, 0.5),
        BodyPreset.Adult => BodyShape.Default,
        BodyPreset.Heavy => new BodyShape(1.0, 0.9, 0.2, 7.3, 0.55),
        BodyPreset.Strong => new BodyShape(1.05, 0.4, 0.9, 7.5, 0.2),
        BodyPreset.Heroic => new BodyShape(1.1, 0.2, 0.65, 8.5, 0.25),
        BodyPreset.Elderly => new BodyShape(0.95, 0.45, 0.05, 7.0, 0.6),
        BodyPreset.Chibi => new BodyShape(0.55, 0.3, 0.1, 3.0, 0.5),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };
}
