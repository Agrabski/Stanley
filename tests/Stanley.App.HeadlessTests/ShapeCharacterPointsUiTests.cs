using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>Edit Points and Edit sockets on a head or torso through the real window (docs/shape-characters.md §9.2): double-click, drags, keys, the Module tab and the right-click menu.</summary>
[Collection("Page Editor Tests")]
public class ShapeCharacterPointsUiTests
{
    private static (MainWindow Window, CharacterEditorViewModel Editor, CharacterEditorView View) OpenShapeCharacter()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.ViewModel.Characters!.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = window.ViewModel.Characters.Items.Single().Editor;
        var view = window.GetVisualDescendants().OfType<CharacterEditorView>().Single();
        return (window, editor, view);
    }

    private static Point FindPoint(Control control, Func<Point, bool> test)
    {
        for (var y = 0.0; y < control.Bounds.Height; y += 3)
            for (var x = 0.0; x < control.Bounds.Width; x += 3)
                if (test(new Point(x, y)))
                    return new Point(x, y);
        throw new InvalidOperationException("No such point on the stage.");
    }

    /// <summary>A figure-space point on the stage, in window coordinates.</summary>
    private static Point OnWindow(MainWindow window, CharacterFigure stage, Point2D figure)
    {
        var p = stage.MainPlacement!.ToPage(figure);
        return stage.TranslatePoint(new Point(p.X, p.Y), window)!.Value;
    }

    private static void Drag(MainWindow window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(MainWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Double_clicking_the_torso_edits_its_points_a_drag_moves_one_and_Esc_is_done()
    {
        var (window, editor, view) = OpenShapeCharacter();
        var stage = view.Figure;
        var torso = FindPoint(stage, p => stage.ModuleAt(p) == ModulePart.Torso && stage.StickerAt(p) is null);

        window.DoubleClick(stage.TranslatePoint(torso, window)!.Value);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        Assert.True(editor.IsEditingPoints);
        Assert.NotNull(stage.EditMarks?.Outline);
        Assert.Null(stage.ModuleHandleAt(stage.ModuleBounds(ModulePart.Torso)!.Value.TopLeft)); // no corner handles while editing
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().Single();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("ModuleTab");
        Dispatcher.UIThread.RunJobs();
        var editPoints = ribbon.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "EditPointsButton");
        Assert.True(editPoints.IsChecked);
        LookTabTests.Snapshot(window, "module-edit-points");

        // Drag the first point out: it follows the pointer, one undo step.
        var point = editor.EditedOutline!.Anchors[0].Point;
        var from = OnWindow(window, stage, point);
        Drag(window, from, from + new Point(-25, -15));
        var moved = editor.EditedOutline!.Anchors[0].Point;
        Assert.True(moved.X < point.X - 0.01 && moved.Y < point.Y - 0.005, $"{point} -> {moved}");
        Assert.Equal(0, editor.SelectedPointIndex);
        Assert.Null(editor.Working.Modules!.Torso!.Shape.Source);
        LookTabTests.Snapshot(window, "module-point-dragged");

        // Delete removes the selected point; Esc is done.
        var count = editor.EditedOutline.Anchors.Count;
        Press(window, Key.Delete, PhysicalKey.Delete);
        Assert.Equal(count - 1, editor.EditedOutline!.Anchors.Count);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(editor.IsEditingPoints);
        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        Assert.False(editPoints.IsChecked);
    }

    [Fact]
    public void A_click_elsewhere_ends_editing_points_and_does_what_it_always_does()
    {
        var (window, editor, view) = OpenShapeCharacter();
        var stage = view.Figure;
        editor.SelectModule(ModulePart.Head);
        editor.IsEditingPoints = true;
        Dispatcher.UIThread.RunJobs();

        // The torso, away from the head's outline: it's picked instead.
        var torso = FindPoint(stage, p => stage.ModuleAt(p) == ModulePart.Torso && stage.StickerAt(p) is null && stage.EditMarkAt(p) is null && !stage.IsInsideEditedOutline(p));
        window.MouseDown(stage.TranslatePoint(torso, window)!.Value, MouseButton.Left);
        window.MouseUp(stage.TranslatePoint(torso, window)!.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(editor.IsEditingPoints);
        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        Assert.Null(stage.EditMarks);
    }

    [Fact]
    public void Edit_sockets_shows_labelled_dots_to_drag_and_the_right_click_menu_offers_both()
    {
        var (window, editor, view) = OpenShapeCharacter();
        var stage = view.Figure;
        var torso = FindPoint(stage, p => stage.ModuleAt(p) == ModulePart.Torso && stage.StickerAt(p) is null);

        var items = view.StageMenuItems(torso).OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        Assert.Equal(["Edit points", "Edit sockets"], items);
        var sockets = view.StageMenuItems(torso).OfType<MenuItem>().Single(i => (string?)i.Header == "Edit sockets");
        sockets.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsEditingSockets);
        Assert.Equal(6, stage.EditMarks!.Sockets.Count);
        LookTabTests.Snapshot(window, "module-edit-sockets");

        var hip = editor.EditedSockets.Single(s => s.Name == ModuleSockets.LeftHip);
        var from = OnWindow(window, stage, hip.At);
        Drag(window, from, from + new Point(6, -12));
        Assert.True(editor.Working.Modules!.Torso!.Shape.Sockets!.ContainsKey(ModuleSockets.LeftHip));
        Assert.True(editor.EditedSockets.Single(s => s.Name == ModuleSockets.LeftHip).HandSet);
        LookTabTests.Snapshot(window, "module-socket-dragged");

        var menu = view.StageMenuItems(torso).OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(["Edit points", "Done editing sockets", "Reset sockets"], menu);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(editor.IsEditingSockets);
    }

    [Fact]
    public void The_canvas_layout_has_Edit_Points_and_Edit_Sockets_in_the_Module_section()
    {
        var window = CanvasLayoutHarness.Open();
        window.ViewModel.Characters!.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = window.ViewModel.Characters.Items.Single().Editor;
        editor.SelectModule(ModulePart.Head);
        Dispatcher.UIThread.RunJobs();
        var module = CanvasLayoutHarness.CharacterInspector(window).FindInSections<ModuleSection>("ModuleProperties")!;

        var editPoints = module.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "EditPointsButton");
        editPoints.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsEditingPoints);
        Assert.True(module.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PointTools").IsVisible);
        LookTabTests.Snapshot(window, "canvas-module-edit-points");

        var editSockets = module.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "EditSocketsButton");
        editSockets.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsEditingSockets);
        Assert.False(editor.IsEditingPoints);
        Assert.False(editPoints.IsChecked);
    }
}
