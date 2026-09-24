# Character authoring POC — design

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
| `ComicProject` | Loads/saves pages and panels of issue 1 | Ignores `characters/` entirely (Save As copies the folder, so they survive, but that's it). |
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
2. **Real-world units for the character, mm for the page.** Height is in cm, so
   "Alice is 162 cm, Bob is 190 cm" means something and survives any panel size.
   Each placed instance has its own page scale (mm on the page per cm of
   character). **Default rule:** a character placed into a panel that already has
   characters takes their scale, so relative heights are correct without the user
   doing anything. Resizing one on purpose (foreground/background depth) is allowed.
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
6. **Rest pose, front view only.** `PoseData` is stored as `Front` with no bone
   rotations. The generator takes a `ViewAngle` so three-quarter/profile are an
   additive follow-up; mirroring *is* in (it's one bool and a comic staple:
   characters facing each other).

## 3. Data model

### 3.1 `BodyShape` (new, `Stanley.ProjectModel/Characters/BodyShape.cs`)

```csharp
/// Source data for a generated body. All values are clamped by BodyShape.Normalize.
public sealed record BodyShape(
    double HeightCm,   // 45–250,  default 175  — ground to top of head
    double Build,      // 0–1,     default 0.35 — "Weight": slim → heavy
    double HeadsTall,  // 3–9,     default 7.5  — head size: chibi → heroic
    double Frame);     // 0–1,     default 0.5  — shoulders ↔ hips (V → A silhouette)
```

- `Build` keeps the meaning the plan already gives it (0 slim – 1 heavy), so the
  existing `CharacterRevision.Build` and the future stretch/breakpoint stickers key
  off the same number. The UI labels it **Weight** and shows an *approximate* kg
  readout derived from height + build (BMI ≈ 17 → 40 across the slider). It's a
  readout, not stored: storing kg would make a height change silently change build.
- `HeadsTall` covers age as well as style: child ≈ 5, adult ≈ 7.5, heroic ≈ 8.5,
  chibi ≈ 3. No separate age parameter.
- **Presets** (`BodyPresets`, same enum + static lookup shape as
  `BubbleStylePresets`/`MetricPaperSizes`): Toddler, Child, Teen, Adult, Heavy,
  Heroic, Chibi. Applying one sets all four values; sliders adjust afterwards.

### 3.2 `CharacterDefinition` changes

```csharp
public sealed record CharacterDefinition(
    CharacterId Id,
    string Name,
    BodyShape Body,                 // NEW
    Skeleton Skeleton,              // now: sparse manual override on the generated rest layout (empty in the POC)
    SortedDictionary<string, ColorValue> ColorSlots,        // POC uses "skin" only
    SortedDictionary<string, StickerSlotDefinition> StickerSlots);  // empty in the POC
```

`Skeleton` changes meaning from "the rest layout" to "overrides on the generated rest
layout". The plan's "files store source data only" rule argues for this: the joint
positions are derived from `Body`, so they aren't written out. Nothing reads
`Skeleton` today, and there's no file-format version yet (0.x), so no migration is
needed.

### 3.3 Resolution chain (designed, POC implements the last step only)

`instance override → revision (CharacterRevision.Build / ProportionOverride) → definition.Body`.
When revisions become editable, `CharacterRevision.Build` generalises to a sparse
`BodyShapeOverride(double? HeightCm, double? Build, …)`; `CharacterInstanceOverrides`
gets the same for the one-off "bloat gag" panel. A `CharacterResolver` in
Rendering does the resolution so renderer and editor agree.

### 3.4 `CharacterInstance` placement (new field)

```csharp
public sealed record CharacterPlacement(
    Point2D Ground,    // page mm: the point on the floor between the feet
    double Scale,      // page mm per character cm (0.5 = a 175 cm character is 87.5 mm tall)
    bool Mirrored);    // flipped horizontally about Ground.X

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

- `BodyRig.RestLayout(BodyShape, ViewAngle) → ViewAngleRestLayout` in **cm, y-down,
  origin at the ground point** — joints for the required VRM bones (hips, spine,
  chest, neck, head, shoulders, arms, legs, feet; no fingers/toes/eyes).
  `Skeleton` overrides are then applied by bone.
- `BodyRig.Segments(BodyShape, layout) → (bone, from, to, radiusFrom, radiusTo)` —
  limb thickness, torso/hip/belly widths as functions of `Build` and `Frame`.
- `BodyRig.Extent(BodyShape) → Rect2D` (cm, relative to ground) — used by
  hit-testing fallback, selection box, default placement and `KeepReachable`.

Invariants the tests pin down (the exact proportion constants are tuned in the PR):
top of head is exactly `HeightCm` above ground; head height is
`HeightCm / HeadsTall`; build and frame change widths only, never heights; the
layout is left/right symmetric; widths are monotonic in `Build`.

### 4.2 Renderer (`Stanley.Rendering`)

```csharp
public interface ICharacterRenderer
{
    SKPath Silhouette(ResolvedCharacter character, CharacterPlacement placement); // page mm, for hit-testing too
    void Draw(SKCanvas canvas, ResolvedCharacter character, CharacterPlacement placement, float strokeMm);
}
```

- `MannequinRenderer`: capsules/ellipses per segment, unioned into one path, filled
  with the `skin` colour slot (default a neutral warm grey), stroked at
  `PanelBorderMm`-ish weight. Placement = translate to `Ground`, scale by `Scale`,
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
- `CharacterLibrary` (Stanley.Editors, observable): the open comic's characters by
  id, `Changed` event. Page editors, thumbnails and the Characters pane all read
  it; a definition change redraws every page that shows that character.

### 5.2 Characters pane (left tool pane, tab next to Pages)

```
┌ Pages │ Characters ┐
│ [fig] Alice  162cm │   ← mannequin thumbnail, name, height
│ [fig] Bob    190cm │
│ [fig] Kid    128cm │
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

Pane: the character full height on a cm grid (lines every 10 cm, labels every
50 cm), with the project's **other characters faded behind it in a line-up** so
height comparisons are immediate (toggle on the View tab). Nothing else in the pane:
all controls are on the ribbon.

Ribbon (`CharacterEditorRibbon`, mapped by `DataTemplate` like `PageEditorRibbon`):

- **Body** tab: *Body type* preset gallery (Toddler … Chibi, thumbnails) ·
  *Height* slider + cm spin box · *Weight* slider (slim ↔ heavy, "≈ 74 kg" readout)
  · *Head size* slider (labelled by heads tall) · *Shoulders/hips* slider ·
  *Skin* colour swatches + custom · *Name* text box.
- **View** tab: fit / zoom, line-up on/off.

Each slider drag is one gesture (`BeginGesture` on press, `UpdateGesture` on move
for live preview everywhere, `CommitGesture` on release) → one undo entry per drag.
History source = the character editor, so Ctrl+Z from a page that undoes a body
change switches to the character (existing behaviour).

### 5.4 Page editor: placing and editing instances

- **Insert › Character** gallery (the library's thumbnails + "New character…"):
  places into the selected panel (else the panel under the viewport centre).
- **Default placement:** scale = the panel's existing characters' scale if any,
  otherwise fit the figure to ~80% of the panel height; ground at ~90% of panel
  height; x = to the right of the rightmost existing character, else panel centre.
- **Select** by clicking the silhouette (path hit-test via `Silhouette`, not the
  bounding box, since characters overlap). Hit priority: bubbles > characters >
  panel.
- **Move**: drag. **Resize**: top-corner handles, uniform, about the ground point.
  **Flip**: button / `H`. **Delete**: Del. **Order**: bring forward/send back.
  **Double-click**: opens the character editor.
- **Snapping** (reusing `SnapGuide` drawing, Alt disables): ground snaps to
  panel-mates' ground line ("same floor"); resize snaps to panel-mates' scale
  ("same distance"). Implemented in `CharacterPlacementSnapping` in
  `Stanley.Editing`.
- **Contextual "Character" tab** (green, like Panel/Bubble): Flip, Match size,
  Bring forward/Send back, Edit character, Delete.
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
| Editing | `CharacterPlacementEditing` (place, move, scale, flip, reorder, refit, split, keep-reachable), `CharacterPlacementSnapping` |
| Editors | `CharacterLibrary`, `CharacterEditorViewModel/View/Ribbon`, `CharactersPaneViewModel/View`, page-editor instance selection/gestures/Character tab, Insert › Character, drop target; `ComicProject` load/save characters; `PageEditorHost` adds the pane |
| App | Nothing structural (workspace already hosted); File › Info could show the character count |

## 7. Delivery slices (each a PR, each green on its own)

1. **Model:** `BodyShape`/presets/`BodyRig`, definition + instance fields, repository
   list/delete. Tests: rig invariants, JSON shape (sorted, camelCase), round-trip.
2. **Rendering:** `MannequinRenderer` + page character layer. Tests: silhouette
   bounds match `BodyRig.Extent × Scale`, mirroring, missing-character placeholder,
   draw order (pixel probe).
3. **Document:** `ComicProject`/`CharacterLibrary` load/save/recovery. Tests:
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

## 9. Open questions (worth confirming before slice 4)

- **Mannequin look:** neutral grey artist's-mannequin (clearly a placeholder) vs. a
  simple flat cartoon silhouette with the skin colour (usable for thumbnails/roughs
  on its own). The design assumes the latter; it's a renderer-only choice.
- **Absolute heights:** is a real cm height the right mental model, or would users
  rather think only in relative terms ("a head taller than Bob")? cm is proposed
  because it makes relative heights automatic and is what a model sheet shows.
- **Slider set:** Height / Weight / Head size / Shoulders–hips. Is muscularity
  (distinct from weight) needed from day one, or can it wait for stickers?
- **Per-panel scale rule:** "new characters inherit the panel's scale" — right
  default, or should scale be a single panel-level camera value that instances
  only offset by depth?
