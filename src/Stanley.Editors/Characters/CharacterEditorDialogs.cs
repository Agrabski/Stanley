using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors;

/// <summary>
/// The character editor's requests that need a window: the file pickers behind a gallery's
/// Import... and a pattern's Custom..., and the SVG editor question behind Draw your own...
/// They're plain events on <see cref="CharacterEditorViewModel"/>, so whatever hosts the
/// editor's controls - the ribbon, or the canvas layout's inspector - attaches one of these to
/// its view model; with nobody listening those buttons would silently do nothing.
/// </summary>
internal sealed class CharacterEditorDialogs(Control host)
{
    private CharacterEditorViewModel? _editor;

    /// <summary>Listens to <paramref name="editor"/> instead of the editor before (null: to none) - call it whenever the host's DataContext changes.</summary>
    public void Attach(CharacterEditorViewModel? editor)
    {
        if (_editor == editor)
            return;
        if (_editor != null)
        {
            _editor.TileImportRequested -= OnTileImportRequested;
            _editor.ArtImportRequested -= OnArtImportRequested;
            _editor.SvgEditorConfigurationRequested -= OnSvgEditorConfigurationRequested;
            _editor.ShapeImportRequested -= OnShapeImportRequested;
        }
        _editor = editor;
        if (_editor != null)
        {
            _editor.TileImportRequested += OnTileImportRequested;
            _editor.ArtImportRequested += OnArtImportRequested;
            _editor.SvgEditorConfigurationRequested += OnSvgEditorConfigurationRequested;
            _editor.ShapeImportRequested += OnShapeImportRequested;
        }
    }

    /// <summary>"Custom..." in a pattern or texture gallery: pick an SVG or PNG and hand it to the editor.</summary>
    private async void OnTileImportRequested(object? sender, TileImportRequest request)
    {
        if (sender is CharacterEditorViewModel editor && await PickArt(editor, request.Texture ? "Choose a texture tile (a greyscale image)" : "Choose a pattern tile (one repeat)") is { } picked)
            editor.ImportTile(request.Slot, picked.Name, picked.File, request.Texture);
    }

    /// <summary>"Import..." in a gallery: pick an SVG or PNG and make it a sticker for that slot.</summary>
    private async void OnArtImportRequested(object? sender, ArtImportRequest request)
    {
        if (sender is CharacterEditorViewModel editor && await PickArt(editor, "Import a picture to wear (SVG or PNG)") is { } picked)
            editor.ImportArt(request.Slot, picked.Name, picked.File);
    }

    /// <summary>"Import shape..." on the Module tab: pick an SVG and make its outline the part's shape.</summary>
    private async void OnShapeImportRequested(object? sender, ShapeImportRequest request)
    {
        if (sender is CharacterEditorViewModel editor && await PickArt(editor, "Import a shape (an SVG drawing - its first closed path)", svgOnly: true) is { } picked)
            editor.ImportShape(request.Part, request.Piece, picked.Name, picked.File);
    }

    /// <summary>No SVG editor is set up yet: ask, and pick up drawing again if one was chosen.</summary>
    private async void OnSvgEditorConfigurationRequested(object? sender, SvgEditorConfigurationRequest request)
    {
        if (sender is not CharacterEditorViewModel editor || TopLevel.GetTopLevel(host) is not Window owner)
            return;
        if (await SvgEditorPicker.PickAsync(owner, editor.SvgEditorPath) is { Length: > 0 } chosen)
        {
            editor.SetSvgEditorPath(chosen);
            editor.DrawYourOwn(request.Slot);
        }
    }

    private async Task<(string Name, ArtFile File)?> PickArt(CharacterEditorViewModel editor, string title, bool svgOnly = false)
    {
        if (TopLevel.GetTopLevel(host) is not { } top)
            return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = svgOnly ? [new("SVG drawing") { Patterns = ["*.svg"] }] : [new("SVG or PNG") { Patterns = ["*.svg", "*.png"] }],
        });
        if (files is not [var picked])
            return null;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var file = picked.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? ArtFile.Svg(System.Text.Encoding.UTF8.GetString(bytes)) : ArtFile.Png(bytes);
            return (picked.Name, file);
        }
        catch (IOException e)
        {
            editor.ShowMessage($"Couldn't read {picked.Name}: {e.Message}");
            return null;
        }
    }
}
