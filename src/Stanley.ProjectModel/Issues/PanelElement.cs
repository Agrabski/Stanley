using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Issues;

/// <summary>Which side of a panel's characters an element draws on.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ElementLayer>))]
public enum ElementLayer
{
    /// <summary>Behind the characters: scenery, a sign on a wall.</summary>
    Background,

    /// <summary>In front of the characters (still under the bubbles): a bush they stand behind, a caption, a sound effect.</summary>
    Foreground
}

/// <summary>
/// Something drawn into a panel that isn't a character or a bubble: a shape, a piece of
/// text or a picture. Embedded directly in <see cref="Panel.Elements"/>, whose order is
/// the z-order within each <see cref="ElementLayer"/> (the end of the list is in front);
/// <see cref="Layer"/> puts it behind or in front of the panel's characters. A panel draws
/// its background, its background elements, its characters, its foreground elements and
/// then its bubbles, all clipped to the panel.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(PictureElement), "picture")]
[JsonDerivedType(typeof(SpeedLinesElement), "speedLines")]
public abstract record PanelElement(ElementId Id, ElementLayer Layer);

/// <summary>The pattern a line is drawn in - Word's "Dashes" - each scaled to the line's thickness.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<LineDash>))]
public enum LineDash
{
    Solid,
    RoundDot,
    SquareDot,
    Dash,
    DashDot,
    LongDash,
    LongDashDot
}

/// <summary>How a drawn shape is painted; a null colour means "none" (no fill, no outline).</summary>
/// <param name="StrokeWidthMm">Outline thickness in page millimetres.</param>
/// <param name="Dash">The outline's pattern; absent in files from before dashes, which read as solid.</param>
public sealed record ShapeStyle(ColorValue? Stroke, ColorValue? Fill, double StrokeWidthMm, LineDash Dash = LineDash.Solid);

/// <summary>
/// A drawn shape: the same bezier anchor model panels and bubbles use (see
/// <see cref="AnchorRing"/>), in page millimetres, either <see cref="Closed"/> (a ring,
/// which can be filled) or open (a line, drawn from the first anchor to the last). Freehand
/// strokes, lines, rectangles and ellipses are all just this - the anchors are the escape
/// hatch for any shape at all.
/// </summary>
public sealed record ShapeElement(ElementId Id, ElementLayer Layer, IReadOnlyList<ShapeAnchor> Anchors, bool Closed, ShapeStyle Style)
    : PanelElement(Id, Layer);

[JsonConverter(typeof(CamelCaseEnumConverter<TextAlign>))]
public enum TextAlign
{
    Left,
    Center,
    Right
}

/// <summary>
/// How a piece of text is lettered - the same fill-and-outline controls as Word's Text
/// Fill / Text Outline for the letters and Shape Fill / Shape Outline for their box. A null
/// colour means "none": hollow letters (no <paramref name="Color"/> - only their outline
/// shows), no letter <paramref name="Outline"/> (the edge around a sound effect), no box
/// behind the text (<paramref name="BoxFill"/>) or around it (<paramref name="BoxStroke"/>)
/// - a caption is text with both.
/// </summary>
/// <param name="FontSizePt">Letter size in points, as in Word (10pt is the usual dialogue size); see <see cref="FontPoints"/>.</param>
/// <param name="OutlineWidthMm">The letter outline's thickness; null keeps it in proportion to the letter size.</param>
/// <param name="BoxStrokeWidthMm">The box outline's thickness (a bubble's, by default).</param>
/// <param name="BoxDash">The box outline's pattern.</param>
/// <param name="FontFamily">The typeface's family name - one Stanley bundles or one installed on the computer; null is the default lettering font. A family the computer doesn't have draws in the default until it's installed, and the name is kept.</param>
public sealed record TextStyle(
    double FontSizePt,
    ColorValue? Color,
    bool Bold = false,
    bool Italic = false,
    TextAlign Align = TextAlign.Center,
    ColorValue? Outline = null,
    ColorValue? BoxFill = null,
    ColorValue? BoxStroke = null,
    double? OutlineWidthMm = null,
    double BoxStrokeWidthMm = TextStyle.DefaultBoxStrokeWidthMm,
    LineDash BoxDash = LineDash.Solid,
    string? FontFamily = null)
{
    public const double DefaultBoxStrokeWidthMm = 0.35;

    /// <summary>The letter size in page millimetres, which is what the renderer draws in.</summary>
    [JsonIgnore]
    public double FontSizeMm => FontPoints.ToMm(FontSizePt);
}

/// <summary>
/// Text placed freely in a panel - a caption, a sign, a sound effect: word-wrapped inside
/// <see cref="Bounds"/> (page millimetres), shrunk to fit if it doesn't.
/// </summary>
public sealed record TextElement(ElementId Id, ElementLayer Layer, Rect2D Bounds, string Text, TextStyle Style)
    : PanelElement(Id, Layer);

/// <summary>
/// An imported picture (PNG, JPEG, SVG...) placed in a panel - a painted backdrop, a tree
/// in the foreground - drawn into <see cref="Bounds"/> (page millimetres).
/// <paramref name="ArtFileName"/> is a file in the issue's <c>art/</c> folder, named after
/// its content (<see cref="Storage.IssueArt"/>), the same kind of reference
/// <see cref="InlineBackground"/> makes.
/// </summary>
public sealed record PictureElement(ElementId Id, ElementLayer Layer, Rect2D Bounds, string ArtFileName)
    : PanelElement(Id, Layer);

/// <summary>
/// How a burst of <see cref="SpeedLinesElement"/> draws: filled, tapered wedges radiating
/// out from the focus ellipse's centre - deterministically, so the same style always draws
/// the same lines (see <see cref="Seed"/>).
/// </summary>
/// <param name="Count">How many lines radiate out.</param>
/// <param name="WidthMm">How thick each line is at its far (outer) end, in page millimetres; it tapers to nothing at its inner tip, near the focus.</param>
/// <param name="Jitter">How irregular the lines are - their angle, gap from the focus and thickness - from dead even (0) to noticeably hand-drawn (1).</param>
/// <param name="Seed">Which pseudo-random lines this draws; the same seed always draws the same burst, on every platform. "Shuffle" just changes this.</param>
public sealed record SpeedLinesStyle(ColorValue Color, int Count = 80, double WidthMm = 1.2, double Jitter = 0.5, int Seed = 1);

/// <summary>
/// A comic's classic "speed lines" or "concentration lines": a burst of thin, tapered
/// lines radiating out past the panel's edges from a clear ellipse in the middle
/// (<see cref="Focus"/>, page millimetres) - <see cref="Focus"/>'s centre is the point they
/// radiate from. Its move and resize handles drag <see cref="Focus"/>, the same way any
/// other element's box moves and resizes (<see cref="PanelElements.Bounds"/>).
/// </summary>
public sealed record SpeedLinesElement(ElementId Id, ElementLayer Layer, Rect2D Focus, SpeedLinesStyle Style)
    : PanelElement(Id, Layer);
