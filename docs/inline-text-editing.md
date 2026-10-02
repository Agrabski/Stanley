# Inline text editing — typing into a bubble or text box

**Status: implemented** (issue #19). How the text box that opens over a bubble (or a
free-text element) is kept looking like the lettering it will become once Enter keeps
it, and the traps that made it drift before.

Scope: `PageEditorView` (the inline `TextBox`), `PageCanvasControl` /
`PageCanvasDrawOperation` (what the canvas draws meanwhile), and the measurement in
`Stanley.Rendering` (`Lettering`, `BubbleTextRenderer`) they share. How a bubble's
shape or a text box's style is edited is elsewhere.

## 1. The goal

What you see while typing is what you get after Enter: same letter size, same words on
each line, the bubble (or text box) already the size it will be. The page draws lettering
with SkiaSharp (`BubbleTextRenderer.Draw`, `ElementRenderer.DrawText`); the editor is an
Avalonia `TextBox`. Two text engines, so every difference between them has to be removed
on purpose. The headless tests in `InlineTextFidelityTests` pin each one below.

## 2. The pieces

- **`PageEditorView.RefreshDraft`** works out what Enter would keep of the words typed so
  far, by asking the view model: `PreviewBubbleText` / `PreviewElementText`. They run the
  very edit `SetBubbleText` / `SetElementText` run (the shared `WithBubbleText` /
  `WithElementText`) on `Working` without applying it, so the preview can't drift from the
  commit - the bubble grown to fit (`BubbleEditing.GrowToFit`), pulled back inside its panel.
- The result is the **draft**: `EditingBubble.Draft` (a bubble) or
  `PageCanvasControl.EditingTextDraft` (a text element). `PageCanvasDrawOperation.WithoutEditedText`
  draws the draft in place of the stored one (the bubble's text blanked - the editor draws
  it), so the outline and box are the size Enter will leave them. Nothing is committed
  until Enter / focus loss; Esc throws the draft away.
- **`PositionTextEditor`** puts the box over the *draft's* text area
  (`BubbleTextRenderer.TextArea` / `ElementRenderer.TextArea`) at the lettering's real
  on-screen size. It re-runs on text change, restyle (`Working` changed), zoom and size
  changes. Left/right-aligned text keeps its box on that side when the box is widened.
- Words that haven't changed get **no draft**: `EndTextEdit` doesn't commit them, so a
  bubble someone dragged smaller than its text must keep spilling, not snap bigger on
  opening the editor.

## 3. What made them differ (don't reintroduce)

1. **A size fudge.** The editor used `size * zoom * 0.95` clamped to 11-160 px. Any zoom
   where that wasn't the real size changed where lines broke, worse the longer the
   text. The font size is now exactly `FontPoints.ToMm(pt) * zoom` (floor of 1 px only
   so it can't be zero). Zoomed far out, typing is small - that's the page's real size.
2. **The bubble grew only on Enter.** A long line overflowed the old bubble while typing,
   then the bubble grew (and re-wrapped) on commit. The draft grows it live.
3. **Layout rounding.** Avalonia rounds a box up to whole pixels (`UseLayoutRounding`);
   81.2 px became 82 px, wide enough to fit a word the page wraps. The editor has
   `UseLayoutRounding="False"` so its width is the text area's, exactly.
4. **Kerning and ligatures.** The page draws glyph by glyph (`canvas.DrawText`, no
   shaper); Avalonia's HarfBuzz shaping kerns pairs and applies `liga`/`clig`/`calt`,
   setting text narrower (6% on "AVATAR WAVY Ty To"). `PageEditorView.PlainGlyphs` turns
   those off (`FontFeatures`). If the page ever gains a shaper, drop this and kern both.
5. **Skia measures short at small sizes.** `SKFont.MeasureText` at the page's size
   (a few millimetres) comes out ~0.35% narrower than the real width - the font engine
   rounds the size - while the page draws those glyphs scaled up by the zoom and Avalonia
   lays out at screen size. `Lettering.Wrap` and `BubbleTextRenderer.NeededScale` measure
   through `Lettering.Measurer` (a copy of the font at 64x, answer scaled back), which
   matches Avalonia to three decimals. Use it for any new wrapping measurement.
6. **Grow-to-fit landing on a knife edge.** `NeededScale` bisects to the *smallest*
   scale that fits, so after growing, the widest line's width equals the area's width to
   within float noise, and two engines can break that tie differently. `FitSlack`
   (0.5%) gives the bubble a hair more room.

Line height and baseline already match (`font.Spacing` and `font.Metrics.Ascent` equal
Avalonia's line height and baseline, checked for the bundled Inter), so the editor sets
neither. A lettering font whose metrics Avalonia reads differently would be the place to
look first if a line pitch ever drifts.

## 4. Gotchas

- `TextBox.TextChanged` fires while `OpenTextEditor` fills the box; `_editing` is set
  first so the draft exists from the first frame.
- `EditingBubble` is a `record struct`; the canvas only repaints when the value changes,
  and `Draft` takes part in that equality (bubbles are records).
- `PageCanvasControl` doesn't import `ProjectModel.Issues` (it clashes with
  `Avalonia.Controls.Documents.TextElement`), so its draft property names the type in full.
- The test text should be long enough to need several lines; the failing cases are on
  the wrap boundary, which short strings never reach.
- The tests read the editor's own lines from `TextPresenter.TextLayout` (Avalonia 12); if
  that moves, the test helper `EditorLines` is the one place to fix.
