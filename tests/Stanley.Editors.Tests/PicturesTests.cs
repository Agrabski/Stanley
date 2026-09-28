using SkiaSharp;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Importing pictures as panel backgrounds and as elements, and keeping them with the comic on disk.</summary>
public sealed class PicturesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-picture-tests-" + Guid.NewGuid().ToString("N"));

    public PicturesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ArtFile Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return ArtFile.Png(data.ToArray());
    }

    private static (PageNavigatorViewModel Navigator, PageEditorViewModel Editor, PictureLibrary Pictures, EditorHistory History) Session(ComicProject project)
    {
        var history = new EditorHistory();
        var pictures = new PictureLibrary(project.Pictures);
        var navigator = new PageNavigatorViewModel(history, project.Pages, pictures: pictures);
        return (navigator, navigator.CurrentPage.Editor, pictures, history);
    }

    [Fact]
    public void Insert_picture_asks_the_view_for_a_file_and_places_it_selected_at_its_own_shape()
    {
        var (_, editor, pictures, history) = Session(ComicProject.CreateNew());
        var panel = editor.Working.PanelOrder[0];
        PictureImportRequest? asked = null;
        editor.PictureImportRequested += r => asked = r;

        editor.InsertPictureCommand.Execute(null);
        Assert.Equal(new PictureImportRequest(panel, AsBackground: false), asked);
        Assert.True(editor.ImportPicture(asked!, "Tree.PNG", Png(30, 60, SKColors.Green)));

        var picture = Assert.IsType<PictureElement>(Assert.Single(editor.Working.Panels[panel].Elements));
        Assert.Equal(0.5, picture.Bounds.Width / picture.Bounds.Height, 6);
        Assert.True(editor.IsPictureContext);
        Assert.True(pictures.Files.ContainsKey(picture.ArtFileName));
        Assert.EndsWith(".png", picture.ArtFileName, StringComparison.Ordinal);

        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
        Assert.True(pictures.Files.ContainsKey(picture.ArtFileName)); // still there for redo
    }

    [Fact]
    public void A_background_picture_fills_the_selected_panel_in_one_undo_step()
    {
        var (_, editor, _, history) = Session(ComicProject.CreateNew());
        var panel = editor.Working.PanelOrder[0];
        editor.Select(panel);
        PictureImportRequest? asked = null;
        editor.PictureImportRequested += r => asked = r;

        editor.BackgroundPictureCommand.Execute(null);
        editor.ImportPicture(asked!, "city.png", Png(64, 48, SKColors.Gray));

        Assert.True(asked!.AsBackground);
        Assert.IsType<InlineBackground>(editor.Working.Panels[panel].Background);
        Assert.Equal("Picture", editor.BackgroundName);
        history.Undo();
        Assert.Null(editor.Working.Panels[panel].Background);
    }

    [Fact]
    public void A_file_that_isnt_a_picture_is_refused_with_a_reason()
    {
        var (_, editor, pictures, history) = Session(ComicProject.CreateNew());
        var request = new PictureImportRequest(editor.Working.PanelOrder[0], AsBackground: false);

        Assert.False(editor.ImportPicture(request, "notes.png", ArtFile.Png([1, 2, 3])));
        Assert.False(editor.ImportPicture(request, "notes.txt", Png(4, 4, SKColors.Red)));

        Assert.NotNull(editor.LastError);
        Assert.Empty(pictures.Files);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_picture_dropped_onto_the_page_is_placed_from_its_raw_bytes_like_a_picked_file()
    {
        var (_, editor, pictures, history) = Session(ComicProject.CreateNew());
        var panel = editor.Working.PanelOrder[0];
        var request = new PictureImportRequest(panel, AsBackground: false);

        Assert.True(editor.ImportPicture(request, "Tree.PNG", Png(30, 60, SKColors.Green).Bytes!));

        var picture = Assert.IsType<PictureElement>(Assert.Single(editor.Working.Panels[panel].Elements));
        Assert.Equal(0.5, picture.Bounds.Width / picture.Bounds.Height, 6);
        Assert.True(pictures.Files.ContainsKey(picture.ArtFileName));

        history.Undo();
        Assert.Empty(editor.Working.Panels[panel].Elements);
    }

    [Fact]
    public void A_dropped_file_that_isnt_a_picture_is_refused_with_a_reason()
    {
        var (_, editor, pictures, history) = Session(ComicProject.CreateNew());
        var request = new PictureImportRequest(editor.Working.PanelOrder[0], AsBackground: false);

        Assert.False(editor.ImportPicture(request, "notes.png", new byte[] { 1, 2, 3 }));

        Assert.NotNull(editor.LastError);
        Assert.Empty(pictures.Files);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void The_same_picture_imported_twice_is_one_file()
    {
        var library = new PictureLibrary();
        var changes = 0;
        library.Changed += () => changes++;

        var a = library.Add(Png(4, 4, SKColors.Red), "png");
        var b = library.Add(Png(4, 4, SKColors.Red), ".png");

        Assert.Equal(a, b);
        Assert.Single(library.Files);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_page_edited_without_a_picture_library_offers_no_import()
    {
        var editor = new PageEditorViewModel(new EditorHistory(), new Rect2D(0, 0, 210, 297), ComicProject.CreateNew().Pages[0].Document);

        Assert.False(editor.CanImportPictures);
        Assert.False(editor.InsertPictureCommand.CanExecute(null));
    }

    [Fact]
    public void Saving_writes_the_pictures_pages_use_reopening_reads_them_and_unused_ones_are_removed()
    {
        var project = ComicProject.CreateNew();
        var (navigator, editor, pictures, _) = Session(project);
        var panel = editor.Working.PanelOrder[0];
        editor.ImportPicture(new PictureImportRequest(panel, AsBackground: true), "sky.png", Png(20, 10, SKColors.SkyBlue));
        editor.ImportPicture(new PictureImportRequest(panel, AsBackground: false), "tree.png", Png(10, 20, SKColors.Green));
        var backgroundName = ((InlineBackground)editor.Working.Panels[panel].Background!).ArtFileName;
        var treeName = ((PictureElement)editor.Working.Panels[panel].Elements[0]).ArtFileName;

        var folder = project.SaveAs(Path.Combine(_root, "Comic"), navigator.Snapshot(), pictures: pictures.Files);
        var artDir = Directory.GetDirectories(folder, "art", SearchOption.AllDirectories).Single();
        Assert.True(File.Exists(Path.Combine(artDir, backgroundName)));
        Assert.True(File.Exists(Path.Combine(artDir, treeName)));
        File.WriteAllText(Path.Combine(artDir, "notes.txt"), "the artist's own file");

        var reopened = ComicProject.Open(folder);
        Assert.True(reopened.Pictures[backgroundName].SameContent(pictures.Files[backgroundName]));
        Assert.True(reopened.Pictures.ContainsKey(treeName));

        editor.DeleteElement(panel, 0);
        project.Save(navigator.Snapshot(), pictures: pictures.Files);
        Assert.False(File.Exists(Path.Combine(artDir, treeName)));
        Assert.True(File.Exists(Path.Combine(artDir, backgroundName)));
        Assert.True(File.Exists(Path.Combine(artDir, "notes.txt")));
    }

    [Fact]
    public void A_crash_recovery_copy_carries_the_pictures()
    {
        var project = ComicProject.CreateNew();
        var (navigator, editor, pictures, _) = Session(project);
        var panel = editor.Working.PanelOrder[0];
        editor.ImportPicture(new PictureImportRequest(panel, AsBackground: true), "sky.png", Png(20, 10, SKColors.SkyBlue));

        var snapshot = Path.Combine(_root, "snapshot");
        project.WriteCopy(snapshot, navigator.Snapshot(), pictures: pictures.Files);
        var recovered = ComicProject.OpenRecovered(snapshot, originalLocation: null);

        Assert.Single(recovered.Pictures);
        Assert.IsType<InlineBackground>(recovered.Pages[0].Document.Panels[panel].Background);
    }
}
