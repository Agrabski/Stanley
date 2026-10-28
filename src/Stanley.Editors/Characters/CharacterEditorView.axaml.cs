using Avalonia.Controls;
using Avalonia.Input;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>The character editor pane; see CharacterEditorView.axaml. Its ribbon is <see cref="CharacterEditorRibbon"/>.</summary>
public partial class CharacterEditorView : UserControl
{
    // Where a drag of drawn art started, and the stage's placement then - held for the
    // whole drag, so the art moving (and the stage re-fitting around it) can't feed back.
    private (Point2D Start, CharacterPlacement Placement, Avalonia.Point Pressed)? _artDrag;
    private bool _artMoving;

    // A drag of the picked head or torso (a body built from shapes): moving it off its socket,
    // or - by a corner - resizing it. The placement and the box are held from the press, as
    // for art, so the stage re-fitting round a growing body can't feed back into the drag.
    private (Point2D Start, CharacterPlacement Placement, Avalonia.Point Pressed, Rect2D? Box, int? Corner)? _moduleDrag;
    private bool _moduleMoving;

    // The "Click to edit ..." hint shown while the pointer is over a compared character -
    // kept so leaving it clears only that hint, never a message something else put up.
    private string? _lineUpHint;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ResizeDiagonalCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor ResizeAntiDiagonalCursor = new(StandardCursorType.TopRightCorner);

    /// <summary>How far (pixels) the pointer goes before a press on drawn art becomes a move - so a click to select never nudges it.</summary>
    private const double DragThreshold = 3;

    public CharacterEditorView()
    {
        InitializeComponent();
        WireOwnShapes();
        // Click a garment on the character to work on it (the Sticker tab); click skin or
        // the background to let it go. Drawn art, once selected, drags to move it.
        // Clicking a compared (faded) character switches to it.
        Stage.PointerPressed += (_, e) =>
        {
            if (DataContext is not CharacterEditorViewModel vm || !e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed)
                return;
            var p = e.GetPosition(Stage);
            if (Stage.LineUpCharacterAt(p) is { } lineUpCharacter)
            {
                ClearLineUpHint(vm);
                vm.Library?.OpenCharacter(lineUpCharacter.Id);
                e.Handled = true;
                return;
            }
            if (vm.IsModular && PressModule(vm, p, e))
                return;
            vm.SelectSticker(Stage.StickerAt(p));
            if (vm.SelectedSticker is { HasArt: true } && Stage.MainPlacement is { } placement && vm.BeginArtDrag())
            {
                _artDrag = (placement.ToFigure(new(p.X, p.Y)), placement, p);
                _artMoving = false;
                e.Pointer.Capture(Stage);
                e.Handled = true;
            }
        };
        Stage.PointerMoved += (_, e) =>
        {
            if (DataContext is not CharacterEditorViewModel vm)
                return;
            var p = e.GetPosition(Stage);
            if (_moduleDrag is { } moduleDrag)
            {
                if (!_moduleMoving && Math.Abs(p.X - moduleDrag.Pressed.X) < DragThreshold && Math.Abs(p.Y - moduleDrag.Pressed.Y) < DragThreshold)
                    return;
                _moduleMoving = true;
                var now = moduleDrag.Placement.ToFigure(new(p.X, p.Y));
                if (moduleDrag is { Box: { } box, Corner: { } corner })
                {
                    // The opposite corner stays put; Shift keeps the proportions.
                    var opposite = corner switch { 0 => new Point2D(box.Right, box.Bottom), 1 => new Point2D(box.Left, box.Bottom), 2 => new Point2D(box.Left, box.Top), _ => new Point2D(box.Right, box.Top) };
                    var fx = Math.Abs(now.X - opposite.X) / Math.Max(box.Width, 1e-6);
                    var fy = Math.Abs(now.Y - opposite.Y) / Math.Max(box.Height, 1e-6);
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        fx = fy = Math.Max(fx, fy);
                    vm.UpdateModuleResize(fx, fy);
                }
                else
                    vm.UpdateModuleNudge(now.X - moduleDrag.Start.X, now.Y - moduleDrag.Start.Y);
            }
            else if (_artDrag is { } drag)
            {
                if (!_artMoving && Math.Abs(p.X - drag.Pressed.X) < DragThreshold && Math.Abs(p.Y - drag.Pressed.Y) < DragThreshold)
                    return;
                _artMoving = true;
                var now = drag.Placement.ToFigure(new(p.X, p.Y));
                vm.UpdateArtDrag(new(now.X - drag.Start.X, now.Y - drag.Start.Y));
            }
            else if (vm.IsModular && Stage.ModuleHandleAt(p) is { } handle)
            {
                Stage.Cursor = handle is 0 or 2 ? ResizeDiagonalCursor : ResizeAntiDiagonalCursor;
                ClearLineUpHint(vm, keepCursor: true);
            }
            else if (vm.IsModular && Stage.ModuleGhostAt(p) is { } ghost)
            {
                Stage.Cursor = HandCursor;
                ClearLineUpHint(vm, keepCursor: true);
                ShowGhostHint(vm, ghost);
            }
            else if (Stage.LineUpCharacterAt(p) is { } hovered)
            {
                Stage.Cursor = HandCursor;
                _lineUpHint = $"Click to edit {hovered.Name}";
                vm.ShowMessage(_lineUpHint);
            }
            else
                ClearLineUpHint(vm);
        };
        Stage.PointerExited += (_, _) =>
        {
            if (DataContext is CharacterEditorViewModel vm)
                ClearLineUpHint(vm);
        };
        Stage.PointerReleased += (_, e) => EndDrag(e.Pointer);
        Stage.PointerCaptureLost += (_, _) => EndDrag(null);
    }

    /// <summary>Leaving the editor takes down the note about what a hairstyle replaced - it's only for the moment after the click.</summary>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as CharacterEditorViewModel)?.DismissHairReplaced();
    }

    private void ClearLineUpHint(CharacterEditorViewModel vm, bool keepCursor = false)
    {
        if (!keepCursor && Stage.Cursor != Cursor.Default && _lineUpHint is null)
            Stage.Cursor = Cursor.Default;
        if (_lineUpHint is null)
            return;
        if (!keepCursor)
            Stage.Cursor = Cursor.Default;
        if (vm.Hint == _lineUpHint)
            vm.ShowMessage(null);
        _lineUpHint = null;
    }

    /// <summary>"Add arms" over a missing part's +, in the status bar like the line-up's hint.</summary>
    private void ShowGhostHint(CharacterEditorViewModel vm, Stanley.Rendering.ModuleGhost ghost)
    {
        var noun = ProjectModel.Characters.ModuleParts.Noun(ghost.Part);
        _lineUpHint = ghost.Side is { } side
            ? $"Click to give them their {side.ToString().ToLowerInvariant()} {noun.TrimEnd('s')} back"
            : ProjectModel.Characters.ModuleParts.IsLimb(ghost.Part) ? $"Click to add {noun}" : $"Click to add a {noun}";
        vm.ShowMessage(_lineUpHint);
    }

    /// <summary>
    /// A press on the stage of a character built from shapes: a corner handle of the picked
    /// head or torso starts resizing it, a missing part's + puts it back, and a click on the
    /// body picks the part under it - unless a sticker is drawn there, which keeps the click as
    /// it always has (a face on a head picks the face; the head round it picks the head).
    /// Pressing a head or torso also starts dragging it off its place. True when handled here.
    /// </summary>
    private bool PressModule(CharacterEditorViewModel vm, Avalonia.Point p, PointerPressedEventArgs e)
    {
        if (Stage.MainPlacement is not { } placement)
            return false;
        if (Stage.ModuleHandleAt(p) is { } corner && vm.SelectedModule is { } picked && Stage.ModuleBounds(picked) is { } bounds && vm.BeginModuleDrag())
        {
            var a = placement.ToFigure(new(bounds.Left, bounds.Top));
            var b = placement.ToFigure(new(bounds.Right, bounds.Bottom));
            StartModuleDrag(placement, p, Rect2D.FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)), corner, e);
            return true;
        }
        if (Stage.ModuleGhostAt(p) is { } ghost)
        {
            ClearLineUpHint(vm);
            vm.ShowMessage(null);
            vm.AddModule(ghost.Part, ghost.Side);
            e.Handled = true;
            return true;
        }
        if (Stage.StickerAt(p) is not null)
            return false;
        vm.SelectModule(Stage.ModuleAt(p));
        if (vm.SelectedModule is null)
            vm.SelectSticker(null);
        else if (vm.BeginModuleDrag())
            StartModuleDrag(placement, p, null, null, e);
        return true;
    }

    private void StartModuleDrag(CharacterPlacement placement, Avalonia.Point p, Rect2D? box, int? corner, PointerPressedEventArgs e)
    {
        _moduleDrag = (placement.ToFigure(new(p.X, p.Y)), placement, p, box, corner);
        _moduleMoving = false;
        e.Pointer.Capture(Stage);
        e.Handled = true;
    }

    private void EndDrag(IPointer? pointer)
    {
        if (_moduleDrag is not null)
        {
            _moduleDrag = null;
            pointer?.Capture(null);
            (DataContext as CharacterEditorViewModel)?.EndModuleDrag();
            return;
        }
        if (_artDrag is null)
            return;
        _artDrag = null;
        pointer?.Capture(null);
        (DataContext as CharacterEditorViewModel)?.EndArtDrag();
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public CharacterFigure Figure => Stage;
}
