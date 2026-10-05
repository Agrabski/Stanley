# Shape editing — Edit Points and the Freeform tool

**Status: implemented** (issue #84). A drawn shape can be reshaped point by point
(**Edit Points**), and a new one built point by point (Insert › **Freeform**). This
doc covers how both fit into the page editor. It doesn't cover the other drawing
tools (Draw, Line, Rectangle, Ellipse, which only *produce* shapes), styling
(Shape Fill / Shape Outline), or panels' and bubbles' outlines, which use the same
anchor model but can't be point-edited yet (§6). See `CLAUDE.md` for the
project's overall priorities.

## 1. The problem, from the user's side

Issue #84 (translated): "A fairly approachable editor for making your own shapes
would add a lot. For example Figma's, where you can add points and move them to
change the shape." Before this, a shape was whatever its tool drew: a rectangle,
an ellipse, a straight line or a smoothed freehand stroke. The only edits were
move, resize and restyle.

## 2. Decisions

- **No new data.** Every drawn shape already is a list of bezier anchors
  (`ShapeElement.Anchors`: `ShapeAnchor` = point, in handle, out handle,
  `Smooth`/`Corner`) plus `Closed`, in page millimetres. Editing points only
  rewrites that list, so the file format, undo, clipboard, grouping, My Assets
  and panel resizing all carry on unchanged, and a rectangle, an ellipse and a
  scribble edit alike.
- **Figma's gestures, PowerPoint's names.** The user-facing model is Figma's
  vector editing, which the issue asks for. The words are Office's, like the
  rest of Stanley's ribbon: "Edit Points", "Freeform", "Smooth point", "Corner
  point", "Close shape", "Open at point".
  - Double-click a shape (or Enter, or Shape › Edit Points, or right-click ›
    Edit points) to edit its points. Esc or Enter is done. So is selecting
    anything else or picking another tool.
  - Drag a point to move it, with its handles. Shift keeps it level, upright or
    at 45° from where it was.
  - Press on the outline to add a point there, and keep dragging to pull it out
    straight away (PowerPoint's behaviour). Released without moving, the point is
    just added. Either way it's one undo step.
  - Click a point to select it. Its curve handles show as round dots on stalks.
    Drag one to bend the curve. On a smooth point the opposite handle swings
    round to stay in line, keeping its own length (Figma's "mirror angle").
    Alt bends just that side and makes the point a corner. Shift snaps the
    handle to 45°.
  - Double-click a point to toggle it between smooth and corner. Making it
    *corner* folds both handles onto the point, so the outline turns sharply
    there. Making it *smooth* lines up handles that are already pulled out, or
    else aims new ones along the line between its neighbours, a third of the
    way to each. That's the same rounding the freehand pen's `ShapeEditing.Smooth`
    gives.
  - Delete or Backspace removes the selected point, never the whole shape while
    editing. Arrows nudge it. A closed shape keeps at least three points and a
    line two.
- **Adding a point never changes the outline.** On a curved edge it's a de
  Casteljau split: the new point is smooth, and its neighbours' handles shorten
  along their own direction. On a *straight* edge (both handles on their points)
  it's a corner with its handles on it. Pulling it out then makes two straight
  edges: a rectangle's side becomes a roof, not a bulge.
- **Corners show no handles.** Only handles that are pulled off their point *and*
  shape an existing edge are drawn or grabbable (`ShapePointEditing.VisibleHandles`).
  An open line's ends have one each. To curve a corner, make it smooth first. That
  keeps a rectangle in edit mode down to four squares instead of twelve dots.
- **Freeform builds point by point.** Each click places a corner. Dragging as you
  place one makes it smooth, its out handle on the pointer and its in handle
  mirrored (`ShapePointEditing.Placed`, as Figma's pen does). Click the first point
  again (with three or more placed) to close the shape. Click the last point,
  double-click, Enter, Esc or right-click to finish it as a line. Backspace takes
  the last point back. Shift keeps the next point at 45° steps from the last.
  Switching tools mid-shape keeps what's placed. The finished shape is one undo
  step. Like Line/Rectangle/Ellipse, it hands back to Select with the shape
  selected. The tool's keyboard shortcut is **F**, except with a character
  selected, where F stays "front view".
- **The box follows the curves.** `PanelElements.Bounds` for a shape is
  `AnchorRing.CurveBounds` (the tight bezier bounds, using the roots of each edge's
  derivative), not the anchor points' box. A handle can bow an edge well past its
  points, and the selection box, resize, "keep reachable" and paste placement should
  all match what's drawn. `ShapeEditing.Resize` maps from the same box, so dragging
  a resize handle lands the *outline* on the new box. For rectangles, ellipses and
  lines the two boxes are identical.

## 3. How it fits together

- **`ShapePointEditing`** (`Stanley.Editing`) holds the pure functions:
  `MovePoint`, `MoveHandle`, `InsertPoint`, `DeletePoint`, `SetPointKind`, `Close`,
  `OpenAt`, plus `Nearest` / `PointOn` (outline geometry for hit testing),
  `VisibleHandles`, `Placed` / `Freeform` (the Freeform tool) and `SnapAngle`.
  Everything returns `EditResult<ShapeElement>` and is unit-tested in
  `ShapePointEditingTests`.
- **`PageEditorViewModel.Points.cs`** holds the mode and its edits.
  - The mode is `_editingPointsId` (an `ElementId`, so a To front / To back that
    changes the shape's index keeps editing on) plus `_selectedPointIndex`.
    `IsEditingPoints` is *derived*: true only while that id is the single selected
    shape. `OnSelectionChangedForPoints` (called from `RaiseSelectionChanged` /
    `RaiseMultiSelectionChanged`) and `DropStalePointEditing` (from the `Working`
    change handler, for undo) drop it when the selection moves on, and let go of
    a point index an undo removed. The `Tool` setter ends it for any tool but
    Select.
  - Drags follow the usual gesture pattern: `BeginMovePoint` / `UpdateMovePoint`
    and `BeginMoveHandle` / `UpdateMoveHandle`, each an absolute target computed
    from the baseline, one undo step per drag. `BeginInsertPoint` follows the
    Alt+drag-copy pattern instead: it puts the document *with the new point* in
    `_moveBase`, so `UpdateMovePoint` works from it and a cancel drops the point.
    It reuses `_duplicateCancelled` to deselect the point on cancel.
  - One-step edits (`SetPointKind`, `TogglePointKind`, `DeleteSelectedPoint`,
    `NudgeSelectedPoint`, `AddPoint`, `CloseEditedShape`, `OpenEditedShapeAt`) go
    through `ApplyToEditedShape`. It skips edits that change nothing (a corner made
    a corner again) so they don't leave an empty undo step.
  - `AddFreeformShape` turns the canvas's placed anchors into the shape, in the
    pen style and layer (`_newShapeStyle`, `_newShapeLayer`).
- **`PageCanvasControl`**:
  - `HitTest` asks `PointAt` first while editing: the selected point's handles,
    then the nearest point, then the outline (within `OutlineHitPx`). Everything
    else keeps its usual priority, except that the edited shape's eight resize
    handles are suppressed, since a rectangle's corners *are* its resize handles'
    spots.
  - The new `DragKind`s are `PendingMovePoint` → `MovePoint` (a click on a point
    only selects it, no undo step), `MoveHandle`, `InsertPoint` and
    `PlaceFreeformPoint`.
  - The Freeform draft (`_freeform`, `_freeformPanel`, `_freeformPointer`) lives
    in the canvas, not the document. A multi-click shape can't be one live
    gesture, because ribbon commands would `Apply` in the middle of it. So it's
    previewed through the scene and committed with one `Apply` at the end.
    `FinishFreeform` clears the draft *before* adding the shape, because adding it
    switches to the Select tool, and the `Tool` change handler would otherwise
    finish it twice.
- **`PageCanvasDrawOperation`**:
  - `DrawPointEditing` draws the outline as a 1px accent line (it shows even for a
    shape with no outline). Each point is a square for a corner or a circle for
    smooth, filled in when selected. The selected point gets its handles on
    stalks. A hollow dot marks where a click would add a point
    (`PageCanvasScene.PointGhost`, set on hover).
  - `DrawFreeform` draws the draft's points, the dashed edge out to the pointer
    (drawn as the curve the next click would make), and a ring on the first
    point when a click there would close the shape. The draft itself is drawn in
    page space, clipped to its panel, and filled when it's about to close.
- **Ribbon**:
  - Shape tab › **Points** group: the Edit Points toggle (`IsEditingPoints`, enabled
    by `CanEditPoints`), Smooth / Corner radio toggles (`IsSelectedPointSmooth` /
    `IsSelectedPointCorner`), Delete point, Close shape, Open at point.
  - Insert › Shapes › **Freeform** (`IsFreeformTool`).
  - Icons: `FreeformIcon` (a pen nib), `EditPointsIcon`.
- **Right-click menu**: on a point it offers Smooth point, Corner point, Delete
  point, Open the shape here, Close the shape and Done editing points. On the
  outline it offers Add point. The shape's own menu gains Edit points / Done
  editing points.

## 4. Gotchas

- `ShapeElement.Closed` decides both filling and the closing edge. A closed shape
  with fewer than three anchors draws as an open line (`ElementRenderer.ShapePath`),
  so `DeletePoint` and `Close` enforce `MinPoints`.
- An open line's first in-handle and last out-handle are never drawn. Nothing
  edits them on purpose. `OpenAt` and `SetPointKind` leave them on their point.
- `EditElementInPanel` applies `ElementEditing.KeepReachable` after every edit. A
  point dragged far out of the panel only shifts the whole shape once its whole
  box has left (less than `MinVisibleMm` overlapping).
- `KeyGesture.Parse` doesn't know "Esc" or "Del". The canvas's `Item` helper maps
  both to Avalonia's names.

## 5. Tests

- `tests/Stanley.Editing.Tests/ShapePointEditingTests.cs`: the pure operations,
  including that inserting a point leaves the outline unchanged.
- `tests/Stanley.ProjectModel.Tests/AnchorRingCurveBoundsTests.cs`: the curve
  bounds.
- `tests/Stanley.Editors.Tests/EditPointsTests.cs`: the mode, gestures, undo
  steps and Freeform commit.
- `tests/Stanley.App.HeadlessTests/EditPointsUiTests.cs`: double-click, drags,
  keys, the ribbon and the right-click menu through the real window.

## 6. Not done yet

- **Panels and bubbles.** `PanelShape` and `BubbleShape` are the same anchor
  rings. Editing their points would give custom panel shapes, but every layout
  operation (gutters, snapping, splitting, `PanelContentScale`) assumes a panel's
  box. Bubbles regenerate their outline from their style on resize.
- **Selecting several points at once** (rubber band, Shift+click) to move them
  together.
- **Bending an edge by dragging it** (Figma's bend tool). For now, pressing the
  outline adds a point. Bends come from handles.
- **Snapping** a dragged point to the grid, guides or the shape's other points.
