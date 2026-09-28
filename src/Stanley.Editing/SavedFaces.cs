using System.Text;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing;

/// <summary>
/// Faces of your own, beyond <see cref="ExpressionPresets"/>: saving a face on the character
/// to use again in any panel, which variants the stickers a character wears draw (the
/// Face dropdown's mix-your-own rows, a drawn face's own expressions included), and the
/// key a newly drawn variant is stored under.
/// </summary>
public static class SavedFaces
{
    /// <summary>Every face slot, in the order the mix-your-own rows go: eyes, brows and mouth, then nose and facial hair.</summary>
    public static IReadOnlyList<string> Slots { get; } =
        [.. ExpressionPresets.FaceSlots, .. StickerSlots.All.Where(s => s.IsFace && !ExpressionPresets.FaceSlots.Contains(s.Name)).Select(s => s.Name)];

    /// <summary><paramref name="pose"/>'s face saved as <paramref name="name"/>: each face slot's variant, neutral ones left out.</summary>
    public static SavedExpression Capture(string name, PoseData pose) =>
        new(name.Trim(), new SortedDictionary<string, string>(
            (pose.Expression ?? []).Where(e => Slots.Contains(e.Key) && e.Value != ExpressionPresets.Neutral).ToDictionary(e => e.Key, e => e.Value),
            StringComparer.Ordinal));

    /// <summary><paramref name="pose"/> with <paramref name="face"/>: every face slot as saved (neutral where it has nothing); other slots' variants are kept.</summary>
    public static PoseData Apply(PoseData pose, SavedExpression face)
    {
        var expression = new SortedDictionary<string, string>(pose.Expression ?? [], StringComparer.Ordinal);
        foreach (var slot in Slots)
            expression.Remove(slot);
        foreach (var (slot, variant) in face.Variants)
            expression[slot] = variant;
        return pose with { Expression = expression };
    }

    /// <summary>Whether <paramref name="pose"/>'s face is exactly <paramref name="face"/> (a slot left out counts as neutral).</summary>
    public static bool Shows(PoseData pose, SavedExpression face) =>
        Slots.All(slot => ExpressionPresets.VariantOf(pose, slot) == (face.Variants.GetValueOrDefault(slot) ?? ExpressionPresets.Neutral));

    /// <summary>The saved face of <paramref name="character"/>'s that <paramref name="pose"/> shows, if any.</summary>
    public static SavedExpression? Of(CharacterDefinition character, PoseData pose) =>
        character.Expressions?.FirstOrDefault(face => Shows(pose, face));

    /// <summary><paramref name="character"/> with <paramref name="face"/> saved: in place of the one with the same name (in any case), else after the others.</summary>
    public static CharacterDefinition Save(CharacterDefinition character, SavedExpression face)
    {
        var faces = (character.Expressions ?? []).ToList();
        var existing = faces.FindIndex(f => string.Equals(f.Name, face.Name, StringComparison.CurrentCultureIgnoreCase));
        if (existing >= 0)
            faces[existing] = face;
        else
            faces.Add(face);
        return character with { Expressions = faces };
    }

    /// <summary><paramref name="character"/> without its saved face called <paramref name="name"/>; with none left, nothing is written for them.</summary>
    public static CharacterDefinition Delete(CharacterDefinition character, string name)
    {
        var faces = (character.Expressions ?? []).Where(f => !string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return character with { Expressions = faces.Count == 0 ? null : faces };
    }

    /// <summary>
    /// What a mix-your-own row offers for <paramref name="slot"/>: the variants the stickers
    /// worn there draw - the standard vocabulary's first, in its order, then their own (a
    /// drawn face's "crying" mouth). Empty when nothing is worn there.
    /// </summary>
    public static IReadOnlyList<string> VariantsFor(string slot, IEnumerable<Sticker> worn)
    {
        var drawn = worn.SelectMany(s => s.Variants).Distinct().ToList();
        var standard = ExpressionPresets.Vocabulary.GetValueOrDefault(slot) ?? [];
        return [.. standard.Where(drawn.Contains), .. drawn.Where(v => !standard.Contains(v))];
    }

    /// <summary>
    /// The key a new variant called <paramref name="name"/> is stored under - it names its
    /// folder, so letters and digits only, the words run together ("My mouth" is "myMouth")
    /// - numbered when <paramref name="taken"/> has it already.
    /// </summary>
    public static string NewVariantKey(string name, IEnumerable<string> taken)
    {
        var words = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var key = new StringBuilder();
        foreach (var word in words)
            key.Append(key.Length == 0 ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        var stem = key.Length == 0 ? "drawn" : key.ToString();
        var used = taken.ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(stem))
            return stem;
        for (var n = 2; ; n++)
        {
            if (!used.Contains(stem + n))
                return stem + n;
        }
    }
}
