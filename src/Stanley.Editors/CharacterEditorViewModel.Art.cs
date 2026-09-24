using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>Asks the view for an SVG or PNG to import as a sticker for <paramref name="Slot"/>.</summary>
public sealed record ArtImportRequest(string Slot);

/// <summary>
/// Drawing your own stickers and importing art (docs/sticker-system.md §13.2), and placing
/// drawn art on the character: the Sticker tab's size, turn and "hug the shape", and
/// dragging it on the stage.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    private readonly Dictionary<(StickerId, ViewAngle), ArtEditSession> _artEdits = [];
    private StickerAsset? _dragBaseline;

    public IRelayCommand<string> DrawYourOwnCommand { get; private set; } = null!;
    public IRelayCommand<string> ImportArtCommand { get; private set; } = null!;
    public IRelayCommand EditSelectedArtCommand { get; private set; } = null!;

    /// <summary>Raised to ask the view for a file to import ("Import..." in a gallery); the view answers with <see cref="ImportArt"/>.</summary>
    public event EventHandler<ArtImportRequest>? ArtImportRequested;

    private void InitializeArt()
    {
        DrawYourOwnCommand = new RelayCommand<string>(slot =>
        {
            if (slot != null)
                DrawYourOwn(slot);
        });
        ImportArtCommand = new RelayCommand<string>(slot =>
        {
            if (slot != null)
                ArtImportRequested?.Invoke(this, new ArtImportRequest(slot));
        });
        EditSelectedArtCommand = new RelayCommand(() =>
        {
            if (SelectedSticker is { } asset)
                DrawYourOwn(asset.Sticker.Slot);
        }, () => SelectedSticker is { HasArt: true });
    }

    private IArtEditing ArtEditing => Library?.ArtEditing ?? _fallbackArtEditing;
    private static readonly IArtEditing _fallbackArtEditing = new SystemArtEditing(Path.Combine(Path.GetTempPath(), "stanley-drawing"));

    /// <summary>
    /// Draws a sticker for <paramref name="slot"/> in the user's SVG editor: the selected
    /// sticker if it's a drawn one in that slot (its art for the view on the stage, or a
    /// template if that view isn't drawn yet), else a new sticker, worn at once. Each save
    /// there comes back as one undo step.
    /// </summary>
    public void DrawYourOwn(string slot)
    {
        var view = PreviewAngle;
        var asset = SelectedSticker is { HasArt: true } selected && selected.Sticker.Slot == slot ? selected : null;
        if (asset is null)
        {
            asset = StickerImport.NewDrawn(slot, UniqueName($"My {StickerSlots.Get(slot).Label.ToLowerInvariant()}"), view);
            ShowMessage(null);
            var drawn = asset;
            ApplyLook(c => LookEditing.Wear(c, drawn));
            SelectSticker(asset.Id);
        }
        var id = asset.Id;
        if (_artEdits.Remove((id, view), out var previous))
            previous.Dispose();
        var fileName = $"{Slug(asset.Sticker.Name)}-{id.Value}-{StickerAsset.ViewFileStem(view)}.svg";
        var session = ArtEditing.Edit(fileName, StickerImport.ArtToEdit(asset, view), text => ReimportArt(id, view, text), out var error);
        if (session is null)
        {
            ShowMessage($"Couldn't write the drawing: {error}");
            return;
        }
        _artEdits[(id, view)] = session;
        ShowMessage(error is not null
            ? $"Drawing {asset.Sticker.Name}: {error}."
            : $"Drawing {asset.Sticker.Name} in your SVG editor ({session.Path}) - every save there updates it here." +
              (view == ViewAngle.Front ? " Switch to Side and draw again for the side view." : ""));
    }

    /// <summary>A new version of a drawing, saved in the user's editor: one undo step. Anything it doesn't draw is said in the status bar.</summary>
    public void ReimportArt(StickerId id, ViewAngle view, string text)
    {
        if (Committed.Wardrobe.Find(id) is not { } asset)
            return;
        var result = StickerImport.WithArt(asset, view, text);
        if (result.Asset is not { } updated)
        {
            ShowMessage($"{asset.Sticker.Name}: {result.Error}.");
            return;
        }
        Apply(EditResult<CharacterDefinition>.Success(Committed with { Wardrobe = Committed.Wardrobe.With(updated) }));
        ShowMessage(result.Report.Count > 0 ? $"{asset.Sticker.Name}: {string.Join("; ", result.Report)}." : null);
    }

    /// <summary>
    /// Imports an SVG or PNG as a sticker for <paramref name="slot"/> and wears it, selected
    /// so it can be placed at once. A file drawn on a template imports as-is; anything else
    /// is pinned on the slot's region, centred and fitted. False, with the reason in the
    /// status bar, if it can't be read.
    /// </summary>
    public bool ImportArt(string slot, string fileName, ArtFile file)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var result = StickerImport.FromFile(slot, UniqueName(name.Length == 0 ? "Imported" : name), file, PreviewAngle);
        if (result.Asset is not { } asset)
        {
            ShowMessage($"Couldn't import {fileName}: {result.Error}.");
            return false;
        }
        ApplyLook(c => LookEditing.Wear(c, asset));
        SelectSticker(asset.Id);
        ShowMessage(result.Report.Count > 0
            ? $"{fileName}: {string.Join("; ", result.Report)}."
            : $"Imported {fileName} - drag it on the character to move it; size and turn it on the Sticker tab.");
        return true;
    }

    // ---------------------------------------------------------------- placing the selected sticker's art

    public bool HasArtParts => SelectedSticker is { HasArt: true };

    /// <summary>"Hug the shape": the art bends with the body (Warp) instead of keeping its shape (Pin). Raster art always keeps its shape.</summary>
    public bool SelectedHugsShape
    {
        get => SelectedSticker?.Sticker.Parts.Where(p => p.Art is not null).All(p => p.Art!.Mapping == ArtMapping.Warp) == true && HasArtParts;
        set => EditSelectedArt(a => a with { Mapping = value ? ArtMapping.Warp : ArtMapping.Pin });
    }

    /// <summary>The art's size, as a percentage of how it was drawn.</summary>
    public double SelectedArtScale
    {
        get => Math.Round((FirstArt?.Scale ?? 1) * 100);
        set
        {
            var ratio = Math.Clamp(value, 5, 800) / 100 / (FirstArt?.Scale ?? 1);
            EditSelectedArt(a => a with { Scale = Math.Round((a.Scale ?? 1) * ratio, 5) });
        }
    }

    /// <summary>How far the art is turned, in degrees.</summary>
    public double SelectedArtRotation
    {
        get => Math.Round(FirstArt?.Rotation ?? 0);
        set
        {
            var delta = Math.Clamp(value, -180, 180) - (FirstArt?.Rotation ?? 0);
            EditSelectedArt(a => a with { Rotation = Math.Round((a.Rotation ?? 0) + delta, 2) });
        }
    }

    private PartArt? FirstArt => SelectedSticker?.Sticker.Parts.FirstOrDefault(p => p.Art is not null)?.Art;

    /// <summary>Changes every drawn part of the selected sticker: a live preview inside a slider drag, otherwise one undo step.</summary>
    private void EditSelectedArt(Func<PartArt, PartArt> edit) =>
        EditSelectedSticker(s => s with { Parts = s.Parts.Select(p => p.Art is { } art ? p with { Art = edit(art) } : p).ToList() });

    /// <summary>Starts dragging the selected sticker's art on the stage (it's the gesture baseline).</summary>
    public bool BeginArtDrag()
    {
        if (SelectedSticker is not { HasArt: true } asset)
            return false;
        BeginGesture();
        _dragBaseline = asset;
        return true;
    }

    /// <summary>Moves the art by <paramref name="figureDelta"/> (figure space, from where the drag started) - converted to the template units its offset is in.</summary>
    public void UpdateArtDrag(Point2D figureDelta)
    {
        if (_dragBaseline is not { } asset || !IsGestureActive)
            return;
        var character = Committed;
        var figure = BodyRig.Build(character.Body, PreviewAngle, character.Skeleton, StagePose);
        var parts = asset.Sticker.Parts.Select(p =>
        {
            if (p.Art is not { } art)
                return p;
            var delta = RegionMapping.ToTemplate(figure, p.Region, p.Side, figureDelta);
            var offset = art.Offset ?? default;
            return p with { Art = art with { Offset = new Point2D(Math.Round(offset.X + delta.X, 2), Math.Round(offset.Y + delta.Y, 2)) } };
        }).ToList();
        UpdateGesture(EditResult<CharacterDefinition>.Success(LookEditing.UpdateSticker(character, asset.Sticker with { Parts = parts })));
    }

    public void EndArtDrag()
    {
        _dragBaseline = null;
        if (IsGestureActive)
            CommitGesture();
    }

    private void RaiseArtChanged()
    {
        OnPropertyChanged(nameof(HasArtParts));
        OnPropertyChanged(nameof(SelectedHugsShape));
        OnPropertyChanged(nameof(SelectedArtScale));
        OnPropertyChanged(nameof(SelectedArtRotation));
        EditSelectedArtCommand.NotifyCanExecuteChanged();
    }

    private string UniqueName(string name)
    {
        var taken = Committed.Wardrobe.Stickers.Values.Select(a => a.Sticker.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(name))
            return name;
        for (var n = 2; ; n++)
        {
            if (!taken.Contains($"{name} {n}"))
                return $"{name} {n}";
        }
    }

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return slug.Length == 0 ? "sticker" : slug;
    }
}
