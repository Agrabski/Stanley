# Stanley

Stanley is a comic editor built on .NET. It aims to be easy to use and quick to
learn, with escape hatches for advanced users.

> **Status:** very early stage

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

## Builds and releases

Test builds come out nightly; see
[Getting and installing Stanley](docs/automatic-builds.md) for how to
download, install and self-update.

## Licence

Stanley is released under the [GNU Affero General Public License v3.0](LICENSE).
