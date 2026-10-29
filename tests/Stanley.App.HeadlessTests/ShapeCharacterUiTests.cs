using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.HeadlessTests;

/// <summary>A character built from shapes in the character editor (docs/shape-characters.md §9): the Shapes and Module tabs, the stage, and the canvas layout's sections.</summary>
[Collection("Page Editor Tests")]
public class ShapeCharacterUiTests
{
    private static (MainWindow Window, CharacterEditorViewModel Editor, CharacterEditorRibbon Ribbon) OpenShapeCharacter()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var characters = window.ViewModel.Characters!;
        characters.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = characters.Items.Single().Editor;
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().Single();
        return (window, editor, ribbon);
    }

    private static Point FindPoint(Control control, Func<Point, bool> test)
    {
        for (var y = 0.0; y < control.Bounds.Height; y += 3)
            for (var x = 0.0; x < control.Bounds.Width; x += 3)
                if (test(new Point(x, y)))
                    return new Point(x, y);
        throw new InvalidOperationException("No such point on the stage.");
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void A_shape_character_has_the_Shapes_tab_and_a_human_the_Body_tab()
    {
        var (window, editor, ribbon) = OpenShapeCharacter();
        var shapes = ribbon.FindControl<TabItem>("ShapesTab")!;
        Assert.True(shapes.IsVisible);
        Assert.False(ribbon.FindControl<TabItem>("BodyTab")!.IsVisible);
        Assert.Same(shapes, ribbon.TabControl.SelectedItem);
        Assert.Equal(CreaturePresetsCount(), ribbon.FindControl<ItemsControl>("CreatureGallery")!.ItemCount);
        Assert.Equal(4, ribbon.FindControl<ItemsControl>("PartButtons")!.ItemCount);
        LookTabTests.Snapshot(window, "shapes-tab");

        // A human character opened next gets the Body tab back.
        window.ViewModel.Characters!.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var human = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().Single();
        Assert.True(human.FindControl<TabItem>("BodyTab")!.IsVisible);
        Assert.False(human.FindControl<TabItem>("ShapesTab")!.IsVisible);
        Assert.Same(human.FindControl<TabItem>("BodyTab"), human.TabControl.SelectedItem);
    }

    private static int CreaturePresetsCount() => Stanley.StickerLibrary.CreaturePresets.All.Count;

    [Fact]
    public void Clicking_the_torso_on_the_stage_picks_it_and_its_gallery_swaps_the_shape()
    {
        var (window, editor, ribbon) = OpenShapeCharacter();
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        var torso = FindPoint(stage, p => stage.ModuleAt(p) == ModulePart.Torso && stage.StickerAt(p) is null);

        Click(window, stage.TranslatePoint(torso, window)!.Value);

        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        var moduleTab = ribbon.FindControl<TabItem>("ModuleTab")!;
        Assert.True(moduleTab.IsVisible);
        ribbon.TabControl.SelectedItem = moduleTab;
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "module-tab-torso");

        var dropdown = ribbon.GetVisualDescendants().OfType<ModuleShapeDropdown>().Single(d => d.IsEffectivelyVisible);
        dropdown.DropDown.Flyout!.ShowAt(dropdown.DropDown);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "module-shape-gallery");
        var content = (Control)((Flyout)dropdown.DropDown.Flyout!).Content!;
        var triangle = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is ModuleShapeChoice { Library.Key: "torso/geometric/soft-triangle" });
        triangle.Command!.Execute(triangle.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Stanley.StickerLibrary.ShapeLibrary.SourcePrefix + "torso/geometric/soft-triangle", editor.Working.Modules!.Torso!.Shape.Source);
        Assert.Equal(ModulePart.Torso, editor.SelectedModule);
        dropdown.DropDown.Flyout.Hide();

        // A click on the background lets go; the Module tab goes and Shapes comes back.
        Click(window, stage.TranslatePoint(new Point(4, 4), window)!.Value);
        Assert.False(editor.HasSelectedModule);
        Assert.False(moduleTab.IsVisible);
        Assert.Same(ribbon.FindControl<TabItem>("ShapesTab"), ribbon.TabControl.SelectedItem);
    }

    [Fact]
    public void Dragging_the_head_moves_it_and_a_missing_parts_plus_puts_it_back()
    {
        var (window, editor, _) = OpenShapeCharacter();
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        var head = stage.TranslatePoint(FindPoint(stage, p => stage.ModuleAt(p) == ModulePart.Head && stage.StickerAt(p) is null), window)!.Value;

        window.MouseDown(head, MouseButton.Left);
        window.MouseMove(head + new Point(10, 0));
        window.MouseMove(head + new Point(30, 0));
        window.MouseUp(head + new Point(30, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ModulePart.Head, editor.SelectedModule);
        Assert.True(editor.Working.Modules!.Head!.Offset!.Value.X > 0.01);
        LookTabTests.Snapshot(window, "module-head-moved");

        editor.SelectModule(ModulePart.Legs);
        editor.RemoveModuleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(editor.Working.Modules!.Legs);
        LookTabTests.Snapshot(window, "module-legs-ghost");
        var ghost = FindPoint(stage, p => stage.ModuleGhostAt(p) is { Part: ModulePart.Legs });
        Click(window, stage.TranslatePoint(ghost, window)!.Value);
        Assert.NotNull(editor.Working.Modules!.Legs);
        Assert.Equal(ModulePart.Legs, editor.SelectedModule);
    }

    [Fact]
    public void The_canvas_layout_shows_the_Shapes_and_Module_sections()
    {
        var window = CanvasLayoutHarness.Open();
        window.ViewModel.Characters!.NewShapeCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = window.ViewModel.Characters.Items.Single().Editor;
        var inspector = CanvasLayoutHarness.CharacterInspector(window);

        Assert.True(inspector.FindInSections<ShapesSection>("ShapesProperties")!.IsVisible);
        Assert.False(inspector.FindInSections<BodySection>("BodyProperties")!.IsVisible);
        var module = inspector.FindInSections<ModuleSection>("ModuleProperties")!;
        Assert.False(module.IsVisible);

        var arms = inspector.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is ModulePartChoice { Part: ModulePart.Arms });
        arms.Command!.Execute(arms.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(module.IsVisible);
        Assert.Equal(ModulePart.Arms, editor.SelectedModule);
        LookTabTests.Snapshot(window, "canvas-module-section");
    }
}
