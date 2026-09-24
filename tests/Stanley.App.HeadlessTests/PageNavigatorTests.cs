using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>The page navigator side pane: thumbnails, switching pages, reordering - and leaving the ribbon alone.</summary>
[Collection("Page Editor Tests")]
public class PageNavigatorTests
{
    private static (MainWindow Window, PageNavigatorView View, PageNavigatorViewModel Navigator) Open(int pages)
    {
        var window = new MainWindow();
        window.Show();
        var navigator = window.ViewModel.Navigator!;
        while (navigator.Pages.Count < pages)
            navigator.AddPageAfter(navigator.Pages[^1]);
        navigator.CurrentPage = navigator.Pages[0];
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<PageNavigatorView>().Single();
        return (window, view, navigator);
    }

    private static Point CenterOf(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    [Fact]
    public void Navigator_IsASidePaneBelowTheRibbon_WithAThumbnailPerPage()
    {
        var (window, view, _) = Open(pages: 3);

        Assert.DoesNotContain(view, window.RibbonBarControl.GetVisualDescendants());
        var ribbonBottom = window.RibbonBarControl.TranslatePoint(new Point(0, window.RibbonBarControl.Bounds.Height), window)!.Value.Y;
        Assert.True(view.TranslatePoint(new Point(0, 0), window)!.Value.Y >= ribbonBottom);

        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
        Assert.True(view.TranslatePoint(new Point(view.Bounds.Width, 0), window)!.Value.X <= canvas.TranslatePoint(new Point(0, 0), window)!.Value.X,
            "navigator should sit to the left of the page");

        var thumbnails = view.GetVisualDescendants().OfType<PageThumbnail>().ToList();
        Assert.Equal(3, thumbnails.Count);
        Assert.All(thumbnails, t => Assert.True(t.Bounds.Height > t.Bounds.Width, "an A4 thumbnail is portrait"));
    }

    [Fact]
    public void ClickingAThumbnail_ShowsThatPage_AndTheRibbonKeepsFollowingTheEditor()
    {
        var (window, view, navigator) = Open(pages: 3);
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        var target = navigator.Pages[2];

        var point = CenterOf((Control)view.List.ContainerFromIndex(2)!, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(target, navigator.CurrentPage);
        Assert.Same(target.Editor, window.Workspace.ActiveEditor);
        Assert.Same(target.Editor, ribbon.DataContext);
        Assert.Same(target.Editor, window.GetVisualDescendants().OfType<PageCanvasControl>().Single().ViewModel);
        Assert.Single(ribbon.TabControl.Items.OfType<TabItem>(), t => t.IsVisible && (string?)t.Header == "Home");
    }

    [Fact]
    public void Thumbnails_AreLaidOutThreeToARow_InReadingOrder()
    {
        var (window, view, _) = Open(pages: 5);
        var tops = Enumerable.Range(0, 5).Select(i => ((Control)view.List.ContainerFromIndex(i)!).TranslatePoint(new Point(0, 0), window)!.Value).ToList();

        Assert.Equal(tops[0].Y, tops[1].Y, 1);
        Assert.Equal(tops[0].Y, tops[2].Y, 1);
        Assert.True(tops[0].X < tops[1].X && tops[1].X < tops[2].X);
        Assert.True(tops[3].Y > tops[0].Y, "the fourth page starts a second row");
        Assert.Equal(tops[0].X, tops[3].X, 1);

        var thumbnail = view.GetVisualDescendants().OfType<PageThumbnail>().First();
        Assert.True(thumbnail.Bounds.Width * 3 <= view.Bounds.Width, "three thumbnails fit across the pane");
    }

    [Fact]
    public void DraggingAThumbnail_ToTheRightHalfOfAPageInTheNextRow_DropsItAfterThatPage()
    {
        var (window, view, navigator) = Open(pages: 5);
        var first = navigator.Pages[0];
        var fourth = navigator.Pages[3];

        var start = CenterOf((Control)view.List.ContainerFromIndex(0)!, window);
        var target = (Control)view.List.ContainerFromIndex(3)!; // first in the second row
        var end = target.TranslatePoint(new Point(target.Bounds.Width * 0.8, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 10, start.Y + 10));
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, navigator.Pages.IndexOf(first));
        Assert.Equal(2, navigator.Pages.IndexOf(fourth));
        Assert.Equal(4, first.Number);
        Assert.True(window.History.CanUndo);
    }

    [Fact]
    public void RibbonPageNumbers_NumberEveryPage()
    {
        var (window, _, navigator) = Open(pages: 3);
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "InsertTab");
        Dispatcher.UIThread.RunJobs();
        var box = ribbon.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PageNumberPositionBox");

        box.SelectedItem = PageNumberOption.All.Single(o => o.Position == Stanley.ProjectModel.Issues.PageNumberPosition.BottomOuter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Stanley.ProjectModel.Issues.PageNumberPosition.BottomOuter, navigator.PageNumbering.Position);
        Assert.Null(navigator.Pages[0].Editor.Folio); // cover unnumbered by default
        Assert.Equal("3", navigator.Pages[2].Editor.Folio!.Text);
        Assert.True(window.ViewModel.IsDirty);
    }

    [Fact]
    public void NewPageButton_AddsAPageAndShowsIt_AndUndoRemovesIt()
    {
        var (window, view, navigator) = Open(pages: 1);
        var add = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddPageButton");

        add.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, navigator.Pages.Count);
        Assert.Same(navigator.Pages[1], navigator.CurrentPage);
        Assert.Same(navigator.Pages[1].Editor, window.Editor);
        Assert.Equal(2, view.GetVisualDescendants().OfType<PageThumbnail>().Count());

        window.ViewModel.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(navigator.Pages);
        Assert.Same(navigator.Pages[0].Editor, window.Editor);
    }

    [Fact]
    public void Thumbnail_RedrawsWhenItsPageIsEdited()
    {
        var (window, view, navigator) = Open(pages: 1);
        var thumbnail = view.GetVisualDescendants().OfType<PageThumbnail>().Single();
        var editor = navigator.CurrentPage.Editor;
        Assert.Same(editor, thumbnail.Page);

        var before = ThumbnailPixels(window, thumbnail);
        var panel = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panel);
        editor.CreateBubble(panel, new Point2D(bounds.MidX, bounds.MidY), new Rect2D(bounds.Left + 10, bounds.Top + 10, bounds.Width - 20, bounds.Height / 2));
        Dispatcher.UIThread.RunJobs();
        var after = ThumbnailPixels(window, thumbnail);

        Assert.NotEqual(before, after);
    }

    /// <summary>The rendered pixels inside the thumbnail's bounds, as a comparable string.</summary>
    private static string ThumbnailPixels(MainWindow window, PageThumbnail thumbnail)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var origin = thumbnail.TranslatePoint(new Point(0, 0), window)!.Value;
        var scale = frame.PixelSize.Width / window.Bounds.Width;
        var left = (int)(origin.X * scale);
        var top = (int)(origin.Y * scale);
        var width = (int)(thumbnail.Bounds.Width * scale);
        var height = (int)(thumbnail.Bounds.Height * scale);

        var row = new byte[width * 4];
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        for (var y = top; y < top + height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + left * 4, row, 0, row.Length);
            hash.AppendData(row);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
