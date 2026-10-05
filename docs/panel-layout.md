# Panel layout — what a panel's contents do when it moves or resizes

**Status: implemented** (issue #58). Resizing a panel scales everything in it as
one picture; moving it carries everything along. This doc covers how the layout
operations treat a panel's contents. It doesn't cover how panels are drawn, how
gutters and snapping find their targets, or the Layers pane (docs/layers-panel.md).
See `CLAUDE.md` for the project's overall priorities.

## 1. The problem, from the user's side

Issue #58 (translated): "Crazy things happen when you resize panels with something
in them. Hard to pin down a rule, but characters dive and the composition falls
apart. Different things move by different amounts. Best if everything just stayed
where it was." The repository owner's direction for the fix: *scaling panels
should scale their contents*.

Before the fix, each kind of content followed its panel by its own rule. Each one
kept its size, while one reference point was mapped proportionally on each axis:
a character's feet (`Ground`), a bubble's centre, an element's centre. A waist-up
character's feet sit *below* the frame, so its reference point moved by a
different amount than the bubble's above its head. Pulling a panel's bottom edge up
dragged the characters up past their bubbles. Narrowing a panel piled its bubbles
onto each other.

## 2. Decisions

- **One transform for everything in the panel** (`PanelContentScale` in
  `Stanley.Editing`): a uniform scale plus a shift. Characters, bubbles and their
  tail tips, drawings, text, pictures, speed lines, groups and a thought cloud's
  trail all go through it. That way relative positions can't drift, whatever each
  thing anchors on.
- **The scale is `min(widthRatio, heightRatio)`**, which is "fit". Everything that
  was in frame stays in frame and nothing is squashed. So:
  - a corner drag scales the picture;
  - narrowing a panel shrinks the picture to fit;
  - widening it one way leaves the picture its size and just adds room.

  We rejected "cover" (the larger ratio) because it crops. Bubbles have to stay
  inside their panel, so cropping would force them to be pushed back in, which
  breaks the composition again.
- **Where the picture sits** (`PanelContentScale.Between`, per axis):
  - It holds onto the edge the resize didn't move: drag the right edge, and things
    stay put against the left. This mirrors `PictureEditing.Pin`.
  - Where neither or both edges moved, it is **centred across** and stands on the
    **floor** (bottom-aligned). Characters keep their footing, for the same reason
    `CharacterPlacement.Ground` is a character's anchor.
- **Sizes scale, line weights don't.** A character's `UnitHeightMm`, a bubble's
  outline and lettering (`FontSizePt`), and a text's box, letters and explicit
  letter outline (`OutlineWidthMm`) all scale. A shape's `StrokeWidthMm`, a text
  box's `BoxStrokeWidthMm` and speed lines' `WidthMm` stay as they were. Ink is
  drawn the same thickness at any size, as character outlines (`strokeMm`) and
  bubble borders (from the style preset) already are.
- **Lettering scales with its bubble.** This trades lettering-size consistency for
  the bubble looking identical, just smaller. Keeping 10pt letters in a halved
  bubble would spill the text. A user can set the size back in Home › Font, and the
  bubble grows to fit.

## 3. How it fits together

- `PanelLayoutEditing.Resize(panel, newBounds, page)` is the one place a panel's
  contents follow it. It computes
  `PanelContentScale.Between(oldBounds, newBounds)` and then:
  - `CharacterPlacementEditing.Scale` on every character;
  - `BubbleEditing.Scale`, then `KeepInside`, on every bubble;
  - `ElementEditing.Scale`, then `KeepReachable`, on every element
    (`TextEditing.Scale` for text);
  - `ThoughtCloudEditing.ScaleTrail` on the trail.
- Every other operation reaches it through `Resize`:
  - `Move` is a resize to the same size, so the scale is exactly 1.
  - `DragBoundary` resizes each panel on both sides of a gutter.
  - `MoveMargin`, the layout presets (`PageEditorViewModel.ApplyLayoutPreset`),
    pasting a whole panel, and creating a panel all go through it too.
- `Split` is different on purpose. Each thing goes to the half it's in and stays
  where it was on the page; nothing scales.
- The per-item `Refit` functions (`BubbleEditing.Refit`, `ElementEditing.Refit`,
  `CharacterPlacementEditing.Refit`) are **not** for panel resizes any more. They
  bring one pasted item into a *different* panel (`PageEditorViewModel.Clipboard`):
  same size, same relative spot. A pasted character shouldn't shrink because the
  panel it lands in is smaller.

## 4. Gotchas

- **A drag is reversible; separate gestures may not be.** The page editor computes
  every `Update*` from `Committed`, so within one drag, shrinking and growing back
  restores everything exactly.
  - Across two gestures, fit is one-way. Narrow a panel (the picture shrinks), then
    widen it back by the same edge (scale 1, just room): the picture stays small.
    Undo restores it. A corner drag back to the old size does scale it back up
    (test `Shrinking_a_panel_and_growing_it_back_puts_everything_back`).
  - Fixing this would need a panel to remember its "natural" framing. That's a
    model change we haven't made.
- **Rounding noise.** `DragBoundary` rebuilds untouched sides with
  `Rect2D.FromEdges`, which can round a size off by an ulp. `Between` snaps a scale
  within 1e-9 of 1 to exactly 1, and the per-item `Scale` functions skip font
  changes at scale 1. Without the snap, every bubble in a panel that only got wider
  would be saved as `9.999999999999998pt`. "Kept edge" detection uses a 1e-6 mm
  tolerance for the same reason.
- **Map anchors point by point.** `PanelContentScale.Map(anchors)` maps each point
  and handle itself. `AnchorRing.Rescale` returns a ring with a zero-width or
  zero-height box unchanged, so a straight line wouldn't move.
- **Clamps:** `UnitHeightMm` stays within
  `CharacterPlacementEditing.Min/MaxUnitHeightMm`, and font sizes within Word's 1–1638pt
  (`TextEditing.ScaleFontSize`). Below those limits the composition stops scaling
  exactly. A bubble whose scaled size comes out at exactly the default 10pt is
  stored as null again, so its file doesn't mention a size.
- Bubbles still go through `KeepInside` and elements through `KeepReachable` after
  scaling. Under fit both are no-ops for anything that was inside the old panel.
  They only matter in degenerate cases, such as an element barely overlapping the
  panel that shrinks below `MinVisibleMm`.
