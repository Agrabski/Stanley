using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>Pulls a part out of its region's layer: to the very back (the back of the hair, a cape) or the very front.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PartDepth>))]
public enum PartDepth
{
    Back,
    Front
}

/// <summary>How a part combines with the rest of its sticker. The default (absent) paints.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PartBlend>))]
public enum PartBlend
{
    /// <summary>Subtracts from the sticker's other parts in the same layer, so what's underneath shows through: a V-neck, an open jacket.</summary>
    Cut
}

/// <summary>What a part is clipped to. The default (absent) is nothing.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PartClip>))]
public enum PartClip
{
    /// <summary>The body's skin (tattoos, face paint).</summary>
    Body,

    /// <summary>The sticker's own cover parts (stripes that must never spill past the shirt).</summary>
    Sticker,

    /// <summary>
    /// Other worn stickers' cover parts in the same layer - the garment underneath (a print
    /// stays on the shirt, not the skin past its edge). Shows unclipped if nothing is worn
    /// there, so a print on bare skin (a tattoo-style use) still draws.
    /// </summary>
    Clothes
}

/// <summary>How drawn art is mapped from its template onto the character's region.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ArtMapping>))]
public enum ArtMapping
{
    /// <summary>Rigid: placed and scaled at the layer's centre, keeping its shape (eyes, logos, buttons).</summary>
    Pin,

    /// <summary>Every point goes through the region mapping, so the art hugs the outline (hair, hats, prints).</summary>
    Warp
}

/// <summary>
/// A part generated from the body itself: <paramref name="From"/> to <paramref name="To"/>
/// of its region (0-1, e.g. the arm from the shoulder to 40% of the way to the wrist),
/// grown by <paramref name="Ease"/> (a fraction of the character's own height; default
/// <see cref="DefaultEase"/>), filled with the colour slot <paramref name="Color"/>.
/// </summary>
/// <param name="Flare">Skirt only: extra width at the hem, as a fraction of the width.</param>
public sealed record PartCover(string Color, double From, double To, double? Ease = null, double? Flare = null)
{
    public const double DefaultEase = 0.008;

    public double EaseOrDefault => Ease ?? DefaultEase;
}

/// <summary>
/// A part drawn as art: the SVG (or PNG) layer named after the part, in the sticker's
/// variant and view files, mapped from its template onto the region. <paramref name="Offset"/>,
/// <paramref name="Scale"/> and <paramref name="Rotation"/> are adjustments in template units,
/// applied before mapping (what the on-canvas handles write).
/// </summary>
/// <param name="KeepReadable">Pin only: un-mirror the part about its own centre when the placement is mirrored, so text never reads backwards.</param>
public sealed record PartArt(ArtMapping Mapping, Point2D? Offset = null, double? Scale = null, double? Rotation = null, bool? KeepReadable = null);

/// <summary>
/// A part drawn as typed text instead of art (docs/sticker-system.md, prints): what the user
/// typed, in the colour slot <paramref name="Color"/>. It needs no art files - the part's
/// <see cref="PartArt"/> still carries its Pin placement (<c>offset</c>, <c>scale</c>,
/// <c>rotation</c>, <c>keepReadable</c>), so it drags, resizes and turns the same way drawn
/// art does.
/// </summary>
/// <param name="FontFamily">null: Stanley's default lettering font. Emoji and symbols it lacks fall back to whatever installed font has them, character by character.</param>
public sealed record PartText(string Text, string Color = "print", string? FontFamily = null, bool Bold = true);

/// <summary>
/// One piece of a sticker, on one body region: exactly one of <paramref name="Cover"/>
/// (generated from the body) or <paramref name="Art"/> (drawn, or - with <paramref name="Text"/>
/// too - typed). The rest are optional and absent by default, so files stay sparse.
/// </summary>
/// <param name="Side">Limb regions only: just this side; absent means both.</param>
/// <param name="Text">Only with <paramref name="Art"/>: typed instead of drawn (a print).</param>
/// <param name="Variants">Only in these of the sticker's variants (a hood's "up" pieces, a cap's brim behind the head); absent means all of them.</param>
public sealed record StickerPart(
    string Name,
    BodyRegion Region,
    PartCover? Cover = null,
    PartArt? Art = null,
    LimbSide? Side = null,
    PartDepth? Depth = null,
    PartBlend? Blend = null,
    PartClip? Clip = null,
    PartText? Text = null,
    IReadOnlyList<string>? Variants = null)
{
    /// <summary>Whether the part is drawn when its sticker shows <paramref name="variant"/>.</summary>
    public bool AppliesTo(string variant) => Variants is not { Count: > 0 } only || only.Contains(variant);
}

/// <summary>
/// <c>characters/&lt;characterId&gt;-slug/stickers/&lt;id&gt;-slug/sticker.json</c> - something a
/// character wears in a slot: hair, eyes, a T-shirt, a watch (docs/sticker-system.md).
/// Made of <see cref="Parts"/>; drawn parts' art lives beside it in
/// <c>variants/&lt;variant&gt;/&lt;view&gt;.svg</c> (or <c>.png</c>).
/// </summary>
/// <param name="Slot">What it is (<see cref="StickerSlots"/>): decides z-order and whether several stack. Any other name behaves like <c>accessory</c>.</param>
/// <param name="Colors">The colour slots the sticker uses, each with its default colour - a fallback under the character's own choice.</param>
/// <param name="Variants">The variant folders; the first is the fallback. Picked per slot by the pose's expression, else per sticker by the style the character wears it in.</param>
/// <param name="Source">"library:&lt;key&gt;" while this is an unmodified copy of a library sticker (tidied away on save when nothing wears it); absent once it's the user's own.</param>
/// <param name="Fabrics">Default pattern/texture per colour slot (the library's jeans come in denim).</param>
public sealed record Sticker(
    StickerId Id,
    string Name,
    string Slot,
    IReadOnlyList<StickerPart> Parts,
    SortedDictionary<string, ColorValue> Colors,
    IReadOnlyList<string> Variants,
    string? Source = null,
    SortedDictionary<string, Fabric>? Fabrics = null)
{
    public const string DefaultVariant = "default";

    /// <summary>A sticker read from a hand-edited file with missing lists gets empty ones instead of nulls.</summary>
    public Sticker Normalized() =>
        Parts is not null && Colors is not null && Variants is { Count: > 0 }
            ? this
            : this with
            {
                Parts = Parts ?? [],
                Colors = Colors ?? new SortedDictionary<string, ColorValue>(),
                Variants = Variants is { Count: > 0 } ? Variants : [DefaultVariant]
            };

    public bool IsFromLibrary => Source is not null;

    /// <summary>
    /// The variant it shows worn in <paramref name="slot"/>: the expression's for that slot if
    /// it has that one (a face), else the style the character wears it in
    /// (<paramref name="chosen"/>, docs/sticker-system.md §20) if it has that one, else
    /// "neutral" if it has one, else its first.
    /// </summary>
    public string VariantFor(string slot, IReadOnlyDictionary<string, string>? expression, string? chosen = null)
    {
        if (expression is not null && expression.TryGetValue(slot, out var wanted) && Variants.Contains(wanted))
            return wanted;
        if (chosen is not null && Variants.Contains(chosen))
            return chosen;
        return Variants.Contains("neutral") ? "neutral" : Variants.Count > 0 ? Variants[0] : DefaultVariant;
    }
}
