using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Backgrounds;

/// <summary>One flat base image under a background's <c>backdrops/</c> folder, named <c>&lt;id&gt;-slug.svg</c> by convention.</summary>
public sealed record BackdropAsset(BackdropId Id, string Name);

/// <summary>
/// <c>backgrounds/&lt;id&gt;-slug/background.json</c> - a reusable, project-level
/// location. <see cref="Backdrops"/> is the catalogue of flat base images a
/// <see cref="BackgroundRevision"/>'s layers can reference by id; the layer/prop
/// composition itself lives on the revision, not here.
/// </summary>
public sealed record Background(BackgroundId Id, string Name, IReadOnlyList<BackdropAsset> Backdrops);
