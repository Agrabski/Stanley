using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Edit Points and the Freeform tool (issue #84), through the page editor's view model.</summary>
public class EditPointsTests
{
    private static readonly Rect2D PanelBounds = new(10, 10, 120, 100);

    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) NewEditor()
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var panel = new Panel(panelId, PanelShapes.Rectangle(PanelBounds), null, [], []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), history, panelId);
    }

    /// <summary>A 40mm square at (30,30), selected, its points being edited.</summary>
    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) EditingSquare()
    {
        var (editor, history, panel) = NewEditor();
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(30, 30), new Point2D(70, 70));
        editor.CommitDrawShape();
        editor.IsEditingPoints = true;
        return (editor, history, panel);
    }

    private static ShapeElement Shape(PageEditorViewModel editor, PanelId panel) => (ShapeElement)editor.Working.Panels[panel].Elements[0];

    [Fact]
    public void Edit_points_needs_one_selected_shape_and_ends_when_something_else_is_selected()
    {
        var (editor, _, panel) = NewEditor();
        Assert.False(editor.CanEditPoints);
        editor.IsEditingPoints = true;
        Assert.False(editor.IsEditingPoints);

        (editor, _, panel) = EditingSquare();
        Assert.True(editor.IsEditingPoints);
        Assert.Equal(-1, editor.SelectedPointIndex);

        editor.SelectPoint(2);
        Assert.Equal(2, editor.SelectedPointIndex);
        Assert.True(editor.IsSelectedPointCorner);

        editor.Select(panel);
        Assert.False(editor.IsEditingPoints);
        editor.SelectElement(panel, 0);
        Assert.False(editor.IsEditingPoints); // coming back to the shape starts afresh
        Assert.Equal(-1, editor.SelectedPointIndex);
    }

    [Fact]
    public void Picking_a_drawing_tool_ends_point_editing()
    {
        var (editor, _, _) = EditingSquare();

        editor.Tool = PageEditorTool.Pan;

        Assert.False(editor.IsEditingPoints);
    }

    [Fact]
    public void Dragging_a_point_reshapes_the_shape_in_one_undo_step()
    {
        var (editor, history, panel) = EditingSquare();

        editor.BeginMovePoint(panel, 0, 2);
        editor.UpdateMovePoint(panel, 0, 2, new Point2D(80, 75));
        editor.UpdateMovePoint(panel, 0, 2, new Point2D(90, 80));
        Assert.Equal(new Point2D(70, 70), ((ShapeElement)editor.Committed.Panels[panel].Elements[0]).Anchors[2].Point);
        editor.EndGesture(commit: true);

        Assert.Equal(new Point2D(90, 80), Shape(editor, panel).Anchors[2].Point);
        Assert.Equal(2, editor.SelectedPointIndex);
        Assert.True(editor.IsEditingPoints);
        history.Undo();
        Assert.Equal(new Point2D(70, 70), Shape(editor, panel).Anchors[2].Point);
        Assert.True(editor.IsEditingPoints, "undoing a point move keeps the points showing");
    }

    [Fact]
    public void Pressing_the_outline_adds_a_point_there_that_the_drag_pulls_out()
    {
        var (editor, history, panel) = EditingSquare();

        var added = editor.BeginInsertPoint(panel, 0, 0, 0.5); // the middle of the top edge
        Assert.Equal(1, added);
        Assert.Equal(1, editor.SelectedPointIndex);
        Assert.Equal(5, Shape(editor, panel).Anchors.Count);
        editor.UpdateMovePoint(panel, 0, added, new Point2D(50, 15));
        editor.EndGesture(commit: true);

        var roof = Shape(editor, panel);
        Assert.Equal(5, roof.Anchors.Count);
        Assert.Equal(new Point2D(50, 15), roof.Anchors[1].Point);
        history.Undo();
        Assert.Equal(4, Shape(editor, panel).Anchors.Count); // adding and pulling it were one step
    }

    [Fact]
    public void A_cancelled_point_drag_adds_nothing()
    {
        var (editor, history, panel) = EditingSquare();

        editor.BeginInsertPoint(panel, 0, 1, 0.5);
        editor.EndGesture(commit: false);

        Assert.Equal(4, Shape(editor, panel).Anchors.Count);
        Assert.Equal(-1, editor.SelectedPointIndex);
        history.Undo(); // the only step left is drawing the square
        Assert.Empty(editor.Working.Panels[panel].Elements);
    }

    [Fact]
    public void Dragging_a_curve_handle_bends_the_edges_beside_the_point()
    {
        var (editor, _, panel) = EditingSquare();
        editor.SetPointKind(1, AnchorHandleKind.Smooth);
        var before = Shape(editor, panel).Anchors[1];

        editor.BeginMoveHandle(panel, 0, 1, HandleSide.Out);
        editor.UpdateMoveHandle(panel, 0, 1, HandleSide.Out, new Point2D(before.Point.X + 10, before.Point.Y), independent: false);
        editor.EndGesture(commit: true);

        var after = Shape(editor, panel).Anchors[1];
        Assert.Equal(new Point2D(before.Point.X + 10, before.Point.Y), after.OutHandle);
        Assert.True(after.InHandle.X < after.Point.X, "the other handle swings round to stay in line");
        Assert.Equal(AnchorHandleKind.Smooth, after.HandleKind);
    }

    [Fact]
    public void A_point_is_made_smooth_and_sharp_again_each_one_undo_step()
    {
        var (editor, history, panel) = EditingSquare();

        editor.TogglePointKind(0);
        Assert.Equal(AnchorHandleKind.Smooth, Shape(editor, panel).Anchors[0].HandleKind);
        Assert.True(editor.IsSelectedPointSmooth);
        editor.IsSelectedPointCorner = true;
        Assert.Equal(AnchorHandleKind.Corner, Shape(editor, panel).Anchors[0].HandleKind);

        history.Undo();
        Assert.Equal(AnchorHandleKind.Smooth, Shape(editor, panel).Anchors[0].HandleKind);
    }

    [Fact]
    public void Making_a_corner_sharp_again_leaves_nothing_to_undo()
    {
        var (editor, history, _) = EditingSquare();
        var undoable = history.CanUndo;

        editor.SelectPoint(0);
        editor.IsSelectedPointCorner = true;
        history.Undo(); // the rectangle itself

        Assert.True(undoable);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Deleting_points_stops_at_a_triangle()
    {
        var (editor, _, panel) = EditingSquare();

        editor.SelectPoint(3);
        Assert.True(editor.DeletePointCommand.CanExecute(null));
        editor.DeletePointCommand.Execute(null);
        Assert.Equal(3, Shape(editor, panel).Anchors.Count);
        Assert.Equal(-1, editor.SelectedPointIndex);

        editor.SelectPoint(0);
        Assert.False(editor.CanDeleteSelectedPoint);
        editor.DeleteSelectedPoint();
        Assert.Equal(3, Shape(editor, panel).Anchors.Count);
    }

    [Fact]
    public void A_shape_opened_at_a_point_becomes_a_line_that_can_be_closed_again()
    {
        var (editor, _, panel) = EditingSquare();
        editor.SelectPoint(1);

        Assert.True(editor.OpenShapeCommand.CanExecute(null));
        editor.OpenShapeCommand.Execute(null);
        var line = Shape(editor, panel);
        Assert.False(line.Closed);
        Assert.Equal(5, line.Anchors.Count);
        Assert.Equal(4, editor.SelectedPointIndex); // the loose end, ready to pull away

        Assert.True(editor.CloseShapeCommand.CanExecute(null));
        editor.CloseShapeCommand.Execute(null);
        Assert.True(Shape(editor, panel).Closed);
        Assert.Equal(4, Shape(editor, panel).Anchors.Count); // the two ends, still together, are one point again
    }

    [Fact]
    public void Arrow_keys_move_the_selected_point()
    {
        var (editor, _, panel) = EditingSquare();
        editor.SelectPoint(0);

        editor.NudgeSelectedPoint(-1, 5);

        Assert.Equal(new Point2D(29, 35), Shape(editor, panel).Anchors[0].Point);
        Assert.Equal(new Point2D(70, 30), Shape(editor, panel).Anchors[1].Point);
    }

    [Fact]
    public void An_undo_that_takes_the_selected_point_away_lets_go_of_it()
    {
        var (editor, history, panel) = EditingSquare();
        editor.AddPoint(2, 0.5);
        Assert.Equal(3, editor.SelectedPointIndex);
        editor.SelectPoint(4);

        history.Undo();

        Assert.Equal(4, Shape(editor, panel).Anchors.Count);
        Assert.Equal(-1, editor.SelectedPointIndex);
        Assert.True(editor.IsEditingPoints);
    }

    [Fact]
    public void Undoing_the_shape_itself_ends_point_editing()
    {
        var (editor, history, _) = EditingSquare();

        history.Undo();

        Assert.False(editor.IsEditingPoints);
        Assert.False(editor.HasSelectedElement);
    }

    [Fact]
    public void Freeform_points_become_a_shape_in_the_pen_selected_with_the_Select_tool_back()
    {
        var (editor, history, panel) = NewEditor();
        editor.Tool = PageEditorTool.Freeform;
        Assert.True(editor.IsShapeTool);
        editor.SetFillColorCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Green"));

        var index = editor.AddFreeformShape(panel, [ShapePointEditing.Placed(new Point2D(20, 80)), ShapePointEditing.Placed(new Point2D(50, 30)), ShapePointEditing.Placed(new Point2D(80, 80))], closed: true);

        var shape = Assert.IsType<ShapeElement>(editor.Working.Panels[panel].Elements[index]);
        Assert.True(shape.Closed);
        Assert.Equal(ColorValue.FromHex("#00b050"), shape.Style.Fill);
        Assert.Equal(PageEditorTool.Select, editor.Tool);
        Assert.Equal(index, editor.SelectedElementIndex);
        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
    }

    [Fact]
    public void A_single_freeform_point_makes_nothing()
    {
        var (editor, history, panel) = NewEditor();
        editor.Tool = PageEditorTool.Freeform;

        Assert.Equal(-1, editor.AddFreeformShape(panel, [ShapePointEditing.Placed(new Point2D(20, 80))], closed: false));
        Assert.Empty(editor.Working.Panels[panel].Elements);
        Assert.False(history.CanUndo);
        Assert.Equal(PageEditorTool.Freeform, editor.Tool);
    }
}
