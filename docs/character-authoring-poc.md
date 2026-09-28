# Character authoring POC — design

**Status: implemented.** The open questions in §9 were answered: flat cartoon
look, *relative* sizes (no cm), a muscle slider from day one, and every character
in a panel sharing one scale by default. Where this document originally said
otherwise it has been updated; §10 lists what was actually built.

Scope: **body only** (height, weight, proportions) and **placing characters on a
page**. No stickers/art, no posing, no expressions, no revisions UI. The point is to
get characters into the project and onto panels end to end: authored, saved,
rendered on canvas/thumbnails/export, undoable, and editable in one place with the
change showing up everywhere.

This builds on the data model in `docs/character-and-project-plan.md`; where the POC
deviates from or narrows that plan it says so.

## 1. Where the app is today (review)

What already exists and the POC can lean on:

| Area | State | Relevance |
|---|---|---|
| `CharacterDefinition` / `CharacterRevision` / `Sticker` / `Skeleton` / `PoseData` | Persisted records + `ProjectRepository.Load/Save*` | Storage is there; nothing creates, lists or renders characters. |
| `CharacterInstance` (in `Panel.CharacterInstances`) | Persisted, always `[]` | **Has no position, size or flip** — it can't be placed yet. |
| `ProjectRepository` | `LoadCharacter(id)` / `SaveCharacter` | No way to **list** or **delete** characters (the manifest doesn't list them). |
| `ComicProject` | Loads/saves pages and panels of one issue (any issue - the comic can have more than one, switchable and addable) | Ignores `characters/` entirely (Save As copies the folder, so they survive, but that's it). |
| `PageRenderer` | Panels + bubbles clipped to panel | No character layer; would need a character lookup. |
| `EditorWorkspace` / ribbon | One editor in the editor area, ribbon follows `ActiveEditor` by `DataTemplate`, left tool panes | A second editor type (and its ribbon) slots in by design. |
| `EditorHistory` | One stack per project, entries carry a source | Undo of a character edit can switch back to the character editor for free. |
| Bubble editing | `KeepInside`, `Refit`, split-by-centre, index-based selection | Same patterns apply to character instances. |

Gaps the POC has to close: placement data on the instance, a way to draw a body
without art, listing/deleting characters, loading/saving them with the comic, an
authoring surface, and page-editor interactions for instances.

## 2. Decisions

1. **Bodies are parametric, not drawn.** A character's body is a handful of numbers;
   the skeleton rest layout and the silhouette are *generated* from them. With no
   sticker art yet, this is also the only way a character can look like anything.
   It's the "sensible defaults / sliders" path; the stored `Skeleton` becomes a
   sparse override on top (the rig-editor escape hatch, unused by the POC UI).
2. **Relative sizes for the character, mm for the page.** Height is relative to
   an average adult (1.0 = 100%), so what the numbers say is "Bob is a head taller
   than Alice" and it survives any panel size. Each placed instance stores its page
   scale (`UnitHeightMm`: how tall a 100% character is drawn). **Default rule: one
   scale per panel.** A character placed into a panel takes the panel's scale;
   resizing any character resizes every character sharing its scale (each about
   its own feet), so relative heights hold without the user doing anything.
   Shift-resizing just one (foreground/background depth) is the deliberate
   exception, and "Match size" puts it back.
3. **The mannequin renderer is V1's `ICharacterRenderer`.** Per-bone rounded
   capsules + torso + head, unioned with `SKPath.Op(Union)` (the same trick bubble
   tails use), filled with the character's skin colour, stroked in panel ink. Later
   the sticker renderer draws on top of / instead of it; the interface is what
   stays.
4. **Characters are edited in their own editor pane** (not a dialog), listed in a
   **Characters** tool pane beside **Pages**. Same Word-style model as pages:
   click to open, ribbon follows the pane.
5. **Definition-level only.** Body values live on `CharacterDefinition`. Revisions
   and per-instance body overrides are designed-for (§3.3) but not built. Every
   instance references the definition, so editing a character updates every panel
   (the Figma component/instance behaviour the plan asks for).
6. **Rest pose, front and side views.** Each placed character's view is its
   existing `Pose.ViewAngle` (`Front` or `Profile`; no bone rotations yet). The
   side view is generated from the same body numbers - same heights, with the
   width sliders becoming depth (chest, belly, seat) - faces right, and mirrors to
   face left, so two characters can face each other in conversation. Three-quarter
   isn't generated yet (it draws as the front).

## 3. Data model

### 3.1 `BodyShape` (new, `Stanley.ProjectModel/Characters/BodyShape.cs`)

```csharp
/// Source data for a generated body. All values are clamped by BodyShape.Normalized().
public sealed record BodyShape(
    double Height,     // 0.3–1.6, default 1.0 — relative to an average adult
    double Build,      // 0–1,     default 0.3 — "Weight": slim → heavy
    double Muscle,     // 0–1,     default 0.3 — soft → muscular
    double HeadsTall,  // 3–9,     default 7.5 — head size: chibi → heroic
    double Frame);     // 0–1,     default 0.5 — shoulders ↔ hips (V → A silhouette)
```

- `Build` keeps the meaning the plan already gives it (0 slim – 1 heavy), so the
  existing `CharacterRevision.Build` and the future stretch/breakpoint stickers key
  off the same number. The UI labels it **Weight** (0–100). No kg readout: with
  relative heights a weight in kg would mean nothing.
- `Muscle` widens mostly shoulders, chest, arms and thighs; `Build` mostly the
  belly, hips and limbs. Neither ever changes a height.
- `HeadsTall` covers age as well as style: child ≈ 5, adult ≈ 7.5, heroic ≈ 8.5,
  chibi ≈ 3. No separate age parameter.
- **Presets** (`BodyPresets`, same enum + static lookup shape as
  `BubbleStylePresets`/`MetricPaperSizes`): Toddler, Child, Teen, Adult, Heavy,
  Strong, Heroic, Elderly, Chibi. Applying one sets all five values; sliders
  adjust afterwards.

### 3.2 `CharacterDefinition` changes

```csharp
public sealed record CharacterDefinition(
    CharacterId Id,
    string Name,
    BodyShape Body,                 // NEW
    Skeleton Skeleton,              // now: sparse manual override on the generated rest layout (empty in the POC)
    SortedDictionary<string, ColorValue> ColorSlots,        // POC uses "skin" only (lower-case slot name)
    SortedDictionary<string, StickerSlotDefinition> StickerSlots);  // empty in the POC
```

A `character.json` written before bodies existed (no `"body"`) loads with the
default body. `Skeleton` changes meaning from "the rest layout" to "overrides on the generated rest
layout". The plan's "files store source data only" rule argues for this: the joint
positions are derived from `Body`, so they aren't written out. Nothing reads
`Skeleton` today, and there's no file-format version yet (0.x), so no migration is
needed.

### 3.3 Resolution chain (designed, POC implements the last step only)

`instance override → revision (CharacterRevision.Build / ProportionOverride) → definition.Body`.
When revisions become editable, `CharacterRevision.Build` generalises to a sparse
`BodyShapeOverride(double? Height, double? Build, …)`; `CharacterInstanceOverrides`
gets the same for the one-off "bloat gag" panel. A `CharacterResolver` in
Rendering does the resolution so renderer and editor agree.

### 3.4 `CharacterInstance` placement (new field)

```csharp
public sealed record CharacterPlacement(
    Point2D Ground,      // page mm: the point on the floor between the feet
    double UnitHeightMm, // page mm a 100%-height character stands (the panel's "camera distance")
    bool Mirrored);      // flipped horizontally about Ground.X

public sealed record CharacterInstance(
    CharacterId CharacterId,
    CharacterPlacement Placement,   // NEW
    CharacterRevisionId? RevisionOverride,
    PoseData Pose,
    CharacterInstanceOverrides? Overrides);
```

- The anchor is the **ground point**, not a bounding-box corner: resizing scales
  about the feet so the character stays standing where it was, and a "same floor"
  snap is a single y value. The ground may lie below the panel for waist-up shots.
- Page mm (like bubbles), not panel-relative, so it fits how bubbles already work
  and how `Refit` carries them.
- `Panel.CharacterInstances` order is z-order (back to front), like bubbles.
  Instances are addressed by index within their panel, like bubbles.

### 3.5 Repository

- `ProjectRepository.ListCharacters()` — enumerates `characters/*/character.json`
  (sorted by name for display; order isn't meaningful, so no manifest array).
- `ProjectRepository.DeleteCharacter(CharacterId)`.
- Everything else (`SaveCharacter`'s `<id>-slug` folder, sorted keys) already exists.

## 4. Geometry and rendering

### 4.1 `BodyRig` (pure functions, `Stanley.ProjectModel/Characters/BodyRig.cs`)

Same home and style as `AnchorRing`/`PanelShapes`: dependency-free math next to the
data it reads.

- `BodyRig.Build(BodyShape, Skeleton? overrides) → BodyFigure` — rest layout plus shapes, in
  **figure space: unit = relative height, y-down, origin at the ground point** — joints for the required VRM bones (hips, spine,
  chest, neck, head, shoulders, arms, legs, feet; no fingers/toes/eyes).
  `Skeleton` overrides are then applied by bone.
- The shapes: a smoothed torso outline, tapered limb capsules, and ellipses for
  head, hands and feet — widths as functions of `Build`, `Muscle` and `Frame`.
- `BodyRig.Extent(BodyShape) → Rect2D` (figure space) — used by
  hit-testing fallback, selection box, default placement and `KeepReachable`.

Invariants the tests pin down (the exact proportion constants are tuned in the code):
top of head is exactly `Height` above ground; head height is
`Height / HeadsTall`; build and frame change widths only, never heights; the
layout is left/right symmetric; widths are monotonic in `Build`.

### 4.2 Renderer (`Stanley.Rendering`)

```csharp
public interface ICharacterRenderer
{
    SKPath Silhouette(ResolvedCharacter character, CharacterPlacement placement); // page mm, for hit-testing too
    void Draw(SKCanvas canvas, ResolvedCharacter character, CharacterPlacement placement, float strokeMm);
}
```

- `MannequinRenderer`: capsules/ellipses per segment, unioned into one path (cached
  per definition value), filled with the `skin` colour slot, inked at
  `CharacterStrokeMm`. Placement = translate to `Ground`, scale by `UnitHeightMm`,
  `-1` x-scale when mirrored.
- `PageRenderer.DrawPanels` gains a character lookup
  (`Func<CharacterId, CharacterDefinition?>` or a small `ICharacterLookup`) and
  draws, inside the existing panel clip: **background → characters → bubbles**
  (the plan's order). An instance whose character is missing draws a dashed
  placeholder box instead of throwing, so a hand-edited project still opens.
- Canvas, thumbnails, PDF and PNG export all go through this, as they do now.

## 5. Editor & app wiring

### 5.1 Document side

- `ComicProject` loads all characters on `Open`/`OpenRecovered`, and `Save` /
  `WriteCopy` write them (writing an unchanged character is a byte-identical no-op,
  so no dirty tracking is needed) and delete removed ones. Recovery snapshots
  therefore include characters.
- `CharacterLibraryViewModel` (Stanley.Editors, observable; the Characters pane and the `ICharacterCatalog`): the open comic's characters by
  id, `Changed` event. Page editors, thumbnails and the Characters pane all read
  it; a definition change redraws every page that shows that character.

### 5.2 Characters pane (left tool pane, tab next to Pages)

```
┌ Pages │ Characters ┐
│ [fig] Alice  100%  │   ← mannequin thumbnail, name, height, usage
│ [fig] Bob    108%  │
│ [fig] Kid     72%  │
│  + New character   │
└────────────────────┘
```

- Click → opens that character's editor in the editor area (`SwitchTo`, like pages).
- **Drag onto the page canvas → places it** in the panel under the drop point, feet
  at the drop point. (The main placement gesture, direct manipulation.)
- "New character" → creates "Character N" with the Adult preset and opens it.
- Right-click: Rename, Delete. Delete of a character that's placed anywhere is
  refused with "Used in N panels" in the status line for the POC (cross-page undo
  of a cascading removal isn't worth it yet).

### 5.3 Character editor (`CharacterEditorViewModel : EditorViewModel<CharacterDefinition>`)

Pane: the character full height on a height grid (lines every 25% of an average
adult), with the project's **other characters faded behind it in a line-up** so
height comparisons are immediate (toggle on the View tab). Nothing else in the pane:
all controls are on the ribbon.

Ribbon (`CharacterEditorRibbon`, mapped by `DataTemplate` like `PageEditorRibbon`):

- **Body** tab (the only one): *Body type* preset gallery (Toddler … Chibi,
  thumbnails in the character's skin) · *Height* (%), *Weight*, *Muscle*,
  *Head size* (heads tall), *Shape* (shoulders ↔ hips) sliders · *Skin* swatches ·
  *Name* box and "Compare with others" (line-up) · *Close* (back to the page).

Each slider drag is one gesture (`BeginGesture` on press, `UpdateGesture` on move
for live preview everywhere, `CommitGesture` on release) → one undo entry per drag.
History source = the character editor, so Ctrl+Z from a page that undoes a body
change switches to the character (existing behaviour).

### 5.4 Page editor: placing and editing instances

- **Insert › Character** gallery (the library's thumbnails + "New character…"):
  places into the selected panel (else the panel under the viewport centre).
- **Default placement:** scale = the panel's existing characters' scale if any,
  otherwise fit the figure to 80% of the panel height; ground at 90% of panel
  height (or the others' floor); x = to the right of the rightmost existing
  character, else panel centre.
- **Select** by clicking the silhouette (path hit-test via `Silhouette`, not the
  bounding box, since characters overlap). Hit priority: bubbles > characters >
  panel.
- **Move**: drag. **Resize**: top-corner handles, uniform, about the ground point,
  resizing everyone at the same scale (Shift: just this one). **Flip**, **Bigger /
  Smaller**, **Match size**: ribbon or right-click. **Delete**: Del. **Order**: to
  front / to back (always behind bubbles). **Double-click**: opens the character editor.
- **Snapping** (reusing `SnapGuide` drawing, Alt disables): ground snaps to
  panel-mates' floor ("same floor"); a Shift-resize snaps back onto the panel's
  scale when close. Done in `PageEditorViewModel` with `PanelSnapping.SnapValue`.
- **Contextual "Character" tab** (green, like Panel/Bubble): Edit body, Flip,
  Bigger / Smaller / Match size, To front / To back, Remove.
- **Panel ops carry characters along** (`CharacterPlacementEditing` in
  `Stanley.Editing`, mirroring `BubbleEditing.Refit`): panel move/resize maps the
  ground point affinely and **keeps scale** (people don't squash); split sends each
  character to the half its ground x is in; deleting a panel deletes its characters.
  Unlike bubbles, characters may hang outside the panel (cropped shots are normal),
  so the constraint is only `KeepReachable`: some of the figure stays inside the
  panel so it can't be lost.
- Status-bar hints for the new selection/tool states, per the existing pattern.

## 6. Where code goes

| Project | New / changed |
|---|---|
| ProjectModel | `BodyShape`, `BodyPresets`, `BodyRig`; `CharacterDefinition.Body`; `CharacterPlacement` + `CharacterInstance.Placement`; `ProjectRepository.ListCharacters/DeleteCharacter` |
| Rendering | `ICharacterRenderer`, `MannequinRenderer`, `CharacterResolver`; `PageRenderer` character layer + lookup |
| Editing | `CharacterPlacementEditing` (default placement, move, resize together/alone, panel scale, flip, reorder, refit, keep-reachable); `PanelLayoutEditing` carries characters through resize/split |
| Editors | `ICharacterCatalog`, `CharacterLibraryViewModel/View` (the Characters pane, also the catalog), `CharacterEditorViewModel/View/Ribbon`, `CharacterFigure` (thumbnails, gallery, line-up stage), page-editor instance selection/gestures/Character tab, Insert › Characters, drop target; `ComicProject` load/save characters; `PageEditorHost` returns an `EditorSession` |
| App | Nothing structural (workspace already hosted); File › Info could show the character count |

## 7. Delivery slices (each a PR, each green on its own)

1. **Model:** `BodyShape`/presets/`BodyRig`, definition + instance fields, repository
   list/delete. Tests: rig invariants, JSON shape (sorted, camelCase), round-trip.
2. **Rendering:** `MannequinRenderer` + page character layer. Tests: silhouette
   bounds match `BodyRig.Extent` mapped through the placement, mirroring, missing-character placeholder,
   draw order (pixel probe).
3. **Document:** `ComicProject`/`CharacterLibraryViewModel` load/save/recovery. Tests:
   save → open round-trip with an instance, delete removes folder, unchanged save is
   byte-identical.
4. **Character authoring UI:** Characters pane + character editor + ribbon.
   Tests: slider gesture = one undo entry, preset applies all four values, line-up;
   headless smoke test that the pane opens and the ribbon swaps.
5. **Placement UI:** insert/drag-drop, select/move/resize/flip/delete/order, snapping,
   carry-along on panel ops. Tests in `Stanley.Editing.Tests` (pure ops) and
   `PageEditorViewModelTests` (gestures, default scale rule).

Slices 1–3 are invisible plumbing; 4 and 5 are what a user sees. A shortcut to
something visible early: land 1+2 with a hard-coded demo character on a new
project's first panel, then remove it in 5.

## 8. Explicitly out of scope

Stickers/art import, faces/expressions, posing/IK, view angles other than front,
revisions UI and per-issue revision picking, per-instance body overrides, a rig
editor, character CLI subcommands, cross-project character libraries.

## 9. Decisions taken (were open questions)

- **Mannequin look:** flat cartoon silhouette in the skin colour.
- **Sizes:** relative (100% = average adult), not cm.
- **Sliders:** Height, Weight, Muscle, Head size, Shape (shoulders ↔ hips).
- **Scale:** the same for every character in a panel by default; resizing one
  resizes the group, Shift resizes one alone.

## 10. What was built

All five slices landed together. Tests: `BodyRigTests` / `CharacterListingTests`
(ProjectModel), `CharacterPlacementEditingTests` (Editing),
`CharacterRendererTests` (Rendering), `CharacterEditingTests` (Editors: scale
sharing, resize together/alone, floor snap, one-undo-step slider drags, presets,
open/close, delete rules, save/open/prune), `CharacterTests` (headless UI: pane,
editor ribbon, click/double-click on the page).

**Side view (added after the first pass, as a priority).** `BodyRig.Build(body,
angle, overrides)` generates a profile facing +x: head with a nose (the cue for
which way a flat figure faces), chest/belly/seat depths from muscle, weight and
frame, legs merged into the body with the far one set back, feet pointing
forward. The near arm, hand and foot (the character's right, as for a real person
facing right) are a second layer (`BodyFigure.NearLimbs` / `NearBlobs`) drawn over
the body with their own outline so they read on a flat
fill; hit-testing uses the true union of both. On the page: the Character tab's
View group (Front / Side / Flip), S and F keys, right-click. The character editor
has a Front/Side preview toggle (the line-up turns too). Skeleton overrides are
per view.

**Limb posing (added next, on request).** Drag a selected character's hand or
foot (green dots) and the arm or leg follows: two-bone inverse kinematics
(`CharacterPosing.Reach`, law of cosines, exact when in reach, pointing straight
at the target when not). The result is stored as ordinary `PoseData.BoneRotations`
(root and middle bone of the limb, degrees relative to the parent, measured from
the rest layout), so a pose survives body edits and could later move between
characters or into a pose library. The elbow/knee bend side is fixed for a drag;
in side view knees bend forward and elbows back. One drag = one undo step; Reset
pose on the Character tab.

**Whole-body movement and pose presets (added next).** Hollow rings on the hips,
chest and head: dragging the hips moves the body with both feet pinned (legs
re-solved each move; crouching is limited to about half the leg so a chibi can't
sink through the floor), the chest leans the upper body about the hips (arms ride
along), the head tilts about the neck. The pose carries a `HipsShift` (a fraction
of the character's height, so it transfers between bodies). Hands now lie along
the forearm and a lifted foot tips with the shin. Mirror pose swaps sides. The
Character tab has a Pose gallery of eleven presets, previewed on the selected
character: each is a set of hand/foot goals relative to that limb's own root and
length (plus lean, head tilt, hips drop, and the view it needs - Walk, Run and Sit
turn the character side on), solved with the same IK - so one preset fits a
toddler, a chibi and a heavy adult alike, and the result is ordinary pose data to
keep adjusting. Not yet: a saved/user pose library, hand/foot rotation handles.

**Trunk by inverse kinematics (added later, on request).** The chest and head rings
no longer rotate rigid blocks. The back bends at three joints (lower back about the
hips, mid back, upper chest) and the torso outline follows a smooth blend of them;
dragging the chest solves all three at once (damped least squares, preferring a bend
shared along the spine), so the back curves. The arms keep their direction while the
body bends under them, so hanging arms keep hanging. The head ring bends the neck and
tilts the head together the same way.

Known limits of the POC: the torso ignores skeleton overrides (limbs, head and
neck follow them); no three-quarter view; front-view Flip shows no difference
until the body is asymmetric (posing, stickers); creating a character from the
Insert tab takes two undo steps (create, place).
