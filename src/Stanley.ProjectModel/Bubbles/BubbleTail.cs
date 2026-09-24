using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Bubbles;

/// <summary>
/// A single tail: where it leaves the outline (<see cref="AttachmentT"/>, a 0-1
/// fraction along the ring) and where it points (<see cref="Target"/>, a free point in
/// the bubble's coordinate space). A <see cref="Bubble"/> can hold any number of
/// these; each is rendered as its own polygon and unioned independently
/// (<c>Stanley.Rendering</c>), which is why adding another tail needs no special case.
/// </summary>
public sealed record BubbleTail(double AttachmentT, Point2D Target, TailKind Kind);
