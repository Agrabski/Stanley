namespace Stanley.ProjectModel.Characters;

/// <summary>One of the four parts a <see cref="ModularBody"/> is built from (docs/shape-characters.md §4).</summary>
public enum ModulePart
{
    /// <summary><see cref="ModularBody.Head"/>, a <see cref="ShapeModule"/>.</summary>
    Head,

    /// <summary><see cref="ModularBody.Torso"/>, a <see cref="ShapeModule"/>.</summary>
    Torso,

    /// <summary><see cref="ModularBody.Arms"/>, a <see cref="LimbModule"/>.</summary>
    Arms,

    /// <summary><see cref="ModularBody.Legs"/>, a <see cref="LimbModule"/>.</summary>
    Legs
}

/// <summary>Which of a limb's own shapes is meant: its hand or foot, or one of its two pieces.</summary>
public enum LimbPiece
{
    /// <summary><see cref="LimbModule.End"/>: the hand or foot.</summary>
    End,

    /// <summary><see cref="LimbModule.Upper"/>: the upper arm or thigh, for <see cref="LimbStyle.Pieces"/>.</summary>
    Upper,

    /// <summary><see cref="LimbModule.Lower"/>: the forearm or shin, for <see cref="LimbStyle.Pieces"/>.</summary>
    Lower
}

/// <summary>Naming and sorting the parts of a <see cref="ModularBody"/>.</summary>
public static class ModuleParts
{
    public static IReadOnlyList<ModulePart> All { get; } = [ModulePart.Head, ModulePart.Torso, ModulePart.Arms, ModulePart.Legs];

    /// <summary>A head or a torso: an outline with a size. The others are limbs.</summary>
    public static bool IsShape(this ModulePart part) => part is ModulePart.Head or ModulePart.Torso;

    public static bool IsLimb(this ModulePart part) => part is ModulePart.Arms or ModulePart.Legs;

    /// <summary>The part's name as a user reads it in a sentence: "head", "torso", "arms", "legs".</summary>
    public static string Noun(this ModulePart part) => part switch
    {
        ModulePart.Head => "head",
        ModulePart.Torso => "torso",
        ModulePart.Arms => "arms",
        _ => "legs"
    };
}
