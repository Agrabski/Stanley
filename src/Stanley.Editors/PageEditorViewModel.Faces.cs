using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// The Character tab's Face dropdown beyond its presets: mixing a face part by part (the
/// character's own drawn expressions included), drawing a new expression in the user's SVG
/// editor, and saving a face on the character to use again in any panel.
/// </summary>
public sealed partial class PageEditorViewModel
{
    private string _newFaceName = "";
    private string? _notice;

    public IRelayCommand<ExpressionVariantChoice> SetExpressionVariantCommand { get; private set; } = null!;

    /// <summary>Saves the selected character's face under <see cref="NewFaceName"/>.</summary>
    public IRelayCommand SaveFaceCommand { get; private set; } = null!;

    private void InitializeFaceCommands()
    {
        SetExpressionVariantCommand = new RelayCommand<ExpressionVariantChoice>(choice =>
        {
            if (choice != null && HasSelectedCharacter)
                SetExpressionVariant(_selectedPanelId!.Value, _selectedCharacterIndex, choice.Slot, choice.Variant);
        });
        SaveFaceCommand = new RelayCommand(() =>
        {
            if (HasSelectedCharacter && SaveFace(_selectedPanelId!.Value, _selectedCharacterIndex, NewFaceName))
                NewFaceName = "";
        }, () => HasSelectedCharacter && _catalog != null && NewFaceName.Trim().Length > 0);
    }

    /// <summary>Says something in the status bar in place of the usual hint (a drawing opened elsewhere), until the selection or tool changes.</summary>
    public void ShowNotice(string? notice)
    {
        _notice = notice;
        OnPropertyChanged(nameof(Hint));
    }

    /// <summary>The selected character as its panel shows it (look and panel changes included) - what the face choices are previewed on.</summary>
    private CharacterDefinition? SelectedPanelView => HasSelectedCharacter ? PanelView(_selectedPanelId!.Value, _selectedCharacterIndex) : null;

    // ---------------------------------------------------------------- mix your own

    /// <summary>Sets one face slot's variant - happy eyes, an open mouth - keeping the rest of the face: a mix of your own, in one undo step.</summary>
    public void SetExpressionVariant(PanelId panelId, int index, string slot, string variant)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;
        if (ExpressionPresets.VariantOf(panel.CharacterInstances[index].Pose, slot) == variant)
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => c with { Pose = ExpressionPresets.SetVariant(c.Pose, slot, variant) }));
        RaiseCharacterViewChanged();
    }

    /// <summary>
    /// The Face dropdown's "mix your own" rows, for when no preset is quite it: a row per face
    /// slot with the variants the face worn there draws (its own ones too), each a close-up of
    /// the selected character with the rest of its face as it is - so what's shown is what a
    /// click gives. A slot with nothing to choose between has no row.
    /// </summary>
    public IReadOnlyList<ExpressionSlotRow> ExpressionMixer
    {
        get
        {
            if (SelectedCharacter is not { } instance || SelectedPanelView is not { } character)
                return [];
            var face = new PoseData(instance.Pose.ViewAngle, [], new SortedDictionary<string, string>(instance.Pose.Expression ?? [], StringComparer.Ordinal));
            var (panelId, index) = (_selectedPanelId!.Value, _selectedCharacterIndex);
            var rows = new List<ExpressionSlotRow>();
            foreach (var slot in SavedFaces.Slots)
            {
                var worn = WornIn(character, slot);
                var variants = SavedFaces.VariantsFor(slot, worn.Select(a => a.Sticker));
                if (variants.Count < 2)
                    continue;
                var current = ExpressionPresets.VariantOf(face, slot);
                var choices = variants
                    .Select(v => new ExpressionVariantChoice(slot, v, ExpressionPresets.VariantName(slot, v), character, ExpressionPresets.SetVariant(face, slot, v), v == current))
                    .ToList();
                rows.Add(new ExpressionSlotRow(slot, StickerSlots.Get(slot).Label, choices, worn.Any(a => a.HasArt) && _catalog != null,
                    new RelayCommand(() => DrawExpression(panelId, index, slot, newOne: true)),
                    new RelayCommand(() => DrawExpression(panelId, index, slot, newOne: false))));
            }
            return rows;
        }
    }

    /// <summary>What <paramref name="character"/> (a panel's view of it) wears in <paramref name="slot"/>.</summary>
    private static IReadOnlyList<StickerAsset> WornIn(CharacterDefinition character, string slot) =>
        character.Stickers.TryGetValue(slot, out var ids) ? ids.Select(character.Wardrobe.Find).OfType<StickerAsset>().ToList() : [];

    /// <summary>
    /// Draws the face the character wears in <paramref name="slot"/> in the user's SVG editor:
    /// a new expression of it (<paramref name="newOne"/>: a copy of the one shown, named "My
    /// mouth", "My mouth 2"..., and shown in this panel at once - one undo step), or the one
    /// shown. Every save there comes back as an undo step of this page's.
    /// </summary>
    public void DrawExpression(PanelId panelId, int index, string slot, bool newOne)
    {
        if (_catalog is null || !Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || PanelView(panelId, index) is not { } character || WornIn(character, slot).FirstOrDefault(a => a.HasArt) is not { } asset)
            return;
        var instance = panel.CharacterInstances[index];
        var variant = asset.Sticker.VariantFor(slot, instance.Pose.Expression);
        if (newOne)
        {
            var shown = variant;
            variant = SavedFaces.NewVariantKey($"My {StickerSlots.Get(slot).Label.ToLowerInvariant()}", asset.Sticker.Variants);
            var added = variant;
            using (History.Group($"New {StickerSlots.Get(slot).Label.ToLowerInvariant()}", this))
            {
                _catalog.EditCharacter(instance.CharacterId, "New expression",
                    c => c.Wardrobe.Find(asset.Id) is { } own ? c with { Wardrobe = c.Wardrobe.With(StickerImport.WithVariant(own, added, shown)) } : c, this);
                SetExpressionVariant(panelId, index, slot, added);
            }
        }
        var view = instance.Pose.ViewAngle == ViewAngle.Profile ? ViewAngle.Profile : ViewAngle.Front;
        ShowNotice(_catalog.DrawVariant(instance.CharacterId, asset.Id, variant, view, this));
    }

    // ---------------------------------------------------------------- saved faces

    /// <summary>The name "Save this face" saves under (the Face dropdown's box); a name already saved is replaced.</summary>
    public string NewFaceName
    {
        get => _newFaceName;
        set
        {
            if (SetProperty(ref _newFaceName, value ?? ""))
                SaveFaceCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The faces saved on the selected character, as close-ups dressed as this panel shows it.</summary>
    public IReadOnlyList<SavedFaceChoice> SavedFaceChoices
    {
        get
        {
            if (SelectedCharacter is not { } instance || SelectedPanelView is not { Expressions: { Count: > 0 } faces } character)
                return [];
            var (panelId, index) = (_selectedPanelId!.Value, _selectedCharacterIndex);
            var standing = new PoseData(instance.Pose.ViewAngle, [], new SortedDictionary<string, string>());
            return faces
                .Select(face => new SavedFaceChoice(face, character, SavedFaces.Apply(standing, face), SavedFaces.Shows(instance.Pose, face),
                    new RelayCommand(() => ApplySavedFace(panelId, index, face)),
                    new RelayCommand(() => DeleteSavedFace(instance.CharacterId, face.Name))))
                .ToList();
        }
    }

    public bool HasSavedFaces => SavedFaceChoices.Count > 0;

    /// <summary>The saved faces' heading: whose they are.</summary>
    public string SavedFacesTitle => SelectedCharacterDefinition is { } character ? $"{character.Name}'s faces" : "";

    /// <summary>Gives the character a saved face, in one undo step (the pose is untouched).</summary>
    public void ApplySavedFace(PanelId panelId, int index, SavedExpression face)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || SavedFaces.Shows(panel.CharacterInstances[index].Pose, face))
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => c with { Pose = SavedFaces.Apply(c.Pose, face) }));
        RaiseCharacterViewChanged();
    }

    /// <summary>
    /// Saves the character's face in this panel on the character as <paramref name="name"/>,
    /// for any panel to use - one undo step of this page's. False if there's no name or
    /// nothing to save it on.
    /// </summary>
    public bool SaveFace(PanelId panelId, int index, string name)
    {
        if (_catalog is null || name.Trim().Length == 0 || !Working.Panels.TryGetValue(panelId, out var panel)
            || index < 0 || index >= panel.CharacterInstances.Count)
            return false;
        var instance = panel.CharacterInstances[index];
        var face = SavedFaces.Capture(name, instance.Pose);
        _catalog.EditCharacter(instance.CharacterId, $"Save face \"{face.Name}\"", c => SavedFaces.Save(c, face), this);
        RaiseFaceChoicesChanged();
        return true;
    }

    /// <summary>Forgets a saved face (panels showing it keep it - it's their own expression); one undo step.</summary>
    public void DeleteSavedFace(CharacterId character, string name)
    {
        _catalog?.EditCharacter(character, $"Delete face \"{name}\"", c => SavedFaces.Delete(c, name), this);
        RaiseFaceChoicesChanged();
    }

    private void RaiseFaceChoicesChanged()
    {
        OnPropertyChanged(nameof(ExpressionChoices));
        OnPropertyChanged(nameof(ExpressionMixer));
        OnPropertyChanged(nameof(SavedFaceChoices));
        OnPropertyChanged(nameof(HasSavedFaces));
        OnPropertyChanged(nameof(SavedFacesTitle));
        OnPropertyChanged(nameof(SelectedExpressionName));
    }
}
