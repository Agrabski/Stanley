using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>A pan/zoom window into a background revision's art.</summary>
public sealed record BackgroundCrop(double X, double Y, double Width, double Height);

/// <summary>
/// What fills a panel behind everything in it: a flat <see cref="ColorBackground"/>, a
/// top-to-bottom <see cref="GradientBackground"/> (a sky), a one-off
/// <see cref="InlineBackground"/> image, or a <see cref="LibraryBackground"/> reference
/// into the shared <c>backgrounds/</c> library. Composability is available, not
/// mandatory - a panel can also have no background at all (plain paper).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ColorBackground), "color")]
[JsonDerivedType(typeof(GradientBackground), "gradient")]
[JsonDerivedType(typeof(InlineBackground), "inline")]
[JsonDerivedType(typeof(LibraryBackground), "library")]
public abstract record PanelBackground;

public sealed record ColorBackground(ColorValue Color) : PanelBackground;

/// <summary>Blends from <paramref name="Top"/> at the panel's top edge to <paramref name="Bottom"/> at its bottom edge.</summary>
public sealed record GradientBackground(ColorValue Top, ColorValue Bottom) : PanelBackground;

/// <summary>
/// A picture filling the panel (scaled to cover it, centred, the overflow cropped).
/// <paramref name="ArtFileName"/> is a one-off image under the issue's <c>art/</c> folder
/// (LFS), not a cross-referenced id - named after its content (<see cref="Storage.IssueArt"/>).
/// </summary>
public sealed record InlineBackground(string ArtFileName) : PanelBackground;

public sealed record LibraryBackground(BackgroundId BackgroundId, BackgroundRevisionId RevisionId, BackgroundCrop Crop) : PanelBackground;
