using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Poses;

/// <summary>
/// <c>poses/&lt;id&gt;-slug.json</c> - a named, project-level pose library entry ("apply
/// with one click, then adjust"). Shares its data shape with the pose a panel's
/// character instance embeds inline for an unsaved, ad hoc pose.
/// </summary>
public sealed record Pose(PoseId Id, string Name, PoseData Data);
