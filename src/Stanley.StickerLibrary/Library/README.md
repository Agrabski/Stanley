# Stanley starter library

The stickers and pattern tiles in this folder are the art Stanley ships for dressing
characters. Wearing one copies it into the user's project, so comics made with them
carry these files.

**They are released under CC0 1.0** (see `LICENSE`): no rights reserved, so a comic
using them carries no licence obligations. This applies to the art in this folder only;
Stanley's code is AGPL-3.0.

Only add art that is your own original work (and that you release here under CC0) or
that is already CC0. No third-party art under any other licence.

Layout: `<slot>/<key>/sticker.json` in Stanley's project format, plus any
`variants/<variant>/<view>.svg` art, and `patterns/<name>.svg` tiles. A library
sticker's `id` is a placeholder; a fresh one is minted when it's worn.

Drawn art follows the template conventions (`StickerTemplates.Export`): the default
body is 1000 units tall with the origin on the ground between the feet, each part is
a top-level layer named after it, `class="slot-<name>"` makes a shape follow that
colour slot (greys such as the ink outline stay as drawn), and a 3-unit line matches
the body's outline. Faces carry the expression vocabulary as variants (eyes: neutral,
happy, sad, angry, wide, closed, wink, halfClosed; brows: neutral, raised, angry, sad,
skeptical; mouth: neutral, smile, grin, open, shout, frown, o, smirk). Pattern tiles
are one repeat per view box, with `slot-ground`, `slot-1` and `slot-2` classes.
Prints (`print/`) are symbols for clothes: one Pin art part named `print` on the
chest, in `class="slot-print"`, clipped to the clothes underneath (`clip: clothes`);
leave details as holes rather than fixed colours, so they read in any colour.
