namespace Stanley.ProjectModel.Characters;

/// <summary>
/// The handful of numbers a character's body is generated from (see <see cref="BodyRig"/>)
/// - the "sliders" path to a body, with no drawing. Sizes are <b>relative</b>, not
/// real-world units: <see cref="Height"/> 1.0 is an average adult, so what the numbers
/// say is "Bob is a head taller than Alice", which is what a comic needs.
/// </summary>
/// <param name="Height">Overall height relative to an average adult (1.0). Head top to ground.</param>
/// <param name="Build">Weight: 0 slim - 1 heavy. Widens the torso, belly, hips and limbs; never changes heights. The same number build-responsive stickers will key off later.</param>
/// <param name="Muscle">0 soft - 1 muscular. Mostly shoulders, chest, arms and thighs.</param>
/// <param name="HeadsTall">Height measured in head heights: ~3 chibi, ~5 child, ~7.5 adult, ~8.5 heroic. Covers age as well as style, so there's no separate age setting.</param>
/// <param name="Frame">0 broad shoulders and narrow hips ("V") - 1 narrow shoulders and wide hips ("A"); 0.5 neutral.</param>
public sealed record BodyShape(double Height, double Build, double Muscle, double HeadsTall, double Frame)
{
    public const double MinHeight = 0.3;
    public const double MaxHeight = 1.6;
    public const double MinHeadsTall = 3;
    public const double MaxHeadsTall = 9;

    /// <summary>An average adult: what a brand-new character starts as.</summary>
    public static BodyShape Default { get; } = new(1.0, 0.3, 0.3, 7.5, 0.5);

    /// <summary>Every value pulled into its range, so a hand-edited file can't produce a broken body.</summary>
    public BodyShape Normalized() => new(
        Clamp(Height, MinHeight, MaxHeight, 1.0),
        Clamp(Build, 0, 1, 0.3),
        Clamp(Muscle, 0, 1, 0.3),
        Clamp(HeadsTall, MinHeadsTall, MaxHeadsTall, 7.5),
        Clamp(Frame, 0, 1, 0.5));

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
