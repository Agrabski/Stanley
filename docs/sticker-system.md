# Sticker system — design

**Status: proposed, not implemented.** This follows the character authoring POC
(`docs/character-authoring-poc.md`) and revises the sticker parts of
`docs/character-and-project-plan.md`. Where the two disagree, this document wins.
§17 lists the decisions that need the user's sign-off before implementation starts.

Scope: how a character gets hair, a face, clothes and accessories. That covers
choosing, wearing, recolouring, expressions, drawing your own and importing art. It
also covers how all of that follows the body sliders, the posing and the two views
that already exist. Named looks (revisions) are covered only as far as stickers need
them.

## 1. Why the plan needs revising

The plan was written before the body existed. It assumed a drawn cutout rig: every
sticker is SVG art, **rigidly attached to one bone**, with art for each view angle.
It also assumed one `build` number, handled by a 9-slice `stretch.json` or by
breakpoint variants. Since then the POC has built the following:

- a body **generated from five sliders** (Height, Weight, Muscle, Head size, Shape).
  Torso width, belly, hips and limb thickness all change continuously.
- **posing by IK**: elbows and knees bend, the spine leans and the hips drop.
- **front and side views** generated from the same numbers, with three-quarter still
  to come.

Rigid per-bone art breaks for anything that covers the body. For example, a shirt has
to cover a torso whose outline depends on three sliders, and a sleeve has to bend at
the elbow. With the planned approach, every garment would need hand-made stretch
regions or breakpoint art, for every view, and would still show seams at every joint.
Head items do not have this problem. The head is a rigid ellipse, so hair, faces, hats
and glasses work well as drawn art.

## 2. Decisions

1. **A sticker is anything a character wears in a slot.** Hair, eyes, a T-shirt and a
   watch are all stickers. The user-facing unit stays the one the plan named. What
   changes is what a sticker is made of.
2. **A sticker is made of parts. Each part either *covers* a body region or places
   *art* on it.**
   - A **cover** part is generated from the body itself: "the arm from the shoulder
     to 40% of the way to the wrist, 1% loose, in the `top` colour". It is the same
     capsules and torso outline the mannequin is built from, clipped and slightly
     enlarged. It follows every slider, every pose and every view (including
     three-quarter, once the rig generates it) with no art at all.
     **Clothing is covers by default.**
   - An **art** part is an SVG (or PNG) drawn over a *template* of a body region. It
     is mapped onto the character's actual region, either rigidly (**Pin**) or bent
     to its outline (**Warp**). Faces, hair, hats and details on clothes such as
     collars, logos and stripes are art.
3. **Art is mapped from template space, not attached to bones.** Every template is
   the default body (`BodyShape.Default`, standing) at 1000 units tall. Stanley maps
   the template's region (head ellipse, torso outline, limb segment) onto the
   character's region. Warp replaces the plan's 9-slice `stretch.json`: it follows
   the actual torso outline, which a 9-slice cannot. This is still bounded, declared
   non-uniform scaling, not mesh weighting.
4. **The figure is drawn as depth groups, not one silhouette.** The rig splits the
   body into ordered groups: back, legs, torso, head, arms and front (§4). Each
   sticker part goes in with the body part it sits on, so a sleeve is drawn with its
   arm and a forearm across the chest is drawn over the shirt. Ink is suppressed
   where a group joins its parent, so shoulders and hips stay seamless. This also
   fixes a known POC limitation: arms crossing the body currently vanish into the
   silhouette.
5. **Colours belong to the character, and stickers refer to them by slot name.** A
   T-shirt fills with `top`. If the user picks a new top colour, swapping the
   T-shirt for a sweater keeps that colour. Drawn art tags its fills with
   `class="slot-hair"`. Each tagged shade keeps its offset from the sticker's default
   colour, so recolouring keeps the artist's shading.
6. **Expressions are variants picked by slot name.** `PoseData.Expression` (already
   stored) maps a slot to a variant (`eyes → happy`). There is one standard variant
   vocabulary (§7), so every expression preset works on every character. A sticker
   that lacks a variant falls back to its default. This means custom stickers mix
   freely with the library.
7. **Projects are self-contained.** Wearing a library sticker copies it into the
   character's folder. A later Stanley release that changes its library never repaints
   an existing comic. A library copy that is unmodified and no longer worn anywhere is
   dropped on save, so trying things on leaves no files behind.
8. **SVG is read through Stanley's own subset parser, not an SVG library** (§6.1).
   The subset is the contract: it guarantees that recolouring and mapping work, it is
   AOT-clean, and it avoids a licence question (§17 Q4).

### Cover vs. drawn clothing

| | Drawn pieces per bone (plan) | Cover parts (this design) |
|---|---|---|
| Five body sliders | stretch region or breakpoint art per garment | automatic |
| Posing (elbow, knee, lean, crouch) | seams at every joint, hidden by overlapping art | automatic, same capsules as the body |
| Front / side / three-quarter | art per view, per variant | automatic |
| Authoring a new garment | a set of drawings | a few numbers |
| Look | anything | flat shapes following the body, plus drawn details |

Drawn clothing is still possible (a Warp art part on the torso) and remains the
escape hatch for garments that covers can't express.

## 3. Vocabulary

| Term | Meaning |
|---|---|
| **Sticker** | A wearable item: `sticker.json` plus art files, in `characters/<id>/stickers/`. |
| **Slot** | What the sticker is (`hair`, `top`, `eyes`, …): decides its default region, z-order and whether several stack (§8). |
| **Part** | One piece of a sticker: a cover or an art layer, on one region. A T-shirt has a body part and a sleeves part. Hair has a back part and a front part. |
| **Region** | A named piece of the generated body that parts attach to (§4.1). |
| **Wardrobe** | Every sticker in the character's folder, worn or not. |
| **Look** | What is worn and in which colours. The definition carries the default look. Named looks are revisions (the UI says "look", the code keeps `CharacterRevision`). |
| **Library** | The starter stickers shipped with Stanley, copied into a character when worn. |
| **Template** | The default body drawn as an SVG guide for one view. Art is drawn over it. |

## 4. The figure: regions and depth groups (rig side)

### 4.1 Regions

`BodyRig` already computes everything needed. `BodyFigure` gains a region frame for
each part of the body, as pure data (figure space, like the rest of the figure):

| Region | Cover range `from → to` | Template mapping | Examples |
|---|---|---|---|
| `head` | crown 0 → chin 1 (band of the head ellipse) | ellipse → ellipse, turns with the head | hair, eyes, mouth, hats, beanie (cover 0 → 0.35) |
| `neck` | base 0 → chin 1 | segment | turtleneck, scarf |
| `torso` | shoulder line 0 → crotch 1 | row by row through the torso outline (Warp); similarity at the pin point (Pin) | shirt body, trouser seat, prints, logos |
| `arm` | shoulder 0 → wrist 1, **through the elbow** | segment (Pin only in V1) | sleeves (short 0.4, long 1.0), watch |
| `leg` | hip 0 → ankle 1, through the knee | segment (Pin only in V1) | trouser legs, shorts (0.35), socks |
| `hand`, `foot` | whole ellipse | ellipse → ellipse | gloves, shoes |
| `skirt` | hip line 0 → ankles 1: the hull around both legs | row by row | skirts, dresses, coat tails |

- One `from`/`to` on the whole limb chain means sleeve length is a single number
  that bends with the elbow (two capsules, cut perpendicular to the segment at the
  right distance).
- Cover parts use the figure **inflated** by the part's ease: capsule and ellipse
  radii grow by `ease`, and the torso outline moves out along its normals. `ease` is
  a fraction of the character's own height (0.01 = 1%), so a toddler's T-shirt fits
  as snugly as an adult's.
- `skirt` is the one compound region. For each row it takes the outer edges of both
  leg covers and adds `flare` toward the hem. Walking and running spread it; Sit
  flattens it over the thighs.
- Limb parts apply to both limbs unless the part says `side: left|right`. Sides are
  the character's own, as in VRM, so a mirrored placement doesn't swap them.

### 4.2 Depth groups

`BodyFigure` gives its shapes as ordered groups, and the renderer paints them back to
front:

```
front view:  back │ legs │ torso (+neck) │ head │ arms │ front
side view:   back │ far arm │ body (torso, neck, legs) │ head │ near foot │ near arm │ front
```

- This generalises the side view's existing two layers (`Limbs`/`NearLimbs`) and
  keeps its choices: the legs merge into the body, and only the near foot and arm are
  drawn over it. In the front view, arms are in front of the body (hands on hips,
  Think). Arms held behind the back (a per-limb "behind" flag in the pose) are a
  later addition.
- `back` and `front` contain no body. They hold parts that ask for them: the back of
  the hair or a cape (`back`), or an item held in front of everything (`front`).
- Within a group, the order is skin, then parts by slot z-order (§8), then stacking
  order within the slot.
- **Ink**: each group is inked around the union of its skin and cover parts, and each
  cover part's own outline (hems, cuffs) is inked inside that union. Both are cut away
  inside the group's **attachment zones**: a disc at each joint where the group joins
  its parent (shoulders, hips, neck), computed by the rig. Shoulders and hips stay
  seamless, and an arm that crosses the chest gets a full outline.
- The side view currently puts the character's **left** limbs nearest the viewer
  when facing right. Anatomically, the right side would be the near one. This does
  not matter for a bare mannequin, but it will for a watch on one wrist. Decide
  before side-specific stickers ship (§17 Q7).

## 5. Parts

```jsonc
// characters/<id>-alice/stickers/<id>-t-shirt/sticker.json
{
  "colors": { "top": "#3b6fd8" },           // default colour for each colour slot the sticker uses
  "id": "…",
  "name": "T-shirt",
  "parts": [
    { "cover": { "color": "top", "ease": 0.01, "from": 0, "to": 0.9 }, "name": "body", "region": "torso" },
    { "cover": { "color": "top", "ease": 0.01, "from": 0, "to": 0.4 }, "name": "sleeves", "region": "arm" }
  ],
  "slot": "top",
  "source": "library:top/t-shirt",          // only while it's an unmodified library copy
  "variants": ["default"]
}
```

```jsonc
// …/stickers/<id>-bob/sticker.json: art in variants/default/front.svg and profile.svg,
// one SVG layer per part ("back", "front")
{
  "colors": { "hair": "#5a3a22" },
  "name": "Bob",
  "parts": [
    { "art": { "mapping": "warp" }, "depth": "back", "name": "back", "region": "head" },
    { "art": { "mapping": "warp" }, "name": "front", "region": "head" }
  ],
  "slot": "hair",
  "variants": ["default"]
}
```

A part has exactly one of `cover` or `art`, and these optional settings (null means
the default, so files stay sparse):

| Field | Values | Default | Meaning |
|---|---|---|---|
| `side` | `left`, `right` | both | Limb regions only. |
| `depth` | `back`, `front` | the region's group | Pull a part out of its region's group (hair back, cape). |
| `blend` | `cut` | paint | Subtract from this sticker's other parts in the same group, so what's underneath shows through: a V-neck (a Warp art triangle at the neck), an open jacket. |
| `clip` | `body`, `sticker` | none | Clip to the body (tattoos, face paint) or to this sticker's cover parts (stripes and prints that must never spill past the shirt). |

**`cover`**: `from`, `to`, `ease`, `flare` (skirt only), `color` (a colour slot name).

**`art`**: `mapping` (`pin` or `warp`), plus the adjustments that on-canvas handles
write: `offset`, `scale`, `rotation` (in template units, applied before mapping) and
`keepReadable`.
- **Pin**: the layer keeps its shape. It is placed by the region's similarity
  transform at the layer's centre and scaled by the region's size (head height, torso
  length, segment length). Use it for eyes, mouths, noses, glasses, logos, buttons
  and a watch.
- **Warp**: every point goes through the region mapping, so the art hugs the outline.
  Use it for hair, hats, hems and prints. Curves are split before mapping, and strokes
  are applied after mapping, so line weight never distorts.
- **`keepReadable`** (Pin only): when the placement is mirrored, the part flips back
  about its own centre, so a logo with text never reads backwards.
- Defaults by slot: face slots Pin, everything else Warp.

## 6. Drawn art

### 6.1 The SVG profile

Stanley reads a subset of SVG with its own parser: `System.Xml.Linq` for the file,
and `SKPath.ParseSvgPathData` for the shapes in the renderer. The parsed form
(`StickerArt`) is plain data in ProjectModel. On save the file is **written back
byte for byte**, never re-serialised, so Inkscape metadata survives and an unchanged
save produces an empty diff.

- **Supported**: `svg` (viewBox), `g` (transform, class), `path`, `rect`, `circle`,
  `ellipse`, `line`, `polyline`, `polygon`; `fill`, `stroke`, `stroke-width`,
  `stroke-linecap`/`-linejoin`, `opacity`, `fill-opacity`, `fill-rule`, `transform`;
  inline `style="…"` (Inkscape) and simple `.class { … }` rules in `<style>`
  (Illustrator's default export).
- **Not supported**: gradients, filters, masks, clip paths, `text`, embedded images
  and CSS beyond the above. Import lists what it skipped or approximated ("2
  gradients drawn as their first colour; 1 text skipped: convert it to a path"), so
  nothing is dropped silently.
- **Parts are top-level layers**, matched by `inkscape:label` or `id` to the
  sticker's part names. A file with no layers is the whole of a single-part sticker.
- **Colour slots**: `class="slot-<name>"` on an element (or an ancestor) makes its
  fill and stroke follow that colour slot. The colour written in the file is the
  sticker's default for the slot. Other shades of it tagged with the same class keep
  their lightness and saturation offset (OKLCH) and take the new hue. An artist
  paints with ordinary colours and tags them, with no extra syntax, and the file
  looks right in any viewer.
- **Line weight is relative**: the template's body outline is 3 units wide. A 3-unit
  stroke in any sticker draws at the character's ink width
  (`PageRenderer.CharacterStrokeMm`), whatever size the character is on the page,
  and a 1.5-unit stroke draws at half of it. Sticker lines therefore always match the
  body outline.

### 6.2 Templates

`StickerTemplates.Export(view, slot)` writes an SVG containing:
- the default body for that view, 1000 units tall, as a locked and faded guide layer
  marked `data-stanley-guide`. The importer ignores this layer.
- empty, named layers for the slot's usual parts (hair: `back`, `front`).
- a viewBox cropped to the slot's region, so Inkscape opens zoomed on the head for
  hair.
- `data-stanley-view` and `data-stanley-slot` on the root, so importing the file
  asks no questions.

### 6.3 Views and missing art

Files are named `variants/<variant>/<view>.svg` (`front`, `three-quarter`,
`profile`). A missing view falls back to the nearest drawn view: three-quarter
falls back to front, then profile; profile falls back to three-quarter, then front.
It always draws something, and the Look tab marks the sticker "no side view" so the
gap is visible instead of silent. The starter library ships front and profile art
for every drawn sticker.

### 6.4 PNG

PNG art (LFS, per the existing `.gitattributes`) is supported for Pin parts only,
with fixed colours in V1. Tint masks (`front.hair.png`: alpha marks where the `hair`
colour applies) and warped rasters (Skia `DrawVertices`) come later.

## 7. Variants and expressions

`variants` lists the sticker's variant folders, and the first one is the fallback.
To pick a variant for a worn sticker, `PoseData.Expression[slot]` is used if the
sticker has that variant. Otherwise `neutral` is used if present, and otherwise the
first variant. `PoseData` already stores `Expression`, so there is no schema change.

The standard vocabulary for the library and the presets:

| Slot | Variants |
|---|---|
| `eyes` | neutral, happy, sad, angry, wide, closed, wink, halfClosed |
| `brows` | neutral, raised, angry, sad, skeptical |
| `mouth` | neutral, smile, grin, open, shout, frown, o, smirk |

`ExpressionPresets` (Stanley.Editing, next to `PosePresets`). Each preset sets all
three slots:

| Preset | eyes | brows | mouth |
|---|---|---|---|
| Neutral | neutral | neutral | neutral |
| Happy | happy | neutral | smile |
| Laughing | happy | raised | grin |
| Sad | sad | sad | frown |
| Angry | angry | angry | frown |
| Surprised | wide | raised | o |
| Scared | wide | sad | open |
| Skeptical | halfClosed | skeptical | smirk |
| Wink | wink | raised | grin |
| Talking | neutral | neutral | open |
| Shouting | angry | angry | shout |
| Asleep | closed | neutral | neutral |

Any slot can have variants; the vocabulary above only makes presets portable. A
character's "Tophat" could have `on`/`tipped`, set per panel by the same mechanism.

**Build breakpoints** (from the plan) remain designed but unbuilt. A sticker would
declare `buildBreakpoints: { "slim": 0, "heavy": 1 }` and the renderer would snap to
the nearest one. Build it only when a real garment fails with Warp.

## 8. Slots

A static table (`StickerSlots`, the same enum + lookup shape as `BodyPresets`). Any
other slot name is allowed and behaves like `accessory`.

| Slot | Default region | z (in its group) | Stacks | Colour slot |
|---|---|---|---|---|
| `bottom` | torso + leg | 10 | – | `bottom` |
| `shoes` | foot | 15 | – | `shoes` |
| `top` | torso + arm | 20 | – | `top` |
| `outer` | torso + arm | 30 | – | `outer` |
| `eyes` | head | 40 | – | `eyes` |
| `nose` | head | 42 | – | `skin` |
| `brows` | head | 44 | – | `hair` |
| `mouth` | head | 46 | – | – |
| `facialHair` | head | 48 | – | `hair` |
| `hair` | head | 50 | – | `hair` |
| `glasses` | head | 60 | – | `glasses` |
| `headwear` | head | 70 | – | `hat` |
| `accessory` | any | 80 | yes | `accent` |

- "Stacks" controls what a gallery click does. It replaces the worn item in the slot,
  or adds a layer (Ctrl+click adds in any slot). Data-wise every slot is a stacked
  list, as the plan says.
- A dress is a `top` whose parts reach into `skirt`. Wearing one alongside trousers
  is allowed and looks like leggings, and the Look tab's "None" removes the trousers.
- A per-part z override stays deferred, as in the plan.

## 9. Colours

Resolution, lowest to highest priority: the sticker's `colors` default → the
definition's `ColorSlots` → the revision's `ColorSlotValues` → the instance's
`ColorSlotOverrides`. The sticker's default is only a fallback. A slot is written
into `ColorSlots` only when the user picks a colour, and from then on it survives
outfit changes. `skin` already works this way.

## 10. Data model

Changes to existing types. Nothing reads these fields yet, and the format is 0.x, so
no migration is needed. Old keys such as `stickerSlots` are ignored on read, and a
missing `stickers` loads as empty (the same way `LoadCharacter` fills in a missing
`body`).

```csharp
// Characters/CharacterDefinition.cs
public sealed record CharacterDefinition(
    CharacterId Id, string Name, BodyShape Body, Skeleton Skeleton,
    SortedDictionary<string, ColorValue> ColorSlots,
    SortedDictionary<string, IReadOnlyList<StickerId>> Stickers);   // was StickerSlots: slot -> worn by default, bottom to top

// Characters/Stickers/Sticker.cs (replaces Sticker/StickerKind; StretchRegion and stretch.json go)
public sealed record Sticker(
    StickerId Id, string Name, string Slot,
    IReadOnlyList<StickerPart> Parts,
    SortedDictionary<string, ColorValue> Colors,
    IReadOnlyList<string> Variants,
    string? Source = null,                                  // "library:<key>" while an unmodified library copy
    SortedDictionary<string, double>? BuildBreakpoints = null);

public sealed record StickerPart(
    string Name, BodyRegion Region,
    PartCover? Cover = null, PartArt? Art = null,           // exactly one
    LimbSide? Side = null, PartDepth? Depth = null, PartBlend? Blend = null, PartClip? Clip = null);

public sealed record PartCover(string Color, double From, double To, double? Ease = null, double? Flare = null);
public sealed record PartArt(ArtMapping Mapping, Point2D? Offset = null, double? Scale = null, double? Rotation = null, bool? KeepReadable = null);

public enum BodyRegion { Head, Neck, Torso, Arm, Hand, Leg, Foot, Skirt }
public enum LimbSide { Left, Right }
public enum PartDepth { Back, Front }
public enum PartBlend { Cut }
public enum PartClip { Body, Sticker }
public enum ArtMapping { Pin, Warp }
```

- `StickerSlotDefinition` (z-order + catalogue) goes. Z-order comes from the slot
  table, and the catalogue is a folder scan (`ListStickers`, like `ListCharacters`),
  since its order means nothing.
- `CharacterRevision.ActiveStickers` and `CharacterInstanceOverrides.ActiveStickerOverrides`
  keep their shape. They are **sparse per slot**: a slot that is present replaces the
  list below it (an empty list means "nothing in this slot", for example no hat for
  this panel), and an absent slot inherits.
- In memory, a character is its whole folder:

```csharp
/// Everything in one character's folder: the character editor's undoable document and what pages draw from.
public sealed record CharacterBundle(
    CharacterDefinition Definition,
    IReadOnlyDictionary<StickerId, StickerAsset> Wardrobe,
    IReadOnlyDictionary<CharacterRevisionId, CharacterRevision> Revisions);

public sealed record StickerAsset(Sticker Sticker, IReadOnlyDictionary<(string Variant, ViewAngle View), StickerArt> Art);

// Parsed SVG (plain data, ProjectModel). Source is the file text, written back verbatim.
public sealed record StickerArt(string Source, Rect2D ViewBox, IReadOnlyList<ArtLayer> Layers, IReadOnlyList<string> Skipped);
public sealed record ArtLayer(string Name, IReadOnlyList<ArtPath> Paths);
public sealed record ArtPath(string Data, ArtPaint? Fill, ArtPaint? Stroke, double StrokeWidth, double Opacity, bool EvenOdd);
public sealed record ArtPaint(ColorValue Color, string? Slot);
```

`ICharacterCatalog.Characters` becomes `CharacterId → CharacterBundle`, and the page
renderer's character dictionary does the same.

## 11. Files on disk

```
characters/<id>-alice/
  character.json                 # body, skeleton overrides, colours, stickers worn by default
  revisions/<id>-winter.json     # a named look: sparse stickers/colours per slot
  stickers/
    <id>-t-shirt/
      sticker.json               # covers only: no art files at all
    <id>-bob/
      sticker.json
      variants/default/front.svg
      variants/default/profile.svg
    <id>-round-eyes/
      sticker.json
      variants/neutral/front.svg   variants/neutral/profile.svg
      variants/happy/front.svg     variants/happy/profile.svg
      …
```

- This is the plan's layout without `stretch.json`.
- `ProjectRepository` gains `ListStickers`, `DeleteSticker` and `Read/WriteStickerArt`.
  Paths still come only from `ProjectPaths`.
- `ComicProject.Save` writes each bundle and deletes the stickers removed since the
  last save, the same way it prunes pages, panels and characters. **Library copies
  are tidied on save**: a sticker with `source` set that isn't worn by the
  definition, by any revision or by any panel override is deleted. Editing a
  library copy in any way clears `source`, which makes it the user's own.

## 12. Resolving and drawing

`CharacterResolver` (Rendering, as the POC doc §3.3 planned) takes a bundle, the
issue's revision for that character and one instance, and returns a
`ResolvedCharacter`:

1. **Body**: definition → revision → instance (the POC's chain, unchanged).
2. **Worn stickers per slot**: definition → revision → instance override. They are
   then ordered by slot z-order and stacking order.
3. **Colours** (§9) and, for each sticker, a **variant** (§7).
4. The **view** and **pose** come from `instance.Pose`.

The renderer (`ICharacterRenderer` now takes a `ResolvedCharacter`) then:

1. Builds the figure: `BodyRig.Build(body, view, skeleton, pose)` gives groups and
   regions.
2. Turns each part into a figure-space path. A cover is its region's range of the
   inflated figure. Art is the SVG layer for the chosen variant and view (with the
   §6.3 fallback), mapped by Pin or Warp and recoloured. Cut and clip are then
   applied within the sticker.
3. Puts each path in its group (from `depth`, the region, and the side for limbs)
   and paints the groups in order (§4.2).
4. Hit-testing uses the union of everything, so a click on hair or a hat selects the
   character.

Caching follows the mannequin's approach: figure-space paths per (bundle, resolved
look, view, pose), bounded so a limb drag can't grow it. SVG is parsed once, on load.

## 13. UX

### 13.1 Character editor: a Look tab next to Body

Ribbon groups, left to right:

| Group | Contents |
|---|---|
| Look | *Look ▾* (Default or a named look; slice 6) |
| Hair | gallery |
| Face | Eyes, Brows, Mouth, Nose, Facial hair galleries; *Preview expression ▾* |
| Clothes | Top, Outer, Bottom, Shoes galleries |
| Accessories | Hat, Glasses, Other galleries |
| Colours | a swatch dropdown for each colour slot in use (Skin, Hair, Eyes, Top, …) |
| Art | *Draw your own…*, *Import…* |

- Each gallery starts with **None**, then this character's wardrobe, then the
  library. Every item is **previewed on this character**, the way the Pose gallery
  previews on the selected character. Clicking wears the item, as one undo step.
- **Colours** shows only the colour slots the worn stickers use. Each is a swatch
  dropdown like Skin.
- Clicking a worn sticker on the character in the pane selects it and shows a
  contextual **Sticker** tab:
  - covers: *Length*, *Sleeves* and *Fit* sliders (the parts' `to` and `ease`, one
    undo step per drag).
  - art: move, scale and rotate handles on the canvas (`offset`, `scale`, `rotation`)
    and a *Hug the shape* toggle (Warp or Pin).
  - both: *Forward/Back* within the slot, *Take off*, and *Remove from wardrobe*.
- *Preview expression* shows the face in any expression while you pick eyes, brows
  and mouths. It is a preview only: expressions are set per panel.
- Warnings are shown inline, never in a modal: "no side view", or which expression
  variants are missing.

### 13.2 Draw your own, and import

- **Draw your own…** (from any gallery) creates a sticker for that slot, writes its
  template (§6.2) as the art for the current view, and opens it in the system's SVG
  editor. Stanley watches the file. Each save re-imports it as one history entry, so
  the character updates live and undo works. There are no dialogs. Repeat in the side
  view to draw that view.
- **Import…** accepts SVG or PNG. A file made from a template imports as-is. Any
  other file becomes a single Pin part on the slot's region, centred and fitted, with
  the handles selected so it can be placed immediately.

### 13.3 Page editor

- Character tab: an **Expression** gallery next to Pose, previewed on the selected
  character. Also a **Look** dropdown when the character has named looks (issue
  default, or a named look for this panel → `RevisionOverride`).
- Right-click › Expression, and right-click › *This panel only* › put on or take off
  (writes `ActiveStickerOverrides`, e.g. sunglasses for one shot).
- Posing handles keep hit priority over the character, and stickers never block them.

## 14. Where code goes

| Project | New / changed |
|---|---|
| ProjectModel | `Sticker`/`StickerPart`/… (§10), `StickerSlots`, `CharacterDefinition.Stickers`, `CharacterBundle`, `StickerArt` + `StickerSvg.Parse`, `BodyFigure` groups/regions/inflate, `ProjectRepository` sticker listing/art I/O; drop `StickerKind`, `StretchRegion`, `StickerSlotDefinition` |
| Rendering | `CharacterResolver`, `ResolvedCharacter`, `RegionMapping` (Pin/Warp), `StickerRenderer` (cover/art paths, recolour, cut/clip), group painting + attachment-zone ink in the renderer (the mannequin becomes "a figure with no stickers") |
| Editing | `ExpressionPresets`, `LookEditing` (wear/take off/stack/recolour, per slot), `StickerFitting` (cover sliders, art offset/scale/rotation) |
| Editors | Look tab, contextual Sticker tab, galleries, colour swatches, Expression gallery on the page's Character tab, `CharacterEditorViewModel : EditorViewModel<CharacterBundle>`, `ComicProject` bundle load/save/prune/tidy, external-edit file watch |
| Library (new, Avalonia-free) | `Stanley.StickerLibrary`: the starter stickers as embedded resources in the project format itself, plus `StickerTemplates` |

## 15. Delivery slices (each a PR, each green on its own)

1. **Layered figure.** `BodyFigure` groups, regions and inflate. The renderer paints
   groups with attachment-zone ink. No stickers yet. Visible: arms crossing the body
   now read. Tests: region frames (template regions map onto themselves for the
   default body), ink-free shoulders and hips at rest (pixel probe), and a forearm
   across the chest being inked.
2. **Cover stickers end to end.** Model, bundle, repository, resolver, cover
   rendering, the Look tab with the clothing galleries and colours. The starter
   garments need no art: T-shirt, long sleeve, tank top, shirt, hoodie, jacket,
   dress, skirt, shorts, trousers, shoes, boots. V-necks and open fronts (cut art)
   follow in slice 3. Tests: JSON shape, the
   fallback chain, a pixel probe for chest = `top` and bare forearm = skin, a sleeve
   bending with the elbow, a wear = one undo step, save/open/prune/tidy.
3. **Drawn stickers.** The SVG profile parser, templates, Pin/Warp, recolouring, view
   fallback. Starter hair (six styles) and faces (three eye/brow/mouth sets, neutral
   only), front and side. Tests: the parser on real Inkscape and Illustrator files,
   the skipped-element report, the recolour shade offset, Warp keeping the template's
   outline on the character's outline, relative stroke width, verbatim save.
4. **Expressions.** Variant selection, `ExpressionPresets`, the Expression gallery on
   the page, and the starter face variants. Tests: preset → variant per slot, missing
   variant → neutral.
5. **Draw your own and import.** Template export, the external edit round trip, and
   arbitrary SVG/PNG placement handles. Tests: a template file imports with no
   questions, a re-import is one history entry.
6. **Looks.** Named revisions in the Look tab, the issue default, the per-panel look,
   and put on/take off for one panel only.

Slices 1 and 2 give a dressed character with no art pipeline at all. Slice 3 is the
first that needs SVG.

## 16. Later

Build breakpoints (§7), PNG tint masks and warped rasters, three-quarter art (covers
get it free once the rig does), per-part z overrides, arms behind the body, "turn
around" as distinct from mirror, gaze (the VRM `LeftEye`/`RightEye` bones moving the
pupils), a talking mouth when a bubble's tail points at the character (after
character-bound tails), a user library shared across projects, flatten to editable
layers, and `stanley sticker import` on the CLI.

## 17. Open questions (please confirm)

1. **Covers as the default for clothing**, instead of drawn per-bone art as the plan
   had. *Recommended*: yes (§1, §2). This is the one real change of direction.
2. **Licence for the starter library art.** Worn stickers are copied into users'
   comics. If the art were AGPL like the code, every comic using it would arguably
   contain AGPL material. *Recommended*: release the library art (not the code) as
   **CC0**, so comics made with it carry no obligations.
3. **Size of the starter library for V1.** *Recommended*: the garments in slice 2,
   about six hairstyles, and three face sets with the full expression vocabulary, in
   front and side, all simple flat vector art in the POC's style.
4. **SVG rendering: our own subset, or Svg.Skia?** Svg.Skia is MIT, but it is built
   on a fork of SVG.NET, which is MS-PL. The FSF lists MS-PL as GPL-incompatible, so
   it is a risky dependency for an AGPL app (re-check this at whatever version we'd
   pin). It is also reflection-heavy for NativeAOT. *Recommended*: our own subset
   (§6.1).
5. **Drop `stretch.json` and 9-slice** in favour of Warp, and leave build breakpoints
   unbuilt until a real case needs them. *Recommended*: yes.
6. **Tidy unused library copies on save** (§11), rather than keeping everything ever
   clicked. *Recommended*: yes.
7. **Which side faces the viewer in the side view.** The rig shows the left side of
   a right-facing character; anatomically it's the right. Fix it in slice 1 (it
   touches the rig, posing and mirror pose), or keep it and treat Flip as a pure
   mirror image? *Recommended*: fix it in slice 1, while the only thing that notices
   is an unclothed mannequin.
