using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>Asks the view for a picture file: for <paramref name="Panel"/>'s background, or to place in it.</summary>
public sealed record PictureImportRequest(PanelId Panel, bool AsBackground);

public sealed partial class PageEditorViewModel
{
    private PictureLibrary? _pictures;
    private static readonly IReadOnlyDictionary<string, ArtFile> NoPictures = new Dictionary<string, ArtFile>();

    /// <summary>Where the comic's pictures live (shared by every page); null for a page edited on its own, which then can't import any.</summary>
    public PictureLibrary? Pictures
    {
        get => _pictures;
        set
        {
            if (_pictures != null)
                _pictures.Changed -= RaisePicturesChanged;
            _pictures = value;
            if (_pictures != null)
                _pictures.Changed += RaisePicturesChanged;
            RaisePicturesChanged();
        }
    }

    /// <summary>The pictures to draw with, by art file name.</summary>
    public IReadOnlyDictionary<string, ArtFile> PictureSnapshot => _pictures?.Files ?? NoPictures;

    public bool CanImportPictures => _pictures != null;

    private void RaisePicturesChanged()
    {
        OnPropertyChanged(nameof(PictureSnapshot));
        OnPropertyChanged(nameof(CanImportPictures));
        InsertPictureCommand?.NotifyCanExecuteChanged();
        BackgroundPictureCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Raised to ask the view for a picture file; it answers with <see cref="ImportPicture"/>.</summary>
    public event Action<PictureImportRequest>? PictureImportRequested;

    /// <summary>Insert tab: a picture from a file, placed in the selected (or first) panel.</summary>
    public IRelayCommand InsertPictureCommand { get; private set; } = null!;

    /// <summary>A picture from a file as the selected panel's background, filling it.</summary>
    public IRelayCommand BackgroundPictureCommand { get; private set; } = null!;

    private void InitializePictureCommands()
    {
        InsertPictureCommand = new RelayCommand(() =>
        {
            var panel = _selectedPanelId is { } id && Working.Panels.ContainsKey(id) ? id : Working.PanelOrder[0];
            RequestPictureImport(panel, asBackground: false);
        }, () => CanImportPictures && Working.PanelOrder.Count > 0);
        BackgroundPictureCommand = new RelayCommand(() => RequestPictureImport(_selectedPanelId!.Value, asBackground: true),
            () => CanImportPictures && HasSelectedPanel);
    }

    private void NotifyPictureCommands()
    {
        InsertPictureCommand.NotifyCanExecuteChanged();
        BackgroundPictureCommand.NotifyCanExecuteChanged();
    }

    public void RequestPictureImport(PanelId panelId, bool asBackground)
    {
        if (CanImportPictures && Working.Panels.ContainsKey(panelId))
            PictureImportRequested?.Invoke(new PictureImportRequest(panelId, asBackground));
    }

    /// <summary>
    /// The view's answer to <see cref="PictureImportRequested"/>: takes the picture into the
    /// comic and uses it - as the panel's background (filling it) or placed in the middle of
    /// it at its own shape, selected - in one undo step. False, with the reason in
    /// <see cref="EditorFramework.EditorViewModel{T}.LastError"/>, if it isn't a picture Stanley can read.
    /// </summary>
    public bool ImportPicture(PictureImportRequest request, string fileName, ArtFile file)
    {
        if (_pictures is null || !Working.Panels.TryGetValue(request.Panel, out var panel))
            return false;
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (!IssueArt.Extensions.Contains(extension) || PictureRenderer.Size(file) is not { } size)
        {
            Apply(EditResult<PageDocument>.Failure($"Couldn't read {fileName} as a picture (PNG, JPEG, WebP, GIF, BMP or SVG)."));
            return false;
        }

        var name = _pictures.Add(file, extension);
        if (request.AsBackground)
        {
            SetPanelBackground(request.Panel, new InlineBackground(name));
            return true;
        }

        var placed = PictureEditing.Place(Bounds(panel), size, name, _newShapeLayer);
        if (!placed.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(placed.Error!));
            return false;
        }
        Apply(EditPanel(Working, request.Panel, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, placed.Value] })));
        var index = IndexOfElement(request.Panel, placed.Value.Id);
        if (index >= 0)
            SelectElement(request.Panel, index);
        return index >= 0;
    }

    /// <summary>
    /// Same as <see cref="ImportPicture(PictureImportRequest, string, ArtFile)"/>, from a picture's
    /// raw bytes rather than an already-built <see cref="ArtFile"/> - what a dropped OS file or a
    /// freshly read stream hands over.
    /// </summary>
    public bool ImportPicture(PictureImportRequest request, string fileName, byte[] bytes) =>
        ImportPicture(request, fileName, IssueArt.IsSvg(fileName) ? ArtFile.Svg(System.Text.Encoding.UTF8.GetString(bytes)) : ArtFile.Png(bytes));

    /// <summary>A placed picture is selected: the ribbon shows its "Picture" contextual tab.</summary>
    public bool IsPictureContext => SelectedElement is PictureElement;
}
