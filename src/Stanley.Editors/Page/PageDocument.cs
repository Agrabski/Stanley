using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>Whose title page a page is, if it's one (Insert › Title page).</summary>
public enum TitlePageScope
{
    /// <summary>An ordinary page.</summary>
    None,

    /// <summary>The comic's title page, kept in the project folder (<c>title-page/</c>): every issue without one of its own opens with it.</summary>
    Comic,

    /// <summary>One issue's own title page, among its pages, shown instead of the comic's.</summary>
    Issue
}

/// <param name="TitlePage">Whether this is a title page, and whose (<see cref="Page.TitlePage"/>): Insert › Title page redoes it rather than adding another.</param>
public sealed record PageDocument(
    IReadOnlyList<PanelId> PanelOrder,
    IReadOnlyDictionary<PanelId, Panel> Panels,
    bool LayoutLocked = false,
    TitlePageScope TitlePage = TitlePageScope.None)
{
    public bool IsTitlePage => TitlePage != TitlePageScope.None;
}
