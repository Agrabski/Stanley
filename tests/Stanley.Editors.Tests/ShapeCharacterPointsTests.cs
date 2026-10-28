using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>Edit Points and Edit sockets on a head or torso in the character editor (docs/shape-characters.md §9.2).</summary>
public sealed class ShapeCharacterPointsTests
{
    private static (EditorSession Session, CharacterEditorViewModel Editor) NewShapeCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        session.Characters.NewShapeCharacterCommand.Execute(null);
        return (session, session.Characters.Items.Single().Editor);
    }

    private static (EditorSession Session, CharacterEditorViewModel Editor) EditingPoints(ModulePart part = ModulePart.Head)
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(part);
        editor.IsEditingPoints = true;
        Assert.True(editor.IsEditingPoints);
        return (session, editor);
    }

    private static string Json(ModularBody? body) => body is null ? "" : ProjectJson.Serialize(body);

    private static void Near(Point2D expected, Point2D actual, double tolerance = 1e-9) =>
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance, $"expected {expected}, got {actual}");

    /// <summary>A drag of point <paramref name="index"/> by (<paramref name="dx"/>, <paramref name="dy"/>) figure units, through two moves; returns where it was let go.</summary>
    private static Point2D DragPoint(CharacterEditorViewModel editor, int index, double dx, double dy)
    {
        var start = editor.EditedOutline!.Anchors[index].Point;
        Assert.True(editor.BeginMovePoint(index));
        editor.UpdateMovePoint(new Point2D(start.X + dx / 2, start.Y + dy / 2));
        var to = new Point2D(start.X + dx, start.Y + dy);
        editor.UpdateMovePoint(to);
        editor.EndPointDrag();
        return to;
    }

    [Fact]
    public void Edit_Points_is_for_the_picked_head_or_torso_and_ends_when_something_else_is_picked()
    {
        var (_, editor) = NewShapeCharacter();
        Assert.False(editor.CanEditPoints);
        editor.IsEditingPoints = true;
        Assert.False(editor.IsEditingPoints);

        editor.SelectModule(ModulePart.Arms);
        Assert.False(editor.CanEditPoints);
        editor.IsEditingPoints = true;
        Assert.False(editor.IsEditingPoints);

        editor.SelectModule(ModulePart.Head);
        Assert.True(editor.CanEditPoints);
        editor.IsEditingPoints = true;
        Assert.True(editor.IsEditingPoints);
        Assert.NotNull(editor.StageEditMarks?.Outline);
        Assert.Equal(editor.Working.Modules!.Head!.Shape.Anchors.Count, editor.EditedOutline!.Anchors.Count);
        Assert.Contains("Editing the head's points", editor.Hint);
        Assert.False(editor.HasSelectedPoint);

        // Another part ends it; so does a sticker, or letting go.
        editor.SelectModule(ModulePart.Torso);
        Assert.False(editor.IsEditingPoints);
        Assert.Null(editor.StageEditMarks);
        editor.IsEditingPoints = true;
        editor.SelectSticker(editor.Working.Wardrobe.Stickers.Values.First().Id);
        Assert.False(editor.IsEditingPoints);
        editor.SelectModule(ModulePart.Head);
        editor.IsEditingPoints = true;
        Assert.True(editor.StopEditing());
        Assert.False(editor.IsEditingPoints);
        Assert.False(editor.StopEditing());
        editor.IsEditingPoints = true;
        editor.SelectModule(null);
        Assert.False(editor.IsEditingPoints);
    }

    [Fact]
    public void Dragging_a_point_is_one_undo_step_that_drops_the_library_source_and_lands_where_it_was_put()
    {
        var (session, editor) = EditingPoints();
        var before = Json(editor.Working.Modules);
        Assert.NotNull(editor.Working.Modules!.Head!.Shape.Source);

        var to = DragPoint(editor, 1, 0.03, 0.01);

        Assert.Equal(1, editor.SelectedPointIndex);
        Assert.Null(editor.Working.Modules!.Head!.Shape.Source);
        Near(to, editor.EditedOutline!.Anchors[1].Point);
        Assert.True(editor.IsEditingPoints);

        session.Workspace.History.Undo();
        Assert.Equal(before, Json(editor.Working.Modules));
        Assert.True(editor.IsEditingPoints);
    }

    [Fact]
    public void A_point_pulled_out_of_the_box_refits_it_and_stays_where_it_was_put_on_the_stage()
    {
        var (_, editor) = EditingPoints();
        var head = editor.Working.Modules!.Head!;
        var top = editor.EditedOutline!.Anchors.Select((a, i) => (a, i)).MinBy(x => x.a.Point.Y).i;

        var to = DragPoint(editor, top, 0, -0.08);

        var now = editor.Working.Modules!.Head!;
        var bounds = AnchorRing.CurveBounds(now.Shape.Anchors);
        Assert.True(bounds.Top >= -1e-9 && bounds.Bottom <= 1 + 1e-9 && bounds.Left >= -1e-9 && bounds.Right <= 1 + 1e-9, $"left the box: {bounds}");
        Assert.True(now.Height > head.Height + 0.05);
        Near(to, editor.EditedOutline!.Anchors[top].Point);
    }

    [Fact]
    public void A_flipped_and_turned_head_edits_in_place()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        editor.FlipModuleHorizontallyCommand.Execute(null);
        editor.FlipModuleVerticallyCommand.Execute(null);
        editor.FlipModuleHorizontallyCommand.Execute(null); // upside down only: the ring runs the other way round
        editor.ModuleRotation = 30;
        editor.IsEditingPoints = true;
        var drawn = ModularRig.Build(editor.Working.Modules!).Layers.SelectMany(l => l.Shapes ?? []).Single(s => s.Part == ModulePart.Head).Anchors;
        Assert.Equal(drawn.Select(a => a.Point), editor.EditedOutline!.Anchors.Select(a => a.Point));

        var to = DragPoint(editor, 2, -0.02, 0.03);

        Near(to, editor.EditedOutline!.Anchors[2].Point);
        Assert.Equal(30, editor.Working.Modules!.Head!.Rotation!.Value, 6);
        Assert.True(editor.Working.Modules.Head.FlipY);
    }

    [Fact]
    public void Pressing_the_outline_adds_a_point_and_Delete_takes_one_away_but_never_below_three()
    {
        var (session, editor) = EditingPoints();
        var count = editor.EditedOutline!.Anchors.Count;
        var before = editor.EditedOutline.Anchors.Select(a => a.Point).ToList();

        var added = editor.BeginInsertPoint(0, 0.5);
        editor.EndPointDrag();
        Assert.Equal(1, added);
        Assert.Equal(added, editor.SelectedPointIndex);
        Assert.Equal(count + 1, editor.Working.Modules!.Head!.Shape.Anchors.Count);
        // The outline didn't change: the old points are where they were.
        Assert.All(before.Select((p, i) => (p, i)), x => Near(x.p, editor.EditedOutline!.Anchors[x.i < added ? x.i : x.i + 1].Point));
        session.Workspace.History.Undo();
        Assert.Equal(count, editor.Working.Modules!.Head!.Shape.Anchors.Count);

        // A press that's dragged pulls the new point out, still one step.
        added = editor.BeginInsertPoint(0, 0.5);
        var at = editor.SelectedPoint!.Point;
        editor.UpdateMovePoint(new Point2D(at.X - 0.02, at.Y - 0.02));
        editor.EndPointDrag();
        Near(new Point2D(at.X - 0.02, at.Y - 0.02), editor.EditedOutline!.Anchors[added].Point);
        session.Workspace.History.Undo();
        Assert.Equal(count, editor.Working.Modules!.Head!.Shape.Anchors.Count);

        // Delete down to three, and no further.
        while (editor.EditedOutline!.Anchors.Count > 3)
        {
            editor.SelectPoint(0);
            Assert.True(editor.CanDeleteSelectedPoint);
            editor.DeletePointCommand.Execute(null);
            Assert.False(editor.HasSelectedPoint);
        }
        editor.SelectPoint(0);
        Assert.False(editor.CanDeleteSelectedPoint);
        Assert.False(editor.DeletePointCommand.CanExecute(null));
        editor.DeleteSelectedPoint();
        Assert.Equal(3, editor.Working.Modules!.Head!.Shape.Anchors.Count);
    }

    [Fact]
    public void A_point_toggles_between_smooth_and_corner_and_the_arrows_nudge_it()
    {
        var (session, editor) = EditingPoints();
        editor.SelectPoint(0);
        Assert.True(editor.IsSelectedPointSmooth); // the egg's points are smooth

        editor.TogglePointKind(0);
        Assert.True(editor.IsSelectedPointCorner);
        var corner = editor.EditedOutline!.Anchors[0];
        Assert.Equal(corner.Point, corner.InHandle);
        editor.IsSelectedPointCorner = true; // already a corner: no empty undo step
        editor.IsSelectedPointSmooth = true;
        Assert.True(editor.IsSelectedPointSmooth);
        session.Workspace.History.Undo();
        Assert.True(editor.IsSelectedPointCorner);

        var p = editor.SelectedPoint!.Point;
        editor.NudgeSelectedPoint(0.005, 0);
        Near(new Point2D(p.X + 0.005, p.Y), editor.EditedOutline!.Anchors[0].Point);
    }

    [Fact]
    public void Dragging_a_handle_of_a_smooth_point_keeps_the_other_in_line()
    {
        var (_, editor) = EditingPoints();
        editor.SelectPoint(0);
        var a = editor.SelectedPoint!;
        Assert.True(editor.BeginMoveHandle(0, HandleSide.Out));
        var to = new Point2D(a.OutHandle.X + 0.01, a.OutHandle.Y + 0.02);
        editor.UpdateMoveHandle(to, independent: false);
        editor.EndPointDrag();

        var now = editor.EditedOutline!.Anchors[0];
        Near(to, now.OutHandle);
        var (ox, oy) = (now.OutHandle.X - now.Point.X, now.OutHandle.Y - now.Point.Y);
        var (ix, iy) = (now.InHandle.X - now.Point.X, now.InHandle.Y - now.Point.Y);
        Assert.Equal(0, ox * iy - oy * ix, 9); // in line through the point
    }

    [Fact]
    public void A_socket_dragged_by_hand_is_one_undo_step_and_Reset_Sockets_puts_it_back()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        editor.IsEditingPoints = true;
        editor.IsEditingSockets = true;
        Assert.False(editor.IsEditingPoints); // one or the other
        Assert.Equal(ModuleSockets.All, editor.EditedSockets.Select(s => s.Name));
        Assert.Equal(6, editor.StageEditMarks!.Sockets.Count);
        Assert.Contains("sockets", editor.Hint);
        Assert.False(editor.CanResetSockets);
        var before = Json(editor.Working.Modules);
        var neck = editor.EditedSockets.Single(s => s.Name == ModuleSockets.Neck).At;
        var headBefore = ModularRig.FrameOf(editor.Working.Modules!, ModulePart.Head)!.Value.Center;

        Assert.True(editor.BeginMoveSocket(ModuleSockets.Neck));
        editor.UpdateMoveSocket(new Point2D(neck.X + 0.01, neck.Y + 0.01));
        editor.UpdateMoveSocket(new Point2D(neck.X + 0.03, neck.Y + 0.02));
        editor.EndSocketDrag();

        Assert.True(editor.Working.Modules!.Torso!.Shape.Sockets!.ContainsKey(ModuleSockets.Neck));
        Assert.True(editor.EditedSockets.Single(s => s.Name == ModuleSockets.Neck).HandSet);
        Near(new Point2D(neck.X + 0.03, neck.Y + 0.02), editor.EditedSockets.Single(s => s.Name == ModuleSockets.Neck).At, 1e-6);
        var headAfter = ModularRig.FrameOf(editor.Working.Modules!, ModulePart.Head)!.Value.Center;
        Near(new Point2D(headBefore.X + 0.03, headBefore.Y + 0.02), headAfter, 1e-6); // the head sits on the neck where it is now
        Assert.True(editor.CanResetSockets);
        Assert.True(editor.ResetSocketsCommand.CanExecute(null));

        editor.ResetSocketsCommand.Execute(null);
        Assert.Null(editor.Working.Modules!.Torso!.Shape.Sockets);
        Assert.False(editor.CanResetSockets);
        session.Workspace.History.Undo();
        Assert.True(editor.CanResetSockets);
        session.Workspace.History.Undo();
        Assert.Equal(before, Json(editor.Working.Modules));
    }

    [Fact]
    public void An_undo_that_takes_the_selected_point_away_lets_go_of_it()
    {
        var (session, editor) = EditingPoints();
        var added = editor.BeginInsertPoint(0, 0.5);
        editor.EndPointDrag();
        var last = editor.EditedOutline!.Anchors.Count - 1;
        editor.SelectPoint(last);
        Assert.True(editor.HasSelectedPoint);
        session.Workspace.History.Undo();
        Assert.False(editor.HasSelectedPoint);
        Assert.True(editor.IsEditingPoints);
        Assert.True(added > 0);
    }

    [Fact]
    public void A_cancelled_insert_leaves_no_point()
    {
        var (_, editor) = EditingPoints();
        var count = editor.Working.Modules!.Head!.Shape.Anchors.Count;
        editor.BeginInsertPoint(0, 0.5);
        Assert.Equal(count + 1, editor.Working.Modules!.Head!.Shape.Anchors.Count);
        editor.EndPointDrag(commit: false);
        Assert.Equal(count, editor.Working.Modules!.Head!.Shape.Anchors.Count);
        Assert.False(editor.HasSelectedPoint);
        Assert.Equal(CreaturePresets.Default.Head!.Shape.Source, editor.Working.Modules.Head.Shape.Source);
    }
}
