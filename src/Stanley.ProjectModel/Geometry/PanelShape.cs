using System.Text.Json.Serialization;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Geometry;

/// <summary>Mirrors <c>Stanley.Bubbles.AnchorHandleType</c>; kept independent since this project has no dependency on Stanley.Bubbles.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AnchorHandleKind>))]
public enum AnchorHandleKind
{
    Smooth,
    Corner
}

/// <summary>One point on a <see cref="PanelShape"/>'s anchor ring, with absolute bezier handle positions.</summary>
public sealed record ShapeAnchor(Point2D Point, Point2D InHandle, Point2D OutHandle, AnchorHandleKind HandleKind);

/// <summary>
/// A panel's freeform outline on the page canvas: an ordered ring of anchors, the same
/// anchor-ring model <c>BubbleOutline</c> uses for speech bubbles, reused here as its
/// own type rather than a shared dependency.
/// </summary>
public sealed record PanelShape(IReadOnlyList<ShapeAnchor> Anchors);
