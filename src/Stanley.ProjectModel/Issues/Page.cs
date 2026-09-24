using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// <c>issues/&lt;issueId&gt;/pages/&lt;id&gt;-slug/page.json</c>. Pages are fixed print
/// size, defaulted from the series manifest and overridable per page.
/// <see cref="PanelIds"/> is both z-order and reading order (left-to-right only for
/// now; a direction flag could be added later without a schema break).
/// </summary>
/// <param name="Label">Optional display label ("Splash", "Page 3") used only for the folder slug; a page's real order comes from its issue's <c>PageIds</c> array.</param>
public sealed record Page(PageId Id, string? Label, PageTrim? TrimOverride, IReadOnlyList<PanelId> PanelIds);
