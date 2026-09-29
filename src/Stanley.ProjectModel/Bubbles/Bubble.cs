using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.ProjectModel.Bubbles;

/// <summary>
/// One speech bubble: a bezier outline, a style preset, any number of tails, and its
/// text. Embedded directly in the one panel that ever references it (<see cref="Ids.BubbleId"/>
/// only needs to be stable within that panel), the same way <c>CharacterInstance</c> is.
/// <c>Stanley.Rendering</c> turns this into pixels; <c>Stanley.Editing</c> owns what
/// counts as a valid edit to it (minimum size, non-empty shape, etc.) - this record has
/// no behaviour of its own.
/// <para>
/// The lettering's font is the same set of choices free text has (see <c>TextStyle</c>),
/// every one optional: a bubble that leaves them alone letters in the default font at
/// <see cref="DefaultFontSizePt"/>, upright and centred, and its file says nothing about
/// them. The size is absolute - it never shrinks to fit; a bubble too small for its text
/// grows instead (<c>Stanley.Editing.BubbleEditing.GrowToFit</c>), and one a user then
/// resizes smaller by hand just spills its text past the outline.
/// </para>
/// </summary>
/// <param name="FontFamily">The typeface family (see <c>TextStyle.FontFamily</c>); null is the default lettering font.</param>
/// <param name="FontSizePt">The letters' size in points, as in Word; null is <see cref="DefaultFontSizePt"/>.</param>
/// <param name="Align">How lines line up; null is centred, as dialogue usually is.</param>
/// <param name="Link">Set when the bubble was grouped with other things in its panel (see <see cref="Issues.PanelElement.Link"/>); null otherwise.</param>
public sealed record Bubble(
    BubbleId Id,
    BubbleShape Shape,
    BubbleStylePreset Style,
    IReadOnlyList<BubbleTail> Tails,
    string Text,
    string? FontFamily = null,
    double? FontSizePt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Bold = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Italic = false,
    TextAlign? Align = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GroupLinkId? Link = null)
{
    /// <summary>The usual dialogue size: bubble lettering's size unless one is chosen.</summary>
    public const double DefaultFontSizePt = 10;
}
