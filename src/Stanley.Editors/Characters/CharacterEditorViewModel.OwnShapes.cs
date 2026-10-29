using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>Asks the view for an SVG whose outline becomes <paramref name="Part"/>'s shape (its hand or foot, for arms or legs: <paramref name="Piece"/>).</summary>
public sealed record ShapeImportRequest(ModulePart Part, LimbPiece? Piece);

/// <summary>
/// Shapes of the user's own for a character built from shapes (docs/shape-characters.md §9.3):
/// the Module tab's *Import shape...* (an SVG's first closed path), *Draw shape* (point by point
/// on the stage, with the page's Freeform gestures) and *Keep in My Assets* (the part's outline,
/// for every comic's galleries' *My shapes*). They work on the picked head or torso, or on the
/// picked arms' or legs' hand or foot. Each change is one undo step; keeping writes My Assets
/// straight away, as keeping a character does.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    // The outline being drawn on the stage (figure units), and the part it's for; null when not drawing.
    private List<ShapeAnchor>? _drawn;
    private (ModulePart Part, LimbPiece? Piece) _drawTarget;

    /// <summary>Raised by <see cref="ImportShapeCommand"/>: the view picks an SVG and hands it to <see cref="ImportShape"/>.</summary>
    public event EventHandler<ShapeImportRequest>? ShapeImportRequested;

    public IRelayCommand ImportShapeCommand { get; private set; } = null!;
    public IRelayCommand DrawShapeCommand { get; private set; } = null!;
    public IRelayCommand KeepShapeInMyAssetsCommand { get; private set; } = null!;

    private void InitializeOwnShapes()
    {
        ImportShapeCommand = new RelayCommand(() =>
        {
            if (OwnShapeTarget is { } target)
                ShapeImportRequested?.Invoke(this, new ShapeImportRequest(target.Part, target.Piece));
        }, () => OwnShapeTarget is not null);
        DrawShapeCommand = new RelayCommand(() =>
        {
            if (IsDrawingShape)
                CancelDrawShape();
            else
                BeginDrawShape();
        }, () => IsDrawingShape || OwnShapeTarget is not null);
        KeepShapeInMyAssetsCommand = new RelayCommand(() => KeepShapeInMyAssets(), () => CanKeepShape);
        PropertyChanged += (_, e) =>
        {
            // Picking another part (or letting go) ends a drawing that was for the one before.
            if (e.PropertyName == nameof(SelectedModule) && IsDrawingShape && OwnShapeTarget != _drawTarget)
                CancelDrawShape();
            if (e.PropertyName is nameof(SelectedModule) or nameof(Working))
                RaiseOwnShapesChanged();
        };
    }

    /// <summary>What Import shape, Draw shape and Keep work on: the picked head or torso, or the picked limb's hand or foot. Null with nothing picked.</summary>
    private (ModulePart Part, LimbPiece? Piece)? OwnShapeTarget =>
        _selectedModule is { } part && Working.Modules is { } body && ModuleEditing.Has(body, part)
            ? (part, part.IsLimb() ? LimbPiece.End : null)
            : null;

    /// <summary>"the head", "their hands": what the commands' tooltips say they work on.</summary>
    private string OwnShapeNoun => _selectedModule switch
    {
        ModulePart.Arms => "their hands",
        ModulePart.Legs => "their feet",
        { } part => "the " + part.Noun(),
        _ => "the part"
    };

    public string ImportShapeTip => $"Import shape - the outline of an SVG drawing (its first closed path) becomes {OwnShapeNoun}; its size, colour and place stay";

    public string DrawShapeTip => IsDrawingShape
        ? "Stop drawing, keeping the shape as it was"
        : $"Draw shape - click on the character to place each point, drag to curve it; click the first point (or press Enter) to make it {OwnShapeNoun}";

    // ---------------------------------------------------------------- importing

    /// <summary>
    /// Makes <paramref name="file"/>'s first closed path the outline of <paramref name="part"/>
    /// (its hand or foot: <paramref name="piece"/>), one undo step: it keeps its size, colour and
    /// place, takes the drawing's proportions and loses any turn or flip. What the file has
    /// besides is reported in the status bar. False, with the reason, if there's no shape in it.
    /// </summary>
    public bool ImportShape(ModulePart part, LimbPiece? piece, string fileName, ArtFile file)
    {
        if (Working.Modules is not { } body)
            return false;
        var import = ShapeOutlineImport.FromFile(file);
        if (import.Outline is not { } outline)
        {
            ShowMessage($"Couldn't import {fileName}: {import.Error}{(import.Report.Count > 0 ? $" ({string.Join("; ", import.Report)})" : "")}.");
            return false;
        }
        var result = piece is { } p
            ? OwnShapeEditing.SwapOwnShape(body, part, p, outline)
            : OwnShapeEditing.SwapOwnShape(body, part, outline, asDrawn: true);
        if (!EditModules(result))
            return false;
        SelectModule(part);
        var what = piece is null ? $"the {part.Noun()}" : part == ModulePart.Legs ? "their feet" : "their hands";
        ShowMessage(import.Report.Count > 0
            ? $"{fileName} is {what} now: {string.Join("; ", import.Report)}."
            : $"{fileName} is {what} now - keep it in My Assets to use it on other characters.");
        return true;
    }

    // ---------------------------------------------------------------- drawing on the stage

    /// <summary>Drawing a shape on the stage (Draw shape): the stage takes clicks as points until it's closed or Esc.</summary>
    public bool IsDrawingShape => _drawn is not null;

    /// <summary>The points placed so far, figure units (as the body stands at rest), for the stage to draw. Empty when not drawing.</summary>
    public IReadOnlyList<ShapeAnchor> DrawnShape => _drawn ?? [];

    /// <summary>Starts Draw shape for the picked part: the stage takes clicks as points. Nothing happens with nothing picked.</summary>
    public void BeginDrawShape()
    {
        if (OwnShapeTarget is not { } target)
            return;
        SelectSticker(null);
        _drawTarget = target;
        _drawn = [];
        ShowMessage(null);
        RaiseDrawingChanged();
    }

    /// <summary>A point placed with Draw shape at <paramref name="at"/> (figure units): a corner; dragged before letting go, <see cref="PullDrawnPoint"/> makes it smooth.</summary>
    public void PlaceDrawnPoint(Point2D at)
    {
        if (_drawn is null)
            return;
        _drawn.Add(ShapePointEditing.Placed(at));
        RaiseDrawnShapeChanged();
    }

    /// <summary>The last point placed, pulled out to <paramref name="pulledTo"/> as the pointer drags: smooth, its handles either side, as on the page.</summary>
    public void PullDrawnPoint(Point2D pulledTo)
    {
        if (_drawn is not { Count: > 0 })
            return;
        _drawn[^1] = ShapePointEditing.Placed(_drawn[^1].Point, pulledTo);
        RaiseDrawnShapeChanged();
    }

    /// <summary>Backspace: takes the last point back.</summary>
    public void TakeBackDrawnPoint()
    {
        if (_drawn is not { Count: > 0 })
            return;
        _drawn.RemoveAt(_drawn.Count - 1);
        RaiseDrawnShapeChanged();
    }

    /// <summary>Whether clicking the first point (or Enter) can close the shape: three points or more.</summary>
    public bool CanCloseDrawnShape => _drawn is { Count: >= 3 };

    /// <summary>
    /// Closes the shape drawn (the first point clicked again, or Enter) and makes it the part's
    /// outline, placed where it was drawn - one undo step. With fewer than three points it says
    /// so and carries on drawing. True when the part took it.
    /// </summary>
    public bool CloseDrawnShape()
    {
        if (_drawn is null || Working.Modules is not { } body)
            return false;
        if (_drawn.Count < 3)
        {
            ShowMessage("Place at least three points, then click the first one to close the shape.");
            return false;
        }
        var (part, _) = _drawTarget;
        var result = OwnShapeEditing.PlaceDrawnShape(body, part, _drawn.ToList(), PreviewAngle);
        if (!result.IsValid)
        {
            ShowMessage(result.Error);
            return false;
        }
        _drawn = null;
        RaiseDrawingChanged();
        EditModules(result);
        SelectModule(part);
        ShowMessage(part.IsLimb()
            ? $"That's {(part == ModulePart.Legs ? "their feet" : "their hands")} now - keep it in My Assets to use it on other characters."
            : $"That's the {part.Noun()} now - drag it or its corners to place it; keep it in My Assets to use it on other characters.");
        return true;
    }

    /// <summary>Esc: stops drawing, the part as it was.</summary>
    public void CancelDrawShape()
    {
        if (_drawn is null)
            return;
        _drawn = null;
        RaiseDrawingChanged();
    }

    /// <summary>The status bar while drawing; null when not.</summary>
    private string? DrawShapeHint => _drawn switch
    {
        null => null,
        { Count: 0 } => $"Draw shape for {OwnShapeNoun}: click to place a point, or drag to pull out a curve. Esc stops.",
        { Count: < 3 } => "Keep placing points - click for a corner, drag for a curve. Backspace takes the last one back, Esc stops.",
        _ => "Click the first point or press Enter to close the shape. Backspace takes the last point back, Esc stops."
    };

    // ---------------------------------------------------------------- My Assets

    /// <summary>The kept shape the target's outline is an unmodified copy of, if My Assets still has it.</summary>
    private ShapeAsset? KeptShapeOfTarget
    {
        get
        {
            if (OwnShapeTarget is not { } target || TargetShape(target) is not { } shape || ShapeAsset.IdOf(shape) is not { } id)
                return null;
            return Library?.MyAssets?.FindShape(id);
        }
    }

    private ModuleShape? TargetShape((ModulePart Part, LimbPiece? Piece) target) =>
        Working.Modules is not { } body ? null
        : ModuleEditing.ShapeOf(body, target.Part) is { } module ? module.Shape
        : ModuleEditing.LimbOf(body, target.Part) is { } limb ? ModuleEditing.ShapeOf(limb, target.Piece ?? LimbPiece.End)
        : null;

    /// <summary>Keep in My Assets is offered: there's a My Assets, the part has an outline of its own, and it isn't kept already.</summary>
    public bool CanKeepShape => Library?.MyAssets is not null && OwnShapeTarget is { } target && TargetShape(target) is not null && KeptShapeOfTarget is null;

    public string KeepShapeTip =>
        Library?.MyAssets is null ? "My Assets isn't set up"
        : KeptShapeOfTarget is { } kept ? $"Kept in My Assets as {kept.Name} - it's under My shapes in every comic"
        : OwnShapeTarget is { } target && TargetShape(target) is null ? $"Pick a shape for {OwnShapeNoun} first - a round one is the limb's own"
        : $"Keep in My Assets - {OwnShapeNoun}'s shape goes under My shapes in every comic's shape galleries";

    /// <summary>
    /// Keeps the target's outline in My Assets as a new shape named after the character and the
    /// part ("Bob's head"), and records in the comic that the part's shape is that one (one undo
    /// step; My Assets keeps it even if that's undone). False, with the reason, if it can't.
    /// </summary>
    public bool KeepShapeInMyAssets()
    {
        if (Library?.MyAssets is not { } myAssets || OwnShapeTarget is not { } target || Working.Modules is not { } body)
            return false;
        if (OwnShapeEditing.OutlineToKeep(body, target.Part, target.Piece) is not { } outline)
        {
            ShowMessage(KeepShapeTip);
            return false;
        }
        var noun = target.Piece is null ? target.Part.Noun() : target.Part == ModulePart.Legs ? "foot" : "hand";
        ShapeAsset kept;
        try
        {
            kept = myAssets.KeepShape(myAssets.NextShapeName($"{Working.Name}'s {noun}"), outline);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Couldn't keep it in My Assets: {e.Message}");
            return false;
        }
        EditModules(OwnShapeEditing.Link(body, target.Part, target.Piece, kept.Source, AssetFingerprint.ShapeFingerprint(kept)));
        ShowMessage($"Kept in My Assets as {kept.Name} - it's under My shapes in every comic's shape galleries; rename it on File › My Assets.");
        return true;
    }

    /// <summary>The kept shapes as a gallery's My shapes section, each fitted to the part's box; null with none (or no My Assets).</summary>
    /// <param name="flipX">The part's flips, for a head or torso: its choices are drawn as it'd wear them.</param>
    private ModuleShapeSection? MyShapesSection(ModulePart part, LimbPiece? piece, ModuleShape? current, double aspect, Avalonia.Media.IBrush fill, string noun, bool flipX = false, bool flipY = false)
    {
        if (Library?.MyAssets?.Shapes() is not { Count: > 0 } kept)
            return null;
        var currentId = current is null ? null : ShapeAsset.IdOf(current);
        var choices = new List<ModuleShapeChoice>();
        foreach (var shape in kept)
        {
            ModuleShape icon;
            var iconAspect = aspect;
            if (piece == LimbPiece.End)
                icon = shape.Shape;
            else if (ModuleOutlines.Fit(shape.Shape.Anchors) is { } fitted)
            {
                icon = ModuleSockets.Flipped(fitted.Shape, flipX, flipY);
                if (piece is null)
                    iconAspect = fitted.Aspect;
            }
            else
                continue;
            var isCurrent = currentId == shape.Id;
            choices.Add(new ModuleShapeChoice(shape.Name, part, piece, null, icon, iconAspect, fill, isCurrent,
                isCurrent ? $"{shape.Name} - {noun} now" : $"Make {noun} your {shape.Name} - from My Assets; its colour and place stay", shape));
        }
        return choices.Count == 0 ? null : new ModuleShapeSection("My shapes", choices);
    }

    /// <summary>The name of the kept shape <paramref name="shape"/> is a copy of, for a gallery's face; null if it isn't one (or My Assets hasn't it any more).</summary>
    private string? KeptShapeName(ModuleShape? shape) =>
        shape is not null && ShapeAsset.IdOf(shape) is { } id ? Library?.MyAssets?.FindShape(id)?.Name : null;

    /// <summary>A My shapes pick: the kept outline copied into the part (the comic keeps its own copy, marked with the fingerprint it matches), one undo step.</summary>
    private void PickKeptShape(ModuleShapeChoice choice, ShapeAsset kept)
    {
        if (Working.Modules is not { } body || ModuleOutlines.Fit(kept.Shape.Anchors, sockets: kept.Shape.Sockets) is not { } fitted)
            return;
        var copy = fitted with { Shape = fitted.Shape with { Source = kept.Source, MyAssetsVersion = AssetFingerprint.ShapeFingerprint(kept) } };
        EditModules(choice.Piece is { } piece
            ? OwnShapeEditing.SwapOwnShape(body, choice.Part, piece, copy)
            : OwnShapeEditing.SwapOwnShape(body, choice.Part, copy, asDrawn: false));
    }

    /// <summary>My Assets changed (something kept, renamed or removed, here or in another comic): the galleries' My shapes and Keep follow.</summary>
    internal void OnMyAssetsChanged()
    {
        OnPropertyChanged(nameof(ModuleShapeGallery));
        OnPropertyChanged(nameof(LimbEndGallery));
        OnPropertyChanged(nameof(LimbUpperGallery));
        OnPropertyChanged(nameof(LimbLowerGallery));
        RaiseOwnShapesChanged();
    }

    private void RaiseOwnShapesChanged()
    {
        OnPropertyChanged(nameof(CanKeepShape));
        OnPropertyChanged(nameof(KeepShapeTip));
        OnPropertyChanged(nameof(ImportShapeTip));
        OnPropertyChanged(nameof(DrawShapeTip));
        ImportShapeCommand.NotifyCanExecuteChanged();
        DrawShapeCommand.NotifyCanExecuteChanged();
        KeepShapeInMyAssetsCommand.NotifyCanExecuteChanged();
    }

    private void RaiseDrawingChanged()
    {
        OnPropertyChanged(nameof(IsDrawingShape));
        OnPropertyChanged(nameof(DrawShapeTip));
        DrawShapeCommand.NotifyCanExecuteChanged();
        RaiseDrawnShapeChanged();
    }

    private void RaiseDrawnShapeChanged()
    {
        OnPropertyChanged(nameof(DrawnShape));
        OnPropertyChanged(nameof(CanCloseDrawnShape));
        OnPropertyChanged(nameof(Hint));
    }
}
