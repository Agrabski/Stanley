using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// Drawing your own stickers and importing art (docs/sticker-system.md §13.2), and placing
/// drawn art on the character: the Sticker tab's size, turn and "hug the shape", and
/// dragging it on the stage.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    private readonly Dictionary<(StickerId, string Variant, ViewAngle), ArtEditSession> _artEdits = [];
    private StickerAsset? _dragBaseline;

    public IRelayCommand<string> DrawYourOwnCommand { get; private set; } = null!;
    public IRelayCommand<string> ImportArtCommand { get; private set; } = null!;
    public IRelayCommand EditSelectedArtCommand { get; private set; } = null!;

    /// <summary>Raised to ask the view for a file to import ("Import..." in a gallery); the view answers with <see cref="ImportArt"/>.</summary>
    public event EventHandler<ArtImportRequest>? ArtImportRequested;

    /// <summary>Raised when <see cref="DrawYourOwn"/> is asked to draw but no SVG editor is set up yet.</summary>
    public event EventHandler<SvgEditorConfigurationRequest>? SvgEditorConfigurationRequested;

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
                ArtImportRequested?.Invoke(this, new(slot));
        });
        EditSelectedArtCommand = new RelayCommand(() =>
        {
            if (SelectedSticker is { } asset)
                DrawYourOwn(asset.Sticker.Slot);
        }, () => SelectedSticker is { HasArt: true, HasText: false });
    }

    private IArtEditing ArtEditing => Library?.ArtEditing ?? _fallbackArtEditing;
    private static readonly IArtEditing _fallbackArtEditing = new SystemArtEditing(Path.Combine(Path.GetTempPath(), "stanley-drawing"));

    /// <summary>The configured SVG editor, for the picker to pre-fill with the current choice.</summary>
    public string? SvgEditorPath => ArtEditing.EditorPath;

    /// <summary>Sets the SVG editor to open drawings in, chosen through <see cref="SvgEditorConfigurationRequested"/>.</summary>
    public void SetSvgEditorPath(string path) => ArtEditing.EditorPath = path;

    /// <summary>
    /// Draws a sticker for <paramref name="slot"/> in the configured SVG editor: the selected
    /// sticker if it's a drawn one in that slot (its art for the view and expression - or the
    /// style it's worn in - on the stage, or a template if that view isn't drawn yet), else a
    /// new sticker, worn at once.
    /// Each save there comes back as one undo step. Nothing is created yet if no editor is set up -
    /// <see cref="SvgEditorConfigurationRequested"/> asks for one first, and calling this
    /// again afterwards picks up where it left off.
    /// </summary>
    public void DrawYourOwn(string slot)
    {
        if (ArtEditing.EditorPath is not { Length: > 0 })
        {
            SvgEditorConfigurationRequested?.Invoke(this, new(slot));
            return;
        }

        var view = PreviewAngle;
        var asset = SelectedSticker is { HasArt: true, HasText: false } selected && selected.Sticker.Slot == slot ? selected : null;
        if (asset is null)
        {
            asset = StickerImport.NewDrawn(slot, UniqueName($"My {StickerSlots.Get(slot).Label.ToLowerInvariant()}"), view);
            ShowMessage(null);
            var drawn = asset;
            ApplyLook(c => LookEditing.Wear(c, drawn));
            SelectSticker(asset.Id);
        }
        // The variant on the stage: the previewed expression's for a face, else the style it's worn in.
        ShowMessage(OpenArt(asset, asset.Sticker.VariantFor(asset.Sticker.Slot, StagePose?.Expression, ChosenStyle(asset.Id)), view, historySource: null));
    }

    /// <summary>
    /// Opens one of the character's stickers - its <paramref name="variant"/> seen from
    /// <paramref name="view"/> - in the user's SVG editor. Each save there comes back as one
    /// undo step, <paramref name="historySource"/>'s when given (a face drawn from a page:
    /// undoing it goes back to that page, not to this editor). Returns what to tell the user.
    /// </summary>
    public string DrawVariant(StickerId id, string variant, ViewAngle view, object? historySource = null) =>
        Committed.Wardrobe.Find(id) is { } asset ? OpenArt(asset, variant, view, historySource) : "That sticker isn't there any more.";

    private string OpenArt(StickerAsset asset, string variant, ViewAngle view, object? historySource)
    {
        var id = asset.Id;
        if (_artEdits.Remove((id, variant, view), out var previous))
            previous.Dispose();
        // A sticker's first variant is the sticker; any other is named after its expression (or style) too.
        var first = asset.Sticker.Variants.Count == 0 || asset.Sticker.Variants[0] == variant;
        var variantName = StickerSlots.Get(asset.Sticker.Slot).IsFace ? ExpressionPresets.VariantName(asset.Sticker.Slot, variant) : LookEditing.StyleName(variant);
        var name = first ? asset.Sticker.Name : $"{asset.Sticker.Name} ({variantName.ToLowerInvariant()})";
        var fileName = $"{Slug(asset.Sticker.Name)}-{id.Value}-{(first ? "" : Slug(variant) + "-")}{StickerAsset.ViewFileStem(view)}.svg";
        var session = ArtEditing.Edit(fileName, StickerImport.ArtToEdit(asset, view, variant), text =>
        {
            using (historySource is null ? null : History.Group($"Draw {name}", historySource))
                ReimportArt(id, view, text, variant);
        }, out var error);
        if (session is null)
            return $"Couldn't write the drawing: {error}";
        _artEdits[(id, variant, view)] = session;
        return error is not null
            ? $"Drawing {name}: {error}."
            : $"Drawing {name} in {session.Path} - every save there updates it here." +
              (view == ViewAngle.Front ? " Switch to Side and draw again for the side view." : "");
    }

    /// <summary>A new version of a drawing (of <paramref name="variant"/>, else the sticker's first), saved in the user's editor: one undo step. Anything it doesn't draw is said in the status bar.</summary>
    public void ReimportArt(StickerId id, ViewAngle view, string text, string? variant = null)
    {
        if (Committed.Wardrobe.Find(id) is not { } asset)
            return;
        var result = StickerImport.WithArt(asset, view, text, variant);
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
