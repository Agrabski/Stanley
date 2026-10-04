using Stanley.EditorFramework;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editors.Tests;

/// <summary>The Layers pane: which rows it lists for a page, how its highlight and the page's selection follow each other, and that it keeps up with edits.</summary>
public class LayersViewModelTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    private sealed record Scene(EditorHistory History, PageEditorViewModel Editor, LayersViewModel Layers, PanelId First, PanelId Second, string CharacterName);

    private static Panel PanelOf(PanelId id, Rect2D bounds, IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, params PanelElement[] elements) =>
        new(id, PanelShapes.Rectangle(bounds), null, characters, bubbles, elements);

    /// <summary>
    /// Two panels. The first holds, from the back: a shape behind the characters, one character,
    /// a caption in front of them and a speech bubble. The second is empty.
    /// </summary>
    private static Scene NewScene(bool pointLayersAtThePage = true)
    {
        var history = new EditorHistory();
        var library = new CharacterLibraryViewModel(history, []);
        var hero = library.CreateCharacter();

        var first = PanelId.New();
        var second = PanelId.New();
        var shape = new ShapeElement(ElementId.New(), ElementLayer.Background, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#112233"), 0));
        var caption = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(20, 20, 60, 10), "Meanwhile, across town...",
            new TextStyle(12, ColorValue.FromHex("#000000")));
        var bubble = new Bubble(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(30, 30, 40, 20)).Anchors), BubbleStylePreset.Speech, [], "Hello there");
        var character = new CharacterInstance(hero.Id, new CharacterPlacement(new Point2D(60, 90), 1, false), null, new PoseData(ViewAngle.Front, [], []), null);

        var document = new PageDocument([first, second], new Dictionary<PanelId, Panel>
        {
            [first] = PanelOf(first, new Rect2D(10, 10, 190, 120), [character], [bubble], shape, caption),
            [second] = PanelOf(second, new Rect2D(10, 140, 190, 120), [], [])
        });
        var editor = new PageEditorViewModel(history, PageBounds, document) { Characters = library };
        var layers = new LayersViewModel();
        if (pointLayersAtThePage)
            layers.Editor = editor;
        return new Scene(history, editor, layers, first, second, hero.Name);
    }

    private static LayerRow RowOf(Scene scene, LayerRowKind kind, PanelId panel, int index = -1) =>
        scene.Layers.Rows.Single(r => r.Kind == kind && r.PanelId == panel && r.Index == index);

    [Fact]
    public void A_page_whose_panels_are_all_closed_lists_just_the_panels_in_reading_order()
    {
        var scene = NewScene();

        Assert.Equal(["Panel 1", "Panel 2"], scene.Layers.Rows.Select(r => r.Label));
        Assert.All(scene.Layers.Rows, r => Assert.Equal(LayerRowKind.Panel, r.Kind));
        Assert.Equal(["4 layers", "empty"], scene.Layers.Rows.Select(r => r.Detail));
        Assert.True(scene.Layers.HasRows);
    }

    [Fact]
    public void An_open_panel_lists_its_layers_from_the_front_to_the_back_then_its_background()
    {
        var scene = NewScene();

        scene.Editor.Select(scene.First); // selecting a panel opens it

        Assert.Equal(
        [
            ("Panel 1", LayerRowKind.Panel),
            ("Speech bubble", LayerRowKind.Bubble),
            ("Text", LayerRowKind.Element),
            (scene.CharacterName, LayerRowKind.Character),
            ("Shape", LayerRowKind.Element),
            ("Background", LayerRowKind.Background),
            ("Panel 2", LayerRowKind.Panel)
        ], scene.Layers.Rows.Select(r => (r.Label, r.Kind)));
        Assert.Equal("Hello there", RowOf(scene, LayerRowKind.Bubble, scene.First, 0).Detail);
        Assert.Equal("Meanwhile, across town...", RowOf(scene, LayerRowKind.Element, scene.First, 1).Detail);
        Assert.Equal("plain paper", RowOf(scene, LayerRowKind.Background, scene.First).Detail);
    }

    [Fact]
    public void Rows_point_at_the_list_index_the_page_selects_by()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First);

        // the shape is element 0 and the caption element 1, whichever side of the characters they stack on
        Assert.Equal(0, scene.Layers.Rows.Single(r => r.Label == "Shape").Index);
        Assert.Equal(1, scene.Layers.Rows.Single(r => r.Label == "Text").Index);
    }

    [Fact]
    public void Selecting_on_the_page_highlights_just_that_row_and_opens_its_panel()
    {
        var scene = NewScene();

        scene.Editor.Select(scene.First, bubbleIndex: 0);

        Assert.Equal(["Speech bubble"], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));

        scene.Editor.SelectCharacter(scene.First, 0);
        Assert.Equal([scene.CharacterName], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));

        scene.Editor.SelectElement(scene.First, 0);
        Assert.Equal(["Shape"], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));

        scene.Editor.Select(scene.Second);
        Assert.Equal(["Panel 2"], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));

        scene.Editor.ClearSelection();
        Assert.DoesNotContain(scene.Layers.Rows, r => r.IsSelected);
    }

    [Fact]
    public void Clicking_a_row_selects_it_on_the_page()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Bubble, scene.First, 0), additive: false);
        Assert.Equal(0, scene.Editor.SelectedBubbleIndex);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Character, scene.First, 0), additive: false);
        Assert.Equal(0, scene.Editor.SelectedCharacterIndex);
        Assert.False(scene.Editor.HasSelectedBubble);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Element, scene.First, 1), additive: false);
        Assert.Equal(1, scene.Editor.SelectedElementIndex);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Panel, scene.Second), additive: false);
        Assert.Equal(scene.Second, scene.Editor.SelectedPanelId);
        Assert.False(scene.Editor.HasSelectedElement);
    }

    [Fact]
    public void Clicking_the_background_row_selects_its_panel()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First, bubbleIndex: 0);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Background, scene.First), additive: false);

        Assert.Equal(scene.First, scene.Editor.SelectedPanelId);
        Assert.False(scene.Editor.HasSelectedBubble);
        Assert.True(RowOf(scene, LayerRowKind.Panel, scene.First).IsSelected);
    }

    [Fact]
    public void Shift_clicking_rows_builds_a_multi_selection_and_drops_one_again()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First);

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Bubble, scene.First, 0), additive: false);
        scene.Layers.Pick(RowOf(scene, LayerRowKind.Character, scene.First, 0), additive: true);
        scene.Layers.Pick(RowOf(scene, LayerRowKind.Element, scene.First, 0), additive: true);

        Assert.Equal(3, scene.Editor.SelectionCount);
        Assert.Equal(["Speech bubble", scene.CharacterName, "Shape"], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));

        scene.Layers.Pick(RowOf(scene, LayerRowKind.Character, scene.First, 0), additive: true);
        Assert.Equal(2, scene.Editor.SelectionCount);
        Assert.Equal(["Speech bubble", "Shape"], scene.Layers.Rows.Where(r => r.IsSelected).Select(r => r.Label));
    }

    [Fact]
    public void The_arrow_on_a_panel_closes_and_reopens_its_layers()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First);
        Assert.Equal(7, scene.Layers.Rows.Count);

        var header = RowOf(scene, LayerRowKind.Panel, scene.First);
        Assert.True(header.IsExpanded);
        header.ToggleCommand!.Execute(null);
        Assert.Equal(2, scene.Layers.Rows.Count);
        Assert.False(RowOf(scene, LayerRowKind.Panel, scene.First).IsExpanded);

        RowOf(scene, LayerRowKind.Panel, scene.First).ToggleCommand!.Execute(null);
        Assert.Equal(7, scene.Layers.Rows.Count);
    }

    [Fact]
    public void Edits_and_undo_keep_the_rows_up_to_date()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.Second);
        Assert.Equal(["Panel 1", "Panel 2", "Background"], scene.Layers.Rows.Select(r => r.Label));

        scene.Editor.CreateBubble(scene.Second, new Point2D(50, 180));
        Assert.Equal(["Panel 1", "Panel 2", "Speech bubble", "Background"], scene.Layers.Rows.Select(r => r.Label));

        scene.History.Undo();
        Assert.Equal(["Panel 1", "Panel 2", "Background"], scene.Layers.Rows.Select(r => r.Label));
        scene.History.Redo();
        Assert.Contains(scene.Layers.Rows, r => r.Kind == LayerRowKind.Bubble);
    }

    [Fact]
    public void Rows_are_not_rebuilt_for_every_frame_of_a_drag()
    {
        var scene = NewScene();
        scene.Editor.Select(scene.First, bubbleIndex: 0);
        var before = scene.Layers.Rows.ToList();

        scene.Editor.BeginMoveBubble(scene.First, 0);
        scene.Editor.UpdateMoveBubble(scene.First, 0, 5, 5);

        Assert.NotEqual(scene.Editor.Committed, scene.Editor.Working); // mid-drag
        Assert.True(before.SequenceEqual(scene.Layers.Rows), "the same rows while the gesture is in flight");
        scene.Editor.CancelGesture();
    }

    [Fact]
    public void The_pane_follows_the_page_editor_it_is_pointed_at_and_lets_go_of_the_old_one()
    {
        var scene = NewScene();
        var other = NewScene(pointLayersAtThePage: false);

        scene.Layers.Editor = other.Editor;
        Assert.Equal(["Panel 1", "Panel 2"], scene.Layers.Rows.Select(r => r.Label));

        // what happens on the page it left no longer reaches it
        scene.Editor.Select(scene.First);
        Assert.Equal(2, scene.Layers.Rows.Count);
        other.Editor.Select(other.First);
        Assert.Equal(7, scene.Layers.Rows.Count);

        scene.Layers.Editor = null;
        Assert.Empty(scene.Layers.Rows);
        Assert.False(scene.Layers.HasRows);
    }

    [Fact]
    public void A_character_the_comic_no_longer_has_is_named_as_missing()
    {
        var scene = NewScene();
        var orphanPanel = PanelId.New();
        var orphan = new CharacterInstance(CharacterId.New(), new CharacterPlacement(new Point2D(60, 90), 1, false), null, new PoseData(ViewAngle.Front, [], []), null);
        var editor = new PageEditorViewModel(scene.History, PageBounds, new PageDocument([orphanPanel], new Dictionary<PanelId, Panel>
        {
            [orphanPanel] = PanelOf(orphanPanel, new Rect2D(10, 10, 190, 120), [orphan], [])
        }));
        scene.Layers.Editor = editor;

        editor.Select(orphanPanel);

        Assert.Equal("Missing character", RowOf(scene, LayerRowKind.Character, orphanPanel, 0).Label);
    }
}

public class LayerLabelsTests
{
    [Fact]
    public void Panels_and_clouds_are_numbered_only_where_that_tells_them_apart()
    {
        Assert.Equal("Panel 3", LayerLabels.Panel(3));
        Assert.Equal("Thought cloud", LayerLabels.Cloud(1, 1));
        Assert.Equal("Thought cloud 2", LayerLabels.Cloud(2, 3));
    }

    [Theory]
    [InlineData(0, "empty")]
    [InlineData(1, "1 layer")]
    [InlineData(5, "5 layers")]
    public void A_panel_says_how_many_layers_it_holds(int count, string expected) => Assert.Equal(expected, LayerLabels.PanelDetail(count));

    [Fact]
    public void Lettering_is_shown_as_its_first_line_cut_to_fit_a_row()
    {
        Assert.Equal("Hello", LayerLabels.Preview("Hello"));
        Assert.Equal("First line", LayerLabels.Preview("\nFirst line\nsecond"));
        Assert.Equal("", LayerLabels.Preview("  \n "));
        Assert.Equal("", LayerLabels.Preview(null));

        var long_ = LayerLabels.Preview(new string('x', 100));
        Assert.Equal(LayerLabels.MaxTextChars, long_.Length);
        Assert.EndsWith("…", long_);
    }

    [Theory]
    [InlineData(BubbleStylePreset.Speech, "Speech bubble")]
    [InlineData(BubbleStylePreset.Shout, "Shout bubble")]
    [InlineData(BubbleStylePreset.Whisper, "Whisper bubble")]
    [InlineData(BubbleStylePreset.Thought, "Thought bubble")]
    public void Bubbles_are_named_by_their_style(BubbleStylePreset style, string expected) => Assert.Equal(expected, LayerLabels.BubbleName(style));

    [Fact]
    public void A_background_is_described_by_what_fills_it()
    {
        Assert.Equal("plain paper", LayerLabels.BackgroundDetail(null));
        Assert.Equal("Picture", LayerLabels.BackgroundDetail(new InlineBackground("abc.png")));
        Assert.Equal("Custom", LayerLabels.BackgroundDetail(new ColorBackground(ColorValue.FromHex("#123456"))));
    }
}
