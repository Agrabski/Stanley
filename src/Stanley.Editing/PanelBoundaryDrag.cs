using Stanley.ProjectModel.Ids;

namespace Stanley.Editing;

public enum BoundaryOrientation
{
    /// <summary>A boundary running top-to-bottom; dragging it moves panels' X position.</summary>
    Vertical,

    /// <summary>A boundary running left-to-right; dragging it moves panels' Y position.</summary>
    Horizontal
}

/// <summary>
/// Which panels share a boundary, identified explicitly by the caller (the page editor
/// already knows its own grid) rather than inferred from geometry: <see cref="PanelsBefore"/>
/// are the panels whose right (or bottom) edge this boundary is, <see cref="PanelsAfter"/>
/// the panels whose left (or top) edge it is.
/// </summary>
public sealed record PanelBoundaryDrag(
    BoundaryOrientation Orientation,
    IReadOnlyList<PanelId> PanelsBefore,
    IReadOnlyList<PanelId> PanelsAfter);
