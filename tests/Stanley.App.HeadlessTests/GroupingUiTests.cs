using Avalonia;
using Avalonia.Controls;
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

/// <summary>Grouping a shape with a character (issue #125) through the real window: the group is one thing to press, drag and right-click.</summary>
[Collection("Page Editor Tests")]
public class GroupingUiTests
{
    private static (MainWindow Window, PageCanvasControl Canvas, PanelId Panel, int Shape, int Character) OpenWithAGroup()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().First();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);

        var character = editor.InsertCharacter(CharacterId.New(), panelId);
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(bounds.Left + 4, bounds.Top + 4), new Point2D(bounds.Left + 16, bounds.Top + 16));
        var shape = editor.CommitDrawShape();
        editor.Tool = PageEditorTool.Select;

        editor.ToggleSelect(panelId, characterIndex: character);
        editor.GroupSelectionCommand.Execute(null);
        editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        return (window, canvas, panelId, shape, character);
    }

    /// <summary>A point on the shape's outline - an unfilled rectangle is only there on its edge.</summary>
    private static Point2D OnOutline(Rect2D box) => new(box.Left, box.MidY);

    private static Point At(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    [Fact]
    public void Dragging_the_shape_of_a_shape_and_character_group_moves_the_character_too()
    {
        var (window, canvas, panelId, shape, character) = OpenWithAGroup();
        var editor = window.Editor;
        var shapeBox = PanelElements.Bounds(editor.Working.Panels[panelId].Elements[shape]);
        var groundBefore = editor.Working.Panels[panelId].CharacterInstances[character].Placement.Ground;
        Assert.True(shapeBox.Right < editor.CharacterBounds(editor.Working.Panels[panelId].CharacterInstances[character]).Left); // pressing the shape can't hit the character

        var start = At(window, canvas, OnOutline(shapeBox).X, OnOutline(shapeBox).Y);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 25, start.Y));
        window.MouseMove(new Point(start.X + 40, start.Y));
        window.MouseUp(new Point(start.X + 40, start.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var panel = editor.Working.Panels[panelId];
        var shapeMoved = PanelElements.Bounds(panel.Elements[shape]).X - shapeBox.X;
        var groundMoved = panel.CharacterInstances[character].Placement.Ground.X - groundBefore.X;
        Assert.True(shapeMoved > 1);
        Assert.Equal(shapeMoved, groundMoved, 3);
        Assert.Equal(2, editor.SelectionCount); // the press picked the whole group

        window.History.Undo(); // one undo step for the drag
        Assert.Equal(groundBefore, editor.Working.Panels[panelId].CharacterInstances[character].Placement.Ground);
    }

    [Fact]
    public void Right_clicking_a_member_of_a_group_offers_ungroup_for_the_whole_group()
    {
        var (window, canvas, panelId, shape, character) = OpenWithAGroup();
        var editor = window.Editor;
        var shapeBox = PanelElements.Bounds(editor.Working.Panels[panelId].Elements[shape]);

        var items = canvas.ContextMenuItems(OnOutline(shapeBox)).OfType<MenuItem>().ToList();

        Assert.Equal(2, editor.SelectionCount);
        Assert.False(items.Single(i => i.Header as string == "Group").IsEnabled); // it's one group already
        var ungroup = items.Single(i => i.Header as string == "Ungroup");
        Assert.Equal(KeyGesture.Parse("Ctrl+Shift+G"), ungroup.InputGesture);

        ungroup.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var panel = editor.Working.Panels[panelId];
        Assert.Null(panel.Elements[shape].Link);
        Assert.Null(panel.CharacterInstances[character].Link);
    }

    private static void Chord(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Ctrl_Shift_G_takes_a_shape_and_character_group_apart_and_Ctrl_G_groups_them_again()
    {
        var (window, canvas, panelId, shape, character) = OpenWithAGroup();
        var editor = window.Editor;
        editor.SelectElement(panelId, shape); // the whole group
        canvas.Focus();
        Assert.Equal(2, editor.SelectionCount);

        Chord(window, Key.G, PhysicalKey.G, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Null(editor.Working.Panels[panelId].Elements[shape].Link);
        Assert.Null(editor.Working.Panels[panelId].CharacterInstances[character].Link);
        Assert.Equal(2, editor.SelectionCount); // still selected, no longer one thing

        Chord(window, Key.G, PhysicalKey.G, RawInputModifiers.Control);

        var link = editor.Working.Panels[panelId].Elements[shape].Link;
        Assert.NotNull(link);
        Assert.Equal(link, editor.Working.Panels[panelId].CharacterInstances[character].Link);
    }
}
