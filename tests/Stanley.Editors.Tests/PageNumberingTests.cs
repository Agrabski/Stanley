using Stanley.EditorFramework;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

public class PageNumberingTests
{
    [Fact]
    public void Folios_SkipTheCoverByDefault_AndOddNumbersAreRightHandPages()
    {
        var numbering = new PageNumbering(PageNumberPosition.BottomOuter);

        Assert.Null(PageFolios.For(numbering, 0));
        Assert.Equal(("2", false), (PageFolios.For(numbering, 1)!.Text, PageFolios.For(numbering, 1)!.IsRightHandPage));
        Assert.Equal(("3", true), (PageFolios.For(numbering, 2)!.Text, PageFolios.For(numbering, 2)!.IsRightHandPage));
    }

    [Fact]
    public void Folios_HonourStartAtAndFirstPage_AndOffMeansNone()
    {
        Assert.Equal("10", PageFolios.For(new PageNumbering(PageNumberPosition.BottomCenter, StartAt: 10, NumberFirstPage: true), 0)!.Text);
        Assert.Null(PageFolios.For(PageNumbering.Off, 3));
    }

    [Fact]
    public void Navigator_AppliesNumberingToEveryPage_UndoablyAndThroughAnyPagesRibbonProperties()
    {
        var history = new EditorHistory();
        var navigator = new PageNavigatorViewModel(history, ComicProject.CreateNew().Pages);
        var second = navigator.AddPageAfter(navigator.CurrentPage);
        var third = navigator.AddPageAfter(second);

        // Set from the third page's editor (the ribbon talks to whichever page is shown).
        third.Editor.PageNumberOption = PageNumberOption.All.Single(o => o.Position == PageNumberPosition.BottomCenter);

        Assert.Null(navigator.Pages[0].Editor.Folio);
        Assert.Equal("2", second.Editor.Folio!.Text);
        Assert.Equal(PageNumberPosition.BottomCenter, navigator.Pages[0].Editor.PageNumberOption.Position);

        navigator.MovePage(2, 1);
        Assert.Equal("2", third.Editor.Folio!.Text);
        Assert.Equal("3", second.Editor.Folio!.Text);

        history.Undo(); // move
        history.Undo(); // numbering
        Assert.All(navigator.Pages, p => Assert.Null(p.Editor.Folio));
        Assert.False(third.Editor.PageNumbersEnabled);
    }
}
