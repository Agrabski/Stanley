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
public sealed record StickerChoice(string Label, string Slot, CharacterDefinition Preview, StickerAsset? Asset, LibrarySticker? Library, bool IsWorn)
{
    public bool IsNone => Asset is null && Library is null;

    public string Tip => IsNone ? "Nothing in this slot" : Library is not null ? $"{Label} - from the starter library" : IsWorn ? $"{Label} - wearing it" : Label;
}

/// <summary>A slot's gallery on the Look tab: its label, what's worn now, and everything it can wear.</summary>
/// <param name="Wear">Wears a choice (carried here so the gallery's popup can reach it).</param>
public sealed record SlotGallery(StickerSlotInfo Info, string Current, IReadOnlyList<StickerChoice> Choices, System.Windows.Input.ICommand Wear)
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

/// <summary>One colour slot the character's clothes use, with its colour and the swatches to pick from.</summary>
public sealed record ColorSlotEditor(string Slot, string Label, ColorValue Color, IReadOnlyList<ColorSwatchChoice> Swatches, System.Windows.Input.ICommand SetColor)
{
    public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(Color)));
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
                Apply(EditResult<CharacterDefinition>.Success(LookEditing.SetColor(Committed, choice.Slot, choice.Color)));
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
    }

    // ---------------------------------------------------------------- galleries

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
        var choices = new List<StickerChoice> { new("None", slot, LookEditing.ClearSlot(character, slot), null, null, worn.Count == 0) };
        var owned = character.Wardrobe.Stickers.Values.Where(a => a.Sticker.Slot == slot).OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var asset in owned)
            choices.Add(new StickerChoice(asset.Sticker.Name, slot, LookEditing.Wear(character, asset), asset, null, worn.Contains(asset.Id)));
        var ownedSources = owned.Select(a => a.Sticker.Source).OfType<string>().ToHashSet();
        foreach (var item in Stanley.StickerLibrary.StickerLibrary.ForSlot(slot).Where(l => !ownedSources.Contains(Stanley.StickerLibrary.StickerLibrary.SourcePrefix + l.Key)))
            choices.Add(new StickerChoice(item.Name, slot, LookEditing.Wear(character, item.Preview), null, item, false));
        var current = worn.Count == 0 ? "None" : string.Join(", ", worn.Select(id => character.Wardrobe.Find(id)?.Sticker.Name ?? "?"));
        return new SlotGallery(info, current, choices, WearCommand);
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

    // ---------------------------------------------------------------- colours

    /// <summary>A swatch dropdown for each colour slot the worn stickers use (skin first).</summary>
    public IReadOnlyList<ColorSlotEditor> ColorEditors
    {
        get
        {
            var character = Working;
            var look = CharacterLooks.Resolve(character);
            return LookEditing.ColorSlotsInUse(character)
                .Select(slot => new ColorSlotEditor(slot, ColorSlotLabel(slot), look.Color(slot, slot == CharacterDefinition.SkinSlot ? character.Skin : ColorValue.FromHex("#9a9a9a")),
                    Palette(slot).Select(p => new ColorSwatchChoice(slot, p.Name, p.Color)).ToList(), SetColorCommand))
                .ToList();
        }
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
        RaiseSelectedStickerChanged();
    }

    public string SelectedStickerName => SelectedSticker?.Sticker.Name ?? "";

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
        OnPropertyChanged(nameof(HasLength));
        OnPropertyChanged(nameof(HasSleeves));
        OnPropertyChanged(nameof(HasFit));
        OnPropertyChanged(nameof(SelectedLength));
        OnPropertyChanged(nameof(SelectedSleeves));
        OnPropertyChanged(nameof(SelectedFit));
        TakeOffSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLookChanged()
    {
        if (_selectedSticker is { } id && Working.Wardrobe.Find(id) is null)
            _selectedSticker = null;
        OnPropertyChanged(nameof(ClothesGalleries));
        OnPropertyChanged(nameof(AccessoryGalleries));
        OnPropertyChanged(nameof(ColorEditors));
        OnPropertyChanged(nameof(WornStickers));
        RaiseSelectedStickerChanged();
    }
}
