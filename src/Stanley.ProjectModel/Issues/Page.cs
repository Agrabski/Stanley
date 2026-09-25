using System.Text.Json.Serialization;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// <c>issues/&lt;issueId&gt;/pages/&lt;id&gt;-slug/page.json</c>. Pages are fixed print
/// size, defaulted from the series manifest and overridable per page.
/// <see cref="PanelIds"/> is both z-order and reading order (left-to-right only for
/// now; a direction flag could be added later without a schema break).
/// </summary>
/// <param name="Label">Optional display label ("Splash", "Page 3") used only for the folder slug; a page's real order comes from its issue's <c>PageIds</c> array.</param>
/// <param name="LayoutLocked">When true, panels on this page can't be moved, resized, split, deleted or re-tiled from a layout preset - a Word-style "protect this layout" switch. Bubbles and characters are unaffected.</param>
/// <param name="TitlePage">A title page (Insert › Title page), which picking another design redoes in place, like Word's cover page: the comic's own under <c>title-page/</c>, or - among an issue's pages - that issue's instead of the comic's. Written only when set, so older page files read unchanged.</param>
public sealed record Page(
    PageId Id,
    string? Label,
    PageTrim? TrimOverride,
    IReadOnlyList<PanelId> PanelIds,
    bool LayoutLocked = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool TitlePage = false);
