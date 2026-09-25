using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>Which number (if any) a page prints under an issue's <see cref="PageNumbering"/>.</summary>
public static class PageFolios
{
    /// <param name="pageIndex">0-based position of the page in its issue.</param>
    public static PageFolio? For(PageNumbering numbering, int pageIndex)
    {
        if (numbering.Position == PageNumberPosition.None || (pageIndex == 0 && !numbering.NumberFirstPage))
            return null;

        var number = numbering.StartAt + pageIndex;
        // Comics open on a right-hand page: odd numbers sit on the right of the spread.
        return new PageFolio(number.ToString(System.Globalization.CultureInfo.InvariantCulture), numbering.Position, IsRightHandPage: number % 2 != 0);
    }
}

/// <summary>The one place an issue's page numbering lives (the page navigator); page editors read and change it through this.</summary>
public interface IPageNumberingHost
{
    PageNumbering PageNumbering { get; }

    /// <summary>Changes the numbering for every page, as one undoable step.</summary>
    void SetPageNumbering(PageNumbering numbering);

    event Action? PageNumberingChanged;
}

/// <summary>
/// Where the comic's margin and gutter live (the page navigator): one setting every page
/// lays out and snaps with. Changing it is one undoable step.
/// </summary>
public interface IPageSpacingHost
{
    void SetSpacing(Stanley.Editing.PanelGrid spacing);
}

/// <summary>
/// Where an issue's look per character lives (<see cref="ProjectModel.Issues.Issue.CharacterRevisions"/>):
/// what its panels show each character in unless a panel picks its own. Changing it is
/// one undoable step that every page redraws for.
/// </summary>
public interface IIssueLooksHost
{
    IReadOnlyDictionary<ProjectModel.Ids.CharacterId, ProjectModel.Ids.CharacterRevisionId> IssueLooks { get; }

    /// <summary>Sets the issue's look for <paramref name="character"/>; null = the character's default look.</summary>
    void SetIssueLook(ProjectModel.Ids.CharacterId character, ProjectModel.Ids.CharacterRevisionId? look);

    event Action? IssueLooksChanged;
}

/// <summary>A choice in the ribbon's page-number position box.</summary>
public sealed record PageNumberOption(PageNumberPosition Position, string Label)
{
    public static IReadOnlyList<PageNumberOption> All { get; } =
    [
        new(PageNumberPosition.None, "No page numbers"),
        new(PageNumberPosition.BottomCenter, "Bottom, centred"),
        new(PageNumberPosition.BottomOuter, "Bottom, outer corner"),
        new(PageNumberPosition.TopOuter, "Top, outer corner"),
    ];

    public override string ToString() => Label;
}
