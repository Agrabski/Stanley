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

    /// <summary>The face slots an expression sets, in the order a mix of your own lists them.</summary>
    public static IReadOnlyList<string> FaceSlots { get; } = [StickerSlots.Eyes, StickerSlots.Brows, StickerSlots.Mouth];

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

    /// <summary>The variant <paramref name="slot"/> shows in <paramref name="pose"/> (a slot left out is neutral).</summary>
    public static string VariantOf(PoseData pose, string slot) => pose.Expression?.GetValueOrDefault(slot) ?? Neutral;

    /// <summary>
    /// A mix of your own: <paramref name="pose"/> with just <paramref name="slot"/> showing
    /// <paramref name="variant"/> - happy eyes over an open mouth, say. The rest of the face
    /// is kept; neutral leaves the slot out, as a preset does.
    /// </summary>
    public static PoseData SetVariant(PoseData pose, string slot, string variant)
    {
        var expression = new SortedDictionary<string, string>(pose.Expression ?? [], StringComparer.Ordinal);
        if (variant == Neutral)
            expression.Remove(slot);
        else
            expression[slot] = variant;
        return pose with { Expression = expression };
    }

    /// <summary>A variant's name for people: "halfClosed" is "Half closed", "myMouth2" "My mouth 2", "o" an "Oh" mouth.</summary>
    public static string VariantName(string slot, string variant) => (slot, variant) switch
    {
        (StickerSlots.Eyes, "wide") => "Wide open",
        (StickerSlots.Mouth, "o") => "Oh",
        _ => Words(variant),
    };

    /// <summary>A camel-case key as words: capitalised, a space before each capital or number.</summary>
    private static string Words(string key)
    {
        var words = new System.Text.StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i > 0 && (char.IsUpper(c) || char.IsDigit(c) && !char.IsDigit(key[i - 1])))
                words.Append(' ');
            words.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return words.ToString();
    }

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
