using Stanley.EditorFramework;
using Stanley.Editing;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editors.Tests;

/// <summary>Moving layers in their panel's stack: through the page editor, through the Layers pane's buttons, and through the older To front / To back commands.</summary>
public class LayerMovesTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    /// <summary>One panel holding, in the usual order from the back: a shape behind the characters, one character, a caption in front of it and a speech bubble.</summary>
    private sealed record Scene(EditorHistory History, PageEditorViewModel Editor, LayersViewModel Layers, PanelId Panel)
    {
        public Panel Current => Editor.Working.Panels[Panel];

        public IReadOnlyList<StackItem> Order => PanelStack.Order(Current);
    }

    private static Scene NewScene(Func<Panel, Panel>? arrange = null)
    {
        // The character library gets a history of its own: adding the character is an undoable step, and these tests count the page's.
        var library = new CharacterLibraryViewModel(new EditorHistory(), []);
        var hero = library.CreateCharacter();
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var shape = new ShapeElement(ElementId.New(), ElementLayer.Background, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#112233"), 0));
        var caption = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(20, 20, 60, 10), "Caption", new TextStyle(12, ColorValue.FromHex("#000000")));
        var bubble = new Bubble(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(30, 30, 40, 20)).Anchors), BubbleStylePreset.Speech, [], "Hello");
        var character = new CharacterInstance(hero.Id, new CharacterPlacement(new Point2D(60, 90), 1, false), null, new PoseData(ViewAngle.Front, [], []), null);
        var panel = new Panel(panelId, PanelShapes.Rectangle(new Rect2D(10, 10, 190, 120)), null, [character], [bubble], [shape, caption]);
        if (arrange != null)
            panel = arrange(panel);
        var editor = new PageEditorViewModel(history, PageBounds, new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel })) { Characters = library };
        return new Scene(history, editor, new LayersViewModel { Editor = editor }, panelId);
    }

    private static readonly StackItem Shape = new(StackKind.Element, 0);
    private static readonly StackItem Caption = new(StackKind.Element, 1);
    private static readonly StackItem Hero = new(StackKind.Character, 0);
    private static readonly StackItem Speech = new(StackKind.Bubble, 0);

    [Fact]
    public void Moving_a_layer_is_one_undo_step_and_the_selection_stays_on_it()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel, bubbleIndex: 0);
        Assert.Equal([Shape, Hero, Caption, Speech], scene.Order);

        scene.Editor.MoveInStack(scene.Panel, Speech, StackMove.ToBack);

        Assert.Equal([Speech, Shape, Hero, Caption], scene.Order);
        Assert.Equal(0, scene.Editor.SelectedBubbleIndex);
        Assert.NotNull(scene.Current.Stack);

        scene.History.Undo();
        Assert.Null(scene.Current.Stack);
        Assert.Equal([Shape, Hero, Caption, Speech], scene.Order);
        Assert.False(scene.History.CanUndo);

        scene.History.Redo();
        Assert.Equal([Speech, Shape, Hero, Caption], scene.Order);
    }

    [Fact]
    public void Moving_a_layer_that_is_already_there_adds_nothing_to_undo()
    {
        var scene = NewScene();

        scene.Editor.MoveInStack(scene.Panel, Speech, StackMove.ToFront);
        scene.Editor.MoveInStack(scene.Panel, Shape, StackMove.Backward);
        scene.Editor.MoveInStack(scene.Panel, new StackItem(StackKind.Bubble, 9), StackMove.Forward); // not there at all
        scene.Editor.MoveInStack(PanelId.New(), Speech, StackMove.Forward); // no such panel

        Assert.False(scene.History.CanUndo);
        Assert.Null(scene.Current.Stack);
    }

    [Fact]
    public void The_Layers_panes_buttons_move_the_selected_layer_and_the_list_follows()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel, characterIndex: 0);

        scene.Layers.BringForwardCommand.Execute(null);
        Assert.Equal([Shape, Caption, Hero, Speech], scene.Order);
        Assert.Equal(["Speech bubble", scene.Layers.Rows.Single(r => r.Kind == LayerRowKind.Character).Label, "Text", "Shape"],
            scene.Layers.Rows.Where(r => r.Kind != LayerRowKind.Panel && r.Kind != LayerRowKind.Background).Select(r => r.Label));

        scene.Layers.SendToBackCommand.Execute(null);
        Assert.Equal([Hero, Shape, Caption, Speech], scene.Order);

        scene.Layers.SendBackwardCommand.Execute(null); // already at the back: nothing happens
        scene.Layers.BringToFrontCommand.Execute(null);
        Assert.Equal([Shape, Caption, Speech, Hero], scene.Order);
        Assert.Equal(0, scene.Editor.SelectedCharacterIndex); // still the one selected
        Assert.True(RowSelected(scene, LayerRowKind.Character));
    }

    private static bool RowSelected(Scene scene, LayerRowKind kind) => scene.Layers.Rows.Single(r => r.Kind == kind).IsSelected;

    private static LayerRow Row(Scene scene, LayerRowKind kind, int index = -1) => scene.Layers.Rows.Single(r => r.Kind == kind && r.Index == index);

    /// <summary>Drags <paramref name="row"/> to the gap above <c>Rows[gap]</c> and lets go there.</summary>
    private static void DragAndDrop(Scene scene, LayerRow row, int gap)
    {
        Assert.True(scene.Layers.BeginDrag(row));
        scene.Layers.DragOver(gap);
        scene.Layers.Drop();
    }

    [Fact]
    public void Dragging_a_layer_to_the_top_of_its_panels_list_puts_it_in_front_in_one_undo_step_and_selects_it()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel);
        // [Panel 1, Speech bubble, Text, hero, Shape, Background]
        var shape = Row(scene, LayerRowKind.Element, 0);
        Assert.Equal(4, scene.Layers.Rows.IndexOf(shape));

        Assert.True(scene.Layers.BeginDrag(shape));
        Assert.True(scene.Layers.IsDragging);
        Assert.True(shape.IsDragged);
        scene.Layers.DragOver(1);
        Assert.True(scene.Layers.Rows[1].ShowDropLine);
        scene.Layers.Drop();

        Assert.Equal([Hero, Caption, Speech, Shape], scene.Order);
        Assert.Equal(ElementLayer.Foreground, scene.Current.Elements[0].Layer); // in front of the character now
        Assert.Equal(0, scene.Editor.SelectedElementIndex);
        Assert.True(Row(scene, LayerRowKind.Element, 0).IsSelected);
        Assert.False(scene.Layers.IsDragging);
        Assert.DoesNotContain(scene.Layers.Rows, r => r.IsDragged || r.ShowDropLine);

        scene.History.Undo();
        Assert.Equal([Shape, Hero, Caption, Speech], scene.Order);
        Assert.False(scene.History.CanUndo);
    }

    [Fact]
    public void Dragging_a_layer_down_the_list_puts_it_behind_what_it_passes()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel);

        DragAndDrop(scene, Row(scene, LayerRowKind.Bubble, 0), 4); // above the shape: under the character

        Assert.Equal([Shape, Speech, Hero, Caption], scene.Order);
        Assert.Equal(0, scene.Editor.SelectedBubbleIndex);
    }

    [Fact]
    public void A_layer_dragged_past_its_panels_rows_stops_at_the_front_or_just_above_the_background()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel);

        DragAndDrop(scene, Row(scene, LayerRowKind.Element, 1), 0); // over the panel's own row
        Assert.Equal([Shape, Hero, Speech, Caption], scene.Order);

        DragAndDrop(scene, Row(scene, LayerRowKind.Character, 0), 99); // below the end of the list
        Assert.Equal([Hero, Shape, Speech, Caption], scene.Order);
        Assert.Equal(0, scene.Editor.SelectedCharacterIndex);
    }

    [Fact]
    public void Dropping_a_layer_where_it_was_shows_no_line_and_adds_nothing_to_undo()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel);
        var hero = Row(scene, LayerRowKind.Character, 0);
        var at = scene.Layers.Rows.IndexOf(hero);

        Assert.True(scene.Layers.BeginDrag(hero));
        scene.Layers.DragOver(at);
        Assert.DoesNotContain(scene.Layers.Rows, r => r.ShowDropLine);
        scene.Layers.DragOver(at + 1);
        Assert.DoesNotContain(scene.Layers.Rows, r => r.ShowDropLine);
        scene.Layers.Drop();

        Assert.False(scene.History.CanUndo);
        Assert.Null(scene.Current.Stack);
        Assert.Equal(0, scene.Editor.SelectedCharacterIndex);
    }

    [Fact]
    public void Panels_and_backgrounds_are_not_dragged_and_a_cancelled_drag_moves_nothing()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel);

        Assert.False(scene.Layers.BeginDrag(Row(scene, LayerRowKind.Panel)));
        Assert.False(scene.Layers.BeginDrag(Row(scene, LayerRowKind.Background)));
        Assert.False(scene.Layers.IsDragging);

        Assert.True(scene.Layers.BeginDrag(Row(scene, LayerRowKind.Bubble, 0)));
        scene.Layers.DragOver(5);
        scene.Layers.CancelDrag();
        scene.Layers.Drop(); // nothing being dragged any more

        Assert.False(scene.History.CanUndo);
        Assert.DoesNotContain(scene.Layers.Rows, r => r.IsDragged || r.ShowDropLine);
    }

    [Fact]
    public void A_button_that_cannot_act_is_disabled_and_says_why()
    {
        var scene = NewScene();
        var layers = scene.Layers;

        // nothing selected
        Assert.All(new[] { layers.BringToFrontCommand, layers.BringForwardCommand, layers.SendBackwardCommand, layers.SendToBackCommand }, c => Assert.False(c.CanExecute(null)));
        Assert.Contains("Select a layer", layers.BringToFrontTip);

        // the frontmost layer
        scene.Editor.Select(scene.Panel, bubbleIndex: 0);
        Assert.False(layers.BringToFrontCommand.CanExecute(null));
        Assert.False(layers.BringForwardCommand.CanExecute(null));
        Assert.True(layers.SendBackwardCommand.CanExecute(null));
        Assert.True(layers.SendToBackCommand.CanExecute(null));
        Assert.Contains("Already in front", layers.BringForwardTip);
        Assert.Equal("Send backward", layers.SendBackwardTip);
        Assert.Equal("Send to back", layers.SendToBackTip);

        // the rearmost one
        scene.Editor.SelectElement(scene.Panel, 0);
        Assert.True(layers.BringToFrontCommand.CanExecute(null));
        Assert.True(layers.BringForwardCommand.CanExecute(null));
        Assert.False(layers.SendBackwardCommand.CanExecute(null));
        Assert.False(layers.SendToBackCommand.CanExecute(null));
        Assert.Contains("Already behind", layers.SendToBackTip);
        Assert.Equal("Bring to front", layers.BringToFrontTip);

        // a panel on its own isn't a layer
        scene.Editor.Select(scene.Panel);
        Assert.False(layers.BringForwardCommand.CanExecute(null));
    }

    [Fact]
    public void The_buttons_notice_when_a_move_changes_what_they_can_do()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Panel, bubbleIndex: 0);
        var raised = new List<string?>();
        scene.Layers.BringToFrontCommand.CanExecuteChanged += (_, _) => raised.Add("front");
        scene.Layers.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        scene.Layers.SendToBackCommand.Execute(null);

        Assert.Contains("front", raised);
        Assert.Contains(nameof(LayersViewModel.BringToFrontTip), raised);
        Assert.True(scene.Layers.BringToFrontCommand.CanExecute(null)); // it's at the back now
        Assert.False(scene.Layers.SendToBackCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- the older commands

    [Fact]
    public void To_front_and_to_back_in_a_panel_stacked_by_hand_go_to_the_ends_of_its_whole_stack()
    {
        var scene = NewScene();
        scene.Editor.MoveInStack(scene.Panel, Speech, StackMove.ToBack); // arranged by hand now: [bubble, shape, character, caption]

        scene.Editor.ReorderCharacter(scene.Panel, 0, toFront: true);
        Assert.Equal([Speech, Shape, Caption, Hero], scene.Order);

        scene.Editor.SendBubbleToBack(scene.Panel, 0);
        scene.Editor.BringBubbleToFront(scene.Panel, 0);
        Assert.Equal([Shape, Caption, Hero, Speech], scene.Order);

        scene.Editor.ReorderElement(scene.Panel, 1, toFront: false);
        Assert.Equal([Caption, Shape, Hero, Speech], scene.Order);
    }

    [Fact]
    public void To_front_and_to_back_in_a_panel_stacked_the_usual_way_keep_the_usual_meaning()
    {
        var scene = NewScene();

        scene.Editor.ReorderCharacter(scene.Panel, 0, toFront: true);
        scene.Editor.ReorderElement(scene.Panel, 0, toFront: true); // the shape: in front of the characters, still behind the bubble

        // the shape went to the end of the element list and into the front layer
        Assert.Null(scene.Current.Stack);
        Assert.Equal("Caption", ((TextElement)scene.Current.Elements[0]).Text);
        Assert.Equal(ElementLayer.Foreground, scene.Current.Elements[1].Layer);
        Assert.Equal([Hero, new StackItem(StackKind.Element, 0), new StackItem(StackKind.Element, 1), Speech], scene.Order);
    }

    [Fact]
    public void To_front_on_a_bubble_or_character_that_is_already_in_front_adds_nothing_to_undo()
    {
        var scene = NewScene();

        scene.Editor.BringBubbleToFront(scene.Panel, 0);
        scene.Editor.ReorderCharacter(scene.Panel, 0, toFront: true);
        scene.Editor.SendBubbleToBack(scene.Panel, 0);
        scene.Editor.ReorderCharacter(scene.Panel, 0, toFront: false);

        Assert.False(scene.History.CanUndo);
    }

    [Fact]
    public void Behind_and_in_front_of_the_characters_in_a_panel_stacked_by_hand_place_a_drawing_right_beside_them()
    {
        var scene = NewScene();
        scene.Editor.MoveInStack(scene.Panel, Speech, StackMove.ToBack); // [bubble, shape, character, caption]

        scene.Editor.SetElementLayer(scene.Panel, 0, ElementLayer.Foreground); // the shape, in front of the character
        Assert.Equal([Speech, Hero, Shape, Caption], scene.Order);
        Assert.Equal(ElementLayer.Foreground, scene.Current.Elements[0].Layer);

        scene.Editor.SetElementLayer(scene.Panel, 1, ElementLayer.Background); // the caption, behind it
        Assert.Equal([Speech, Caption, Hero, Shape], scene.Order);
        Assert.Equal(ElementLayer.Background, scene.Current.Elements[1].Layer);

        scene.Editor.SetElementLayer(scene.Panel, 1, ElementLayer.Background); // already behind: nothing to undo

        scene.History.Undo(); // the caption's move
        scene.History.Undo(); // the shape's move - not a third, empty step
        Assert.Equal([Speech, Shape, Hero, Caption], scene.Order);
        Assert.Equal(ElementLayer.Background, scene.Current.Elements[0].Layer);
    }

    // ---------------------------------------------------------------- grouping in a stacked panel

    [Fact]
    public void Grouping_and_ungrouping_drawings_in_a_panel_stacked_by_hand_keep_their_place_in_the_stack()
    {
        var scene = NewScene(panel =>
        {
            var a = new ShapeElement(ElementId.New(), ElementLayer.Background, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#111111"), 0));
            var b = a with { Id = ElementId.New() };
            var c = a with { Id = ElementId.New() };
            var hero = panel.CharacterInstances[0] with { Id = CharacterInstanceId.New() };
            // from the back: c, a, the character, b - so a group of a and c stands where a was (the frontmost of the two), behind the character
            return panel with
            {
                Elements = [a, b, c],
                Bubbles = [],
                CharacterInstances = [hero],
                Stack = [PanelStack.Token(c), PanelStack.Token(a), PanelStack.Token(hero)!, PanelStack.Token(b)]
            };
        });
        var (a, b, c) = (0, 1, 2);
        scene.Editor.SelectElement(scene.Panel, a);
        scene.Editor.ToggleSelect(scene.Panel, elementIndex: c);

        scene.Editor.GroupSelectionCommand.Execute(null);

        // the list is now [b, group]: the group (1) behind the character, b (0) in front of it
        Assert.Equal(2, scene.Current.Elements.Count);
        Assert.IsType<GroupElement>(scene.Current.Elements[1]);
        Assert.Equal([new StackItem(StackKind.Element, 1), Hero, new StackItem(StackKind.Element, 0)], scene.Order);

        scene.Editor.UngroupSelectionCommand.Execute(null);

        // the list is now [b, a, c]: a and c back where the group stood, in the order they were in it
        Assert.Equal(3, scene.Current.Elements.Count);
        Assert.Equal([new StackItem(StackKind.Element, 1), new StackItem(StackKind.Element, 2), Hero, new StackItem(StackKind.Element, 0)], scene.Order);

        scene.History.Undo(); // ungroup
        scene.History.Undo(); // group
        Assert.Equal(3, scene.Current.Elements.Count);
        Assert.Equal([new StackItem(StackKind.Element, 2), new StackItem(StackKind.Element, 0), Hero, new StackItem(StackKind.Element, 1)], scene.Order);
    }
}
