namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// A bubble's freeform outline: the same anchor-ring model <see cref="PanelShape"/>
/// uses, kept as its own named type for domain clarity even though the representation
/// (and its math, in <see cref="AnchorRing"/>) is identical.
/// </summary>
public sealed record BubbleShape(IReadOnlyList<ShapeAnchor> Anchors);
