using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

public sealed partial class CharacterEditorViewModel
{
    private StickerId? _selectedSticker;

    public IRelayCommand<StickerChoice> WearCommand { get; private set; } = null!;
    public IRelayCommand<StickerChoice> WearLeftEyeCommand { get; private set; } = null!;
    public IRelayCommand<StickerChoice> WearRightEyeCommand { get; private set; } = null!;
    public IRelayCommand<ColorSwatchChoice> SetColorCommand { get; private set; } = null!;
    public IRelayCommand TakeOffSelectedCommand { get; private set; } = null!;
    public IRelayCommand RemoveSelectedCommand { get; private set; } = null!;
    public IRelayCommand MoveSelectedUpCommand { get; private set; } = null!;
    public IRelayCommand MoveSelectedDownCommand { get; private set; } = null!;
    public IRelayCommand DeselectStickerCommand { get; private set; } = null!;
    public IRelayCommand DuplicateSelectedCommand { get; private set; } = null!;
    public IRelayCommand<string> WearTextCommand { get; private set; } = null!;
    public IRelayCommand<string> InsertEmojiCommand { get; private set; } = null!;

    private void InitializeLook()
    {
        WearCommand = new RelayCommand<StickerChoice>(choice =>
        {
            if (choice != null)
                Wear(choice);
        });
        WearLeftEyeCommand = new RelayCommand<StickerChoice>(choice =>
        {
            if (choice != null)
                WearEyeSide(choice, LimbSide.Left);
        });
        WearRightEyeCommand = new RelayCommand<StickerChoice>(choice =>
        {
            if (choice != null)
                WearEyeSide(choice, LimbSide.Right);
        });
        SetColorCommand = new RelayCommand<ColorSwatchChoice>(choice =>
        {
            if (choice != null)
                SetSlotColor(choice.Slot, choice.Color);
        });
        TakeOffSelectedCommand = new RelayCommand(() => EditSelected(id => c => LookEditing.TakeOff(c, id)), () => SelectedStickerIsWorn);
        RemoveSelectedCommand = new RelayCommand(() =>
        {
            if (_selectedSticker is { } id)
            {
                SelectSticker(null);
                // A removed streak leaves its colour behind otherwise.
                Apply(EditResult<CharacterDefinition>.Success(HairEditing.DropOrphanStreakColors(LookEditing.RemoveFromWardrobe(Committed, id))));
            }
        }, () => HasSelectedSticker);
        MoveSelectedUpCommand = new RelayCommand(() => EditSelected(id => c => LookEditing.MoveInStack(c, id, +1)), () => SelectedStickerIsWorn);
        MoveSelectedDownCommand = new RelayCommand(() => EditSelected(id => c => LookEditing.MoveInStack(c, id, -1)), () => SelectedStickerIsWorn);
        DeselectStickerCommand = new RelayCommand(() => SelectSticker(null));
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected, () => CanDuplicateSelected);
        WearTextCommand = new RelayCommand<string>(text =>
        {
            if (!string.IsNullOrWhiteSpace(text))
                WearText(text);
        });
        InsertEmojiCommand = new RelayCommand<string>(emoji =>
        {
            if (emoji != null)
                SelectedText += emoji;
        });
        PreviewExpressionCommand = new RelayCommand<ExpressionPresetChoice>(choice =>
        {
            if (choice != null)
                PreviewExpression = choice.Preset;
        });
    }

    // ---------------------------------------------------------------- galleries

    /// <summary>The hair gallery.</summary>
    public IReadOnlyList<SlotGallery> HairGalleries => Galleries(StickerSlots.Hair);

    /// <summary>
    /// The face's galleries: Eyes (or, split left/right - docs/sticker-system.md §21 - Left
    /// eye and Right eye instead), Brows, Mouth, Nose.
    /// </summary>
    public IReadOnlyList<SlotGallery> FaceGalleries =>
        (IsEyesSplit ? [EyeSideGallery(LimbSide.Left), EyeSideGallery(LimbSide.Right)] : new[] { Gallery(StickerSlots.Eyes) })
        .Concat(Galleries(StickerSlots.Brows, StickerSlots.Mouth, StickerSlots.Nose))
        .ToList();

    /// <summary>The clothes slots' galleries (Top, Prints, Outer, Bottom, Shoes).</summary>
    public IReadOnlyList<SlotGallery> ClothesGalleries => Galleries(StickerSlots.Top, StickerSlots.Print, StickerSlots.Outer, StickerSlots.Bottom, StickerSlots.Shoes);

    /// <summary>Hats, glasses and everything else.</summary>
    public IReadOnlyList<SlotGallery> AccessoryGalleries => Galleries(StickerSlots.Headwear, StickerSlots.Glasses, StickerSlots.Accessory);

    private IReadOnlyList<SlotGallery> Galleries(params string[] slots) => slots.Select(Gallery).ToList();

    /// <summary>
    /// A slot's choices: None, then this character's wardrobe for the slot, then the
    /// library's stickers it doesn't already have a copy of - each previewed on this
    /// character wearing it, on top of what the slot already holds.
    /// </summary>
    public SlotGallery Gallery(string slot)
    {
        var info = StickerSlots.Get(slot);
        var character = LookWorking;
        var worn = character.Stickers.TryGetValue(slot, out var ids) ? ids : [];
        var pose = StagePose;
        var choices = new List<StickerChoice> { new("None", slot, LookEditing.ClearSlot(character, slot), null, null, worn.Count == 0, pose) };
        var owned = character.Wardrobe.Stickers.Values.Where(a => a.Sticker.Slot == slot).OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var library = Stanley.StickerLibrary.StickerLibrary.ForSlot(slot).ToList();
        if (info.StampsCopies)
        {
            // A design is offered once however many copies are worn (a click stamps another):
            // the character's own designs, each by the first copy put on (the one further copies
            // are spotted from), then the library's. Copies of a library print stamp from the
            // library; a library sticker that isn't placed (gloves) is offered as the worn copy
            // itself, so a second click takes it off again.
            var libraryNames = library.Select(l => l.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            var byWear = worn.Select(id => character.Wardrobe.Find(id)).OfType<StickerAsset>().Concat(owned);
            foreach (var asset in byWear.Where(a => (!StickerCopies.IsPlaceable(a.Sticker) || !libraryNames.Contains(a.Sticker.Name)) && seen.Add(a.Sticker.Name)))
                choices.Add(new(asset.Sticker.Name, slot, LookEditing.Wear(character, asset), asset, null, IsWornDesign(character, slot, asset.Sticker.Name), pose));
            foreach (var item in library.Where(l => seen.Add(l.Name)))
                choices.Add(new(item.Name, slot, LookEditing.Wear(character, item.Preview), null, item, IsWornDesign(character, slot, item.Name), pose));
        }
        else
        {
            foreach (var asset in owned)
                choices.Add(new(asset.Sticker.Name, slot, LookEditing.Wear(character, asset), asset, null, worn.Contains(asset.Id), pose));
            var ownedSources = owned.Select(a => a.Sticker.Source).OfType<string>().ToHashSet();
            foreach (var item in library.Where(l => !ownedSources.Contains(Stanley.StickerLibrary.StickerLibrary.SourcePrefix + l.Key)))
                choices.Add(new(item.Name, slot, LookEditing.Wear(character, item.Preview), null, item, false, pose));
        }
        // "Star ×3, HELLO": copies of one design counted, not listed.
        var current = worn.Count == 0 ? "None" : string.Join(", ", worn.Select(id => character.Wardrobe.Find(id)?.Sticker.Name ?? "?")
            .GroupBy(n => n, StringComparer.CurrentCultureIgnoreCase).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));
        return new(info, current, choices, WearCommand, DrawYourOwnCommand, ImportArtCommand, slot == StickerSlots.Print ? WearTextCommand : null);
    }

    /// <summary>
    /// A gallery click, one undo step. It only ever adds: what isn't worn goes on over
    /// whatever the slot already holds, selected so the Sticker tab's Forward and Back are
    /// right there; what is worn comes off again. None takes everything in the slot off. In
    /// a slot that stamps copies (Prints, Other), a placed design - drawn art or text - puts
    /// on another copy each click instead.
    /// </summary>
    private void Wear(StickerChoice choice)
    {
        if (choice.IsNone)
        {
            ApplyLook(c => LookEditing.ClearSlot(c, choice.Slot));
            return;
        }
        var character = LookWorking;
        var worn = character.Stickers.TryGetValue(choice.Slot, out var ids) ? ids : [];
        if (choice.StampsCopy)
        {
            // A print or a badge: every click puts on another copy, in the next free spot across the chest.
            var design = choice.Asset?.Sticker ?? choice.Library!.Asset.Sticker;
            var spot = StickerCopies.Spot(StickerCopies.WornCopies(character, choice.Slot, design.Name));
            var copy = choice.Asset is { } own
                ? (worn.Contains(own.Id) ? StickerCopies.Copy(own, spot) : own)
                : StickerCopies.Copy(choice.Library!.Instantiate(), spot);
            ApplyLook(c => LookEditing.Wear(c, copy));
            SelectSticker(copy.Id);
        }
        else if (choice.Asset is { } asset && worn.Contains(asset.Id))
        {
            ApplyLook(c => LookEditing.TakeOff(c, asset.Id));
            if (_selectedSticker == asset.Id)
                SelectSticker(null);
        }
        else
        {
            var wearing = choice.Asset ?? choice.Library!.Instantiate();
            ApplyLook(c => LookEditing.Wear(c, wearing));
            SelectSticker(wearing.Id);
        }
    }

    /// <summary>Whether any worn sticker in <paramref name="slot"/> is the design named <paramref name="name"/>.</summary>
    private static bool IsWornDesign(CharacterDefinition character, string slot, string name) => StickerCopies.WornCopies(character, slot, name) > 0;

    // ---------------------------------------------------------------- split eyes (docs/sticker-system.md §21)

    /// <summary>Whether this character's eyes are split left/right: the Face group shows two eye galleries and swatches instead of one.</summary>
    public bool IsEyesSplit
    {
        get => LookEditing.IsEyesSplit(LookWorking);
        set
        {
            if (value == IsEyesSplit)
                return;
            _previewEyeLeft = _previewEyeRight = null;
            ApplyLook(value ? LookEditing.SplitEyes : LookEditing.UnsplitEyes);
        }
    }

    /// <summary>Whether there's an eyes sticker worn to split - the checkbox is disabled otherwise.</summary>
    public bool CanSplitEyes => LookWorking.Stickers.TryGetValue(StickerSlots.Eyes, out var worn) && worn.Count > 0;

    /// <summary>
    /// One eye's gallery once the eyes are split: this character's own eye designs
    /// (deduplicated by name - splitting can leave two identical copies, one per side),
    /// then the library's, each previewed on this side alone.
    /// </summary>
    public SlotGallery EyeSideGallery(LimbSide side)
    {
        const string slot = StickerSlots.Eyes;
        var character = LookWorking;
        var info = StickerSlots.Get(slot) with { Label = side == LimbSide.Left ? "Left eye" : "Right eye" };
        var pose = StagePose;
        var currentId = LookEditing.EyeSticker(character, side)?.Id;
        var owned = character.Wardrobe.Stickers.Values.Where(a => a.Sticker.Slot == slot)
            .OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase)
            .GroupBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.FirstOrDefault(a => a.Id == currentId) ?? g.First())
            .ToList();
        var choices = new List<StickerChoice>();
        foreach (var asset in owned)
        {
            var isCurrent = asset.Id == currentId;
            // Already worn on the other side, it needs a copy of its own - resolved here so the
            // preview and the click (SelectSticker) agree on which id actually gets worn.
            var target = LookEditing.ResolveForSide(character, asset, side);
            choices.Add(new(asset.Sticker.Name, slot, LookEditing.WearOnSide(character, target, side), target, null, isCurrent, pose));
        }
        var ownedSources = owned.Select(a => a.Sticker.Source).OfType<string>().ToHashSet();
        foreach (var item in Stanley.StickerLibrary.StickerLibrary.ForSlot(slot).Where(l => !ownedSources.Contains(Stanley.StickerLibrary.StickerLibrary.SourcePrefix + l.Key)))
            choices.Add(new(item.Name, slot, LookEditing.WearOnSide(character, item.Preview, side), null, item, false, pose));
        var currentName = currentId is { } id && character.Wardrobe.Find(id) is { } cur ? cur.Sticker.Name : "None";
        return new(info, currentName, choices, side == LimbSide.Left ? WearLeftEyeCommand : WearRightEyeCommand);
    }

    /// <summary>A click in a split eye's gallery: wears the choice on that side only, one undo step.</summary>
    private void WearEyeSide(StickerChoice choice, LimbSide side)
    {
        var wearing = LookEditing.ResolveForSide(LookWorking, choice.Asset ?? choice.Library!.Instantiate(), side);
        ApplyLook(c => LookEditing.WearOnSide(c, wearing, side));
        SelectSticker(wearing.Id);
    }

    /// <summary>The Sticker tab's "Duplicate": another copy of the selected print or badge, a small step from it, selected to move.</summary>
    private void DuplicateSelected()
    {
        if (SelectedSticker is not { } asset || !CanDuplicateSelected)
            return;
        var copy = StickerCopies.Copy(asset, StickerCopies.DuplicateStep);
        ApplyLook(c => LookEditing.Wear(c, copy));
        SelectSticker(copy.Sticker.Id);
    }

    /// <summary>Whether the selected sticker is worn in a slot that stamps copies and is placed (art or text), so another copy of it makes sense.</summary>
    public bool CanDuplicateSelected =>
        SelectedStickerIsWorn && SelectedSticker is { } asset && StickerSlots.Get(asset.Sticker.Slot).StampsCopies && StickerCopies.IsPlaceable(asset.Sticker);

    /// <summary>The Prints gallery's "Text" button: wears a new text print, selected so its text can be typed over at once on the Sticker tab.</summary>
    private void WearText(string text)
    {
        var chestTaken = Working.Stickers.TryGetValue(StickerSlots.Print, out var worn) && worn.Count > 0;
        var asset = TextPrints.New(text, chestTaken ? TextPrints.BelowAnotherPrint : null);
        ApplyLook(c => LookEditing.Wear(c, asset));
        SelectSticker(asset.Id);
        TextPrintWorn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the "Text" button has put on a new text print, for the view to open its text box.</summary>
    public event EventHandler? TextPrintWorn;

    // ---------------------------------------------------------------- preview expression

    private ExpressionPresetDefinition _previewExpression = ExpressionPresets.Get(ExpressionPreset.Neutral);
    private string? _previewEyeLeft;
    private string? _previewEyeRight;

    public IRelayCommand<ExpressionPresetChoice> PreviewExpressionCommand { get; private set; } = null!;

    /// <summary>The expression the stage (and the Look galleries) show - only a preview: expressions are set per panel.</summary>
    public ExpressionPresetDefinition PreviewExpression
    {
        get => _previewExpression;
        set
        {
            if (_previewExpression == value)
                return;
            _previewExpression = value;
            _previewEyeLeft = _previewEyeRight = null; // a whole-face preset starts both eyes fresh
            OnPropertyChanged();
            RaisePreviewPoseChanged();
        }
    }

    /// <summary>
    /// What the stage shows the character in: the previewed view and expression (null at
    /// rest, front view and neutral). Split eyes (docs/sticker-system.md §21) get the
    /// preset's eyes on both sides, then <see cref="PreviewLeftEye"/>/<see cref="PreviewRightEye"/>
    /// override either one.
    /// </summary>
    public Stanley.ProjectModel.Poses.PoseData? StagePose
    {
        get
        {
            if (_previewExpression.Preset == ExpressionPreset.Neutral && _previewEyeLeft is null && _previewEyeRight is null)
                return null;
            var pose = ExpressionPresets.Apply(new Stanley.ProjectModel.Poses.PoseData(PreviewAngle, [], new()), _previewExpression, IsEyesSplit);
            if (_previewEyeLeft is { } left)
                pose = ExpressionPresets.SetVariant(pose, StickerSlots.EyesLeft, left);
            if (_previewEyeRight is { } right)
                pose = ExpressionPresets.SetVariant(pose, StickerSlots.EyesRight, right);
            return pose;
        }
    }

    /// <summary>Every expression, as a close-up of this character.</summary>
    public IReadOnlyList<ExpressionPresetChoice> PreviewExpressionChoices
    {
        get
        {
            var rest = new Stanley.ProjectModel.Poses.PoseData(PreviewAngle, [], new());
            return ExpressionPresets.All.Select(p => new ExpressionPresetChoice(p, LookWorking, ExpressionPresets.Apply(rest, p, IsEyesSplit), p == _previewExpression)).ToList();
        }
    }

    /// <summary>The eyes' variant vocabulary (docs/sticker-system.md §7), for the split "Left eye"/"Right eye" expression dropdowns.</summary>
    public IReadOnlyList<EyeExpressionOption> EyeExpressionOptions { get; } =
        ExpressionPresets.Vocabulary[StickerSlots.Eyes].Select(v => new EyeExpressionOption(v, ExpressionPresets.VariantName(StickerSlots.Eyes, v))).ToList();

    /// <summary>The left eye's previewed expression, once split - the whole-face preset's until picked separately.</summary>
    public string PreviewLeftEye
    {
        get => _previewEyeLeft ?? _previewExpression.Eyes;
        set
        {
            if (value == PreviewLeftEye)
                return;
            _previewEyeLeft = value == _previewExpression.Eyes ? null : value;
            RaisePreviewPoseChanged();
        }
    }

    /// <summary>The right eye's previewed expression, once split - the whole-face preset's until picked separately.</summary>
    public string PreviewRightEye
    {
        get => _previewEyeRight ?? _previewExpression.Eyes;
        set
        {
            if (value == PreviewRightEye)
                return;
            _previewEyeRight = value == _previewExpression.Eyes ? null : value;
            RaisePreviewPoseChanged();
        }
    }

    /// <summary>What the previewed expression can't show on this face - "Dots has no wink" - or null.</summary>
    public string? ExpressionWarning
    {
        get
        {
            var missing = CharacterLooks.Resolve(LookWorking).Stickers
                .Where(w => _previewExpression.Variants.TryGetValue(w.Slot, out var v) && v != ExpressionPresets.Neutral && !w.Asset.Sticker.Variants.Contains(v))
                .Select(w => $"{w.Asset.Sticker.Name} ({StickerSlots.Get(w.Slot).Label.ToLowerInvariant()}) has no \"{_previewExpression.Variants[w.Slot]}\"")
                .ToList();
            return missing.Count == 0 ? null : string.Join("; ", missing) + " - showing neutral.";
        }
    }

    private void RaisePreviewPoseChanged()
    {
        OnPropertyChanged(nameof(StagePose));
        OnPropertyChanged(nameof(PreviewExpressionChoices));
        OnPropertyChanged(nameof(PreviewLeftEye));
        OnPropertyChanged(nameof(PreviewRightEye));
        OnPropertyChanged(nameof(ExpressionWarning));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HairGalleries));
        OnPropertyChanged(nameof(FaceGalleries));
        RaiseStylesChanged(); // previewed in the stage's view
    }

    // ---------------------------------------------------------------- colours

    private List<ColorSlotEditor> _colorEditors = [];

    /// <summary>A colour (and fabric) dropdown for each colour slot the worn stickers use, skin first.</summary>
    public IReadOnlyList<ColorSlotEditor> ColorEditors
    {
        get
        {
            RefreshColorEditors();
            return _colorEditors;
        }
    }

    /// <summary>Brings the editors up to date: the same objects while the slots stay the same (so an open dropdown stays open), a new list when they change. True if the list changed.</summary>
    private bool RefreshColorEditors()
    {
        var character = LookWorking;
        var look = CharacterLooks.Resolve(character);
        // A hair piece is listed once it has a colour of its own (docs: modular hair); a streak never is.
        var slots = HairEditing.ColorGroupSlots(character, LookEditing.ColorSlotsInUse(character));
        var changed = !slots.SequenceEqual(_colorEditors.Select(e => e.Slot));
        if (changed)
            _colorEditors = slots.Select(NewColorEditor).ToList();
        foreach (var editor in _colorEditors)
            RefreshColorEditor(editor, character, look);
        return changed;
    }

    private ColorSlotEditor NewColorEditor(string slot) =>
        new(this, slot, Palette(slot).Select(p => new ColorSwatchChoice(slot, p.Name, p.Color)).ToList());

    private static void RefreshColorEditor(ColorSlotEditor editor, CharacterDefinition character, CharacterLook look) =>
        editor.Refresh(look.Color(editor.Slot, editor.Slot == CharacterDefinition.SkinSlot ? character.Skin : ColorValue.FromHex("#9a9a9a")), look.FabricOf(editor.Slot));

    internal void SetSlotColor(string slot, ColorValue color) => ApplyLook(c => LookEditing.SetColor(c, slot, color));

    /// <summary>Changes a colour slot's fabric, starting from what it wears now: a live preview inside a slider drag, otherwise one undo step.</summary>
    internal void EditFabric(string slot, Func<Fabric, Fabric> edit)
    {
        var baseline = IsGestureActive ? Working : Committed;
        var current = CharacterLooks.Resolve(ProjectLook(baseline)).FabricOf(slot) ?? new Fabric();
        var next = edit(current);
        if (Equals(next, current))
            return;
        var result = EditResult<CharacterDefinition>.Success(StoreLookEdit(baseline, c => LookEditing.SetFabric(c, slot, next)));
        if (IsGestureActive)
            UpdateGesture(result);
        else
            Apply(result);
    }

    /// <summary>Raised to ask the view for a tile file ("Custom..." in a pattern or texture gallery); the view answers with <see cref="ImportTile"/>.</summary>
    public event EventHandler<TileImportRequest>? TileImportRequested;

    internal void RequestTileImport(TileImportRequest request) => TileImportRequested?.Invoke(this, request);

    /// <summary>Prefix of a texture tile's file name, telling it apart from pattern tiles in the character's <c>patterns/</c> folder.</summary>
    public const string TextureTilePrefix = "texture-";

    /// <summary>Tiles on offer: for patterns, the library's then the character's own; for textures, the character's own texture tiles.</summary>
    internal static IEnumerable<(string Name, ArtFile File)> TileChoices(CharacterDefinition character, bool texture)
    {
        var library = texture ? [] : Stanley.StickerLibrary.StickerLibrary.PatternTiles.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => (t.Key, t.Value)).ToList();
        var own = character.Wardrobe.Tiles
            .Where(t => t.Key.StartsWith(TextureTilePrefix, StringComparison.Ordinal) == texture)
            .Where(t => !Stanley.StickerLibrary.StickerLibrary.PatternTiles.TryGetValue(t.Key, out var shipped) || !shipped.SameContent(t.Value))
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => (t.Key, t.Value));
        return library.Concat(own);
    }

    internal static string TileLabel(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.StartsWith(TextureTilePrefix, StringComparison.Ordinal))
            stem = stem[TextureTilePrefix.Length..];
        stem = stem.Replace('-', ' ');
        return stem.Length == 0 ? name : char.ToUpperInvariant(stem[0]) + stem[1..];
    }

    /// <summary>Puts a tile on a colour slot - as its pattern or its texture - copying it into the character's tiles if it isn't there yet. One undo step.</summary>
    internal void SetTile(string slot, string name, ArtFile file, bool texture)
    {
        var character = Committed;
        if (!character.Wardrobe.Tiles.TryGetValue(name, out var existing) || !existing.SameContent(file))
            character = character with { Wardrobe = character.Wardrobe.WithTile(name, file) };
        var current = CharacterLooks.Resolve(ProjectLook(character)).FabricOf(slot) ?? new Fabric();
        var next = texture
            ? current with { Texture = new(TextureKind.Tile, current.Texture?.Strength, current.Texture?.Size, name) }
            : current with { Pattern = new(PatternKind.Tile, current.Pattern?.Colors ?? [], current.Pattern?.Size, current.Pattern?.Angle, Tile: name) };
        ShowMessage(null);
        Apply(EditResult<CharacterDefinition>.Success(StoreLookEdit(character, c => LookEditing.SetFabric(c, slot, next))));
    }

    /// <summary>
    /// Imports a tile file the user picked (an SVG, whose view box is one repeat, or a PNG)
    /// and puts it on <paramref name="slot"/>. False, with the reason in the status bar, if
    /// the file can't be drawn; anything in an SVG that isn't drawn is reported there too.
    /// </summary>
    public bool ImportTile(string slot, string fileName, ArtFile file, bool texture)
    {
        var extension = file.IsSvg ? ".svg" : ".png";
        if (file.IsSvg ? Stanley.Rendering.StickerSvg.Parse(file) is null : !Stanley.Rendering.ArtFiles.IsImage(file))
        {
            ShowMessage($"Couldn't read {fileName} as a {(file.IsSvg ? "SVG" : "PNG")} tile.");
            return false;
        }
        var stem = new string(Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        var baseName = (texture ? TextureTilePrefix : "") + (stem.Length == 0 ? "tile" : stem);
        var name = baseName + extension;
        for (var n = 2; Committed.Wardrobe.Tiles.TryGetValue(name, out var taken) && !taken.SameContent(file); n++)
            name = $"{baseName}-{n}{extension}";
        SetTile(slot, name, file, texture);
        if (file.IsSvg && Stanley.Rendering.StickerSvg.Parse(file) is { Report.Count: > 0 } art)
            ShowMessage($"{fileName}: {string.Join("; ", art.Report)}.");
        return true;
    }

    public static string ColorSlotLabel(string slot) => slot switch
    {
        CharacterDefinition.SkinSlot => "Skin",
        "accent" => "Accents",
        StickerSlots.EyesLeft => "Left eye",
        StickerSlots.EyesRight => "Right eye",
        StickerSlots.HairTop => "Top",
        StickerSlots.HairFringe => "Fringe",
        StickerSlots.HairSides => "Sides",
        StickerSlots.HairBack => "Back",
        StickerSlots.HairExtras => "Extras",
        _ when StickerSlots.IsStreakColorKey(slot) => "Streak",
        _ => slot.Length == 0 ? slot : char.ToUpperInvariant(slot[0]) + slot[1..]
    };

    internal static IReadOnlyList<(string Name, ColorValue Color)> Palette(string slot) => slot switch
    {
        CharacterDefinition.SkinSlot => Swatches.Select(s => (s.Name, s.Color)).ToList(),
        "hair" or "brows" => HairColors,
        _ when IsHairColorKey(slot) => HairColors,
        StickerSlots.Eyes or StickerSlots.EyesLeft or StickerSlots.EyesRight => EyeColors,
        _ => ClothColors
    };

    private static (string, ColorValue) C(string name, string hex) => (name, ColorValue.FromHex(hex));

    private static readonly IReadOnlyList<(string Name, ColorValue Color)> ClothColors =
    [
        C("White", "#f4f4f4"), C("Light grey", "#bdc3c7"), C("Dark grey", "#4d5656"), C("Black", "#1c1c1c"),
        C("Red", "#c0392b"), C("Orange", "#e67e22"), C("Yellow", "#f1c40f"), C("Green", "#27ae60"),
        C("Teal", "#16a085"), C("Sky", "#5dade2"), C("Blue", "#2e86c1"), C("Navy", "#1b2a49"),
        C("Purple", "#8e44ad"), C("Pink", "#e98fb0"), C("Brown", "#6e4b2a"), C("Beige", "#d8c3a5"),
    ];

    private static readonly IReadOnlyList<(string Name, ColorValue Color)> HairColors =
    [
        C("Black", "#1f1a17"), C("Dark brown", "#3b2a20"), C("Brown", "#5a3a22"), C("Auburn", "#8b3a1f"),
        C("Ginger", "#c25e20"), C("Blonde", "#e0c068"), C("Platinum", "#efe6c8"), C("Grey", "#a0a0a0"),
        C("White", "#f2f2f2"), C("Blue", "#3a6fd8"), C("Pink", "#e57fb0"), C("Green", "#4caf50"),
        // Vivid dyes.
        C("Purple", "#8e24aa"), C("Magenta", "#d6409f"), C("Red", "#d32f2f"), C("Orange", "#f57c00"), C("Teal", "#00897b"),
    ];

    /// <summary>Whether <paramref name="key"/> colours hair: the hair itself, one of its pieces or one streak (<see cref="ColorSlotEditor.IsHairKey"/>) - these are dyed, not patterned.</summary>
    internal static bool IsHairColorKey(string key) =>
        key == StickerSlots.Hair || StickerSlots.HairPieces.Contains(key) || StickerSlots.IsStreakColorKey(key);

    private static readonly IReadOnlyList<(string Name, ColorValue Color)> EyeColors =
    [
        C("Brown", "#5b3a1e"), C("Hazel", "#8e6b2e"), C("Green", "#3f7d4e"), C("Blue", "#3b7dd8"), C("Grey", "#7d8a96"), C("Dark", "#2b2016"),
    ];

    // ---------------------------------------------------------------- the selected sticker (Sticker tab)

    /// <summary>The worn stickers, for the Sticker tab's picker - in paint order.</summary>
    public IReadOnlyList<WornStickerItem> WornStickers =>
        CharacterLooks.Resolve(LookWorking).Stickers.Select(w => new WornStickerItem(w.Asset.Id, w.Asset.Sticker.Name, StickerSlots.Get(w.Slot).Label)).ToList();

    /// <summary>The sticker the Sticker tab works on (picked on the stage or in its list), or null.</summary>
    public StickerId? SelectedStickerId => _selectedSticker;

    public StickerAsset? SelectedSticker => _selectedSticker is { } id ? Working.Wardrobe.Find(id) : null;

    public bool HasSelectedSticker => SelectedSticker is not null;

    public bool SelectedStickerIsWorn => _selectedSticker is { } id && LookWorking.Stickers.Values.Any(v => v.Contains(id));

    /// <summary>The picker's selection - two-way.</summary>
    public WornStickerItem? SelectedWorn
    {
        get => WornStickers.FirstOrDefault(w => w.Id == _selectedSticker);
        // The list has no "none" to pick: a null is the list refreshing under it (a text print
        // renamed as it's typed over), not a choice - Done and the stage let go instead.
        set
        {
            if (value is not null)
                SelectSticker(value.Id);
        }
    }

    public void SelectSticker(StickerId? id)
    {
        if (id is { } some && Working.Wardrobe.Find(some) is null)
            id = null;
        if (_selectedSticker == id)
            return;
        _selectedSticker = id;
        ShowMessage(null); // a message about the last thing worked on is stale now
        RaiseSelectedStickerChanged();
    }

    public string SelectedStickerName => SelectedSticker?.Sticker.Name ?? "";

    // ---------------------------------------------------------------- text prints

    /// <summary>Whether the selected sticker is typed text (a text print): the Sticker tab shows its text box, and hides what only drawn art has.</summary>
    public bool SelectedIsTextPrint => SelectedSticker?.HasText == true;

    /// <summary>Drawn art only: "Hug the shape" and "Edit drawing..." - text is always pinned, and has no drawing to open.</summary>
    public bool HasDrawnArt => HasArtParts && !SelectedIsTextPrint;

    /// <summary>The selected text print's typed text - two-way, one undo step per commit (the view binds it to update on losing focus, like the name box).</summary>
    public string SelectedText
    {
        get => SelectedSticker is { } a && TextPrints.Part(a.Sticker) is { Text: { } t } ? t.Text : "";
        set
        {
            if (value == SelectedText)
                return;
            EditSelectedSticker(s => TextPrints.WithText(s, value));
        }
    }

    public bool SelectedTextBold
    {
        get => SelectedSticker is { } a && TextPrints.Part(a.Sticker) is { Text: { } t } ? t.Bold : true;
        set
        {
            if (value != SelectedTextBold)
                EditSelectedSticker(s => TextPrints.WithBold(s, value));
        }
    }

    /// <summary>A row of common emoji the Sticker tab offers to insert into the selected text print.</summary>
    public IReadOnlyList<string> CommonEmoji { get; } = ["\U0001F480", "❤️", "⭐", "⚡", "\U0001F525", "\U0001F600"];

    /// <summary>Gaps in the selected sticker's art, said inline: "No side view", "No wink, sad (shows neutral)" - or null.</summary>
    public string? SelectedStickerWarning
    {
        get
        {
            if (SelectedSticker is not { HasArt: true, HasText: false } asset)
                return null;
            var notes = new List<string>();
            var variant = asset.Sticker.Variants[0];
            if (asset.ArtFor(variant, ViewAngle.Profile) is null)
                notes.Add("No side view (shows the front)");
            if (asset.ArtFor(variant, ViewAngle.Front) is null)
                notes.Add("No front view (shows the side)");
            if (ExpressionPresets.MissingVariants(asset.Sticker) is { Count: > 0 } missing)
            {
                var total = ExpressionPresets.Vocabulary[asset.Sticker.Slot].Count;
                notes.Add(missing.Count >= total - 1 ? "No expressions (always looks the same)"
                    : missing.Count <= 3 ? $"No {string.Join(", ", missing)} (shows neutral)"
                    : $"{missing.Count} of {total} expressions missing (they show neutral)");
            }
            return notes.Count == 0 ? null : string.Join(". ", notes) + ".";
        }
    }

    public bool HasSelectedStickerWarning => SelectedStickerWarning is not null;

    public bool HasLength => SelectedSticker is { } s && StickerFitting.LengthPart(s.Sticker) is not null;
    public bool HasSleeves => SelectedSticker is { } s && StickerFitting.SleevePart(s.Sticker) is not null;
    public bool HasFit => SelectedSticker is { } s && StickerFitting.HasCovers(s.Sticker);

    /// <summary>How far down the garment reaches, 0-100.</summary>
    public double SelectedLength
    {
        get => SelectedSticker is { } s ? Math.Round(StickerFitting.Length(s.Sticker) * 100) : 0;
        set => EditSelectedSticker(s => StickerFitting.WithLength(s, value / 100));
    }

    /// <summary>How far down the arm the sleeves reach, 0-100.</summary>
    public double SelectedSleeves
    {
        get => SelectedSticker is { } s ? Math.Round(StickerFitting.Sleeves(s.Sticker) * 100) : 0;
        set => EditSelectedSticker(s => StickerFitting.WithSleeves(s, value / 100));
    }

    /// <summary>How loose it fits, 0 (skin tight) - 50 (baggy): tenths of a percent of the character's height.</summary>
    public double SelectedFit
    {
        get => SelectedSticker is { } s ? Math.Round(StickerFitting.Fit(s.Sticker) * 1000) : 0;
        set => EditSelectedSticker(s => StickerFitting.WithFit(s, value / 1000));
    }

    /// <summary>Edits the selected sticker: a live preview inside a slider drag, otherwise one undo step.</summary>
    private void EditSelectedSticker(Func<Sticker, Sticker> edit)
    {
        if (SelectedSticker is not { } asset)
            return;
        var edited = edit(asset.Sticker);
        if (edited == asset.Sticker)
            return;
        var result = EditResult<CharacterDefinition>.Success(LookEditing.UpdateSticker(Working, edited));
        if (IsGestureActive)
            UpdateGesture(result);
        else
            Apply(result);
    }

    private void EditSelected(Func<StickerId, Func<CharacterDefinition, CharacterDefinition>> edit)
    {
        if (_selectedSticker is { } id)
            ApplyLook(edit(id));
    }

    private void RaiseSelectedStickerChanged()
    {
        OnPropertyChanged(nameof(SelectedStickerId));
        OnPropertyChanged(nameof(SelectedSticker));
        OnPropertyChanged(nameof(HasSelectedSticker));
        OnPropertyChanged(nameof(SelectedStickerIsWorn));
        OnPropertyChanged(nameof(SelectedWorn));
        OnPropertyChanged(nameof(SelectedStickerName));
        OnPropertyChanged(nameof(SelectedStickerWarning));
        OnPropertyChanged(nameof(HasSelectedStickerWarning));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasLength));
        OnPropertyChanged(nameof(HasSleeves));
        OnPropertyChanged(nameof(HasFit));
        OnPropertyChanged(nameof(SelectedLength));
        OnPropertyChanged(nameof(SelectedSleeves));
        OnPropertyChanged(nameof(SelectedFit));
        OnPropertyChanged(nameof(SelectedIsTextPrint));
        OnPropertyChanged(nameof(HasDrawnArt));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectedTextBold));
        RaiseStylesChanged();
        RaiseArtChanged();
        TakeOffSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDuplicateSelected));
        RaiseHairColorChanged();
    }

    private void RaiseLookChanged()
    {
        if (_selectedSticker is { } id && Working.Wardrobe.Find(id) is null)
            _selectedSticker = null;
        if (_currentLook is { } look && !Working.Revisions.ContainsKey(look))
            _currentLook = null; // deleted (or undone away)
        _lookWorking = null;
        OnPropertyChanged(nameof(LookWorking));
        RaiseLooksChanged();
        OnPropertyChanged(nameof(IsEyesSplit));
        OnPropertyChanged(nameof(CanSplitEyes));
        OnPropertyChanged(nameof(HairGalleries));
        OnPropertyChanged(nameof(FaceGalleries));
        OnPropertyChanged(nameof(ClothesGalleries));
        OnPropertyChanged(nameof(AccessoryGalleries));
        if (RefreshColorEditors())
            OnPropertyChanged(nameof(ColorEditors));
        OnPropertyChanged(nameof(WornStickers));
        OnPropertyChanged(nameof(PreviewExpressionChoices));
        OnPropertyChanged(nameof(PreviewLeftEye));
        OnPropertyChanged(nameof(PreviewRightEye));
        OnPropertyChanged(nameof(ExpressionWarning));
        OnPropertyChanged(nameof(Hint));
        RaiseSelectedStickerChanged();
    }
}
