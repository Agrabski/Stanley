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
