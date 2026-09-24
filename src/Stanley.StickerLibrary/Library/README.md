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
