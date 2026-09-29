using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.StickerLibrary;

namespace Stanley.Editors;

/// <summary>
/// The Hair button and its flyout (docs: modular hair): one button on the Look tab, whose
/// flyout has a row of tabs - Hairstyles (open first), then Top, Fringe, Sides, Back, Extras
/// and Streaks. Hairstyles are presets of library pieces put on in one click; each piece tab is
/// an ordinary slot gallery. Two small notes come with it: what a hairstyle replaced (with a way
/// to add to the mix instead) and the offer to switch an old whole hairstyle for pieces.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    /// <summary>The Hairstyles tab's key; the other tabs are keyed by their slot.</summary>
    public const string HairstylesTabKey = "hairstyles";

    private string _hairTab = HairstylesTabKey;

    public IRelayCommand<HairstyleChoice> WearHairstyleCommand { get; private set; } = null!;
    public IRelayCommand<HairTab> ShowHairTabCommand { get; private set; } = null!;

    private void InitializeHair()
    {
        WearHairstyleCommand = new RelayCommand<HairstyleChoice>(choice =>
        {
            if (choice != null)
                WearHairstyle(choice);
        });
        ShowHairTabCommand = new RelayCommand<HairTab>(tab =>
        {
            if (tab != null)
                HairTabKey = tab.Key;
        });
        SwitchHairCommand = new RelayCommand(SwitchHair);
        KeepHairCommand = new RelayCommand(KeepHair);
        AddToMixCommand = new RelayCommand(AddToMix, () => HasHairReplaced);
        DismissHairReplacedCommand = new RelayCommand(DismissHairReplaced);
    }

    // ---------------------------------------------------------------- the button

    /// <summary>
    /// The Hair button's second line: the hairstyle worn exactly, "Own mix" when hair is worn
    /// but no hairstyle matches, a lone whole-hairstyle sticker's name, or "None" when bald.
    /// </summary>
    public string HairCurrent => HairName(LookWorking);

    public string HairTip => $"Hair: {HairCurrent}";

    /// <summary>What the Hair button says <paramref name="character"/> wears.</summary>
    public static string HairName(CharacterDefinition character)
    {
        var worn = HairEditing.WornHairdo(character);
        if (worn.Count == 0)
            return "None";
        if (Hairstyles.Matching(character) is { } style)
            return style.Name;
        if (worn.Count == 1 && character.Stickers.TryGetValue(StickerSlots.Hair, out var whole) && whole.Count == 1
            && character.Wardrobe.Find(worn[0]) is { } only)
            return only.Sticker.Name;
        return "Own mix";
    }

    // ---------------------------------------------------------------- the flyout's tabs

    /// <summary>Which tab the flyout shows - the last one used, kept until the editor closes. <see cref="HairstylesTabKey"/> or a piece slot's name.</summary>
    public string HairTabKey
    {
        get => _hairTab;
        set
        {
            if (!HairTabKeys.Contains(value) || _hairTab == value)
                return;
            _hairTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HairTabs));
            OnPropertyChanged(nameof(HairTabContent));
        }
    }

    private static readonly IReadOnlyList<string> HairTabKeys = [HairstylesTabKey, .. StickerSlots.HairPieces, StickerSlots.HairStreaks];

    /// <summary>Hairstyles · Top · Fringe · Sides · Back · Extras · Streaks.</summary>
    public IReadOnlyList<HairTab> HairTabs =>
        HairTabKeys.Select(key => new HairTab(key, key == HairstylesTabKey ? "Hairstyles" : StickerSlots.Get(key).Label, key == _hairTab)).ToList();

    /// <summary>
    /// What the flyout shows under its tabs: the <see cref="HairstyleGallery"/>, or a piece
    /// slot's ordinary <see cref="SlotGallery"/>. Only the tab in view is built - previews are
    /// expensive, so the other six cost nothing until they're opened.
    /// </summary>
    public object HairTabContent => _hairTab == HairstylesTabKey ? HairstylesGallery() : Gallery(_hairTab);

    /// <summary>
    /// Bald, then the hairstyles whose pieces are all in the library, then this character's
    /// own whole-hairstyle stickers (the old library ones and its own drawings) - each
    /// previewed close-up on this character, in its colours, as it would look wearing it.
    /// </summary>
    public HairstyleGallery HairstylesGallery()
    {
        var character = LookWorking;
        var pose = StagePose;
        var worn = HairEditing.WornHairdo(character);
        var matching = Hairstyles.Matching(character);
        var choices = new List<HairstyleChoice> { new("Bald", null, null, TakeHairOff(character), worn.Count == 0, pose) };
        foreach (var style in Hairstyles.Available)
        {
            var preview = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(style, character), replace: true);
            choices.Add(new(style.Name, style, null, preview, matching?.Name == style.Name, pose));
        }
        var own = character.Wardrobe.Stickers.Values.Where(a => a.Sticker.Slot == StickerSlots.Hair)
            .OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var asset in own)
        {
            // An old library hairstyle sits beside the preset of the same name: told apart by "(old)".
            var label = Hairstyles.ForLegacy(asset.Sticker) is null ? asset.Sticker.Name : $"{asset.Sticker.Name} (old)";
            var preview = HairEditing.WearHairdo(character, [(asset, null)], replace: true);
            choices.Add(new(label, null, asset, preview, worn.Count == 1 && worn[0] == asset.Id, pose));
        }
        return new(choices, WearHairstyleCommand, DrawYourOwnCommand, ImportArtCommand);
    }

    // ---------------------------------------------------------------- putting a hairstyle on

    /// <summary>A click on the Hairstyles tab: bald takes all the hair off, anything else replaces it. One undo step, in the look being edited; colours are never touched.</summary>
    private void WearHairstyle(HairstyleChoice choice)
    {
        if (choice.IsWorn)
            return;
        if (choice.IsBald)
        {
            ApplyLook(TakeHairOff);
            DeselectIfTakenOff();
            return;
        }
        if (choice.Style is { } style)
            WearHairstyle(style.Name, Hairstyles.PiecesFor(style, LookWorking));
        else
            WearHairstyle(choice.Own!.Sticker.Name, [(choice.Own, null)]);
    }

    /// <summary>
    /// Replaces the hair with a hairdo made of <paramref name="pieces"/>, as one undo step. If
    /// that throws away hair that isn't a hairstyle - your own mix - a note says so and offers
    /// to add the pieces to the mix instead.
    /// </summary>
    public void WearHairstyle(string name, IReadOnlyList<(StickerAsset Asset, string? Style)> pieces)
    {
        var ownMix = Hairstyles.IsOwnMix(LookWorking);
        ApplyLook(c => HairEditing.WearHairdo(c, pieces, replace: true));
        DeselectIfTakenOff();
        SetHairReplaced(ownMix ? new($"Replaced your hair with {name}.", pieces, Working, _currentLook) : null);
    }

    /// <summary>Every slot that holds a hairdo emptied; streaks stay on.</summary>
    private static CharacterDefinition TakeHairOff(CharacterDefinition character)
    {
        foreach (var slot in StickerSlots.Hairdo)
        {
            if (character.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0)
                character = LookEditing.ClearSlot(character, slot);
        }
        return character;
    }

    /// <summary>Lets go of the selected sticker if the last edit took it off.</summary>
    private void DeselectIfTakenOff()
    {
        if (_selectedSticker is not null && !SelectedStickerIsWorn)
            SelectSticker(null);
    }

    // ---------------------------------------------------------------- "Replaced your hair with Bob."

    /// <summary>What a hairstyle click replaced, kept while its note is up: the pieces to add instead, and the state and look the click left (any other edit, or another look, makes the note stale).</summary>
    private sealed record HairReplaced(string Text, IReadOnlyList<(StickerAsset Asset, string? Style)> Pieces, CharacterDefinition After, CharacterRevisionId? Look);

    private HairReplaced? _hairReplaced;

    public IRelayCommand AddToMixCommand { get; private set; } = null!;
    public IRelayCommand DismissHairReplacedCommand { get; private set; } = null!;

    /// <summary>"Replaced your hair with Bob." - shown after a hairstyle click threw away an own mix; null when there's nothing to say.</summary>
    public string? HairReplacedText => _hairReplaced?.Text;

    public bool HasHairReplaced => _hairReplaced is not null;

    /// <summary>
    /// "Add to my mix instead": undoes the replacing click and puts the pieces on over the
    /// mix, so the two are still one undo step in the end.
    /// </summary>
    private void AddToMix()
    {
        if (_hairReplaced is not { } note)
            return;
        SetHairReplaced(null);
        if (ReferenceEquals(Committed, note.After) && History.CanUndo)
            History.Undo();
        ApplyLook(c => HairEditing.WearHairdo(c, note.Pieces, replace: false));
    }

    /// <summary>Takes the note down without doing anything: the bar's own close button, and leaving the editor.</summary>
    public void DismissHairReplaced() => SetHairReplaced(null);

    private void SetHairReplaced(HairReplaced? note)
    {
        if (_hairReplaced == note)
            return;
        _hairReplaced = note;
        OnPropertyChanged(nameof(HairReplacedText));
        OnPropertyChanged(nameof(HasHairReplaced));
        AddToMixCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- "This is the old Bob - switch to the new Bob made of pieces?"

    private HairUpgradeMemory? _ownHairUpgrades;

    /// <summary>Who has kept their old hairstyle: the app's settings through the library, else this editor's memory alone.</summary>
    private HairUpgradeMemory HairUpgrades => Library?.HairUpgrades ?? (_ownHairUpgrades ??= new());

    public IRelayCommand SwitchHairCommand { get; private set; } = null!;
    public IRelayCommand KeepHairCommand { get; private set; } = null!;

    /// <summary>The old whole-hairstyle sticker the current look wears that a hairstyle now replaces, with that hairstyle - or null when there's none, or the user chose to keep it.</summary>
    private (StickerAsset Legacy, Hairstyle Preset)? LegacyHair()
    {
        if (HairUpgrades.IsDeclined(CharacterId) || !LookWorking.Stickers.TryGetValue(StickerSlots.Hair, out var worn))
            return null;
        foreach (var id in worn)
        {
            if (LookWorking.Wardrobe.Find(id) is { } asset && Hairstyles.ForLegacy(asset.Sticker) is { } preset)
                return (asset, preset);
        }
        return null;
    }

    /// <summary>Whether the upgrade bar is up: the current look wears an old library hairstyle that pieces now replace.</summary>
    public bool HasHairUpgrade => LegacyHair() is not null;

    public string? HairUpgradeText =>
        LegacyHair() is { } found ? $"This is the old {found.Legacy.Sticker.Name} - switch to the new {found.Preset.Name} made of pieces?" : null;

    /// <summary>"Switch": the old sticker becomes the hairstyle's pieces in the default look and every named look, colours kept. One undo step.</summary>
    private void SwitchHair()
    {
        if (LegacyHair() is not { } found)
            return;
        var pieces = Hairstyles.PiecesFor(found.Preset, Committed);
        Apply(EditResult<CharacterDefinition>.Success(HairEditing.ReplaceLegacy(Committed, found.Legacy.Id, pieces)));
        RaiseHairChanged();
    }

    /// <summary>"Keep": this character keeps its old hairstyle and isn't asked again - remembered in the user's settings, not in the comic.</summary>
    private void KeepHair()
    {
        HairUpgrades.Decline(CharacterId);
        RaiseHairChanged();
    }

    /// <summary>Called whenever the character changes: the button's line and tab move on, a note about an edit that isn't the latest goes away, and the upgrade bar comes or goes.</summary>
    private void RaiseHairChanged()
    {
        if (_hairReplaced is { } note && (!ReferenceEquals(Working, note.After) || _currentLook != note.Look))
            SetHairReplaced(null);
        OnPropertyChanged(nameof(HairCurrent));
        OnPropertyChanged(nameof(HairTip));
        OnPropertyChanged(nameof(HairTabContent));
        OnPropertyChanged(nameof(HasHairUpgrade));
        OnPropertyChanged(nameof(HairUpgradeText));
    }
}
