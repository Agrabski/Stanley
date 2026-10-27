using Stanley.ProjectModel.Characters;

namespace Stanley.StickerLibrary;

/// <summary>A named one-click body built from library shapes (docs/shape-characters.md §8): the way in to building a character from shapes.</summary>
public sealed record CreaturePreset(string Name, ModularBody Body);

/// <summary>
/// The starter creature presets, built from the shape library, in the gallery's order, plus
/// what a new shape character starts as (<see cref="Default"/>) and what putting a part
/// back adds (<see cref="DefaultShape"/>, <see cref="DefaultLimbs"/>). Sizes are figure units
/// (1.0 = an average adult's height; the mannequin's adult head is about 0.13 tall), so a
/// creature stands beside a human character at a sensible size. Every call hands out fresh
/// copies of the library shapes, each marked with its source, so applying a preset is just
/// storing its body.
/// </summary>
public static class CreaturePresets
{
    /// <summary>What "From shapes" starts as: an egg head on a pear, tube arms with round hands, tube legs in shoes.</summary>
    public static ModularBody Default => new(DefaultHead, DefaultTorso, DefaultArms, DefaultLegs);

    /// <summary>The head "Add head" puts back.</summary>
    public static ShapeModule DefaultHead => new(Shape("head/organic/egg"), 0.26, 0.28);

    /// <summary>The torso "Add torso" puts back.</summary>
    public static ShapeModule DefaultTorso => new(Shape("torso/organic/pear"), 0.3, 0.32);

    /// <summary>The arms "Add arms" puts back.</summary>
    public static LimbModule DefaultArms => new(LimbStyle.Tube, 0.26, 0.06, End: Shape("hand/geometric/ball"), EndSize: 0.085);

    /// <summary>The legs "Add legs" puts back.</summary>
    public static LimbModule DefaultLegs => new(LimbStyle.Tube, 0.24, 0.075, End: Shape("foot/organic/shoe"), EndSize: 0.16);

    /// <summary>The default head or torso for <paramref name="part"/>; throws for a limb.</summary>
    public static ShapeModule DefaultShape(ModulePart part) => part switch
    {
        ModulePart.Head => DefaultHead,
        ModulePart.Torso => DefaultTorso,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Arms and legs are limbs: see DefaultLimbs.")
    };

    /// <summary>The default arms or legs for <paramref name="part"/>; throws for a head or torso.</summary>
    public static LimbModule DefaultLimbs(ModulePart part) => part switch
    {
        ModulePart.Arms => DefaultArms,
        ModulePart.Legs => DefaultLegs,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "A head or a torso is a shape: see DefaultShape.")
    };

    /// <summary>Every preset, in the gallery's order. Each read builds fresh copies.</summary>
    public static IReadOnlyList<CreaturePreset> All =>
    [
        // No head: the face goes on the bean itself.
        new("Bean", new ModularBody(
            Head: null,
            Torso: new(Shape("torso/organic/bean"), 0.36, 0.56),
            Arms: new(LimbStyle.Tube, 0.2, 0.045),
            Legs: new(LimbStyle.Tube, 0.12, 0.06, End: Shape("foot/organic/shoe"), EndSize: 0.13))),

        new("Mr. Square", new ModularBody(
            Head: new(Shape("head/geometric/rounded-square"), 0.3, 0.3),
            Torso: new(Shape("torso/geometric/block"), 0.34, 0.34),
            Arms: new(LimbStyle.Tube, 0.27, 0.055, End: Shape("hand/geometric/block"), EndSize: 0.09),
            Legs: new(LimbStyle.Tube, 0.26, 0.08, End: Shape("foot/geometric/flat"), EndSize: 0.17))),

        // The issue's own example: a square head on an upside-down triangle.
        new("Hero", new ModularBody(
            Head: new(Shape("head/geometric/rounded-square"), 0.22, 0.24),
            Torso: new(Shape("torso/geometric/soft-triangle"), 0.42, 0.4, FlipY: true),
            Arms: new(LimbStyle.Taper, 0.32, 0.075, Taper: 0.35, End: Shape("hand/organic/mitten"), EndSize: 0.1),
            Legs: new(LimbStyle.Taper, 0.38, 0.1, Taper: 0.4, End: Shape("foot/organic/shoe"), EndSize: 0.17))),

        // Building blocks: every piece outlined on its own.
        new("Robot", new ModularBody(
            Head: new(Shape("head/geometric/hexagon"), 0.26, 0.24),
            Torso: new(Shape("torso/geometric/block"), 0.36, 0.36),
            Arms: new(LimbStyle.Pieces, 0.3, 0.07, End: Shape("hand/abstract/claw"), EndSize: 0.1,
                Upper: Shape("piece/geometric/block"), Lower: Shape("piece/geometric/block")),
            Legs: new(LimbStyle.Pieces, 0.28, 0.09, End: Shape("foot/geometric/flat"), EndSize: 0.2,
                Upper: Shape("piece/geometric/block"), Lower: Shape("piece/geometric/block")),
            Separate: true)),

        // A thin block for a body, so the arms and legs meet on a line, as drawn with a pen.
        new("Stick figure", new ModularBody(
            Head: new(Shape("head/organic/egg"), 0.19, 0.21),
            Torso: new(Shape("piece/geometric/block"), 0.022, 0.32),
            Arms: new(LimbStyle.Stick, 0.3, 0.02),
            Legs: new(LimbStyle.Stick, 0.4, 0.02))),

        // No head and no arms: a wobbly blob with a face, on stubby legs.
        new("Blob", new ModularBody(
            Head: null,
            Torso: new(Shape("head/abstract/wobble"), 0.5, 0.46),
            Arms: null,
            Legs: new(LimbStyle.Tube, 0.08, 0.09, End: Shape("foot/organic/paw"), EndSize: 0.14))),

        new("Big-head", new ModularBody(
            Head: new(Shape("head/organic/potato"), 0.42, 0.4),
            Torso: new(Shape("torso/organic/pear"), 0.22, 0.22),
            Arms: new(LimbStyle.Taper, 0.2, 0.05, Taper: 0.4, End: Shape("hand/organic/mitten"), EndSize: 0.08),
            Legs: new(LimbStyle.Taper, 0.2, 0.065, Taper: 0.3, End: Shape("foot/organic/shoe"), EndSize: 0.14))),

        // No torso: little arms on the head's sides, long legs under it.
        new("Head-on-legs", new ModularBody(
            Head: new(Shape("head/geometric/dome"), 0.36, 0.36),
            Torso: null,
            Arms: new(LimbStyle.Tube, 0.16, 0.04),
            Legs: new(LimbStyle.Taper, 0.34, 0.06, Taper: 0.35, End: Shape("foot/organic/shoe"), EndSize: 0.15))),
    ];

    /// <summary>The preset called <paramref name="name"/>, or null.</summary>
    public static CreaturePreset? Get(string name) => All.FirstOrDefault(p => p.Name == name);

    private static ModuleShape Shape(string key) => ShapeLibrary.Instantiate(key);
}
