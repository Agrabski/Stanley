using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>A repeating motif laid over a colour slot's colour (docs/sticker-system.md §9.2). All but <see cref="Tile"/> are generated in code.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PatternKind>))]
public enum PatternKind
{
    Stripes,
    Pinstripes,
    Checks,
    Plaid,
    Dots,
    Chevron,

    /// <summary>A drawn tile (SVG or PNG) from the character's <c>patterns/</c> folder.</summary>
    Tile
}

/// <summary>A greyscale surface multiplied over the colour and pattern, so it survives any recolour. All but <see cref="Tile"/> are procedural.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<TextureKind>))]
public enum TextureKind
{
    Denim,
    Knit,
    Corduroy,
    Wool,
    Leather,
    Canvas,
    Felt,

    /// <summary>A greyscale PNG tile from the character's <c>patterns/</c> folder.</summary>
    Tile
}

/// <summary>
/// A pattern: the slot's colour is the ground, <paramref name="Colors"/> are the pattern's
/// own (one or two). <paramref name="Size"/> is one repeat as a fraction of the character's
/// height (default <see cref="DefaultSize"/>), <paramref name="Angle"/> in degrees,
/// <paramref name="Weight"/> the stripe width or dot size as a fraction of one repeat.
/// </summary>
/// <param name="Tile">For <see cref="PatternKind.Tile"/>: the tile's file name in the character's <c>patterns/</c> folder.</param>
public sealed record PatternFill(PatternKind Kind, IReadOnlyList<ColorValue> Colors, double? Size = null, double? Angle = null, double? Weight = null, string? Tile = null)
{
    public const double DefaultSize = 0.05;
}

/// <summary>A texture at <paramref name="Strength"/> (0-1, default <see cref="DefaultStrength"/>), one repeat <paramref name="Size"/> (fraction of height).</summary>
public sealed record TextureFill(TextureKind Kind, double? Strength = null, double? Size = null, string? Tile = null)
{
    public const double DefaultStrength = 0.5;
    public const double DefaultSize = 0.03;
}

/// <summary>What a colour slot is filled with besides its colour: an optional pattern and an optional texture.</summary>
public sealed record Fabric(PatternFill? Pattern = null, TextureFill? Texture = null)
{
    public bool IsPlain => Pattern is null && Texture is null;
}
