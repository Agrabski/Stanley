using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors.Tests;

/// <summary>Drawing shapes, placing text and filling backgrounds on a page, through the page editor's view model.</summary>
public class PageElementsTests
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

    private static List<Point2D> Loop(Point2D center, double radius) =>
        Enumerable.Range(0, 121).Select(i => new Point2D(center.X + Math.Cos(i * Math.PI / 60) * radius, center.Y + Math.Sin(i * Math.PI / 60) * radius)).ToList();

    private static ShapeElement DrawRectangle(PageEditorViewModel editor, PanelId panel, Rect2D box)
    {
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom));
        var index = editor.CommitDrawShape();
        return (ShapeElement)editor.Working.Panels[panel].Elements[index];
    }

    [Fact]
    public void A_freehand_stroke_shows_while_drawing_and_is_kept_as_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        editor.Tool = PageEditorTool.Draw;

        editor.BeginDrawShape(panel);
        var trail = Loop(new Point2D(60, 60), 20);
        editor.UpdateDrawFreehand(trail.Take(40).ToList(), 0.2, 2);
        Assert.Single(editor.Working.Panels[panel].Elements); // live preview
        Assert.Empty(editor.Committed.Panels[panel].Elements);
        editor.UpdateDrawFreehand(trail, 0.2, 2);
        var index = editor.CommitDrawShape();

        var shape = Assert.IsType<ShapeElement>(Assert.Single(editor.Working.Panels[panel].Elements));
        Assert.Equal(0, index);
        Assert.True(shape.Closed);
        Assert.Equal(ElementLayer.Background, shape.Layer);
        Assert.Equal(PageEditorTool.Draw, editor.Tool); // the pen stays on
        Assert.False(editor.HasSelectedElement);

        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_pen_click_without_a_drag_leaves_no_trace()
    {
        var (editor, history, panel) = NewEditor();
        editor.Tool = PageEditorTool.Draw;

        editor.BeginDrawShape(panel);
        editor.UpdateDrawFreehand([new Point2D(50, 50)], 0.2, 2);
        Assert.Equal(-1, editor.CommitDrawShape());

        Assert.Empty(editor.Working.Panels[panel].Elements);
        Assert.False(history.CanUndo);
        Assert.Null(editor.LastError);
    }

    [Fact]
    public void A_rectangle_tool_drag_selects_the_new_shape_and_hands_back_to_select()
    {
        var (editor, _, panel) = NewEditor();

        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));

        Assert.Equal(new Rect2D(20, 20, 40, 30), AnchorRing.BoundingBox(shape.Anchors));
        Assert.Equal(PageEditorTool.Select, editor.Tool);
        Assert.True(editor.IsShapeContext);
        Assert.Same(shape, editor.SelectedShape);
    }

    [Fact]
    public void Shift_makes_a_square_and_keeps_a_line_at_45_degrees()
    {
        var (editor, _, panel) = NewEditor();

        editor.Tool = PageEditorTool.Ellipse;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(20, 20), new Point2D(60, 30), constrain: true);
        var circle = (ShapeElement)editor.Working.Panels[panel].Elements[editor.CommitDrawShape()];
        var box = AnchorRing.BoundingBox(circle.Anchors);
        Assert.Equal(box.Width, box.Height, 6);

        editor.Tool = PageEditorTool.Line;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(20, 20), new Point2D(50, 48), constrain: true);
        var line = (ShapeElement)editor.Working.Panels[panel].Elements[editor.CommitDrawShape()];
        var end = line.Anchors[1].Point;
        Assert.Equal(end.X - 20, end.Y - 20, 6);
    }

    [Fact]
    public void Colours_restyle_the_selected_shape_and_become_the_pen_for_the_next()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        var yellow = DrawingPalette.Colors.Single(c => c.Name == "Yellow");

        editor.SetFillColorCommand.Execute(yellow);
        editor.SetStrokeWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Name == "Heavy"));

        Assert.Equal(yellow.Color, editor.SelectedShape!.Style.Fill);
        Assert.Equal(2.5, editor.SelectedShape.Style.StrokeWidthMm);
        Assert.Equal("Yellow", editor.FillName);
        var next = DrawRectangle(editor, panel, new Rect2D(70, 20, 20, 20));
        Assert.Equal(yellow.Color, next.Style.Fill);

        history.Undo(); // the second rectangle
        history.Undo(); // the weight
        history.Undo(); // the fill
        Assert.Null(((ShapeElement)editor.Working.Panels[panel].Elements[0]).Style.Fill);
    }

    [Fact]
    public void An_element_moves_between_behind_and_in_front_of_the_characters()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        Assert.True(editor.IsElementBehind);

        editor.IsElementInFront = true;

        Assert.Equal(ElementLayer.Foreground, editor.Working.Panels[panel].Elements[0].Layer);
        Assert.True(editor.IsElementInFront);
        history.Undo();
        Assert.Equal(ElementLayer.Background, editor.Working.Panels[panel].Elements[0].Layer);
    }

    [Fact]
    public void Picking_a_drawing_tool_lets_go_of_a_selected_element_so_the_ribbon_shows_the_pen()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        editor.SetStrokeColorCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "Red"));

        editor.Tool = PageEditorTool.Draw;

        Assert.False(editor.HasSelectedElement);
        Assert.Equal("Red", editor.StrokeName); // the last colour picked is the pen's
    }

    [Fact]
    public void Moving_and_resizing_an_element_are_one_undo_step_each()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));

        editor.BeginMoveElement(panel, 0);
        editor.UpdateMoveElement(panel, 0, 5, 5);
        editor.UpdateMoveElement(panel, 0, 10, 6);
        editor.EndGesture(commit: true);
        Assert.Equal(new Rect2D(30, 26, 40, 30), PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]));

        editor.BeginResizeElement(panel, 0);
        editor.UpdateResizeElement(panel, 0, new Rect2D(30, 26, 60, 40));
        editor.EndGesture(commit: true);
        Assert.Equal(new Rect2D(30, 26, 60, 40), PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]));

        history.Undo();
        Assert.Equal(new Rect2D(30, 26, 40, 30), PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]));
        history.Undo();
        Assert.Equal(new Rect2D(20, 20, 40, 30), PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]));
    }

    [Fact]
    public void An_element_dragged_out_of_its_panel_stays_reachable()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));

        editor.BeginMoveElement(panel, 0);
        editor.UpdateMoveElement(panel, 0, 500, 0);
        editor.EndGesture(commit: true);

        Assert.Equal(PanelBounds.Right - ElementEditing.MinVisibleMm, PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]).Left, 6);
    }

    [Fact]
    public void Delete_nudge_and_reorder_work_on_the_selected_element()
    {
        var (editor, _, panel) = NewEditor();
        var first = DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        var second = DrawRectangle(editor, panel, new Rect2D(30, 30, 40, 30));

        editor.SendToBackCommand.Execute(null);
        Assert.Same(second, editor.Working.Panels[panel].Elements[0]);
        Assert.Equal(0, editor.SelectedElementIndex);

        editor.NudgeSelection(1, 0);
        Assert.Equal(31, PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]).Left, 6);

        editor.DeleteSelectionCommand.Execute(null);
        Assert.Same(first, Assert.Single(editor.Working.Panels[panel].Elements));
        Assert.False(editor.HasSelectedElement);
        Assert.True(editor.HasSelectedPanel);
    }

    [Fact]
    public void Undoing_the_selected_element_away_drops_the_selection()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        Assert.True(editor.HasSelectedElement);

        history.Undo();

        Assert.False(editor.HasSelectedElement);
    }

    [Fact]
    public void Elements_stay_selectable_and_editable_on_a_locked_layout()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));
        editor.IsLayoutLocked = true;

        editor.SelectElement(panel, 0);
        Assert.True(editor.HasSelectedElement);
        editor.NudgeSelection(2, 0);
        Assert.Equal(22, PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]).Left, 6);
    }

    [Fact]
    public void Clicking_with_the_text_tool_places_one_line_of_text_there_ready_to_type()
    {
        var (editor, _, panel) = NewEditor();
        editor.Tool = PageEditorTool.Text;

        var index = editor.CreateText(panel, new Point2D(60, 50));

        var text = Assert.IsType<TextElement>(editor.Working.Panels[panel].Elements[index]);
        Assert.True(editor.IsTextContext);
        Assert.Equal(ElementLayer.Foreground, text.Layer);
        Assert.Equal(ElementRenderer.NeededHeight(text), text.Bounds.Height, 6);
        Assert.Equal(50, text.Bounds.MidY, 6);
        Assert.Equal(60, text.Bounds.Left, 6); // a caption is left-aligned: it starts at the click
        Assert.Equal("Caption", editor.TextPresetName);
    }

    [Fact]
    public void Text_near_a_panel_edge_is_slid_inside()
    {
        var (editor, _, panel) = NewEditor();

        var index = editor.CreateText(panel, new Point2D(PanelBounds.Right - 2, PanelBounds.Bottom - 1));

        var box = PanelElements.Bounds(editor.Working.Panels[panel].Elements[index]);
        Assert.True(box.Right <= PanelBounds.Right + 1e-9);
        Assert.True(box.Bottom <= PanelBounds.Bottom + 1e-9);
    }

    [Fact]
    public void Typing_more_lines_grows_the_box()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(60, 50));
        var oneLine = PanelElements.Bounds(editor.Working.Panels[panel].Elements[index]).Height;

        editor.SetElementText(panel, index, "One\nTwo\nThree");

        var text = (TextElement)editor.Working.Panels[panel].Elements[index];
        Assert.Equal("One\nTwo\nThree", text.Text);
        Assert.True(text.Bounds.Height > oneLine + 5);
        Assert.Equal(ElementRenderer.NeededHeight(text), text.Bounds.Height, 6);
    }

    [Fact]
    public void Insert_caption_goes_in_the_panel_corner_and_asks_for_the_text_editor()
    {
        var (editor, _, panel) = NewEditor();
        (PanelId Panel, int Index)? requested = null;
        editor.ElementTextEditRequested += (p, i) => requested = (p, i);

        editor.InsertTextCommand.Execute(TextStylePreset.Caption);

        var text = (TextElement)editor.Working.Panels[panel].Elements[0];
        Assert.Equal((panel, 0), requested);
        Assert.Equal(PanelBounds.Left + 1.5, text.Bounds.Left, 6);
        Assert.Equal(PanelBounds.Top + 1.5, text.Bounds.Top, 6);
        Assert.NotNull(text.Style.BoxFill);
    }

    [Fact]
    public void Text_style_controls_restyle_the_selected_text_and_bigger_letters_get_a_bigger_box()
    {
        var (editor, history, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(60, 50));
        var before = (TextElement)editor.Working.Panels[panel].Elements[index];

        editor.IsTextBold = true;
        editor.IsTextAlignRight = true;
        editor.BiggerTextCommand.Execute(null);
        editor.ApplyTextPresetCommand.Execute(TextStylePreset.SoundEffect);

        var after = (TextElement)editor.Working.Panels[panel].Elements[index];
        Assert.Equal(TextStylePresets.Style(TextStylePreset.SoundEffect), after.Style);
        Assert.True(after.Bounds.Height > before.Bounds.Height);
        Assert.Equal("Sound effect", editor.TextPresetName);

        history.Undo();
        var bigger = (TextElement)editor.Working.Panels[panel].Elements[index];
        Assert.True(bigger.Style.Bold);
        Assert.Equal(TextAlign.Right, bigger.Style.Align);
        Assert.Equal(TextEditing.Bigger(3.5), bigger.Style.FontSizeMm);
    }

    [Fact]
    public void A_text_box_colour_adds_an_outlined_box_and_none_takes_it_away()
    {
        var (editor, _, panel) = NewEditor();
        editor.ApplyTextPresetCommand.Execute(TextStylePreset.Plain);
        var index = editor.CreateText(panel, new Point2D(60, 50));

        editor.SetTextBoxCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "White"));
        var boxed = ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;
        Assert.NotNull(boxed.BoxFill);
        Assert.NotNull(boxed.BoxStroke);

        editor.SetTextBoxCommand.Execute(DrawingPalette.None);
        var bare = ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;
        Assert.Null(bare.BoxFill);
        Assert.Null(bare.BoxStroke);
    }

    [Fact]
    public void Edit_text_opens_the_editor_for_selected_text()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(60, 50));
        var requested = false;
        editor.ElementTextEditRequested += (_, _) => requested = true;

        Assert.True(editor.EditTextCommand.CanExecute(null));
        editor.EditTextCommand.Execute(null);

        Assert.True(requested);
        Assert.Equal(index, editor.SelectedElementIndex);
    }

    [Fact]
    public void A_background_fills_the_selected_panel_even_on_a_locked_layout()
    {
        var (editor, history, panel) = NewEditor();
        var sky = editor.BackgroundChoices.Single(b => b.Name == "Day sky");
        Assert.False(editor.SetBackgroundCommand.CanExecute(sky));

        editor.Select(panel);
        editor.SetBackgroundCommand.Execute(sky);
        Assert.IsType<GradientBackground>(editor.Working.Panels[panel].Background);
        Assert.Equal("Day sky", editor.BackgroundName);

        history.Undo();
        Assert.Null(editor.Working.Panels[panel].Background);

        DrawRectangle(editor, panel, new Rect2D(20, 20, 10, 10));
        editor.IsLayoutLocked = true;
        editor.SelectElement(panel, 0);
        editor.SetBackgroundCommand.Execute(editor.BackgroundChoices.Single(b => b.Name == "Night"));
        Assert.IsType<GradientBackground>(editor.Working.Panels[panel].Background);
    }

    [Fact]
    public void Resizing_a_panel_carries_its_elements_along()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        editor.ClearSelection();

        editor.BeginMovePanel(panel);
        editor.UpdateMovePanel(panel, 30, 20, snapTolerance: 0);
        editor.EndGesture(commit: true);

        Assert.Equal(new Rect2D(50, 40, 20, 20), PanelElements.Bounds(editor.Working.Panels[panel].Elements[0]));
    }
}
