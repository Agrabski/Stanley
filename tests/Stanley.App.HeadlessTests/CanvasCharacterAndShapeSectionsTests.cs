using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// The canvas layout's inspector for a placed character, a drawn shape, the pen a drawing tool
/// draws with, and the Arrange row any selection gets - each doing what its ribbon tab does.
/// </summary>
[Collection("Page Editor Tests")]
public class CanvasCharacterAndShapeSectionsTests
{
    private static (MainWindow Window, PageEditorViewModel Editor, PanelId Panel) Open()
    {
        var window = CanvasLayoutHarness.Open();
        return (window, window.Editor, window.Editor.Working.PanelOrder[0]);
    }

    private static PageInspector Inspector(MainWindow window) => CanvasLayoutHarness.PageInspector(window);

    private static T Find<T>(MainWindow window, string name) where T : Control =>
        Inspector(window).FindInSections<T>(name) ?? throw new InvalidOperationException($"No {name} in the inspector.");

    /// <summary>
    /// Whether the named control shows: it and every section and row around it visible. Logical
    /// ancestors, since a section that has never shown hasn't put its content in the visual tree yet.
    /// </summary>
    private static bool Shows<T>(MainWindow window, string name) where T : Control
    {
        Dispatcher.UIThread.RunJobs();
        return Find<T>(window, name).GetSelfAndLogicalAncestors().OfType<Control>().All(c => c.IsVisible);
    }

    /// <summary>A real click in the middle of <paramref name="control"/> - in the window, or in the flyout it's in.</summary>
    private static void Click(Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var topLevel = TopLevel.GetTopLevel(control)!;
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseDown(at, MouseButton.Left);
        topLevel.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static int PlaceCharacter(MainWindow window, PanelId panelId)
    {
        var created = window.ViewModel.Characters!.CreateCharacter();
        var index = window.Editor.InsertCharacter(created.Id, panelId);
        Dispatcher.UIThread.RunJobs();
        return index;
    }

    /// <summary>A rectangle drawn the way the tool draws one - selected afterwards, the Select tool back in hand.</summary>
    private static int DrawRectangle(PageEditorViewModel editor, PanelId panelId, double left, double top)
    {
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(left, top), new Point2D(left + 20, top + 12));
        var index = editor.CommitDrawShape();
        Dispatcher.UIThread.RunJobs();
        return index;
    }

    private static void PickColour(ColorMenuButton button, string name)
    {
        button.Menu.ShowAt(button.Button);
        Dispatcher.UIThread.RunJobs();
        var palette = (Control)((MenuItem)button.Menu.Items[0]!).Header!;
        palette.GetLogicalDescendants().OfType<Button>().Single(b => (b.Tag as PaletteColor)?.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static ColorValue Colour(string name) => DrawingPalette.Colors.Single(c => c.Name == name).Color!.Value;

    [Fact]
    public void The_Arrange_row_shows_for_any_selection_and_copies_duplicates_and_deletes_it()
    {
        var (window, editor, panelId) = Open();
        Assert.False(Shows<Button>(window, "CutButton"));

        PlaceCharacter(window, panelId);
        Assert.True(Shows<Button>(window, "CutButton"));
        var delete = Find<Button>(window, "DeleteButton");
        Assert.Contains("stays in the Characters pane", ToolTip.GetTip(delete) as string);
        Assert.Equal("Del", Shortcut.GetKeys(delete));
        Assert.Equal(["Ctrl+X", "Ctrl+C", "Ctrl+V", "Ctrl+D", "Ctrl+G", "Ctrl+Shift+G"],
            new[] { "CutButton", "CopyButton", "PasteButton", "DuplicateButton", "GroupButton", "UngroupButton" }.Select(n => Shortcut.GetKeys(Find<Button>(window, n))));
        Assert.False(Find<Button>(window, "GroupButton").IsEffectivelyEnabled); // one thing alone can't be grouped

        Click(Find<Button>(window, "DuplicateButton"));
        Assert.Equal(2, editor.Working.Panels[panelId].CharacterInstances.Count);
        Click(Find<Button>(window, "CopyButton"));
        Assert.True(Find<Button>(window, "PasteButton").IsEffectivelyEnabled);
        Click(delete);
        Assert.Single(editor.Working.Panels[panelId].CharacterInstances);

        // A panel, then a multi-selection: the same row, Delete saying what it deletes.
        editor.Select(panelId);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Shows<Button>(window, "CutButton"));
        Assert.Equal("Delete this panel and its bubbles", ToolTip.GetTip(delete));

        var shape = DrawRectangle(editor, panelId, editor.PanelBounds(panelId).Left + 4, editor.PanelBounds(panelId).Top + 4);
        editor.ToggleSelect(panelId, characterIndex: 0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2 selected", Inspector(window).Title);
        Assert.Equal("Delete what's selected", ToolTip.GetTip(delete));
        Click(Find<Button>(window, "GroupButton"));
        Assert.True(editor.UngroupSelectionCommand.CanExecute(null));
        Assert.True(Find<Button>(window, "UngroupButton").IsEffectivelyEnabled);
        Click(Find<Button>(window, "UngroupButton"));
        Assert.False(editor.UngroupSelectionCommand.CanExecute(null));
        Assert.IsType<ShapeElement>(editor.Working.Panels[panelId].Elements[shape]);
    }

    [Fact]
    public void A_placed_character_gets_the_Character_section_which_turns_poses_sizes_and_orders_it()
    {
        var (window, editor, panelId) = Open();
        var index = PlaceCharacter(window, panelId);
        CharacterInstance Placed() => editor.Working.Panels[panelId].CharacterInstances[index];

        Assert.Equal(editor.SelectedCharacterName, Inspector(window).Title);
        Assert.True(Shows<Button>(window, "EditCharacterButton"));
        Assert.False(Shows<ColorMenuButton>(window, "ShapeFillButton"));
        Assert.False(Shows<ColorMenuButton>(window, "PenFillButton"));
        Assert.False(Shows<DropDownButton>(window, "LookGallery")); // no looks to pick from yet

        // View: side on, back to front, and flipped.
        Click(Find<RadioButton>(window, "SideViewButton"));
        Assert.Equal(ViewAngle.Profile, Placed().Pose.ViewAngle);
        Assert.False(Find<RadioButton>(window, "FrontViewButton").IsChecked);
        Click(Find<RadioButton>(window, "FrontViewButton"));
        Assert.Equal(ViewAngle.Front, Placed().Pose.ViewAngle);
        var mirrored = Placed().Placement.Mirrored;
        Click(Find<Button>(window, "FlipCharacterButton"));
        Assert.NotEqual(mirrored, Placed().Placement.Mirrored);

        // Pose: a preset from the gallery, which closes behind the pick; then Mirror and Reset.
        Assert.False(Find<Button>(window, "ResetPoseButton").IsEffectivelyEnabled);
        var poses = Find<DropDownButton>(window, "PoseGalleryButton");
        Click(poses);
        Assert.True(poses.Flyout!.IsOpen);
        var wave = Find<ItemsControl>(window, "PoseGallery").GetVisualDescendants().OfType<Button>()
            .Single(b => b.CommandParameter is PosePresetChoice { Name: "Wave" });
        Click(wave);
        Assert.True(editor.SelectedCharacterIsPosed);
        Assert.False(poses.Flyout.IsOpen);
        Click(Find<Button>(window, "MirrorPoseButton"));
        Click(Find<Button>(window, "ResetPoseButton"));
        Assert.False(editor.SelectedCharacterIsPosed);

        // Face: a preset from the gallery, which stays open for mixing; the button names it.
        var faces = Find<DropDownButton>(window, "ExpressionGallery");
        Click(faces);
        var happy = Find<ItemsControl>(window, "ExpressionChoices").GetVisualDescendants().OfType<Button>()
            .Single(b => b.CommandParameter is ExpressionPresetChoice { Name: "Happy" });
        Click(happy);
        Assert.Equal("Happy", editor.SelectedExpressionName);
        Assert.True(faces.Flyout!.IsOpen);
        Assert.Contains(faces.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Happy");
        faces.Flyout.Hide();

        // Size and order.
        var height = Placed().Placement.UnitHeightMm;
        Click(Find<Button>(window, "BiggerCharacterButton"));
        Assert.True(Placed().Placement.UnitHeightMm > height);
        Click(Find<Button>(window, "SmallerCharacterButton"));
        Assert.Equal(height, Placed().Placement.UnitHeightMm, 6);
        var other = PlaceCharacter(window, panelId);
        Assert.Equal(1, other);
        Click(Find<Button>(window, "CharacterToBackButton"));
        Assert.Equal(0, editor.SelectedCharacterIndex);
        Click(Find<Button>(window, "CharacterToFrontButton"));
        Assert.Equal(1, editor.SelectedCharacterIndex);

        // Edit body opens the character's own editor, with the character inspector.
        Click(Find<Button>(window, "EditCharacterButton"));
        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);
    }

    [Fact]
    public void A_character_with_named_looks_gets_a_Look_row_for_this_panel_or_the_whole_issue()
    {
        var (window, editor, panelId) = Open();
        var characters = window.ViewModel.Characters!;
        var created = characters.CreateCharacter();
        var body = characters.Items.Single(i => i.Id == created.Id).Editor;
        body.NewLookCommand.Execute(null);
        var look = body.CurrentLook;
        editor.InsertCharacter(created.Id, panelId);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Shows<DropDownButton>(window, "LookGallery"));

        var looks = Find<DropDownButton>(window, "LookGallery");
        Click(looks);
        Click(Find<ItemsControl>(window, "PanelLooks").GetVisualDescendants().OfType<Button>().Single(b => b.CommandParameter is LookChoice { Look: { } id } && id == look));
        Assert.Equal(look, editor.Working.Panels[panelId].CharacterInstances[0].RevisionOverride);
        Assert.EndsWith("(panel)", editor.SelectedLookName);

        Click(Find<ItemsControl>(window, "IssueLooks").GetVisualDescendants().OfType<Button>().Single(b => b.CommandParameter is LookChoice { Look: { } id } && id == look));
        Assert.Equal(look, editor.IssueLooks[created.Id]);
        looks.Flyout!.Hide();
    }

    [Fact]
    public void A_drawn_shape_gets_the_Shape_section_which_restyles_layers_and_edits_its_points()
    {
        var (window, editor, panelId) = Open();
        var bounds = editor.PanelBounds(panelId);
        var index = DrawRectangle(editor, panelId, bounds.Left + 4, bounds.Top + 4);
        ShapeElement Shape() => (ShapeElement)editor.Working.Panels[panelId].Elements[index];

        Assert.Equal("Shape", Inspector(window).Title);
        Assert.True(Shows<ColorMenuButton>(window, "ShapeFillButton"));
        Assert.False(Shows<ColorMenuButton>(window, "PenFillButton"));
        Assert.Equal("Delete this shape", ToolTip.GetTip(Find<Button>(window, "DeleteButton")));

        PickColour(Find<ColorMenuButton>(window, "ShapeFillButton"), "Gold, Accent 4");
        Assert.Equal(Colour("Gold, Accent 4"), Shape().Style.Fill);
        PickColour(Find<ColorMenuButton>(window, "ShapeOutlineButton"), "Red");
        Assert.Equal(Colour("Red"), Shape().Style.Stroke);

        Click(Find<RadioButton>(window, "ShapeInFrontButton"));
        Assert.Equal(ElementLayer.Foreground, Shape().Layer);
        Click(Find<RadioButton>(window, "ShapeBehindButton"));
        Assert.Equal(ElementLayer.Background, Shape().Layer);

        // Edit Points: the point tools show only while editing, and work on the selected point.
        Assert.False(Shows<Grid>(window, "PointTools"));
        Assert.Equal("Enter", Shortcut.GetKeys(Find<ToggleButton>(window, "EditPointsButton")));
        Click(Find<ToggleButton>(window, "EditPointsButton"));
        Assert.True(editor.IsEditingPoints);
        Assert.True(Shows<Grid>(window, "PointTools"));
        Assert.False(Find<RadioButton>(window, "SmoothPointButton").IsEffectivelyEnabled); // no point picked yet
        editor.SelectPoint(0);
        Dispatcher.UIThread.RunJobs();
        Click(Find<RadioButton>(window, "SmoothPointButton"));
        Assert.Equal(AnchorHandleKind.Smooth, Shape().Anchors[0].HandleKind);
        Click(Find<Button>(window, "OpenShapeButton"));
        Assert.False(Shape().Closed);
        Click(Find<Button>(window, "CloseShapeButton"));
        Assert.True(Shape().Closed);
        var points = Shape().Anchors.Count;
        editor.SelectPoint(0);
        Dispatcher.UIThread.RunJobs();
        Click(Find<Button>(window, "DeletePointButton"));
        Assert.Equal(points - 1, Shape().Anchors.Count);
        Click(Find<ToggleButton>(window, "EditPointsButton"));
        Assert.False(editor.IsEditingPoints);
        Assert.False(Shows<Grid>(window, "PointTools"));

        // Order: in front of everything in the panel but bubbles, then back behind it all.
        var second = DrawRectangle(editor, panelId, bounds.Left + 40, bounds.Top + 4);
        var id = editor.Working.Panels[panelId].Elements[second].Id;
        Click(Find<Button>(window, "ShapeToBackButton"));
        Assert.Equal(0, editor.Working.Panels[panelId].Elements.ToList().FindIndex(e => e.Id == id));
        Click(Find<Button>(window, "ShapeToFrontButton"));
        Assert.Equal(editor.Working.Panels[panelId].Elements.Count - 1, editor.Working.Panels[panelId].Elements.ToList().FindIndex(e => e.Id == id));
    }

    [Fact]
    public void A_drawing_tool_gets_the_Pen_section_which_sets_what_the_next_shape_is_drawn_with()
    {
        var (window, editor, panelId) = Open();
        Assert.False(Shows<ColorMenuButton>(window, "PenFillButton"));

        editor.Tool = PageEditorTool.Rectangle;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("New shape", Inspector(window).Title);
        Assert.True(Shows<ColorMenuButton>(window, "PenFillButton"));
        Assert.False(Shows<ColorMenuButton>(window, "ShapeFillButton"));

        PickColour(Find<ColorMenuButton>(window, "PenFillButton"), "Green");
        PickColour(Find<ColorMenuButton>(window, "PenOutlineButton"), "Purple");
        Click(Find<RadioButton>(window, "PenInFrontButton"));
        Assert.Empty(editor.Working.Panels[panelId].Elements); // nothing drawn yet: it's all for the next shape

        var bounds = editor.PanelBounds(panelId);
        var index = DrawRectangle(editor, panelId, bounds.Left + 4, bounds.Top + 4);
        var drawn = (ShapeElement)editor.Working.Panels[panelId].Elements[index];
        Assert.Equal(Colour("Green"), drawn.Style.Fill);
        Assert.Equal(Colour("Purple"), drawn.Style.Stroke);
        Assert.Equal(ElementLayer.Foreground, drawn.Layer);

        // The shape just drawn is selected, with the Select tool back: the Shape section, not the pen.
        Assert.False(Shows<ColorMenuButton>(window, "PenFillButton"));
        Assert.True(Shows<ColorMenuButton>(window, "ShapeFillButton"));
    }
}
