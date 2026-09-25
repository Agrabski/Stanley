using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>File › New's strip and webcomic templates, Insert › Title page and the Panel tab's Border switch, through the real window.</summary>
[Collection("Page Editor Tests")]
public class TitlePageAndTemplateTests
{
    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static PageEditorRibbon Ribbon(MainWindow window) => window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();

    private static void ShowTab(MainWindow window, string name)
    {
        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == name);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The window, or the popup a flyout opened in if it isn't drawn inside the window.</summary>
    private static void Snapshot(Control shown, MainWindow window, string name) =>
        LookTabTests.Snapshot(TopLevel.GetTopLevel(shown) ?? window, name);

    [Fact]
    public void File_New_offers_strip_and_webcomic_templates_and_a_tile_starts_that_comic()
    {
        var window = Open();
        window.ViewModel.ShowBackstage(BackstagePage.New);
        Dispatcher.UIThread.RunJobs();
        var tiles = window.BackstageControl.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Template", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(ComicTemplates.All.Count, tiles.Count);
        LookTabTests.Snapshot(window, "backstage-new-templates");

        tiles.Single(b => b.Name == "TemplateVerticalScroll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.ViewModel.IsBackstageOpen);
        Assert.Equal(new Rect2D(0, 0, 200, 320), window.Editor.PageBounds);
        Assert.Equal(2, window.Editor.Working.PanelOrder.Count);
        Assert.Equal(new PanelGrid(10, 20), window.Editor.Grid);
        LookTabTests.Snapshot(window, "vertical-scroll-comic");
    }

    [Fact]
    public void Insert_title_page_puts_the_picked_design_first_and_the_gallery_closes()
    {
        var window = Open();
        ShowTab(window, "InsertTab");
        var dropDown = Ribbon(window).GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "TitlePageButton");
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)dropDown.Flyout).Content!;
        var designs = content.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is TitlePageChoice).ToList();
        Assert.Equal(TitlePages.All.Count, designs.Count);
        Assert.Equal(TitlePages.All.Count, content.GetVisualDescendants().OfType<TitlePagePreview>().Count());
        Snapshot(content, window, "title-page-gallery");

        // A real click: the button raises Click (which closes the gallery) before it runs its command.
        var cover = designs.Single(b => b.DataContext is TitlePageChoice { Design: TitlePageDesign.Cover });
        var at = cover.TranslatePoint(new Point(cover.Bounds.Width / 2, cover.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var navigator = window.ViewModel.Navigator!;
        Assert.False(dropDown.Flyout.IsOpen);
        Assert.Equal(2, navigator.Pages.Count);
        Assert.Same(navigator.Pages[0], navigator.TitlePage);
        Assert.Same(navigator.Pages[0].Editor, window.Editor);
        var captions = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("pageNumber")).Select(t => t.Text).ToList();
        Assert.Equal(["Title", "2"], captions);
        LookTabTests.Snapshot(window, "title-page");
    }

    [Fact]
    public void Only_this_issue_gives_the_issue_its_own_title_page_and_unticking_brings_the_comics_back()
    {
        var window = Open();
        var navigator = window.ViewModel.Navigator!;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Banner);
        Dispatcher.UIThread.RunJobs();
        ShowTab(window, "InsertTab");
        var dropDown = Ribbon(window).GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "TitlePageButton");
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)dropDown.Flyout).Content!;
        var box = content.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "OwnTitlePageBox");
        var remove = content.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveTitlePageButton");
        string RemoveText() => string.Concat(remove.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.True(box.IsEnabled);
        Assert.False(box.IsChecked);
        Assert.Equal("Remove the title page from every issue", RemoveText());

        var at = box.TranslatePoint(new Point(10, box.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var own = Assert.IsType<PageItem>(navigator.OwnTitlePage);
        Assert.Same(own, navigator.Pages[0]);
        Assert.Same(own.Editor, window.Editor);
        Assert.True(box.IsChecked);
        Assert.Equal("Remove this issue's title page", RemoveText());
        Snapshot(content, window, "title-page-only-this-issue");

        box.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(comic, navigator.Pages[0]);
        Assert.Null(navigator.OwnTitlePage);
    }

    [Fact]
    public void A_field_clicked_on_the_Text_tab_while_typing_goes_in_at_the_caret_and_shows_the_issue_number()
    {
        var window = Open();
        var editor = window.Editor;
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateText(panelId, new Point2D(bounds.Left + 20, bounds.Top + 30), new Rect2D(bounds.Left + 20, bounds.Top + 30, 80, 12));
        editor.SetElementText(panelId, index, "Wydanie # tom 2");
        window.ViewModel.IssueNumber = "7";
        editor.RequestElementTextEdit(panelId, index);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.TextEditor.IsFocused);
        view.TextEditor.CaretIndex = "Wydanie #".Length;
        ShowTab(window, "TextTab");

        var issue = Ribbon(window).GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is TextFieldChoice { Token: "{issue}" });
        var at = issue.TranslatePoint(new Point(issue.Bounds.Width / 2, issue.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible, "the ribbon button mustn't take the keyboard away from the text being typed");
        Assert.Equal("Wydanie #{issue} tom 2", view.TextEditor.Text);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Wydanie #{issue} tom 2", ((Stanley.ProjectModel.Issues.TextElement)editor.Working.Panels[panelId].Elements[index]).Text);
        Assert.Equal("Wydanie #7 tom 2", editor.Fields!.Fill("Wydanie #{issue} tom 2"));
        LookTabTests.Snapshot(window, "field-issue");
    }

    [Fact]
    public void The_Layout_tab_margin_moves_every_pages_panels_onto_it_as_one_undo_step()
    {
        var window = Open();
        var navigator = window.ViewModel.Navigator!;
        navigator.AddPageAfter(navigator.CurrentPage);
        navigator.CurrentPage = navigator.Pages[0];
        Dispatcher.UIThread.RunJobs();
        ShowTab(window, "LayoutTab");
        var margin = Ribbon(window).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "MarginInput");
        double Left(PageItem page) => AnchorRing.BoundingBox(page.Editor.Working.Panels.Values.Single().Shape.Anchors).Left;
        Assert.All(navigator.Pages, p => Assert.Equal(10, Left(p), 6));

        margin.Value = 20;
        Dispatcher.UIThread.RunJobs();

        Assert.All(navigator.Pages, p => Assert.Equal(20, Left(p), 6));
        Assert.All(navigator.Pages, p => Assert.Equal(20, p.Editor.Grid.MarginMm));
        LookTabTests.Snapshot(window, "margin-20");

        window.ViewModel.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.All(navigator.Pages, p => Assert.Equal(10, Left(p), 6));
        Assert.Equal(10m, margin.Value);
    }

    [Fact]
    public void The_panel_tab_border_switch_takes_a_panels_border_off_and_back_on()
    {
        var window = Open();
        var panelId = window.Editor.Working.PanelOrder[0];
        window.Editor.Select(panelId);
        Dispatcher.UIThread.RunJobs();
        ShowTab(window, "PanelTab");
        var border = Ribbon(window).GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PanelBorderButton");
        Assert.True(border.IsChecked);

        border.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Editor.Working.Panels[panelId].Borderless);
        LookTabTests.Snapshot(window, "panel-borderless");

        window.ViewModel.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.Editor.Working.Panels[panelId].Borderless);
        Assert.True(border.IsChecked);
    }
}
