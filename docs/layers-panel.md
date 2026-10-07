# Layers panel — what's in front of what

**Status: implemented** (issue #17, slices 1–4 of §8). A list of the page's
panels and what each holds, to pick overlapping things and move them in front
of or behind each other - bubbles, characters and drawings alike. It came with a
change to how a panel stacks: three fixed tiers became one stack the user can
arrange.

Scope: how a panel orders its bubbles, characters and drawn elements, how that
order is saved, and the pane that shows and changes it. Not how any of those
things are drawn, posed or placed. See `CLAUDE.md` for the project's overall
priorities.

## 1. The problem, from the user's side

Issue #17 (translated): "A program like this must have a layers menu. It makes
work much easier when objects overlap", with ComiPo's layer list as the model: a
tree of the page's panels, each opening to what it holds, with the selection
highlighted and buttons to move it forward or back.

In their words:

- "I can't click the bubble - the character is in the way." / "I can't click the
  character - the bubble is on top."
- "I want the speech bubble *behind* the character's arm, not in front."
- "Which of these three shapes is the one I want?"

Before this, a panel drew in fixed tiers - drawings behind the characters, the
characters, drawings in front of them, then the bubbles - so a bubble could never
go behind a character, and a drawing could only be behind *all* the characters or
in front of all of them.

## 2. Decisions

- **One free-form stack per panel**, not a richer set of fixed tiers: bubbles,
  characters and drawings interleave in any order. A panel stays "automatic" (the
  tiers above, no new data) until the user arranges it in the Layers pane.
- **A right-hand dock**, so the pane is visible together with the Pages thumbnails.
  **On by default** (#161: a tester only found it by hunting through View - a pane you
  have to know about is one most people never see); View › Panes › Layers hides it,
  remembered per user (`AppSettings.ShowLayers`, true unless the file says `False`).
  The key is only written once someone flips the switch, so an older profile that never
  touched it gets the new default; one that turned the pane off keeps it off.
- **Front first, top to bottom** - Office's Selection Pane convention, and "move up"
  means "towards the front". (ComiPo's screenshot lists back-first.)
- **Panels in reading order** (`PageDocument.PanelOrder`). A panel's position there is
  *also* its z-order and its reading order, and re-tiling rebuilds it, so the pane
  doesn't reorder panels.
- **The pane follows the current page** only.
- **To front / To back** (ribbon, right-click) keep their old meaning in an automatic
  panel - front of its own kind of thing, bubbles still on top - so nothing changes
  for anyone who never opens the pane. In a panel arranged by hand they mean the
  very front / back of its whole stack, as in the pane. The pane makes the real
  order visible, which is what makes that safe.

## 3. The stacking model

### 3.1 The usual order

Back to front: the panel's background, drawings with `Layer = Background`, the
characters (`CharacterInstances` order), drawings with `Layer = Foreground`, the
bubbles (`Bubbles` order). Within a list the end is the front. Panels themselves
draw in `PanelOrder`, a thought cloud last so it floats on top.

### 3.2 `Panel.Stack`

`IReadOnlyList<string>?`, back to front: every bubble, character and element of
the panel as a token - `b:<BubbleId>`, `c:<CharacterInstanceId>`, `e:<ElementId>`.
Null (and absent from the JSON) until the user arranges the panel; an empty list
is read as null. Plain strings, not typed ids, so one bad token can never stop a
panel reading. The kind prefix keeps a bubble and an element that happen to share
an id apart.

Characters had no id (they're addressed by index), so `CharacterInstance.Id`
(`CharacterInstanceId?`, written only when set) is minted **lazily** - when a
stack is first written, never on load, so opening a comic dirties no files.
`Clippings.Copy` never carries it over.

### 3.3 Reading it — `PanelStack.Order`

`Stanley.ProjectModel/Issues/PanelStack.cs`; returns `StackItem(Kind, Index)`
back to front, the index being the item's place in its list - the same addressing
the editor's selection uses. Reconcile on read:

1. Tokens that name nothing are skipped; a token met twice counts once; a token two
   items share belongs to the first of them in the usual order.
2. Anything the list doesn't name (a bubble added since, a character with no id) is
   put just in front of its immediate predecessor in the usual order - or at the
   back if it has none. So a new bubble lands in front of the frontmost bubble,
   wherever that has been put.

Because of that, **nothing that adds, copies, deletes or merges items has to keep
`Stack` up to date** - the ~12 places that append to a list, delete, paste,
duplicate or Alt+drag are untouched. Only what *moves* things in the stack writes it.

### 3.4 Writing it — `PanelStackEditing`

`Stanley.Editing/PanelStackEditing.cs`, pure functions in the `ElementEditing`
style; an edit that changes nothing returns the very `Panel` it was given, so the
caller skips the undo step.

- `Move(panel, item, StackMove)` - one place forward/back or all the way, past
  whatever kind of thing is next. Works on the order as drawn now, so the first move
  in an automatic panel writes the whole stack.
- `Arranged(panel, order)` - writes `Stack`, gives every character an id, and gives
  two things that share an id a new one (only a hand-edited or merged file can), so
  the order written is the order read back.
- **`ElementLayer` is kept in step**: after every write a drawing is flagged
  Foreground if it's above every character, Background if below every one, and left
  as it was if between two. The flag still drives the ribbon's Behind / In front
  toggles and `Grouping.Group`'s "same layer" rule.
- `MoveBeyondCharacters` - "In front of / Behind the characters" in an arranged panel.
- `Grouped` / `Ungrouped` - a new group stands where its frontmost member was; its
  children take the group's place when it's taken apart.

A move changes only `Stack` (plus character ids and drawings' layer flags), never
the order of the three lists. So **no index changes**: the selection, a drag in
progress and the text editor all stay valid, and no re-pointing is needed.

### 3.5 What else knows about it

- `Clippings.Copy(Panel)` renames the stack's tokens to the copies' new ids.
- `PanelLayoutEditing.Split` gives the second half the same `Stack`; tokens of what
  stayed behind are ignored.
- Git: written only when set, keys sorted like everything else (`stack` between
  `shape` and `trail`). An older build ignores `stack` and the character `id`, draws
  the usual order, and drops them on its next save - no corruption.

## 4. Who walks the order

One function, three consumers - so what's drawn, what's listed and what a click
gets can't disagree:

- **Rendering** - `PageRenderer.DrawPanels` draws the background, then
  `PanelStack.Order`. Thumbnails, PDF and PNG export go through the same function.
- **Picking** - `PageCanvasControl.HitTest`: the selected item's handles first, then
  panel corners and gutters, then `StackedAt`: panel by panel from the top one down,
  and within a panel from the front of its stack - bubble bodies (the outline plus
  tails), characters (their silhouette), drawings in front of the characters. Panel
  edges come next, and `SceneryAt` (drawings behind the characters) last, so scenery
  covering a panel never stops its edges being dragged. (This used to scan whole
  tiers across *all* panels, so a lower panel's bubble beat a thought cloud's own
  character; it's per panel now.)
- **The Layers pane** - `LayersViewModel`.

## 5. The pane

`Stanley.Editors/Layers/`: `LayersViewModel` (a dock `Tool`), `LayersView`,
`LayerRow`, `LayerLabels`, `LayersPaneMemory`.

- Rows: each panel (reading order) as a header, then - if open - its layers front to
  back, then "Background" (never movable). A panel opens when anything in it is
  selected, from the page or the pane, and the arrow on its row closes it.
- Labels have no names to draw on: a bubble is its style and what it says, a
  character its name (`CharacterSnapshot`, "Missing character" if the comic lost
  it), a text its words, shapes/pictures/speed lines/groups their kind.
- A click selects on pointer *release* (as the page navigator does), Shift+click
  toggles, a panel or background row selects the panel; then the keyboard goes to
  the page (`FocusPage`) so Delete and the arrow keys act on what was picked. Rows
  highlight themselves (`LayerRow.IsSelected`) rather than through a list box's
  selection, because Shift+click selects several.
- The highlight follows the page's selection (`SelectedPanelId`, the three
  `Selected*Index` properties, `SelectionCount`); the rows are rebuilt on
  `Committed` - not `Working`, which changes on every frame of a drag.
- Four buttons above the list: to front, forward, backward, to back, acting on the
  primary selected layer. A button that can't act is disabled and its tooltip says
  why (`ToolTip.ShowOnDisabled`). One undo step each.

## 6. The right-hand dock

- `EditorDockHost.CreateLayout` builds `[LeftTools | Editors | RightTools]` (dock ids
  `MainRow`, `RightTools`); `EditorWorkspace.SetRightToolsVisible` adds or
  removes the right dock and its splitter at runtime.
- **Gotcha - proportions drift.** Dock re-normalises on every layout, so without
  remembering each column's width a hidden-then-shown pane comes back ~13% narrower
  every time. The workspace captures the proportions when hiding and writes them back
  to both `Proportion` and `CollapsedProportion` when showing.
- The Layers tool turns off pinning, dragging and "dock as document" (`CanPin`,
  `CanDrag`, `CanDockAsDocument`) - there's nowhere else for it to go - and the dock
  takes no drops. A single tool shows a plain header ("Layers"), no tab strip.
- The toggle reaches every page the way the other comic-wide settings do:
  `ILayersPaneHost` (implemented by `LayersPaneMemory`, get/set delegates like
  `HairUpgradeMemory`) handed to each editor by `PageNavigatorViewModel.SyncPages`;
  `PageEditorViewModel.ShowLayers` is what the View tab's checkbox binds to.
  `MainWindowViewModel.Load` plugs `AppSettings.ShowLayers` in.

## 7. Where code goes

| Project | What |
|---|---|
| `Stanley.ProjectModel` | `Panel.Stack`, `CharacterInstance.Id`, `CharacterInstanceId`, `PanelStack` |
| `Stanley.Editing` | `PanelStackEditing`; `Clippings` and `PanelLayoutEditing.Split` carry the stack along |
| `Stanley.Rendering` | `PageRenderer` walks `PanelStack.Order` |
| `Stanley.EditorFramework` | `EditorDockHost`, `EditorWorkspace`: the right-hand dock |
| `Stanley.Editors` | `Layers/*`, `PageEditorViewModel.MoveInStack` (+ `Reorder*` / `SetElementLayer` / group and ungroup redirecting in an arranged panel), `PageCanvasControl.StackedAt` |
| `Stanley.App` | `AppSettings.ShowLayers`, the `DataTemplate`, `MainWindowViewModel.Load` |

## 8. Delivery slices

1. **Right-hand dock and an empty pane**, View › Panes › Layers, remembered. Tests:
   workspace hide/show (columns, one splitter, widths over repeated toggles), headless
   (pane right of the page, room given back).
   *As built:* as above; the Dock behaviour was checked in the real `DockControl`.
2. **The list**: `PanelStack.Order` (usual order only) and the renderer walking it;
   rows, labels, selection both ways.
   *As built:* the existing render tests passed untouched.
3. **The free-form stack**: the model, persistence, reconcile, rendering and picking
   from it; clipboard and split carry it. Tests: JSON (only when set, old files read
   unchanged, unknown properties ignored), reconcile cases, pixels (bubble behind a
   character, drawing in front of a bubble).
4. **Moving**: `PanelStackEditing`, `MoveInStack`, the four buttons, the older To
   front / To back and "Behind / In front of the characters" in an arranged panel,
   group / ungroup. Also: To front / To back on a bubble or character that is already
   there no longer leaves an empty undo step.
5. **Docs.**

## 9. Later / open questions

- Hide and lock toggles per layer; drag-and-drop to reorder in the list.
- Reordering panels (z-order *is* reading order, and re-tiling rebuilds it).
- Showing a group's children, and moving a multi-selection as a block (a move acts on
  the primary item only).
- Keyboard shortcuts (Ctrl+[ and Ctrl+]), and "Bring forward" in the right-click menus.

## 10. Gotchas for the next person

- `Panel` is a positional record with `with` expressions everywhere: `with { Stack = [] }`
  skips the constructor's "empty means none" tidying, so `PanelStack.Order` also
  treats an empty list as none.
- `PageCanvasControl.cs` doesn't import `Stanley.ProjectModel.Issues` (its `Panel`
  would clash with Avalonia's); qualify the types.
- Adding a character in a test (`CharacterLibraryViewModel.CreateCharacter`) is itself
  an undoable step: give the library its own `EditorHistory` when a test counts the
  page's undo steps.
- A command-disabled Avalonia button still has `IsEnabled == true`; headless tests
  check `IsEffectivelyEnabled`.
- Headless tests share one real settings file for the whole process: any test that
  switches the Layers pane off must switch it back on (`ShowLayers = true` in a
  `finally`), or every later window comes up without it. Every other headless test
  runs with the pane showing, as people see it. A test that needs the true default
  regardless builds its window on a `MainWindowViewModel` with no settings
  (`AppSettings(null)`), as `The_Layers_pane_shows_from_the_start` does.
- The pane's tool buttons are named `BringToFrontButton` / `SendToBackButton`, the same
  as the Bubble tab's: a headless test looking one up by name from the whole window
  gets two now that the pane shows by default - search the ribbon or the pane, not the
  window.
- `LayersPaneMemory` with no getter/setter (`PageEditorHost.CreateWorkspace` without a
  host, i.e. unit tests) starts showing too.
