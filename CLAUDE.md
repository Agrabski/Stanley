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

No code, solution or build system exists yet. The only files are `LICENSE`, the
`README.md` and this file. The UI stack (Avalonia / WPF / MAUI) has not been
chosen; do not assume one. Before starting implementation, confirm the stack with
the user, then update this file with build, test and lint commands.

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

## Licensing constraint

The project is **AGPL-3.0**. Check the licence of every dependency before adding
it. Some character-animation runtimes need proprietary or per-user licences (for
example the Spine runtimes and the Live2D Cubism SDK). Prefer open formats
(glTF, VRM, DragonBones, SVG) and libraries compatible with the AGPL.
