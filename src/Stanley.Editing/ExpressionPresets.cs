using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing;

/// <summary>Ready-made facial expressions - one click on a placed character.</summary>
public enum ExpressionPreset
{
    Neutral,
    Happy,
    Laughing,
    Sad,
    Angry,
    Surprised,
    Scared,
    Skeptical,
    Wink,
    Talking,
    Shouting,
    Asleep
}

/// <summary>
/// An expression: the variant each face slot shows (docs/sticker-system.md §7). A face
/// sticker without that variant shows its neutral one, so a preset works on any face.
/// </summary>
public sealed record ExpressionPresetDefinition(ExpressionPreset Preset, string Name, string Eyes, string Brows, string Mouth)
{
    /// <summary>Slot → variant, for the three face slots a preset sets.</summary>
    public IReadOnlyDictionary<string, string> Variants { get; } = new Dictionary<string, string>
    {
        [StickerSlots.Eyes] = Eyes,
        [StickerSlots.Brows] = Brows,
        [StickerSlots.Mouth] = Mouth,
    };
}

/// <summary>
/// The expression presets and the standard variant vocabulary they draw on - the same
/// enum-plus-static-lookup shape as <see cref="PosePresets"/>. An expression is stored in
/// <see cref="PoseData.Expression"/> (slot → variant), so it's per panel like the pose.
/// </summary>
public static class ExpressionPresets
{
    public const string Neutral = "neutral";

    /// <summary>The standard variants per face slot: what the library draws, and what presets ask for.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Vocabulary { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [StickerSlots.Eyes] = [Neutral, "happy", "sad", "angry", "wide", "closed", "wink", "halfClosed"],
        [StickerSlots.Brows] = [Neutral, "raised", "angry", "sad", "skeptical"],
        [StickerSlots.Mouth] = [Neutral, "smile", "grin", "open", "shout", "frown", "o", "smirk"],
    };

    public static IReadOnlyList<ExpressionPresetDefinition> All { get; } =
    [
        new(ExpressionPreset.Neutral, "Neutral", Neutral, Neutral, Neutral),
        new(ExpressionPreset.Happy, "Happy", "happy", Neutral, "smile"),
        new(ExpressionPreset.Laughing, "Laughing", "happy", "raised", "grin"),
        new(ExpressionPreset.Sad, "Sad", "sad", "sad", "frown"),
        new(ExpressionPreset.Angry, "Angry", "angry", "angry", "frown"),
        new(ExpressionPreset.Surprised, "Surprised", "wide", "raised", "o"),
        new(ExpressionPreset.Scared, "Scared", "wide", "sad", "open"),
        new(ExpressionPreset.Skeptical, "Skeptical", "halfClosed", "skeptical", "smirk"),
        new(ExpressionPreset.Wink, "Wink", "wink", "raised", "grin"),
        new(ExpressionPreset.Talking, "Talking", Neutral, Neutral, "open"),
        new(ExpressionPreset.Shouting, "Shouting", "angry", "angry", "shout"),
        new(ExpressionPreset.Asleep, "Asleep", "closed", Neutral, Neutral),
    ];

    public static ExpressionPresetDefinition Get(ExpressionPreset preset) => All.Single(p => p.Preset == preset);

    /// <summary>
    /// <paramref name="pose"/> with the face slots set to <paramref name="preset"/>'s variants;
    /// other slots' variants (a hat tipped) are kept. Neutral leaves the face slots out
    /// altogether, so a neutral pose stores nothing.
    /// </summary>
    public static PoseData Apply(PoseData pose, ExpressionPresetDefinition preset)
    {
        var expression = new SortedDictionary<string, string>(pose.Expression ?? [], StringComparer.Ordinal);
        foreach (var (slot, variant) in preset.Variants)
        {
            if (variant == Neutral)
                expression.Remove(slot);
            else
                expression[slot] = variant;
        }
        return pose with { Expression = expression };
    }

    public static CharacterInstance Apply(CharacterInstance instance, ExpressionPresetDefinition preset) =>
        instance with { Pose = Apply(instance.Pose, preset) };

    /// <summary>The preset <paramref name="pose"/>'s face matches (a slot left out counts as neutral), or null for a mix of its own.</summary>
    public static ExpressionPresetDefinition? Of(PoseData pose) =>
        All.FirstOrDefault(p => p.Variants.All(v => (pose.Expression?.GetValueOrDefault(v.Key) ?? Neutral) == v.Value));

    /// <summary>
    /// The vocabulary variants a face sticker doesn't draw (it shows neutral instead) - for
    /// the inline "no wink" warning. Empty for a slot outside the vocabulary.
    /// </summary>
    public static IReadOnlyList<string> MissingVariants(Sticker sticker) =>
        Vocabulary.TryGetValue(sticker.Slot, out var wanted) ? wanted.Where(v => !sticker.Variants.Contains(v)).ToList() : [];
}
