using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

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

    /// <summary>A profile that's never switched the pane off: it's there from the start, so nobody has to know to look for it (#161).</summary>
    [Fact]
    public void The_Layers_pane_shows_from_the_start()
    {
        var window = new MainWindow(new MainWindowViewModel(new NoDialogs(), new Stanley.App.Documents.RecentProjects(null)));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Editor.ShowLayers);
        Assert.Single(Views(window));
    }

    private sealed class NoDialogs : Stanley.App.Documents.IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(null);
        public Task<Stanley.App.Documents.SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(Stanley.App.Documents.SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> AskInstallUpdateAsync(string version, string? notes) => Task.FromResult(false);
        public Task<bool> AskDeleteIssueAsync(string caption) => Task.FromResult(false);
    }

    [Fact]
    public void The_Layers_pane_can_be_hidden_and_shown_again_right_of_the_page()
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
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
            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
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
            Assert.Equal(editor.ShowLayers, check.IsChecked);

            check.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.ShowLayers);
            Assert.Empty(Views(window));

            check.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.ShowLayers);
            Assert.Single(Views(window));
        }
        finally
        {
            editor.ShowLayers = true; // back to the default, for the tests that follow
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

            editor.ShowLayers = false;
            Assert.All(navigator.AllPages, p => Assert.False(p.Editor.ShowLayers));

            editor.ShowLayers = true;
            Assert.All(navigator.AllPages, p => Assert.True(p.Editor.ShowLayers));
        }
        finally
        {
            editor.ShowLayers = true; // back to the default, for the tests that follow
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
        }
    }

    private static Point OnPage(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    private static void Click(MainWindow window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ToolButton(LayersView view, string name) => view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    /// <summary>A character and a speech bubble over the middle of it; the bubble is in front, as bubbles always used to be.</summary>
    private static (PanelId Panel, int Bubble, int Character, Point2D Overlap) CharacterWithABubbleOverIt(PageEditorViewModel editor)
    {
        var panel = editor.Working.PanelOrder[0];
        var character = editor.InsertCharacter(CharacterId.New(), panel); // not in the comic: drawn as a placeholder box, which is all a click needs
        var box = editor.CharacterBounds(editor.Working.Panels[panel].CharacterInstances[character]);
        var bubble = editor.CreateBubble(panel, new Point2D(box.MidX, box.MidY));
        var bubbleBox = AnchorRing.BoundingBox(editor.Working.Panels[panel].Bubbles[bubble].Shape.Anchors);
        var overlap = new Point2D(bubbleBox.MidX, bubbleBox.MidY);
        Assert.True(overlap.X >= box.Left && overlap.X <= box.Right && overlap.Y >= box.Top && overlap.Y <= box.Bottom, "the bubble's middle is over the character");
        editor.ClearSelection();
        return (panel, bubble, character, overlap);
    }

    [Fact]
    public void The_buttons_above_the_list_move_the_selected_layer_one_undo_step_each()
    {
        var (window, editor) = Open();
        try
        {
            var (panel, bubble, character, _) = CharacterWithABubbleOverIt(editor);
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            var view = Views(window).Single();
            Assert.False(ToolButton(view, "BringForwardButton").IsEffectivelyEnabled, "nothing is selected");

            editor.Select(panel, bubble);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolButton(view, "BringToFrontButton").IsEffectivelyEnabled, "the bubble is in front of everything already");

            Click(window, CenterOf(ToolButton(view, "SendBackwardButton"), window));

            var moved = editor.Working.Panels[panel];
            var order = PanelStack.Order(moved).ToList();
            Assert.True(order.IndexOf(new StackItem(StackKind.Bubble, bubble)) < order.IndexOf(new StackItem(StackKind.Character, character)), "the bubble is behind the character now");
            Assert.True(ToolButton(view, "BringForwardButton").IsEffectivelyEnabled);

            Click(window, CenterOf(ToolButton(view, "BringToFrontButton"), window));
            Assert.Equal(new StackItem(StackKind.Bubble, bubble), PanelStack.Order(editor.Working.Panels[panel]).Last());

            window.History.Undo(); // the move to the front
            window.History.Undo(); // the move back
            Assert.Null(editor.Working.Panels[panel].Stack);
            Assert.Equal(new StackItem(StackKind.Bubble, bubble), PanelStack.Order(editor.Working.Panels[panel]).Last());
        }
        finally
        {
            editor.ShowLayers = true; // back to the default, for the tests that follow
        }
    }

    [Fact]
    public void A_click_on_the_page_picks_what_is_in_front_wherever_the_stack_has_put_it()
    {
        var (window, editor) = Open();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
        var (panel, bubble, character, overlap) = CharacterWithABubbleOverIt(editor);
        var point = OnPage(window, canvas, overlap.X, overlap.Y);

        Click(window, point); // the usual order: the bubble is in front
        Assert.True(editor.HasSelectedBubble);
        Assert.False(editor.HasSelectedCharacter);

        editor.ClearSelection();
        editor.MoveInStack(panel, new StackItem(StackKind.Bubble, bubble), StackMove.Backward);
        Click(window, point); // now the character is
        Assert.True(editor.HasSelectedCharacter);
        Assert.Equal(character, editor.SelectedCharacterIndex);
        Assert.False(editor.HasSelectedBubble);

        editor.ClearSelection();
        editor.MoveInStack(panel, new StackItem(StackKind.Bubble, bubble), StackMove.ToFront);
        Click(window, point);
        Assert.True(editor.HasSelectedBubble);
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
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
            editor.ShowLayers = true; // back to the default, for the tests that follow
        }
    }
}
