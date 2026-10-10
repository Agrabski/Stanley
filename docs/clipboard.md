# Clipboard — Copy, Cut, Paste, Duplicate and Alt+drag

**Status: implemented** (single items from the start; a multi-selection since issue
#124). Everything about taking something off a page and putting a copy of it
back: the clipboard itself, how a paste is placed and selected, and what the
copies keep or drop. It doesn't cover how a multi-selection is built
(`PageEditorViewModel.cs`, "selection") or how groups are made
(`docs/asset-packs.md` §3). See `CLAUDE.md` for the project's overall priorities.

## 1. The problem, from the user's side

Issue #124 (translated): "Copy a character and a bubble together with Ctrl+C. It
would be good if copying worked when a bubble and a character are both selected.
Currently duplicating (Alt + left-drag) works in that situation, but copying
doesn't - it only takes the most recently selected item."

Alt+drag already copied the whole multi-selection
(`BeginDuplicateSelection`); Copy, Cut, Paste and Duplicate went through
`SelectedClipping()`, which returned one `Clipping` - the primary item (the one
last clicked).

## 2. The model

- **`Clipping`** (`Stanley.Editing/Clippings.cs`) is one thing taken off a page,
  with the panel it came from and that panel's bounds: `BubbleClipping`,
  `CharacterClipping`, `ElementClipping`, `PanelClipping`, and
  **`SelectionClipping`** - several bubbles, characters and elements selected
  together in one panel. Its three lists are each in the panel's own order (the end
  is the front), whatever order they were clicked in.
- **`PageClipboard`** holds a `Clipping` and the pictures it shows
  (`PicturesIn`; a `SelectionClipping` carries those of all its elements), so a
  paste works in another page, issue or comic. `PageClipboard.Shared` is the one
  every editor uses unless a test gives it another.
- **`PageEditorViewModel.SelectedClipping()`** builds the clipping: a
  `SelectionClipping` when `HasMultiSelection`, else bubble, element, character,
  panel as before. `CanCopy`, Copy, Cut and Duplicate all start there, so the ribbon,
  the shortcuts and the right-click menus need nothing of their own - except that a
  right-click on a member of a multi-selection used to offer only Group, and now
  lists the clipboard commands first (`ClipboardItems` in `PageCanvasControl`).
- **Cut** is Copy then `DeleteSelection()`, which already deletes a whole
  multi-selection as one undo step (`DeleteSelectedItems`).
- **Duplicate** is `PutBack` with `PictureSnapshot` and the clipping's own panel;
  it never touches the clipboard.

## 3. Putting a copy back (`PutBack`)

One `Apply` per paste, so one undo step, and the copies are selected afterwards.

For a single bubble, character or element: `Clippings.Copy`, then `Refit` to the
target panel (same relative spot, same size), then `Clippings.FreeSpot` steps it
aside from same-kind things already at that spot (a character sideways only, along
its floor), then `KeepInside` (bubbles) or `KeepReachable` (the others).

For a `SelectionClipping` (`PutBackSelection`) the same steps, but together:

1. **Characters this comic doesn't have are skipped.** If that leaves nothing,
   the paste is refused with the same message a lone foreign character gets
   (plural for several). If something else is left, it is pasted and the status
   bar says how many characters stayed behind (`ShowNotice`). Chosen over
   refusing the lot: a bubble and a foreign character, pasted into a new comic, are
   still worth having the bubble of.
2. `Clippings.Copy(SelectionClipping)`: fresh ids, characters' stacking id dropped,
   and group links remade (§4).
3. Every copy is `Refit` to the target panel on its own - the single-item rule, so
   it only moves when the target panel is another size, and then each by its
   own relative spot.
4. **One shared offset**, from `Clippings.FreeOffset`: the group's boxes (bubbles,
   characters via `CharacterBounds`, elements via `PanelElements.Bounds`) are stepped
   together until none of their corners is on the corner of anything already in the
   panel, of any kind - so they never come apart and a second paste fans out. Steps
   are `CascadeStepMm` each way, or sideways only when a character is among them.
   It tries forwards, then backwards once the group's combined centre would leave
   the panel, then gives up (0, 0). `Clippings.FreeSpot` is now `FreeOffset` for one
   box.
5. Each copy is moved by that offset (a bubble `withTails`: its tails go with it) and
   then kept inside/reachable exactly as its own single paste would. That last step
   can nudge one item that sits at the panel's edge by itself, the same as a group
   drag does (`MoveSelectedItems`) - the shared offset is only exact while everything
   stays in the panel.
6. Appended to the end of the panel's lists, bubbles, then characters, then elements,
   each in its old order. The copies become the multi-selection
   (`SelectCopies`); the primary item - the one the ribbon follows - is the first
   bubble, else the first character, else the first drawing. (Which of the originals
   was clicked last isn't kept on the clipboard.)

## 4. Groups (`Link`) and stacking (`Panel.Stack`)

- **A copy never joins the original's group.** `Clippings.Copy` on a lone bubble,
  character or element sets `Link = null`. For a `SelectionClipping`,
  things copied together that shared a link get one new link per original link
  (`Clippings.Relinker`, as Alt+drag does), so the copy is a group of its own and the
  original is untouched. A link that only one copied item carries is dropped: a group
  of one is no group (Alt+drag doesn't bother, since selecting one member always
  selects the whole group, so it can't happen from the editor).
- **Stacking needs no upkeep.** `PanelStack.Order` is read forgivingly (see
  `docs/layers-panel.md` §3.3): a copy has a new bubble/element id and a character
  without one, so the panel's `Stack` doesn't name it, and it lands just in front of
  its predecessor in the usual order. Paste leaves `Panel.Stack` untouched, as the
  single-item paths and Alt+drag do. The one thing that is *not* carried over for a
  multi-paste into a hand-arranged panel is the copies' stacking relative to each
  other across kinds (a bubble that was behind a character comes out in front of
  its copy); a whole `PanelClipping` does carry it, since `Clippings.Copy(Panel)`
  renames the tokens.

## 5. Alt+drag

`BeginDuplicateSelection` (and the single-kind `BeginDuplicate*`) add the copies to
the page straight away and start a move gesture on them, so copy and move are one
undo step; Esc takes the copies away and `RestoreSelection` puts the original
selection back. It copies in selection order, not list order, and doesn't refit or
step aside (the copies start exactly on the originals).

## 6. Gotchas

- **`Clipping` records compare by reference for their lists**, like the rest of the
  model (`IReadOnlyList`), so don't `Assert.Equal` two of them in a test.
- **Exact equality of `Refit`ed bounds is luck.** A refit maps through the panel's
  fractions, so a paste back "where it was" differs from the original by ~1e-14;
  compare with a precision.
- **`ToggleSelect` / `SetPrimaryIndex` leave `_extraSelection` alone**, which is what
  `SelectCopies` relies on: `Select(panelId)` first (collapses, and moves the
  selection to the target panel), then the extras, then the primary.
- **`HasMultiSelection` can be a single group.** Selecting one member of a group
  selects all of it (`AddGroupedSiblings`), so copying one linked item copies the
  whole group - that is the `SelectionClipping` path, not a bug.
- A character pasted into a comic without a Characters pane (`_catalog` null, as in
  most view-model tests) always counts as "in this comic"; use a real
  `EditorSession` (`PageEditorHost.CreateWorkspace`) to test the foreign-character
  rules.
