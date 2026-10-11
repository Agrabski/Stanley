# UI shell — the window around the editors

**Status: implemented - two layouts.** The ribbon layout (Word-style, the default)
and the canvas layout (Figma-style: a top bar, Pages and Layers on the left, an
inspector on the right), picked in File › Options › Layout and applied at startup:
§6.

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
- **View-model events that need a window**: `TileImportRequested`,
  `ArtImportRequested` and `SvgEditorConfigurationRequested` open file pickers / dialogs;
  `Characters/CharacterEditorDialogs` handles them, and every host of the character
  editor's controls `Attach`es it to its view model as its `DataContext` changes (the
  ribbon and `CharacterInspector` both do). They're plain events: a host that forgets
  leaves Import… / Custom… silently doing nothing. `TextPrintWorn` is each host's own: the
  ribbon switches to the Sticker tab and focuses its text box, the inspector scrolls the
  Sticker section's text box into view and focuses it.

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

In the canvas layout (`SidePaneArrangement.Stacked`) the same docks sit in one
vertical `ProportionalDock` "SideColumn" left of the editors: LeftTools on top,
RightTools (Layers, min 120 high) below; the right of the window is the inspector,
outside the dock area.

`EditorWorkspace.SetRightToolsVisible` hides the right dock by removing it and
re-adding it later with the proportions saved at hiding time (otherwise Dock
re-normalises them and the pane creeps narrower on each toggle). That's what
View › Panes › Layers drives, through `LayersPaneMemory` (↔
`AppSettings.ShowLayers`) and `PageEditorViewModel.LayersHost`. "The right-hand
tools" *are* the Layers pane, wherever they sit: the workspace toggles them in
whichever dock holds them (the main row, or the side column when stacked).

## 5. Settings and tests

- `AppSettings` is a `key=value` file; unknown keys survive, an enum setting reads
  with `Enum.TryParse` + `Enum.IsDefined` and falls back to a default (`Theme` is the
  pattern). File › Options is in `Backstage.axaml`: radio buttons bound to
  `Is…Theme` flags on `MainWindowViewModel`.
- The theme is applied live (`ThemeSwitcher`). `App.axaml` loads `FluentTheme`,
  `DockFluentTheme`, the ColorPicker theme and `RibbonStyles.axaml`.
- Headless UI tests build `new MainWindow(viewModel?)` and reach ribbon controls
  through `window.RibbonBarControl` → `PageEditorRibbon` / `CharacterEditorRibbon`
  → `FindControl<T>(name)` or the `TabControl`. The canvas layout's tests go
  through `CanvasLayoutHarness` (§6.6).

## 6. The canvas layout

An alternative to the ribbon for people coming from Figma-like tools. The ribbon stays
the default: priority 1 in `CLAUDE.md` is the flat learning curve, and this is an
option, not a replacement.

### 6.1 The setting

`AppSettings.InterfaceLayout` (`Ribbon` | `Canvas`; unknown values read as `Ribbon`) -
an application-level preference in `settings.txt`, not part of any comic. File ›
Options › Layout sets it through `MainWindowViewModel.InterfaceLayout` /
`IsRibbonLayout` / `IsCanvasLayout`. **It's read once, at startup**:
`MainWindowViewModel.ActiveInterfaceLayout` (and `IsCanvasActive`) is what this window
was built with and never changes; `IsLayoutRestartPending` says the two differ, and
Options shows "applies the next time Stanley starts" with **Restart now**
(`RestartCommand` → `RestartRequested` → `MainWindow` closes as usual - asking about
unsaved changes, or saving with AutoSave on - and `OnClosed`, after `EndSession`, calls
`AppRestart.StartNewInstance`: the AppImage via `APPIMAGE`, else `Environment.ProcessPath`,
re-adding the dll under `dotnet`). Restart-only keeps it simple: nothing ever swaps the
dock layout, open flyouts or focus while the app runs.

### 6.2 The window

`MainWindow.UseCanvasLayout` (in the constructor, before the window shows) takes
`TitleBar` and `RibbonBar` out of `ShellDock` and puts in:

- `Canvas/CanvasTopBar` (Stanley.App, 44px, `Border.canvasBar`): the main menu
  (`MainMenuButton`'s `MenuFlyout`: New, Open, Save, Save As, Export ▸, Edit ▸, View ▸,
  Info, My Assets, Options, Close - the File view's pages through `OpenBackstageCommand`
  with a `BackstagePage`; Edit and View act on `MainWindowViewModel.Editor`, the page being
  edited, and are off while a character editor is active - `CanvasTopBar.IsPageEditor` on
  `Workspace.ActiveEditor` - since that page is hidden behind it), the active editor's
  tools (`ToolbarHost`: `PageToolbar` / `CharacterToolbar`), the caption with the issue
  switcher in the middle, and undo/redo, zoom (`ZoomHost`: `PageZoomControl`, the % as a
  menu of zoom in/out, fit, actual size), AutoSave and Save on the right. `PageToolbar` is
  the Home tab's tools and the Insert tab: Select, Pan; Panel, Bubble and Text each with a
  narrow chevron menu that inserts at once (thought cloud; speech/shout/whisper/thought;
  caption/text/sound effect); one Shapes button showing the shape tool in hand (five radio
  buttons on the `Is…Tool` flags, only the current one shown) with a chevron menu of all
  five; Picture; Speed lines; and Insert ▾ (characters, background, title page, My Assets).
  It listens to its view model only while on screen - a new one is built each time a page
  comes back after a character, and the view model would otherwise keep every old one alive. Both hosts follow `Workspace.ActiveEditor` like the
  ribbon host, with a `DataTemplate` per editor type (an empty `Panel` where a type has
  nothing - a `ContentControl` with no template would show the type's name).
- `Canvas/InspectorColumn` (Stanley.App, `Border.inspector`, a fixed 288 wide so a change
  of selection never resizes the page): `PageInspector` / `CharacterInspector`, also by
  `Workspace.ActiveEditor`.

The dock area between them is built with `SidePaneArrangement.Stacked` (§4), passed by
`MainWindowViewModel.Load` → `PageEditorHost.CreateWorkspace`. Window `KeyBindings`, the
File view (Alt+F, the main menu) and the export toast are shared. In the real app (not
tests - it's app-wide) `App` also sets Fluent's `DensityStyle.Compact` at startup for the
canvas layout.

### 6.3 The inspector

`Inspector/PageInspector` stacks one control per kind of thing, each its own
`UserControl` under `Inspector/Page/` with the page's `PageEditorViewModel` as
`DataContext`: `ArrangeSection` (any selection: clipboard, group, delete),
`PanelSection`, `CharacterSection`, `BubbleSection`, `TextSection`, `ShapeSection`,
`PictureSection`, `SpeedLinesSection`, `PenSection` (a drawing tool in hand: what the
next shape gets) and `PageSection` (nothing selected - `PageEditorViewModel.IsPageContext`:
layout presets, spacing, page numbers, title page, guides). **Delete is the Arrange row's
alone**, for whatever is selected (its tooltip follows the kind, in the ribbon tab's words);
each kind's own section keeps only its "Order" (To front / To back), whose meaning differs by
kind. `LetteringRows` is the ribbon's `FontGroup` laid out for the column, shared by the
Bubble and Text sections; both also show for their tool in hand ("New bubble", "New text")
with what a thing that doesn't exist yet can have. **Each section owns its own
visibility** (its root's `IsVisible`, from the same `Is…Context` flags the ribbon's
contextual tabs use, sometimes widened - e.g. the Bubble section also shows for the Bubble
tool's next bubble); `PageInspector` only owns their order and the title over them
(`PageInspector.TitleFor`: "Bubble", "Panel", "3 selected", "Page"...). Several can show
at once - a text box is a shape too.

`Inspector/CharacterInspector` does the same for a character: `BodySection`,
`LookSection` and `StickerSection` (the worn sticker picked on the stage), under
`Inspector/Character/`, **in that order on purpose**: every gallery pick selects what it
puts on, so a Sticker section appearing above the galleries would push them out from under
the pointer. A sticker clicked on the stage scrolls the Sticker section into view instead.
The window-needing events go through `CharacterEditorDialogs` (§3), so neither layout loses
them. What changes how the stage *looks* rather than the character - Front / Side, compare
with others (`ShowLineUp`), the expression preview - is on `CharacterToolbar`, with Back to
the page. `InspectorFlyouts` opens a section's gallery flyouts flush against the
inspector's left edge, over the stage, whichever column its button is in.

Sections reuse the ribbon's widgets (`ColorMenuButton`, `BackgroundPicker`, the font
boxes, gallery templates, `LayoutPresetPreview`...) and bind the same view-model
members; only the arrangement differs. Look: `CanvasStyles.axaml` (Stanley.Editors,
included app-wide after `RibbonStyles.axaml`) - `Border.inspectorSection`,
`TextBlock.sectionHeader`, `DockPanel.propertyRow` + `TextBlock.propertyLabel`,
`StackPanel.iconRow` + `:is(Button).iconButton`, `:is(Button).inspectorButton`,
`:is(Button).barTool`, the canvas brushes (light and dark) and a few icons. All hang off
their own classes, so the ribbon layout never picks them up. Usable width inside a section
is about 250px; big galleries go behind dropdowns. Buttons never take focus (like the
ribbon's), so Delete, arrows and tool keys keep going to the page.

### 6.4 Adding a feature

A feature lands in **both** layouts: the ribbon tab and the matching canvas section (or the
top bar / main menu, for a tool or a command). `LayoutParityTests` (Stanley.App.Tests) reads
the XAML and code-behind of each layout and fails if a view-model member the ribbon uses
isn't used anywhere in the canvas layout, unless it's on the test's "ribbon-only" list - and
fails too if an entry there goes stale, so the list stays honest. Each entry carries its
reason; today the only one is the Sticker tab's label (`SelectedStickerName`), which the
Sticker section shows through its picker instead. Member-level, not control-level: it
can't tell a button from a menu item, only that the feature is reachable.

### 6.5 Gotchas

- A section is its own name scope: `PageInspector.FindInSections<T>(name)` /
  `CharacterInspector.FindInSections<T>(name)` look in every section.
- A closed flyout's buttons have lost the `DataContext` their commands bind through - close a
  gallery behind a pick by posting `Flyout.Hide()` (see `InsertRibbonTab.axaml.cs`).
- Members raised only from `PageEditorViewModel`'s selection-change method (the `Is…Context`
  flags, `IsPageContext`) follow selection changes made through `Select`; add a new
  context flag next to `IsPanelContext` and raise it there.
- Density is app-wide in Avalonia, so it's never switched in tests (tests share one
  `Application`); the canvas layout's tests run at normal density.
- Radio buttons in a section group by their parent panel - no `GroupName`: a shared name
  would tie together the two `LetteringRows`, or the Text, Picture and Speed lines layer
  toggles, across sections.
- A gallery flyout in the inspector opens towards the page (`BottomEdgeAlignedRight`), not
  off the window's right edge.
- Avalonia doesn't template a `UserControl` that has never been shown, so in a test a
  hidden section's controls have no visual parents and `IsEffectivelyVisible` lies - check
  the logical ancestors' `IsVisible`.
- A closed `MenuFlyout`'s items (and an unopened submenu's) have no `DataContext`, so their
  `Command` is null: a test `ShowAt`s the menu and `Open()`s each parent first. Raising
  `MenuItem.ClickEvent` runs the command but doesn't flip a `ToggleType="CheckBox"` item - a
  pointer click does.
- Headless, opening a flyout with nothing focusable in it moves the keyboard to the first
  focusable control in the window - in the canvas layout an inspector number box. (The real
  app's popups are windows of their own.) A test that checks where keys go after a flyout
  focuses the page again first.
- The window applies the saved theme when it's built, so a test that wants dark sets
  `RequestedThemeVariant` after `CanvasLayoutHarness.Open()`.
- A standard flyout whose content can't take focus hands it to the first text box in the
  window (in the character inspector, the Name box) and the inspector scrolls back up to it,
  opening the flyout away from its button. The character sections' flyouts use
  `ShowMode="Transient"` (they still close on a click elsewhere), and the inspector's
  `ScrollViewer` has `BringIntoViewOnFocusChange="False"` for the shared ones it can't change
  (`ColorSlotDropdown`).
- A slot gallery's flyout closes itself after a pick: the galleries are rebuilt, taking the
  open flyout with them - nothing needs to `Hide()` it.

### 6.6 Tests

`CanvasLayoutHarness` (Stanley.App.HeadlessTests) opens a window in the canvas layout with
in-memory settings and finds its parts (`TopBar`, `InspectorColumn`, `PageInspector`,
`PageToolbar`, `Find<T>(window, name)` across both). `CanvasLayoutTests` covers the shell;
each area has its own `Canvas…Tests` file. The ribbon layout's tests are unchanged - it's
the default.
