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

    // The "Click to edit ..." hint shown while the pointer is over a compared character -
    // kept so leaving it clears only that hint, never a message something else put up.
    private string? _lineUpHint;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    /// <summary>How far (pixels) the pointer goes before a press on drawn art becomes a move - so a click to select never nudges it.</summary>
    private const double DragThreshold = 3;

    public CharacterEditorView()
    {
        InitializeComponent();
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
            if (_artDrag is { } drag)
            {
                if (!_artMoving && Math.Abs(p.X - drag.Pressed.X) < DragThreshold && Math.Abs(p.Y - drag.Pressed.Y) < DragThreshold)
                    return;
                _artMoving = true;
                var now = drag.Placement.ToFigure(new(p.X, p.Y));
                vm.UpdateArtDrag(new(now.X - drag.Start.X, now.Y - drag.Start.Y));
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

    private void ClearLineUpHint(CharacterEditorViewModel vm)
    {
        if (_lineUpHint is null)
            return;
        Stage.Cursor = Cursor.Default;
        if (vm.Hint == _lineUpHint)
            vm.ShowMessage(null);
        _lineUpHint = null;
    }

    private void EndDrag(IPointer? pointer)
    {
        if (_artDrag is null)
            return;
        _artDrag = null;
        pointer?.Capture(null);
        (DataContext as CharacterEditorViewModel)?.EndArtDrag();
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public CharacterFigure Figure => Stage;
}
