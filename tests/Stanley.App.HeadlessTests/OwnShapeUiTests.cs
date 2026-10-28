using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>Shapes of your own in the character editor (docs/shape-characters.md §9.3): Draw shape on the stage, the Module tab's Your own group, the right-click menu, My shapes and File › My Assets, and the canvas layout's row.</summary>
[Collection("Page Editor Tests")]
public class OwnShapeUiTests
{
    private static (MainWindow Window, CharacterEditorViewModel Editor, CharacterEditorRibbon Ribbon, CharacterFigure Stage) OpenShapeCharacter(MainWindow? window = null)
    {
        window ??= new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var characters = window.ViewModel.Characters!;
        characters.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = characters.Items.Last().Editor;
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().SingleOrDefault()!;
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        return (window, editor, ribbon, stage);
    }

    /// <summary>A figure-space point as the window sees it on the stage.</summary>
    private static Point OnStage(MainWindow window, CharacterFigure stage, Point2D figure)
    {
        var page = stage.MainPlacement!.ToPage(figure);
        return stage.TranslatePoint(new Point(page.X, page.Y), window)!.Value;
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public void Draw_shape_on_the_Module_tab_builds_the_head_point_by_point_on_the_stage()
    {
        var (window, editor, ribbon, stage) = OpenShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        var moduleTab = ribbon.FindControl<TabItem>("ModuleTab")!;
        ribbon.TabControl.SelectedItem = moduleTab;
        Dispatcher.UIThread.RunJobs();
        var group = ribbon.GetVisualDescendants().OfType<OwnShapeRibbonGroup>().Single();
        Assert.True(group.IsEffectivelyVisible);
        var draw = Named<ToggleButton>(group, "DrawShapeButton");
        var overlay = window.GetVisualDescendants().OfType<ShapeDrawOverlay>().Single();
        Assert.False(overlay.IsVisible);

        draw.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsDrawingShape);
        Assert.True(overlay.IsVisible);
        Assert.True(draw.IsChecked);
        Assert.Contains("Draw shape", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "HintText").Text);

        // A five-pointed star round where the head is; its top point dragged out into a curve.
        var centre = OwnShapeEditing.CentreOf(editor.Working.Modules!, ModulePart.Head)!.Value;
        var points = Enumerable.Range(0, 10).Select(i =>
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var r = i % 2 == 0 ? 0.16 : 0.07;
            return new Point2D(centre.X + r * Math.Cos(angle), centre.Y + r * Math.Sin(angle));
        }).ToList();
        var first = OnStage(window, stage, points[0]);
        window.MouseDown(first, MouseButton.Left);
        window.MouseMove(first + new Point(12, 0));
        window.MouseMove(first + new Point(20, 0));
        window.MouseUp(first + new Point(20, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AnchorHandleKind.Smooth, Assert.Single(editor.DrawnShape).HandleKind);
        foreach (var p in points.Skip(1))
            Click(window, OnStage(window, stage, p));
        Assert.Equal(10, editor.DrawnShape.Count);
        window.MouseMove(first);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "draw-shape-closing");

        Click(window, first);

        Assert.False(editor.IsDrawingShape);
        Assert.False(overlay.IsVisible);
        var head = editor.Working.Modules!.Head!;
        Assert.Equal(10, head.Shape.Anchors.Count);
        Assert.Null(head.Shape.Source);
        Assert.Equal(ModulePart.Head, editor.SelectedModule);
        LookTabTests.Snapshot(window, "draw-shape-head");
    }

    [Fact]
    public void Esc_stops_drawing_and_Backspace_takes_a_point_back()
    {
        var (window, editor, _, stage) = OpenShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        var before = Stanley.ProjectModel.Serialization.ProjectJson.Serialize(editor.Working.Modules!);
        editor.DrawShapeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var centre = OwnShapeEditing.CentreOf(editor.Working.Modules!, ModulePart.Torso)!.Value;
        Click(window, OnStage(window, stage, centre));
        Click(window, OnStage(window, stage, new Point2D(centre.X + 0.1, centre.Y)));
        Assert.Equal(2, editor.DrawnShape.Count);

        Press(window, Key.Back, PhysicalKey.Backspace);
        Assert.Single(editor.DrawnShape);
        Press(window, Key.Escape, PhysicalKey.Escape);

        Assert.False(editor.IsDrawingShape);
        Assert.Equal(before, Stanley.ProjectModel.Serialization.ProjectJson.Serialize(editor.Working.Modules!));
    }

    [Fact]
    public void An_imported_and_a_drawn_shape_on_the_stage_and_keeping_one_from_the_right_click_menu()
    {
        var (window, editor, ribbon, stage) = OpenShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        // A blob of quadratics and an arc, as a drawing program would save it.
        Assert.True(editor.ImportShape(ModulePart.Torso, null, "blob.svg", Stanley.ProjectModel.Characters.ArtFile.Svg(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 120"><path d="M50,5 Q95,10 90,60 A40,40 0 0 1 10,70 Q0,15 50,5 Z" fill="#c33"/></svg>""")));
        Dispatcher.UIThread.RunJobs();
        editor.SelectModule(ModulePart.Head);
        editor.BeginDrawShape();
        var centre = OwnShapeEditing.CentreOf(editor.Working.Modules!, ModulePart.Head)!.Value;
        foreach (var (dx, dy) in new[] { (0.0, -0.13), (0.12, 0.0), (0.06, 0.11), (-0.06, 0.11), (-0.12, 0.0) })
            Click(window, OnStage(window, stage, new Point2D(centre.X + dx, centre.Y + dy)));
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(5, editor.Working.Modules!.Head!.Shape.Anchors.Count);
        editor.SelectModule(null);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "own-shapes-stage");

        // Right-click the torso: its menu keeps it in My Assets.
        editor.SelectModule(ModulePart.Torso);
        var items = CharacterEditorView.ModuleMenuItems(editor);
        Assert.Equal(["Import shape...", "Draw shape", "Keep in My Assets"], items.Select(i => i.Header as string));
        var keep = items.Single(i => i.Header as string == "Keep in My Assets");
        Assert.True(keep.Command!.CanExecute(null));
        keep.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var name = editor.Name + "'s torso";
        Assert.Contains(window.ViewModel.MyAssets!.Shapes(), s => s.Name.StartsWith(name, StringComparison.Ordinal));
        Assert.False(CharacterEditorView.ModuleMenuItems(editor).Single(i => i.Header as string == "Keep in My Assets").Command!.CanExecute(null));

        // It's under My shapes in the head's gallery.
        editor.SelectModule(ModulePart.Head);
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("ModuleTab");
        Dispatcher.UIThread.RunJobs();
        var dropdown = ribbon.GetVisualDescendants().OfType<ModuleShapeDropdown>().First(d => d.IsEffectivelyVisible);
        dropdown.DropDown.Flyout!.ShowAt(dropdown.DropDown);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)dropdown.DropDown.Flyout!).Content!;
        Assert.Contains(content.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "My shapes");
        LookTabTests.Snapshot(window, "my-shapes-gallery");
        dropdown.DropDown.Flyout.Hide();

        // And on File › My Assets, under Shapes.
        window.ViewModel.ShowBackstage(BackstagePage.MyAssets);
        Dispatcher.UIThread.RunJobs();
        var chip = window.BackstageControl.FindControl<RadioButton>("MyAssetsShapesChip")!;
        chip.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var grid = window.BackstageControl.FindControl<ItemsControl>("MyAssetsGrid")!;
        Assert.Contains(grid.Items.OfType<MyAssetTile>(), t => t.IsShape && t.Name.StartsWith(name, StringComparison.Ordinal));
        LookTabTests.Snapshot(window, "my-assets-shapes");
    }

    [Fact]
    public void The_canvas_layouts_Module_section_has_the_Your_own_row()
    {
        var window = CanvasLayoutHarness.Open();
        window.ViewModel.Characters!.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = window.ViewModel.Characters.Items.Single().Editor;
        editor.SelectModule(ModulePart.Legs);
        Dispatcher.UIThread.RunJobs();
        var inspector = CanvasLayoutHarness.CharacterInspector(window);
        var row = inspector.GetVisualDescendants().OfType<OwnShapeRow>().Single();

        Assert.True(row.IsEffectivelyVisible);
        var draw = Named<ToggleButton>(row, "DrawShapeButton");
        Assert.True(Named<Button>(row, "ImportShapeButton").IsEnabled);
        Assert.Contains("feet", ToolTip.GetTip(draw) as string);
        draw.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsDrawingShape);
        Assert.True(draw.IsChecked);
        LookTabTests.Snapshot(window, "canvas-own-shape-row");
    }
}
