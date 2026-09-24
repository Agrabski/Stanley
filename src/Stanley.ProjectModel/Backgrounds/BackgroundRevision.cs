using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Backgrounds;

/// <summary>
/// One back or front (occlusion) layer of a background revision: an optional flat
/// backdrop plus an ordered (= z-order) list of prop placements on top of it. A layer
/// with no placements and just a backdrop behaves exactly like a single flat painted
/// image, so nothing is lost for a background that's just one painting.
/// </summary>
public sealed record BackgroundLayer(BackdropId? BackdropId, IReadOnlyList<PropPlacement> PropPlacements);

/// <summary>
/// <c>backgrounds/&lt;backgroundId&gt;/revisions/&lt;id&gt;-slug.json</c> - a named,
/// project-level snapshot of a location (e.g. "Before Renovation"). Render order is
/// always back layer -&gt; character stickers -&gt; front layer -&gt; bubbles.
/// </summary>
public sealed record BackgroundRevision(
    BackgroundRevisionId Id,
    BackgroundId BackgroundId,
    string Name,
    BackgroundLayer Back,
    BackgroundLayer? Front);
