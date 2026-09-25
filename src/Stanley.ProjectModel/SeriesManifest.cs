using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel;

/// <summary>
/// The project root's <c>stanley.json</c>: a whole comic series (not a single issue).
/// <see cref="IssueIds"/> is the explicit storage order (and default reading order) of
/// issues; number/title live on each <see cref="Issues.Issue"/> instead, so a #0 preview
/// or a #1.5 annual doesn't force renumbering.
/// </summary>
/// <param name="Format">What the comic was set up as (a newspaper strip, a webcomic...); absent for a printed comic book, and in projects from before formats existed.</param>
public sealed record SeriesManifest(string Title, PageTrim DefaultPageTrim, IReadOnlyList<IssueId> IssueIds, ComicFormat? Format = null);
