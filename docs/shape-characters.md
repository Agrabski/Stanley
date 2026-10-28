# Characters built from shapes — design

**Status: in progress** (issue #173). Slices 1–4 and 6 are built (§14 records how); the rest
is the working plan. This is the design for a
second way to build a character: by snapping together ready-made shapes instead of
moving body sliders. It builds on the character POC (`docs/character-authoring-poc.md`)
and the sticker system (`docs/sticker-system.md`). It changes neither of them: it adds
a new kind of body next to the generated mannequin. §13 lists what still needs the
user's decision.

Scope: what a shape-built body is made of, how it's stored, how it's generated,
drawn and posed, how faces and other stickers go on it, how it's edited in the
character editor, the shipped shape library, and custom shapes. Out of scope:
per-panel module overrides, more than two arms or legs, a 3D backend.

## 1. The problem, from the user's side

Issue #173 (translated from Polish):

> Right now the program has a fairly typical character editor: a ready-made skeleton
> with parameters to set and clothes to put on. That direction may be a mistake,
> because we can't match competitors who have far more resources to churn out pretty
> assets. Relying on users to find assets in outside libraries isn't good either:
> searching is tiring, and keeping a consistent style is very hard.
>
> Instead it's better to go for building characters from ready-made shapes, like
> building blocks. That gives users a lot of room to be creative, and it's original
> and easy.
>
> Characters are made of a few modules (e.g. head, torso, arms, legs). Click a
> module's piece to swap it for another or change it. For example, someone can build
> a character with a square head, an upside-down triangle for a torso, and legs. Or
> they can remove the torso and make a character that's just a head and legs.
>
> A bit like Spore, but in 2D.
>
> The modules won't be plain figures (circles, triangles), because that would look
> ugly. Instead the program will have a library of ready-made shapes: organic,
> geometric and abstract, plus a way to add your own.
>
> Modules should be posable the way our current characters are. For example, the
> legs module should have points you can grab to set how the legs are placed.

What the user needs, in short:

- Pick a shape for each part, from a good-looking library, in one click.
- Leave a part out, and the rest still holds together.
- Change any shape (size, turn, flip, colour, its points), or bring your own.
- Pose it on the page by dragging hands, feet, hips, chest and head, exactly as now.

## 2. Decisions

1. **A second kind of body, not a replacement.** `CharacterDefinition` keeps its
   generated `BodyShape` body. A character gets an optional `Modules`, and when it's
   set, that's what the body is built from. Every existing comic opens and draws
   unchanged; no migration. *New character* offers both ways (§9.1). Whether shapes
   become the default later is §13's first question.
2. **Same figure out, so everything downstream keeps working.** A new
   `ModularRig.Build` produces the same `BodyFigure` record `BodyRig.Build` does:
   `HumanoidBone` joints (rest, base and posed layouts), depth layers, regions and
   extent. Posing (`CharacterPosing`: IK, trunk lean, head tilt, hips), pose presets,
   *Turn*, mirroring, hit-testing, the line-up, thumbnails, export and the page
   editor's handles all read a `BodyFigure`. They don't care where it came from.
3. **One seam picks the rig.** A `CharacterRig` (ProjectModel) takes the whole
   `CharacterDefinition` and dispatches to `BodyRig` or `ModularRig`. Today only a
   handful of places call `BodyRig.Build`/`Extent` with a character's body (§3), so
   switching them to the seam is a small, behaviour-free first PR.
4. **A module is a shape plus where it attaches.** Head and torso are outlines
   (bezier anchors, the same `ShapeAnchor` model as drawn shapes and panels). Arms and
   legs are limbs: two bones each, so IK works, drawn in a limb *style* (a tube, a
   taper, separate pieces, a stick) with an optional end shape (hand, foot). Everything
   is a few numbers plus a list of anchors.
5. **Sockets connect modules, and fall back when a part is missing.** Each shape has
   attachment points (neck, shoulders, hips) in its own unit box. They're derived from
   the outline automatically, so any shape, including one you drew yourself, works
   with no setup. They can be dragged by hand as the escape hatch. A missing torso
   makes the head the attachment for arms and legs. A missing head moves the face onto
   the torso (§4.3).
6. **Shapes are copied into the character.** Picking a library shape copies its
   anchors into `character.json` (with a `source` while unmodified), as wearing a
   library sticker copies it (sticker-system.md §2.7). A later Stanley release that
   changes its library never repaints an existing comic, and a comic carries
   everything it needs.
7. **Modify means Edit Points.** Changing a module's outline is the Figma-style point
   editing that already exists for drawn shapes (`ShapePointEditing`,
   docs/shape-editing.md), run on the module's anchors. No second shape editor.
8. **Stickers still work.** The head module is the `head` region, so the whole face
   library (eyes, brows, mouths, noses, split eyes, expressions), hair and headwear
   goes on a square head as it does on the mannequin's. Limbs are capsules, so sleeves,
   gloves and shoes fit. A part on a region the character doesn't have is not drawn.
   Clothing *covers* on a shape torso come in a later slice (§12).
9. **The library is original, CC0, and stored in the project format** (JSON anchors as
   embedded resources, next to the sticker library), like `StickerLibrary`.

## 3. What exists to build on (as mapped for this plan)

| Piece | Where | How this plan uses it |
|---|---|---|
| `BodyFigure` (layouts, `Layers`, `Regions`, `Extent`) | `ProjectModel/Characters/BodyRig.cs`, `FigureLayers.cs` | `ModularRig` produces one. |
| `BodyRig.ApplyPose`, `LimbChains`, `SpineJoints`, `NeckJoints`, `HipsShiftRange` | `BodyRig.cs` | Reused as is: rotations relative to the rest layout, same limits. |
| `TorsoFrame(RestOutline, TrunkBend)` | `FigureLayers.cs` | A shape torso's outline goes through the same bend, so leaning curves it like the mannequin's back. |
| `FigureLayer` (Kind, Torso, Capsules, Ellipses, Seams) | `FigureLayers.cs` | Gains `Shapes` (§5). |
| `FigureRenderer.Build` | `Rendering/CharacterRenderer.cs` | Paints layers back to front; learns per-module colours and shape fills. |
| `CharacterPosing` | `Editing/CharacterPosing.cs` | Unchanged except for asking the figure which limbs exist. |
| `ShapeAnchor`, `AnchorRingPath`, `ShapePointEditing` | `ProjectModel/Geometry`, `Rendering`, `Editing` | Module outlines, their drawing, and Edit Points. |
| `StickerLibrary` (embedded resources, `library:` source) | `src/Stanley.StickerLibrary` | Template for the shape library. |
| Character editor stage click → `SelectSticker(Stage.StickerAt(p))` | `Editors/Characters/CharacterEditorView.axaml.cs` | The same click picks a module. |

**Callers of the rig with a character's body** (all move to `CharacterRig` in slice
1): `CharacterPosing.Figure` and its trunk solver (`CharacterPosing.cs:37`, `:194`),
`FigureRenderer.Drawing` and `Extent` (`CharacterRenderer.cs:268`, `:304`),
`PageEditorViewModel` default placement extents (`:1936`, `:1946`),
`CharacterEditorViewModel.Art.cs:217`, `CharacterFigure.cs:115`. About 20 more places
read `character.Body` directly (heights for ease, the Body tab's sliders,
`CharacterItem`'s height label); those that need a height use
`CharacterRig.Height(character)`. The Body tab's sliders stay mannequin-only. Calls
with `BodyShape.Default` (missing-character placeholder, sticker templates) stay as
they are.

**Gotcha to carry over**: `FigureRegions` has a non-null frame for every region, and
the sticker code assumes them. Rather than making them nullable across the renderer,
`BodyFigure` gains `Absent` (a set of `BodyRegion`). A missing region still gets a
small degenerate frame at its socket, and sticker parts on an absent region are
skipped.

## 4. Data model

### 4.1 Records (`Stanley.ProjectModel/Characters/ModularBody.cs`)

```csharp
// As built (nullable optionals keep files sparse: null is the default)
public sealed record ModularBody(
    ShapeModule? Head, ShapeModule? Torso, LimbModule? Arms, LimbModule? Legs,
    bool? Separate = null);                      // null/false: joints seamless (one creature); true: every piece outlined

public sealed record ShapeModule(                // a head or a torso
    ModuleShape Shape,
    double Width, double Height,                 // figure units (1.0 = an average adult's height)
    double? Rotation = null, bool? FlipX = null, bool? FlipY = null,   // "upside-down triangle" is FlipY
    Point2D? Offset = null,                      // nudge off the socket, figure units
    string? Color = null);                       // colour slot; null = "skin"

public sealed record ModuleShape(                // an outline in a unit box (0,0)-(1,1), y down
    IReadOnlyList<ShapeAnchor> Anchors,
    SortedDictionary<string, Point2D>? Sockets = null,   // ModuleSockets names; only hand-set ones
    string? Source = null);                      // "library:shapes/<kind>/<category>/<name>" while unmodified

public sealed record LimbModule(                 // a pair of arms or legs, two bones each
    LimbStyle Style,                             // Tube, Taper, Pieces, Stick
    double Length, double Thickness,             // root to wrist/ankle; width at the root
    double? Taper = null, double? Bend = null,   // 0.5 each when null
    ModuleShape? End = null, double? EndSize = null,          // hand/foot, hung from the wrist/ankle in an EndSize box
    ModuleShape? Upper = null, ModuleShape? Lower = null,     // Pieces only
    LimbSides? Sides = null, string? Color = null);
```

Extras (ears, tails, horns) are not in the record yet; they come with slice 9.

- **Sizes are figure units**, like everything in a `BodyFigure`: 1.0 is an average
  adult's height, so the panel's shared scale (`CharacterPlacement.UnitHeightMm`)
  and "Bob is a head taller than Alice" work across both kinds of character. A
  shape character's height is derived (top of the assembly above the ground), never
  stored, so it can't disagree with its parts. `CharacterRig.Height` reads it.
- **`CharacterDefinition.Modules`** (`ModularBody?`, written only when set) is the
  only change to the definition. `Body` stays (required, ignored while `Modules` is
  set, and what *Convert to sliders* would fall back to, if that's ever wanted).
- Anchors are inline because a module outline is small (typically 6–20 anchors) and
  inline keeps undo, clipboard, My Assets fingerprints and git diffs trivial. Sorted
  keys and sparse nulls keep a no-op save byte-identical, as everywhere else.

### 4.2 Sockets

Derived from the outline in its unit box (`ModuleSockets.Derive`), so a new shape
needs no setup:

| Socket | Derived as | Used by |
|---|---|---|
| `Neck` | top of the outline at its centre line | the head (on a torso), the torso (under a head, as its top) |
| `LeftShoulder` / `RightShoulder` | the outline's edges, 15% down from its top (the rig then moves the arm in by its radius) | arms |
| `LeftHip` / `RightHip` | the outline's bottom edge at 70% / 30% across (the character's own left is the viewer's right from the front, as in VRM) | legs |
| `Face` | the centre of the largest ellipse with the outline's proportions that fits inside it (a torso: its upper two-thirds) | the `head` region (face stickers, hair, headwear) |

A socket dragged by hand in the character editor is stored in `Sockets` and wins.
Flip and rotation transform sockets with the outline, so the upside-down triangle's
shoulders are its wide top corners.

### 4.3 Fallbacks when a part is missing

| Missing | Arms attach to | Legs attach to | Head sits on | Face (`head` region) on |
|---|---|---|---|---|
| nothing | torso shoulders | torso hips | torso neck | head |
| torso | head's sides (shoulder sockets of the head) | head's hip sockets | legs' top | head |
| head | torso shoulders | torso hips | — | torso's `Face` socket (a "bean" creature) |
| legs | as usual | — | as usual | as usual; the lowest piece stands on the ground |
| arms | — | as usual | as usual | as usual |

The ground point (figure origin) is always under the lowest thing drawn: the feet,
else the bottom of the lowest shape. Missing joints collapse onto their neighbours
(no torso: `Hips`, `Spine`, `Chest` and `Neck` sit at the head's bottom), so a
`PoseData` always finds every bone it rotates, and a pose made on one character still
applies to another with different parts.

## 5. Rig: `ModularRig.Build`

`ModularRig.Build(ModularBody, ViewAngle, PoseData?) → BodyFigure`, pure math in
ProjectModel next to `BodyRig`, no stored geometry.

1. **Stack the rest layout bottom-up**: legs from the ground (their length decides
   hip height), the torso on the hips, the head on the torso's neck socket, arms on
   the shoulder sockets hanging straight down. Joints for every `HumanoidBone`
   `BodyRig.LimbChains`, `SpineJoints` and `NeckJoints` name. The spine knots sit at
   fixed fractions up the torso's height, so `TrunkBend` works unchanged.
2. **Pose it with the same code**: `ApplyPose` for limb rotations, the trunk bend for
   spine joints, the hips shift, neck and head tilt. The torso outline goes through
   `TorsoFrame`'s bend (it's a rest outline like the mannequin's). The head shape turns
   rigidly with the head bone.
3. **Shapes into layers**, the same `FigureLayerKind`s: legs (each their own layer,
   right over left), torso, head, arms (right over left); side view: far arm, body
   (torso and both legs), head, near foot, near arm. Limbs become `BodyCapsule`s
   (Tube, Taper), two rigid shapes along the bones (Pieces), or a thin capsule (Stick).
   End shapes are placed at the wrist/ankle along the lower bone, like the mannequin's
   hands and feet.
4. **Seams**: unless `Separate`, discs at each socket in use, as the mannequin's
   shoulders and hips have, so a tube arm joins a triangle's corner with no line.
   With `Separate`, no seams: every piece keeps its full outline (the block-toy look).
5. **Regions**: `Head` = the face ellipse (head module, or the torso's `Face` socket);
   `Torso` = the torso outline; arm/leg `LimbFrame`s from the bones; hands/feet from
   the end shapes' bounds. `Absent` lists what's missing (§3's gotcha).
6. **Side view**: the same outlines (a shape is a shape), limbs arranged side on as
   `BodyRig.BuildProfile` does: one arm near, one far, legs overlapping with the far
   one set back, feet pointing forward. Mirroring makes it face left. Face stickers
   already ship profile art. An optional profile outline per module is a later
   refinement (§12). Three-quarter draws as the front, as for the mannequin.

`FigureLayer` gains `IReadOnlyList<FigureShape> Shapes` (anchors already in figure
space, plus the colour slot), counted by `HasBody`. The mannequin leaves it empty.

Invariants the tests pin down: the ground point is under the lowest drawn thing;
every bone in `LimbChains`/`SpineJoints`/`NeckJoints` has a joint whatever is
missing; the derived height equals the extent's top; sockets follow flip and
rotation; a missing torso puts the head on the legs and the arms on the head; Build
with a null pose equals the rest layout; left/right symmetric shapes give a symmetric
layout.

## 6. Drawing

- `FigureRenderer.Build` paints each layer's body as now, but **per colour**: the
  shapes of one colour slot in a layer are one `ShapeItem` (filled with
  `look.Color(slot)`, so a per-panel colour override works for free), inked around
  their union with the layer's seam mask. One colour for everything (the default)
  draws exactly like the mannequin.
- Shapes are filled from anchors through the existing `AnchorRingPath` builder.
- Ink width is `PageRenderer.CharacterStrokeMm`, as for every character.
- Sticker parts whose region is in `BodyFigure.Absent` are skipped
  (`StickerGeometry.Of`), and the Look tab marks such a sticker "not drawn: no torso".
- Caching is unchanged: `FigureRenderer` keys drawings by definition value, which now
  includes the modules.

## 7. Posing on the page

Nothing new to learn: the same green dots on hands and feet, rings on hips, chest and
head (character-authoring-poc.md §10).

- `PageEditorViewModel` shows a limb's handles only when the figure has that limb
  (`Absent`), the chest ring only with a torso, the head ring with a head.
- Leaning with no torso tilts the head about the hips; tilting a head-less character
  does nothing (no ring).
- Pose presets solve from each limb's own root and length, so Wave, Walk, Sit and the
  rest fit a stubby-legged blob as they fit an adult. The #76 test (grab every hand and
  foot of every preset without moving it; the joint stays put) runs on every shipped
  creature preset as well.

## 8. Library

`src/Stanley.StickerLibrary/Library/shapes/<kind>/<category>/<name>.json` (embedded,
CC0, original art, `ShapeLibrary` next to `StickerLibrary`):

- **Kinds**: `head`, `torso`, `hand`, `foot`, `piece` (for Pieces limbs); any head or
  torso shape is allowed in the other slot (the gallery shows the slot's own first).
- **Categories**, as the issue asks: *Organic* (bean, pear, drop, potato, peanut,
  cloud), *Geometric* (rounded square, soft triangle, hexagon, capsule, diamond, but
  with slightly eased corners and imperfect curves, never textbook primitives - the
  issue's "plain figures would look ugly"), *Abstract* (star-ish, wobble, crescent,
  bolt, spiral blob).
- **Limb styles**: Tube, Taper, Pieces, Stick; hand ends (mitten, ball, three-finger,
  claw, none) and foot ends (shoe, ball, paw, flat, none).
- **Creature presets** (`CreaturePresets`, a one-click whole body, the on-ramp):
  e.g. Bean, Mr. Square, Robot, Stick figure, Blob, Big-head, Head-on-legs. Applying
  one replaces the modules; everything stays editable afterwards.
- First release target: about 8 heads, 8 torsos, 5 hands, 5 feet, 6 presets. Size
  and art direction are §13's fourth question.

A shape file is just a `ModuleShape` (anchors + optional sockets) and a name, so
copying one into a character is writing the same JSON.

## 9. UX

### 9.1 Creating

*New character* (Characters pane, Insert › Character) becomes a split choice: **From
shapes** (opens the character editor on the creature preset gallery) or **Human
body** (today's Adult preset). The Characters pane's rows, thumbnails, line-up,
compare and placement are unchanged: a shape character is a character.

### 9.2 Character editor

For a shape character the **Body** tab is replaced by a **Shapes** tab; Look and
Sticker stay.

```
┌ Shapes ─────────────────────────────────────────────────────────────────┐
│ [Creature presets ▾] │ Head [▣] Torso [▽] Arms [~] Legs [~] │ Joined ⇄ Separate │ Height % │
└─────────────────────────────────────────────────────────────────────────┘
```

- **Stage, direct manipulation first**: click a module to select it (outline
  highlight, like a selected sticker); drag its corner handles to resize, its top
  handle to turn, the body to nudge it off its socket. Missing parts show a faint
  **+** at their socket ("Add arms"), one click to put the default back.
- **Contextual Module tab** (like the Sticker tab) for the selected module: a shape
  gallery by category (*Organic / Geometric / Abstract / My shapes*; clicking one
  swaps it, keeping size, colour and attachments), Width/Height, Rotate, Flip
  horizontal/vertical, Colour (a colour slot: *Skin* or its own), **Edit Points**,
  **Edit sockets** (escape hatch), **Remove**. For a limb: Style, Length, Thickness,
  Taper, End shape, One side only.
- **Height %** scales the whole assembly about the ground, for "make her a bit
  smaller" without resizing each part.
- **Edit Points**: double-click a module (or the button). The module's anchors are
  edited exactly as a drawn shape's: drag points, press the outline to add, bend
  handles, smooth/corner, delete. Esc or Enter is done. The library `source` is dropped
  on the first change. Implemented by wrapping the module as a `ShapeElement` in
  figure space for `ShapePointEditing` and writing the anchors back into the unit box.
- Every drag is one gesture, one undo step (`BeginGesture`/`UpdateGesture`/
  `CommitGesture`, as the sliders do).
- **Canvas layout parity**: the same controls as a `ShapesSection` (and a
  `ModuleSection` for the selection) in `CharacterInspector`, plus the toolbar, so
  `LayoutParityTests` holds (docs/ui-shell.md §6.3).

### 9.3 Custom shapes (the issue's "add your own")

1. **Change a library shape** with Edit Points (above). It's the character's own from
   then on.
2. **Draw one**: Module tab › *Draw shape* runs the Freeform tool on the stage; the
   closed outline becomes the module.
3. **Import an SVG**: the first closed path of the file (through the `StickerSvg`
   adapter) becomes the outline. Anything else in the file is reported, as sticker
   import does.
4. **Keep it**: right-click a module › *Keep in My Assets* puts the shape in My Assets
   (docs/asset-packs.md, a new `shape` kind), and it shows under *My shapes* in every
   comic's galleries.

## 10. Files on disk

```jsonc
// characters/<id>-blob/character.json (excerpt)
{
  "modules": {
    "arms": { "length": 0.32, "style": "tube", "thickness": 0.035 },
    "head": {
      "height": 0.3,
      "shape": { "anchors": [ /* … */ ], "source": "library:shapes/head/geometric/rounded-square" },
      "width": 0.3
    },
    "legs": { "end": { "anchors": [ /* … */ ], "source": "library:shapes/foot/organic/shoe" }, "endSize": 0.08, "length": 0.3, "style": "taper", "thickness": 0.05 },
    "torso": {
      "flipY": true,
      "height": 0.35,
      "shape": { "anchors": [ /* … */ ], "source": "library:shapes/torso/geometric/soft-triangle" },
      "width": 0.4
    }
  }
}
```

No new files or folders: modules live in `character.json`. A character without
`modules` reads as today.

## 11. Where code goes

| Project | New / changed |
|---|---|
| ProjectModel | `ModularBody`, `ShapeModule`, `ModuleShape`, `LimbModule`, `ModuleSocket`; `ModuleSockets.Derive`; `ModularRig`; `CharacterRig` (dispatch + `Height`); `CharacterDefinition.Modules`; `FigureLayer.Shapes`, `FigureShape`; `BodyFigure.Absent` |
| Rendering | `FigureRenderer` per-colour body items and shape fills; sticker parts skipped on absent regions; module thumbnails |
| Editing | `ModuleEditing` (swap keeping size/colour, resize, turn, flip, remove/add with defaults, height scale, socket drag) |
| StickerLibrary | `Library/shapes/…` (CC0), `ShapeLibrary`, `CreaturePresets` (next to `Hairstyles`) |
| Editors | `CharacterEditorViewModel.Modules.cs`; Shapes and Module ribbon tabs; stage selection, handles and + ghosts; Edit Points on a module; `ShapesSection`/`ModuleSection` inspector sections and toolbar; New character split; page handles filtered by `Absent`; My Assets shape kind |
| App | nothing structural |

## 12. Delivery slices (each a PR, each green on its own)

1. **The seam**: `CharacterRig`, every caller in §3 moved to it, `Height`. No
   behaviour change; existing tests prove it.
2. **Model and rig**: the records, JSON (sorted, sparse, round-trip, old files read
   unchanged), `ModuleSockets.Derive`, `ModularRig.Build` front view with fallbacks.
   Tests: §5's invariants.
3. **Drawing**: `FigureLayer.Shapes`, per-colour items, seams/Separate, absent-region
   stickers skipped. Tests: pixel probes (fill inside a triangle torso, no ink line at
   a joined shoulder, one with Separate), silhouette hit-test, face stickers on a
   square head.
4. **Library and presets**: shapes in three categories, limb styles, ends, creature
   presets.
5. **Character editor**: New character › From shapes, Shapes tab, stage selection,
   swap gallery, size/turn/flip/colour, remove/add, Height %; inspector and toolbar
   parity. Tests: one undo step per drag, swap keeps size and colour, headless smoke
   (select a module, swap it), `LayoutParityTests`.
6. **Posing**: page handles by present parts; side view for modules; the #76 test and
   preset solves on every creature preset; Turn and Mirror pose on a shape character.
7. **Modify**: Edit Points and Edit sockets on a module.
8. **Custom shapes**: Draw shape, SVG import, Keep in My Assets / My shapes.
9. **Later**: extras (ears, tails, horns, antennae: rigid shapes attached anywhere
   along a parent's outline, in front or behind, following its pose), clothing covers
   fitted to a shape torso, optional profile outlines per module, per-panel module
   overrides.

1–3 are plumbing; 4–5 are the first thing a user sees. A shortcut to something
visible: land 1–3 with one hard-coded creature preset behind *New character › From
shapes*, then grow the editor in 5.

## 13. Open questions (ask the user before deciding)

1. **Default path**: should *New character* lead with shapes (the issue's argument
   that this is the better direction), with the human body one click away, or stay
   human-first? The plan works either way; it's one line in §9.1. Recommendation:
   shapes first once slice 5 lands.
2. **Joined or separate pieces by default** (§5 step 4)? Joined reads as one creature;
   separate reads as building blocks. Recommendation: joined, with the toggle.
3. **Limb count**: `PoseData` and `HumanoidBone` know two arms and two legs. More
   (Spore's six-legged creatures) would mean extending the bone vocabulary and pose
   presets. Recommendation: keep to pairs (one side removable); extra limbs as
   non-posable extras in slice 9.
4. **Library scope and art direction**: how many shapes in the first release, and in
   whose hand? The issue's worry is matching competitors' asset volume, so a small set
   with a strong, consistent look plus easy custom shapes seems the better bet.
5. **Clothes on shape bodies**: is the face/head-sticker set enough at first, or do
   T-shirts and trousers need to fit a triangle torso early (slice 9 today)?

## 14. Implementation notes (slices 1–4, as built)

### 14.1 The seam and the contract

- `CharacterRig.Build/Extent/Height(CharacterDefinition, ...)` is the only way code gets a
  character's figure. `BodyRig` is called directly only for `BodyShape.Default` (the
  missing-character placeholder, sticker templates). New code must not call
  `BodyRig.Build(character.Body, ...)`.
- `BodyFigure.Absent` (`FigurePart(region, side?)`) is the whole contract for missing parts:
  the renderer infers nothing except that a skirt needs at least one leg. Absent regions
  still get finite frames (`ModularRig.Degenerate`, 1e-3) because extents, posing and the
  editors read them. `Neck` is always absent on a shape body; `Head` is absent only when
  there is neither a head nor a torso (with no head the face goes on the torso), so the
  page's head-tilt ring must check `Modules.Head is null`, not `Has(Head)`.
- Computed properties on the records (`IsModular`, `IsSeparate`, `ColorSlot`) carry
  `[JsonIgnore]`; without it every existing character would gain `"isModular": false` on its
  next save. `ModularBodyJsonTests` pins this.

### 14.2 Rig (`ModularRig`, `ModuleSockets`, `ModuleFrame`, `ModularBodies`)

- **Sockets** are derived in the outline's curve bounds (§4.2) and cached per `ModuleShape`
  instance (an IK drag builds the figure ~160 times). `ModularBodies.Normalized` returns the
  same instance when nothing needed fixing, which keeps that cache warm - keep it that way.
  `ModuleSockets.Flipped` mirrors the outline and its hand-set sockets (swapping left/right
  names on an X flip) and derived sockets are re-derived from the flipped outline: that is
  what puts an upside-down triangle's neck and shoulders on its wide top. Rotation is
  applied afterwards by `ModuleFrame` (centre, size, rotation: one affine map), so sockets
  turn with the outline. `ModuleSockets.Chin` (bottom of the centre line) is internal.
- **Stacking**: the torso, else the head, is the *host*. Legs hang from the host's hip
  sockets (raised by the leg radius, measured before the host's Offset - a nudge moves a
  part and what sits on it, never what it stands on); the head's chin sits on the neck
  socket, sunk by `HeadSink` (0.08 × head height); arms hang from the shoulder sockets moved
  in by their radius, `ArmSpread` (8°) off vertical - out to the side from the front,
  forward side on, the same both ways so a legless figure is as tall from either side.
  Hand-set sockets get no raise or inset. Side on, limbs sit on the host's centre line with
  the far ones set back (arms 0.6 × radius, legs 0.4).
- **Trunk joints**: Hips midway between the leg roots, Neck = Head = the neck socket, Spine
  and Chest at 0.3 / 0.65 of hips→neck. With no torso all five sit at the head's bottom (a
  lean tilts the head about the hips) and the arms turn rigidly with the head in a step
  after the trunk bend. BodyRig's `Trunk` (now `internal`) and `FootTilt` are reused, so the
  torso outline bends exactly as the mannequin's back does.
- **Ground and height**: two passes - at rest to find the ground (bottom of everything
  drawn) and the height, then posed (`HipsShift` is a fraction of that height). The rest
  Hips joint is at x = 0. Long arms on a legless body stand on the hands.
- **Limbs**: Tube/Taper are `BodyCapsule`s (taper end radius r × (1 − taper)); Stick uses
  `StickRadius` (0.006); Pieces are `FigureShape`s per bone (capsules when null); default
  ends are ellipses. A limb with its own colour slot is emitted as anchor outlines of its
  capsules (`CapsuleOutline`/`EllipseOutline`), because the layer's capsules are always
  skin-filled. From the front the right-side end and pieces are mirrored so a thumb or
  claw drawn on +x points outward on both sides; side on nothing is mirrored.
- `Blobs[0]` is the head's bounding ellipse (or the face ellipse): `CharacterPosing.HeadTop`
  reads it. The torso's `TorsoFrame.RestOutline` is its outline flattened to a polygon, so
  torso covers and warped torso art (`SmoothClosed` + `RowAt`) see a polygon.
- **Seams** (unless Separate): hip discs 1.5 × leg radius (on the torso layer, else the
  head's), the neck at 0.3 × the narrower of head and torso, arm roots 1.4 × arm radius.
- **Normalising** clamps sizes 0.02–2, lengths 0.02–1.5, thickness 0.005–0.5, taper 0–0.9,
  bend 0.2–0.8, end size 0.005–0.6, offsets ±1; a broken outline becomes an ellipse (head,
  torso) or the style's own shape (end, piece). `ModuleEditing` validates with its own,
  similar ranges and refuses zero/negative/NaN sizes with a message.

### 14.3 Drawing

- `FigureGeometry.LayerFills(layer)` gives (colour slot, path) pairs back to front: skin
  first (torso outline, capsules, ellipses, skin shapes, unioned as before), then each other
  slot in first-appearance order. `LayerSkin` is their union, so silhouettes, hit-testing,
  `PartClip.Body` and highlighting include shapes. A mannequin's layers are one skin fill
  each and draw pixel-identical to before (`ShapeBodyRenderingTests` pins it).
- Every fill in a layer shares that layer's seam mask, which only covers what *earlier*
  layers painted: two colours in one layer always get a line between them, while a joint to
  an earlier layer stays seamless even across colours (a red head on a skin torso shows no
  neck line). A skin shape meant to sit in front of a coloured one in the same layer would
  be hidden, so the rig puts such shapes in a later layer. In Separate mode, pieces sharing a
  layer and a colour (a limb and its hand) still union.
- A colour slot the character never set falls back to the resolved skin colour (after any
  per-panel skin override).
- `StickerArtPieces.Sides(figure, part)` is the one place sticker parts learn which sides
  they draw on, minus absent ones; covers, art and text all use it.
- Close-up framing (`CharacterFigure.Arrange`) frames from `Regions.Head` (the face
  ellipse), so a head shape much larger than its face can be cropped - check when touching it.

### 14.4 Library and presets

- One `ModuleShape` per file under `Library/shapes/<kind>/<category>/<name>.json`, written in
  the project format; the key comes from the resource path. Files carry no `source`:
  `ShapeLibrary.Instantiate` sets `library:shapes/<key>`. The existing `Library\**\*`
  embed rule covers the folder, and sticker loading ignores it.
- Box conventions the rig honours: heads, torsos and pieces fill the unit box (Width ×
  Height, or thickness × bone length for a piece, set the aspect). Hands and feet keep their
  own proportions: long side 1, centred, top at y = 0, so (0.5, 0) is the wrist/ankle and the
  end hangs in an EndSize box; a shoe only fills its top ~0.44, and the ground goes under
  what is drawn. Mitten and claw have their thumb/claws on +x.
- The shapes were generated by a throwaway script (rounded polygons with bowed edges and
  filleted corners, centripetal Catmull-Rom splines through hand-placed points, unions of
  circles, a polar wobble; one least-squares cubic per segment) and checked on rendered
  contact sheets. To add one: drop an original CC0 `ModuleShape` file in the right folder -
  closed, at least 3 anchors, filling its box - and `ShapeLibraryTests` checks it.
- `CreaturePresets` (StickerLibrary): Default (egg head, pear torso, tube arms with ball
  hands, tube legs with shoes), Bean, Mr. Square, Hero (square head on an upside-down
  triangle), Robot (Pieces, Separate), Stick figure (a hair-thin torso rather than none, so
  arms and legs meet on a line), Blob, Big-head, Head-on-legs; creatures stand about
  0.6–1.05 tall. Every read builds fresh library copies.
- `ModuleEditing`: a head or torso always stays (removing both is refused); giving a limb an
  Upper or Lower piece switches it to `Pieces`; `SetAnchors` stores the outline as given
  (re-fitting to the box is Edit Points' job, slice 7) and drops the library `Source`.

### 14.5 Posing on the page (slice 6)

- `CharacterPosing.Limbs(character, instance)` and `TrunkParts(character)` say which handles a
  character has: the hips always, the chest only with a torso, the head only with a head
  module (`HasHead`, not `Has(Head)`: a head-less body's head region is its face on the
  torso). The page's `LimbHandles`, `BendHandles` and `TrunkHandles` go through them, and
  drawing, hit-testing, cursors and the status hint (`PoseDots`) all read those, so a
  missing part has nothing to show or grab. `TiltHead` with no head is a no-op.
- `PosePresets.Apply` and `Turn`'s translation skip limbs the body hasn't got instead of
  solving for a zero-length one; `MaxHipsDrop` goes by the longer leg.
- `Turn` can't round-trip every preset on a body missing parts: where two presets look the
  same there (an armless Blob's Cheer is just standing), `Recognize` returns whichever the
  gallery lists first. Mirror pose on a one-armed body moves the pose onto the missing arm.
- `FigureGeometry.Covers` (the guard on Skia unions) checks the union's tight bounds as well
  as a 3×3 grid of samples: a stick figure's thin legs walking side on slipped between the
  samples and the union dropped them. Coverage tests must not rely on grid samples alone.
- A shape body builds in ~1.5 ms against the mannequin's ~0.3 (Release): `ModularRig.Build`
  draws twice (standing, to find the ground; then posed), and `Recognize`, the pose gallery
  and `Turn` apply all 11 presets. Caching the standing pass is the cheap fix if it shows.

