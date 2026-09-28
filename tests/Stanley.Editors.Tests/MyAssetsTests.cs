using SkiaSharp;
using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors.Tests;

/// <summary>Keeping things in My Assets and adding them to a comic (docs/asset-packs.md §10 slice 3): characters and object groups.</summary>
public sealed class MyAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-my-assets-tests-" + Guid.NewGuid().ToString("N"));

    public MyAssetsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MyAssetsLibrary NewLibrary() => new(Path.Combine(_root, "My Assets"));

    private static (CharacterLibraryViewModel Pane, EditorHistory History) Pane(MyAssetsLibrary library, params CharacterDefinition[] characters)
    {
        var history = new EditorHistory();
        return (new CharacterLibraryViewModel(history, characters) { MyAssets = library }, history);
    }

    // ---------------------------------------------------------------- characters

    [Fact]
    public void Keeping_a_character_writes_it_to_My_Assets_and_records_the_version_in_the_comic()
    {
        var library = NewLibrary();
        var (pane, history) = Pane(library, CharacterDefinition.Create("Alice"));
        var item = pane.Items[0];
        Assert.Equal(KeptState.NotKept, pane.KeptStateOf(item));
        Assert.False(item.IsKept);

        var kept = pane.KeepInMyAssets(item);

        Assert.NotNull(kept);
        var stored = Assert.Single(library.Characters());
        Assert.Equal(item.Id, stored.Id);
        Assert.Equal(AssetFingerprint.CharacterFingerprint(stored), stored.MyAssetsVersion);
        Assert.Equal(stored.MyAssetsVersion, item.Editor.Committed.MyAssetsVersion);
        Assert.Equal(KeptState.Kept, pane.KeptStateOf(item));
        Assert.True(item.IsKept);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Undoing_a_keep_forgets_it_in_the_comic_but_leaves_My_Assets_alone()
    {
        var library = NewLibrary();
        var (pane, history) = Pane(library, CharacterDefinition.Create("Alice"));
        pane.KeepInMyAssets(pane.Items[0]);

        history.Undo();

        Assert.Null(pane.Items[0].Editor.Committed.MyAssetsVersion);
        Assert.False(pane.Items[0].IsKept);
        Assert.Single(library.Characters());
    }

    [Fact]
    public void A_kept_character_changed_in_the_comic_offers_Save_to_My_Assets_which_updates_it_there()
    {
        var library = NewLibrary();
        var (pane, _) = Pane(library, CharacterDefinition.Create("Alice"));
        var item = pane.Items[0];
        pane.KeepInMyAssets(item);

        pane.EditCharacter(item.Id, "Rename", c => c with { Name = "Alicia" }, null);
        Assert.Equal(KeptState.ChangedHere, pane.KeptStateOf(item));

        pane.KeepInMyAssets(item);
        Assert.Equal(KeptState.Kept, pane.KeptStateOf(item));
        Assert.Equal("Alicia", Assert.Single(library.Characters()).Name);
    }

    [Fact]
    public void A_duplicate_is_a_new_character_so_it_is_not_kept()
    {
        var library = NewLibrary();
        var (pane, _) = Pane(library, CharacterDefinition.Create("Alice"));
        pane.KeepInMyAssets(pane.Items[0]);

        pane.DuplicateCharacterCommand.Execute(pane.Items[0]);

        var copy = pane.Items[1];
        Assert.Null(copy.Editor.Committed.MyAssetsVersion);
        Assert.False(copy.IsKept);
    }

    [Fact]
    public void Adding_from_My_Assets_copies_the_character_with_the_same_ids_as_one_undo_step_without_opening_it()
    {
        var library = NewLibrary();
        var alice = LookEditing.NewLook(CharacterDefinition.Create("Alice"), "Winter").Character;
        library.KeepCharacter(alice);
        var (pane, history) = Pane(library);
        CharacterItem? revealed = null;
        pane.CharacterRevealed += item => revealed = item;

        pane.RefreshGallery();
        var choice = Assert.Single(pane.GalleryMyAssets);
        var added = pane.AddFromGallery(choice);

        Assert.NotNull(added);
        Assert.Same(added, revealed);
        Assert.Null(pane.Current); // not opened - most likely it's about to be placed
        Assert.Equal(alice.Id, added.Id);
        Assert.Equal(alice.Revisions.Keys, added.Editor.Committed.Revisions.Keys);
        Assert.Equal(KeptState.Kept, pane.KeptStateOf(added));
        Assert.True(added.IsKept);

        history.Undo();
        Assert.Empty(pane.Items);
    }

    [Fact]
    public void Adding_a_character_the_comic_already_has_just_shows_it()
    {
        var library = NewLibrary();
        var alice = CharacterDefinition.Create("Alice");
        library.KeepCharacter(alice);
        var (pane, history) = Pane(library, alice);
        CharacterItem? revealed = null;
        pane.CharacterRevealed += item => revealed = item;

        pane.RefreshGallery();
        var choice = Assert.Single(pane.GalleryMyAssets);
        Assert.True(choice.InThisComic);
        var result = pane.AddFromGallery(choice);

        Assert.Single(pane.Items);
        Assert.Same(pane.Items[0], result);
        Assert.Same(result, revealed);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void The_gallery_lists_other_recent_comics_characters_skipping_ones_already_here_or_kept_and_comics_that_cannot_be_read()
    {
        var library = NewLibrary();
        var bob = CharacterDefinition.Create("Bob");
        var kept = CharacterDefinition.Create("Kept");
        var here = CharacterDefinition.Create("Here");
        library.KeepCharacter(kept);
        var other = SaveComic("Space Cats", bob, kept, here);
        var broken = Path.Combine(_root, "Broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "stanley.json"), "{ not json");
        var thisComic = SaveComic("This one", here);

        var (pane, _) = Pane(library, here);
        pane.RecentComics = () => [thisComic, broken, Path.Combine(_root, "Gone"), other];
        pane.ComicLocation = thisComic;
        pane.RefreshGallery();

        var comic = Assert.Single(pane.GalleryOtherComics);
        Assert.Equal("Space Cats", comic.Title);
        Assert.Equal(bob.Id, Assert.Single(comic.Characters).Character.Id);
        Assert.Equal(kept.Id, Assert.Single(pane.GalleryMyAssets).Character.Id);
    }

    [Fact]
    public void Picking_a_character_from_another_comic_also_keeps_it_in_My_Assets()
    {
        var library = NewLibrary();
        var bob = CharacterDefinition.Create("Bob");
        var other = SaveComic("Space Cats", bob);
        var (pane, _) = Pane(library);
        pane.RecentComics = () => [other];
        pane.RefreshGallery();

        var added = pane.AddFromGallery(Assert.Single(Assert.Single(pane.GalleryOtherComics).Characters));

        Assert.NotNull(added);
        Assert.Equal(bob.Id, added.Id);
        Assert.Equal(bob.Id, Assert.Single(library.Characters()).Id);
        Assert.Equal(KeptState.Kept, pane.KeptStateOf(added));
    }

    private string SaveComic(string title, params CharacterDefinition[] characters)
    {
        var folder = Path.Combine(_root, title);
        var repository = ProjectRepository.Initialize(folder, title, new PageTrim(MetricPaperSizes.Size(MetricPaperSize.A4), 3));
        foreach (var character in characters)
            repository.SaveCharacter(character);
        return folder;
    }

    // ---------------------------------------------------------------- object groups

    private static ArtFile Png(SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return ArtFile.Png(data.ToArray());
    }

    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel, PictureLibrary Pictures, ObjectGroupLibrary Groups) PageSession(MyAssetsLibrary library)
    {
        var history = new EditorHistory();
        var pictures = new PictureLibrary();
        var groups = new ObjectGroupLibrary();
        var navigator = new PageNavigatorViewModel(history, ComicProject.CreateNew().Pages, pictures: pictures) { MyAssets = library, ObjectGroups = groups };
        var editor = navigator.CurrentPage.Editor;
        return (editor, history, editor.Working.PanelOrder[0], pictures, groups);
    }

    /// <summary>A rectangle and a picture, grouped and selected.</summary>
    private static GroupElement MakeGroup(PageEditorViewModel editor, PanelId panel)
    {
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(30, 30), new Point2D(60, 50));
        var rectangle = editor.CommitDrawShape();
        Assert.True(editor.ImportPicture(new PictureImportRequest(panel, AsBackground: false), "star.png", Png(SKColors.Gold)));
        var picture = editor.SelectedElementIndex;
        editor.SelectElement(panel, rectangle);
        editor.ToggleSelect(panel, elementIndex: picture);
        editor.GroupSelectionCommand.Execute(null);
        return Assert.IsType<GroupElement>(editor.SelectedElement);
    }

    [Fact]
    public void Keeping_a_group_names_it_writes_it_with_its_pictures_and_links_it_as_one_undo_step()
    {
        var library = NewLibrary();
        var (editor, history, panel, _, groups) = PageSession(library);
        MakeGroup(editor, panel);
        Assert.Equal(KeptState.NotKept, editor.SelectedGroupKeptState());
        Assert.True(editor.KeepGroupInMyAssetsCommand.CanExecute(null));

        var kept = editor.KeepGroupInMyAssets();

        Assert.NotNull(kept);
        var stored = Assert.Single(library.ObjectGroups());
        Assert.Equal("Group 1", stored.Name);
        Assert.Equal(AssetFingerprint.ObjectGroupFingerprint(stored), stored.MyAssetsVersion);
        Assert.Single(stored.ArtFiles);
        Assert.Equal(stored.MyAssetsVersion, groups.Groups[stored.Id].MyAssetsVersion); // the comic's own copy, for objects/
        var linked = Assert.IsType<GroupElement>(editor.SelectedElement);
        Assert.Equal(stored.Id, linked.SourceId);
        Assert.Equal(KeptState.Kept, editor.SelectedGroupKeptState());

        history.Undo();
        Assert.Null(Assert.IsType<GroupElement>(Assert.Single(editor.Working.Panels[panel].Elements)).SourceId);
        Assert.Single(library.ObjectGroups()); // My Assets isn't the comic's to undo
    }

    [Fact]
    public void Moving_a_kept_group_is_not_a_change_but_resizing_it_is()
    {
        var library = NewLibrary();
        var (editor, _, panel, _, _) = PageSession(library);
        MakeGroup(editor, panel);
        editor.KeepGroupInMyAssets();
        var index = editor.SelectedElementIndex;

        editor.NudgeSelection(3.3, 1.7);
        editor.NudgeSelection(-0.1, 0.9);
        editor.SelectElement(panel, index);
        Assert.Equal(KeptState.Kept, editor.SelectedGroupKeptState());

        var box = PanelElements.Bounds(editor.SelectedElement!);
        editor.BeginResizeElement(panel, index);
        editor.UpdateResizeElement(panel, index, new Rect2D(box.X, box.Y, box.Width * 2, box.Height));
        editor.EndGesture(commit: true);
        editor.SelectElement(panel, index);
        Assert.Equal(KeptState.ChangedHere, editor.SelectedGroupKeptState());

        editor.KeepGroupInMyAssets(); // Save to My Assets
        Assert.Equal(KeptState.Kept, editor.SelectedGroupKeptState());
        Assert.Single(library.ObjectGroups());
    }

    [Fact]
    public void Inserting_a_kept_group_puts_a_linked_copy_centred_in_the_selected_panel_with_its_pictures()
    {
        var library = NewLibrary();
        var (source, _, sourcePanel, _, _) = PageSession(library);
        MakeGroup(source, sourcePanel);
        var kept = source.KeepGroupInMyAssets()!;

        var (editor, history, panel, pictures, groups) = PageSession(library); // another comic
        Assert.Equal(kept.Id, Assert.Single(editor.MyAssetsObjectGroups).Id);
        editor.Select(panel);

        Assert.True(editor.InsertObjectGroupCommand.CanExecute(kept));
        editor.InsertObjectGroupCommand.Execute(kept);

        var inserted = Assert.IsType<GroupElement>(editor.SelectedElement);
        Assert.Equal(kept.Id, inserted.SourceId);
        var panelBox = AnchorRing.BoundingBox(editor.Working.Panels[panel].Shape.Anchors);
        var box = PanelElements.Bounds(inserted);
        Assert.Equal(panelBox.X + panelBox.Width / 2, box.X + box.Width / 2, 3);
        Assert.Equal(panelBox.Y + panelBox.Height / 2, box.Y + box.Height / 2, 3);
        var picture = Assert.Single(inserted.Children.OfType<PictureElement>());
        Assert.True(pictures.Files.ContainsKey(picture.ArtFileName));
        Assert.Equal(kept.MyAssetsVersion, groups.Groups[kept.Id].MyAssetsVersion);
        Assert.Equal(KeptState.Kept, editor.SelectedGroupKeptState());

        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
    }

    [Fact]
    public void Keeping_is_offered_only_for_a_single_selected_group_and_only_with_My_Assets()
    {
        var (editor, _, panel, _, _) = PageSession(NewLibrary());
        Assert.False(editor.KeepGroupInMyAssetsCommand.CanExecute(null));
        MakeGroup(editor, panel);
        Assert.True(editor.KeepGroupInMyAssetsCommand.CanExecute(null));

        editor.MyAssets = null;
        Assert.False(editor.KeepGroupInMyAssetsCommand.CanExecute(null));
        Assert.Null(editor.KeepGroupInMyAssets());
    }

    [Fact]
    public void Copying_a_group_with_a_picture_into_another_comic_brings_the_picture_along()
    {
        var (source, _, sourcePanel, _, _) = PageSession(NewLibrary());
        MakeGroup(source, sourcePanel);
        var clipboard = new PageClipboard();
        source.Clipboard = clipboard;
        Assert.True(source.Copy());

        var (target, _, targetPanel, pictures, _) = PageSession(NewLibrary());
        target.Clipboard = clipboard;
        target.Select(targetPanel);
        Assert.True(target.Paste());

        var picture = Assert.Single(Assert.IsType<GroupElement>(target.SelectedElement).Children.OfType<PictureElement>());
        Assert.True(pictures.Files.ContainsKey(picture.ArtFileName));
    }

    [Fact]
    public void A_comic_saves_its_copies_of_kept_groups_to_objects_and_opens_them_again()
    {
        var library = NewLibrary();
        var project = ComicProject.CreateNew();
        var session = PageEditorHost.CreateWorkspace(project, library);
        var editor = session.Navigator.CurrentPage.Editor;
        MakeGroup(editor, editor.Working.PanelOrder[0]);
        var kept = editor.KeepGroupInMyAssets()!;

        var folder = Path.Combine(_root, "Comic");
        project.SaveAs(folder, session.Navigator.Snapshot(), pictures: session.Pictures.Files, objectGroups: session.ObjectGroups.Groups.Values.ToList());

        Assert.True(Directory.Exists(Path.Combine(folder, "objects")));
        var reopened = ComicProject.Open(folder);
        var copy = Assert.Single(reopened.ObjectGroups);
        Assert.Equal(kept.Id, copy.Id);
        Assert.Equal(kept.MyAssetsVersion, copy.MyAssetsVersion);
        Assert.Equal(AssetFingerprint.ObjectGroupFingerprint(kept), AssetFingerprint.ObjectGroupFingerprint(copy));

        // And back in an editor, the reopened comic still sees the group as kept.
        var again = PageEditorHost.CreateWorkspace(reopened, library).Navigator.CurrentPage.Editor;
        var panel = again.Working.PanelOrder[0];
        again.SelectElement(panel, 0);
        Assert.Equal(KeptState.Kept, again.SelectedGroupKeptState());
    }

    // ---------------------------------------------------------------- File › My Assets

    [Fact]
    public void The_My_Assets_page_filters_by_kind_and_follows_what_is_kept()
    {
        var library = NewLibrary();
        var page = new MyAssetsPageViewModel(library);
        Assert.True(page.IsEmpty);

        library.KeepCharacter(CharacterDefinition.Create("Alice"));
        library.KeepObjectGroup(new ObjectGroup(ObjectGroupId.New(), "Rocket", [new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 20, 10), "Zoom", TextStylePresets.Style(TextStylePreset.Caption))]));

        Assert.False(page.IsEmpty);
        Assert.Equal(["Rocket", "Alice"], page.Tiles.Select(t => t.Name));
        page.IsCharacters = true;
        Assert.Equal("Alice", Assert.Single(page.Tiles).Name);
        page.IsObjectGroups = true;
        Assert.Equal("Rocket", Assert.Single(page.Tiles).Name);
        Assert.Equal("Characters (1)", page.CharactersText);
    }

    [Fact]
    public void Removing_from_the_My_Assets_page_asks_first()
    {
        var library = NewLibrary();
        library.KeepCharacter(CharacterDefinition.Create("Alice"));
        var page = new MyAssetsPageViewModel(library);
        var tile = Assert.Single(page.Tiles);

        page.RemoveCommand.Execute(tile);
        Assert.True(page.HasPendingRemoval);
        Assert.Contains("keep their own copy", page.RemovalQuestion);
        Assert.Single(library.Characters()); // nothing removed yet

        page.CancelRemoveCommand.Execute(null);
        Assert.Single(library.Characters());

        page.RemoveCommand.Execute(tile);
        page.ConfirmRemoveCommand.Execute(null);
        Assert.Empty(library.Characters());
        Assert.True(page.IsEmpty);
        Assert.False(page.HasPendingRemoval);
    }

    [Fact]
    public void An_object_group_can_be_renamed_on_the_My_Assets_page()
    {
        var library = NewLibrary();
        var group = library.KeepObjectGroup(new ObjectGroup(ObjectGroupId.New(), "Group 1", [new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 20, 10), "Zoom", TextStylePresets.Style(TextStylePreset.Caption))]));
        var page = new MyAssetsPageViewModel(library);

        var tile = Assert.Single(page.Tiles);
        Assert.True(page.RenameCommand.CanExecute(tile));
        page.RenameCommand.Execute(tile);
        Assert.True(tile.IsEditingName);
        tile.Name = "Rocket ship";

        var renamed = Assert.Single(library.ObjectGroups());
        Assert.Equal(group.Id, renamed.Id);
        Assert.Equal("Rocket ship", renamed.Name);
        Assert.Equal("Rocket ship", Assert.Single(page.Tiles).Name);
    }
}
