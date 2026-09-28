# Modular hair — design

**Status: plan** (GitHub issue #59, "Modułowe fryzury"). Nothing here is built yet.
It builds on the sticker system (`sticker-system.md`) and assumes everything that
document marks as implemented. Treat it as the working plan unless the user says
otherwise. §11 lists the choices that most need a yes or no.

Scope: hair built from pieces (fringe, top, sides, back and extras), each chosen on its
own and each in its own colour. Also multi-colour dyes (streaks, dip-dye, ombré,
rainbow) and streaks placed by hand. Hats, brows and facial hair don't change.

## 1. The problem, from the user's side

What the issue asks for (translated from Polish):

- "Hairstyles put together from different elements, like 2D building blocks. For
  example, the fringe is chosen separately from the back of the hair."
- "Ideally it can do complicated, multi-coloured alternative hairstyles like scene
  hair, without being overwhelming."
- References: Rainbow Dash (a mane striped in six colours) and a real scene haircut.
  The haircut has a black base, a long purple side fringe over one eye, and a purple
  crown and a purple panel down one side. Underneath and at the back it's black.

Today a hairstyle is one sticker in the `hair` slot with a `back` and a `front` layer
(sticker-system.md §5), all in the one `hair` colour. This causes four problems:

- Fringe and back can't be swapped separately. A Bob's fringe comes with a Bob's back.
- Every slot is a stack, so two hairstyles can be worn at once. But that doubles up
  whole hairdos instead of combining parts of them.
- Every hair sticker fills from the single `hair` colour slot. A purple fringe on black
  hair therefore needs a hand-drawn sticker with a second colour class.
- Hair can already take clothing patterns (stripes, plaid) through its colour slot
  (`ColorSlotEditor.CanHaveFabric`), laid out over the whole head. None of them is
  shaped for hair: there are no dip-dyed ends, no streaks and no rainbow.

The user must never need a manual or a layers panel, and must never have to know which
piece is which before seeing one.

## 2. Decisions

1. **A hairstyle is a set of pieces, and each piece is an ordinary sticker in its own
   slot**: `hairTop`, `hairFringe`, `hairSides`, `hairBack` and `hairExtras` (§3). Slots
   already give each piece its own gallery and z-order, per-look and per-panel
   overrides (`ActiveStickers` is per slot), styles (sticker-system.md §20) and Draw your own. No new
   sticker machinery is needed.
2. **Pieces fit because they're all drawn over the same template head.** Warp maps
   every piece onto the character's real head (sticker-system.md §5). Pieces from
   different artists therefore line up, as long as they meet at shared **join lines**
   (hairline, parting, ear line, nape) that the piece templates show (§6).
3. **The named hairstyles become presets.** *Short, Bob, Long, Ponytail, Curly, Bun*
   and the new styles are recipes of pieces. Picking one swaps the whole hairdo in one
   undo step, the way picking a pose or a body type does. This is the one gallery that
   replaces instead of adding (sticker-system.md §8): nobody ever wants two whole
   hairdos stacked.
4. **Every piece follows the Hair colour until it's given its own.** A piece's art is
   still tagged `slot-hair`. When it's worn in a piece slot, those elements take the
   piece's own colour key (the slot name, such as `hairFringe`), which falls back to
   `hair`. Split eyes already work this way (`eyesLeft` → `eyes`, sticker-system.md §21). Nothing changes
   colour until the user asks. A purple fringe stays purple when the Blunt fringe is
   swapped for the Side-swept one, because colours belong to the character
   (sticker-system.md decision 5).
5. **Several colours within one piece come from a dye**: new patterns shaped for hair
   (Streaks, Tips, Roots, Ombré, Rainbow) on the colour of the hair or of one piece.
   A dye is laid across *that piece*, so "the ends in pink" means the ends of that
   piece, however long it is (§5). Dyes are code, like the existing patterns, and need
   no art.
6. **Streaks placed by hand have their own slot** (`hairStreaks`): a lock dragged onto
   the hair and clipped to it, so it never spills over the edge (slice 6).
7. **The default path doesn't change.** There is still one Hair button, a gallery of
   hairstyles and one Hair colour. Pieces are one click further away, in a row at the
   top of the same flyout. Colouring one piece means clicking it on the character and
   picking a colour (§7). There are no new ribbon buttons and no dialogs. The Colours
   group only grows when a piece has a colour of its own.

## 3. Pieces

| Slot | Label | z (in its layer) | Colour key | What it is | Examples |
|---|---|---|---|---|---|
| `hairBack` | Back | 49 | `hairBack` | hair behind the head and body; its parts are `depth: back` | Nape, Bob, Shoulder, Long, Very long, Layered, Curly |
| `hair` | Hair | 50 | `hair` | unchanged: a whole hairstyle in one sticker | existing comics, your own drawings |
| `hairTop` | Top | 51 | `hairTop` | covers the skull down to the hairline and ears | Smooth (parted left, middle or right as styles), Teased, Spiky, Buzz, Curly |
| `hairSides` | Sides | 52 | `hairSides` | locks framing the face, in front of the ears | Short, Chin, Long, Choppy, Curly |
| `hairExtras` | Extras | 53 | `hairExtras` | anything added on | Ponytail, Pigtails, Bun, Space buns, Braid, Cowlick |
| `hairFringe` | Fringe | 62 | `hairFringe` | over the forehead; can cover an eye | Blunt, Side-swept (left or right as styles), Curtain, Wispy, Choppy |
| `hairStreaks` | Streaks | 63 | `streak` (its own) | coloured locks placed by hand; stamps copies | Thin, Chunky, Skunk stripe |

All of them are on the `head` region, like `hair`.

- **The fringe is drawn over glasses (60) and under hats (70).** A fringe over one eye
  then covers that lens too, and a cap's brim still covers the fringe's roots. Every
  other piece stays under glasses, where `hair` is today (§11, question 1).
- **Top is a piece of its own**, not part of the fringe as in most character makers.
  Scene hair is exactly a teased crown with a long side fringe, while a character with
  a straight fringe wants a smooth crown. With Top separate, any fringe goes with any
  crown, and nobody has to draw every combination.
- **A piece never floats on a bald head.** Putting on a Fringe, Sides, Back or Extras
  piece while nothing is worn in Top or `hair` also puts on the Smooth top, in the same
  undo step. *None* in the Top gallery still gives a bald crown on purpose.
- **Extras don't stamp copies**: a second click takes a ponytail off, as in any slot.
  Two buns are the *Space buns* item. Streaks do stamp copies, like prints (sticker-system.md §19).
- **`StickerSlotInfo` gains `SharesColor`**: `"hair"` for the five piece slots, null
  elsewhere. It names the colour slot whose tagged art this slot recolours from its
  own key. The slot's `ColorSlot` stays `"hair"`, so Draw your own and Import tag new
  art `slot-hair` exactly as they do now.
- **Brows and facial hair keep following Hair**, the base colour, as the dark brows in
  the issue's photo do.

## 4. Colour per piece

- **Resolution**: after the chain, `CharacterLooks.Resolve` adds a fallback for each
  piece key: `colors.TryAdd("hairFringe", colors["hair"])`, and the same for fabrics.
  It already does this for `eyesLeft`/`eyesRight`. Piece stickers declare only `hair`
  in their `colors`, so no sticker default can block the fallback. Colour and dye fall
  back separately: a fringe given its own purple keeps the hair's dye until it gets a
  dye of its own (or *None*).
- **Drawing**: `StickerArtPieces.Map` reads a `slot-hair` element worn in a piece slot
  from the piece key. Recolouring keeps the artist's shading: the original colour is
  the sticker's default for the *tagged* slot (`hair`), and the new one is the piece
  key's. Today, recolouring to a key that isn't in the sticker's own `colors` replaces
  the colour flat, which is what happens to split eyes (sticker-system.md §21) now. The same change
  fixes them.
- **Storage**: nothing new. A piece's colour is an entry in the maps that already exist
  (`ColorSlots`, `ColorSlotValues`, `ColorSlotOverrides` and the fabric maps) under a
  key such as `hairFringe`. The entry is absent while the piece follows the hair.
  `character.json` only gains a line like `"hairFringe": "#7b2fbe"` once a piece has
  its own colour, so the diff stays readable.
- **Same as hair** removes the piece's colour and dye at the level being edited. The
  chain can only override, so a named look can't make a piece follow the hair again
  if the character itself gives the piece a colour. In that case *Same as hair* stores
  the look's current hair colour and says so (§11, question 5).
- **The Colours group** (`LookEditing.ColorSlotsInUse`) lists a piece key only when it
  has its own colour somewhere in the chain being edited. A character whose hair is
  all one colour therefore shows one Hair swatch, as today.

## 5. Dyes

New `PatternKind`s, generated in code like Stripes and Plaid:

| Dye | Look | Colours | Parameters |
|---|---|---|---|
| Streaks | uneven stripes running down the hair | 1 | spacing (`size`), width (`weight`), `angle` |
| Tips | the ends in a second colour (dip-dye) | 1 | how far up (`weight`) |
| Roots | the reverse of Tips | 1 | how far down (`weight`) |
| Ombré | a gradient from the hair colour to another, top to bottom | 1 | where the blend starts (`weight`) |
| Rainbow | bands of several colours down the hair | 2 to 7 (six by default) | `angle` |

- **Hair keeps the clothing patterns**, listed below the dyes under "More patterns".
  Stripes across the hair look like raccoon tails, and nothing hair can do today is
  taken away.
- **Dyes are laid across the piece, not repeated.** The five new kinds fit the part's
  own mapped outline, in the head's frame (`RegionMapping.FabricFrame`). Tips means the
  bottom of *that* piece: the waist on a Long back, the eyebrows on a fringe. A
  Rainbow's bands span the piece's width whatever the head's size. The frame still
  turns with the head. For these kinds, the frame becomes one per part instead of one
  per element. Repeating patterns don't change.
- **PDF export stays vector.** Dyes are drawn as clipped bands and a linear gradient,
  not as picture shaders, so the concern about picture shaders in sticker-system.md
  §9.5 doesn't apply.
- **Storage**: `PatternFill` as it is, for example
  `{"kind": "tips", "colors": ["#e0409a"], "weight": 0.3}`. `colors` may now hold up to
  seven for Rainbow; its comment says "one or two" today.
- **UI**: in a hair colour's dropdown, the "Pattern" section is titled **Dye**. It lists
  None, Streaks, Tips, Roots, Ombré and Rainbow, then More patterns, each previewed in
  the current colours as patterns are now. Rainbow shows one small swatch per band;
  clicking a band's swatch recolours that band.
- **Nothing is named after a character.** The dye is called **Rainbow**. Library art and
  presets are original and CC0 (sticker-system.md §17), with nothing taken from any show.

## 6. Drawing your own pieces (the escape hatch)

- **Template layers**: `StickerTemplates.PartsFor` gets the layers for each piece slot.
  Top, Fringe and Sides each get a `front` layer. Back gets a `back` layer (depth
  back). Extras get a `back` and a `front` layer.
- **Join guides**: every hair template, including the one for a whole `hair` sticker,
  gets a second locked guide layer called **joins**. It shows the hairline, the parting
  line, the ear line and the nape, drawn on the default head. A piece that reaches its
  join lines meets any other piece that does. The library's pieces are drawn to them.
- **Every piece tab has *Draw your own...* and *Import...***, as every gallery does
  now. *Draw your own...* on a worn library piece edits a copy of it (existing
  behaviour), so the quickest way to a custom fringe is to start from the nearest one.
- **A whole hairstyle drawn in one file still goes in `hair`**, from the Hairstyles tab.

## 7. UX

### 7.1 The Hair button

The Look tab keeps one **Hair** button in the *Hair & face* group. Its flyout gets a
row of tabs along the top: **Hairstyles** (open by default), Top, Fringe, Sides, Back
and Extras, plus Streaks from slice 6.

- **Hairstyles** starts with **Bald** (everything off), then the presets, then this
  character's own whole `hair` stickers. Each is previewed close-up on this character,
  in its colours, as the Hair gallery is now.
  - A click swaps the whole hairdo in one undo step. Everything in `hair` and in the
    piece slots comes off, and the preset's pieces go on in the preset's styles.
    Colours are kept.
  - The preset that matches what's worn is marked. The button's second line shows its
    name, or "Own mix" once pieces have been changed.
- **A piece tab** is an ordinary slot gallery: None, the character's own pieces, then
  the library's. Each is previewed on this character with the rest of its hair on, so
  it's obvious what a fringe is before knowing the word. A click adds a piece, and a
  click on a worn one takes it off (sticker-system.md §8).
- **The flyout remembers the last tab** until the editor closes. Previews render one
  tab at a time.

### 7.2 Clicking a piece on the character

Clicking hair on the stage already selects the piece under the pointer and opens the
contextual **Sticker** tab. For a hair piece, the tab gains two things:

- A **Colour** swatch dropdown for that piece (labelled "Fringe", for example). It's
  the same colour-and-dye dropdown the Colours group uses.
- A **Same as hair** button, while the piece has its own colour.

The rest of the tab is as now: Worn (styles, such as a fringe swept left or right),
Size, Turn, drag to move, Forward/Back and Take off.

The photo in the issue, starting from scratch, takes four clicks and no new concepts:

1. Hairstyles › Scene.
2. Hair colour › black.
3. Click the fringe, pick purple.
4. Click the top, pick purple.

### 7.3 Colour schemes

The Hair swatch's dropdown gets a **Schemes** row above its colours. Each scheme is
previewed on this character and gives a multi-colour look in one click. It takes one
accent colour, a vivid purple by default.

| Scheme | Does |
|---|---|
| Natural | every piece back to the hair colour, no dyes |
| Two-tone | Top and Fringe in the accent (the issue's photo) |
| Peekaboo | Back in the accent, under everything else |
| Fringe only | Fringe in the accent |
| Dip-dye | Tips in the accent, on the hair |
| Ombré | Ombré to the accent, on the hair |
| Rainbow | Rainbow, on the hair |

A scheme writes ordinary piece colours and dyes in one undo step. Everything it does
can then be changed piece by piece.

### 7.4 Page editor

Nothing new is needed. Right-click › *This panel only* already takes stickers off and
puts them on (a fringe blown back for one panel), sets colours (a fringe that's purple
in one flashback) and picks styles.

## 8. Library

The art is original, CC0, flat, in front and profile views, and drawn to the join
guides (§6):

- **Tops**: Smooth (styles: part left, middle, right), Teased, Spiky, Buzz, Curly.
- **Fringes**: Blunt, Side-swept (styles: left, right; long, over one eye), Curtain,
  Wispy, Choppy.
- **Sides**: Short, Chin, Long, Choppy, Curly.
- **Backs**: Nape, Bob, Shoulder, Long, Very long, Layered, Curly.
- **Extras**: Ponytail, Pigtails, Bun, Space buns, Braid, Cowlick.
- **Streaks** (slice 6): Thin, Chunky, Skunk stripe.

The presets live in `StickerLibrary.Hairstyles`, next to `DefaultFace`. Each is a name
and a list of library keys, each key with an optional style:

| Preset | Top | Fringe | Sides | Back | Extras |
|---|---|---|---|---|---|
| Short | Smooth | Wispy | – | Nape | – |
| Bob | Smooth | Blunt | Chin | Bob | – |
| Long | Smooth (middle) | – | Long | Long | – |
| Ponytail | Smooth | Wispy | – | – | Ponytail |
| Curly | Curly | – | Curly | Curly | – |
| Bun | Smooth | – | – | Nape | Bun |
| Pixie | Spiky | Choppy | – | Nape | – |
| Pigtails | Smooth (middle) | Blunt | – | – | Pigtails |
| Space buns | Smooth (middle) | Wispy | – | – | Space buns |
| Side-swept | Smooth (left) | Side-swept | Chin | Shoulder | – |
| Emo | Smooth (left) | Side-swept | Choppy | Shoulder | – |
| Scene | Teased | Side-swept | Choppy | Layered | – |

- **The first six are today's six**, redrawn as pieces with the same silhouettes, so
  the gallery a new user sees looks as it does now.
- **The old whole-hairstyle stickers stay in the library, unlisted**, so comics that
  wear them still resolve `library:hair/<name>` (`StickerLibrary.SourceOf`). They draw
  exactly as before and appear in the Hairstyles tab as the character's own.

## 9. Where code goes

| Project | New / changed |
|---|---|
| ProjectModel | `StickerSlots`: the six slots and `SharesColor`. `CharacterLooks.Resolve`: fallbacks from piece keys to `hair` for colours and fabrics. `PatternKind`: Streaks, Tips, Roots, Ombre, Rainbow. `PartClip.Hair` (slice 6) |
| Rendering | `StickerArtPieces.Map`/`Recolor`: the piece key, with shading kept from the tagged slot. `FabricShaders`: dyes fitted to a part's bounds. `StickerTemplates`: piece layers and the joins guide. `CharacterRenderer`: `clip: hair` (slice 6) |
| Editing | `LookEditing.WearHairstyle` (replaces every hair slot in one step), the automatic Top on wear, `FollowHair` (Same as hair), `HairSchemes` (a scheme → colours and dyes) |
| StickerLibrary | `Library/hairTop/…`, `hairFringe/…`, `hairSides/…`, `hairBack/…`, `hairExtras/…`, `hairStreaks/…`. `StickerLibrary.Hairstyles` |
| Editors | the Hair flyout's tabs and Hairstyles tab (`HairGallery`), piece colour on the Sticker tab, the Dye section and Schemes row in `ColorSlotEditor`, and `ColorSlotLabel` for piece keys |

## 10. Delivery slices (each a PR, each green on its own)

1. **Pieces.**
   - Builds: the five piece slots; colour keys with their fallback (no UI for a piece's
     own colour yet); templates and join guides; the Hair flyout with its tabs; the
     pieces for today's six hairstyles, and those six as presets.
   - Visible: the same six hairstyles, now mixable (a Bob's fringe on Long hair).
   - Tests:
     - A piece with no colour of its own draws in `hair` (pixel probe) and keeps its
       shade offsets.
     - A preset is one undo step and replaces a legacy `hair` sticker.
     - Wearing a fringe with nothing on top also puts on the Smooth top.
     - A character wearing an old whole hairstyle opens and draws as before, and an
       unchanged save is an empty diff.
     - Pieces survive save and open.
     - The flyout works (headless test).
2. **Colour per piece.**
   - Builds: the Sticker tab's colour and Same as hair; the Colours group listing
     pieces that have their own colour; the same per look and per panel.
   - Visible: the issue's photo.
   - Tests:
     - Pixel probes: a purple fringe over a black back.
     - Swapping the fringe keeps its colour.
     - *Same as hair* is one undo step.
     - A named look's piece colour applies.
     - Split eyes keep their shading (the `Recolor` fix).
3. **Scene and friends.**
   - Builds: the Teased top, the Side-swept fringe (left and right styles), Choppy
     sides, the Layered back, the other extras, the Pixie to Scene presets, and the
     fringe over glasses.
   - Tests:
     - The side-swept fringe covers the eye on its side (probe).
     - Changing its style moves it to the other side.
4. **Dyes.**
   - Builds: the five dye kinds, fitted to the part; the Dye section; Rainbow's band
     swatches.
   - Tests:
     - Tips colour shows at the bottom of a Long back and not at its top.
     - The same dye on a short fringe still shows its tips.
     - Rainbow puts the right number of bands across a piece.
     - A dye survives swapping the piece.
     - PDF export keeps dyes as vectors.
5. **Schemes.**
   - Builds: the Schemes row.
   - Tests: what each scheme writes; each is one undo step; Natural clears them.
6. **Streaks.**
   - Builds: the `hairStreaks` slot, `clip: hair`, and the three streaks.
   - Tests:
     - A streak dragged past the edge of the hair is clipped (probe).
     - With no hair worn, it isn't clipped.
     - Copies stamp like prints.

Slices 1 and 2 already cover the issue's first paragraph and its photo. Slices 3 to 6
are the "complicated but not overwhelming" part and can come in any order after 2.

## 11. Open questions

Each has a recommendation, which the plan assumes until told otherwise:

1. **Should the fringe go over glasses** (z 62), not under them like the rest of the
   hair? *Recommended*: over. Otherwise a fringe covering one eye has the lens drawn on
   top of it.
2. **Should Hairstyles replace while pieces add?** *Recommended*: yes. It's the one
   exception to "a gallery click only ever adds", and it's a gallery of presets, not of
   items.
3. **Five pieces (with Top) or four?** *Recommended*: five (§3).
4. **Should colour and dye fall back separately?** *Recommended*: yes (§4).
5. **Same as hair inside a named look** copies the colour when the character gives the
   piece a colour of its own. *Recommended*: accept this in V1 instead of adding an
   "inherit" marker to the colour maps, and revisit if it causes trouble.
6. **Should the old six whole-hairstyle stickers stay in the library, unlisted**,
   instead of being deleted? *Recommended*: yes (§8).

## 12. Later

- A length slider per piece: a vertical stretch of Back and Sides from the crown.
- A colour of its own for each streak copy.
- Hair that reacts to a pose: wind, or falling with gravity when the head tilts.
- Three-quarter art, once the rig has that view.
- Keeping a whole hairdo (pieces, styles and colours) in My Assets as one item
  (`asset-packs.md`).
