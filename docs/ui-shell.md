# UI shell — the window around the editors

**Status: the ribbon shell is implemented (and the only one).** A Figma-style
alternative ("canvas layout", a setting that applies after a restart) was assessed
but not built yet: §6.

Scope: the window's chrome (the title bar, the ribbon, the dock layout, the File
view, toasts) and how it reaches the editors. Not what the editors draw or how
they edit. See `CLAUDE.md` for the project's overall priorities.

## 1. How the window is put together

`MainWindow.axaml`, top to bottom in a `DockPanel`:

- **Title bar** (`Border.titleBar`, 32px): AutoSave switch, the quick access
  toolbar (Save / Undo / Redo), the comic's caption with the issue switcher and
  "New issue", and the message line on the right.
- **Ribbon bar** (`RibbonBar`, fixed `Height="150"` so a contextual tab appearing
  never moves the page): a `ContentControl` (`RibbonHost`) bound to
  `Workspace.ActiveEditor`. Its own `DataTemplates` map `PageEditorViewModel` →
  `PageEditorRibbon` and `CharacterEditorViewModel` → `CharacterEditorRibbon`; inside
  the host these win over the window's templates for the same types. The File
  button is laid over the left end of the tab strip, which is why both ribbons'
  tab strips start with a 64px margin.
- **Dock area** (`EditorDock`, a Dock.Avalonia `DockControl`): `Factory` and
  `Layout` come from the open comic's `EditorWorkspace` and are swapped in
  `MainWindow.ShowWorkspace` whenever `MainWindowViewModel.Workspace` changes.

Laid over all of that in a `Grid`: the export toast (`ExportNotice`) and the File
view (`Backstage`, shown while `IsBackstageOpen`).

The window's `DataTemplates` map every pane's view model to its view
(`PageEditorView`, `CharacterEditorView`, `PageNavigatorView`,
`CharacterLibraryView`, `LayersView`). The ribbon follows
`EditorWorkspace.ActiveEditor`, which only ever changes to an `IEditorPane`:
activating a tool pane (Pages, Characters, Layers) leaves the ribbon on the editor
being worked in.

## 2. Nothing below the shell knows about the ribbon

- `PageEditorView`, `PageEditorViewModel`, `PageCanvasControl` and
  `MainWindowViewModel` hold no reference to either ribbon. Everything a ribbon
  does goes through the editor's view model (74 distinct commands are bound across
  the ribbon tabs and `FontGroup`, plus the `Is…` flags and value properties).
- **Keyboard shortcuts don't live on the ribbon buttons.** Single-key tools (V, P,
  B, H, D, F, L, R, E, T), Ctrl+C/X/V/D/G, zoom, arrows and Delete are handled in
  `PageCanvasControl.OnKeyDown`; Ctrl+S/Z/Y/N/O, Ctrl+Shift+S, F12 and Alt+F are
  `KeyBindings` on `MainWindow`. A button's `local:Shortcut.Keys` is only the label
  its KeyTip shows while Ctrl is held (`Shortcut.RevealWhileCtrlHeld`, drawn in the
  adorner layer for any control in the window). So a shell without the ribbon loses
  no shortcut, and any button anywhere gets a KeyTip by setting that property.
- **Contextual tabs** are driven by the view model:
  `PageEditorViewModel.Is{Panel,Character,Bubble,Shape,Text,Picture,SpeedLines}Context`
  and `CharacterEditorViewModel.HasSelectedSticker`. The ribbons only add "if the
  contextual tab you're on goes away, fall back to Home (Look)".

## 3. The ribbon tabs: what's code and what's XAML

Each tab is its own `UserControl` (`Page/Ribbon/*RibbonTab`,
`Characters/Ribbon/*RibbonTab`) with the editor's view model as `x:DataType`:
about 1,800 lines of page-tab XAML and 550 of character-tab XAML. Most
code-behinds are 12-line stubs. The ones that aren't:

- `LayoutRibbonTab`: builds the layout-preset gallery's buttons in code, and keeps
  the margin, gutter and page-number `NumericUpDown`s and the view model in step
  both ways (undo can change them underneath).
- `InsertRibbonTab`: closes the title-page and My Assets gallery flyouts behind a
  pick (posted - a closed flyout's buttons have lost their `DataContext`).
- `BodyRibbonTab`, `StickerRibbonTab`: `SliderDrag.Wire` (a slider drag is one undo
  step) and Enter/Escape on the name and print-text boxes.
- **`CharacterEditorRibbon` handles view-model events that need a window**:
  `TileImportRequested`, `ArtImportRequested` and `SvgEditorConfigurationRequested`
  open file pickers / dialogs, and `TextPrintWorn` switches to the Sticker tab and
  focuses its text box. They're plain events: any other host of the character
  editor's controls must subscribe too, or Import… / Custom… silently do nothing.

The ribbon look comes from classes in `RibbonStyles.axaml` (included app-wide from
`App.axaml`): `StackPanel.tabContent` is horizontal and 92px high, a
`DockPanel.group` docks its `TextBlock.groupLabel` to the bottom, a
`Rectangle.divider` is 1px wide - all set by *style*, so a scoping class can
override them. But about 56 inner `StackPanel`s say `Orientation="Horizontal"`
locally (a local value beats a style), and some sizes are fixed (`ComboBox.fontBox`
170, `Slider.body` 130, `SplitButton.colorMenu` min 132, `DropDownButton.slotGallery`
84×64, the layout gallery 60 high). Restyling alone re-stacks the groups but can't
fully reflow what's inside them.

## 4. The dock layout

`EditorDockHost.CreateLayout(panes, leftTools, rightTools)` builds one row
(`MainRow`): a tabbed `ToolDock` "LeftTools", a splitter, the `DocumentDock`
"Editors", a splitter, a `ToolDock` "RightTools" (min 160 wide). Side docks start at
0.15 of the width. `PageEditorHost.CreateWorkspace` hard-codes left = Pages and
Characters (tabbed), right = Layers.

`EditorWorkspace.SetRightToolsVisible` hides the right dock by removing it and
re-adding it later with the proportions saved at hiding time (otherwise Dock
re-normalises them and the pane creeps narrower on each toggle). That's what
View › Panes › Layers drives, through `LayersPaneMemory` (↔
`AppSettings.ShowLayers`) and `PageEditorViewModel.LayersHost`. It assumes "the
right-hand tools" *are* the Layers pane.

## 5. Settings and tests

- `AppSettings` is a `key=value` file; unknown keys survive, an enum setting reads
  with `Enum.TryParse` + `Enum.IsDefined` and falls back to a default (`Theme` is the
  pattern). File › Options is in `Backstage.axaml`: radio buttons bound to
  `Is…Theme` flags on `MainWindowViewModel`.
- The theme is applied live (`ThemeSwitcher`). `App.axaml` loads `FluentTheme`,
  `DockFluentTheme`, the ColorPicker theme and `RibbonStyles.axaml`.
- Headless UI tests build `new MainWindow(viewModel?)` and reach ribbon controls
  through `window.RibbonBarControl` → `PageEditorRibbon` / `CharacterEditorRibbon`
  → `FindControl<T>(name)` or the `TabControl` (about 76 references across the
  headless tests). Another shell needs its own test hooks; with the ribbon still
  the default, the existing tests don't change.

## 6. A Figma-style alternative layout (assessed, not built)

Asked for: an alternative, Figma-inspired layout, chosen in settings; needing a
restart to switch is acceptable.

### 6.1 What it would be

- A thin top bar (about 40px): a main menu button (the File view's pages, plus
  Edit / View items), the tools (Select, Panel, Bubble, Text, shapes ▾, Freeform,
  Speed lines, Pan, Insert ▾), the caption and issue switcher in the middle, zoom ▾
  on the right.
- Left sidebar: Pages above Layers (split, not tabbed), with an Assets tab
  (Characters, My Assets) beside Layers.
- Right sidebar, fixed width (so a selection change never resizes the page, as the
  ribbon's fixed height does today): an **inspector** - the selection's properties
  in sections; page settings (the Layout tab) with nothing selected; the pen's fill
  and outline while a drawing tool is on (what Home › Shape Fill/Outline does now).
- The editor panes keep their status bars. Compact density
  (`FluentTheme.DensityStyle`), which can be chosen at startup since switching
  needs a restart anyway (check Avalonia 12 still has it; otherwise compact styles
  of Stanley's own under the shell's class).

The ribbon stays the default: priority 1 in `CLAUDE.md` is the flat learning
curve, and this is an alternative for people coming from Figma-like tools, not a
replacement.

### 6.2 Slices

1. **Setting and shell switch.** `AppSettings.InterfaceLayout` (`Ribbon` default,
   `Canvas`), radio buttons on File › Options with "applies the next time Stanley
   starts". Split `MainWindow`'s body into a `RibbonShell` and a `CanvasShell`
   bound to the same `MainWindowViewModel`, picked once in the constructor; title
   bar pieces, the toast and the File view are shared. Restart-only means no live
   swapping of the dock layout, open flyouts or focus. The choice must be
   injectable for headless tests.
2. **Dock profile.** Give `EditorDockHost.CreateLayout` a description of which
   tools go where and whether a column is tabbed or split; generalise
   `SetRightToolsVisible`/`LayersPaneMemory` to "the Layers pane, wherever it is".
   `PageEditorHost.CreateWorkspace` passes the profile.
3. **Top bar and main menu.** Everything it needs is already bound by the Home,
   Insert and View tabs (`IsSelectTool`..., `ZoomText`, `FitPageCommand`...); the
   Insert flyouts (title page gallery, My Assets) can be reused as they are.
4. **Inspector, first cut: re-stacked ribbon tabs.** Host the existing tab controls
   in the right sidebar, under a scoping class that turns `tabContent` vertical with
   auto height, group labels into section headers and dividers into rules. Every
   feature reachable on day one; looks like a ribbon turned on its side. Move
   `CharacterEditorRibbon`'s event handlers (§3) into a helper both hosts attach.
5. **Inspector, proper.** Figma-like sections with compact rows. Extract the ribbon
   groups into shared group controls first (`FontGroup` is the precedent: one
   control used by the Bubble and Text tabs) so both shells compose the same
   controls and differ only in layout classes. Numeric X / Y / W / H / rotation
   rows, if wanted, are new view-model properties that go through
   `EditorViewModel.Apply` (one undo step) and the existing editing functions
   behind the drags.
6. **Tests and a parity guard.** Headless smoke tests for the canvas shell, and a
   test that compares the commands reachable in each shell, so a feature added to
   one without the other fails CI.

### 6.3 How hard

Moderate, and most of it is XAML. In its favour: the chrome is already a
window-level layer bound to `ActiveEditor` (§1); nothing under it knows about the
ribbon (§2); keyboard handling doesn't depend on it; tabs are nearly pure bindings
(§3); settings and Options already have the pattern; and requiring a restart
removes the hardest part of switching. Rough sizes: slices 1–3 small to medium,
slice 4 small, slice 5 the bulk (about 2,300 lines of tab XAML to lay out again),
slice 6 small.

The lasting cost is two UIs: every later feature needs a home in both. Shared
group controls (slice 5) and the parity test (slice 6) are what keep that cheap;
without them the canvas layout will drift behind the ribbon.
