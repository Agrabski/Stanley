using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>The canvas layout's inspector with nothing selected: the page's own settings - layout, spacing, guides, page numbers, title page.</summary>
[Collection("Page Editor Tests")]
public class CanvasPageSectionTests
{
    private static PageSection Section(MainWindow window) =>
        CanvasLayoutHarness.PageInspector(window).FindControl<PageSection>("PageProperties")!;

    /// <summary>A named control in the page section's own name scope (another section may use the same name).</summary>
    private static T Find<T>(MainWindow window, string name) where T : Control =>
        Section(window).FindControl<T>(name) ?? throw new InvalidOperationException($"No {name} in the page section.");

    private static void Click(MainWindow window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Undo(MainWindow window)
    {
        window.ViewModel.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void It_shows_with_nothing_selected_and_gives_way_when_a_panel_is_selected()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        Assert.True(Section(window).IsEffectivelyVisible);
        LookTabTests.Snapshot(window, "canvas-page-section");

        editor.Select(editor.Working.PanelOrder[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Section(window).IsVisible);

        editor.Select(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Section(window).IsEffectivelyVisible);

        // Nothing in it runs into the sections' right-hand padding, let alone out of the inspector.
        var section = Section(window);
        var room = section.Bounds.Width - 12;
        foreach (var control in section.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TemplatedControl or TextBlock))
        {
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), section)!.Value.X;
            Assert.True(right <= room + 0.5, $"{control.GetType().Name} {control.Name} ends at {right}, past {room}");
        }
    }

    [Fact]
    public void A_layout_preset_retiles_the_page_and_a_locked_layout_greys_them_out()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var presets = Find<ItemsControl>(window, "LayoutGallery").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(PanelLayoutPresets.All.Count, presets.Count);
        Button Preset(string name) => presets.Single(b => b.DataContext is PanelLayoutPreset p && p.Name == name);

        Click(window, Preset("Six panels"));
        Assert.Equal(6, editor.Working.PanelOrder.Count);

        var locked = Find<CheckBox>(window, "LockLayoutCheck");
        locked.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsLayoutLocked);
        Assert.All(presets, b => Assert.False(b.IsEffectivelyEnabled));
        Click(window, Preset("Splash"));
        Assert.Equal(6, editor.Working.PanelOrder.Count);
        LookTabTests.Snapshot(window, "canvas-page-section-locked");

        // Locking is undoable like the layout itself; the box follows.
        Undo(window);
        Assert.False(editor.IsLayoutLocked);
        Assert.False(locked.IsChecked);
        Assert.True(Preset("Splash").IsEffectivelyEnabled);
    }

    [Fact]
    public void The_margin_and_gutter_boxes_set_the_comics_spacing_and_follow_undo()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var navigator = window.ViewModel.Navigator!;
        var margin = Find<NumericUpDown>(window, "MarginInput");
        var gutter = Find<NumericUpDown>(window, "GutterInput");
        var before = navigator.Spacing;
        Assert.Equal((decimal)before.MarginMm, margin.Value);
        Assert.Equal((decimal)before.GutterMm, gutter.Value);

        margin.Value = 20;
        Dispatcher.UIThread.RunJobs();
        gutter.Value = 7.5m;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new PanelGrid(20, 7.5), navigator.Spacing);
        Assert.Equal(20, editor.MarginMm);
        Assert.Equal(7.5, editor.GutterMm);

        Undo(window);
        Assert.Equal((decimal)before.GutterMm, gutter.Value);
        Assert.Equal(20m, margin.Value);
        Undo(window);
        Assert.Equal((decimal)before.MarginMm, margin.Value);
        Assert.Equal(before, navigator.Spacing);
    }

    [Fact]
    public void The_guides_switch_the_margin_guide_and_snapping()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;

        Find<CheckBox>(window, "MarginGuidesCheck").IsChecked = false;
        Find<CheckBox>(window, "SnapCheck").IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(editor.ShowMarginGuides);
        Assert.False(editor.SnapEnabled);
    }

    [Fact]
    public void Page_numbers_are_placed_from_the_position_menu_and_their_options_show_once_there_are_some()
    {
        var window = CanvasLayoutHarness.Open();
        var navigator = window.ViewModel.Navigator!;
        navigator.AddPageAfter(navigator.CurrentPage);
        navigator.AddPageAfter(navigator.CurrentPage);
        navigator.CurrentPage = navigator.Pages[0];
        Dispatcher.UIThread.RunJobs();
        var button = Find<DropDownButton>(window, "PageNumbersButton");
        var firstPage = Find<CheckBox>(window, "NumberFirstPageCheck");
        var start = Find<NumericUpDown>(window, "PageNumberStartInput");
        string Summary() => string.Concat(button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal("None", Summary());
        Assert.False(firstPage.IsEffectivelyVisible);
        Assert.False(start.IsEffectivelyVisible);

        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name);
        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Item("RemovePageNumbersItem").Command!.CanExecute(null));
        Item("BottomOuterItem").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        menu.Hide();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(PageNumberPosition.BottomOuter, navigator.PageNumbering.Position);
        Assert.Equal("Bottom, outer", Summary());
        Assert.Null(navigator.Pages[0].Editor.Folio); // the cover isn't numbered...
        Assert.True(firstPage.IsEffectivelyVisible);
        firstPage.IsChecked = true; // ...until asked
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigator.PageNumbering.NumberFirstPage);
        Assert.Equal("1", navigator.Pages[0].Editor.Folio!.Text);

        start.Value = 10;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(10, navigator.PageNumbering.StartAt);
        Assert.Equal("12", navigator.Pages[2].Editor.Folio!.Text);
        LookTabTests.Snapshot(window, "canvas-page-section-numbers");
        Undo(window);
        Assert.Equal(1m, start.Value);

        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Item("RemovePageNumbersItem").Command!.Execute(null);
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PageNumberPosition.None, navigator.PageNumbering.Position);
        Assert.False(firstPage.IsEffectivelyVisible);
    }

    [Fact]
    public void A_title_page_is_picked_from_the_gallery_then_made_this_issues_own_and_removed()
    {
        var window = CanvasLayoutHarness.Open();
        var navigator = window.ViewModel.Navigator!;
        Assert.False(Find<CheckBox>(window, "OwnTitlePageBox").IsEffectivelyVisible);
        Assert.False(Find<Button>(window, "RemoveTitlePageButton").IsEffectivelyVisible);

        var dropDown = Find<DropDownButton>(window, "TitlePageButton");
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)dropDown.Flyout).Content!;
        var designs = content.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is TitlePageChoice).ToList();
        Assert.Equal(TitlePages.All.Count, designs.Count);
        LookTabTests.Snapshot(TopLevel.GetTopLevel(content) ?? window, "canvas-title-page-gallery");

        // A real click: the gallery closes behind it, and the new title page is shown.
        Click(window, designs.Single(b => b.DataContext is TitlePageChoice { Design: TitlePageDesign.Cover }));
        Assert.False(dropDown.Flyout.IsOpen);
        Assert.Same(navigator.Pages[0], navigator.TitlePage);
        Assert.Same(navigator.Pages[0].Editor, window.Editor);

        // The title page's own inspector offers what to do with it.
        var own = Find<CheckBox>(window, "OwnTitlePageBox");
        var remove = Find<Button>(window, "RemoveTitlePageButton");
        Assert.True(own.IsEffectivelyVisible);
        Assert.True(remove.IsEffectivelyVisible);
        Assert.Equal("Remove the title page from every issue", ToolTip.GetTip(remove));
        LookTabTests.Snapshot(window, "canvas-page-section-title-page");

        own.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(navigator.OwnTitlePage);
        Assert.Same(navigator.OwnTitlePage!.Editor, window.Editor);
        remove = Find<Button>(window, "RemoveTitlePageButton");
        Assert.Equal("Remove this issue's title page", ToolTip.GetTip(remove));

        Click(window, remove);
        Assert.Null(navigator.OwnTitlePage);
    }
}
