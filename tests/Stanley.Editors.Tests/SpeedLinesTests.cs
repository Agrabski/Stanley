using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Inserting and restyling a burst of speed lines through the page editor's view model.</summary>
public class SpeedLinesTests
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

    [Fact]
    public void Insert_speed_lines_centres_a_burst_in_the_selected_panel_selected_as_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();

        editor.InsertSpeedLinesCommand.Execute(null);

        var speedLines = Assert.IsType<SpeedLinesElement>(Assert.Single(editor.Working.Panels[panel].Elements));
        Assert.Equal(PanelBounds.MidX, speedLines.Focus.MidX, 6);
        Assert.Equal(PanelBounds.MidY, speedLines.Focus.MidY, 6);
        Assert.Equal(ElementLayer.Background, speedLines.Layer);
        Assert.True(editor.IsSpeedLinesContext);
        Assert.Same(speedLines, editor.SelectedSpeedLines);

        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
        Assert.False(history.CanUndo);
        Assert.False(editor.HasSelectedElement);
    }

    [Fact]
    public void With_nothing_selected_speed_lines_go_in_the_first_panel()
    {
        var (editor, _, panel) = NewEditor();
        editor.ClearSelection();

        var index = editor.InsertSpeedLines();

        Assert.Equal(0, index);
        Assert.IsType<SpeedLinesElement>(editor.Working.Panels[panel].Elements[0]);
    }

    [Fact]
    public void Colour_count_and_thickness_restyle_the_selection_and_become_the_style_for_the_next_burst()
    {
        var (editor, history, panel) = NewEditor();
        editor.InsertSpeedLinesCommand.Execute(null);
        var red = DrawingPalette.StandardColors.Single(c => c.Name == "Red");

        editor.SetSpeedLinesColorCommand.Execute(red);
        editor.SpeedLinesCount = 150;
        editor.SpeedLinesThickness = 2.5;

        var style = editor.SelectedSpeedLines!.Style;
        Assert.Equal(red.Color, style.Color);
        Assert.Equal(150, style.Count);
        Assert.Equal(2.5, style.WidthMm, 6);

        var secondIndex = editor.InsertSpeedLines();
        var second = (SpeedLinesElement)editor.Working.Panels[panel].Elements[secondIndex];
        Assert.Equal(red.Color, second.Style.Color); // the style just set is the one new speed lines start with

        history.Undo(); // the second burst
        history.Undo(); // thickness
        history.Undo(); // count
        history.Undo(); // colour
        Assert.Equal(ColorValue.FromHex("#1c1c1c"), ((SpeedLinesElement)editor.Working.Panels[panel].Elements[0]).Style.Color);
    }

    [Fact]
    public void A_count_slider_drag_previews_live_and_is_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        editor.InsertSpeedLinesCommand.Execute(null);

        editor.BeginSliderDrag();
        editor.SpeedLinesCount = 120;
        editor.SpeedLinesCount = 200;
        Assert.Equal(200, ((SpeedLinesElement)editor.Working.Panels[panel].Elements[0]).Style.Count);
        Assert.Equal(80, ((SpeedLinesElement)editor.Committed.Panels[panel].Elements[0]).Style.Count); // not committed yet
        editor.EndSliderDrag();

        Assert.Equal(200, ((SpeedLinesElement)editor.Committed.Panels[panel].Elements[0]).Style.Count);
        history.Undo(); // the drag
        history.Undo(); // the insert
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Shuffle_changes_the_seed_and_nothing_else_as_one_undo_step()
    {
        var (editor, history, _) = NewEditor();
        editor.InsertSpeedLinesCommand.Execute(null);
        var before = editor.SelectedSpeedLines!.Style;

        Assert.True(editor.ShuffleSpeedLinesCommand.CanExecute(null));
        editor.ShuffleSpeedLinesCommand.Execute(null);

        var after = editor.SelectedSpeedLines!.Style;
        Assert.NotEqual(before.Seed, after.Seed);
        Assert.Equal(before with { Seed = after.Seed }, after);

        history.Undo(); // the shuffle
        Assert.Equal(before.Seed, editor.SelectedSpeedLines!.Style.Seed);
    }

    [Fact]
    public void Delete_and_layer_commands_work_on_speed_lines_like_any_other_element()
    {
        var (editor, _, panel) = NewEditor();
        editor.InsertSpeedLinesCommand.Execute(null);
        Assert.True(editor.IsElementBehind);

        editor.IsElementInFront = true;
        Assert.Equal(ElementLayer.Foreground, editor.Working.Panels[panel].Elements[0].Layer);

        Assert.True(editor.DeleteSelectionCommand.CanExecute(null));
        editor.DeleteSelectionCommand.Execute(null);
        Assert.Empty(editor.Working.Panels[panel].Elements);
    }
}
