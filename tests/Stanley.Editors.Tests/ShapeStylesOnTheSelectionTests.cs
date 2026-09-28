using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors.Tests;

/// <summary>Shape Fill and Shape Outline change what's selected - a shape, a text's box, a panel - and only with nothing selected the pen, as in Word and Figma.</summary>
public sealed class ShapeStylesOnTheSelectionTests
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

    private static PaletteColor Named(string name) => DrawingPalette.Colors.First(c => c.Name == name);

    [Fact]
    public void Shape_Fill_on_a_selected_panel_colours_its_background_and_leaves_the_pen_alone()
    {
        var (editor, history, panel) = NewEditor();
        var pen = editor.CurrentShapeStyle;
        editor.Select(panel);

        editor.SetFillColorCommand.Execute(Named("Red"));

        Assert.Equal(new ColorBackground(Named("Red").Color!.Value), editor.Working.Panels[panel].Background);
        Assert.Equal(Named("Red").Color, editor.CurrentShapeStyle.Fill);
        Assert.Equal("Red", editor.FillName);
        editor.ClearSelection();
        Assert.Equal(pen, editor.CurrentShapeStyle); // nothing drawn next turned red

        editor.Select(panel);
        editor.SetFillColorCommand.Execute(DrawingPalette.None);
        Assert.Null(editor.Working.Panels[panel].Background); // plain paper again
        history.Undo();
        history.Undo();
        Assert.Null(editor.Working.Panels[panel].Background);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Shape_Outline_on_a_selected_panel_sets_its_border()
    {
        var (editor, _, panel) = NewEditor();
        editor.Select(panel);
        Assert.Equal(PageRenderer.PanelBorderColor, editor.CurrentShapeStyle.Stroke);

        editor.SetStrokeColorCommand.Execute(DrawingPalette.None);
        Assert.True(editor.Working.Panels[panel].Borderless);
        Assert.False(editor.SelectedPanelHasBorder);
        Assert.Null(editor.CurrentShapeStyle.Stroke);

        editor.SetStrokeDashCommand.Execute(LineDash.Dash); // like Word: a dash for "No Outline" turns the outline on
        editor.SetStrokeColorCommand.Execute(Named("Blue"));
        editor.SetStrokeWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Mm == 2));
        var styled = editor.Working.Panels[panel];
        Assert.False(styled.Borderless);
        Assert.Equal(new PanelBorderStyle(Named("Blue").Color!.Value, 2, LineDash.Dash), styled.BorderStyle);

        // Back to the usual black ink line: nothing special to keep.
        editor.SetStrokeColorCommand.Execute(new PaletteColor("Black", PageRenderer.PanelBorderColor));
        editor.SetStrokeWeightCommand.Execute(new ShapeWeightChoice("0.7 mm", PageRenderer.PanelBorderMm));
        editor.SetStrokeDashCommand.Execute(LineDash.Solid);
        Assert.Null(editor.Working.Panels[panel].BorderStyle);
    }

    [Fact]
    public void Changing_a_panels_outline_keeps_its_gradient_background()
    {
        var (editor, _, panel) = NewEditor();
        var sky = DrawingPalette.Backgrounds.Single(b => b.Name == "Day sky").Background;
        editor.SetPanelBackground(panel, sky);
        editor.Select(panel);

        editor.SetStrokeColorCommand.Execute(Named("Red"));

        Assert.Equal(sky, editor.Working.Panels[panel].Background);
    }

    [Fact]
    public void The_title_pages_band_changes_colour_with_Shape_Fill()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var title = session.Navigator.InsertTitlePage(TitlePageDesign.Banner).Editor;
        var band = title.Working.PanelOrder.First(id => title.Working.Panels[id].Background is ColorBackground);
        title.Select(band);

        title.SetFillColorCommand.Execute(Named("Dark Red"));

        Assert.Equal(new ColorBackground(Named("Dark Red").Color!.Value), title.Working.Panels[band].Background);
        Assert.True(title.Working.Panels[band].Borderless); // filling it didn't give it a border
    }

    [Fact]
    public void Shape_Fill_on_selected_text_fills_its_box_as_the_Text_tab_does()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(60, 60), style: TextStylePresets.Style(TextStylePreset.Plain));

        editor.SetFillColorCommand.Execute(Named("Yellow"));
        editor.SetStrokeWeightCommand.Execute(DrawingPalette.Weights.Single(w => w.Mm == 1));

        var style = ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;
        Assert.Equal(Named("Yellow").Color, style.BoxFill);
        Assert.Equal(1, style.BoxStrokeWidthMm);
        Assert.NotNull(style.BoxStroke);
    }

    [Fact]
    public void They_dont_apply_to_a_bubble_and_with_a_drawing_tool_on_they_set_the_pen()
    {
        var (editor, _, panel) = NewEditor();
        editor.CreateBubble(panel, new Point2D(60, 60));
        Assert.False(editor.CanUseShapeStyles);

        editor.Select(panel);
        Assert.True(editor.CanUseShapeStyles);
        editor.Tool = PageEditorTool.Rectangle;
        editor.SetFillColorCommand.Execute(Named("Green"));

        Assert.Null(editor.Working.Panels[panel].Background);
        Assert.Equal(Named("Green").Color, editor.CurrentShapeStyle.Fill);

        // The Panel tab's own always mean the panel.
        editor.SetPanelFillCommand.Execute(Named("Orange"));
        editor.SetPanelOutlineDashCommand.Execute(LineDash.RoundDot);
        Assert.Equal(new ColorBackground(Named("Orange").Color!.Value), editor.Working.Panels[panel].Background);
        Assert.Equal(new PanelBorderStyle(PageRenderer.PanelBorderColor, PageRenderer.PanelBorderMm, LineDash.RoundDot), editor.Working.Panels[panel].BorderStyle);
        Assert.Equal(Named("Orange").Color, editor.CurrentPanelStyle.Fill);
    }

    [Fact]
    public void Splitting_a_panel_gives_both_halves_its_border()
    {
        var (editor, _, panel) = NewEditor();
        editor.Select(panel);
        editor.SetStrokeColorCommand.Execute(Named("Red"));

        editor.SplitPanel(panel, BoundaryOrientation.Vertical, 0.5);

        Assert.All(editor.Working.Panels.Values, p => Assert.Equal(Named("Red").Color, p.BorderStyle?.Color));
    }
}
