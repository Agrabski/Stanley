# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

Stanley is a .NET comic editor. Priorities, in order:

1. **Ease of use / flat learning curve.** Prefer sensible defaults, presets and
   direct manipulation over configuration dialogs. A feature that needs a manual
   to use is a design smell.
2. **Escape hatches for advanced users.** Anything the simple UI does should be
   reachable and overridable at a lower level (rig editing, custom part import,
   flatten-to-layers), without that complexity leaking into the default path.

## Current state

Stack: **Avalonia** on **.NET 10** (`net10.0`; retarget to net11.0 once that
ships — .NET 11 doesn't exist as a stable release yet), rendering via
**SkiaSharp** (Avalonia's built-in Skia backend). Shared MSBuild settings live
in the root `Directory.Build.props`; the solution file is `Stanley.slnx`
(the newer XML-free format).

Only the speech-bubble POC exists so far (see below) — no character system,
no document/page model, no persistence.

```
src/Stanley.Bubbles/    # bubble geometry model, no Avalonia dependency (SkiaSharp only)
src/Stanley.App/        # Avalonia POC host (single-bubble editor)
tests/Stanley.Bubbles.Tests/     # xunit v2, geometry unit tests
tests/Stanley.App.HeadlessTests/ # xunit v3 (Avalonia.Headless.XUnit requires it), UI smoke tests
```

Build/test/run:
```
dotnet build Stanley.slnx
dotnet test tests/Stanley.Bubbles.Tests/Stanley.Bubbles.Tests.csproj
dotnet test tests/Stanley.App.HeadlessTests/Stanley.App.HeadlessTests.csproj
dotnet run --project src/Stanley.App
```
No linter is configured yet.

Environment notes: on a fresh Linux container, `apt-get install dotnet-sdk-10.0`
works when `dot.net`/`builds.dotnet.microsoft.com` is egress-blocked (the
official dotnet-install script host). SkiaSharp needs an explicit
`SkiaSharp.NativeAssets.{Linux,macOS,Win32}` package reference per platform —
the base `SkiaSharp` package alone throws `DllNotFoundException` at runtime.
Avalonia's headless test host needs `.UseSkia()` even though it's not
rendering to a real window, or any `TextBlock` measurement throws
(`Unable to locate 'Avalonia.Platform.IFontManagerImpl'`).

## Speech bubble system (POC)

Implemented in `Stanley.Bubbles` + `Stanley.App`, demonstrating: resizing,
switching between style presets, and adding/moving any number of tails.

- **`BubbleOutline`**: an arbitrary closed bezier shape (ordered `BubbleAnchor`
  ring, each with absolute in/out handle points and a corner-vs-smooth type).
  `Rescale(from, to)` affine-maps every anchor for resizing.
- **Tails are independent, not part of the outline.** Each `BubbleTail` has an
  `AttachmentT` (0–1 fraction along the outline) and a free `Target` point.
  `SpeechBubble.BuildRenderPath()` unions the outline with every tail's own
  polygon via `SKPath.Op(..., SKPathOp.Union)` — this is why adding another
  tail needs no special case, and why any number of tails works.
- **Style presets** (`BubbleStylePreset`: Speech/Shout/Whisper) are pure
  `bounds -> anchors` generator functions plus a default tail shape/stroke —
  the "ease of use" default path. The anchor model itself is the escape
  hatch for arbitrary hand-edited shapes later.
- Deferred: text/lettering rendering, thought-bubble style (disjoint circle
  chain — breaks the single-polygon-per-tail union model), colour slots,
  character-bound tail targets, persistence, NativeAOT publish validation.
  See the design discussion in this repo's history for the full reasoning
  (bezier outlines, boolean-union tails, Avalonia+AOT tradeoffs, AGPL
  licensing check on the dependency stack).

## Character system design (proposed, not final)

These directions came out of design discussions. Treat them as the working
plan unless the user says otherwise.

### Three-layer model — keep these separate
- **CharacterDefinition**: the "wardrobe" — a 2D skeleton (VRM humanoid bone
  set) with a **per-view-angle rest layout** (front/three-quarter/profile are
  genuinely different 2D bone arrangements, not one rig viewed from different
  cameras), named colour slots, and the full catalogue of **stickers** ever
  available per slot (hair, torso, accessories, ...). Nothing here is
  issue-specific; it only grows as the series goes.
- **Revision**: a named, **project-level** snapshot of a character's *look* —
  which stickers are active per slot (slots are **stackable**: multiple
  simultaneously-active stickers per slot, ordered), colour slot values, and
  optional skeleton proportion overrides (aging up, redesigns). Revisions are
  user-named ("Post-Haircut", "Winter Arc") and reused freely across issues —
  not auto-generated per issue, not locked to one issue.
- **Pose / Expression**: pure data (bone rotations relative to parent,
  expression preset, view angle), independent of artwork and
  **angle-agnostic** — a pose rotates from whichever angle's rest layout is
  active, so the pose library doesn't need per-angle duplicates.
- **CharacterInstance** (per panel): references a definition + a revision
  (defaults to the issue's chosen revision for that character), plus pose,
  view angle, and sparse per-panel overrides (e.g. sunglasses for one shot).
  Changes to the definition or a revision reach every instance that hasn't
  locally overridden that field — like components/instances in Figma.

### Rendering approach
- **Version 1:** a 2D layered cutout rig (Spine/Moho/Pixton-style), rendered
  with SkiaSharp. Stickers are **rigidly attached to a single bone** — no
  mesh deformation (Live2D-style) — matching the still-image use case.
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

### UX expectations
- Pose by dragging, with inverse kinematics and pinnable feet or hands. Users
  shouldn't have to rotate individual bones.
- Pose and expression libraries (apply with one click, then adjust), mirror pose,
  and an angle control.
- Recolour through named colour slots (vector fills, or tint masks for bitmaps),
  not free-form recolouring.
- Skeleton/proportion editing (bone lengths, per-angle rest layout) is a
  **full rig editor**, not a simplified slider UI — it's the advanced/escape
  hatch surface, not part of the default per-issue flow. A thin convenience
  layer (e.g. a "height" slider) could sit on top of the same rig data later
  without changing storage.
- Escape hatches: flatten to editable layers, custom SVG/PNG sticker import,
  rig editor.

### Open questions (ask the user before deciding)
- Target art style (Western cartoon, manga, semi-realistic, user's choice?).
- Whether characters come from a shipped sticker library or users author them
  fully.
- How much camera-angle freedom is needed beyond front/three-quarter/profile —
  any extra angle means new per-angle art for every existing sticker.

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
        <id>-jacket-a/
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

## Licensing constraint

The project is **AGPL-3.0**. Check the licence of every dependency before adding
it. Some character-animation runtimes need proprietary or per-user licences (for
example the Spine runtimes and the Live2D Cubism SDK). Prefer open formats
(glTF, VRM, DragonBones, SVG) and libraries compatible with the AGPL.
