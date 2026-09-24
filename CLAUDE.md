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

The speech-bubble POC (see below) and the project/data model (persistence only —
no editor UI yet) exist so far. No character rendering, no document/page editor.

```
src/Stanley.Bubbles/      # bubble geometry model, no Avalonia dependency (SkiaSharp only)
src/Stanley.ProjectModel/ # project/data model + JSON persistence, no Avalonia/SkiaSharp dependency
src/Stanley.App/          # Avalonia POC host (single-bubble editor)
tests/Stanley.Bubbles.Tests/       # xunit v2, geometry unit tests
tests/Stanley.ProjectModel.Tests/  # xunit v2, id/serialization/repository unit tests
tests/Stanley.App.HeadlessTests/   # xunit v3 (Avalonia.Headless.XUnit requires it), UI smoke tests
```

Build/test/run:
```
dotnet build Stanley.slnx
dotnet test tests/Stanley.Bubbles.Tests/Stanley.Bubbles.Tests.csproj
dotnet test tests/Stanley.ProjectModel.Tests/Stanley.ProjectModel.Tests.csproj
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

## Project & data model (implemented)

Implemented in `Stanley.ProjectModel`, the persistence layer described under
"Project & data model" in `docs/character-and-project-plan.md`. No editor UI
consumes it yet; `ProjectRepository` is a `dotnet build`/`dotnet test`-only
persistence layer so far. Character rendering (rig, stickers-as-pixels,
posing, IK) itself is not implemented — this is the *data model* those
features will read and write.

- **One id struct per stable-id entity** (`CharacterId`, `PanelId`, etc., in
  `Ids/`), each a validated opaque token — non-empty, no path separators, no
  `-` (reserved as the folder-name id/slug delimiter, so an id can never be a
  false-positive prefix match for another, longer id). `FromValue`/`Parse`
  bring an id in from disk or JSON; `New()` mints one. Each has its own
  `StrongIdJsonConverter<TId>` (value form and, for id-keyed maps like
  `Issue.CharacterRevisions`, property-name form) — no reflection, so this
  stays NativeAOT-safe under source-generated `System.Text.Json`.
- **Folder structure is enforced, not optional**: `ProjectRepository` computes
  every path from `ProjectPaths`; callers only ever pass ids and entity
  values, never a path. Most entities get a `<id>-slug` folder/file (the slug
  is cosmetic, recomputed only when an entity is first created — renaming
  later doesn't move or rename its folder, so a rename never cascades into
  unrelated diffs); panels are the one exception (`<id>.json`, no slug, since
  panels aren't user-named).
- **JSON conventions** (`Serialization/`): 2-space indent, alphabetically
  sorted object keys (a `JsonTypeInfo` modifier over the source-generated
  `StanleyJsonContext`, so declaration order in C# can stay readable while
  the JSON output stays sorted), camelCase property *and* enum-value names,
  trailing newline. `SortedDictionary` is used wherever a map's key order
  isn't itself meaningful (colour slots, id-keyed maps); an explicit ordered
  id array (never dictionary/filename/folder-position order) is used
  wherever order *is* meaningful (z-order, reading order, stacking order).
- **Geometry/skeleton is plain data, no SkiaSharp/Avalonia dependency**:
  `Point2D`/`PanelShape` reimplement the anchor-ring model `BubbleOutline`
  uses (deliberately not shared, to keep this project dependency-free);
  `HumanoidBone` is the full VRM 1.0 humanoid bone set; bone rest
  poses/rotations are `IReadOnlyList<(bone, value)>`, not
  `Dictionary<HumanoidBone, T>`, to sidestep enum-as-dictionary-key edge
  cases entirely.
- **Not yet designed**: bubble persistence (`Panel.Bubbles` is a placeholder
  `IReadOnlyList<BubbleId>` — Stanley.Bubbles has no JSON format yet),
  `sticker.json`'s exact schema beyond what's implemented here (the design
  doc doesn't draw one explicitly), any convenience "create new project/
  character/issue" helpers beyond `ProjectRepository.Initialize` and raw
  `SaveX`/`LoadX`, and NativeAOT publish validation (same deferral as the
  bubble POC — analyzer-clean under `IsAotCompatible`, not yet published via
  a real `PublishAot` executable).

## Character system design (proposed, not final)

These directions came out of an early design discussion. Treat them as the
working plan unless the user says otherwise.

### Three-layer model — keep these separate
- **CharacterDefinition**: rig (skeleton), parts, customisation slots (named
  colour slots like "Skin", "Hair", "Jacket primary"; swappable part slots;
  parameter sliders), and view angle sets.
- **Pose / Expression**: pure data (bone rotations, expression preset, view angle),
  independent of artwork, so poses transfer between characters.
- **CharacterInstance** (per panel): references a definition, plus pose, camera or
  angle, and per-panel overrides. Changes to the definition reach every
  instance, like components and instances in Figma.

### Rendering approach
- **Version 1:** a 2D layered cutout rig (in the style of Spine, Moho or Pixton)
  with front / three-quarter / profile views, rendered with SkiaSharp. Mesh
  deformation (as in Live2D) is not worth the extra authoring work for still
  images at first.
- **Skeleton:** use the **VRM humanoid bone set** as the canonical skeleton even
  for 2D, so pose data could later drive a 3D backend or an angle-switching
  hybrid without migration.
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
- Escape hatches: flatten to editable layers, custom SVG/PNG part import, rig
  editor.

### Open questions (ask the user before deciding)
- Target art style (Western cartoon, manga, semi-realistic, user's choice?).
- Whether characters come from a shipped parts library or users author them
  fully.
- How much camera-angle freedom is needed. "Any angle" pushes toward 3D.

A fuller, evolving design (sticker/revision model, multi-issue project
structure, page/panel/background storage, git-friendliness rules) lives in
[`docs/character-and-project-plan.md`](docs/character-and-project-plan.md) —
that file is the plan, not implemented yet; this section stays the short
summary.

## Licensing constraint

The project is **AGPL-3.0**. Check the licence of every dependency before adding
it. Some character-animation runtimes need proprietary or per-user licences (for
example the Spine runtimes and the Live2D Cubism SDK). Prefer open formats
(glTF, VRM, DragonBones, SVG) and libraries compatible with the AGPL.
