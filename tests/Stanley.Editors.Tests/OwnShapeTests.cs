using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Serialization;
using Stanley.ProjectModel.Storage;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>Shapes of your own for a character built from shapes (docs/shape-characters.md §9.3): Import shape, Draw shape, Keep in My Assets and My shapes.</summary>
public sealed class OwnShapeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-own-shape-tests-" + Guid.NewGuid().ToString("N"));

    public OwnShapeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MyAssetsLibrary NewLibrary() => new(Path.Combine(_root, "My Assets"));

    private static (EditorSession Session, CharacterEditorViewModel Editor) NewShapeCharacter(MyAssetsLibrary? myAssets = null, string name = "Bob")
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew(), myAssets);
        session.Characters.NewShapeCharacterCommand.Execute(null);
        var editor = session.Characters.Items.Single().Editor;
        editor.Name = name;
        return (session, editor);
    }

    private static string Json(ModularBody? body) => body is null ? "" : ProjectJson.Serialize(body);

    private static ArtFile Svg(string body, string viewBox = "0 0 200 100") =>
        ArtFile.Svg($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}">{body}</svg>""");

    // ---------------------------------------------------------------- Import shape

    [Fact]
    public void Importing_an_svg_makes_it_the_heads_shape_keeping_its_size_and_colour_as_one_undo_step()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        editor.SelectedModuleColorEditor!.SetColor.Execute(new ColorSwatchChoice(editor.SelectedModuleColorEditor.Slot, "Blue", ColorValue.FromHex("#3355ff")));
        editor.FlipModuleVerticallyCommand.Execute(null);
        var before = editor.Working.Modules!.Head!;
        Assert.NotNull(ShapeLibrary.SourceOf(before.Shape));
        var bodyBefore = Json(editor.Working.Modules);

        Assert.True(editor.ImportShapeCommand.CanExecute(null));
        Assert.True(editor.ImportShape(ModulePart.Head, null, "kite.svg", Svg("""<path d="M100,0 L200,50 L100,100 L0,50 Z" fill="#f00"/>""")));

        var head = editor.Working.Modules!.Head!;
        Assert.Equal(4, head.Shape.Anchors.Count);
        Assert.Null(head.Shape.Source);                                         // the library's source is gone
        Assert.Equal("bodyHead", head.Color);                                   // its colour stayed
        Assert.Equal(before.Width * before.Height, head.Width * head.Height, 6); // as big as it was...
        Assert.Equal(2, head.Width / head.Height, 6);                           // ...in the drawing's proportions
        Assert.Null(head.FlipY);                                                 // and as drawn: no flip
        Assert.Equal(before.Offset, head.Offset);
        Assert.Contains("kite.svg", editor.Hint);
        Assert.Equal(ModulePart.Head, editor.SelectedModule);
        Assert.Equal("Your own", editor.ModuleShapeGallery!.Current);

        session.Workspace.History.Undo();
        Assert.Equal(bodyBefore, Json(editor.Working.Modules));
    }

    [Fact]
    public void An_import_says_what_it_left_out_and_a_file_with_no_shape_changes_nothing()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        var before = Json(editor.Working.Modules);

        Assert.False(editor.ImportShape(ModulePart.Torso, null, "line.svg", Svg("""<path d="M0,0 L100,100" stroke="#000" fill="none"/>""")));
        Assert.Equal(before, Json(editor.Working.Modules));
        Assert.Contains("Couldn't import line.svg", editor.Hint);
        Assert.Contains("1 open path ignored", editor.Hint);

        Assert.True(editor.ImportShape(ModulePart.Torso, null, "two.svg", Svg("""<rect width="50" height="100"/><path d="M0,0 L10,10" stroke="#000"/><circle cx="150" cy="50" r="10"/>""")));
        Assert.Contains("1 open path ignored", editor.Hint);
        Assert.Contains("1 more shape ignored", editor.Hint);
        session.Workspace.History.Undo();
        Assert.Equal(before, Json(editor.Working.Modules)); // the failed import added no step
        Assert.Equal("Bob", editor.Working.Name);
    }

    [Fact]
    public void For_arms_an_imported_shape_becomes_their_hands_in_its_own_proportions()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Arms);

        Assert.True(editor.ImportShapeCommand.CanExecute(null));
        Assert.True(editor.ImportShape(ModulePart.Arms, LimbPiece.End, "mitt.svg", Svg("""<rect width="50" height="100"/>""")));

        var hand = editor.Working.Modules!.Arms!.End!;
        var bounds = AnchorRing.CurveBounds(hand.Anchors);
        Assert.Equal((0.25, 0, 0.5, 1), (Math.Round(bounds.X, 4), Math.Round(bounds.Y, 4), Math.Round(bounds.Width, 4), Math.Round(bounds.Height, 4)));
        Assert.Equal("Your own", editor.LimbEndGallery!.Current);
    }

    // ---------------------------------------------------------------- Draw shape

    [Fact]
    public void Drawing_a_shape_and_closing_it_makes_it_the_part_where_it_was_drawn_as_one_undo_step()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        var before = Json(editor.Working.Modules);
        var centre = OwnShapeEditing.CentreOf(editor.Working.Modules!, ModulePart.Head)!.Value;

        editor.DrawShapeCommand.Execute(null);
        Assert.True(editor.IsDrawingShape);
        Assert.Contains("Draw shape", editor.Hint);
        // A diamond 0.2 wide and 0.3 tall, a little right of where the head was; the last point dragged smooth.
        var (cx, cy) = (centre.X + 0.05, centre.Y);
        editor.PlaceDrawnPoint(new Point2D(cx, cy - 0.15));
        editor.PlaceDrawnPoint(new Point2D(cx + 0.1, cy));
        editor.PlaceDrawnPoint(new Point2D(cx, cy + 0.15));
        Assert.True(editor.CanCloseDrawnShape);
        editor.PlaceDrawnPoint(new Point2D(cx - 0.1, cy));
        editor.PullDrawnPoint(new Point2D(cx - 0.1, cy - 0.01));
        Assert.Equal(AnchorHandleKind.Smooth, editor.DrawnShape[^1].HandleKind);
        Assert.Equal(before, Json(editor.Working.Modules)); // nothing changes until it's closed

        Assert.True(editor.CloseDrawnShape());

        Assert.False(editor.IsDrawingShape);
        var head = editor.Working.Modules!.Head!;
        Assert.Equal(4, head.Shape.Anchors.Count);
        Assert.Null(head.Shape.Source);
        var drawn = OwnShapeEditing.CentreOf(editor.Working.Modules, ModulePart.Head)!.Value;
        Assert.Equal(cx, drawn.X, 3);
        Assert.Equal(cy, drawn.Y, 3);
        Assert.Equal(0.3, head.Height, 2);

        session.Workspace.History.Undo();
        Assert.Equal(before, Json(editor.Working.Modules));
        Assert.Equal("Bob", editor.Working.Name); // one step: the rename before it is still there
    }

    [Fact]
    public void Esc_stops_drawing_and_changes_nothing()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        var before = Json(editor.Working.Modules);

        editor.BeginDrawShape();
        editor.PlaceDrawnPoint(new Point2D(0, -0.5));
        editor.PlaceDrawnPoint(new Point2D(0.1, -0.4));
        editor.PlaceDrawnPoint(new Point2D(0, -0.3));
        editor.CancelDrawShape();

        Assert.False(editor.IsDrawingShape);
        Assert.Empty(editor.DrawnShape);
        Assert.Equal(before, Json(editor.Working.Modules));
        session.Workspace.History.Undo();
        Assert.NotEqual("Bob", editor.Working.Name); // no step of its own: the last one was the rename
    }

    [Fact]
    public void Closing_needs_three_points_and_backspace_takes_one_back()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        editor.BeginDrawShape();
        editor.PlaceDrawnPoint(new Point2D(0, -0.5));
        editor.PlaceDrawnPoint(new Point2D(0.1, -0.4));
        editor.PlaceDrawnPoint(new Point2D(0, -0.3));
        editor.TakeBackDrawnPoint();

        Assert.False(editor.CanCloseDrawnShape);
        Assert.False(editor.CloseDrawnShape());
        Assert.True(editor.IsDrawingShape); // still drawing
        Assert.Contains("at least three points", editor.Hint);
    }

    [Fact]
    public void Picking_another_part_stops_drawing_and_the_button_toggles_it_off()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        editor.DrawShapeCommand.Execute(null);
        editor.PlaceDrawnPoint(new Point2D(0, -0.8));

        editor.SelectModule(ModulePart.Torso);
        Assert.False(editor.IsDrawingShape);

        editor.DrawShapeCommand.Execute(null);
        Assert.True(editor.IsDrawingShape);
        editor.DrawShapeCommand.Execute(null);
        Assert.False(editor.IsDrawingShape);
    }

    [Fact]
    public void A_hand_drawn_for_the_arms_is_as_big_as_it_was_drawn()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Arms);
        editor.BeginDrawShape();
        editor.PlaceDrawnPoint(new Point2D(0, 0));
        editor.PlaceDrawnPoint(new Point2D(0.04, 0));
        editor.PlaceDrawnPoint(new Point2D(0.04, 0.08));
        editor.PlaceDrawnPoint(new Point2D(0, 0.08));

        Assert.True(editor.CloseDrawnShape());

        var arms = editor.Working.Modules!.Arms!;
        Assert.Equal(0.08, arms.EndSize!.Value, 6);
        Assert.Equal(4, arms.End!.Anchors.Count);
    }

    // ---------------------------------------------------------------- Keep in My Assets, My shapes

    [Fact]
    public void A_kept_shape_shows_under_My_shapes_in_another_comic_and_picking_it_copies_it_in()
    {
        var library = NewLibrary();
        var (session, bob) = NewShapeCharacter(library);
        bob.SelectModule(ModulePart.Head);
        bob.ImportShape(ModulePart.Head, null, "kite.svg", Svg("""<path d="M100,0 L200,50 L100,100 L0,50 Z"/>"""));
        Assert.True(bob.CanKeepShape);

        Assert.True(bob.KeepShapeInMyAssets());

        var kept = Assert.Single(library.Shapes());
        Assert.Equal("Bob's head", kept.Name);
        Assert.Single(Directory.GetFiles(Path.Combine(library.RootDirectory, "shapes"), "*.json"));
        var bounds = AnchorRing.CurveBounds(kept.Shape.Anchors);
        Assert.Equal((0, 0, 1, 0.5), (Math.Round(bounds.X, 4), Math.Round(bounds.Y, 4), Math.Round(bounds.Width, 4), Math.Round(bounds.Height, 4))); // its own proportions
        // Bob's head is now that kept shape's copy, one undo step; Keep isn't offered again.
        Assert.Equal(kept.Source, bob.Working.Modules!.Head!.Shape.Source);
        Assert.Equal(AssetFingerprint.ShapeFingerprint(kept), bob.Working.Modules.Head.Shape.MyAssetsVersion);
        Assert.False(bob.CanKeepShape);
        Assert.Contains("Bob's head", bob.KeepShapeTip);
        Assert.Equal("Bob's head", bob.ModuleShapeGallery!.Current);

        // Another comic: its torso gallery lists it under My shapes.
        var (other, alice) = NewShapeCharacter(library, "Alice");
        alice.SelectModule(ModulePart.Torso);
        var torso = alice.Working.Modules!.Torso!;
        var section = alice.ModuleShapeGallery!.Sections.Single(s => s.Label == "My shapes");
        var choice = Assert.Single(section.Choices);
        Assert.Equal("Bob's head", choice.Label);
        Assert.False(choice.IsCurrent);
        Assert.Equal(2, choice.Aspect, 3);

        alice.SwapModuleShapeCommand.Execute(choice);

        var copy = alice.Working.Modules!.Torso!;
        Assert.Equal(kept.Source, copy.Shape.Source);
        Assert.Equal(AssetFingerprint.ShapeFingerprint(kept), copy.Shape.MyAssetsVersion);
        Assert.Equal(torso.Width * torso.Height, copy.Width * copy.Height, 6);
        Assert.Equal(2, copy.Width / copy.Height, 6);
        Assert.Equal(torso.Color, copy.Color);
        Assert.True(alice.ModuleShapeGallery!.Sections.Single(s => s.Label == "My shapes").Choices.Single().IsCurrent);
        other.Workspace.History.Undo();
        Assert.Equal(torso.Shape.Source, alice.Working.Modules!.Torso!.Shape.Source);
        Assert.Null(alice.Working.Modules.Torso.Shape.MyAssetsVersion);

        // And its hands' gallery too, fitted to a hand's box.
        alice.SelectModule(ModulePart.Arms);
        var hand = alice.LimbEndGallery!.Sections.Single(s => s.Label == "My shapes").Choices.Single();
        alice.SwapModuleShapeCommand.Execute(hand);
        Assert.Equal(kept.Source, alice.Working.Modules!.Arms!.End!.Source);
    }

    [Fact]
    public void Undoing_a_keep_unlinks_the_part_but_My_Assets_keeps_the_shape_and_an_edit_unlinks_it_too()
    {
        var library = NewLibrary();
        var (session, editor) = NewShapeCharacter(library);
        editor.SelectModule(ModulePart.Torso);
        editor.KeepShapeInMyAssets();
        Assert.NotNull(editor.Working.Modules!.Torso!.Shape.MyAssetsVersion);

        session.Workspace.History.Undo();
        Assert.Null(editor.Working.Modules!.Torso!.Shape.MyAssetsVersion);
        Assert.Single(library.Shapes());

        session.Workspace.History.Redo();
        var edited = ModuleEditing.SetAnchors(editor.Working.Modules!, ModulePart.Torso, editor.Working.Modules!.Torso!.Shape.Anchors);
        Assert.Null(edited.Value.Torso!.Shape.Source);
        Assert.Null(edited.Value.Torso.Shape.MyAssetsVersion);
    }

    [Fact]
    public void Keeping_needs_My_Assets_and_a_shape_of_the_parts_own()
    {
        var (_, without) = NewShapeCharacter();
        without.SelectModule(ModulePart.Head);
        Assert.False(without.CanKeepShape);
        Assert.False(without.KeepShapeInMyAssetsCommand.CanExecute(null));

        var (_, editor) = NewShapeCharacter(NewLibrary());
        editor.SelectModule(ModulePart.Legs);
        editor.SwapModuleShapeCommand.Execute(editor.LimbEndGallery!.Sections[0].Choices.Single(c => c.Library is null)); // a round foot
        Assert.Null(editor.Working.Modules!.Legs!.End);
        Assert.False(editor.CanKeepShape);
        Assert.Contains("Pick a shape", editor.KeepShapeTip);
    }

    [Fact]
    public void Two_shapes_kept_from_one_part_get_their_own_names()
    {
        var library = NewLibrary();
        var (_, editor) = NewShapeCharacter(library);
        editor.SelectModule(ModulePart.Head);
        editor.KeepShapeInMyAssets();
        editor.ImportShape(ModulePart.Head, null, "kite.svg", Svg("""<rect width="10" height="10"/>"""));
        editor.KeepShapeInMyAssets();

        Assert.Equal(["Bob's head", "Bob's head 2"], library.Shapes().Select(s => s.Name));
    }

    // ---------------------------------------------------------------- File › My Assets

    [Fact]
    public void A_kept_shape_is_renamed_and_removed_on_the_My_Assets_page()
    {
        var library = NewLibrary();
        var (_, editor) = NewShapeCharacter(library);
        editor.SelectModule(ModulePart.Head);
        editor.KeepShapeInMyAssets();
        var page = new MyAssetsPageViewModel(library);
        Assert.Equal("Shapes (1)", page.ShapesText);
        page.IsShapes = true;
        var tile = Assert.Single(page.Tiles);
        Assert.True(tile.IsShape);
        Assert.Equal("Shape", tile.KindText);
        Assert.NotNull(tile.ShapeOutline);
        var fingerprint = AssetFingerprint.ShapeFingerprint(library.Shapes().Single());

        Assert.True(page.RenameCommand.CanExecute(tile));
        page.RenameCommand.Execute(tile);
        tile.Name = "Kite head";

        var renamed = Assert.Single(library.Shapes());
        Assert.Equal("Kite head", renamed.Name);
        Assert.Equal(fingerprint, AssetFingerprint.ShapeFingerprint(renamed)); // a rename is no change to the characters using it
        Assert.Equal("Kite head", editor.ModuleShapeGallery!.Current);

        tile = Assert.Single(page.Tiles);
        page.RemoveCommand.Execute(tile);
        Assert.Contains("keep their own copy", page.RemovalQuestion);
        Assert.Single(library.Shapes());
        page.ConfirmRemoveCommand.Execute(null);

        Assert.Empty(library.Shapes());
        Assert.True(page.IsEmpty);
        Assert.Null(editor.ModuleShapeGallery!.Sections.FirstOrDefault(s => s.Label == "My shapes"));
        // The character keeps its own copy.
        Assert.Equal(renamed.Source, editor.Working.Modules!.Head!.Shape.Source);
        Assert.Equal("Your own", editor.ModuleShapeGallery.Current);
    }
}
