namespace Stanley.ProjectModel.Characters;

/// <summary>
/// A standard sticker slot: where a new sticker for it goes by default, its z-order
/// within its layer, whether a click on a placed design already worn there stamps
/// another copy of it (<paramref name="StampsCopies"/>: prints and badges) instead of
/// taking it off, and the colour slot its stickers usually use. Every slot holds as many
/// stickers as you like, bottom to top: a gallery click always adds.
/// </summary>
public sealed record StickerSlotInfo(string Name, string Label, BodyRegion Region, int ZOrder, bool StampsCopies, string? ColorSlot, bool IsFace = false);

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
        new(Hair, "Hair", BodyRegion.Head, 50, false, "hair"),
        new(Glasses, "Glasses", BodyRegion.Head, 60, false, "glasses"),
        new(Headwear, "Hat", BodyRegion.Head, 70, false, "hat"),
        new(Accessory, "Other", BodyRegion.Torso, 80, true, "accent"),
    ];

    /// <summary>The slot's entry, or <see cref="Accessory"/>'s for a custom slot name.</summary>
    public static StickerSlotInfo Get(string slot) =>
        All.FirstOrDefault(s => s.Name == slot) ?? All.First(s => s.Name == Accessory) with { Name = slot, Label = slot };

    /// <summary>Paint order within a layer: the slot's z-order, custom slots with accessories.</summary>
    public static int ZOrder(string slot) => Get(slot).ZOrder;

    /// <summary>
    /// The colour and expression key for a sticker restricted to one side of a symmetric slot
    /// (docs/sticker-system.md §21: split eyes) - "eyesLeft", "eyesRight".
    /// </summary>
    public static string SidedSlot(string slot, LimbSide side) => slot + (side == LimbSide.Left ? "Left" : "Right");

    public const string EyesLeft = "eyesLeft";
    public const string EyesRight = "eyesRight";
}
