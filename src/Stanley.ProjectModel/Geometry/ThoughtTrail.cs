namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// A thought cloud's trail: 2-3 small circles, shrinking towards <see cref="Target"/>, leading
/// from the cloud's own outline (<see cref="AttachmentT"/>, the same 0-1 ring fraction
/// <c>BubbleTail.AttachmentT</c> uses) towards whoever is thinking. One per cloud rather than a
/// list - unlike a bubble's tails, a cloud only ever needs one - so it's removed and re-added
/// rather than indexed.
/// </summary>
public sealed record ThoughtTrail(double AttachmentT, Point2D Target);
