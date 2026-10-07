# Sticker system — design

**Status: implemented** — every slice in §15 is built, and §18 records where the
code differs from this text. This follows the character authoring POC
(`docs/character-authoring-poc.md`) and revises the sticker parts of
`docs/character-and-project-plan.md`. Where the two disagree, this document wins.
§17 lists what has been decided since the first draft and what is still open.

Scope: how a character gets hair, a face, clothes and accessories. That covers
choosing, wearing, recolouring, patterned and textured fabrics, expressions, drawing
your own and importing art. It
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
   **A colour slot can also carry a pattern and a texture** (stripes, plaid, denim,
   knit, …; §9). They follow the same rules as the colour, so a plaid top stays plaid
   when the T-shirt becomes a hoodie. They are laid out on the body part they cover,
   so they move with the pose.
6. **Expressions are variants picked by slot name.** `PoseData.Expression` (already
   stored) maps a slot to a variant (`eyes → happy`). There is one standard variant
   vocabulary (§7), so every expression preset works on every character. A sticker
   that lacks a variant falls back to its default. This means custom stickers mix
   freely with the library.
7. **Projects are self-contained.** Wearing a library sticker copies it into the
   character's folder. A later Stanley release that changes its library never repaints
   an existing comic. A library copy that is unmodified and no longer worn anywhere is
   dropped on save, so trying things on leaves no files behind.
8. **SVG is read with VectSharp.SVG (LGPL-3.0), behind one adapter** (§6.1). It is
   a ready-made, pure-managed reader whose licence is compatible with the AGPL. The
   better-known Svg.Skia is not, because it is built on MS-PL code.
9. **The starter library's art is CC0**, not AGPL, so comics made with it carry no
   licence obligations (§17).

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
| **Fabric** | A colour slot's optional pattern and texture, on top of its colour (§9). |
| **Look** | What is worn and in which colours and fabrics. The definition carries the default look. Named looks are revisions (the UI says "look", the code keeps `CharacterRevision`). |
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
  right distance). Its fabric runs on unbroken past the elbow (§9.3).
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
front view:  back │ left leg │ right leg │ torso (+neck) │ head │ left arm │ right arm │ front
side view:   back │ far arm │ body (torso, neck, legs) │ head │ near foot │ near arm │ front
```

- This generalises the side view's existing two layers (`Limbs`/`NearLimbs`) and
  keeps its choices: the legs merge into the body, and only the near foot and arm are
  drawn over it. In the front view, arms are in front of the body (hands on hips,
  Think). Arms held behind the back (a per-limb "behind" flag in the pose) are a
  later addition.
- In the front view each arm and each leg is a group of its own, the character's
  right over its left (#120: one group per pair merged legs crossed at the shins, or an
  arm across the other, into one shape). So the thighs, which touch standing, still
  meet without a line, the right leg's attachment zones include the strip down the
  middle from the crotch as far as they touch at rest (`BodyRig.ThighsTouch`), besides
  its hip.
- `back` and `front` contain no body. They hold parts that ask for them: the back of
  the hair or a cape (`back`), or an item held in front of everything (`front`).
- Within a group, the order is skin, then parts by slot z-order (§8), then stacking
  order within the slot.
- **Ink**: each group is inked around the union of its skin and cover parts, and each
  cover part's own outline (hems, cuffs) is inked inside that union. Both are cut away
  inside the group's **attachment zones**: a disc at each joint where the group joins
  its parent (shoulders, hips, neck), computed by the rig. Shoulders and hips stay
  seamless, and an arm that crosses the chest gets a full outline.
- Facing right, the character's own **right** limbs are the near ones, as for a
  real person (fixed alongside this design, §17). A watch on the right wrist is
  therefore in front, side on. A mirrored placement is a mirror image, not the
  character turned around.

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

A part has exactly one of `cover` or `art` (an `art` part may also carry `text`, see
§19), and these optional settings (null means the default, so files stay sparse):

| Field | Values | Default | Meaning |
|---|---|---|---|
| `side` | `left`, `right` | both | Limb regions only. |
| `depth` | `back`, `front` | the region's group | Pull a part out of its region's group (hair back, cape). |
| `blend` | `cut` | paint | Subtract from this sticker's other parts in the same group, so what's underneath shows through: a V-neck (a Warp art triangle at the neck), an open jacket. |
| `clip` | `body`, `sticker`, `clothes` | none | Clip to the body (tattoos, face paint), to this sticker's cover parts (stripes that must never spill past the shirt), or to the covers of everything *else* worn in the same group - the garment underneath, for prints (§19). |

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

### 6.1 Reading SVG

**Reader: [VectSharp.SVG](https://github.com/arklumpus/VectSharp)** (`VectSharp.SVG`
on NuGet), wrapped by one Stanley adapter, `StickerSvg` in Stanley.Rendering.
- **Licence**: LGPL-3.0-only. It is compatible with Stanley's AGPL-3.0: LGPLv3 is
  GPLv3 with extra permissions, and AGPLv3 §13 allows combining with GPLv3 code.
  Its dependencies are ExCSS (MIT), VectSharp (LGPL-3.0-only, with bundled
  Arimo/Tinos/Cousine fonts under Apache-2.0) and System.Collections.Immutable (MIT).
- **Why it**: it is pure managed code (netstandard2.0, no native library), so it runs
  on Linux as is. It reads everything stickers need: `path` and the basic shapes,
  `g`, `use`/`symbol`, transforms, inline styles and `<style>` sheets (via ExCSS),
  linear and radial gradients, clip paths and `text`. Each drawn element comes out as
  a drawing action tagged with the element's `id`.
- **Rejected**:
  - **Svg.Skia** (MIT) sits on `Svg.Custom`, a build of SVG.NET, which is MS-PL.
    The FSF lists MS-PL as GPL-incompatible.
  - **SharpVectors** (BSD-3) targets Windows only.
  - **resvg/usvg** (Apache-2.0 OR MIT) has the best SVG normaliser, but it is Rust.
    Its C API only renders and returns bounding boxes, with no access to the element
    tree, and the .NET wrapper does the same.
  - **Skia's own SVG module** is not exposed by SkiaSharp
    ([mono/SkiaSharp#2689](https://github.com/mono/SkiaSharp/issues/2689), still open).
  - **SkiaSharp.Extended.Svg** (MIT) is deprecated.

How the adapter uses it:
1. With `System.Xml.Linq`, it finds the top-level layers (`inkscape:label` or `id`),
   skips the guide layer, reads the root's `data-stanley-*` metadata, and resolves
   each element's `slot-*` class (its own or an ancestor's). Each drawable element
   gets a unique `id`, which VectSharp passes through as the action's tag, and the
   adapter maps that tag back to (layer, slot).
2. It parses **one layer at a time** (the root, `defs` and styles plus that layer)
   with `Parser.FromString`, so every part gets its own drawing.
3. It walks the actions: transforms are accumulated, path segments (arcs turned into
   curves) become `SKPath`s in viewBox space, solid and gradient brushes become Skia
   paints (gradient stops are recoloured by slot too), and stroke width, caps, joins
   and dashes are kept. Text arrives as text actions, which the adapter turns into
   outlines with VectSharp's fonts. Fonts VectSharp doesn't have are substituted and
   reported ("convert text to paths in Inkscape to keep the font").
4. Anything it doesn't map (blur and colour-matrix filters, masks, embedded images)
   is **reported at import** ("1 blur ignored; 1 embedded image skipped"), so
   nothing is dropped silently.

Risks, and why the adapter exists:
- The author calls its SVG input "limited".
- It has one maintainer. The last stable release is 1.10.2 (Dec 2024), with 1.10.3
  alphas on master.
- Its project declares no trimming or AOT compatibility (`IsTrimmable` /
  `IsAotCompatible`).

Slice 4 therefore starts with a spike: real Inkscape and Illustrator files, plus a
trimmed publish with the AOT analyzers on. If VectSharp falls short, only
`StickerSvg` changes. The fallback is the small subset parser the first draft
proposed (System.Xml plus `SKPath.ParseSvgPathData`).

Conventions that hold whichever reader is behind the adapter:
- **The file is kept as written.** The bundle holds the file text; parsing is
  derived and cached. On save the file is written back byte for byte, so Inkscape
  metadata survives and an unchanged save produces an empty diff.
- **Parts are top-level layers**, matched by `inkscape:label` or `id` to the
  sticker's part names. A file with no layers is the whole of a single-part sticker.
- **Colour slots**: `class="slot-<name>"` on an element (or an ancestor) makes its
  fill and stroke follow that colour slot, including its pattern and texture (§9).
  Add `solid` (`class="slot-top solid"`) for the colour only. The colour written in
  the file is the sticker's default for the slot. Other shades of it tagged with the
  same class keep their lightness and saturation offset (OKLCH) and take the new
  hue. An artist paints with ordinary colours and tags them, with no extra syntax,
  and the file looks right in any viewer. `class="side-left"`/`"side-right"` tags an
  element for split eyes the same way (§21) - unrelated to a limb part's own `side` (§5).
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
| `mouth` | neutral, smile, grin, open, shout, frown, o, smirk, doubtful |

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
| Skeptical | neutral | skeptical | doubtful |
| Seductive | halfClosed | skeptical | smirk |
| Wink | wink | raised | grin |
| Talking | neutral | neutral | open |
| Shouting | angry | angry | shout |
| Asleep | closed | neutral | neutral |

Any slot can have variants; the vocabulary above only makes presets portable. A
character's "Tophat" could have `on`/`tipped`, set per panel by the same mechanism.

Outside the face, variants are usually **styles** - ways to wear a sticker (a hood up or
down, a cap's brim forward or back) - chosen per sticker rather than per slot, and kept
with the character, a named look or one panel: see §20. An expression for the slot still
wins over a chosen style.

**Build breakpoints** (from the plan) remain designed but unbuilt. A sticker would
declare `buildBreakpoints: { "slim": 0, "heavy": 1 }` and the renderer would snap to
the nearest one. Build it only when a real garment fails with Warp.

## 8. Slots

A static table (`StickerSlots`, the same enum + lookup shape as `BodyPresets`). Any
other slot name is allowed and behaves like `accessory`.

| Slot | Default region | z (in its group) | Stamps copies | Colour slot |
|---|---|---|---|---|
| `bottom` | torso + leg | 10 | – | `bottom` |
| `shoes` | foot | 15 | – | `shoes` |
| `top` | torso + arm | 20 | – | `top` |
| `print` | torso | 25 | yes | `print` |
| `outer` | torso + arm | 30 | – | `outer` |
| `eyes` | head | 40 | – | `eyes` |
| `nose` | head | 42 | – | `skin` |
| `brows` | head | 44 | – | `hair` |
| `mouth` | head | 46 | – | – |
| `facialHair` | head | 48 | – | `hair` |
| `hairBack` | head | 49 | – | `hair` → own key (§22) |
| `hair` | head | 50 | – | `hair` |
| `hairTop` | head | 51 | – | `hair` → own key (§22) |
| `hairSides` | head | 52 | – | `hair` → own key (§22) |
| `hairExtras` | head | 53 | – | `hair` → own key (§22) |
| `hairFringe` | head | 54 (62 over glasses) | – | `hair` → own key (§22) |
| `glasses` | head | 60 | – | `glasses` |
| `hairStreaks` | head | 63 | yes | `streak`, one per streak (§22) |
| `headwear` | head | 70 | – | `hat` |
| `accessory` | any | 80 | yes | `accent` |

- Every slot holds as many stickers as you like, bottom to top (layered clothes: a cap
  over a hood, a shirt over a T-shirt). Data-wise every slot is a stacked list, as the
  plan says. A gallery click only ever adds: an item that isn't worn goes on top of
  what the slot already holds, in every slot, the face ones included, and is selected
  so the Sticker tab's *Forward*/*Back* can reorder it. A click on an item that is
  worn takes it off. *None* takes everything in the slot off. Nothing is ever replaced.
- "Stamps copies" (`StickerSlotInfo.StampsCopies`) is the one exception to that
  toggle: in Prints and Other, a click on a placed design (drawn art or text) that is
  already worn puts on another copy instead of taking it off (§19).
- **Automatic layer fit**: when a sticker is worn over others in the same slot, each
  of its cover parts is drawn at least a little looser (half of the default ease,
  0.004) than the loosest cover (as drawn) on the same region among the stickers
  under it in that slot, so a tighter shirt over a looser T-shirt hides it instead of
  showing a sliver of it round the edge. It happens at draw time only and is never
  saved; one sticker per slot draws exactly as made.
- A dress is a `top` whose parts reach into `skirt`. Wearing one alongside trousers
  is allowed and looks like leggings, and the Look tab's "None" removes the trousers.
- A per-part z override stays deferred, as in the plan.

## 9. Colours and fabrics

### 9.1 Colours

Resolution, lowest to highest priority: the sticker's `colors` default → the
definition's `ColorSlots` → the revision's `ColorSlotValues` → the instance's
`ColorSlotOverrides`. The sticker's default is only a fallback. A slot is written
into `ColorSlots` only when the user picks a colour, and from then on it survives
outfit changes. `skin` already works this way.

### 9.2 Fabrics: pattern and texture on top of the colour

A colour slot's **fabric** is its colour plus an optional **pattern** and an
optional **texture**. Each fabric is stored per slot, next to the colour, and
resolves through the same chain as §9.1: sticker default → definition → revision →
instance. The consequences:
- A plaid top stays plaid when the T-shirt is swapped for a hoodie.
- The library's *Jeans* comes with denim as its default, and a user-picked fabric
  replaces it.
- One panel can override a fabric (a muddy shirt) like any colour.

**Patterns** are repeating motifs. The slot's colour is the ground, and the pattern
adds one or two colours of its own.

| Pattern | Generated from | Parameters |
|---|---|---|
| Stripes, Pinstripes | code | colour, stripe width, angle, size |
| Checks (gingham) | code | colour, size, angle |
| Plaid (tartan) | code | two colours, size, angle |
| Dots | code | colour, dot size, size |
| Chevron | code | colour, size, angle |
| Floral, Camo, Leopard, … | CC0 SVG tiles in the library | colours via classes, size, angle |
| Custom | the user's SVG or PNG tile (§9.4) | size, angle |

The built-in patterns are code, like `BodyPresets`, so they need no files. In an SVG
tile, the classes `slot-ground`, `slot-1` and `slot-2` take the slot's colour and the
pattern's two colours, with the same shade-offset recolouring as §6.1. The rest of a
tile keeps its own colours. PNG tiles are fixed-colour.

**Textures** are greyscale tiles multiplied over the colour and pattern at a chosen
strength (0 to 1), so they survive any recolour: Denim, Knit, Corduroy, Wool,
Leather, Canvas and Felt. They are generated procedurally, using Skia's
Perlin-noise shaders combined with fine line tiles, so they need no art files. A
custom texture is a greyscale PNG.

### 9.3 How fabrics sit on the body

- **They are laid out in each part's region frame** (§4.1): the torso, each limb
  segment, the skirt and the head. Stripes on a sleeve turn with the arm, and the
  shirt's pattern leans with the torso. Where regions meet at a real seam (shoulder,
  hip, neck) the pattern breaks, as it does on real clothes.
- **A sleeve or trouser leg is one piece of cloth** (#14): there is no seam at the
  elbow or knee, so the pattern runs on past it. The lower segment's frame
  (`StickerCovers.SegmentFrame(segment, offset)`) starts the upper segment's length
  back up its own line, so "down" in both frames is the distance from the shoulder or
  hip, and on a straight limb the two frames coincide. Where the two capsules overlap
  at the joint, the lower piece is cut back (`StickerCovers.CutAtJoint`) to the lower
  side of the joint's bisector and drawn over the whole upper piece: a bent sleeve's
  stripes meet in a chevron along the crease. Gotchas: don't cut both pieces at the
  bisector - two antialiased edges meeting leave a hairline of what's underneath; and
  keep the pieces in upper-then-lower order, as `FabricFill` draws them in turn. Fabric
  on art (`StickerArtPieces.FabricFrame`) uses the same offset frame on a forearm or
  shin, so a print lines up with a sleeve's.
- **They are rigid within a frame.** They rotate and scale with the region but don't
  bend around a belly: flat, as the art style is. A Warp-style fabric that follows
  the outline is listed under §16.
- **Size is relative to the character's own height**, like `ease`, so a shirt has
  about the same number of stripes on a toddler as on an adult. `size` and `angle`
  are per slot.
- **Mirroring** flips fabrics with the character, since the placement is a mirror
  image.
- **Cover parts** fill with the fabric. **Art elements** tagged with the slot get it
  too, unless tagged `solid`.

### 9.4 Storage

```csharp
public sealed record Fabric(PatternFill? Pattern = null, TextureFill? Texture = null);
public sealed record PatternFill(PatternKind Kind, IReadOnlyList<ColorValue> Colors,
    double? Size = null, double? Angle = null,
    double? Weight = null,                      // stripe width or dot size, as a fraction of one repeat
    PatternId? Tile = null);                    // Kind == Tile: a project-level tile (patterns/)
public sealed record TextureFill(TextureKind Kind, double? Strength = null, double? Size = null, PatternId? Tile = null);
public enum PatternKind { Stripes, Pinstripes, Checks, Plaid, Dots, Chevron, Tile }
public enum TextureKind { Denim, Knit, Corduroy, Wool, Leather, Canvas, Felt, Tile }
```

- `CharacterDefinition.Fabrics`, `CharacterRevision.FabricValues`,
  `CharacterInstanceOverrides.FabricOverrides` and `Sticker.Fabrics` are all
  `SortedDictionary<string, Fabric>?`, keyed by colour slot. Each is absent (null)
  when there are no fabrics, so existing files read unchanged.
- **Tiles are project-level**: `patterns/<id>-slug/pattern.json` (name, and whether
  it is a pattern or a texture) plus `tile.svg` or `tile.png`. The SVG's viewBox is
  one repeat. They are shared by every character, so a school's tartan is defined
  once. Library tiles are copied in when first used and tidied on save when unused,
  under the same rule as library stickers (§11). A slot whose tile is missing draws
  its plain colour, and the Look tab flags it.

### 9.5 Drawing

Each fabric is a Skia shader, filled inside the part's path:
- A generated pattern is an `SKPicture` tile, drawn with a repeating picture shader.
- An SVG tile is its parsed picture. A PNG tile is an image shader.
- The texture darkens it as if multiplied on top, but is drawn as a **shade**, with
  a plain paint: black, as opaque as the grey at its strength darkens (multiplying by
  grey `g` at strength `s` is laying black at `s × (1 − g)` over it; a tile's
  see-through parts shade nothing). See `FabricShaders.Texture`.
- The shader's local matrix is the region frame × size × angle.

PDF export must keep vector patterns as vector tiling patterns. If Skia's PDF
backend rasterises picture shaders, export renders them at the export resolution
instead. Check this in the fabrics slice.

**No blend modes on the page (#60).** Skia's PDF backend rasterises a procedural
texture (Perlin noise, composed shaders) into an image tiling pattern, and a paint's
blend mode and alpha become an ExtGState (`/BM /Multiply /ca .5`) on that pattern.
Poppler, which Linux PDF viewers and file-manager thumbnails use, gets that wrong in
two different ways: its Cairo backend paints the pattern opaque (grey clothes), and
Splash applies the alpha but not the multiply (pale clothes). MuPDF and Skia get it
right, so the viewer and the thumbnail disagreed. A shade with its alpha baked into the
image (an `/SMask`) and `/BM /Normal` renders the same everywhere. So page drawing
should stick to `SrcOver`: work any other blend out into a shader (as the texture's
shade is) rather than a paint's `BlendMode`. `FabricRenderingTests` checks a textured
PDF asks for no blending. To compare renderers locally, `pdftoppm` (Splash),
`pdftocairo` (Cairo) and PyMuPDF each render a page to PNG.

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
    SortedDictionary<string, IReadOnlyList<StickerId>> Stickers,    // was StickerSlots: slot -> worn by default, bottom to top
    SortedDictionary<string, Fabric>? Fabrics = null);              // colour slot -> pattern/texture (§9.4)

// Characters/Stickers/Sticker.cs (replaces Sticker/StickerKind; StretchRegion and stretch.json go)
public sealed record Sticker(
    StickerId Id, string Name, string Slot,
    IReadOnlyList<StickerPart> Parts,
    SortedDictionary<string, ColorValue> Colors,
    IReadOnlyList<string> Variants,
    string? Source = null,                                  // "library:<key>" while an unmodified library copy
    SortedDictionary<string, Fabric>? Fabrics = null,        // default fabric per colour slot (Jeans: denim)
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

public sealed record StickerAsset(Sticker Sticker, IReadOnlyDictionary<(string Variant, ViewAngle View), ArtFile> Art);

/// A sticker's art exactly as on disk (SVG text, or PNG bytes), written back verbatim on save.
public sealed record ArtFile(string? Svg, byte[]? Png);
```

- Parsing is derived, so it lives in Stanley.Rendering. `StickerSvg.Parse(ArtFile)`
  returns the layers as Skia paths and paints, with each element's slot and a report
  of anything skipped. The result is cached by file content.
- Custom pattern and texture tiles (`patterns/`) are loaded into the open comic next
  to the characters. They are project-level, not part of any one bundle.
- `ICharacterCatalog.Characters` becomes `CharacterId → CharacterBundle`, and the page
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
patterns/                        # project-level, shared by every character
  <id>-school-tartan/
    pattern.json                 # name; pattern or texture
    tile.svg                     # one repeat (or tile.png, LFS)
```

- This is the plan's layout without `stretch.json`, plus `patterns/`.
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
3. **Colours and fabrics** (§9) and, for each sticker, a **variant** (§7).
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
| Look | *Look ▾* (Default or a named look; slice 7) |
| Hair | gallery |
| Face | Eyes, Brows, Mouth, Nose, Facial hair galleries; *Preview expression ▾* |
| Clothes | Top, Outer, Bottom, Shoes galleries |
| Accessories | Hat, Glasses, Other galleries |
| Colours | a swatch dropdown for each colour slot in use (Skin, Hair, Eyes, Top, …), with the slot's fabric |
| Art | *Draw your own…*, *Import…* |

- Each gallery starts with **None**, then this character's wardrobe, then the
  library. Every item is **previewed on this character**, the way the Pose gallery
  previews on the selected character. Clicking an item puts it on over what the slot
  already holds and selects it; clicking a worn item takes it off. Each click is one
  undo step (§8).
- **Colours** shows only the colour slots the worn stickers use. Each is a swatch
  dropdown like Skin. For a clothing slot, the same dropdown continues below the
  colours:
  - a **Pattern** gallery: None, Stripes, Checks, Plaid, Dots, Chevron, the library
    tiles, and *Custom…*, each previewed on this character in its current colour.
  - the pattern's own colour swatches.
  - a **Texture** gallery: None, Denim, Knit, Corduroy, …
  - *Size*, *Angle* and *Strength* sliders, one undo step per drag.

  The swatch button itself shows the fabric, so a plaid top reads as plaid on the
  ribbon.
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
| ProjectModel | `Sticker`/`StickerPart`/… (§10), `Fabric`/`PatternFill`/`TextureFill` (§9.4), `PatternId`, `StickerSlots`, `CharacterDefinition.Stickers`/`Fabrics`, `CharacterBundle`, `ArtFile`, `BodyFigure` groups/regions/inflate, `ProjectRepository` sticker and pattern listing, art/tile I/O; drop `StickerKind`, `StretchRegion`, `StickerSlotDefinition` |
| Rendering | `StickerSvg` (the one adapter over VectSharp.SVG, §6.1), `CharacterResolver`, `ResolvedCharacter`, `RegionMapping` (Pin/Warp), `StickerRenderer` (cover/art paths, recolour, cut/clip), `FabricShaders` (generated patterns, procedural textures, tiles), group painting + attachment-zone ink in the renderer (the mannequin becomes "a figure with no stickers") |
| Editing | `ExpressionPresets`, `LookEditing` (wear/take off/stack/recolour/set fabric, per slot), `StickerFitting` (cover sliders, art offset/scale/rotation) |
| Editors | Look tab, contextual Sticker tab, galleries, colour and fabric dropdowns, Expression gallery on the page's Character tab, `CharacterEditorViewModel : EditorViewModel<CharacterBundle>`, `ComicProject` bundle and pattern load/save/prune/tidy, external-edit file watch |
| Library (new, Avalonia-free) | `Stanley.StickerLibrary`: the starter stickers and pattern tiles as embedded resources in the project format itself, plus `StickerTemplates`. Its art is **CC0-1.0** (its own `LICENSE` file). Art contributed to it must be original or already CC0 |

## 15. Delivery slices (each a PR, each green on its own)

0. **Side view faces the right way.** Done alongside this design: facing right, the
   right limbs are the near ones, and the side-on presets are unchanged.
1. **Layered figure.** `BodyFigure` groups, regions and inflate. The renderer paints
   groups with attachment-zone ink. No stickers yet. Visible: arms crossing the body
   now read. Tests: region frames (template regions map onto themselves for the
   default body), ink-free shoulders and hips at rest (pixel probe), and a forearm
   across the chest being inked.
2. **Cover stickers end to end.** Model, bundle, repository, resolver, cover
   rendering, the Look tab with the clothing galleries and colours. The starter
   garments need no art: T-shirt, long sleeve, tank top, shirt, hoodie, jacket,
   dress, skirt, shorts, trousers, shoes, boots. V-necks and open fronts (cut art)
   follow in slice 4. Tests: JSON shape, the fallback chain, a pixel probe for
   chest = `top` and bare forearm = skin, a sleeve bending with the elbow, a wear =
   one undo step, save/open/prune/tidy.
3. **Fabrics.** `Fabric` storage and resolution, the generated patterns and
   procedural textures, `FabricShaders`, and the fabric part of the colour dropdowns.
   Jeans come in denim. No tiles yet. Tests: stripes turn with a posed sleeve (pixel
   probes along the arm), a fabric survives swapping the garment, a texture keeps its
   recolour, and PDF export keeps patterns (vector, or rasterised at export
   resolution).
4. **Drawn stickers.** It starts with the VectSharp.SVG spike (real Inkscape and
   Illustrator files; a trimmed publish with the AOT analyzers on). Then the
   `StickerSvg` adapter, templates, Pin/Warp, recolouring and view fallback. Then
   the starter hair (six styles), faces (three eye/brow/mouth sets, neutral only),
   front and side, and the library's SVG pattern tiles, plus *Custom…* pattern and
   texture tiles. Tests: layer and slot mapping through the adapter, the
   skipped-feature report, the recolour shade offset, Warp keeping the template's
   outline on the character's outline, relative stroke width, verbatim save.
5. **Expressions.** Variant selection, `ExpressionPresets`, the Expression gallery on
   the page, and the starter face variants. Tests: preset → variant per slot, missing
   variant → neutral.
6. **Draw your own and import.** Template export, the external edit round trip, and
   arbitrary SVG/PNG placement handles. Tests: a template file imports with no
   questions, a re-import is one history entry.
7. **Looks.** Named revisions in the Look tab, the issue default, the per-panel look,
   put on/take off for one panel only, and per-panel fabric overrides.

Slices 1 to 3 give a dressed character, patterns and textures included, with no
art pipeline at all. Slice 4 is the first that needs SVG.

## 16. Later

Build breakpoints (§7), PNG tint masks and warped rasters, fabrics that bend with
the outline (Warp) or stay fixed to the page (a printed-paper look), three-quarter art (covers
get it free once the rig does), per-part z overrides, arms behind the body, "turn
around" as distinct from mirror, gaze (the VRM `LeftEye`/`RightEye` bones moving the
pupils), a talking mouth when a bubble's tail points at the character (after
character-bound tails), a length slider per hair piece and hair that reacts to a pose
(§22), a user library shared across projects (for whole
characters and for individual stickers, designed in
[`asset-packs.md`](asset-packs.md)), flatten to editable layers, and
`stanley sticker import` on the CLI.

## 17. Decisions and open questions

Decided (answers to the first draft):
- **Side view: fixed.** Facing right, the character's right side is near (slice 0).
- **Clothing gets patterns and textures**: fabrics (§9.2 to §9.5, slice 3).
- **Starter library art is CC0-1.0.** Only the art is CC0; the code stays AGPL. It
  gets its own `LICENSE` in `Stanley.StickerLibrary`. Library contributions must be
  original or already CC0.
- **SVG is read with a ready-made, AGPL-compatible reader: VectSharp.SVG**
  (LGPL-3.0-only; §6.1 has the candidates and why the others were ruled out).

Still open. Each has a recommendation, which the design assumes until told otherwise:
1. **Covers as the default for clothing**, instead of drawn per-bone art as the plan
   had. *Recommended*: yes (§1, §2). This is the one real change of direction.
2. **Size of the starter library for V1.** *Recommended*: the garments in slice 2,
   the built-in patterns and textures, about six hairstyles, a handful of SVG pattern
   tiles, and three face sets with the full expression vocabulary, in front and side,
   all simple flat vector art in the POC's style.
3. **Drop `stretch.json` and 9-slice** in favour of Warp, and leave build breakpoints
   unbuilt until a real case needs them. *Recommended*: yes.
4. **Tidy unused library copies on save** (stickers and pattern tiles, §11), rather
   than keeping everything ever clicked. *Recommended*: yes.
5. **Fabric size relative to the character** (a toddler's shirt has as many stripes
   as an adult's) rather than absolute. *Recommended*: relative (§9.3).

## 18. Implementation notes

Where the built code differs from the design above, or settles something it left
open:
- **The wardrobe lives on the character in memory.** Instead of a separate
  `CharacterBundle`, `CharacterDefinition.Wardrobe` (stickers with their art files,
  and tiles) and `Revisions` are `[JsonIgnore]` properties loaded and saved with the
  character's folder, so any edit to them is an ordinary character edit: one undo
  step, one redraw everywhere.
- **Tiles are per character**, in `characters/<id>-slug/patterns/`, keyed by file
  name (not project-level `patterns/<id>-slug/` with a `pattern.json`). A texture
  tile's name starts with `texture-`. Library tiles are copied in under their
  library name (`floral.svg`) when picked and tidied on save while unmodified and
  unused, like library stickers.
- **Ink at seams**: skin leaves its ink out where a layer's seam lies over anything
  painted before it; a garment only where it lies over its *own* earlier pieces, so
  a shirt's hem over the trousers keeps its line.
- **The SVG adapter** also normalises what VectSharp reads differently from the
  spec: ellipses become paths (VectSharp strokes them in a scaled space), clip paths
  become one path (it only takes a single path or rectangle there), Inkscape's
  duplicate `svg:` namespace prefix is dropped, and the document is sized to its own
  view box (`width`/`height` set to the view box's, which is made explicit): VectSharp
  scales the drawing to a declared size, while everything here reads it in view-box
  units, so an icon saved as `width="800px" viewBox="0 0 24 24"` drew as an empty
  corner (#112). Without a view box, the declared size in px (mm, in, pt... converted)
  is the view box. Only *named* top-level groups are
  parts; drawing outside them is reported. A trimmed publish shows trim warnings
  only from ExCSS (VectSharp.SVG's CSS parser); VectSharp itself is clean. CI
  doesn't trim.
- **Recolouring leaves greys alone** on a colourful default: black ink and white
  highlights on a tagged shape aren't shades of its colour, so an artist can put
  fill and outline on one element.
- **Pattern tiles recolour by class** (`slot-ground`, `slot-1`, `slot-2`), the
  default for each being the first colour drawn in it; a pattern without colours of
  its own keeps the tile's.
- **Starter library as built**: 23 cover garments; hair (short, bob, long,
  ponytail, curly, bun - no longer offered since hair is built from pieces, §22, but kept
  for the comics that wear them); eyes (dots, round, lashes), brows (thin, medium, thick),
  mouth (simple, wide, lips) with the full expression vocabulary as variants, and
  two noses; two drawn hats, their styles as variants - a Hood, up or down (in the
  `top` colour, so it matches the top it's worn with, fabric included; worn after a
  cap it goes over it, the brim showing in its opening) and a Cap with its brim
  forward, backward, right or left (the character's own sides); five SVG pattern
  tiles (floral, stars, hearts, camo, leopard); all front and profile. New
  characters start wearing the default face (dot eyes, thin brows, simple mouth).
- **Trunk posing is inverse kinematics** (asked for alongside slice 4): dragging
  the chest or head bends the spine or neck joint by joint, and the upper body's
  outline bends with it (`TrunkBend`), instead of turning the trunk as one board.
- **Expressions**: applying Neutral removes the face slots from
  `PoseData.Expression` rather than writing "neutral", so an unposed face stores
  nothing. The page's Expression gallery is a dropdown of close-ups (the Character
  tab has no room for twelve more thumbnails beside the poses).
- **Draw your own and import**: the art is placed with *Size* and *Turn* sliders on
  the Sticker tab and by dragging it on the stage, instead of on-canvas scale and
  rotate handles. An imported file that wasn't made from a template becomes one
  part named `all`, which takes every layer of its file. Files being drawn live
  under the app's data folder (`Drawing/`), not in the project, until a save brings
  them in. "Draw your own" on a drawn sticker that's selected edits it (the stage's
  view), so the side view is drawn by switching to Side and choosing it again.
- **Looks**: every look edit (in the character editor, and a panel's "this panel
  only") works on a flattened view of the character and is stored back as the
  differences only — a named look against the default look, a panel against its
  look. A panel can ask for the default look even when its issue uses a named one,
  through a reserved look id (`default`). Per-panel changes are reached by
  right-click › *This panel only* (take off, put on, colour, pattern); a look that a
  panel or the issue uses can't be deleted. Proportion overrides and build on a look
  (`ProportionOverride`, `Build`) stay unused: body edits always change the
  character.

## 19. Prints on clothes

**Status: implemented.** Symbols and your own words on clothes (a skull on a T-shirt,
a band name, emoji), beyond patterns and textures.
- **A `print` slot** ("Prints" on the Look tab, among the clothes): torso, z-order 25 -
  over the top (20), under outerwear (30), so an open jacket covers a T-shirt's print -
  and it **stamps copies** (§8), so a shirt can carry several. Its colour slot is `print`.
- **Symbol prints in the starter library** (`Library/print/`): skull, heart, star,
  lightning bolt, flame, smiley and a music note - original CC0 art, one Pin art part
  each, recoloured through `class="slot-print"`. The skull's eyes, nose and teeth are
  holes, so it reads in any colour. They drag, size and turn like any drawn art.
- **`clip: clothes`** keeps a print on the fabric: it's clipped to the union of the
  other worn stickers' cover pieces in its group (not cut or clipped ones themselves),
  so one dragged past the shirt's edge doesn't spill onto skin. With nothing worn
  underneath it isn't clipped at all, so a print on bare skin still shows (a quick
  tattoo).
- **Text prints**: a part with `text` (`text`, `color` - a colour slot, `print` by
  default - `fontFamily`, `bold`) alongside its `art`, which carries the Pin placement
  (`offset`, `scale`, `rotation`, `keepReadable`), so it moves, sizes and turns exactly
  like drawn art and never reads backwards on a mirrored character. It needs no art
  files. It's drawn as vector text with per-character font fallback
  (`Lettering.FallbackRuns`): emoji and symbols the lettering font lacks come from
  whatever installed font has them (a colour emoji font draws in colour). Its
  clickable area and selection outline are its text box, mapped.
- **As many of the same as you like**: in a slot that stamps copies (Prints, Other), each
  click on a placed design - drawn art or text, a library symbol or your own - puts on
  another copy (`StickerCopies`), with its own id and placement, a step down and to the
  right of the last one and selected, so it can be dragged straight into place; the
  Sticker tab's **Duplicate** does the same for the selected print. The gallery offers
  each design once however many copies are worn. Copies come off one at a time from
  the Sticker tab (Take off / Remove) or all at once with None. Stickers made only of
  covers (gloves, a scarf) aren't copied - a second click takes them off, as before.
- **UX**: the Prints gallery's **Text** button puts on "HELLO" and opens the Sticker
  tab's text box to type over (Enter or leaving the box applies it, one undo step),
  with a Bold toggle and a row of common emoji to add with a click. Size, Turn and
  dragging work as for drawn art; "Hug the shape" and "Edit drawing..." are hidden for
  text. "Draw your own..." and "Import..." work in the Prints gallery like anywhere
  else, for a print of your own.

## 20. Styles

**Status: implemented.** Ways to wear a sticker: a hood up or down (#72), a cap with its
brim forward, backward or to either side (#74), a jacket zipped up, half open or open (#71). A style is one of the sticker's
`variants` (§7), chosen **per sticker**, not per slot, so a cap and a hood worn together
in `headwear` each keep their own.
- **Storage**: sticker id → variant, sparse and absent when unused, along the look chain
  (§9.1): `CharacterDefinition.StickerVariants` (`stickerVariants` in `character.json`) →
  the named look's `CharacterRevision.StickerVariantValues` (`stickerVariantValues`) → one
  panel's `CharacterInstanceOverrides.StickerVariantOverrides` (`stickerVariantOverrides`).
  A sticker worn its default way (its first variant, or `neutral` if it has one) has no
  entry, so choosing the default removes it. A named look or a panel that goes back to the
  default over a style underneath stores the default explicitly, as a plain fabric does.
  Removing a sticker from the wardrobe, or tidying an unused library copy away on save,
  drops its entries from the character and every named look.
- **Resolution**: `CharacterLooks.Resolve` puts the chosen style on each `WornSticker`
  (`Variant`), and `Sticker.VariantFor(slot, expression, chosen)` picks what is drawn: the
  expression's variant for the slot if the sticker has it (faces work as in §7), else the
  chosen style if it has it, else `neutral`, else the first. The render cache keys on the
  definition, the look and the panel's overrides, so each style draws on its own.
- **Parts per style**: a part (§5) with `"variants": [...]` exists only in those, so a
  style can change a sticker's shape, not just its drawing:
  the hood's cover pieces only when it's up, the brim behind the head only when it's back.
  Drawn parts take their art from `variants/<style>/<view>.svg` as for any variant.
  The **Zip jacket** (`outer/zip-jacket`) is the pattern for an opening garment: cover parts
  for the jacket, a `blend: cut` warp part `opening` only in `half` and `open` (so the top or
  skin underneath shows through, whatever is worn), and a `zip` part in every style drawing
  the slider and pull tab. The cut is a plain polygon per style, so a new zipped garment is
  three SVGs per view.
- **Character editor**: the contextual Sticker tab has a **Worn** gallery - each style of
  the selected sticker, previewed on the character wearing it that way (a close-up for
  head stickers), the current one marked. A click is one undo step through the same look
  editing as colours and fabrics, so it lands in the named look being edited. It shows
  only for a worn sticker outside the face with more than one variant: a face's variants
  are expressions, previewed with *Preview expression*. Labels are the variant keys made
  readable (`brim-back` → "Brim back"). *Draw your own...* on a styled sticker edits the
  style it's shown in.
- **Page editor**: right-click › *This panel only* › **Style** lists each worn sticker
  that has styles, with its styles (the current one ticked); picking one keeps it as the
  panel's override, and *Back to the look* clears it with the panel's other changes.

## 21. Split eyes

**Status: implemented** (GitHub issues #100-#102). By default both eyes are one sticker,
drawn, coloured and expressed as one - unchanged from §7-§9. **Different left/right eyes**
(the Look tab's Face group) turns that off for one character: the eyes' shape, colour and
expression can then differ side to side, the way a cap's brim already can (§20), without
turning every other symmetric part (brows, hands, shoes) into a per-side thing too - only
eyes need this, per the issues, so only eyes get it.

- **Art**: a sticker's art elements can carry `class="side-left"`/`"side-right"` (any
  element `slot-*` already can, §6.1) - the character's own left/right, as `side` on a limb
  part already means (§5). An element with neither class draws whichever side(s) the
  sticker is worn on, as before; this is inert until a sticker is actually restricted to one
  side (below), so an ordinary two-eyed sticker with no such classes keeps working exactly
  as it always has. The starter library's three eye stickers (dots, round, lashes) tag
  their left and right eye's elements this way, in every expression variant and in profile
  (which shows only the near eye - the character's right, by the rig's own convention).
- **Wearing**: no new slot. A split character simply wears *two* stickers in `eyes` (still
  one stacked list, §8) - by default two copies of whatever was worn before, one per side,
  so splitting never changes how the character looks until you change one. Which side a
  worn sticker is restricted to is `CharacterDefinition.StickerSides` (sticker id → `left`/
  `right`), sparse and character-level only (not per look or per panel - unlike a style,
  picking one eye's shape isn't something one panel needs its own answer to). Absent for a
  sticker, as every sticker was before this existed, it draws on both sides.
  `LookEditing.SplitEyes`/`UnsplitEyes` turn it on and off; `WearOnSide` swaps one side's
  sticker for another, making its own copy first if that design is already worn on the
  other side (the same id can never be on both).
- **Colour and expression**: no new storage shape either - both already resolve by a plain
  string key (§9.1, §7), so a split eye simply resolves under `"eyesLeft"`/`"eyesRight"`
  instead of `"eyes"` once it's worn on a side (`StickerSlots.SidedSlot`). Colour falls back
  to the shared `"eyes"` slot when a side has no colour of its own (`CharacterLooks.Resolve`
  bakes this in), so an existing character's eye colour still reaches both sides right after
  splitting. Expression has no such fallback to bake in - it's already panel-scoped
  (`PoseData.Expression`), so `ExpressionPresets.Apply`'s `splitEyes` parameter simply also
  writes the preset's eyes to `"eyesLeft"`/`"eyesRight"` when asked, and a panel can mix its
  own beyond that with `SetVariant(pose, "eyesLeft", …)` exactly as any slot already can.
- **Rendering**: `StickerArtPieces.Map` drops elements tagged for the other side once a
  worn sticker has a side (`WornSticker.Side`, from `StickerSides`), and recolours a
  `slot-eyes` element from the sided slot instead of the shared one. `CharacterRenderer`
  picks the sided key for the expression too. Both are no-ops when nothing is split.
- **Character editor**: **Different left/right eyes** (a checkbox by the Face group,
  disabled with nothing worn in `eyes`) replaces the single Eyes gallery and swatch with
  **Left eye**/**Right eye** ones - each the same kind of gallery any slot has (own
  wardrobe, deduplicated by name since splitting can leave two identical copies, then the
  library), so no new gallery UI was needed. *Preview expression* still sets the whole
  face's mood in one click (both eyes, split or not); two small dropdowns next to it, shown
  only once split, preview either eye's expression on its own.

## 22. Modular hair

**Status: implemented** (GitHub issue #59). Hair is put together from pieces, like 2D
building blocks - a fringe chosen apart from the back - and each piece can be its own
colour, so multi-colour "scene hair" is a few clicks, without the simple path getting
any harder: one Hair button, a gallery of hairstyles, one Hair colour.

- **Pieces are ordinary stickers in their own slots** (§8): `hairTop` (the skull down to
  the hairline), `hairFringe`, `hairSides` (locks framing the face), `hairBack` (behind
  the head and body: its parts are `depth: back`) and `hairExtras` (ponytail, pigtails,
  bun, space buns, braid, cowlick). Slots already give each piece its gallery, z-order,
  per-look and per-panel overrides, styles (§20) and Draw your own. The whole-hairstyle
  `hair` slot stays for comics made before and for hair drawn in one file.
- **They fit because they're drawn over the same head.** Warp maps every piece onto the
  character's real head, and the pieces meet at **join lines** - hairline, partings,
  temples, brow and eye lines, nape (`HairJoins`) - which every hair template shows as a
  locked `joins` guide layer, so a piece you draw fits the library's.
- **Colour per piece.** Piece art is still tagged `slot-hair`; worn in a piece slot it's
  coloured from the piece's own key, the slot name (`StickerSlots.ColorKey`), and
  `CharacterLooks.Resolve` falls that key back to `hair` - colour and dye separately, so a
  fringe given its own purple keeps the hair's dye. Nothing changes colour until asked, and
  a purple fringe stays purple when the fringe is swapped (decision 5). `character.json`
  gains a line (`"hairFringe": "#8e24aa"`) only once a piece has a colour of its own.
  Recolouring under another key keeps the artist's shading: the original is the sticker's
  default for the tagged slot (split eyes, §21, recoloured flat before this).
- **Hairstyles are presets** (`StickerLibrary.Hairstyles`): recipes of library pieces with
  styles - Short, Bob, Long, Ponytail, Curly, Bun (today's six, redrawn as pieces), Pixie,
  Pigtails, Space buns, Side-swept, Emo and Scene. Picking one swaps the whole hairdo in one
  undo step (`HairEditing.WearHairdo`) - the one gallery that replaces instead of adding
  (§8), since two whole hairdos stacked is never wanted - reusing the wardrobe's unmodified
  copies, each in the preset's style. Colours are kept. When the head wore its own mix of
  pieces, a bar under the stage says so and offers **Add to my mix instead** (still one
  undo step); bald, a preset as it came, or an old whole hairstyle lose nothing, so there's
  no bar. Putting on a fringe, sides, back or extra on a head with nothing on top brings
  the Smooth top with it, so a piece never floats on a bald crown.
- **Over glasses**: `Sticker.OverGlasses` paints a sticker at z 62, over glasses (60):
  the library's Side-swept fringe, which covers an eye, is; the rest aren't. The Sticker tab's
  **Over glasses** toggle flips any hair sticker (a library copy becomes the character's own,
  as any edit does).
- **Dyes** - several colours within one piece: `PatternKind` Streaks, Tips, Roots, Ombre
  and Rainbow, on the colour of the hair or of one piece. Unlike patterns they're laid once
  across each drawn part, in its region's frame (`DyeLayout`): Tips is the bottom of *that*
  piece, the waist on a long back or the brows on a fringe. `Weight` is how far (Tips 0.3,
  Roots 0.25, where an Ombré starts 0.3) or a streak's width; Rainbow's bands are its
  `colors` (six by default). They're drawn as vector bands and a gradient, so PDF export
  stays vector. Clothing patterns stay available to hair (stripes across hair are raccoon
  tails).
- **Streaks** (`hairStreaks`): coloured locks put on like prints (§19) - each click stamps
  another, placed across the fringe (`HairEditing.StreakSpot`) - then dragged, sized and
  turned. Their art is tagged `slot-streak` and their part is `clip: hair`: drawn only where
  other hair is drawn in the same layer, so dragging one past the hair's edge never spills.
  **Each streak has its own colour** (`streak-<id>`, falling back to the streaks' default);
  a new one starts in the colour of the last one put on, and removing one drops its colour.
- **Schemes**: the Hair colour's dropdown starts with a row of one-click schemes on one accent
  colour - Natural, Two-tone (top and fringe), Peekaboo (the back), Fringe only, Dip-dye,
  Ombré, Rainbow (`HairEditing.ApplyScheme`) - each previewed on the character. A scheme
  writes ordinary piece colours and dyes, so everything it did can then be changed piece by
  piece; each starts from Natural, so trying them one after another never piles up.
- **Character editor**: the Look tab keeps one **Hair** button; its flyout has tabs -
  **Hairstyles** (Bald, the presets, then the character's own whole hairstyles, each previewed
  on it), Top, Fringe, Sides, Back, Extras, Streaks (ordinary slot galleries). The button's
  second line names the hairstyle worn exactly, else "Own mix". Clicking a piece on the stage
  opens the Sticker tab with that piece's colour and dye, **Same as hair** while it has its
  own, and Over glasses. The Colours group shows a piece only once it has a colour of its own.
  A hair colour's dropdown titles its patterns **Dye**, with the clothing patterns under More
  patterns; Rainbow shows a swatch per band.
- **Named looks**: every hair edit lands in the look being edited. A look can only override,
  so *Same as hair* in a named look, where the default look gives the piece its own colour,
  copies the look's current hair colour - and says it won't follow later changes.
- **Page editor**: right-click › *This panel only* › Colour lists each worn piece after Hair (a
  fringe purple in one flashback), and Pattern offers hair its dyes.
- **Old whole hairstyles**: a character wearing one of the old library hairstyles, unmodified,
  gets a bar in the character editor - "This is the old Bob - switch to the new Bob made of
  pieces?" **Switch** swaps it for the preset's pieces in the default look and every named
  look, colours kept, one undo step; **Keep** stops asking for that character, remembered in
  the user's settings (`AppSettings.DeclinedHairUpgrades`), not the comic.
- **Rendering note**: the area of warped art is a union of its fills and ink, and Skia's path
  ops can silently drop a shape when a line runs along its own fill's edge (the pieces are
  drawn that way). `FigureGeometry.Union` checks its result against points inside both
  shapes and falls back to a union of rasterised regions (512 cells across) when one is
  missing - otherwise a streak's `clip: hair` found no hair to show on.
- **Later**: a length slider per piece (a vertical stretch of Back and Sides from the crown),
  hair that reacts to a pose (wind, a tilt's gravity), three-quarter art, and keeping a
  whole hairdo - pieces, styles and colours - in My Assets as one item (`asset-packs.md`).

## 23. A colour for one sticker (#127)

Any worn sticker outside hair and the face can be coloured apart from the others in its
slot (two hats, two prints). The mechanism is the streaks' one, generalised:

- **Key.** `StickerSlots.StickerColorKey(slot, id)` = `"<slot's colour slot>@<id>"`
  (`top@abc123`). `StickerSlots.HasOwnColorKey(slot)` says which slots qualify: a colour
  slot, not hair (pieces and streaks keep their own keys), not `IsFace` (skin/eyes belong
  to the character). `ColorKey(wornSlot, id, tagged)` returns it for art tagged with the
  slot's colour slot, so the renderer needs no other change.
- **Resolution.** `CharacterLooks.Resolve` gives each such sticker its key with the slot's
  colour and fabric as fallback (`TryAdd`), so a sticker follows the slot until it is given
  a colour, and the key is only *stored* in `ColorSlots`/`Fabrics` once the user picks one.
- **UI.** The Sticker tab's colour group (`HasSelectedHairColor`, label
  `SelectedColorGroupLabel`) shows for any such sticker; "Same as slot" (`FollowHair`,
  `HairEditing.FollowHair` follows `SharedColorOf(key)`) drops the own colour. The Look tab's
  Colours group doesn't list these keys (`ColorGroupSlots` only adds hair pieces). The
  page's right-click Colour menu is still per slot.
- **Tidying.** `HairEditing.DropOrphanStreakColors` drops any key whose sticker
  (`StickerSlots.StickerOfKey`) has left the wardrobe, streaks' and stickers' alike.

The ribbons are split per tab (`Page/Ribbon/*RibbonTab`, `Characters/Ribbon/*RibbonTab`):
each tab's groups are their own `UserControl`, so their controls are named in *that*
control's name scope. `PageEditorRibbon.FindControl<T>` / `CharacterEditorRibbon.FindControl<T>`
look in every tab too (an instance method, so it wins over Avalonia's extension), and
templates a tab uses (the Look tab's slot galleries) live in that tab's own resources -
a `StaticResource` can't reach a parent control's resources from a nested XAML.

### Gotcha: slider drags and the Sticker tab picker

A slider drag on the Sticker tab edits the sticker on every move, so `RaiseLookChanged` runs per move. `WornStickers` (the ComboBox list) is therefore cached and only re-raised when its content actually changes (`RefreshWornStickers`); replacing an equal list resets the ComboBox mid-drag and the ribbon dropped back to the Look tab (#61).
