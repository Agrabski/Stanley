using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editors.Tests;

public class PageNavigatorViewModelTests
{
    private static (EditorHistory History, PageNavigatorViewModel Navigator) NewNavigator()
    {
        var history = new EditorHistory();
        return (history, new PageNavigatorViewModel(history, ComicProject.CreateNew().Pages));
    }

    [Fact]
    public void AddPage_InsertsAfterTheCurrentPage_ShowsItAndNumbersEveryPage()
    {
        var (_, navigator) = NewNavigator();
        var first = navigator.CurrentPage;
        PageItem? shown = null;
        navigator.CurrentPageChanged += p => shown = p;

        var second = navigator.AddPageAfter(first);
        navigator.CurrentPage = first;
        var inserted = navigator.AddPageAfter(first);

        Assert.Equal([first, inserted, second], navigator.Pages);
        Assert.Same(inserted, navigator.CurrentPage);
        Assert.Same(inserted, shown);
        Assert.Equal([1, 2, 3], navigator.Pages.Select(p => p.Number));
        Assert.Equal("Page 3", second.Editor.Title);
    }

    [Fact]
    public void PageOperations_AreUndoableThroughTheSharedHistory()
    {
        var (history, navigator) = NewNavigator();
        var first = navigator.CurrentPage;
        var second = navigator.AddPageAfter(first);

        navigator.DeletePage(second);
        Assert.Single(navigator.Pages);
        Assert.Same(first, navigator.CurrentPage);

        history.Undo();
        Assert.Equal([first, second], navigator.Pages);
        Assert.Same(second, navigator.CurrentPage);

        history.Undo();
        Assert.Equal([first], navigator.Pages);
    }

    [Fact]
    public void UndoingAnEditOnAnotherPage_SwitchesToThatPage()
    {
        var (history, navigator) = NewNavigator();
        var first = navigator.CurrentPage;
        first.Editor.CreateBubble(first.Editor.Working.PanelOrder[0], new Point2D(50, 50));
        var second = navigator.AddPageAfter(first);
        navigator.CurrentPage = second;
        history.Undo(); // undoes "add page" - back on page 1 anyway
        navigator.AddPageAfter(first);
        navigator.CurrentPage = navigator.Pages[1];

        history.Undo(); // "add page" again
        history.Undo(); // the bubble on page 1

        Assert.Same(first, navigator.CurrentPage);
        Assert.Empty(first.Editor.Working.Panels.Values.SelectMany(p => p.Bubbles));
    }

    [Fact]
    public void Reveal_OnTheAlreadyCurrentPage_StillRaisesCurrentPageChanged()
    {
        // Simply re-setting CurrentPage to itself wouldn't fire the change event (nothing
        // changed) - Reveal exists so bringing the current page back (e.g. after a
        // character's editor was shown over it) always works.
        var (_, navigator) = NewNavigator();
        var current = navigator.CurrentPage;
        var seen = new List<PageItem>();
        navigator.CurrentPageChanged += p => seen.Add(p);

        navigator.Reveal(current);

        Assert.Equal([current], seen);
    }

    [Fact]
    public void MovePage_Reorders_AndDeleteNeverRemovesTheLastPage()
    {
        var (_, navigator) = NewNavigator();
        var a = navigator.CurrentPage;
        var b = navigator.AddPageAfter(a);
        var c = navigator.AddPageAfter(b);

        navigator.MovePage(2, 0);
        Assert.Equal([c, a, b], navigator.Pages);
        Assert.Equal(1, c.Number);

        navigator.DeletePage(a);
        navigator.DeletePage(b);
        navigator.DeletePage(c);
        Assert.Single(navigator.Pages);
        Assert.False(navigator.DeletePageCommand.CanExecute(null));
    }

    [Fact]
    public void DuplicatePage_CopiesContentWithFreshPanelIds()
    {
        var (_, navigator) = NewNavigator();
        var original = navigator.CurrentPage;
        original.Editor.CreateBubble(original.Editor.Working.PanelOrder[0], new Point2D(50, 50));

        var copy = navigator.DuplicatePage(original);

        Assert.Equal(1, navigator.Pages.IndexOf(copy));
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Empty(copy.Editor.Working.PanelOrder.Intersect(original.Editor.Working.PanelOrder));
        Assert.Single(copy.Editor.Working.Panels.Values.SelectMany(p => p.Bubbles));
    }
}
