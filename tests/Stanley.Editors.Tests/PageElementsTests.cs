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
        var yellow = DrawingPalette.StandardColors.Single(c => c.Name == "Yellow");

        editor.SetFillColorCommand.Execute(yellow);
        editor.SetStrokeWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Mm == 3));

        Assert.Equal(yellow.Color, editor.SelectedShape!.Style.Fill);
        Assert.Equal(3, editor.SelectedShape.Style.StrokeWidthMm);
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
        editor.SetStrokeColorCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Red"));

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
        Assert.Equal(TextEditing.Bigger(10), bigger.Style.FontSizePt);
    }

    [Fact]
    public void Shape_Fill_and_Shape_Outline_set_a_texts_box_and_its_border_separately_as_in_Word()
    {
        var (editor, _, panel) = NewEditor();
        editor.ApplyTextPresetCommand.Execute(TextStylePreset.Plain);
        var index = editor.CreateText(panel, new Point2D(60, 50));
        TextStyle Style() => ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;

        editor.SetBoxFillCommand.Execute(DrawingPalette.ThemeColors[0]);
        Assert.Equal(ColorValue.FromHex("#ffffff"), Style().BoxFill);
        Assert.Null(Style().BoxStroke);

        editor.SetBoxDashCommand.Execute(LineDash.Dash); // picking a dash for "no outline" turns the border on
        Assert.Equal(TextStylePresets.Ink, Style().BoxStroke);
        editor.SetBoxWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Mm == 1));
        editor.SetBoxOutlineCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Dark Red"));
        Assert.Equal(new TextStyle(10, TextStylePresets.Ink, BoxFill: ColorValue.FromHex("#ffffff"), BoxStroke: ColorValue.FromHex("#c00000"),
            BoxStrokeWidthMm: 1, BoxDash: LineDash.Dash), Style());

        editor.SetBoxOutlineCommand.Execute(DrawingPalette.None);
        editor.SetBoxFillCommand.Execute(DrawingPalette.None);
        Assert.Null(Style().BoxFill);
        Assert.Null(Style().BoxStroke);
    }

    [Fact]
    public void Text_Fill_none_leaves_hollow_letters_and_Text_Outline_has_a_weight()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(60, 50));
        TextStyle Style() => ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;

        editor.SetTextOutlineWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Mm == 0.5));
        editor.SetTextColorCommand.Execute(DrawingPalette.None);

        Assert.Null(Style().Color);
        Assert.Equal(TextStylePresets.Ink, Style().Outline); // a weight for "no outline" turns it on
        Assert.Equal(0.5, Style().OutlineWidthMm);
    }

    [Fact]
    public void Shape_Outline_dashes_restyle_the_selected_shape()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 40, 30));

        editor.SetStrokeDashCommand.Execute(LineDash.RoundDot);

        Assert.Equal(LineDash.RoundDot, editor.SelectedShape!.Style.Dash);
        history.Undo();
        Assert.Equal(LineDash.Solid, ((ShapeElement)editor.Working.Panels[panel].Elements[0]).Style.Dash);
    }

    [Fact]
    public void The_palette_is_Words_theme_colours_with_their_shades_and_the_standard_colours()
    {
        Assert.Equal(10, DrawingPalette.ThemeColors.Count);
        Assert.Equal(5, DrawingPalette.ThemeShades.Count);
        Assert.All(DrawingPalette.ThemeShades, row => Assert.Equal(10, row.Count));
        Assert.Equal(10, DrawingPalette.StandardColors.Count);
        // Word's Office theme: "Blue, Accent 1, Lighter 80%" is #dae3f3, "Darker 50%" is #203864.
        Assert.Equal(ColorValue.FromHex("#dae3f3"), DrawingPalette.ThemeShades[0][4].Color);
        Assert.Equal(ColorValue.FromHex("#203864"), DrawingPalette.ThemeShades[4][4].Color);
        Assert.Equal(ColorValue.FromHex("#f2f2f2"), DrawingPalette.ThemeShades[0][0].Color); // white, darker 5%

        var custom = DrawingPalette.Remember(ColorValue.FromHex("#123456"));
        Assert.Same(custom, DrawingPalette.RecentColors[0]);
        DrawingPalette.Remember(ColorValue.FromHex("#123456"));
        Assert.Single(DrawingPalette.RecentColors, c => c.Color == ColorValue.FromHex("#123456"));
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

    [Fact]
    public void Bring_to_front_moves_background_element_to_foreground_layer()
    {
        var (editor, history, panel) = NewEditor();
        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        Assert.Equal(ElementLayer.Background, shape.Layer);
        editor.SelectElement(panel, 0);

        editor.ReorderElement(panel, 0, toFront: true);

        var reordered = Assert.IsType<ShapeElement>(editor.Working.Panels[panel].Elements[0]);
        Assert.Equal(ElementLayer.Foreground, reordered.Layer);
        Assert.Equal(0, editor.SelectedElementIndex);

        history.Undo();
        var reverted = Assert.IsType<ShapeElement>(editor.Working.Panels[panel].Elements[0]);
        Assert.Equal(ElementLayer.Background, reverted.Layer);
    }

    [Fact]
    public void Bring_to_front_places_element_at_the_end_of_the_list()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        DrawRectangle(editor, panel, new Rect2D(30, 30, 20, 20));
        DrawRectangle(editor, panel, new Rect2D(40, 40, 20, 20));
        Assert.Equal(3, editor.Working.Panels[panel].Elements.Count);
        editor.SelectElement(panel, 0);

        editor.ReorderElement(panel, 0, toFront: true);

        // Element should be at the end after bring to front
        Assert.Equal(2, editor.SelectedElementIndex);
        var movedElement = editor.Working.Panels[panel].Elements[2];
        Assert.Equal(ElementLayer.Foreground, movedElement.Layer);
    }

    [Fact]
    public void Send_to_back_moves_foreground_element_to_background_layer()
    {
        var (editor, history, panel) = NewEditor();
        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        editor.SelectElement(panel, 0);
        editor.ReorderElement(panel, 0, toFront: true);
        Assert.Equal(ElementLayer.Foreground, editor.SelectedElement!.Layer);

        editor.ReorderElement(panel, editor.SelectedElementIndex, toFront: false);

        var reordered = Assert.IsType<ShapeElement>(editor.Working.Panels[panel].Elements[0]);
        Assert.Equal(ElementLayer.Background, reordered.Layer);

        history.Undo();
        var reverted = Assert.IsType<ShapeElement>(editor.Working.Panels[panel].Elements[0]);
        Assert.Equal(ElementLayer.Foreground, reverted.Layer);
    }

    [Fact]
    public void Send_to_back_places_element_at_the_start_of_the_list()
    {
        var (editor, _, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        DrawRectangle(editor, panel, new Rect2D(30, 30, 20, 20));
        DrawRectangle(editor, panel, new Rect2D(40, 40, 20, 20));
        editor.SelectElement(panel, 2);

        editor.ReorderElement(panel, 2, toFront: false);

        // Element should be at the start after send to back
        Assert.Equal(0, editor.SelectedElementIndex);
        var movedElement = editor.Working.Panels[panel].Elements[0];
        Assert.Equal(ElementLayer.Background, movedElement.Layer);
    }

    [Fact]
    public void Bring_to_front_on_the_front_element_adds_nothing_to_undo()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        DrawRectangle(editor, panel, new Rect2D(30, 30, 20, 20));
        editor.SelectElement(panel, 0);
        editor.ReorderElement(panel, 0, toFront: true);
        var before = editor.Working;

        editor.ReorderElement(panel, editor.SelectedElementIndex, toFront: true);

        Assert.Same(before, editor.Working);
        history.Undo(); // the first To front, not the second
        Assert.All(editor.Working.Panels[panel].Elements, e => Assert.Equal(ElementLayer.Background, e.Layer));
    }

    [Fact]
    public void Send_to_back_on_the_back_element_adds_nothing_to_undo()
    {
        var (editor, history, panel) = NewEditor();
        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var second = DrawRectangle(editor, panel, new Rect2D(30, 30, 20, 20));
        editor.SelectElement(panel, 1);
        editor.ReorderElement(panel, 1, toFront: false);
        var before = editor.Working;

        editor.ReorderElement(panel, editor.SelectedElementIndex, toFront: false);

        Assert.Same(before, editor.Working);
        history.Undo(); // the first To back, not the second
        Assert.Equal(second.Id, editor.Working.Panels[panel].Elements[1].Id);
    }
}
