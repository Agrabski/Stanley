using System.Text.Json.Serialization;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>A pan/zoom window into a background revision's art.</summary>
public sealed record BackgroundCrop(double X, double Y, double Width, double Height);

/// <summary>
/// A panel's background: either a one-off <see cref="InlineBackground"/> image (the
/// default/simple path, no library entry) or a <see cref="LibraryBackground"/> reference
/// into the shared <c>backgrounds/</c> library. Composability is available, not
/// mandatory - a panel can also have no background at all.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(InlineBackground), "inline")]
[JsonDerivedType(typeof(LibraryBackground), "library")]
public abstract record PanelBackground;

/// <summary><paramref name="ArtFileName"/> is a one-off image under the issue's <c>art/</c> folder (LFS), not a cross-referenced id.</summary>
public sealed record InlineBackground(string ArtFileName) : PanelBackground;

public sealed record LibraryBackground(BackgroundId BackgroundId, BackgroundRevisionId RevisionId, BackgroundCrop Crop) : PanelBackground;
