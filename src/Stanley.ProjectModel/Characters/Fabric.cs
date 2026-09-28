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
    Tile,

    // Dyes (docs: modular hair): laid once across each drawn part, not repeated - "the
    // ends" means the ends of that piece, however long it is. See PatternFill.IsDye.

    /// <summary>Uneven stripes running down the part, in <see cref="PatternFill.Colors"/>[0]: <see cref="PatternFill.Size"/> spacing, <see cref="PatternFill.Weight"/> width.</summary>
    Streaks,

    /// <summary>The ends of the part in <see cref="PatternFill.Colors"/>[0] (dip-dye): <see cref="PatternFill.Weight"/> is how far up.</summary>
    Tips,

    /// <summary>The roots of the part in <see cref="PatternFill.Colors"/>[0]: <see cref="PatternFill.Weight"/> is how far down.</summary>
    Roots,

    /// <summary>A gradient from the ground colour at the top to <see cref="PatternFill.Colors"/>[0] at the bottom: <see cref="PatternFill.Weight"/> is where it starts.</summary>
    Ombre,

    /// <summary>Bands of every colour in <see cref="PatternFill.Colors"/> (2-7) running down the part, side by side.</summary>
    Rainbow
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

    /// <summary>Whether this is a dye (docs: modular hair) - fitted once across each drawn part instead of repeated in its region's frame.</summary>
    public bool IsDye => IsDyeKind(Kind);

    public static bool IsDyeKind(PatternKind kind) => kind is PatternKind.Streaks or PatternKind.Tips or PatternKind.Roots or PatternKind.Ombre or PatternKind.Rainbow;

    /// <summary>The Rainbow dye's colours when none are picked: red, orange, yellow, green, blue, violet.</summary>
    public static IReadOnlyList<ColorValue> RainbowColors { get; } =
    [
        ColorValue.FromHex("#e53935"), ColorValue.FromHex("#fb8c00"), ColorValue.FromHex("#fdd835"),
        ColorValue.FromHex("#43a047"), ColorValue.FromHex("#1e88e5"), ColorValue.FromHex("#8e24aa"),
    ];
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
