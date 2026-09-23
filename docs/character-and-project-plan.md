# Character & project data model — design plan

This is the working design plan for Stanley's character system and
project/data model, produced from design discussion. Nothing here is final —
treat it as the current working plan unless the user says otherwise. See
`CLAUDE.md` for the project's overall priorities and current implementation
state; this file is where the not-yet-implemented design lives so it doesn't
bloat that file.

## Character system design (proposed, not final)

These directions came out of design discussions. Treat them as the working
plan unless the user says otherwise.

### Three-layer model — keep these separate
- **CharacterDefinition**: the "wardrobe" — a 2D skeleton (VRM humanoid bone
  set) with a **per-view-angle rest layout** (front/three-quarter/profile are
  genuinely different 2D bone arrangements, not one rig viewed from different
  cameras), named colour slots, and the full catalogue of **stickers** ever
  available per slot (hair, torso, accessories, ...). Face-related stickers
  (eyes, mouth, eyebrows) additionally carry **variants** — alternate art for
  the same sticker identity, used for expression state (e.g. eyes: neutral /
  closed / wide; mouth: neutral / smile / open) — each variant still needs
  its own per-view-angle art set. Nothing here is issue-specific; it only
  grows as the series goes.
- **Revision**: a named, **project-level** snapshot of a character's *look* —
  which stickers are active per slot (slots are **stackable**: multiple
  simultaneously-active stickers per slot, ordered), colour slot values, and
  optional skeleton proportion overrides (aging up, redesigns, **build** — see
  "Body type / build" below). Revisions are user-named ("Post-Haircut",
  "Winter Arc") and reused freely across issues — not auto-generated per
  issue, not locked to one issue.
- **Pose / Expression**: pure data — bone rotations relative to parent, view
  angle, and an **expression preset** (a mapping of face slot -> active
  sticker variant, e.g. "surprised" = wide eyes + open mouth). Independent of
  artwork identity and **angle-agnostic** — poses rotate from whichever
  angle's rest layout is active and expressions just pick a variant, so
  neither the pose nor expression library needs per-angle or per-character
  duplicates as long as slot/variant names line up.
- **CharacterInstance** (per panel): references a definition + a revision
  (defaults to the issue's chosen revision for that character), plus pose,
  view angle, and sparse per-panel overrides (e.g. sunglasses for one shot).
  Changes to the definition or a revision reach every instance that hasn't
  locally overridden that field — like components/instances in Figma.

### Rendering approach
- **Version 1:** a 2D layered cutout rig (Spine/Moho/Pixton-style), rendered
  with SkiaSharp. Stickers are **rigidly attached to a single bone** — no
  mesh deformation (Live2D-style) — matching the still-image use case. The
  one constrained exception is build-driven stretch (see below), which is a
  bounded, declared non-uniform scale, not free-form mesh weighting.
- **Skeleton:** VRM humanoid bone set, with a rest layout stored **per view
  angle** since stickers need per-angle art from the start.
- **Sticker z-order**: each slot has a fixed z-order number; stacking within
  a slot follows list order. Per-sticker z-order overrides (e.g. a popped
  collar needing to jump in front of hair) are a deferred escape hatch, not
  built until a real case needs it.
- **Put rendering behind an interface** (e.g. `ICharacterRenderer`) so a later
  3D backend (glTF/VRM via SharpGLTF, toon/outline shading) can be added
  without rewriting the pose or document model.
- AI pose-conditioned generation is at most a later optional plugin, never the
  core. Its output isn't repeatable and characters drift between panels.

### Body type / build
- **`build`** is a continuous character parameter (0–1, slim → heavy),
  stored as a skeleton proportion override at the **Revision** level —
  same tier as aging/redesigns — with a sparse per-instance override
  available for a one-off panel (e.g. a bloat gag) without creating a new
  named revision.
- **Hybrid rendering, stretch by default:** a sticker that touches the
  body silhouette (shirt, jacket) declares a stretch-safe region (fixed
  margins around a stretchable centre, like a 9-slice image) and is scaled
  non-uniformly to the current `build` value at render time — one asset
  covers the whole slider, no extra art needed for the common case.
- **Variants as the escape hatch:** a sticker can instead opt out of
  stretching and declare **build breakpoint variants** (e.g. `slim` = 0.0,
  `average` = 0.5, `heavy` = 1.0) for garments where stretching would
  visibly break (printed logos, fitted seams, patterns) — this reuses the
  same variant mechanism as expressions, just keyed by build breakpoint
  instead of expression name. The renderer snaps to the **nearest**
  declared breakpoint rather than cross-fading between two images; a
  visible snap at the midpoint is the accepted trade-off for not needing a
  blending/deformation pipeline. Crossfading is a possible later refinement,
  not required for V1.
- Stickers that don't touch the affected region (a hat, glasses) declare no
  build response and render unaffected by the slider.

### UX expectations
- Pose by dragging, with inverse kinematics and pinnable feet or hands. Users
  shouldn't have to rotate individual bones.
- Pose and expression libraries (apply with one click, then adjust), mirror pose,
  and an angle control.
- Recolour through named colour slots (vector fills, or tint masks for bitmaps),
  not free-form recolouring.
- **Character creation has a ready-made-component path**: assemble a new
  character by picking from a shipped library of eyes/mouth/hair/etc.
  stickers — each already carrying its expression variants and per-angle art
  — as the fast default on-ramp, no drawing required. Full custom
  sticker/variant authoring and import stays available as the escape hatch
  for a fully bespoke character.
- Skeleton/proportion editing (bone lengths, per-angle rest layout) is a
  **full rig editor**, not a simplified slider UI — it's the advanced/escape
  hatch surface, not part of the default per-issue flow. A thin convenience
  layer (e.g. a "height" slider) could sit on top of the same rig data later
  without changing storage.
- Escape hatches: flatten to editable layers, custom SVG/PNG sticker import,
  rig editor.

### Open questions (ask the user before deciding)
- Target art style (Western cartoon, manga, semi-realistic, user's choice?).
- A shipped component library is confirmed as one character-creation
  on-ramp; still open how large/opinionated that starter library needs to
  be, and whether custom-authored stickers can be mixed into its slot/variant
  naming conventions so expressions still transfer.
- How much camera-angle freedom is needed beyond front/three-quarter/profile —
  any extra angle means new per-angle art for every existing sticker
  (and every existing variant).
- Whether `build` should also drive skeleton bone-width scaling (so the
  overall silhouette — arms, neck — widens, not just torso stickers), or
  stay a sticker-only effect for V1.
- Whether the visible snap between build-breakpoint variants is acceptable
  long-term, or a later crossfade/blend pass is worth the added rendering
  complexity.

## Project & data model (proposed, not final)

A **project** is a whole comic series (not a single issue) — one git repo,
designed to be git-native: diffable JSON for structure, Git LFS for raster
assets, and IDs (never filenames or array position) for every cross-reference
so reordering or renaming doesn't cause cascading diffs.

### Repository layout
```
MyComic/
  stanley.json                    # series manifest: title, default page trim,
                                   # ordered issue ID list
  .gitattributes                  # LFS rules for raster assets
  characters/
    <id>-alice/
      character.json              # skeleton (per-angle rest layout), colour
                                   # slots, sticker catalogue per slot
      revisions/
        <id>-default.json
        <id>-winter-arc.json
      stickers/
        <id>-jacket-a/                  # non-face sticker: single variant
          variants/
            default/
              front.svg  three-quarter.svg  profile.svg
        <id>-eyes-almond/                # face sticker: expression variants
          variants/
            neutral/
              front.svg  three-quarter.svg  profile.svg
            closed/
              front.svg  three-quarter.svg  profile.svg
            surprised/
              front.svg  three-quarter.svg  profile.svg
        <id>-shirt-plain/                # build-responsive: stretch (default)
          stretch.json                    # declares the 9-slice-style safe region
          variants/
            default/
              front.svg  three-quarter.svg  profile.svg
        <id>-shirt-logo/                  # build-responsive: breakpoint variants
          variants/
            slim/                          # build 0.0
              front.svg  three-quarter.svg  profile.svg
            average/                       # build 0.5
              front.svg  three-quarter.svg  profile.svg
            heavy/                         # build 1.0
              front.svg  three-quarter.svg  profile.svg
      thumbnail.png                # LFS
  poses/
    <id>-wave.json                 # bone rotations, angle-agnostic
  backgrounds/
    <id>-alices-apartment/
      background.json
      revisions/
        <id>-before-renovation.json   # each revision: back layer + optional
                                       # front (occlusion) layer
  issues/
    <id>-issue-01/
      issue.json                  # number, title, ordered page ID list,
                                   # default characterId -> revisionId map
      pages/
        <id>-page/
          page.json                # trim overrides, ordered panel ID list
          panels/
            <id>.json               # shape, background ref/inline, character
                                     # instances, bubbles
      art/                          # one-off per-panel art, LFS
```

### Multi-issue projects
- `characters/`, `poses/`, and `backgrounds/` are shared across the whole
  series; `issues/` holds per-issue content only.
- Character/background **revisions live at project level**, are user-named,
  and are picked per issue (`issue.json`'s `characterRevisions` map) — not
  auto-generated or locked per issue. A redesign for issue #5 is just a new
  named revision; earlier issues keep referencing the revision they already
  point to, so nothing repaints retroactively.
- Issue **number/title are separate from storage order** (`stanley.json`
  holds an explicit ordered issue-ID array), so a #0 preview or a #1.5 annual
  doesn't force renumbering folders.

### Git-friendliness rules
- JSON, 2-space indent, alphabetically sorted keys, trailing newline — a
  no-op save produces an empty diff.
- Every entity (character, revision, sticker, background, issue, page, panel,
  pose) has a stable ID used for all cross-references; **ordering is always
  an explicit ID array** in the parent file, never inferred from filename or
  folder position — inserting a page/panel/issue is a one-line array edit,
  not a cascade of renames.
- Files store source data only, never derived/cached geometry (e.g. resolved
  render paths) — recomputed on load.
- One panel = one file, so editing one panel's content touches exactly one
  file.
- `.gitattributes` puts LFS on raster formats (`*.png *.jpg *.psd`); SVG/JSON
  stay as normal git text objects.
- Autosave writes to disk per-entity; it does not auto-commit — committing
  stays a deliberate, manual action.

### Page & panel model
- Pages are **fixed print size** (trim/bleed, defaulted from `stanley.json`,
  overridable per page); panels are **freeform** shapes on the page canvas,
  reusing the same anchor-ring model as `BubbleOutline` rather than a second
  shape primitive.
- **`PageLayoutPreset`** mirrors `BubbleStylePreset`: a pure
  `pageSize -> panel list (as anchor rings)` generator. Applying a preset
  bakes concrete, freely-editable panel shapes into `page.json` — nothing
  stays "live" against the template afterward.
- The panel-ID order array in `page.json` is both z-order and reading order
  (LTR only for now; direction is a flag that could be added later without a
  schema break, since the order array itself carries no direction
  semantics).
- Each panel holds its own character instances, bubbles, and background
  reference — no shared scene graph spanning panels.

### Backgrounds
- Two tiers: an **inline** background (a panel points straight at one image,
  no library entry — the default/simple path) or a **library entry** under
  `backgrounds/` for recurring locations, referenced as
  `backgroundId + revisionId + crop` (pan/zoom window into the art).
- Each background revision has a required **back** layer and an optional
  **front/occlusion** layer, built into the model from the start. Render
  order: **background back → character stickers → background front →
  bubbles**.

### Open questions (ask the user before deciding)
- Lettering/text rendering (deferred, same as the bubble POC).
- Export/print output pipeline.
- Whether an "extract inline background to a reusable library entry" action
  is worth building, or manual promotion (copy the file, add
  `background.json`) is good enough.
