# Stanley

Stanley is a comic editor built on .NET. It aims to be easy to use and quick to
learn, with escape hatches for advanced users.

> **Linux only.** Stanley currently ships as a Linux AppImage. There is no
> Windows or macOS build.

## Install

See [Getting and installing Stanley](docs/automatic-builds.md) for how to
download, install, keep on the start menu and self-update. Short version:
download the `Stanley*.AppImage` from the [Releases](../../releases) page,
`chmod +x` it, and run it.

## System requirements

- **Linux only** (x86-64 or arm64). There are no Windows or macOS builds, and
  none are currently planned.
- A distribution able to run AppImages (most mainstream distros; `libfuse2` is
  only needed to silence a harmless warning, not to run the app — see the
  install guide).
- The usual desktop Linux graphics libraries: `libfontconfig1`, `libx11-6`,
  `libice6`, `libsm6`, `libxrandr2`, `libxi6`, `libxcursor1`, `libgl1`. These
  are present on virtually all desktop installs already.
- No .NET installation needed — the AppImage is self-contained.

## Features

- **Page and panel layout.** Grid-based page templates, panel splitting,
  resizing and re-tiling, with snapping and an optional layout lock that keeps
  geometry fixed while panels stay selectable.
- **Speech bubbles.** Word-wrapped bubble text that grows the bubble to fit
  instead of shrinking the letters, with tails and standard bubble styles.
- **Characters.** Posable mannequin characters (front or side view) built from
  sliders, posed by dragging hands/feet/hips/chest/head or from a preset pose
  gallery, with independently shaped, coloured and expressive eyes.
- **Stickers.** Dress characters in hair, faces, clothes, prints (including
  your own text and emoji) and accessories, in any colour, layered (a cap
  under a hood, a shirt over a T-shirt) and styled (hood up or down, brim
  angle), per-character or per-panel.
- **Panel elements and backgrounds.** Drawn shapes, free text, pictures and
  speed lines, placed behind or in front of characters, over a colour,
  gradient or picture background.
- **Title pages and formats.** Comic book pages, comic strips and webcomics,
  plus an optional title page.
- **Thought clouds.** A scalloped panel for what a character imagines, with a
  draggable trail of dots pointing back to the thinker.
- **Copy, cut, paste, duplicate and Alt-drag** for anything selected on a
  page, including between comics, via Stanley's own clipboard.
- **Multi-select.** Shift-click to select several bubbles, characters or
  elements at once and move, nudge, Alt-drag or delete them together.
- **Undo/redo** for every editing action.
- **Multiple issues per comic**, added and switched from File › Info or the
  title bar's issue switcher.
- **My Assets.** A personal, per-user library of characters and page groups
  that can be reused across comics.
- **Keyboard-discoverable UI.** Holding Ctrl shows every on-screen button's
  shortcut, like Office's KeyTips.
- **Self-updating.** Built-in update checking and installing on a Stable or
  Nightly channel.
- **Git-friendly project format.** Comics are saved as plain folders/files
  suitable for version control, not an opaque binary blob.
- **Command-line interface.** The same `stanley` executable also works from
  the terminal (`stanley init`, `stanley issue add`, `stanley issue list`) for
  scripting and automation.

## Goals

- **Flat learning curve.** A new user should be able to lay out pages, place
  characters and add speech bubbles without reading a manual.
- **Characters without redrawing.** Characters are reusable models that can be
  posed, re-expressed and restyled in every panel instead of being drawn by hand
  each time.
- **Escape hatches.** Advanced users can import their own art, edit character
  rigs, or flatten anything to plain editable layers and paint over it.

## Planned character system

Each character is split into three layers:

| Layer | Contains |
|---|---|
| **Character definition** | Skeleton, body parts, customisation slots (colours, swappable parts such as hair or clothes, proportion sliders), view angles |
| **Pose / expression** | Bone rotations, facial expression preset and view angle, stored as data apart from the artwork |
| **Panel instance** | A reference to the definition, plus a pose and any changes for that panel only |

If you change a character's definition, such as their jacket colour, the change
reaches every panel they appear in. Poses are portable: any pose from the library
works on any character that uses the standard skeleton.

The first version will use a **2D layered ("cutout") rig** with front,
three-quarter and profile views. You pose a character by dragging its limbs, and
the rest of the limb follows (inverse kinematics). A library of ready-made poses
and expressions will be included. A 3D backend (glTF/VRM with toon shading) may
come later behind the same interfaces.

## Licence

Stanley is released under the [GNU Affero General Public License v3.0](LICENSE).
