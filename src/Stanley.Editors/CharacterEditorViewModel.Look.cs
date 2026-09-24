using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.StickerLibrary;

namespace Stanley.Editors;

/// <summary>One thing a slot gallery offers: nothing, a sticker from the wardrobe, or one from the library - previewed on this character.</summary>
/// <param name="Pose">The stage's preview pose (its view and expression), so a face gallery shows the expression being previewed.</param>
public sealed record StickerChoice(string Label, string Slot, CharacterDefinition Preview, StickerAsset? Asset, LibrarySticker? Library, bool IsWorn,
    Stanley.ProjectModel.Poses.PoseData? Pose = null)
{
    public bool IsNone => Asset is null && Library is null;

    /// <summary>Worn on the head (hair, face, hats, glasses): previewed as a close-up.</summary>
    public bool Closeup => StickerSlots.Get(Slot).Region == BodyRegion.Head;

    public string Tip => IsNone ? "Nothing in this slot" : Library is not null ? $"{Label} - from the starter library" : IsWorn ? $"{Label} - wearing it" : Label;
}

/// <summary>A slot's gallery on the Look tab: its label, what's worn now, and everything it can wear.</summary>
/// <param name="Wear">Wears a choice (carried here so the gallery's popup can reach it).</param>
/// <param name="Draw">Draws a sticker for the slot in the user's SVG editor (the slot's name is the parameter).</param>
/// <param name="Import">Imports an SVG or PNG as a sticker for the slot.</param>
public sealed record SlotGallery(StickerSlotInfo Info, string Current, IReadOnlyList<StickerChoice> Choices, System.Windows.Input.ICommand Wear,
    System.Windows.Input.ICommand? Draw = null, System.Windows.Input.ICommand? Import = null)
{
    public string Label => Info.Label;

    public string Tip => $"{Info.Label}: {Current}";
}

/// <summary>A colour on offer for one colour slot.</summary>
public sealed record ColorSwatchChoice(string Slot, string Name, ColorValue Color)
{
    public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorHex(Color)));

    internal static string ColorHex(ColorValue c) => c.Hex.Length == 9 ? "#" + c.Hex[7..] + c.Hex[1..7] : c.Hex;
}

/// <summary>
/// A pattern or texture on offer for a colour slot (or none), previewed in the slot's
/// colour: a generated one, a tile (<paramref name="Tile"/>, from the library or the
/// character's own), or <paramref name="IsCustom"/> - "Custom...", which asks for a file.
/// </summary>
public sealed record FabricChoice(string Label, ColorValue Ground, Fabric Preview, PatternKind? Pattern, TextureKind? Texture, bool IsCurrent,
    string? Tile = null, ArtFile? TileFile = null, bool IsCustom = false)
{
    /// <summary>The tile the preview draws, by name.</summary>
    public IReadOnlyDictionary<string, ArtFile>? Tiles => Tile is { } name && TileFile is { } file ? new Dictionary<string, ArtFile> { [name] = file } : null;
}

/// <summary>Asks the view for a tile file to import for a colour slot - as its pattern, or its texture.</summary>
public sealed record TileImportRequest(string Slot, bool Texture);

/// <summary>
/// One colour slot the character's clothes use, on the Look tab: its colour, and - for
/// clothes - its fabric (pattern, texture, their size, angle and strength). Kept alive
/// across edits and refreshed in place, so its dropdown stays open while you drag its
/// sliders; each drag is one undo step.
/// </summary>
public sealed class ColorSlotEditor : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IDragGesture
{
    private readonly CharacterEditorViewModel _owner;
    private ColorValue _color;
    private Fabric? _fabric;

    internal ColorSlotEditor(CharacterEditorViewModel owner, string slot, IReadOnlyList<ColorSwatchChoice> swatches)
    {
        _owner = owner;
        Slot = slot;
        Label = CharacterEditorViewModel.ColorSlotLabel(slot);
        Swatches = swatches;
        SetColor = new RelayCommand<ColorSwatchChoice>(c => { if (c != null) owner.SetSlotColor(Slot, c.Color); });
        SetPatternColor = new RelayCommand<ColorSwatchChoice>(c =>
        {
            if (c != null)
                owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Colors = [c.Color, .. p.Colors.Skip(1)] } : null });
        });
        SetPattern = new RelayCommand<FabricChoice>(c =>
        {
            if (c is { IsCustom: true })
                owner.RequestTileImport(new TileImportRequest(Slot, Texture: false));
            else if (c is { Tile: { } tile, TileFile: { } file })
                owner.SetTile(Slot, tile, file, texture: false);
            else if (c != null)
                owner.EditFabric(Slot, f => f with { Pattern = c.Pattern is { } kind ? new PatternFill(kind, f.Pattern?.Colors is { Count: > 0 } colors ? colors : [], f.Pattern?.Size, f.Pattern?.Angle) : null });
        });
        SetTexture = new RelayCommand<FabricChoice>(c =>
        {
            if (c is { IsCustom: true })
                owner.RequestTileImport(new TileImportRequest(Slot, Texture: true));
            else if (c is { Tile: { } tile, TileFile: { } file })
                owner.SetTile(Slot, tile, file, texture: true);
            else if (c != null)
                owner.EditFabric(Slot, f => f with { Texture = c.Texture is { } kind ? new TextureFill(kind, f.Texture?.Strength, f.Texture?.Size) : null });
        });
    }

    public string Slot { get; }

    public string Label { get; }

    /// <summary>Clothes get fabrics; skin and eyes are just colours.</summary>
    public bool CanHaveFabric => Slot is not (CharacterDefinition.SkinSlot or "eyes");

    public IReadOnlyList<ColorSwatchChoice> Swatches { get; }

    /// <summary>Colours for the pattern's own colour.</summary>
    public IReadOnlyList<ColorSwatchChoice> PatternSwatches => Swatches;

    public System.Windows.Input.ICommand SetColor { get; }
    public System.Windows.Input.ICommand SetPatternColor { get; }
    public System.Windows.Input.ICommand SetPattern { get; }
    public System.Windows.Input.ICommand SetTexture { get; }

    public ColorValue Color => _color;

    public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(_color)));

    /// <summary>The slot's fabric as worn (the character's own, or the garment's default), or null for plain.</summary>
    public Fabric? Fabric => _fabric;

    public bool HasPattern => _fabric?.Pattern is not null;

    public bool HasTexture => _fabric?.Texture is not null;

    /// <summary>The character's tiles, for drawing this slot's swatch.</summary>
    public IReadOnlyDictionary<string, ArtFile> Tiles => _owner.Working.Wardrobe.Tiles;

    /// <summary>The generated patterns, then the library's tiles, then the character's own, then "Custom...".</summary>
    public IReadOnlyList<FabricChoice> PatternChoices
    {
        get
        {
            var colors = _fabric?.Pattern?.Colors ?? [];
            var angle = _fabric?.Pattern?.Angle;
            var current = _fabric?.Pattern;
            var choices = new (string Label, PatternKind? Kind)[] { ("None", null), ("Stripes", PatternKind.Stripes), ("Pinstripes", PatternKind.Pinstripes), ("Checks", PatternKind.Checks),
                    ("Plaid", PatternKind.Plaid), ("Dots", PatternKind.Dots), ("Chevron", PatternKind.Chevron) }
                .Select(p => new FabricChoice(p.Label, _color, new Fabric(p.Kind is { } kind ? new PatternFill(kind, colors, Angle: angle) : null),
                    p.Kind, null, current?.Kind == p.Kind && current?.Kind != PatternKind.Tile))
                .ToList();
            foreach (var (name, file) in CharacterEditorViewModel.TileChoices(_owner.Working, texture: false))
                choices.Add(new FabricChoice(CharacterEditorViewModel.TileLabel(name), _color, new Fabric(new PatternFill(PatternKind.Tile, colors, Angle: angle, Tile: name)),
                    PatternKind.Tile, null, current is { Kind: PatternKind.Tile } && current.Tile == name, name, file));
            choices.Add(new FabricChoice("Custom...", _color, new Fabric(), null, null, false, IsCustom: true));
            return choices;
        }
    }

    /// <summary>The generated textures, then the character's own texture tiles, then "Custom..." (a greyscale PNG or SVG).</summary>
    public IReadOnlyList<FabricChoice> TextureChoices
    {
        get
        {
            var current = _fabric?.Texture;
            var choices = new (string Label, TextureKind? Kind)[] { ("None", null), ("Denim", TextureKind.Denim), ("Knit", TextureKind.Knit), ("Corduroy", TextureKind.Corduroy),
                    ("Wool", TextureKind.Wool), ("Leather", TextureKind.Leather), ("Canvas", TextureKind.Canvas), ("Felt", TextureKind.Felt) }
                .Select(t => new FabricChoice(t.Label, _color, new Fabric(Texture: t.Kind is { } kind ? new TextureFill(kind, 0.9) : null), null, t.Kind,
                    current?.Kind == t.Kind && current?.Kind != TextureKind.Tile))
                .ToList();
            foreach (var (name, file) in CharacterEditorViewModel.TileChoices(_owner.Working, texture: true))
                choices.Add(new FabricChoice(CharacterEditorViewModel.TileLabel(name), _color, new Fabric(Texture: new TextureFill(TextureKind.Tile, 0.9, Tile: name)),
                    null, TextureKind.Tile, current is { Kind: TextureKind.Tile } && current.Tile == name, name, file));
            choices.Add(new FabricChoice("Custom...", _color, new Fabric(), null, null, false, IsCustom: true));
            return choices;
        }
    }

    /// <summary>Pattern size: one repeat as a percentage of the character's height.</summary>
    public double PatternSize
    {
        get => Math.Round((_fabric?.Pattern?.Size ?? PatternFill.DefaultSize) * 100, 1);
        set => _owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Size = Math.Round(Math.Clamp(value, 1, 25) / 100, 4) } : null });
    }

    public double PatternAngle
    {
        get => Math.Round(_fabric?.Pattern?.Angle ?? 0);
        set => _owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Angle = Math.Round(value) } : null });
    }

    /// <summary>Texture strength, 0-100.</summary>
    public double TextureStrength
    {
        get => Math.Round((_fabric?.Texture?.Strength ?? TextureFill.DefaultStrength) * 100);
        set => _owner.EditFabric(Slot, f => f with { Texture = f.Texture is { } t ? t with { Strength = Math.Round(Math.Clamp(value, 0, 100) / 100, 3) } : null });
    }

    public void BeginDrag() => _owner.BeginSliderDrag();

    public void EndDrag() => _owner.EndSliderDrag();

    private IReadOnlyDictionary<string, ArtFile>? _tiles;

    internal void Refresh(ColorValue color, Fabric? fabric)
    {
        var colorChanged = color != _color;
        var fabricChanged = !Equals(fabric, _fabric);
        var tilesChanged = !ReferenceEquals(_tiles, _owner.Working.Wardrobe.Tiles);
        _tiles = _owner.Working.Wardrobe.Tiles;
        _color = color;
        _fabric = fabric;
        if (colorChanged)
        {
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(Brush));
        }
        if (colorChanged || fabricChanged || tilesChanged)
        {
            OnPropertyChanged(nameof(Tiles));
            OnPropertyChanged(nameof(Fabric));
            OnPropertyChanged(nameof(HasPattern));
            OnPropertyChanged(nameof(HasTexture));
            OnPropertyChanged(nameof(PatternChoices));
            OnPropertyChanged(nameof(TextureChoices));
            OnPropertyChanged(nameof(PatternSize));
            OnPropertyChanged(nameof(PatternAngle));
            OnPropertyChanged(nameof(TextureStrength));
        }
    }
}

/// <summary>A worn sticker, for the Sticker tab's picker.</summary>
public sealed record WornStickerItem(StickerId Id, string Name, string SlotLabel)
{
    public override string ToString() => $"{Name} ({SlotLabel})";
}

public sealed partial class CharacterEditorViewModel
{
    private StickerId? _selectedSticker;

    public IRelayCommand<StickerChoice> WearCommand { get; private set; } = null!;
    public IRelayCommand<ColorSwatchChoice> SetColorCommand { get; private set; } = null!;
    public IRelayCommand TakeOffSelectedCommand { get; private set; } = null!;
    public IRelayCommand RemoveSelectedCommand { get; private set; } = null!;
    public IRelayCommand MoveSelectedUpCommand { get; private set; } = null!;
    public IRelayCommand MoveSelectedDownCommand { get; private set; } = null!;
    public IRelayCommand DeselectStickerCommand { get; private set; } = null!;

    private void InitializeLook()
    {
        WearCommand = new RelayCommand<StickerChoice>(choice =>
        {
            if (choice != null)
                Wear(choice);
        });
        SetColorCommand = new RelayCommand<ColorSwatchChoice>(choice =>
        {
            if (choice != null)
                SetSlotColor(choice.Slot, choice.Color);
        });
        TakeOffSelectedCommand = new RelayCommand(() => EditSelected(id => LookEditing.TakeOff(Committed, id)), () => SelectedStickerIsWorn);
        RemoveSelectedCommand = new RelayCommand(() =>
        {
            if (_selectedSticker is { } id)
            {
                SelectSticker(null);
                Apply(EditResult<CharacterDefinition>.Success(LookEditing.RemoveFromWardrobe(Committed, id)));
            }
        }, () => HasSelectedSticker);
        MoveSelectedUpCommand = new RelayCommand(() => EditSelected(id => LookEditing.MoveInStack(Committed, id, +1)), () => SelectedStickerIsWorn);
        MoveSelectedDownCommand = new RelayCommand(() => EditSelected(id => LookEditing.MoveInStack(Committed, id, -1)), () => SelectedStickerIsWorn);
        DeselectStickerCommand = new RelayCommand(() => SelectSticker(null));
        PreviewExpressionCommand = new RelayCommand<ExpressionPresetChoice>(choice =>
        {
            if (choice != null)
                PreviewExpression = choice.Preset;
        });
    }

    // ---------------------------------------------------------------- galleries

    /// <summary>The hair gallery.</summary>
    public IReadOnlyList<SlotGallery> HairGalleries => Galleries(StickerSlots.Hair);

    /// <summary>The face's galleries (Eyes, Brows, Mouth, Nose).</summary>
    public IReadOnlyList<SlotGallery> FaceGalleries => Galleries(StickerSlots.Eyes, StickerSlots.Brows, StickerSlots.Mouth, StickerSlots.Nose);

    /// <summary>The clothes slots' galleries (Top, Outer, Bottom, Shoes).</summary>
    public IReadOnlyList<SlotGallery> ClothesGalleries => Galleries(StickerSlots.Top, StickerSlots.Outer, StickerSlots.Bottom, StickerSlots.Shoes);

    /// <summary>Hats, glasses and everything else.</summary>
    public IReadOnlyList<SlotGallery> AccessoryGalleries => Galleries(StickerSlots.Headwear, StickerSlots.Glasses, StickerSlots.Accessory);

    private IReadOnlyList<SlotGallery> Galleries(params string[] slots) => slots.Select(Gallery).ToList();

    /// <summary>
    /// A slot's choices: None, then this character's wardrobe for the slot, then the
    /// library's stickers it doesn't already have a copy of - each previewed on this
    /// character wearing it.
    /// </summary>
    public SlotGallery Gallery(string slot)
    {
        var info = StickerSlots.Get(slot);
        var character = Working;
        var worn = character.Stickers.TryGetValue(slot, out var ids) ? ids : [];
        var pose = StagePose;
        var choices = new List<StickerChoice> { new("None", slot, LookEditing.ClearSlot(character, slot), null, null, worn.Count == 0, pose) };
        var owned = character.Wardrobe.Stickers.Values.Where(a => a.Sticker.Slot == slot).OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var asset in owned)
            choices.Add(new StickerChoice(asset.Sticker.Name, slot, LookEditing.Wear(character, asset), asset, null, worn.Contains(asset.Id), pose));
        var ownedSources = owned.Select(a => a.Sticker.Source).OfType<string>().ToHashSet();
        foreach (var item in Stanley.StickerLibrary.StickerLibrary.ForSlot(slot).Where(l => !ownedSources.Contains(Stanley.StickerLibrary.StickerLibrary.SourcePrefix + l.Key)))
            choices.Add(new StickerChoice(item.Name, slot, LookEditing.Wear(character, item.Preview), null, item, false, pose));
        var current = worn.Count == 0 ? "None" : string.Join(", ", worn.Select(id => character.Wardrobe.Find(id)?.Sticker.Name ?? "?"));
        return new SlotGallery(info, current, choices, WearCommand, DrawYourOwnCommand, ImportArtCommand);
    }

    private void Wear(StickerChoice choice)
    {
        var character = Committed;
        CharacterDefinition next;
        if (choice.IsNone)
            next = LookEditing.ClearSlot(character, choice.Slot);
        else if (choice.Asset is { } asset)
            next = choice.IsWorn && StickerSlots.Get(choice.Slot).Stacks ? LookEditing.TakeOff(character, asset.Id) : LookEditing.Wear(character, asset);
        else
            next = LookEditing.Wear(character, choice.Library!.Instantiate());
        Apply(EditResult<CharacterDefinition>.Success(next));
    }

    // ---------------------------------------------------------------- preview expression

    private ExpressionPresetDefinition _previewExpression = ExpressionPresets.Get(ExpressionPreset.Neutral);

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
            OnPropertyChanged();
            RaisePreviewPoseChanged();
        }
    }

    /// <summary>What the stage shows the character in: the previewed view and expression (null at rest, front view and neutral).</summary>
    public Stanley.ProjectModel.Poses.PoseData? StagePose
    {
        get
        {
            if (_previewExpression.Preset == ExpressionPreset.Neutral)
                return null;
            if (_stagePose is not { } pose || pose.ViewAngle != PreviewAngle || ExpressionPresets.Of(pose) != _previewExpression)
                _stagePose = ExpressionPresets.Apply(new Stanley.ProjectModel.Poses.PoseData(PreviewAngle, [], new SortedDictionary<string, string>()), _previewExpression);
            return _stagePose;
        }
    }

    private Stanley.ProjectModel.Poses.PoseData? _stagePose;

    /// <summary>Every expression, as a close-up of this character.</summary>
    public IReadOnlyList<ExpressionPresetChoice> PreviewExpressionChoices
    {
        get
        {
            var rest = new Stanley.ProjectModel.Poses.PoseData(PreviewAngle, [], new SortedDictionary<string, string>());
            return ExpressionPresets.All.Select(p => new ExpressionPresetChoice(p, Working, ExpressionPresets.Apply(rest, p), p == _previewExpression)).ToList();
        }
    }

    /// <summary>What the previewed expression can't show on this face - "Dots has no wink" - or null.</summary>
    public string? ExpressionWarning
    {
        get
        {
            var missing = CharacterLooks.Resolve(Working).Stickers
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
        OnPropertyChanged(nameof(ExpressionWarning));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HairGalleries));
        OnPropertyChanged(nameof(FaceGalleries));
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
        var character = Working;
        var look = CharacterLooks.Resolve(character);
        var slots = LookEditing.ColorSlotsInUse(character);
        var changed = !slots.SequenceEqual(_colorEditors.Select(e => e.Slot));
        if (changed)
            _colorEditors = slots.Select(slot => new ColorSlotEditor(this, slot, Palette(slot).Select(p => new ColorSwatchChoice(slot, p.Name, p.Color)).ToList())).ToList();
        foreach (var editor in _colorEditors)
            editor.Refresh(look.Color(editor.Slot, editor.Slot == CharacterDefinition.SkinSlot ? character.Skin : ColorValue.FromHex("#9a9a9a")), look.FabricOf(editor.Slot));
        return changed;
    }

    internal void SetSlotColor(string slot, ColorValue color) =>
        Apply(EditResult<CharacterDefinition>.Success(LookEditing.SetColor(Committed, slot, color)));

    /// <summary>Changes a colour slot's fabric, starting from what it wears now: a live preview inside a slider drag, otherwise one undo step.</summary>
    internal void EditFabric(string slot, Func<Fabric, Fabric> edit)
    {
        var baseline = IsGestureActive ? Working : Committed;
        var current = CharacterLooks.Resolve(baseline).FabricOf(slot) ?? new Fabric();
        var next = edit(current);
        if (Equals(next, current))
            return;
        var result = EditResult<CharacterDefinition>.Success(LookEditing.SetFabric(baseline, slot, next));
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
        var current = CharacterLooks.Resolve(character).FabricOf(slot) ?? new Fabric();
        var next = texture
            ? current with { Texture = new TextureFill(TextureKind.Tile, current.Texture?.Strength, current.Texture?.Size, name) }
            : current with { Pattern = new PatternFill(PatternKind.Tile, current.Pattern?.Colors ?? [], current.Pattern?.Size, current.Pattern?.Angle, Tile: name) };
        ShowMessage(null);
        Apply(EditResult<CharacterDefinition>.Success(LookEditing.SetFabric(character, slot, next)));
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
        _ => slot.Length == 0 ? slot : char.ToUpperInvariant(slot[0]) + slot[1..]
    };

    private static IReadOnlyList<(string Name, ColorValue Color)> Palette(string slot) => slot switch
    {
        CharacterDefinition.SkinSlot => Swatches.Select(s => (s.Name, s.Color)).ToList(),
        "hair" or "brows" => HairColors,
        "eyes" => EyeColors,
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
    ];

    private static readonly IReadOnlyList<(string Name, ColorValue Color)> EyeColors =
    [
        C("Brown", "#5b3a1e"), C("Hazel", "#8e6b2e"), C("Green", "#3f7d4e"), C("Blue", "#3b7dd8"), C("Grey", "#7d8a96"), C("Dark", "#2b2016"),
    ];

    // ---------------------------------------------------------------- the selected sticker (Sticker tab)

    /// <summary>The worn stickers, for the Sticker tab's picker - in paint order.</summary>
    public IReadOnlyList<WornStickerItem> WornStickers =>
        CharacterLooks.Resolve(Working).Stickers.Select(w => new WornStickerItem(w.Asset.Id, w.Asset.Sticker.Name, StickerSlots.Get(w.Slot).Label)).ToList();

    /// <summary>The sticker the Sticker tab works on (picked on the stage or in its list), or null.</summary>
    public StickerId? SelectedStickerId => _selectedSticker;

    public StickerAsset? SelectedSticker => _selectedSticker is { } id ? Working.Wardrobe.Find(id) : null;

    public bool HasSelectedSticker => SelectedSticker is not null;

    public bool SelectedStickerIsWorn => _selectedSticker is { } id && Working.Stickers.Values.Any(v => v.Contains(id));

    /// <summary>The picker's selection - two-way.</summary>
    public WornStickerItem? SelectedWorn
    {
        get => WornStickers.FirstOrDefault(w => w.Id == _selectedSticker);
        set => SelectSticker(value?.Id);
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

    /// <summary>Gaps in the selected sticker's art, said inline: "No side view", "No wink, sad (shows neutral)" - or null.</summary>
    public string? SelectedStickerWarning
    {
        get
        {
            if (SelectedSticker is not { HasArt: true } asset)
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

    private void EditSelected(Func<StickerId, CharacterDefinition> edit)
    {
        if (_selectedSticker is { } id)
            Apply(EditResult<CharacterDefinition>.Success(edit(id)));
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
        RaiseArtChanged();
        TakeOffSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLookChanged()
    {
        if (_selectedSticker is { } id && Working.Wardrobe.Find(id) is null)
            _selectedSticker = null;
        OnPropertyChanged(nameof(HairGalleries));
        OnPropertyChanged(nameof(FaceGalleries));
        OnPropertyChanged(nameof(ClothesGalleries));
        OnPropertyChanged(nameof(AccessoryGalleries));
        if (RefreshColorEditors())
            OnPropertyChanged(nameof(ColorEditors));
        OnPropertyChanged(nameof(WornStickers));
        OnPropertyChanged(nameof(PreviewExpressionChoices));
        OnPropertyChanged(nameof(ExpressionWarning));
        OnPropertyChanged(nameof(Hint));
        RaiseSelectedStickerChanged();
    }
}
