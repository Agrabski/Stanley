using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Backgrounds;

/// <summary>
/// One prop from the shared <c>props/</c> library placed into a background layer.
/// Composing a location means placing props into a revision's back/front layers, the
/// same mental model as placing character instances into a panel.
/// </summary>
public sealed record PropPlacement(
    PropPlacementId Id,
    PropId PropId,
    string VariantName,
    Point2D Position,
    Point2D Scale,
    double RotationDegrees);
