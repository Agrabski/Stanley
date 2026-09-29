using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// A standard sticker slot: where a new sticker for it goes by default, its z-order
/// within its layer, whether a click on a placed design already worn there stamps
/// another copy of it (<paramref name="StampsCopies"/>: prints and badges) instead of
/// taking it off, and the colour slot its stickers usually use. Every slot holds as many
/// stickers as you like, bottom to top: a gallery click always adds.
/// </summary>
/// <param name="SharesColor">
/// A hair piece's (docs: modular hair): art tagged with this colour slot ("hair"), worn in
/// this slot, is coloured from the slot's own key (its <see cref="Name"/>, "hairFringe")
/// instead, which falls back to the shared one - so every piece follows the Hair colour
/// until it's given its own. Null for every other slot.
/// </param>
public sealed record StickerSlotInfo(string Name, string Label, BodyRegion Region, int ZOrder, bool StampsCopies, string? ColorSlot, bool IsFace = false,
    string? SharesColor = null);

/// <summary>
/// The standard slots (docs/sticker-system.md §8) - the same static-lookup shape as
/// <see cref="BodyPresets"/>. Any other slot name is allowed and behaves like
/// <see cref="Accessory"/>.
/// </summary>
public static class StickerSlots
{
    public const string Bottom = "bottom";
    public const string Shoes = "shoes";
    public const string Top = "top";
    public const string Print = "print";
    public const string Outer = "outer";
    public const string Eyes = "eyes";
    public const string Nose = "nose";
    public const string Brows = "brows";
    public const string Mouth = "mouth";
    public const string FacialHair = "facialHair";
    public const string Hair = "hair";
    public const string HairBack = "hairBack";
    public const string HairTop = "hairTop";
    public const string HairSides = "hairSides";
    public const string HairExtras = "hairExtras";
    public const string HairFringe = "hairFringe";
    public const string HairStreaks = "hairStreaks";
    public const string Glasses = "glasses";
    public const string Headwear = "headwear";
    public const string Accessory = "accessory";

    public static IReadOnlyList<StickerSlotInfo> All { get; } =
    [
        new(Bottom, "Bottom", BodyRegion.Torso, 10, false, "bottom"),
        new(Shoes, "Shoes", BodyRegion.Foot, 15, false, "shoes"),
        new(Top, "Top", BodyRegion.Torso, 20, false, "top"),
        new(Print, "Prints", BodyRegion.Torso, 25, true, "print"),
        new(Outer, "Outer", BodyRegion.Torso, 30, false, "outer"),
        new(Eyes, "Eyes", BodyRegion.Head, 40, false, "eyes", IsFace: true),
        new(Nose, "Nose", BodyRegion.Head, 42, false, CharacterDefinition.SkinSlot, IsFace: true),
        new(Brows, "Brows", BodyRegion.Head, 44, false, "hair", IsFace: true),
        new(Mouth, "Mouth", BodyRegion.Head, 46, false, null, IsFace: true),
        new(FacialHair, "Facial hair", BodyRegion.Head, 48, false, "hair", IsFace: true),
        new(HairBack, "Back", BodyRegion.Head, 49, false, "hair", SharesColor: "hair"),
        new(Hair, "Hair", BodyRegion.Head, 50, false, "hair"),
        new(HairTop, "Top", BodyRegion.Head, 51, false, "hair", SharesColor: "hair"),
        new(HairSides, "Sides", BodyRegion.Head, 52, false, "hair", SharesColor: "hair"),
        new(HairExtras, "Extras", BodyRegion.Head, 53, false, "hair", SharesColor: "hair"),
        new(HairFringe, "Fringe", BodyRegion.Head, 54, false, "hair", SharesColor: "hair"),
        new(Glasses, "Glasses", BodyRegion.Head, 60, false, "glasses"),
        new(HairStreaks, "Streaks", BodyRegion.Head, 63, true, StreakColor),
        new(Headwear, "Hat", BodyRegion.Head, 70, false, "hat"),
        new(Accessory, "Other", BodyRegion.Torso, 80, true, "accent"),
    ];

    /// <summary>The slot's entry, or <see cref="Accessory"/>'s for a custom slot name.</summary>
    public static StickerSlotInfo Get(string slot) =>
        All.FirstOrDefault(s => s.Name == slot) ?? All.First(s => s.Name == Accessory) with { Name = slot, Label = slot };

    /// <summary>Paint order within a layer: the slot's z-order, custom slots with accessories.</summary>
    public static int ZOrder(string slot) => Get(slot).ZOrder;

    /// <summary>
    /// Paint order of <paramref name="sticker"/> worn in <paramref name="slot"/>: the slot's,
    /// raised over glasses (to <see cref="OverGlassesZOrder"/>) for a sticker that asks for it -
    /// a long fringe over one eye covers that lens too.
    /// </summary>
    public static int ZOrder(string slot, Sticker sticker) =>
        sticker.OverGlasses == true ? Math.Max(ZOrder(slot), OverGlassesZOrder) : ZOrder(slot);

    /// <summary>Where a sticker that's <see cref="Sticker.OverGlasses"/> paints: over glasses (60), under streaks (63) and hats (70).</summary>
    public const int OverGlassesZOrder = 62;

    /// <summary>The five pieces hair is built from (docs: modular hair), in the Hair flyout's order.</summary>
    public static IReadOnlyList<string> HairPieces { get; } = [HairTop, HairFringe, HairSides, HairBack, HairExtras];

    /// <summary>Every slot that holds a hairdo: the whole-hairstyle <see cref="Hair"/> slot and the <see cref="HairPieces"/> - what a hairstyle preset replaces. Streaks aren't among them: they stay on.</summary>
    public static IReadOnlyList<string> Hairdo { get; } = [Hair, .. HairPieces];

    /// <summary>Whether <paramref name="slot"/> holds hair of any kind: a whole hairstyle, a piece or a streak.</summary>
    public static bool IsHair(string slot) => slot == Hair || slot == HairStreaks || HairPieces.Contains(slot);

    /// <summary>The colour slot streak art is tagged with (<c>class="slot-streak"</c>); each worn streak is coloured under its own key (<see cref="StreakColorKey"/>).</summary>
    public const string StreakColor = "streak";

    /// <summary>The colour key one worn streak is coloured under - each streak its own colour: "streak-&lt;id&gt;".</summary>
    public static string StreakColorKey(StickerId id) => StreakColor + "-" + id.Value;

    /// <summary>Whether <paramref name="key"/> is one streak's own colour key (<see cref="StreakColorKey"/>).</summary>
    public static bool IsStreakColorKey(string key) => key.StartsWith(StreakColor + "-", StringComparison.Ordinal);

    /// <summary>
    /// The colour key an element tagged <paramref name="tagged"/> (its <c>slot-*</c> class) is
    /// coloured from, on sticker <paramref name="id"/> worn in <paramref name="wornSlot"/>: a hair
    /// piece's own key for its shared colour (<see cref="StickerSlotInfo.SharesColor"/>), a
    /// streak's own key for <see cref="StreakColor"/>, else the tag itself.
    /// </summary>
    public static string? ColorKey(string wornSlot, StickerId id, string? tagged)
    {
        if (tagged is null)
            return null;
        if (wornSlot == HairStreaks && tagged == StreakColor)
            return StreakColorKey(id);
        return Get(wornSlot).SharesColor == tagged ? wornSlot : tagged;
    }

    /// <summary>
    /// The colour and expression key for a sticker restricted to one side of a symmetric slot
    /// (docs/sticker-system.md §21: split eyes) - "eyesLeft", "eyesRight".
    /// </summary>
    public static string SidedSlot(string slot, LimbSide side) => slot + (side == LimbSide.Left ? "Left" : "Right");

    public const string EyesLeft = "eyesLeft";
    public const string EyesRight = "eyesRight";
}
