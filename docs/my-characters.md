# My Characters — sharing characters between comics

**Superseded by [`asset-packs.md`](asset-packs.md).** That doc generalises this
design (#99 needs to share more than characters — object groups, stickers,
backgrounds, poses — via one mechanism instead of a character-only one) and is
now the working plan; this file is kept for the character-specific detail
`asset-packs.md` points back to, but treat `asset-packs.md` as authoritative
where the two differ.

**Status: proposed, not implemented.** This came out of a design discussion about
sharing a set of characters between projects. It went through "a cast is any
project", then dedicated cast packages referenced by relative path or git tag, before
landing here after looking at it from a non-technical user's point of view. §11 says
why the earlier ideas were dropped.

Scope: reusing a character in more than one comic, keeping those copies in step when
the character changes, sorting saved characters into groups, and giving characters to
other people. It does not change how a character is built, dressed or posed.

## 1. The problem, from the user's side

In their words:

- "I made Alice. I want her in my new comic."
- "My friend made a great character. Can I use it?"
- "I gave Alice a new haircut. Did my finished comic change?" A finished comic changing
  by itself, with no idea why or how to undo it, is the worst thing that can happen to
  this user. Git won't rescue someone who doesn't use it.
- "I copied my comic to a USB stick (a new laptop, an email) and Alice is gone."
- "My new Space Cats story needs the whole crew again. Do I add them one by one?"

None of package, path, tag, commit, version or sync means anything to this user. A
feature that needs those words fails the project's first priority.

## 2. Decisions

1. **Characters you want to reuse live in My Characters**: one place per user that every
   comic can see, like saved Sims or Pixton's character list. There are no casts,
   packages, paths or git references in the default UI.
2. **Keeping a character is deliberate**: a star, or *Keep in My Characters*. The
   *Add character* gallery also offers characters from your recent comics, so the first
   reuse needs no preparation. Picking one from another comic keeps it too.
3. **Every comic contains a copy of every character it uses.** A clone, a zip or a USB
   stick copy of a comic renders with nothing else. My Characters is a convenience, not
   a dependency: if it's lost, any comic can put Alice back.
4. **Changing a kept character asks one plain question**: just in this comic, or
   everywhere? *Just in this comic* is pre-selected. The question is a bar, never a
   dialog that interrupts a drag.
5. **Other comics never change by themselves.** They show Alice before and after, as
   pictures, with **Update** and **Keep as is**.
6. **Sharing with another person means sending a file.** Double-clicking it adds the
   characters. Sending a newer file is how their copy gets updated.
7. **Git compatibility comes from the file format**, never from a feature the user has
   to operate. Stanley doesn't run git.
8. **Copies keep the character's ids.** The id is the link between a comic's Alice and
   My Characters' Alice. *Duplicate*, which gives a new id, stays the way to fork a
   character (Alice's twin).
9. **Groups sort My Characters the way albums sort photos.** A group ("Space Cats
   crew", "The Smith family") is a named list of saved characters. A character can be
   in several groups, or in none. Deleting a group never deletes a character. A whole
   group can be added to a comic in one step, and shared as one file. Groups exist only
   in My Characters: a comic's own folder doesn't change.

## 3. Vocabulary

| The user sees | Under the hood |
|---|---|
| My Characters | A folder with `characters/` in the same format as a comic's, plus `groups/` |
| *Keep in My Characters*, the star | Copy the character folder there; record its fingerprint in the comic |
| *Just in this comic* / *Everywhere* | Whether saving the comic also writes My Characters |
| "Updated in My Characters", *Update* / *Keep as is* | Fingerprint comparison (§6.3); replacing the comic's copy (§6.4) |
| A group | `groups/<id>-slug.json` in My Characters: a name and a list of character ids (§6.2) |
| *Share…*, a `.chp` file | A zip of character folders, plus the group when a group is shared (§6.5) |

Words the user never sees: cast, package, path, tag, commit, version, sync, fingerprint.

## 4. How it works for the user

### 4.1 Keeping a character

- The Characters pane's right-click menu gets **Keep in My Characters**. A kept
  character shows a star on its row, and clicking the star is the same action.
- Once kept, the menu offers **Save to My Characters** (when this comic's Alice differs)
  and **Remove from My Characters**. Removing asks first: "Remove Alice from My
  Characters? Comics that use her keep their own copy."
- The menu also has **Add to group ›**, listing the groups plus *New group…* (§4.5). On
  a character that isn't kept yet, it keeps her first.
- **Keep all in My Characters**, at the bottom of the pane, keeps every character in
  the comic at once and puts them in a group. A small dialog names it after the comic
  ("Space Cats") and lets the user pick an existing group instead. This is the likely
  way in: finishing one comic and starting the next with the same cast.
- Writing to My Characters happens straight away, not on the next save of the comic.
  Ctrl+Z in the comic can't reach it, so each of these actions shows a toast with
  **Undo**. The previous My Characters version is kept in memory while the toast is up.

### 4.2 Adding a character to a comic

The pane's **New character** button becomes **Add character**, which opens a gallery:

- **New character**: a blank character, as the button makes today, opened in its
  editor.
- **My Characters**: every kept character, drawn with `CharacterFigure`. A row of
  chips above the tiles filters by group: *All*, then each group by name. With a group
  chosen, an **Add all (4)** button adds the whole group.
- **From your other comics**: characters in the comics on the recent list
  (`RecentProjects`, up to 10), grouped by comic title, each comic with its own
  **Add all**. Characters that are already in this comic or already in My Characters are
  left out. The comics are read in the background when the gallery opens and cached for
  the session. A comic that can't be read is skipped without a message.
- **Add from file…** at the bottom (§4.6).

Picking a tile copies the character into this comic, keeping its ids, and selects it.
It doesn't open the editor, because the user most likely wants to place her:
double-click or drag onto a panel works as usual. A character picked from another comic
is also kept in My Characters, so she has one home ("Alice is now in My Characters
too · Undo"). If the comic already has a character with that id, it's the same
character, so the gallery just selects her.

**Add all** does the same for every character in the group or comic, as one undo step
("Add Space Cats crew (4 characters)"), and skips characters the comic already has.
*Add all* from another comic also keeps those characters in a group named after that
comic.

### 4.3 Changing a kept character: just here or everywhere

The first time a kept character that matches My Characters is changed in this comic, a
bar appears at the top of her editor tab. It never covers or interrupts the canvas:

> Alice is one of your saved characters. Change her **[Just in this comic]** or
> **[Everywhere]**?

- **Ignoring the bar** means *Just in this comic*. The bar then shrinks to a quiet line:
  "Changed just in this comic · **Save to My Characters**".
- **Everywhere**: until the comic is closed, every save of the comic also saves Alice to
  My Characters. This includes AutoSave, which is on by default. The bar reads "Changes
  to Alice also go to My Characters · **Stop**".
- **Changes from the page**, such as saving an expression through `ICharacterCatalog.EditCharacter`,
  count too. The same question then shows as a strip over the page.
- The question is asked only when a character goes from matching My Characters to
  changed. A comic whose Alice was already changed "just here" shows the quiet line,
  not the question again.

A look that only this comic needs (Alice's winter coat for one story) is simply a
change made just in this comic. It needs no concept of its own.

### 4.4 When My Characters changes: other comics

- When a comic opens, a character whose My Characters version has changed gets a badge
  on its row: "Updated in My Characters". If any character has one, a single info bar
  at the top of the window says "2 characters were updated in My Characters ·
  **Review**". It's never a modal dialog.
- Clicking the badge shows the two versions side by side: this comic's Alice and My
  Characters' Alice. Both are drawn in the issue's look, front view with a Front/Side
  toggle, with the names of their looks underneath. There are two buttons:
  **Update Alice** and **Keep as is**.
- **Update** is one undo step ("Update Alice from My Characters"). §6.4 describes what
  it keeps.
- **Keep as is** records that this comic has seen that version (§6.3). The badge goes
  away until My Characters' Alice changes again. The decision is stored in the comic,
  so it survives reopening and needs no per-user state.
- If Alice was changed in both places, the before/after view says so: "You also
  changed Alice in this comic. Updating replaces those changes." It then offers
  **Update Alice**, **Save this one to My Characters** and **Keep as is**.

The check runs when a comic opens and when the window gets focus back (the user may
have changed Alice in another Stanley window in the meantime).

### 4.5 Groups

Groups work like photo albums: a character can be in several, and a group only points
at characters.

- **Making a group:** *New group* on the My Characters page (§5), *Add to group › New
  group…* on a character, or *Keep all in My Characters* (§4.1). A new group gets a
  name that isn't taken yet ("New group 2"), the way new characters do.
- **Filling it:** drag characters onto a group on the My Characters page, or use *Add
  to group ›* from either the page or a comic's Characters pane. *Remove from group*
  takes a character out of that group only.
- **Renaming or deleting a group** changes nothing else. Deleting asks: "Delete the
  group Space Cats crew? The characters stay in My Characters."
- **Removing a character from My Characters** also takes it out of every group.
- **Using a group:** *Add all* in the gallery (§4.2) and *Share…* on the group (§4.6).
- **Order:** groups and their characters are shown by name, like the Characters pane.
  There's no manual ordering to maintain.

Groups belong to My Characters only. A comic's Characters pane stays one flat list, and
the comic's folder doesn't record which groups its characters came from.

### 4.6 Sharing with someone

- **Share…** on a character's right-click menu saves one file, such as `Alice.chp`.
  The My Characters page (§5) allows selecting several characters and saves them
  together.
- **Share…** on a group saves the group and all its characters as one file, such as
  `Space Cats crew.chp`.
- The other person double-clicks the file, drags it onto Stanley's window, or uses
  **Add from file…** in the gallery. Stanley shows the characters with their figures,
  under the group's name if the file has one:
  - **New characters** are ticked.
  - **A character already in My Characters** (same id) shows before/after with
    **Replace**, **Keep both** and **Skip**. *Keep both* gives the incoming character
    new ids, as *Duplicate* does, and names it "Alice (2)".
  - **When a comic is open**, an **Also add to this comic** box is ticked.
- **A shared group** becomes a group in their My Characters. If they already have that
  group (same id), from an earlier file, the new members are added and nobody is taken
  out, so their own changes to the group survive. The group points at whichever copy
  of each character they ended up with: their existing one after *Replace* or *Skip*,
  the new one after *Keep both*.
- That is the whole update mechanism for other people's characters. No accounts, no
  server, no git.

## 5. Where My Characters lives

- **Default: `Documents/Stanley/My Characters/`**, not the app-data folder that
  `AppPaths` uses. It's the user's work, not Stanley's state. People back up
  Documents, and on many computers it is already synced (OneDrive folder backup on
  Windows, iCloud Desktop & Documents on a Mac), which covers several computers with
  nothing to set up.
- **Settings › "Keep My Characters in…"** moves it, for example into a Dropbox folder
  or a git repo. Moving copies the folder, then switches.
- **Tests:** when `STANLEY_DATA_DIR` is set, My Characters defaults to
  `<data dir>/My Characters`, so tests never touch the real Documents folder.
- **A My Characters page in the File view** (Backstage). A list on the left has *All*,
  then each group, then **New group**. The grid on the right shows the characters in
  whatever is picked, with multi-select, **Share…**, **Rename**, **Add to group ›**,
  **Remove from group** (inside a group), **Remove**, **Add from file…**, **Keep My
  Characters in…** and **Open folder** (for advanced users).

## 6. Files and data

### 6.1 The comic: same layout, one new field

A character that came from My Characters is an ordinary character in `characters/`.
The only addition is one optional field in `character.json`:

```jsonc
// characters/<id>-alice/character.json
{
  "myCharactersVersion": "sha256:9f2c…",   // absent for a character that was never kept
  "name": "Alice",
  …
}
```

It holds the My Characters version this comic's copy last matched. That's when the
character was added, kept, saved to My Characters, updated, or kept as is. It changes
only through those deliberate actions, never by opening a comic. `null` is omitted
(`WhenWritingNull`), so every existing character file reads and writes unchanged.

### 6.2 The My Characters folder

```
Documents/Stanley/My Characters/
  characters/
    <id>-alice/               # exactly a comic's characters/<id>-alice/
      character.json          # without myCharactersVersion
      stickers/…  patterns/…  revisions/…
    <id>-bob/…
  groups/
    <id>-space-cats-crew.json
```

- **Characters** sit under `characters/`, the same as in a comic and in a `.chp` file,
  and are read and written by the same code as a comic's (§9), so the format can't
  drift. The folder slug stays as first written, as everywhere else.
- **Each group is one file**, following the project's git rules: an id, a name, and the
  member ids.

  ```jsonc
  // groups/<id>-space-cats-crew.json
  {
    "characterIds": [
      "4k2m9x0q1c",
      "9f3b7w2d8e",
      "a1c5t6y7u0"
    ],
    "id": "p0n8r3s5v2",
    "name": "Space Cats crew"
  }
  ```

  Members are shown by name, so the id list is stored sorted by id. Adding or removing
  a member is a one-line diff, and the same group always writes the same bytes. Groups
  get their own strong id type, `CharacterGroupId`, next to the others in `EntityIds`.
- **There is no manifest.** The folder is all characters and groups.

### 6.3 Fingerprints and the states

`CharacterFingerprint` is a SHA-256 over the character's canonical files, built from
the in-memory definition the same way the repository would write it:
- `character.json` without `myCharactersVersion`
- every sticker's `sticker.json` and art files
- the pattern tiles
- every look file

Paths are keyed by id, not slug (`stickers/<stickerId>/variants/default/front.svg`),
and sorted ordinally. Each entry hashes its path, its length and its bytes. Line endings
in text files (SVG) are normalised before hashing, so a Windows checkout with
`core.autocrlf` doesn't look like a change. JSON is already canonical: sorted keys,
two-space indent and a trailing newline (`ProjectJsonOptions`).

Three values decide what the user sees:
- **C**: the fingerprint of this comic's copy.
- **B**: its `myCharactersVersion`.
- **L**: the fingerprint of My Characters' copy.

| Situation | State | What the user sees |
|---|---|---|
| No character with this id in My Characters | Not kept | Menu offers *Keep in My Characters* |
| C = L | Matches | The star, nothing else |
| C ≠ B, L = B | Changed here | The question, then the quiet line (§4.3) |
| C = B, L ≠ B | Changed in My Characters | Badge and before/after (§4.4) |
| C ≠ B, L ≠ B, C ≠ L | Changed in both | Badge; before/after with the "both" wording |
| B absent, C ≠ L | Unknown history (a comic from before this, or a collaborator's) | Like *changed in both*, worded "Alice here is different from Alice in My Characters" |

When C = L but B differs (both sides made the same change), the character counts as
matching. B is corrected the next time the comic saves anyway, never by a save made
just for it.

### 6.4 Updating safely

`Update` replaces the comic's definition, wardrobe and looks with My Characters'
version. It then puts back anything this comic still uses that the new version lacks:
- a look that an issue (`issue.json`'s look map) or a panel points at, with the
  stickers that look wears
- a sticker that a panel override wears

Everything else from the old copy goes. If anything was put back, C ≠ L afterwards,
and the character honestly shows as changed here. Nothing a panel shows can be deleted
by an update.

### 6.5 The share file

A `.chp` file is a zip, using `System.IO.Compression` from the .NET libraries, so
there's no new dependency. `stanley-characters.json` is always the first entry, so the
file can be recognised by its content as well as its name (§10, slice 6):

```
Space Cats crew.chp
  stanley-characters.json         # { "format": 1, "stanley": "<version that wrote it>" }
  characters/<id>-alice/…         # exactly the folder from a comic
  characters/<id>-bob/…
  groups/<id>-space-cats-crew.json  # only when a group was shared
```

A file holds at most one group, and every character the group lists is in the file.
Sharing a hand-picked set of characters writes no group.

The file is untrusted input. Import checks every entry before writing anything:
- The path is relative and under `characters/` or `groups/`, with no `..`.
- A group file lists only characters that are in the file.
- There's a cap on the number of entries and on the total unpacked size.
- Art is SVG or PNG only, and SVG goes through the existing reader (sticker-system §6.1).
- JSON is parsed with the normal readers.

Any failure refuses the whole file with one plain message: "This isn't a Stanley
characters file, or it's damaged." Nothing is half-imported.

## 7. Git

- A comic's folder layout doesn't change, and nothing in a comic points outside it.
  Every clone renders.
- An update shows up in git as an ordinary diff of that character's files, which can
  be reviewed and reverted like any other change.
- Someone who clones a comic gets all its characters. Their own My Characters stays
  theirs. `myCharactersVersion` is a fingerprint of content, not a per-person value, so
  if a collaborator keeps Alice too, the states in §6.3 still work for them.
- Groups live only in My Characters, one small JSON file each. Sorting characters into
  groups never changes a comic's files.
- For advanced users, My Characters is a folder in the project format. Moving it into
  a git repo (*Keep My Characters in…*) gives it history. Stanley doesn't know or care.
- `.chp` files are for moving characters between people, not for committing, and
  nothing in a comic refers to one.

## 8. Edge cases

- **Adding a character whose id is already in the comic** selects the one that's there.
  If My Characters' copy differs, the states in §6.3 apply.
- **Removing a character from My Characters** leaves every comic's copy alone; those
  copies become "not kept". Deleting a character from a comic leaves My Characters
  alone.
- **Renaming** counts as a change, because the name is part of the character. Folder
  names never change.
- **Library sticker tidying** (sticker-system §11) runs when a comic saves. My
  Characters stores the character as it was when saved from a comic, and each comic
  tidies its own copy. Nothing changes here.
- **My Characters is unreachable** (a disconnected drive, a sync still in progress): the
  gallery section says so, and comics are unaffected.
- **Two Stanley windows write the same character**: the last write wins, per file. A
  character that can't be read at that moment is skipped until the next check, and is
  never treated as deleted.
- **A synced folder creates conflict copies of files**: unrecognised files are ignored
  when reading and removed by the next write of that character.
- **A group lists a character that isn't in My Characters** (removed on another
  computer before a sync, or a hand-edited file): it's left out of the display and
  dropped the next time that group is written.
- **Two groups with the same name** are allowed, since ids tell them apart. Stanley only
  avoids making one itself (§4.5).
- **Saved expressions** travel with the character, because they're in
  `character.json`. Project-level `poses/` don't (§11).

## 9. Where code goes

| Project | What |
|---|---|
| `Stanley.ProjectModel` | `CharacterStore`: reading and writing character folders under any `characters/`-shaped folder, extracted from `ProjectRepository` so a comic and My Characters share one implementation. Also `MyCharacters` (list, load, save and remove characters and groups over a folder), `CharacterGroup` and `CharacterGroupId`, `CharacterFingerprint`, `CharacterShareFile` (zip read/write with the checks in §6.5), and `MyCharactersVersion` on `CharacterDefinition`. All Avalonia-free. |
| `Stanley.Editing` | Pure functions: the state from C, B and L (§6.3), and `Update` with its keep-what's-used rule (§6.4). |
| `Stanley.Editors` | The *Add character* gallery with its group chips and *Add all*, the pane's star, badge and menu items (including *Add to group ›* and *Keep all in My Characters*), the editor bar with the question, the before/after view, and the Everywhere-on-save hook in `CharacterLibraryViewModel`. |
| `Stanley.App` | `AppPaths.MyCharactersDirectory` and the settings entry, the My Characters page in the File view with its group list, drag-and-drop onto the window, `Program.Main` dispatch for a `.chp` argument, and file-type registration. |

## 10. Delivery slices (each a PR, each green on its own)

1. **Storage.**
   - Extract `CharacterStore` with no change in behaviour (the existing ProjectModel
     tests stay green). Add `MyCharacters` with characters and group files, the
     fingerprint and the new field.
   - Tests: the fingerprint is unchanged by a save/load round trip, by a renamed folder
     slug and by CRLF line endings. It changes with the name, a colour, sticker art or a
     look. A no-op comic save still produces an empty diff. The field is omitted when
     null. A group round-trips and writes its member ids sorted, and adding a member is a
     one-line diff. Removing a character from My Characters removes it from its groups.
2. **Keep and add.**
   - The pane's menu items and star, the *Add character* gallery (My Characters, other
     comics), `AppPaths`, the settings entry and the File view page.
   - Tests: keeping writes the folder and sets the version. Adding copies with the same
     ids. Adding an id that's already present selects it. Picking from another comic
     also keeps the character. A recent comic that fails to load is skipped.
3. **Staying in step.**
   - The states, the bar and its question, Everywhere on save, the before/after view
     with *Update* and *Keep as is*, the info bar on open, and the Undo toasts.
   - Tests: every row of the table in §6.3. *Update* is one undo step. *Update* keeps a
     look an issue uses and a sticker a panel override wears. *Keep as is* silences the
     badge until My Characters changes again. Everywhere writes on save and AutoSave,
     and stops on *Stop*.
4. **Groups.**
   - The group list on the File view page (new, rename, delete, drag onto a group),
     *Add to group ›* and *Remove from group*, *Keep all in My Characters*, and the
     gallery's group chips and *Add all*, including *Add all* from another comic.
   - Tests: a character can be in two groups. Deleting a group keeps its characters.
     *Add all* is one undo step and skips characters the comic already has. *Keep all in
     My Characters* makes a group named after the comic, or fills the existing group the
     user picks. *Add all* from another comic keeps the characters in a group named after
     it.
5. **Sharing files.**
   - *Share…* on a character, on a group and on a multi-selection on the File view
     page, *Add from file…*, dropping onto the window, and the import view with
     *Replace*, *Keep both* and *Skip*.
   - Tests: a round trip keeps the ids and the fingerprint. Files with entries outside
     `characters/` and `groups/`, a group listing a character the file lacks, oversized
     files and files with unknown entry types are refused whole. *Keep both* gives new
     ids. Importing a group adds it. Importing it again with a new member adds the member
     and keeps the members the receiver added. After *Keep both*, the group points at the
     new copy.
6. **Double-click.**
   - `Program.Main` currently sends any argument to the CLI. A single argument that is
     an existing `.chp` file opens the GUI with the import view instead, and the result
     lands in My Characters.
   - macOS delivers opened files as an event, not as an argument. Avalonia exposes it
     through its activation API *(unverified: check the Avalonia 12 API)*.
   - Windows registers the file type per user (`HKCU\Software\Classes`, no admin) from
     Velopack's install and uninstall hooks. Velopack doesn't register file types by
     itself *(unverified)*.
   - Linux uses a `.desktop` `MimeType` and a shared-mime-info entry, in whichever
     packages get built (`docs/linux-packaging.md`; none are yet).
   - `.chp` is short, so a few older programs use it too (Ventura Publisher chapter
     files, for one *(unverified)*). Stanley registers as one handler for the user
     rather than taking the extension over. The writer puts `stanley-characters.json`
     first in the zip, so the file can also be recognised by its content, the way
     OpenDocument files are: the Linux shared-mime-info entry matches that name at the
     start of the file, and a `.chp` without it is refused with the plain message from
     §6.5.
   - Dropping onto the window and *Add from file…* work without any of this, which is
     why this slice can wait until the end.
   - Tests: a lone `.chp` argument goes to the GUI import; `init` and other arguments
     still go to the CLI.
7. **CLI, optional, for advanced users.**
   - `stanley characters list`, `export` and `import` (a character or a group), for
     scripting and for people who prefer the terminal.

## 11. Later, and what was dropped

**Later:**
- Groups inside a comic's own Characters pane, for comics with a large cast.
- A creator name and a plain-language credit or licence in share files. CC-BY art needs
  credit. Characters built only from the starter library are CC0 and need none.
- My Poses: project-level poses shared the same way.
- Sharing by link or through a web gallery.
- Dragging a My Characters tile straight onto a panel.
- Watching My Characters for changes while a comic is open. For now it's checked on
  open and when the window gets focus.

**Dropped during the discussion, and why:**
- *A cast is any Stanley project, with a per-file three-way merge.* Too technical, and
  it leaves the user resolving conflicts.
- *Dedicated cast packages in `casts/`, referenced by relative path or by a git tag
  from any repo.* Every noun is jargon. A path outside the repo breaks clones. A tag
  needs git, network access and possibly credentials. Most of the machinery (a
  read-only copy in the comic, replaced as a whole on update) survives underneath as
  §6. Path and tag references could come back as an advanced escape hatch if someone
  asks.
- *Live links, where comics always show My Characters' current version.* Finished
  comics would change on their own.
- *NuGet/npm-style restore, where the comic stores only a reference.* A clone wouldn't
  render until it downloaded the characters.

## 12. Open questions

1. **Several computers:** is Documents (often already synced) plus *Keep My Characters
   in…* enough? *Recommended:* yes for v1.
2. **How long *Everywhere* lasts:** until the comic closes (as written), or one change
   at a time?
3. **Creator credits in share files:** v1 or later? *Recommended:* later (§11).
