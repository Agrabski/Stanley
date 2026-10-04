using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>The Layers pane: docked right of the page, shown from the View tab, and not an editor.</summary>
[Collection("Page Editor Tests")]
public class LayersPaneTests
{
    private static (MainWindow Window, PageEditorViewModel Editor) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.Editor);
    }

    private static IReadOnlyList<LayersView> Views(MainWindow window) => window.GetVisualDescendants().OfType<LayersView>().ToList();

    private static double Left(Visual visual, Visual relativeTo) => visual.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;

    [Fact]
    public void The_Layers_pane_stays_away_until_asked_for_and_then_sits_right_of_the_page()
    {
        var (window, editor) = Open();
        try
        {
            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Views(window));

            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();

            var layers = Assert.Single(Views(window));
            var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
            var pages = window.GetVisualDescendants().OfType<PageNavigatorView>().Single();
            Assert.True(Left(layers, window) >= Left(canvas, window) + canvas.Bounds.Width - 1, "the Layers pane sits to the right of the page");
            Assert.True(Left(pages, window) + pages.Bounds.Width <= Left(canvas, window) + 1, "the Pages pane is still on the left");
            Assert.DoesNotContain(layers, window.RibbonBarControl.GetVisualDescendants());
        }
        finally
        {
            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void Hiding_the_Layers_pane_gives_the_page_its_room_back()
    {
        var (window, editor) = Open();
        try
        {
            var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
            var before = canvas.Bounds.Width;

            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(canvas.Bounds.Width < before, "the pane takes room from the page");

            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, canvas.Bounds.Width, 1);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void The_View_tabs_Layers_checkbox_shows_and_hides_the_pane()
    {
        var (window, editor) = Open();
        try
        {
            var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
            ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ViewTab");
            Dispatcher.UIThread.RunJobs();
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ShowLayersCheck");
            Assert.True(check.IsVisible);
            Assert.False(check.IsChecked);

            check.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.ShowLayers);
            Assert.Single(Views(window));

            check.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.ShowLayers);
            Assert.Empty(Views(window));
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void The_Layers_switch_is_one_setting_for_every_page()
    {
        var (window, editor) = Open();
        try
        {
            var navigator = window.ViewModel.Navigator!;
            navigator.AddPageAfter(navigator.Pages[^1]);
            Dispatcher.UIThread.RunJobs();

            editor.ShowLayers = true;

            Assert.All(navigator.AllPages, p => Assert.True(p.Editor.ShowLayers));
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    private static Border RowBorder(LayersView view, LayerRowKind kind) =>
        view.List.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("layerRow") && b.DataContext is LayerRow row && row.Kind == kind);

    private static Point CenterOf(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    [Fact]
    public void Clicking_a_row_selects_that_layer_on_the_page_and_selecting_on_the_page_moves_the_highlight()
    {
        var (window, editor) = Open();
        try
        {
            var panel = editor.Working.PanelOrder[0];
            var bubble = editor.CreateBubble(panel, new Point2D(60, 60)); // selected, which opens its panel in the list
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            var view = Views(window).Single();
            Assert.Contains("selected", RowBorder(view, LayerRowKind.Bubble).Classes);

            editor.ClearSelection();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("selected", RowBorder(view, LayerRowKind.Bubble).Classes);

            var point = CenterOf(RowBorder(view, LayerRowKind.Bubble), window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(bubble, editor.SelectedBubbleIndex);
            Assert.Contains("selected", RowBorder(view, LayerRowKind.Bubble).Classes);

            editor.Select(panel);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("selected", RowBorder(view, LayerRowKind.Bubble).Classes);
            Assert.Contains("selected", view.List.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("layerRow") && b.DataContext is LayerRow { Kind: LayerRowKind.Panel }).Classes);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    /// <summary>Writes a PNG of the pane beside a page with a few layers (only when STANLEY_UI_SNAPSHOTS is set), so how it looks can be eyeballed.</summary>
    [Fact]
    public void The_Layers_pane_with_a_few_layers_renders()
    {
        var (window, editor) = Open();
        try
        {
            var panel = editor.Working.PanelOrder[0];
            editor.Tool = PageEditorTool.Rectangle;
            editor.BeginDrawShape(panel);
            editor.UpdateDrawShape(new Point2D(30, 120), new Point2D(120, 200));
            editor.CommitDrawShape();
            editor.Tool = PageEditorTool.Select;
            editor.CreateText(panel, new Point2D(60, 40));
            var bubble = editor.CreateBubble(panel, new Point2D(120, 80));
            editor.Select(panel, bubble);
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();

            LookTabTests.Snapshot(window, "layers-pane");

            var view = Views(window).Single();
            Assert.True(view.List.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("layerRow")) >= 5);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void A_panels_arrow_hides_its_layers_without_selecting_anything()
    {
        var (window, editor) = Open();
        try
        {
            var panel = editor.Working.PanelOrder[0];
            editor.CreateBubble(panel, new Point2D(60, 60));
            editor.ClearSelection();
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            var view = Views(window).Single();
            Assert.NotEmpty(view.List.GetVisualDescendants().OfType<Border>().Where(b => b.DataContext is LayerRow { Kind: LayerRowKind.Bubble }));

            var arrow = view.List.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("expander"));
            var point = CenterOf(arrow, window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(view.List.GetVisualDescendants().OfType<Border>().Where(b => b.DataContext is LayerRow { Kind: LayerRowKind.Bubble }));
            Assert.False(editor.HasSelectedBubble);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void Showing_the_Layers_pane_never_moves_the_ribbon_off_the_page()
    {
        var (window, editor) = Open();
        try
        {
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            window.Workspace.Factory.SetActiveDockable(window.GetVisualDescendants().OfType<LayersView>().Single().DataContext as Dock.Model.Core.IDockable);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(editor, window.Workspace.ActiveEditor);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }
}
