using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;

namespace Stanley.App.HeadlessTests;

/// <summary>The canvas layout's shell: one top bar instead of the title bar and ribbon, Pages and Layers stacked on the left, the inspector on the right.</summary>
[Collection("Page Editor Tests")]
public class CanvasLayoutTests
{
    private static double Left(Visual visual, Visual relativeTo) => visual.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;
    private static double Top(Visual visual, Visual relativeTo) => visual.TranslatePoint(new Point(0, 0), relativeTo)!.Value.Y;

    [Fact]
    public void The_ribbon_layout_is_the_default_and_has_no_canvas_parts()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.ViewModel.IsCanvasActive);
        Assert.Null(window.CanvasTopBarControl);
        Assert.Null(window.InspectorControl);
        Assert.Single(window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>());
        Assert.True(window.FindControl<Border>("TitleBar")!.IsAttachedToVisualTree());
    }

    [Fact]
    public void The_canvas_layout_swaps_the_title_bar_and_ribbon_for_a_top_bar_and_an_inspector()
    {
        var window = CanvasLayoutHarness.Open();

        Assert.Empty(window.GetVisualDescendants().OfType<PageEditorRibbon>());
        Assert.DoesNotContain(window.GetVisualDescendants(), v => v is Border { Name: "TitleBar" or "RibbonBar" });
        Assert.Single(CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<PageToolbar>());
        var inspector = CanvasLayoutHarness.PageInspector(window);
        Assert.Equal("Page", inspector.Title);

        // The inspector is right of the page, and the top bar above both.
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
        Assert.True(Left(inspector, window) > Left(canvas, window));
        Assert.True(Top(canvas, window) >= CanvasLayoutHarness.TopBar(window).Bounds.Height);
    }

    [Fact]
    public void Pages_and_Layers_stack_on_the_left_of_the_page()
    {
        var window = CanvasLayoutHarness.Open();

        var pages = window.GetVisualDescendants().OfType<PageNavigatorView>().Single();
        var layers = window.GetVisualDescendants().OfType<LayersView>().Single();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
        Assert.True(Left(layers, window) < Left(canvas, window));
        Assert.True(Top(layers, window) > Top(pages, window));

        // View › Layers still hides it - and the Pages pane takes the room.
        window.Editor.ShowLayers = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.GetVisualDescendants().OfType<LayersView>());
        window.Editor.ShowLayers = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(window.GetVisualDescendants().OfType<LayersView>());
    }

    [Fact]
    public void The_inspector_follows_the_selection()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;

        editor.Select(editor.Working.PanelOrder[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Panel", CanvasLayoutHarness.PageInspector(window).Title);
        Assert.True(CanvasLayoutHarness.PageInspector(window).FindControl<PanelSection>("PanelProperties")!.IsVisible);
        Assert.False(CanvasLayoutHarness.PageInspector(window).FindControl<PageSection>("PageProperties")!.IsVisible);

        editor.Select(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Page", CanvasLayoutHarness.PageInspector(window).Title);
        Assert.True(CanvasLayoutHarness.PageInspector(window).FindControl<PageSection>("PageProperties")!.IsVisible);
    }

    [Fact]
    public void A_character_editor_gets_the_character_inspector_and_the_page_comes_back_after()
    {
        var window = CanvasLayoutHarness.Open();
        var characters = window.ViewModel.Characters!;

        characters.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);
        Assert.Single(CanvasLayoutHarness.InspectorColumn(window).GetVisualDescendants().OfType<CharacterInspector>());
        Assert.Single(CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<CharacterToolbar>());

        window.Workspace.Activate(window.Editor);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(CanvasLayoutHarness.InspectorColumn(window).GetVisualDescendants().OfType<PageInspector>());
    }

    [Fact]
    public void The_status_bar_hints_point_at_the_inspector_not_the_ribbon()
    {
        var canvas = CanvasLayoutHarness.Open();
        Assert.True(canvas.Editor.UsesInspector);
        Assert.DoesNotContain("ribbon", canvas.Editor.Hint);
        canvas.Editor.Select(canvas.Editor.Working.PanelOrder[0]);
        Assert.Contains("inspector", canvas.Editor.Hint);

        var ribbon = new MainWindow();
        ribbon.Show();
        Assert.False(ribbon.Editor.UsesInspector);
        Assert.Contains("ribbon", ribbon.Editor.Hint);
    }
}
