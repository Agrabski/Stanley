using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;
using Stanley.StickerLibrary;

namespace Stanley.Editors;

/// <summary>
/// A character built from shapes (docs/shape-characters.md §9.2): the Shapes tab - creature
/// presets, the four parts, joined or separate pieces, Height % - and the contextual Module
/// tab for the part picked on the stage or the Shapes tab: its shape, size, turn, flips,
/// colour, a limb's style, ends and proportions, and taking it off. Every change is a
/// <see cref="ModuleEditing"/> call and one undo step; a slider or stage drag is one gesture.
/// A character with a generated body has none of this (<see cref="IsModular"/>).
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    private ModulePart? _selectedModule;
    private ColorSlotEditor? _moduleColorEditor;

    public IRelayCommand<CreaturePresetChoice> ApplyCreatureCommand { get; private set; } = null!;
    public IRelayCommand<ModulePartChoice> PickModuleCommand { get; private set; } = null!;
    public IRelayCommand<ModuleShapeChoice> SwapModuleShapeCommand { get; private set; } = null!;
    public IRelayCommand<LimbStyleChoice> SetLimbStyleCommand { get; private set; } = null!;
    public IRelayCommand FlipModuleHorizontallyCommand { get; private set; } = null!;
    public IRelayCommand FlipModuleVerticallyCommand { get; private set; } = null!;
    public IRelayCommand RemoveModuleCommand { get; private set; } = null!;
    public IRelayCommand DeselectModuleCommand { get; private set; } = null!;
    public IRelayCommand ModuleSameAsSkinCommand { get; private set; } = null!;

    private void InitializeModules()
    {
        ApplyCreatureCommand = new RelayCommand<CreaturePresetChoice>(choice =>
        {
            if (choice != null)
                ApplyCreature(choice.Preset);
        });
        PickModuleCommand = new RelayCommand<ModulePartChoice>(choice =>
        {
            if (choice == null)
                return;
            if (choice.IsPresent)
                SelectModule(choice.Part);
            else
                AddModule(choice.Part);
        });
        SwapModuleShapeCommand = new RelayCommand<ModuleShapeChoice>(choice =>
        {
            if (choice != null)
                SwapModuleShape(choice);
        });
        SetLimbStyleCommand = new RelayCommand<LimbStyleChoice>(choice =>
        {
            if (choice != null && !choice.IsCurrent)
                EditSelectedModule((body, part) => ModuleEditing.SetLimbStyle(body, part, choice.Style));
        });
        FlipModuleHorizontallyCommand = new RelayCommand(() => EditSelectedModule(ModuleEditing.FlipHorizontally), () => SelectedModuleIsShape);
        FlipModuleVerticallyCommand = new RelayCommand(() => EditSelectedModule(ModuleEditing.FlipVertically), () => SelectedModuleIsShape);
        RemoveModuleCommand = new RelayCommand(RemoveSelectedModule, () => CanRemoveModule);
        DeselectModuleCommand = new RelayCommand(() => SelectModule(null));
        ModuleSameAsSkinCommand = new RelayCommand(() => EditSelectedModule((body, part) => ModuleEditing.SetColor(body, part, null)), () => SelectedModuleHasOwnColor);
    }

    // ---------------------------------------------------------------- which kind of body

    /// <summary>Built from shapes: the Shapes tab (and Module tab) instead of the Body tab.</summary>
    public bool IsModular => Working.IsModular;

    /// <summary>Generated from sliders: the Body tab.</summary>
    public bool IsGeneratedBody => !IsModular;

    // ---------------------------------------------------------------- Shapes tab

    /// <summary>The creature presets, each previewed as this character built that way (its colours and clothes kept), the one it's built as now ticked.</summary>
    public IReadOnlyList<CreaturePresetChoice> CreatureChoices
    {
        get
        {
            if (Working.Modules is not { } current)
                return [];
            var look = LookWorking;
            var now = Fingerprint(current);
            return Stanley.StickerLibrary.CreaturePresets.All
                .Select(p => (Preset: p, Body: KeepingColors(p.Body, current)))
                .Select(p => new CreaturePresetChoice(p.Preset, look with { Modules = p.Body }, Fingerprint(p.Body) == now))
                .ToList();
        }
    }

    /// <summary>The four parts, as buttons: each drawn in its own shape and colour, a missing one as a faint +.</summary>
    public IReadOnlyList<ModulePartChoice> ModulePartChoices
    {
        get
        {
            if (Working.Modules is not { } body)
                return [];
            var look = CharacterLooks.Resolve(LookWorking);
            var skin = LookWorking.Skin;
            return ModuleParts.All.Select(part =>
            {
                var present = ModuleEditing.Has(body, part);
                var (icon, aspect, slot) = (ModuleEditing.ShapeOf(body, part), ModuleEditing.LimbOf(body, part)) switch
                {
                    ({ } shape, _) => (ModuleSockets.Flipped(shape.Shape, shape.FlipX == true, shape.FlipY == true), shape.Width / shape.Height, shape.ColorSlot),
                    (_, { } limb) => (ModuleShapes.RoundedRectangle(0.5), LimbAspect(limb), limb.ColorSlot),
                    _ => ((ModuleShape?)null, 1.0, CharacterDefinition.SkinSlot),
                };
                return new ModulePartChoice(part, present, _selectedModule == part, icon, aspect, ModuleNames.Brush(look.Color(slot, skin)));
            }).ToList();
        }
    }

    /// <summary>Joined: the pieces join seamlessly, one creature. Two-way, one undo step.</summary>
    public bool IsJoinedBody
    {
        get => Working.Modules is { IsSeparate: false };
        set
        {
            if (value)
                SetSeparate(false);
        }
    }

    /// <summary>Separate: every piece keeps its own full outline, like building blocks. Two-way, one undo step.</summary>
    public bool IsSeparateBody
    {
        get => Working.Modules is { IsSeparate: true };
        set
        {
            if (value)
                SetSeparate(true);
        }
    }

    private void SetSeparate(bool separate)
    {
        if (Working.Modules is { } body && body.IsSeparate != separate)
            EditModules(EditResult<ModularBody>.Success(ModuleEditing.SetSeparate(body, separate)));
    }

    /// <summary>How tall the whole body stands, percent of an average adult: setting it scales every part about the ground (Height %). A slider drag is one gesture.</summary>
    public double BodyHeightPercent
    {
        get => Working.Modules is { } body ? Math.Round(ModularRig.Height(body) * 100) : 0;
        set
        {
            // Scaled from where the drag (or click) started, so each move sets the height outright instead of compounding.
            if (Committed.Modules is not { } baseline || !double.IsFinite(value))
                return;
            var height = ModularRig.Height(baseline);
            if (height <= 0)
                return;
            EditModules(ModuleEditing.Scale(baseline, Math.Clamp(value, MinBodyHeightPercent, MaxBodyHeightPercent) / 100 / height), baseline);
        }
    }

    public double MinBodyHeightPercent => 15;
    public double MaxBodyHeightPercent => 250;

    // ---------------------------------------------------------------- the selected module (Module tab)

    /// <summary>The part the Module tab works on (picked on the stage or the Shapes tab), or null.</summary>
    public ModulePart? SelectedModule => _selectedModule;

    public bool HasSelectedModule => _selectedModule is not null;

    public bool SelectedModuleIsShape => _selectedModule?.IsShape() == true;

    public bool SelectedModuleIsLimb => _selectedModule?.IsLimb() == true;

    /// <summary>"Head", "Torso", "Arms" or "Legs": the Module tab's title.</summary>
    public string SelectedModuleName => _selectedModule is { } part ? ModuleNames.Title(part) : "";

    /// <summary>Picks a part (null lets go). A part the character hasn't got can't be picked; picking one lets go of any sticker.</summary>
    public void SelectModule(ModulePart? part)
    {
        if (part is { } p && (Working.Modules is not { } body || !ModuleEditing.Has(body, p)))
            part = null;
        if (part is not null)
            SelectSticker(null);
        if (_selectedModule == part)
            return;
        _selectedModule = part;
        ShowMessage(null);
        RaiseSelectedModuleChanged();
    }

    /// <summary>
    /// Puts a missing part back with the library's default (the stage's faint +, or a missing
    /// part's button), and picks it. With <paramref name="side"/>, a limb that's on one side
    /// only gets its other side back instead. One undo step.
    /// </summary>
    public void AddModule(ModulePart part, LimbSide? side = null)
    {
        if (Working.Modules is not { } body)
            return;
        var result = side is not null && ModuleEditing.LimbOf(body, part) is not null
            ? ModuleEditing.SetSides(body, part, LimbSides.Both)
            : part.IsShape()
                ? ModuleEditing.Add(body, part, Stanley.StickerLibrary.CreaturePresets.DefaultShape(part))
                : ModuleEditing.Add(body, part, Stanley.StickerLibrary.CreaturePresets.DefaultLimbs(part));
        if (EditModules(result))
            SelectModule(part);
    }

    private ShapeModule? SelectedShape => _selectedModule is { } part && Working.Modules is { } body ? ModuleEditing.ShapeOf(body, part) : null;

    private LimbModule? SelectedLimb => _selectedModule is { } part && Working.Modules is { } body ? ModuleEditing.LimbOf(body, part) : null;

    // A head's or torso's size and turn. Sizes are shown as percent of an average adult's height, like Height %.

    public double ModuleWidth
    {
        get => SelectedShape is { } s ? Math.Round(s.Width * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.Resize(body, part, value / 100, ModuleEditing.ShapeOf(body, part)?.Height ?? 0));
    }

    public double ModuleHeight
    {
        get => SelectedShape is { } s ? Math.Round(s.Height * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.Resize(body, part, ModuleEditing.ShapeOf(body, part)?.Width ?? 0, value / 100));
    }

    /// <summary>Degrees clockwise, -180 to 180.</summary>
    public double ModuleRotation
    {
        get => SelectedShape is { } s ? Math.Round(s.Rotation ?? 0) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetRotation(body, part, value));
    }

    public bool IsModuleFlippedHorizontally => SelectedShape?.FlipX == true;

    public bool IsModuleFlippedVertically => SelectedShape?.FlipY == true;

    /// <summary>The head's or torso's shape gallery: its own kind by category, then the other kind's.</summary>
    public ModuleShapeGallery? ModuleShapeGallery
    {
        get
        {
            if (_selectedModule is not { } part || SelectedShape is not { } module || ShapeLibrary.KindFor(part) is not { } kind)
                return null;
            var fill = ModuleFill(module.ColorSlot);
            var aspect = module.Width / module.Height;
            var current = ShapeLibrary.SourceOf(module.Shape);
            ModuleShapeChoice Choice(LibraryShape shape) => new(shape.DisplayName, part, null, shape,
                ModuleSockets.Flipped(shape.Shape, module.FlipX == true, module.FlipY == true), aspect, fill, current?.Key == shape.Key,
                current?.Key == shape.Key ? $"{shape.DisplayName} - the {part.Noun()} now" : $"Make the {part.Noun()} {Article(shape.DisplayName)} - its size, colour and place stay");
            var sections = ShapeLibrary.Categories
                .Select(category => new ModuleShapeSection(ShapeLibrary.DisplayName(category), ShapeLibrary.ForKind(kind, category).Select(Choice).ToList()))
                .Where(s => s.Choices.Count > 0)
                .ToList();
            if (MyShapesSection(part, null, module.Shape, aspect, fill, $"the {part.Noun()}", module.FlipX == true, module.FlipY == true) is { } mine)
                sections.Add(mine);
            var other = kind == ShapeLibrary.Head ? ShapeLibrary.Torso : ShapeLibrary.Head;
            sections.Add(new ModuleShapeSection(other == ShapeLibrary.Head ? "Head shapes" : "Torso shapes", ShapeLibrary.ForKind(other).Select(Choice).ToList()));
            return new ModuleShapeGallery("Shape", current?.DisplayName ?? KeptShapeName(module.Shape) ?? "Your own",
                ModuleSockets.Flipped(module.Shape, module.FlipX == true, module.FlipY == true), aspect, fill, sections,
                $"The {part.Noun()}'s shape - pick another; its size, colour and place stay", SwapModuleShapeCommand);
        }
    }

    // A limb's style and proportions.

    public IReadOnlyList<LimbStyleChoice> LimbStyleChoices =>
        SelectedLimb is { } limb ? Enum.GetValues<LimbStyle>().Select(s => new LimbStyleChoice(s, s == limb.Style)).ToList() : [];

    public bool HasLimbTaper => SelectedLimb?.Style == LimbStyle.Taper;

    public bool HasLimbPieces => SelectedLimb?.Style == LimbStyle.Pieces;

    public double LimbLength
    {
        get => SelectedLimb is { } l ? Math.Round(l.Length * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetLength(body, part, value / 100));
    }

    /// <summary>Thickness at the root, tenths of a percent of an adult's height (finer than the others: limbs are thin).</summary>
    public double LimbThickness
    {
        get => SelectedLimb is { } l ? Math.Round(l.Thickness * 1000) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetThickness(body, part, value / 1000));
    }

    /// <summary>The thickness as a user reads it: percent of an adult's height, to a tenth.</summary>
    public string LimbThicknessText => SelectedLimb is { } l ? $"{l.Thickness * 100:0.#}%" : "";

    /// <summary>How much thinner the end is than the root, percent.</summary>
    public double LimbTaper
    {
        get => SelectedLimb is { } l ? Math.Round((l.Taper ?? 0.5) * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetTaper(body, part, value / 100));
    }

    /// <summary>Where the elbow or knee sits, percent of the way down.</summary>
    public double LimbBend
    {
        get => SelectedLimb is { } l ? Math.Round((l.Bend ?? 0.5) * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetBend(body, part, value / 100));
    }

    /// <summary>The hand's or foot's size, percent of an adult's height.</summary>
    public double LimbEndSize
    {
        get => SelectedLimb is { } l ? Math.Round((l.EndSize ?? 2 * l.Thickness) * 100) : 0;
        set => EditSelectedModule((body, part) => ModuleEditing.SetEndSize(body, part, value / 100));
    }

    public double MaxLimbEndSize => ModularBodies.MaxEndSize * 100;

    /// <summary>"Hand" or "Foot", for the selected limb's end.</summary>
    public string LimbEndName => _selectedModule == ModulePart.Legs ? "Foot" : "Hand";

    /// <summary>The selected limb's hand or foot gallery: its own round end, then the library's by category.</summary>
    public ModuleShapeGallery? LimbEndGallery => LimbGallery(LimbPiece.End);

    /// <summary>For Pieces: the upper arm's or thigh's shape.</summary>
    public ModuleShapeGallery? LimbUpperGallery => HasLimbPieces ? LimbGallery(LimbPiece.Upper) : null;

    /// <summary>For Pieces: the forearm's or shin's shape.</summary>
    public ModuleShapeGallery? LimbLowerGallery => HasLimbPieces ? LimbGallery(LimbPiece.Lower) : null;

    private ModuleShapeGallery? LimbGallery(LimbPiece piece)
    {
        if (_selectedModule is not { } part || SelectedLimb is not { } limb)
            return null;
        var fill = ModuleFill(limb.ColorSlot);
        var end = piece == LimbPiece.End;
        var (label, noun) = piece switch
        {
            LimbPiece.End => (LimbEndName, part == ModulePart.Legs ? "feet" : "hands"),
            LimbPiece.Upper => ("Upper", part == ModulePart.Legs ? "thighs" : "upper arms"),
            _ => ("Lower", part == ModulePart.Legs ? "shins" : "forearms"),
        };
        var aspect = end ? 1 : LimbAspect(limb) * 2;
        var shape = ModuleEditing.ShapeOf(limb, piece);
        var current = shape is null ? null : ShapeLibrary.SourceOf(shape);
        var none = end ? "Round" : "Capsule";
        var noneIcon = end ? ModuleShapes.Ellipse() : ModuleShapes.RoundedRectangle(0.5);
        var choices = new List<ModuleShapeChoice>
        {
            new(none, part, piece, null, noneIcon, aspect, fill, shape is null,
                shape is null ? $"{none} - the {noun} now" : $"Plain {none.ToLowerInvariant()} {noun}"),
        };
        var sections = new List<ModuleShapeSection> { new("", choices) };
        var kind = ShapeLibrary.KindFor(part, piece);
        foreach (var category in ShapeLibrary.Categories)
        {
            var items = ShapeLibrary.ForKind(kind, category).Select(s => new ModuleShapeChoice(s.DisplayName, part, piece, s, s.Shape, aspect, fill, current?.Key == s.Key,
                current?.Key == s.Key ? $"{s.DisplayName} - the {noun} now" : $"Give the {part.Noun()} {s.DisplayName.ToLowerInvariant()} {noun}")).ToList();
            if (items.Count > 0)
                sections.Add(new ModuleShapeSection(ShapeLibrary.DisplayName(category), items));
        }
        if (MyShapesSection(part, piece, shape, aspect, fill, $"their {noun}") is { } mine)
            sections.Add(mine);
        return new ModuleShapeGallery(label, shape is null ? none : current?.DisplayName ?? KeptShapeName(shape) ?? "Your own", shape ?? noneIcon, aspect, fill, sections,
            $"The {part.Noun()}' {noun} - pick another shape", SwapModuleShapeCommand);
    }

    public bool IsBothSides
    {
        get => SelectedLimb is { } l && (l.Sides ?? LimbSides.Both) == LimbSides.Both;
        set
        {
            if (value)
                SetSides(LimbSides.Both);
        }
    }

    /// <summary>Only the character's own left one (on the right as you look at them from the front).</summary>
    public bool IsLeftSideOnly
    {
        get => SelectedLimb?.Sides == LimbSides.Left;
        set
        {
            if (value)
                SetSides(LimbSides.Left);
        }
    }

    public bool IsRightSideOnly
    {
        get => SelectedLimb?.Sides == LimbSides.Right;
        set
        {
            if (value)
                SetSides(LimbSides.Right);
        }
    }

    private void SetSides(LimbSides sides)
    {
        if (SelectedLimb is { } limb && (limb.Sides ?? LimbSides.Both) != sides)
            EditSelectedModule((body, part) => ModuleEditing.SetSides(body, part, sides));
    }

    // The selected part's colour: the skin, or a colour of its own.

    /// <summary>Whether the selected part has a colour of its own rather than the skin's.</summary>
    public bool SelectedModuleHasOwnColor => SelectedModuleColorSlot is { } slot && slot != CharacterDefinition.SkinSlot;

    private string? SelectedModuleColorSlot => SelectedShape?.ColorSlot ?? SelectedLimb?.ColorSlot;

    /// <summary>The selected part's own colour slot, as a colour dropdown: picking a colour gives the part that colour (until then it shows the skin's).</summary>
    public ColorSlotEditor? SelectedModuleColorEditor
    {
        get
        {
            if (_selectedModule is not { } part)
                return null;
            var slot = ModuleNames.ColorSlot(part);
            if (_moduleColorEditor?.Slot != slot)
                _moduleColorEditor = NewColorEditor(slot);
            RefreshColorEditor(_moduleColorEditor, LookWorking, CharacterLooks.Resolve(LookWorking));
            return _moduleColorEditor;
        }
    }

    /// <summary>What the Colour group says: "Skin" while the part follows it, else its own.</summary>
    public string ModuleColorNote => SelectedModuleHasOwnColor ? "Its own colour" : "Same as skin";

    public bool CanRemoveModule => _selectedModule is { } part && Working.Modules is { } body && ModuleEditing.Remove(body, part).IsValid;

    /// <summary>Why Remove can't take the part off, or what it does.</summary>
    public string RemoveModuleTip => CanRemoveModule
        ? $"Take the {SelectedModuleName.ToLowerInvariant()} off - a faint + on the character puts {(SelectedModuleIsLimb ? "them" : "it")} back"
        : "A character needs a head or a torso to hold the rest together";

    private void RemoveSelectedModule()
    {
        if (_selectedModule is not { } part || Working.Modules is not { } body)
            return;
        var result = ModuleEditing.Remove(body, part);
        if (!result.IsValid)
        {
            ShowMessage(result.Error);
            return;
        }
        SelectModule(null);
        EditModules(result);
    }

    // ---------------------------------------------------------------- dragging on the stage

    /// <summary>Starts a stage drag of the selected head or torso (moving it off its socket, or resizing it by a corner): one gesture, one undo step. False when there's nothing to drag.</summary>
    public bool BeginModuleDrag()
    {
        if (!SelectedModuleIsShape)
            return false;
        BeginGesture();
        return true;
    }

    /// <summary>The selected head or torso moved by (<paramref name="dx"/>, <paramref name="dy"/>) figure units from where the drag started.</summary>
    public void UpdateModuleNudge(double dx, double dy)
    {
        if (!IsGestureActive || _selectedModule is not { } part || Committed.Modules is not { } baseline || ModuleEditing.ShapeOf(baseline, part) is not { } start)
            return;
        var offset = start.Offset ?? default;
        EditModules(ModuleEditing.SetOffset(baseline, part, new Point2D(offset.X + dx, offset.Y + dy)), baseline);
    }

    /// <summary>The selected head or torso <paramref name="widthFactor"/> by <paramref name="heightFactor"/> times the size it was when the drag started.</summary>
    public void UpdateModuleResize(double widthFactor, double heightFactor)
    {
        if (!IsGestureActive || _selectedModule is not { } part || Committed.Modules is not { } baseline || ModuleEditing.ShapeOf(baseline, part) is not { } start)
            return;
        EditModules(ModuleEditing.Resize(baseline, part, start.Width * widthFactor, start.Height * heightFactor), baseline);
    }

    public void EndModuleDrag()
    {
        CommitGesture();
        OnPropertyChanged(nameof(CreatureChoices));
    }

    // ---------------------------------------------------------------- edits

    /// <summary>A creature preset in place of the body, one undo step: the colours each part had and everything worn stay.</summary>
    public void ApplyCreature(CreaturePreset preset)
    {
        if (Working.Modules is not { } current)
            return;
        var body = KeepingColors(preset.Body, current);
        if (Fingerprint(body) == Fingerprint(current))
            return;
        if (EditModules(ModuleEditing.ApplyPreset(body)) && _selectedModule is { } part && !ModuleEditing.Has(body, part))
            SelectModule(null);
    }

    private void SwapModuleShape(ModuleShapeChoice choice)
    {
        if (choice.IsCurrent || Working.Modules is not { } body)
            return;
        if (choice.Kept is { } kept)
            PickKeptShape(choice, kept);
        else if (choice.Piece is { } piece)
            EditModules(ModuleEditing.SwapShape(body, choice.Part, piece, choice.Library?.Instantiate()));
        else if (choice.Library is { } library)
            EditModules(ModuleEditing.SwapShape(body, choice.Part, library.Instantiate()));
    }

    private void EditSelectedModule(Func<ModularBody, ModulePart, EditResult<ModularBody>> edit)
    {
        if (_selectedModule is { } part && Working.Modules is { } body)
            EditModules(edit(body, part));
    }

    /// <summary>
    /// Stores an edited body: a live preview inside a gesture (a slider or stage drag),
    /// otherwise one undo step. A refused edit says why in the status bar. True if it went in.
    /// </summary>
    /// <param name="from">The body the edit was made from when it isn't the working one (a drag's starting point).</param>
    private bool EditModules(EditResult<ModularBody> result, ModularBody? from = null)
    {
        if (!result.IsValid)
        {
            ShowMessage(result.Error);
            return false;
        }
        var edited = EditResult<CharacterDefinition>.Success((from is null ? Working : Committed) with { Modules = result.Value });
        if (IsGestureActive)
            UpdateGesture(edited);
        else
            Apply(edited);
        return true;
    }

    /// <summary>
    /// A colour picked in a part's own colour dropdown: the colour goes in its slot and, if the
    /// part was following the skin, the part takes that slot - one undo step. False when
    /// <paramref name="slot"/> isn't a part's.
    /// </summary>
    private bool SetModuleColor(string slot, ColorValue color)
    {
        if (ModuleNames.PartOfColorSlot(slot) is not { } part || Committed.Modules is not { } body || !ModuleEditing.Has(body, part))
            return false;
        var edited = StoreLookEdit(Committed, c => LookEditing.SetColor(c, slot, color));
        if ((ModuleEditing.ShapeOf(body, part)?.ColorSlot ?? ModuleEditing.LimbOf(body, part)?.ColorSlot) != slot && ModuleEditing.SetColor(body, part, slot) is { IsValid: true } colored)
            edited = edited with { Modules = colored.Value };
        Apply(EditResult<CharacterDefinition>.Success(edited));
        return true;
    }

    /// <summary>The colour slots the parts have of their own, for the Look tab's colours.</summary>
    private static IEnumerable<string> ModuleColorSlots(CharacterDefinition character) =>
        character.Modules is not { } body
            ? []
            : new[] { body.Head?.ColorSlot, body.Torso?.ColorSlot, body.Arms?.ColorSlot, body.Legs?.ColorSlot }
                .OfType<string>().Where(s => s != CharacterDefinition.SkinSlot).Distinct();

    // ---------------------------------------------------------------- status bar

    /// <summary>The status bar's hint for a body built from shapes: what a click on the stage does, or what to do with the part picked. Null for a generated body.</summary>
    private string? ModuleHint
    {
        get
        {
            if (!IsModular)
                return null;
            if (SelectedModuleIsShape)
                return $"{SelectedModuleName}: pick another shape {Where($"on the {SelectedModuleName} tab", "under " + SelectedModuleName)}, drag it to move it off its place, drag a corner to resize it, or double-click it to edit its points. " +
                       "Click the background when you're done.";
            if (SelectedModuleIsLimb)
                return $"{SelectedModuleName}: pick a style and {(SelectedModule == ModulePart.Legs ? "feet" : "hands")}, and set how long and thick they are {Where($"on the {SelectedModuleName} tab", "under " + SelectedModuleName)}. " +
                       "Pose them on the page by dragging their ends.";
            var missing = Working.Modules is { } body && ModuleParts.All.Any(p => !ModuleEditing.Has(body, p)) ? " A faint + puts a missing part back." : "";
            return $"Click a part of the body to change it, or pick a creature {Where("on the Shapes tab", "under Shapes")}.{missing} " +
                   $"Dress them {Where("on the Look tab", "under Look")}; every panel this character is in updates as you go.";
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary><paramref name="preset"/> with each part's colour taken from the same part of <paramref name="current"/>, where both have it.</summary>
    private static ModularBody KeepingColors(ModularBody preset, ModularBody current) => preset with
    {
        Head = preset.Head is { } h ? h with { Color = current.Head?.Color ?? h.Color } : null,
        Torso = preset.Torso is { } t ? t with { Color = current.Torso?.Color ?? t.Color } : null,
        Arms = preset.Arms is { } a ? a with { Color = current.Arms?.Color ?? a.Color } : null,
        Legs = preset.Legs is { } l ? l with { Color = current.Legs?.Color ?? l.Color } : null,
    };

    /// <summary>A body as the file writes it - records holding lists only compare by reference, so two built alike are told apart by this.</summary>
    private static string Fingerprint(ModularBody body) => ProjectJson.Serialize(body);

    /// <summary>A limb drawn as a tube: its width over its length, held to something an icon can show.</summary>
    private static double LimbAspect(LimbModule limb) =>
        Math.Clamp((limb.Style == LimbStyle.Stick ? ModularRig.StickRadius * 2 : limb.Thickness) / Math.Max(limb.Length, 1e-3), 0.12, 1);

    private Avalonia.Media.IBrush ModuleFill(string slot) =>
        ModuleNames.Brush(CharacterLooks.Resolve(LookWorking).Color(slot, LookWorking.Skin));

    private static string Article(string name) => ("aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an " : "a ") + name.ToLowerInvariant();

    private void RaiseSelectedModuleChanged()
    {
        OnPropertyChanged(nameof(SelectedModule));
        OnPropertyChanged(nameof(HasSelectedModule));
        OnPropertyChanged(nameof(SelectedModuleIsShape));
        OnPropertyChanged(nameof(SelectedModuleIsLimb));
        OnPropertyChanged(nameof(SelectedModuleName));
        OnPropertyChanged(nameof(LimbEndName));
        OnPropertyChanged(nameof(Hint));
        RaiseModuleValuesChanged();
    }

    /// <summary>Everything the Shapes and Module tabs show that follows the body.</summary>
    private void RaiseModulesChanged()
    {
        if (_selectedModule is { } part && (Working.Modules is not { } body || !ModuleEditing.Has(body, part)))
        {
            _selectedModule = null; // taken off (or undone away)
            RaiseSelectedModuleChanged();
        }
        OnPropertyChanged(nameof(IsModular));
        OnPropertyChanged(nameof(IsGeneratedBody));
        // The presets preview the whole character: rebuilt once a drag is done, not on every move.
        if (!IsGestureActive)
            OnPropertyChanged(nameof(CreatureChoices));
        OnPropertyChanged(nameof(IsJoinedBody));
        OnPropertyChanged(nameof(IsSeparateBody));
        OnPropertyChanged(nameof(BodyHeightPercent));
        RaiseModuleValuesChanged();
    }

    private void RaiseModuleValuesChanged()
    {
        OnPropertyChanged(nameof(ModulePartChoices));
        OnPropertyChanged(nameof(ModuleWidth));
        OnPropertyChanged(nameof(ModuleHeight));
        OnPropertyChanged(nameof(ModuleRotation));
        OnPropertyChanged(nameof(IsModuleFlippedHorizontally));
        OnPropertyChanged(nameof(IsModuleFlippedVertically));
        OnPropertyChanged(nameof(ModuleShapeGallery));
        OnPropertyChanged(nameof(LimbStyleChoices));
        OnPropertyChanged(nameof(HasLimbTaper));
        OnPropertyChanged(nameof(HasLimbPieces));
        OnPropertyChanged(nameof(LimbLength));
        OnPropertyChanged(nameof(LimbThickness));
        OnPropertyChanged(nameof(LimbThicknessText));
        OnPropertyChanged(nameof(LimbTaper));
        OnPropertyChanged(nameof(LimbBend));
        OnPropertyChanged(nameof(LimbEndSize));
        OnPropertyChanged(nameof(LimbEndGallery));
        OnPropertyChanged(nameof(LimbUpperGallery));
        OnPropertyChanged(nameof(LimbLowerGallery));
        OnPropertyChanged(nameof(IsBothSides));
        OnPropertyChanged(nameof(IsLeftSideOnly));
        OnPropertyChanged(nameof(IsRightSideOnly));
        OnPropertyChanged(nameof(SelectedModuleHasOwnColor));
        OnPropertyChanged(nameof(SelectedModuleColorEditor));
        OnPropertyChanged(nameof(ModuleColorNote));
        OnPropertyChanged(nameof(CanRemoveModule));
        OnPropertyChanged(nameof(RemoveModuleTip));
        FlipModuleHorizontallyCommand.NotifyCanExecuteChanged();
        FlipModuleVerticallyCommand.NotifyCanExecuteChanged();
        RemoveModuleCommand.NotifyCanExecuteChanged();
        ModuleSameAsSkinCommand.NotifyCanExecuteChanged();
    }
}
