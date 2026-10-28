using Avalonia.Media;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.StickerLibrary;

namespace Stanley.Editors;

/// <summary>One creature preset in the Shapes tab's gallery, previewed as this character (its colours and what it wears) built that way.</summary>
public sealed record CreaturePresetChoice(CreaturePreset Preset, CharacterDefinition Preview, bool IsCurrent)
{
    public string Name => Preset.Name;

    public string Tip => IsCurrent
        ? $"{Name} - built this way now; click a part on the stage to change it"
        : $"Build them as {Name} - colours and what they wear stay; every part stays editable";
}

/// <summary>
/// One of the four parts on the Shapes tab: a click picks it for the Module tab, or - when
/// the character hasn't got it - puts the library's default back on.
/// </summary>
/// <param name="Icon">What the button draws: the part's own outline (flipped as it's worn), a limb's tube, or nothing for a missing part.</param>
/// <param name="Aspect">The icon's width over its height.</param>
public sealed record ModulePartChoice(ModulePart Part, bool IsPresent, bool IsSelected, ModuleShape? Icon, double Aspect, IBrush Fill)
{
    public string Label => ModuleNames.Title(Part);

    public bool IsMissing => !IsPresent;

    public string Tip => IsPresent
        ? IsSelected ? $"The {Part.Noun()} - picked; change {(Part.IsLimb() ? "them" : "it")} on the {Label} tab" : $"Pick the {Part.Noun()} to change {(Part.IsLimb() ? "them" : "it")} (or click {(Part.IsLimb() ? "them" : "it")} on the character)"
        : $"Add {(Part.IsLimb() ? "" : "a ")}{Part.Noun()} - the library's default, to change as you like";
}

/// <summary>
/// A shape gallery on the Module tab - a head's or torso's outline, a limb's hand or foot, or
/// one of its pieces: the current shape (for the dropdown's face) and the choices by category.
/// </summary>
/// <param name="Label">The gallery's name: "Shape", "Hand", "Foot", "Upper", "Lower".</param>
/// <param name="Current">The current shape's name, "Round" / "Capsule" for none, or "Your own" for an edited one.</param>
/// <param name="Pick">What a choice's click runs, with the choice.</param>
public sealed record ModuleShapeGallery(string Label, string Current, ModuleShape? CurrentShape, double Aspect, IBrush Fill, IReadOnlyList<ModuleShapeSection> Sections, string Tip,
    System.Windows.Input.ICommand Pick);

/// <summary>One category of a shape gallery: Organic, Geometric or Abstract, or the other part's shapes.</summary>
public sealed record ModuleShapeSection(string Label, IReadOnlyList<ModuleShapeChoice> Choices);

/// <summary>
/// A shape on offer: a library shape (<paramref name="Library"/>), or none - a limb's own
/// round end or a plain capsule piece. <paramref name="Icon"/> is what the button draws.
/// </summary>
public sealed record ModuleShapeChoice(string Label, ModulePart Part, LimbPiece? Piece, LibraryShape? Library, ModuleShape Icon, double Aspect, IBrush Fill, bool IsCurrent, string Tip);

/// <summary>A limb style on the Module tab (Tube, Taper, Pieces, Stick).</summary>
public sealed record LimbStyleChoice(LimbStyle Style, bool IsCurrent)
{
    public string Label => Style.ToString();

    public string Tip => Style switch
    {
        LimbStyle.Tube => "A round tube, as thick all the way",
        LimbStyle.Taper => "A tube thinning towards the end - set how much with Taper",
        LimbStyle.Pieces => "Two pieces, one each side of the bend - pick their shapes",
        _ => "A thin line, like a stick figure's"
    };
}

/// <summary>How parts and their colours are named to the user.</summary>
internal static class ModuleNames
{
    public static string Title(ModulePart part) => part switch
    {
        ModulePart.Head => "Head",
        ModulePart.Torso => "Torso",
        ModulePart.Arms => "Arms",
        _ => "Legs"
    };

    /// <summary>The colour slot a part gets when it's given a colour of its own (docs/shape-characters.md §4.1).</summary>
    public static string ColorSlot(ModulePart part) => "body" + Title(part);

    /// <summary>The part a colour slot of <see cref="ColorSlot"/>'s colours, or null.</summary>
    public static ModulePart? PartOfColorSlot(string slot) =>
        ModuleParts.All.Cast<ModulePart?>().FirstOrDefault(p => ColorSlot(p!.Value) == slot);

    public static IBrush Brush(ColorValue color) => new SolidColorBrush(Color.Parse(ColorSwatchChoice.ColorHex(color)));
}
