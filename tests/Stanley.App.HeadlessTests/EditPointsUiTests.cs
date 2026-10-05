using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>Edit Points and the Freeform tool (issue #84) through the real window: pointer, keyboard, ribbon and right-click menu.</summary>
[Collection("Page Editor Tests")]
public class EditPointsUiTests
{
    private static (MainWindow Window, PageCanvasControl Canvas, PanelId Panel, Rect2D Bounds) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().First();
        var panelId = window.Editor.Working.PanelOrder[0];
        return (window, canvas, panelId, window.Editor.PanelBounds(panelId));
    }

    private static Point At(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    private static void Press(MainWindow window, Key key, PhysicalKey physical, string? text = null)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Drag(MainWindow window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>An unfilled 40x30mm rectangle 20mm into the panel, then nothing selected.</summary>
    private static Rect2D DrawBox(MainWindow window, PanelId panelId, Rect2D bounds)
    {
        var editor = window.Editor;
        var box = new Rect2D(bounds.Left + 20, bounds.Top + 30, 40, 30);
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom));
        editor.CommitDrawShape();
        editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        return box;
    }

    private static ShapeElement Shape(MainWindow window, PanelId panelId, int index = 0) =>
        Assert.IsType<ShapeElement>(window.Editor.Working.Panels[panelId].Elements[index]);

    [Fact]
    public void Double_clicking_a_shape_shows_its_points_and_dragging_its_outline_pulls_out_a_new_one()
    {
        var (window, canvas, panelId, bounds) = Open();
        var box = DrawBox(window, panelId, bounds);

        window.DoubleClick(At(window, canvas, box.Left + 10, box.Bottom));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Editor.IsEditingPoints);
        Assert.Contains("Editing points", window.Editor.Hint);

        var top = At(window, canvas, box.MidX, box.Top);
        Drag(window, top, new Point(top.X, top.Y - 40));

        var roof = Shape(window, panelId);
        Assert.Equal(5, roof.Anchors.Count);
        Assert.True(roof.Anchors[1].Point.Y < box.Top - 5, "the new point was pulled up out of the top edge");
        Assert.Equal(box.MidX, roof.Anchors[1].Point.X, 1);
        Assert.Equal(1, window.Editor.SelectedPointIndex);

        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(window.Editor.IsEditingPoints);
        Assert.True(window.Editor.IsShapeContext, "Esc finishes editing points, the shape still selected");
    }

    [Fact]
    public void A_point_is_dragged_nudged_and_deleted_with_the_Delete_key_leaving_the_shape()
    {
        var (window, canvas, panelId, bounds) = Open();
        var box = DrawBox(window, panelId, bounds);
        window.Editor.SelectElement(panelId, 0);
        canvas.Focus();
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.True(window.Editor.IsEditingPoints);

        var corner = At(window, canvas, box.Right, box.Bottom);
        Drag(window, corner, new Point(corner.X + 30, corner.Y + 15));
        var moved = Shape(window, panelId).Anchors[2].Point;
        Assert.True(moved.X > box.Right + 3 && moved.Y > box.Bottom + 1, $"the corner should have followed the pointer, but is at {moved}");
        Assert.Equal(new Point2D(box.Right, box.Top), Shape(window, panelId).Anchors[1].Point);

        canvas.Focus();
        Press(window, Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(moved.X - 1, Shape(window, panelId).Anchors[2].Point.X, 6);

        Press(window, Key.Delete, PhysicalKey.Delete);
        Assert.Equal(3, Shape(window, panelId).Anchors.Count);
        Assert.True(window.Editor.IsEditingPoints);
        Press(window, Key.Delete, PhysicalKey.Delete); // no point selected: the shape stays
        Assert.Single(window.Editor.Working.Panels[panelId].Elements);
    }

    [Fact]
    public void Double_clicking_a_point_makes_it_smooth_and_its_handle_bends_the_outline()
    {
        var (window, canvas, panelId, bounds) = Open();
        var box = DrawBox(window, panelId, bounds);
        window.Editor.SelectElement(panelId, 0);
        window.Editor.IsEditingPoints = true;
        Dispatcher.UIThread.RunJobs();

        window.DoubleClick(At(window, canvas, box.Right, box.Top));
        Dispatcher.UIThread.RunJobs();
        var point = Shape(window, panelId).Anchors[1];
        Assert.Equal(AnchorHandleKind.Smooth, point.HandleKind);
        Assert.Equal(1, window.Editor.SelectedPointIndex);

        var handle = At(window, canvas, point.OutHandle.X, point.OutHandle.Y);
        Drag(window, handle, new Point(handle.X + 40, handle.Y));
        var bent = Shape(window, panelId).Anchors[1];
        Assert.True(bent.OutHandle.X > point.OutHandle.X + 5, "the handle followed the pointer");
        Assert.Equal(AnchorHandleKind.Smooth, bent.HandleKind);
    }

    [Fact]
    public void Double_clicking_the_outline_just_adds_a_point_there()
    {
        var (window, canvas, panelId, bounds) = Open();
        var box = DrawBox(window, panelId, bounds);
        window.Editor.SelectElement(panelId, 0);
        window.Editor.IsEditingPoints = true;
        Dispatcher.UIThread.RunJobs();

        window.DoubleClick(At(window, canvas, box.MidX, box.Top));
        Dispatcher.UIThread.RunJobs();

        var shape = Shape(window, panelId);
        Assert.Equal(5, shape.Anchors.Count);
        Assert.Equal(AnchorHandleKind.Corner, shape.Anchors[1].HandleKind); // not flipped by the second click
        Assert.Equal(1, window.Editor.SelectedPointIndex);
    }

    [Fact]
    public void The_Shape_tabs_Edit_Points_button_turns_point_editing_on_and_its_point_buttons_follow_the_selected_point()
    {
        var (window, canvas, panelId, bounds) = Open();
        DrawBox(window, panelId, bounds);
        window.Editor.SelectElement(panelId, 0);
        Dispatcher.UIThread.RunJobs();
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ShapeTab");
        Dispatcher.UIThread.RunJobs();

        var editPoints = ribbon.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "EditPointsButton");
        var smooth = ribbon.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "SmoothPointButton");
        Assert.True(editPoints.IsEnabled);
        Assert.False(smooth.IsEnabled);

        editPoints.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Editor.IsEditingPoints);

        window.Editor.SelectPoint(0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(smooth.IsEnabled);
        smooth.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AnchorHandleKind.Smooth, Shape(window, panelId).Anchors[0].HandleKind);

        editPoints.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.Editor.IsEditingPoints);
    }

    [Fact]
    public void Right_clicking_a_point_offers_to_delete_it_or_open_the_shape_there()
    {
        var (window, canvas, panelId, bounds) = Open();
        var box = DrawBox(window, panelId, bounds);
        window.Editor.SelectElement(panelId, 0);
        window.Editor.IsEditingPoints = true;

        var items = canvas.ContextMenuItems(new Point2D(box.Left, box.Bottom)).OfType<MenuItem>().ToList();
        Assert.Equal(3, window.Editor.SelectedPointIndex);
        Assert.Contains(items, i => i.Header as string == "Delete point");
        Assert.True(items.Single(i => i.Header as string == "Corner point").IsChecked);
        items.Single(i => i.Header as string == "Open the shape here").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(Shape(window, panelId).Closed);

        var outline = canvas.ContextMenuItems(new Point2D(box.MidX, box.Top)).OfType<MenuItem>().ToList();
        outline.Single(i => i.Header as string == "Add point").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(6, Shape(window, panelId).Anchors.Count);

        var shapeMenu = canvas.ContextMenuItems(new Point2D(box.MidX, box.Bottom)).OfType<MenuItem>().ToList();
        Assert.Contains(shapeMenu, i => i.Header as string is "Done editing points" or "Add point");
    }

    [Fact]
    public void The_Freeform_tool_places_points_click_by_click_and_clicking_the_first_one_closes_the_shape()
    {
        var (window, canvas, panelId, bounds) = Open();
        canvas.Focus();
        Press(window, Key.F, PhysicalKey.F, "f");
        Assert.Equal(PageEditorTool.Freeform, window.Editor.Tool);

        var a = At(window, canvas, bounds.Left + 20, bounds.Top + 60);
        Click(window, a);
        Click(window, At(window, canvas, bounds.Left + 45, bounds.Top + 20));
        Assert.Empty(window.Editor.Working.Panels[panelId].Elements); // nothing kept until it's finished
        Drag(window, At(window, canvas, bounds.Left + 70, bounds.Top + 60), At(window, canvas, bounds.Left + 75, bounds.Top + 70));
        Click(window, a);

        var shape = Shape(window, panelId);
        Assert.True(shape.Closed);
        Assert.Equal(3, shape.Anchors.Count);
        Assert.Equal(AnchorHandleKind.Corner, shape.Anchors[0].HandleKind);
        Assert.Equal(AnchorHandleKind.Smooth, shape.Anchors[2].HandleKind); // dragged as it was placed
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool);
        Assert.True(window.Editor.IsShapeContext);

        window.History.Undo();
        Assert.Empty(window.Editor.Working.Panels[panelId].Elements);
    }

    [Fact]
    public void Enter_finishes_a_freeform_line_and_Backspace_takes_the_last_point_back()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.Tool = PageEditorTool.Freeform;
        canvas.Focus();

        Click(window, At(window, canvas, bounds.Left + 20, bounds.Top + 20));
        Click(window, At(window, canvas, bounds.Left + 50, bounds.Top + 30));
        Click(window, At(window, canvas, bounds.Left + 60, bounds.Top + 60));
        Press(window, Key.Back, PhysicalKey.Backspace);
        Press(window, Key.Enter, PhysicalKey.Enter);

        var line = Shape(window, panelId);
        Assert.False(line.Closed);
        Assert.Equal(2, line.Anchors.Count);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool);
    }

    [Fact]
    public void Switching_tools_mid_shape_keeps_the_freeform_points_placed_so_far()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.Tool = PageEditorTool.Freeform;

        Click(window, At(window, canvas, bounds.Left + 20, bounds.Top + 20));
        Click(window, At(window, canvas, bounds.Left + 50, bounds.Top + 30));
        window.Editor.Tool = PageEditorTool.Rectangle;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, Shape(window, panelId).Anchors.Count);
        Assert.Equal(PageEditorTool.Rectangle, window.Editor.Tool);
    }
}
