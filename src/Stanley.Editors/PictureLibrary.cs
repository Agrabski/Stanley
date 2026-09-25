using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors;

/// <summary>
/// The pictures an open comic's panels use (background pictures, picture elements), by
/// art file name - shared by every page, the way the Characters pane is. Pictures only ever
/// come in, never go (an undone import can be redone, a deleted picture undeleted); saving
/// writes just the ones pages still use (<see cref="UsedBy"/>).
/// </summary>
public sealed class PictureLibrary
{
    private IReadOnlyDictionary<string, ArtFile> _files;

    public PictureLibrary(IReadOnlyDictionary<string, ArtFile>? files = null) =>
        _files = new Dictionary<string, ArtFile>(files ?? new Dictionary<string, ArtFile>(), StringComparer.Ordinal);

    /// <summary>
    /// Every picture, as a snapshot: an import replaces it with a new one rather than
    /// changing it, so the page can be drawn from it on the render thread while more come in.
    /// </summary>
    public IReadOnlyDictionary<string, ArtFile> Files => _files;

    public event Action? Changed;

    /// <summary>Takes a picture in and returns the name panels refer to it by (<see cref="IssueArt.NameFor"/>) - the same picture twice is one entry.</summary>
    public string Add(ArtFile file, string extension)
    {
        var name = IssueArt.NameFor(file, extension);
        if (!_files.ContainsKey(name))
        {
            _files = new Dictionary<string, ArtFile>(_files, StringComparer.Ordinal) { [name] = file };
            Changed?.Invoke();
        }
        return name;
    }

    /// <summary>The pictures <paramref name="pages"/> use, by name - what a save writes.</summary>
    public IReadOnlyDictionary<string, ArtFile> UsedBy(IEnumerable<PageDocument> pages)
    {
        var used = new Dictionary<string, ArtFile>(StringComparer.Ordinal);
        foreach (var name in pages.SelectMany(p => p.Panels.Values).SelectMany(PanelElements.ArtFileNames))
        {
            if (_files.TryGetValue(name, out var file))
                used[name] = file;
        }
        return used;
    }
}
