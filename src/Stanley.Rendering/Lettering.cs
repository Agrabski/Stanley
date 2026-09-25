using SkiaSharp;

namespace Stanley.Rendering;

/// <summary>
/// The text tools bubbles and free text share: the lettering typeface (in bold and italic
/// too) and a greedy word wrap. SkiaSharp-only measurement - no Avalonia text stack here -
/// so the canvas, thumbnails and exports all wrap a line at exactly the same word.
/// </summary>
public static class Lettering
{
    private static readonly Dictionary<(bool Bold, bool Italic), SKTypeface> Typefaces = new();
    private static readonly Lock TypefacesLock = new();

    /// <summary>
    /// A font at <paramref name="size"/> (in whatever units the canvas is drawing in). Linear
    /// metrics and subpixel positioning: the page draws at a few units per glyph (millimetres)
    /// under a zoom transform, where hinted metrics would snap widths to whole units and throw
    /// the wrapping off. Bold and italic use the default family's real faces when it has them,
    /// and fake them (embolden, slant) when it doesn't, so they always show.
    /// </summary>
    public static SKFont Font(float size, bool bold = false, bool italic = false)
    {
        var typeface = Typeface(bold, italic);
        var font = new SKFont(typeface, size) { LinearMetrics = true, Subpixel = true };
        if (bold && typeface.FontStyle.Weight < (int)SKFontStyleWeight.SemiBold)
            font.Embolden = true;
        if (italic && typeface.FontStyle.Slant == SKFontStyleSlant.Upright)
            font.SkewX = -0.2f;
        return font;
    }

    private static SKTypeface Typeface(bool bold, bool italic)
    {
        if (!bold && !italic)
            return SKTypeface.Default;
        lock (TypefacesLock)
        {
            if (!Typefaces.TryGetValue((bold, italic), out var typeface))
            {
                var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                    italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                typeface = SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, style) ?? SKTypeface.Default;
                Typefaces[(bold, italic)] = typeface;
            }
            return typeface;
        }
    }

    /// <summary>Splits <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> at word breaks (a single over-long word keeps its own line); <c>\n</c> always starts a new line.</summary>
    public static List<string> Wrap(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var current = words[0];
            for (var i = 1; i < words.Length; i++)
            {
                var candidate = current + " " + words[i];
                if (font.MeasureText(candidate, paint) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = words[i];
                }
            }
            lines.Add(current);
        }
        return lines;
    }
}
