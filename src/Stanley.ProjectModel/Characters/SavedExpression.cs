namespace Stanley.ProjectModel.Characters;

/// <summary>
/// A face saved on a character to use again in any panel (the page's Face dropdown › Save
/// this face): its name, and the variant each face slot shows - neutral ones left out, as
/// in <see cref="Poses.PoseData.Expression"/>.
/// </summary>
public sealed record SavedExpression(string Name, SortedDictionary<string, string> Variants);
