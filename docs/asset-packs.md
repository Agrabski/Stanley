# Asset packs — reusable, shareable content across comics

**Status: in progress.** Slices 1–3 of §10 are implemented (grouping, the
storage core, keep and add); slice 4 onwards is still the plan. This supersedes `my-characters.md`, which
designed sharing for characters only. It came out of #99 ("save a group as an
asset you can move between comics"), which needs #86 ("group several objects
into one") first, and generalises the whole "My Characters" idea (still the
right model) to every kind of reusable content instead of characters alone.
Treat it as the current working plan unless the user says otherwise; nothing
here is final. See `CLAUDE.md` for the project's overall priorities.

Scope: grouping panel objects into one object (#86), saving anything reusable —
a group, a character, a sticker, a pattern, a background, a pose — under a
name so it can be reused across comics and shared with other people. It does
not change how any of these things are built, drawn or posed.

## 1. The problem, from the user's side

In their words:

- "I built a little rocket ship out of shapes and text. I want to drop it into
  every panel of this chapter without rebuilding it." (#86, #99)
- "Someone on the forum made a great prop / sticker pack / background. Can I
  use it?"
- "I made Alice. I want her in my new comic." (`my-characters.md` §1, unchanged)
- "I gave Alice a new haircut. Did my finished comic change?" Silent change is
  still the worst thing that can happen to this user.
- "I copied my comic to a USB stick and Alice — or my rocket ship — is gone."
- "My new Space Cats story needs the whole crew, their ship, and the nebula
  background again. Do I rebuild it all by hand?"

None of package, path, tag, commit, version, sync or manifest means anything
to this user. A feature that needs those words fails the project's first
priority. "Asset" is the one piece of jargon this plan keeps, because there's
no plainer word that covers a group, a character, a sticker and a background
at once — the UI calls it **My Assets**, matching what Stanley already calls
individual library items internally (`StickerAsset`).

## 2. Decisions

1. **Anything reusable lives in My Assets**: one place per user, every comic
   can see it, like My Characters but for every kind of content. There are no
   packages, paths or git references in the default UI.
2. **An asset pack can hold any mix of kinds.** A "Space Cats crew" pack can
   be four characters, their ship (an object group), and a nebula background,
   shared as one file. This is the generalisation `my-characters.md` §11 left
   for later ("groups inside a comic's own pane") plus the part #99 actually
   asks for (object groups) — done as one mechanism instead of two.
3. **Keeping is deliberate**: a star, or *Keep in My Assets*, on whatever's
   selected — a group on the page, a character, a sticker in the picker, a
   background, a saved pose. The relevant gallery also offers items from your
   recent comics, so first reuse needs no preparation.
4. **Every comic contains its own copy of everything it uses.** My Assets is
   a convenience, not a dependency: if it's lost, any comic still renders and
   any asset can be put back by re-adding it.
5. **Changing a kept asset asks one plain question**: just in this comic, or
   everywhere? *Just in this comic* is pre-selected, as a bar, never a modal.
6. **Other comics never change by themselves.** They show a before/after
   preview with **Update** and **Keep as is**.
7. **Sharing means sending a file.** Double-clicking it adds the assets.
   Sending a newer file updates the receiver's copy.
8. **Git compatibility comes from the file format**, never from a feature the
   user has to operate. Stanley doesn't run git.
9. **Copies keep ids.** *Duplicate* (new id) stays the way to fork anything.
10. **Packs sort My Assets like albums**, and can mix kinds freely — this is
    the "group" from `my-characters.md` §2.9, renamed so it stops colliding
    with the page-level *group* from #86 (§3): a **pack** lives in My Assets
    and can hold characters, object groups, stickers, patterns, backgrounds
    and poses in any combination; a **group** lives on a page and only holds
    panel elements. A pack can be added to a comic in one step and shared as
    one file. Deleting a pack never deletes what's in it. Packs exist only in
    My Assets — a comic's own folders don't change.
11. **Building a pack is deliberate and happens in the gallery.** The My
    Assets page lets the user multi-select across every kind at once (an
    object group here, two characters there, a background) and name the
    result — not just something that falls out of keeping items one at a
    time. §6.2 spells this out; it's the direct answer to "a gallery for
    creating a named pack of anything, in any combination."
12. **Grouping objects on a page (#86) is a separate, smaller feature that
    this depends on.** It ships first, on its own, because it's useful with
    no asset-pack machinery at all (a rocket ship you can drag as one object
    in *this* comic).

## 3. Object grouping (#86) — the prerequisite

Selecting several panel elements (shapes, free text, pictures, speed lines —
the "Panel elements" from `CLAUDE.md`, not bubbles or characters, which have
their own placement model and stay out of a `GroupElement`, see §3.1) and choosing **Group**
(ribbon, right-click, `Ctrl+G`) replaces them with one new panel element:

- **`GroupElement`**: an ordered list of child `PanelElement`s, each still
  holding its own absolute geometry — no separate position/rotation/scale
  field on the group itself, because no `PanelElement` stores one today
  (`TextElement`/`PictureElement` keep an absolute `Rect2D`, `ShapeElement`
  absolute anchors). A group is exactly today's transient multi-selection
  (`PageEditorViewModel`'s "drags as one, nudges as one, deletes as one undo
  step", `CLAUDE.md` Shift+click) made persistent: **Move** translates every
  child by the same delta (recursing for nested groups); **Resize** rescales
  every child from the group's old bounding box to the new one, the same
  rect-to-rect remap `ElementEditing.Refit` already does when a panel itself
  is resized. **Rotation is out of scope for v1** — nothing in Stanley
  rotates an element today, and inventing that primitive is a separate,
  larger piece of work this issue doesn't need to unblock #99.
- **Ungroup** (`Ctrl+Shift+G`) dissolves it back into loose elements at their
  current absolute positions — already true by construction, so this is
  just removing the wrapper, a lossless round trip.
- A group is one selectable, draggable and resizable object; double click
  (or a ribbon button) enters it to select/edit a child without ungrouping,
  the way entering a group works in vector editors.
- **To front / To back**, copy/cut/paste/duplicate, Alt-drag, and undo/redo
  all treat a group as one `PanelElement` — no special-casing needed beyond
  `GroupElement` itself implementing the same interfaces existing elements do.
- Nested groups (a group containing a group) are allowed, since it falls out
  of `GroupElement`'s children being `PanelElement`s — no extra code, but v1
  UI need not expose grouping a group beyond what "select the group + another
  element + Group" already does naturally.

This slice alone closes #86 and is useful with zero asset-pack UI.

### 3.1 Grouping characters and bubbles too (#125)

A shape and a character couldn't be grouped ("Group" stayed greyed out), which
is the "group a character with its prop" case open question 2 below said to
revisit. **A `GroupElement` still can't hold them** — a character's box comes
from its definition, rig and pose (`PageEditorViewModel.CharacterBounds`),
none of which `PanelElements.Bounds` in `Stanley.ProjectModel` can see, and
the group would have to draw between the two element passes. So the user still
just presses **Group** (Ctrl+G) and the editor picks the mechanism:

| The selection is… | Group makes… |
|---|---|
| elements only, all on one side of the characters | a `GroupElement` (§3, unchanged) |
| anything else: a character, a bubble, elements from both sides of the characters | a **link**: each member keeps its own list and z-order and gets the same `GroupLinkId` |

- `PanelElement.Link` (base record, `init`), `CharacterInstance.Link` and
  `Bubble.Link`, all `GroupLinkId?`, `WhenWritingNull` so an ungrouped page's
  JSON is unchanged. Scoped to one panel, like an `ElementId`. Pure functions:
  `Grouping.Link` / `Unlink` / `MembersOf`.
- **A link is only ever a selection.** `PageEditorViewModel.Select` (and
  `ToggleSelect`, for Shift+click) adds every member sharing a link of anything
  it selects (`AddGroupedSiblings`, `ToggleGroup`), and the existing
  multi-selection machinery does the rest — drag, nudge, Alt+drag and delete
  are already "one undo step for the whole selection". Rendering, hit-testing,
  bounds and the drawing order never look at `Link`. The canvas calls
  `SelectGroupOf` before a press or right-click so a press on an unselected
  member drags the whole group. The only way to reach one member is to Ungroup.
- **Grouping a group with something else makes one bigger group** (every
  selected member gets one new link; the old ones simply stop existing).
  **Ungroup** on a link removes it from everything selected (`Grouping.Unlink`);
  on a lone `GroupElement` it unwraps as before. If both are true of a
  selection, the link goes first.
- **Anything that copies must not copy the link into the original's group.**
  `Clippings.Copy` (bubble, element, character) sets `Link = null`;
  `Clippings.Copy(Panel)` keeps a panel's groups (they're all inside it);
  `BeginDuplicateSelection` (Alt+drag) gives the copies a group of their own via
  `Clippings.Relinker`. `Grouping.Group` clears the link of what it wraps. New
  code that builds a copy of one of these three records needs the same care —
  `with { ... }` alone carries the link over.
- **Not for linked groups (yet):** resizing as one (a handle drags only the
  primary member, as in any multi-selection today), Copy/Cut/Paste of the whole
  group (the clipboard holds only the primary item), *Keep in My Assets*
  (`GroupElement` only — a character's own copy is kept via §6), and being one
  z-order slot (each member keeps its own place in the panel's stack, `docs/layers-panel.md`).

## 4. Vocabulary

| The user sees | Under the hood |
|---|---|
| My Assets | `Documents/Stanley/My Assets/`, one subfolder per kind, plus `packs/` |
| Group (on the page) | `GroupElement`, a `PanelElement` (§3) |
| *Keep in My Assets*, the star | Copy the asset's files there; record its fingerprint in the comic |
| *Just in this comic* / *Everywhere* | Whether saving the comic also writes My Assets |
| "Updated in My Assets", *Update* / *Keep as is* | Fingerprint comparison, replacing the comic's copy |
| A pack | `packs/<id>-slug.json`: a name and a list of `(kind, id)` refs, any mix of kinds |
| *New pack…*, *Add to pack ›* | Create/append a `packs/*.json` entry from whatever's multi-selected in the gallery, or from one kept item's menu |
| *Share…*, a `.stpack` file | A zip of the referenced assets, plus the pack definition when a named pack is shared |

Words the user never sees: pack, package, path, tag, commit, version, sync,
fingerprint, manifest.

## 5. Kinds of asset and what "kept" means for each

| Kind | What's copied into My Assets | Kept from |
|---|---|---|
| Object group | The `GroupElement` and everything it references (art files it points at) | Right-click a group on the page |
| Character | Definition, wardrobe stickers, looks — exactly `my-characters.md`'s scope | Characters pane |
| Sticker | One sticker's `sticker.json` + art files, independent of any character | Sticker picker / wardrobe |
| Pattern | One fill pattern's tile art | Shape Fill picker |
| Background | A panel's background composition (colour/gradient/picture/props) | Background editor |
| Pose | Bone rotations + expression preset (angle-agnostic, per `character-and-project-plan.md`) | Pose gallery / "Save pose…" |

All six share one mechanism (§6–§9); only the fingerprinted file set and the
before/after preview renderer differ per kind, matching how `StickerAsset`,
`GroupElement`, `CharacterDefinition` etc. already differ as types today.

## 6. How it works for the user

This is `my-characters.md` §4 generalised; only what's different from that
design is spelled out here.

### 6.1 Keeping something

Wherever an asset of one of the six kinds is selected, its context menu gets
**Keep in My Assets** (a star). Once kept: **Save to My Assets** (when
changed), **Remove from My Assets** (with the same "comics that use it keep
their own copy" confirmation), and **Add to pack ›** (listing My Assets packs
plus *New pack…*). Writing happens immediately, with an **Undo** toast, same
as `my-characters.md` §4.1.

**Keep all in My Assets**, on the page and on the Characters pane, keeps
everything selected (or, on the Characters pane, every character) at once
into one named pack — a quick on-ramp for "save my whole scene as a kit", but
still just one instance of the general mechanism in §6.2.

### 6.2 Building a pack deliberately, in the gallery

Keeping things one at a time (§6.1) is how a pack usually *starts*, but the
My Assets page (the File-view page from §9) is where one is built on purpose:

- The page shows every kept asset in one grid, with kind filter chips (*All*,
  *Object groups*, *Characters*, *Stickers*, *Patterns*, *Backgrounds*,
  *Poses*) above it — chips narrow what's shown, they don't limit selection.
- **Multi-select works across chips.** Switching from *Characters* to
  *Backgrounds* keeps whatever was already ticked, so picking two characters,
  an object group and a background for one pack means: tick two, switch
  chip, tick one, switch chip, tick one.
- **New pack…** (toolbar, or right-click on a selection) names the current
  selection and saves it to `packs/`, in any combination of kinds — this is
  the literal "gallery allows for deliberately creating a named pack
  containing groups of objects, character designs or individual assets, or
  any combination of these" behaviour this section exists to spell out.
- An existing pack is just a saved chip of its own (*My packs* in the filter
  row): opening one selects its members, so **Add to selection**, remove a
  member, and **Save as new pack…** / overwrite the open pack all fall out of
  the same multi-select, no separate "edit pack" UI needed.
- Dragging a tile onto a pack tile in the pack list adds it, matching
  `my-characters.md` §4.5's "drag characters onto a group on the My
  Characters page", generalised to any kind.

### 6.3 Adding an asset to a comic

Each kind's existing picker (Characters pane's *Add character*, the sticker
picker, Shape Fill, the background editor, the pose gallery) gains a **My
Assets** section alongside its built-in library, plus **From your other
comics** and **Add from file…** — same three sources as `my-characters.md`
§4.2, just present in six places instead of one. The page canvas additionally
gets an **Insert from My Assets…** command for object groups, since there's
no existing "picker" for arbitrary panel content to extend.

Picking a tile copies the asset in, keeping its id, and (for an object group)
drops it in placement mode the way pasting does.

### 6.4 Staying in step, and sharing

Unchanged from `my-characters.md` §4.3–§4.6: the Just-here/Everywhere bar,
the before/after badge with Update/Keep as is, and Share…/**Add from
file…**/drag-and-drop for `.stpack` files — all generalised to work over any
kind and over mixed-kind packs. A shared pack's before/after view shows a
strip of thumbnails (one per member) rather than one character figure when
it's mixed-kind.

## 7. Files and data

### 7.1 The comic: same layout, one new field per kind

Exactly `my-characters.md` §6.1's pattern, generalised: whatever file already
names an asset (`character.json`, a future `group.json` for a `GroupElement`
saved to disk — see below, `sticker.json`, a pattern's own JSON, a saved
background or pose file) gets one optional field, `myAssetsVersion`, holding
the fingerprint it last matched. `null` is omitted, so nothing existing
changes shape.

A `GroupElement` living only inline inside a panel's `page.json` has nothing
to attach a version to; keeping one in My Assets is what gives it its own
`group.json` under the comic's `objects/` folder (new — mirroring `stickers/`,
`patterns/`), which the panel then references by id the way it already
references stickers. This is the one real schema addition: **panels can
reference an out-of-line object-group definition**, not just inline elements.

### 7.2 The My Assets folder

```
Documents/Stanley/My Assets/
  characters/<id>-alice/...              # exactly my-characters.md §6.2
  objects/<id>-rocket-ship/
    group.json
    art/...                              # pictures the group's elements reference
  stickers/<id>-.../sticker.json + art
  patterns/<id>-.../tile art
  backgrounds/<id>-.../background.json + art
  poses/<id>-....json
  packs/<id>-slug.json                   # { name, members: [{kind, id}, ...] }
```

Each kind's folder is read/written by the same store code a comic uses for
that kind — `objects/` by whatever `Stanley.ProjectModel` uses to persist a
panel's elements today, `characters/` by `CharacterStore` (`my-characters.md`
§9), and so on — so the format can never drift between "in a comic" and "in
My Assets". A pack file is the same shape as `my-characters.md`'s character
group, with a `kind` tag added to each member entry so it can mix kinds.

### 7.3 Fingerprints

One `AssetFingerprint(kind, files)` function replaces `CharacterFingerprint`,
built the same way (SHA-256 over canonical, id-keyed, sorted paths; SVG line
endings normalised; JSON already canonical per `ProjectJsonOptions`) but
parameterised over which files make up "the asset" for each kind. The states
table (`my-characters.md` §6.3: Not kept / Matches / Changed here / Changed in
My Assets / Changed in both / Unknown history) is unchanged and kind-agnostic.

### 7.4 The share file

A `.stpack` file (short for "Stanley pack" — see open question in §11 on the
extension itself) is a zip, `stanley-assets.json` first for content-based
recognition, exactly `my-characters.md` §6.5's shape:

```
Space Cats crew.stpack
  stanley-assets.json         # { "format": 1, "stanley": "<version>" }
  characters/<id>-alice/...
  objects/<id>-rocket-ship/...
  backgrounds/<id>-nebula/...
  packs/<id>-space-cats-crew.json   # only when a named pack was shared
```

Import validation is unchanged from `my-characters.md` §6.5: relative paths
under the seven known top-level folders only, no `..`, a pack file lists only
members present in the file, a cap on entry count and unpacked size, art
restricted to the formats each kind already accepts, JSON through the normal
readers, and any failure refuses the whole file with one plain message.

Sharing a hand-picked gallery selection that was never saved as a named pack
(§6.2) still works and writes no `packs/*.json` entry, exactly as
`my-characters.md` §6.5 already allowed for a hand-picked set of characters —
*New pack…* is for reuse, plain multi-select-and-Share is for a one-off send.

## 8. Git

Unchanged from `my-characters.md` §7: a comic's folder layout doesn't change
beyond the additive `objects/` folder (§7.1) and the one new optional field;
every clone still renders standalone; My Assets and `.stpack` files are for
convenience and for moving content between people, never referenced by a
comic; for advanced users, *Keep My Assets in…* a git repo gives it history
Stanley doesn't need to know about.

## 9. Where code goes

| Project | What |
|---|---|
| `Stanley.ProjectModel` | `GroupElement` and the `objects/` folder (§7.1); generalise `CharacterStore`'s pattern into per-kind stores; `AssetFingerprint`, `AssetShareFile` (the `.stpack` zip), `AssetPack`/`AssetPackId`, `myAssetsVersion` on each kind's definition. Avalonia-free, as `my-characters.md` §9 already specifies for the character slice. |
| `Stanley.Editing` | Pure functions: `Grouping` (page-level group/ungroup, §3), the kept/changed state machine and `Update`'s keep-what's-used rule, generalised over kind. |
| `Stanley.Editors` | Group/ungroup ribbon + right-click + ‘enter group’ on the page canvas; each picker's My Assets section; the Just-here/Everywhere bar; the before/after view (thumbnail strip for mixed packs); `MyAssetsLibraryViewModel` (renamed/generalised from `CharacterLibraryViewModel`), including the gallery's cross-kind multi-select and *New pack…* (§6.2). |
| `Stanley.App` | `AppPaths.MyAssetsDirectory` + settings entry, the My Assets File-view page (kind filter chips alongside the pack list), `Program.Main` dispatch for a `.stpack` argument, file-type registration. |

## 10. Delivery slices (each a PR, each green on its own)

1. **Grouping (#86).** `GroupElement`, group/ungroup, selection/hit-testing/
   rendering/undo integration, ribbon + right-click + `Ctrl+G`/`Ctrl+Shift+G`.
   Ships and closes #86 with no My Assets code at all.
   - Tests: group/ungroup round-trips losslessly (positions match pre-group).
     A group drags, resizes, rotates, copies, Alt-drags and deletes as one
     undo step. To front/back on a group behaves like on any element. Nested
     grouping works.
2. **Storage core.** `objects/` folder + `GroupElement` persistence; extract
   `CharacterStore`'s pattern generically; `AssetFingerprint` for object
   groups and characters first (the two kinds #99 and `my-characters.md`
   actually need); `MyAssets` read/write for those two kinds plus generic
   `AssetPack`.
   - Tests: as `my-characters.md` §10 slice 1, plus: a group saved to
     `objects/` and referenced from a panel round-trips byte-for-byte; the
     fingerprint changes when a child element changes and not on an
     unrelated panel edit.
3. **Keep and add — object groups + characters.** *Keep in My Assets* on a
   page group and on a character; the two pickers' My Assets sections;
   `AppPaths`, settings, File-view page skeleton (grid + kind chips, no
   multi-select/pack creation yet).
   - Tests: as `my-characters.md` §10 slice 2, run for both kinds.
   - *As built:* the Characters pane keeps its **New character** button and
     gains **Reuse a character** beside it (the gallery: My Assets, then each
     recent comic's characters) rather than folding both into one *Add
     character* button - a new character stays one click. A kept page group is
     named "Group N" and renamed on File › My Assets. What's fingerprinted for
     a group is position-free (children stored relative to the group's
     top-left, numbers rounded to 0.1 µm, child ids derived from the group's
     id), so dragging a kept group around - or inserting a fresh copy - never
     reads as a change; resizing it does. Recent comics are read when the
     gallery opens (cached for the session), not in the background. The comic's
     own `objects/` copies only ever grow, like its pictures, since another
     issue may still use one. Undo toasts for My Assets writes come with
     slice 4.
4. **Staying in step.** The bar, Everywhere-on-save, before/after with
   Update/Keep as is, the open/focus check — generalised, tested against
   object groups and characters.
   - Tests: as `my-characters.md` §10 slice 3.
5. **Packs (My Assets albums), mixed-kind, built deliberately (§6.2).**
   Cross-chip multi-select on the gallery page, *New pack…*, *Add to pack ›*,
   *Keep all in My Assets*, rename/delete/drag-onto, and *Add all* on a pack
   from any other picker — a pack mixing an object group with characters
   throughout.
   - Tests: as `my-characters.md` §10 slice 4, plus: selecting across two
     kind chips and choosing *New pack…* saves both kinds into one pack file;
     *Add all* from a mixed-kind pack adds every member as one undo step and
     skips ones the comic already has.
6. **Sharing files (`.stpack`).** Share…/Add from file…/drop-onto-window/
   import view — generalised validation over the two kinds.
   - Tests: as `my-characters.md` §10 slice 5.
7. **Double-click / file association.** Same as `my-characters.md` §10
   slice 6, extension renamed.
8. **Extend to stickers, patterns, backgrounds, poses.** One PR per kind,
   reusing every mechanism above — each is "plug a new `AssetFingerprint`
   file set and a picker section into what slices 2–6 already built."
   Lowest priority: object groups and characters are what #99 and the
   existing design actually ask for.
9. **CLI, optional.** `stanley assets list`, `export`, `import` (any kind or
   a pack), superseding `my-characters.md` §10 slice 7's narrower command.

## 11. Later, and open questions

**Later** (unchanged from `my-characters.md` §11 unless noted):
- Groups inside a comic's own Characters/page panes for a large cast or a
  busy panel — the My Assets pack already covers the cross-comic case.
- A creator name and plain-language credit/licence in `.stpack` files.
  Matters more now that packs can carry drawn art and props, not just
  character sliders — worth revisiting sooner than "later" once sharing
  ships (§10 slice 6), see `CLAUDE.md`'s licensing constraint on bundled art.
- Sharing by link or a web gallery; dragging a My Assets tile straight onto
  a panel; watching My Assets for changes while a comic is open.

**Open questions:**
1. **The file extension.** `.stpack` above is a placeholder — needs a short,
   collision-unlikely choice the way `my-characters.md` §10 slice 6 picked
   `.chp`. *Recommended:* decide alongside slice 6, not now.
2. **Can a group (§3) contain a character or a bubble?** *Answered by #125
   (§3.1):* yes, by tying them together with a shared link rather than by
   nesting them in a `GroupElement`.
3. **Do stickers/patterns/backgrounds/poses (§10 slice 8) all ship, or does
   #99 only need object groups + characters?** *Recommended:* ship 1–7 first
   and treat slice 8 as separate follow-up work, reusing this same doc.
