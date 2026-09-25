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
public abstract record PanelElement(ElementId Id, ElementLayer Layer);

/// <summary>How a drawn shape is painted; a null colour means "none" (no fill, no outline).</summary>
/// <param name="StrokeWidthMm">Outline thickness in page millimetres.</param>
public sealed record ShapeStyle(ColorValue? Stroke, ColorValue? Fill, double StrokeWidthMm);

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
/// How a piece of text is lettered. A null colour means "none": no letter
/// <paramref name="Outline"/> (the white edge around a sound effect), no box behind the
/// text (<paramref name="BoxFill"/>) or around it (<paramref name="BoxStroke"/>) - a
/// caption is text with both.
/// </summary>
/// <param name="FontSizeMm">Letter size in page millimetres (3.5mm is the usual ~10pt dialogue size).</param>
public sealed record TextStyle(
    double FontSizeMm,
    ColorValue Color,
    bool Bold = false,
    bool Italic = false,
    TextAlign Align = TextAlign.Center,
    ColorValue? Outline = null,
    ColorValue? BoxFill = null,
    ColorValue? BoxStroke = null);

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
