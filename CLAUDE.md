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
(the newer XML-free format). All test projects opt into
`Microsoft.Testing.Platform` via `global.json`.

The project/data model (persistence layer), editing operations (validation +
transformation), editor framework (undo/redo + gesture lifecycle), and one
concrete page/panel/bubble editor (Word-style tabbed ribbon + File view, zoom,
snapping, page navigator) all exist. The GUI opens/saves real project folders (the
pages of one issue for now — see "Documents" below). Characters exist as a
**body-only POC** (sliders + a generated flat mannequin, front or side view,
placed on panels — see "Characters (POC, implemented)" below); no stickers,
posing or three-quarter view yet.
`Stanley.App` is the single `stanley` executable: no args opens the Avalonia
GUI, any args dispatch through a CLI (System.CommandLine; currently just
`init`) instead, without touching Avalonia at all — one binary, not a
separate GUI exe plus a separate CLI exe (see "Command-line interface" below
for why).

```
src/Stanley.Editing.Abstractions/ # EditResult only, zero-dependency shared vocabulary
src/Stanley.Editing/              # pure editing functions (bubble/panel operations), Avalonia-free
src/Stanley.EditorFramework/      # undo/redo (EditorHistory), gesture lifecycle (EditorViewModel), Dock.Avalonia-coupled
src/Stanley.Editors/              # concrete editors (PageEditorViewModel, PageEditorView, PageEditorHost)
src/Stanley.ProjectModel/         # project/data model + JSON persistence, no Avalonia/SkiaSharp dependency
src/Stanley.Rendering/            # pure SkiaSharp rendering (bubble/panel path-building, text, drawing)
src/Stanley.App/                  # the `stanley` executable: Avalonia GUI host (window, File view, Documents/) + CLI (Commands/)
tests/Stanley.App.HeadlessTests/   # xunit v3, UI smoke tests
tests/Stanley.App.Tests/           # xunit v3, CLI command unit tests
tests/Stanley.Editing.Tests/       # xunit v3, editing operation unit tests
tests/Stanley.EditorFramework.Tests/ # xunit v3, undo/redo + gesture lifecycle unit tests
tests/Stanley.Editors.Tests/       # xunit v3, concrete editor unit tests
tests/Stanley.ProjectModel.Tests/  # xunit v3, id/serialization/repository unit tests
tests/Stanley.Rendering.Tests/     # xunit v3, rendering unit tests
```

Build/test/run:
```
dotnet build Stanley.slnx
dotnet test --project tests/Stanley.App.HeadlessTests/Stanley.App.HeadlessTests.csproj
dotnet test --project tests/Stanley.App.Tests/Stanley.App.Tests.csproj
dotnet test --project tests/Stanley.Editing.Tests/Stanley.Editing.Tests.csproj
dotnet test --project tests/Stanley.EditorFramework.Tests/Stanley.EditorFramework.Tests.csproj
dotnet test --project tests/Stanley.Editors.Tests/Stanley.Editors.Tests.csproj
dotnet test --project tests/Stanley.ProjectModel.Tests/Stanley.ProjectModel.Tests.csproj
dotnet test --project tests/Stanley.Rendering.Tests/Stanley.Rendering.Tests.csproj
dotnet run --project src/Stanley.App
dotnet run --project src/Stanley.App -- init ./MyComic --title "My Comic"
```
All tests use xunit v3 (4.0.1), Microsoft.NET.Test.Sdk (18.10.1), and
coverlet.collector (10.0.1). No linter is configured yet.

Dependencies: **Avalonia** 12.1.3, **SkiaSharp** 4.152.1, **System.CommandLine**
2.0.12, **CommunityToolkit.Mvvm** 8.4.2, **Dock.Avalonia** / **Dock.Model.Mvvm**
12.1.0.6 (for dockable panes). Environment notes: on a fresh Linux container,
`apt-get install dotnet-sdk-10.0` works when `dot.net`/`builds.dotnet.microsoft.com`
is egress-blocked (the official dotnet-install script host). SkiaSharp needs an
explicit `SkiaSharp.NativeAssets.{Linux,macOS,Win32}` package reference per
platform — the base `SkiaSharp` package alone throws `DllNotFoundException` at
runtime. Avalonia's headless test host needs `.UseSkia()` even though it's not
rendering to a real window, or any `TextBlock` measurement throws
(`Unable to locate 'Avalonia.Platform.IFontManagerImpl'`).

## Speech bubble system

Data model in `Stanley.ProjectModel/Bubbles/` (immutable `record`), rendering
in `Stanley.Rendering`, editing operations in `Stanley.Editing`, wired into
the page editor via `PageEditorViewModel`.

- **`BubbleShape`** (not `BubbleOutline`): an arbitrary closed bezier shape
  (ordered `ShapeAnchor` ring, each with absolute in/out handle points and
  smoothness type). Reuses the anchor-ring math (`AnchorRing` free functions)
  shared with `PanelShape` — no duplication, no shared base type (kept them
  separate to sidestep unnecessary coupling). `BubbleEditing.Resize(bubble, newBounds)`
  rescales anchors affine-style.
- **`Bubble` is a persisted value**, embedded directly in `Panel.Bubbles`
  (like `CharacterInstance`) with id stable only within its panel — nothing
  outside that panel ever references a bubble by id.
- **Tails are independent, not part of the outline.** Each `BubbleTail` has an
  `AttachmentT` (0–1 fraction along the outline) and a free `Target` point.
  `BubbleRenderer` (in `Stanley.Rendering`) unions the outline with every
  tail's own polygon via `SKPath.Op(..., SKPathOp.Union)` — this is why adding
  another tail needs no special case, and why any number of tails works.
- **Style presets** (`BubbleStylePreset`: Speech/Shout/Whisper) are pure
  `bounds -> anchors` generator functions plus a default tail kind and stroke —
  the "ease of use" default path. `BubbleEditing.SetStyle(bubble, style)`
  regenerates the shape from current bounds under the new preset while
  preserving tail attachment/target so they don't jump. The anchor model
  itself is the escape hatch for arbitrary hand-edited shapes later.
- Deferred: text/lettering interactive editing (rendering exists in
  `BubbleTextRenderer`), thought-bubble style (disjoint circle chain — breaks
  the single-polygon-per-tail union model), colour slots, character-bound tail
  targets, NativeAOT publish validation. See the design discussion in this
  repo's history for the full reasoning (bezier outlines, boolean-union tails,
  Avalonia+AOT tradeoffs, AGPL licensing check on the dependency stack).

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
- **`PageTrim` = `PageSize` (width/height) + a bleed margin, kept as two
  types.** Bleed is a print-production choice, not part of a paper size, so
  it isn't baked into presets. `MetricPaperSize`/`MetricPaperSizes` give the
  ISO 216 "A" series (A0–A6) as portrait `PageSize`s — the same
  enum-plus-static-lookup shape as `BubbleStylePreset`/`BubbleStylePresets`.
  **Always metric, project-wide** — no inch-derived defaults or imperial
  preset table anywhere; `stanley init` defaults to A4 with a 3mm bleed
  (a static `PageSize` field in `InitCommand` plus a plain `const` bleed,
  not its own preset table entry, since bleed isn't part of a paper size).
- **Not yet designed**: `sticker.json`'s exact schema beyond what's
  implemented here (the design doc doesn't draw one explicitly), any
  convenience "create new project/character/issue" helpers beyond
  `ProjectRepository.Initialize` and raw `SaveX`/`LoadX`, and NativeAOT
  publish validation (analyzer-clean under `IsAotCompatible`, not yet
  published via a real `PublishAot` executable).

## Editor architecture

Editing pipeline layers, bottom to top:

- **`Stanley.Editing.Abstractions`**: one type (`EditResult<T>`), zero
  dependencies — the shared vocabulary between editing logic and the editor
  framework, kept here so EditorFramework never has to reference Editing.
- **`Stanley.Editing`**: pure editing functions over immutable document
  values (currently `BubbleEditing`, `PanelLayoutEditing`, `PanelBoundaryDrag`,
  `PanelSnapping`, `PanelGutters`, `PanelLayoutPresets`).
  Avalonia-free by design — a future `stanley` subcommand could invoke the
  same logic headlessly, with no recompilation needed.
- **`Stanley.EditorFramework`**: `EditorHistory` (one shared undo/redo stack
  per open project, not per pane, storing closures for before/after states)
  and `EditorViewModel<TDocument>` (gesture lifecycle: `BeginGesture()`
  captures state, `UpdateGesture(result)` applies on every pointer move for
  live preview, `CommitGesture()` records in history, `CancelGesture()` reverts
  to baseline). Allows Avalonia coupling (extends Dock.Avalonia's `Document`
  directly) since undo/redo and pane lifecycle have no lower-level reuse
  requirement. `TDocument` must be immutable (`record` satisfies this).
- **`Stanley.Editors`**: concrete editor implementations (`PageEditorViewModel`
  extends `EditorViewModel<PageDocument>`, `PageEditorView` is the UI, `PageEditorHost`
  builds the demo page + history + layout at startup). Panels in the page editor are
  always axis-aligned rectangles (an arbitrary hand-edited `PanelShape` remains a
  data-model escape hatch, just unreachable through this editor's drag interactions).
  The page editor UI:
  - **Ribbon** (Word-style): one ribbon in the window, above the dock area — not
    inside a pane. Rows: a blue title bar with the quick access toolbar (Save,
    Undo, Redo) and the "<title> - saved / unsaved changes" caption; then the tab
    strip with the window-level **File** button laid over its left end; then the
    active tab's groups (fixed height, so the page never moves). The tabs come from
    the active pane: `EditorWorkspace.ActiveEditor` (EditorFramework; follows the
    dock factory's active/focused dockable) is the ribbon host's content, and a
    `DataTemplate` scoped to that host maps each editor view-model type to its
    ribbon (`PageEditorViewModel` → `PageEditorRibbon`, a `TabControl`). A new
    editor type adds its own tabs the same way. Page editor tabs: Home (tools,
    bubble style, add/edit/delete), Insert (panel, speech/shout/whisper bubble),
    Layout (inline preset gallery, margin/gutter, snap, split), View (fit/actual
    size/zoom, margin guides), plus contextual **Panel** (blue) and **Bubble**
    (orange) tabs visible only for that selection (`IsPanelContext` /
    `IsBubbleContext`); like Word they aren't forced open, and if the selected one
    disappears the ribbon falls back to Home. The ribbon only talks to its pane
    through the view model: commands plus events for view-only work
    (`ViewportRequested` for zoom, `TextEditRequested` for the inline text editor).
    Ribbon buttons are non-focusable so shortcuts keep reaching the page. Shared
    look and icon geometries: `RibbonStyles.axaml`, included from `App.axaml`.
    Group labels are pinned to the bottom (`DockPanel.group`).
  - **Page navigator** (`PageNavigatorViewModel` + `PageNavigatorView`): a dock
    *tool* pane on the left (`EditorWorkspace(history, panes, leftTools)`), not an
    editor, so focusing it never changes `ActiveEditor` and the ribbon stays put.
    Live thumbnails, three to a row with the page's position underneath
    (`PageThumbnail`, drawing through `PageRenderer` and redrawing on the page's
    `Working`/`Folio` changes), click to show a page, drag to reorder (drop
    position is the gap nearest the pointer in reading order), right-click /
    Delete / Ctrl+D / Ctrl+Left/Right for page actions, "New page" at the bottom. Each page has its own `PageEditorViewModel`, all sharing
    the one `EditorHistory`; showing a page swaps which editor is in the editor
    area (`EditorWorkspace.SwitchTo` — one page at a time, not a row of tabs).
    Page add/duplicate/delete/move are history entries too. History entries carry
    their source (`EditorHistory.Push(..., source)` / `Restored`), so undoing an
    edit made on another page switches to that page first.
  - **Page numbers** (folios): an issue-level `PageNumbering` (ProjectModel:
    position None / BottomCenter / BottomOuter / TopOuter, `StartAt`,
    `NumberFirstPage` — off by default since covers aren't numbered), stored on
    `Issue.PageNumbering` (absent when off, so older files read unchanged). The
    navigator owns it (`IPageNumberingHost`, undoable) and sets each page
    editor's `Folio` (`PageFolios.For`: odd numbers are right-hand pages, so
    "outer" flips sides). Changed from the Insert tab's "Page numbers" group via
    the shown page editor's `PageNumberOption`/`PageNumberStart`/`NumberFirstPage`
    (they write through to the host, so they apply to every page). Drawn by
    `PageRenderer.DrawFolio` in the margin — on the canvas, thumbnails and
    exports alike.
  - **Pane** (`PageEditorView`): just the canvas, inline text editor, and a status
    bar with a one-line hint for the current tool/selection plus the last
    validation error. Right-click gives a context menu for the thing under the
    pointer.
  - **Zoom**: the document is in millimetres; `PageCanvasControl` owns the mm→screen
    transform. 100% = the page at its printed size on a 96 DPI screen
    (`ActualSizeZoom`); starts in fit-page mode (re-fits on resize until the user
    zooms/pans). Ctrl+scroll zooms at the cursor, scroll/Space-drag/middle-drag pans.
    `PageCanvasDrawOperation` draws artwork in page space (mm values:
    `FontSizeMm`, `BubbleStrokeMm`, …) and handles/guides in screen space so they
    stay grabbable at any zoom.
  - **Bubbles belong to their panel**: every bubble edit goes through
    `BubbleEditing.KeepInside` (slide/shrink into the panel, clamp tail targets),
    rendering clips bubbles to their panel, and panel resize/move/split/layout carry
    bubbles along (`BubbleEditing.Refit`, split sends each bubble to the half its
    centre is in). Double-click in a panel (or the Bubble tool, or "Add bubble")
    creates a bubble with a tail already aimed into free space, and opens an inline
    text editor over it (Enter = done, Shift+Enter = newline, Esc = cancel).
  - **Snapping** (`PanelSnapping`, `PanelGrid` = margin + gutter, default 10mm/4mm):
    panel edges snap to the page margin, one gutter from neighbours, and into line
    with neighbours' edges; Alt disables it for one drag. Gutter drags
    (`PanelGutters.FindAt`) move the whole aligned run of panels on both sides,
    keeping the gutter width (`PanelBoundaryDrag.Gap`).
  - Gesture `Update*` methods compute from `Committed` (the gesture baseline), never
    `Working`, so a drag is a pure function of the current pointer position.
- **`Stanley.App`**: `MainWindow` + `MainWindowViewModel` own the document
  lifecycle (below) and swap a fresh `EditorWorkspace` (history + dock layout +
  active pane, from `PageEditorHost.CreateWorkspace(ComicProject)`) into the
  ribbon bar and Dock.Avalonia `DockControl` whenever a comic is created/opened.

### Documents (File view, open/save)

Modelled on Word. `ComicProject` (Stanley.Editors) is "the document": a project
folder on disk (or untitled, `Location == null`) plus the pages the editor edits —
all pages of the first issue, with a blank one created on the fly for a project with
none (e.g. straight from `stanley init`). `Save(pages)` (from
`PageNavigatorViewModel.Snapshot()`) writes the manifest title, the issue's page
order, every page and panel, and deletes the folders/files of pages and panels
removed since the last save (`ProjectRepository.DeletePage` / `DeletePanel`);
nothing else in the folder is touched. A page's label and trim override survive a
save. Multi-issue navigation isn't implemented yet.
`SaveAs` copies the whole project folder (minus `.git`) to the new location first,
and never writes into a non-empty folder — it uses a subfolder named after the title
instead. An untitled comic takes its folder's name as title on first save. Export
(all pages as one PDF at trim size; the current page as a 300 dpi PNG) goes through
`PageRenderer` (Stanley.Rendering), the same code the canvas and thumbnails draw
with.

`MainWindowViewModel` (Stanley.App) runs New / Open / Save / Save As / Close /
Export and the File ("backstage") view, `Backstage.axaml`: full-window, blue command
rail, pages New (paper size + layout tiles), Open (Browse + Recent), Info (editable
title, location, size), Save As, Export. With no comic open the window *is* the File
view. Dirty state is `EditorHistory.IsDirty` (undo-stack top vs. the top at
`MarkSaved()`, so undoing back to the saved state is clean again) or an unsaved title
edit; New/Open/Close/window-close ask Save / Don't Save / Cancel first. Errors show in
the File view, not modals. OS dialogs sit behind `IFileDialogs`
(`AvaloniaFileDialogs` for real; tests script a fake). Recent comics:
`RecentProjects`, a plain text file under the user's app-data folder.

**AutoSave, crash recovery, logging** (all under `AppPaths.DataDirectory` —
`<AppData>/Stanley`, overridable with `STANLEY_DATA_DIR`, which the headless tests
point at a temp folder):
- *AutoSave*: the title-bar switch left of Save (`AutoSaveEnabled`), a persisted
  preference (`AppSettings`, `settings.txt`, on by default) that only applies once
  the comic has a folder; switching it on for an untitled comic runs Save As first.
  Saves `AutoSaveDelay` (2 s) after the last change (debounced), and with it on,
  New/Open/Close/window-close save instead of prompting. Failures are logged and
  shown in the title bar, never as a prompt.
- *Crash recovery* (`RecoveryStore`, `Recovery/<session>/`): each session holds an
  exclusively-locked `session.lock`; while there are unsaved changes a snapshot
  (`ComicProject.WriteCopy` — same issue/page ids — written beside the old one then
  swapped in) plus `info.txt` is kept at most `RecoveryDelay` (5 s) stale. Saving or
  deliberately discarding clears it; a clean exit (`MainWindowViewModel.EndSession`,
  from `MainWindow.OnClosed`) deletes the session folder. On start, session folders
  whose lock can be taken belong to dead processes: their snapshots appear under
  File › Open › *Recovered* (the File view opens there), with Open
  (`ComicProject.OpenRecovered`: the snapshot's pages, back at the original folder,
  marked unsaved) or Discard. An unhandled UI-thread exception writes one last
  snapshot before the process goes down (`App`).
- *Dark mode*: File › Options › Appearance (System / Light / Dark; `AppSettings.Theme`,
  System by default) sets `Application.RequestedThemeVariant` via `ThemeSwitcher`.
  Stanley's own colours are `Stanley*Brush` keys in `RibbonStyles.axaml`'s
  `ThemeDictionaries` — use `{DynamicResource ...}` for them, never hex literals that only
  suit one theme. The page (paper), thumbnails and the inline text editor
  (`ThemeVariantScope` forced Light) stay white; only the canvas pasteboard darkens
  (`PageCanvasScene.DarkChrome`).
- *Logging* (`Diagnostics/AppLog`): `Logs/stanley-yyyy-MM-dd.log`, append-and-close
  per line (nothing lost in a crash), pruned after 14 days, a no-op until
  `Initialize` (so the CLI and tests don't log). Records startup environment,
  every open/save/autosave/export/recovery with failures' exceptions, and all
  unhandled exceptions (`Program`: AppDomain + unobserved tasks; `App`: UI thread).
  Timers go through `IDelayScheduler` (`DispatcherDelayScheduler` for real; tests
  use a manual one). Shortcuts:
Ctrl+N new, Ctrl+O open, Ctrl+S save, Ctrl+Shift+S / F12 save as, Alt+F File view,
Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo/redo, Esc back out of the File view. `Ctrl+Z`/`Ctrl+Shift+Z`
  bound globally to history's undo/redo commands.

The separation (editing Avalonia-free, undo/redo Avalonia-coupled) means a
future editor or subcommand can reach `BubbleEditing`, `PanelLayoutEditing`
etc. without pulling in Avalonia dependencies. `EditorHistory` has no such
reuse requirement, so it's fine for it to couple to Avalonia/MVVM.

## Command-line interface (implemented)

`stanley` is **one executable** with both a GUI and a CLI, not two separate
binaries — `Stanley.App`'s `Program.Main` checks `args` before doing
anything else: no args builds and starts the Avalonia app exactly as
before; any args parse and invoke a CLI command instead, never touching
Avalonia/the windowing system (so CLI use works headlessly — CI, no display
server). This mirrors how e.g. Blender or VS Code ship a single binary that
dispatches on args rather than a separate GUI product and CLI product —
appropriate here since `stanley init` and the editor are the same tool, not
different install/versioning lifecycles the way `docker`/Docker Desktop or
`kubectl`/a dashboard are. (An earlier pass put the CLI in its own
`Stanley.Cli` project/exe; that was wrong and was folded back in here.)

Parsing is **System.CommandLine 2.0** (GA, not a beta) — chosen because
it's Microsoft's own, AOT/trim-clean (0 analyzer warnings under this repo's
`IsAotCompatible`), and MIT-licensed (AGPL-compatible).

- One `Command` per subcommand, each in its own file under
  `Stanley.App/Commands/` (`InitCommand` so far), wired into a `RootCommand`
  in `Program.cs`. `[assembly: InternalsVisibleTo("Stanley.App.Tests")]`
  (`Stanley.App/AssemblyInfo.cs`) lets `Stanley.App.Tests` call a command's
  `Build()` and `.Parse(args).Invoke()` directly instead of shelling out to
  the built exe.
- `stanley init <path>`: creates a new project via
  `ProjectRepository.Initialize`. Defaults the title to the target
  directory's name and the page trim to A4 (`MetricPaperSizes.Size(A4)`)
  with a 3mm bleed so it works with zero flags, per the project's "ease of
  use" priority; `--title`/`--page-*-mm` override. Always metric, no
  inch-derived defaults anywhere. `--force` is required to overwrite a
  directory that already has a `stanley.json` (checked via
  `ProjectRepository.IsInitialized`).
- Not yet implemented: any subcommand beyond `init` (add/list
  character/issue/page/panel, etc.), opening a project from the GUI via a
  CLI arg, and NativeAOT publish validation (same deferral noted for the
  other two projects).

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

### Characters (POC, implemented)

Designed in [`docs/character-authoring-poc.md`](docs/character-authoring-poc.md);
body and placement only.

- **Body = numbers**: `BodyShape` (ProjectModel/Characters) — `Height` (relative:
  1.0 = average adult, never cm), `Build` (shown as "Weight"), `Muscle`,
  `HeadsTall`, `Frame` (shoulders ↔ hips); `BodyPresets` (Toddler … Chibi).
  Stored as `CharacterDefinition.Body`; `CharacterDefinition.Skeleton` is now a
  sparse joint *override* on the generated rest layout (empty from the UI).
  `CharacterDefinition.Create(name)` makes a default one; `Skin` reads the `skin`
  colour slot.
- **`BodyRig.Build(body, angle, overrides)`**: pure math → `BodyFigure` (VRM rest
  layout + torso outline, limb capsules, head/hand/foot ellipses) in *figure space*
  (unit = relative height, y down, origin = ground between the feet; head top at
  `-Height`), for `ViewAngle.Front` or `Profile` (faces +x, has a nose; the near
  arm/hand/foot are `NearLimbs`/`NearBlobs`, drawn as a second outlined layer).
  Heights depend only on `Height`/`HeadsTall`, identical in both views;
  `ThreeQuarter` falls back to front. Skeleton overrides are per view.
- **Rendering**: `ICharacterRenderer` / `CharacterRenderers.Default` =
  `MannequinRenderer` (unions all shapes, fills skin, inks outline; caches the
  figure path per definition). `PageRenderer.Draw/DrawPanels/Export*` take an
  optional character dictionary and draw background → characters → bubbles inside
  the panel clip; a missing character draws a dashed placeholder.
- **Placement**: `CharacterInstance.Placement` = `CharacterPlacement(Ground,
  UnitHeightMm, Mirrored)` (page mm; `ToPage` maps figure space); the view is the
  instance's `Pose.ViewAngle` (page: Character tab Front/Side, S/F keys; mirrored
  side views face left). One scale per
  panel by default: `CharacterPlacementEditing` (Stanley.Editing) places new ones
  at the panel's scale/floor, resizes "together" (everyone sharing the scale) or
  alone, keeps them reachable (may hang out of the panel — cropping is fine), and
  `PanelLayoutEditing` carries them through panel resize/move/split.
- **Editors**: `CharacterLibraryViewModel` (the **Characters** tool pane, tab next
  to Pages; also the `ICharacterCatalog` page editors draw from, live incl.
  mid-drag) owns one `CharacterEditorViewModel` per character (all in the shared
  history; showing one swaps it into the editor area, `ReturnToPage`/Close swaps
  back; undoing a body edit re-opens that character). Placed characters can't be
  deleted. `CharacterEditorRibbon` = Body tab (presets, sliders — one drag = one
  undo step via `BeginSliderDrag`/`EndSliderDrag` — skin, name, line-up, Close).
  Page editor: `SelectedCharacterIndex`, contextual green **Character** tab,
  Insert › Characters gallery + New character, drag from the pane onto a panel
  (`CharacterDrag.Format`), double-click opens the body editor.
  `PageEditorHost.CreateWorkspace` returns an `EditorSession(Workspace, Navigator,
  Characters)`.
- **Persistence**: `ProjectRepository.ListCharacters()` (scans `characters/`, no
  index file) / `DeleteCharacter`; `ComicProject.Characters`, and
  `Save`/`SaveAs`/`WriteCopy` take the characters (null = leave disk alone) and
  prune deleted ones.

## Builds, versioning & releases

**Versions and release notes come from the issues each PR closes**, not from
commit messages (write those however reads best). Every PR should link the
issue(s) it resolves — "Closes #123" in the description, or the Development box.
The closed issues' labels (or GitHub issue type) decide the bump:
`breaking` → major (minor while on 0.x); `enhancement`/`feature` or type Feature
→ minor; anything else (`bug`, unlabelled, a PR closing no issue) → patch. A PR
closing no issue is listed under "Other changes" by its own title. Logic:
`.github/scripts/release-notes.js` (run through `actions/github-script`).

**Branches**: feature PRs target **`develop`** (the default branch, so "Closes #n"
links work); releasing is merging `develop` into **`main`**. Hotfix PRs straight
into `main` are counted too. PRs between the two branches (develop → main, the
automatic back-merge) are plumbing and never appear in release notes.

`.github/workflows/ci.yml`:
- **Pull requests to `develop` or `main`**: Release build of `Stanley.slnx` plus
  every test project (`dotnet test --project … -c Release --no-build`), and a
  notice (never a failure) naming the issues the PR closes and the bump that
  implies. Nothing is published.
- **Nightly** (daily 02:00 UTC schedule; at most once a day, and skipped when
  `develop` hasn't moved since the last nightly; a manual run always builds):
  builds and tests `develop`, then a self-contained `dotnet publish` of
  `src/Stanley.App` for linux-x64, win-x64 and osx-arm64
  (`stanley-<version>-<rid>.tar.gz`, `.zip` on Windows; ~44 MB on Linux), kept as
  workflow artifacts for 30 days and replacing the rolling pre-release tagged
  `nightly` (**Releases › nightly**).
- **Push to `main`**: build + test, and keep **one draft GitHub Release `vX.Y.Z`**
  up to date: the next version and notes (New features / Bug fixes / Breaking
  changes / Other changes, one line per closed issue) for every change PR merged
  since the last published release that `main` now contains.
- **Releasing = pressing Publish on the draft** (notes can be edited first). That
  creates the `vX.Y.Z` tag; the `release: published` run builds and tests that
  tag, attaches the builds, and merges the tag back into `develop` (if that fails
  — protected branch, conflict — it warns; merge `main` into `develop` by hand).
  The repo is private, so only collaborators can download releases or nightlies.
- **Version numbers are never written by hand.** MinVer (`Stanley.App.csproj`)
  derives the binaries' version from git tags: a `vX.Y.Z` commit is `X.Y.Z`,
  anything after it is `X.Y.(Z+1)-alpha.0.<commits since>`, before the first tag
  `0.1.0-alpha.0.<n>`. Nightlies see release tags only because each release is
  merged back into `develop`. `stanley --version` shows it plus the commit SHA. CI checks
  out with `fetch-depth: 0` so MinVer can see the tags. The first release is 0.1.0.
- Stay on 0.x until the project file format is stable. A project-file format
  version (in `stanley.json`), separate from the app version, is still to do.

## Licensing constraint

The project is **AGPL-3.0**. Check the licence of every dependency before adding
it. Some character-animation runtimes need proprietary or per-user licences (for
example the Spine runtimes and the Live2D Cubism SDK). Prefer open formats
(glTF, VRM, DragonBones, SVG) and libraries compatible with the AGPL.
