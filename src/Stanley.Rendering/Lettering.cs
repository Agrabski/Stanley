using System.Text;
using SkiaSharp;

namespace Stanley.Rendering;

/// <summary>
/// The text tools bubbles and free text share: typefaces (in bold and italic too) and a
/// greedy word wrap. SkiaSharp-only measurement - no Avalonia text stack here - so the
/// canvas, thumbnails and exports all wrap a line at exactly the same word.
/// <para>
/// Fonts come from two places: the ones Stanley ships with (<see cref="AddBundledFace"/> -
/// the app registers them at startup, one of them as the default lettering font, and
/// they look the same on every computer) and every family installed on this computer
/// (<see cref="SystemFamilies"/>). A family that's neither - a comic made on another
/// computer - draws in <see cref="DefaultFamily"/>, keeping its name for when it's there.
/// </para>
/// </summary>
public static class Lettering
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, List<SKTypeface>> Bundled = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> BundledOrder = [];
    private static readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> Typefaces = new();
    private static string? _defaultBundled;
    private static IReadOnlyList<string>? _systemFamilies;
    private static HashSet<string>? _systemFamilySet;

    /// <summary>
    /// Adds one face (a TrueType/OpenType file's bytes) of a font Stanley ships with, under
    /// <paramref name="family"/>. Add every weight and slant the family has; drawing picks the
    /// closest. <paramref name="isDefault"/> makes the family the default lettering font.
    /// Returns false (and adds nothing) when the bytes aren't a font.
    /// </summary>
    public static bool AddBundledFace(string family, byte[] data, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        var typeface = SKTypeface.FromData(SKData.CreateCopy(data));
        if (typeface is null)
            return false;

        lock (Gate)
        {
            if (!Bundled.TryGetValue(family, out var faces))
            {
                Bundled[family] = faces = [];
                BundledOrder.Add(family);
            }
            faces.Add(typeface);
            if (isDefault)
                _defaultBundled = family;
            Typefaces.Clear();
            _systemFamilies = null;
            _systemFamilySet = null;
        }
        return true;
    }

    /// <summary>The fonts Stanley ships with, in the order they were added.</summary>
    public static IReadOnlyList<string> BundledFamilies
    {
        get { lock (Gate) return BundledOrder.ToList(); }
    }

    /// <summary>The font lettering uses when none is chosen (or the chosen one isn't here): the bundled default, or the system's default typeface until one is registered.</summary>
    public static string DefaultFamily
    {
        get { lock (Gate) return _defaultBundled ?? SKTypeface.Default.FamilyName; }
    }

    /// <summary>Every font family installed on this computer, once each (not under its aliases too), sorted by name - hidden system families and ones Stanley bundles itself left out.</summary>
    public static IReadOnlyList<string> SystemFamilies
    {
        get
        {
            lock (Gate)
                return _systemFamilies ??= LoadSystemFamilies();
        }
    }

    // Caller holds Gate. Every installed name counts as available, but the list shows each
    // font once: fontconfig also names a family by its aliases (a Japanese font under its
    // Latin and its Japanese name), which open the same face - the face's own name wins.
    private static List<string> LoadSystemFamilies()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // face's own name -> name shown
        foreach (var name in SKFontManager.Default.FontFamilies)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.') || Bundled.ContainsKey(name) || !installed.Add(name))
                continue;
            var face = SKFontManager.Default.MatchFamily(name)?.FamilyName ?? name;
            if (!listed.TryGetValue(face, out _) || string.Equals(name, face, StringComparison.OrdinalIgnoreCase))
                listed[face] = name;
        }
        _systemFamilySet = installed;
        return listed.Values.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when <paramref name="family"/> draws as itself here: null (the default), a bundled font or an installed one.</summary>
    public static bool IsAvailable(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return true;
        lock (Gate)
        {
            if (Bundled.ContainsKey(family))
                return true;
            _systemFamilies ??= LoadSystemFamilies();
            return _systemFamilySet!.Contains(family);
        }
    }

    /// <summary>True when <paramref name="family"/> is one Stanley ships with.</summary>
    public static bool IsBundled(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return false;
        lock (Gate)
            return Bundled.ContainsKey(family);
    }

    /// <summary>The family that <paramref name="family"/> is actually drawn in: itself when it's here, <see cref="DefaultFamily"/> otherwise.</summary>
    public static string Resolve(string? family) =>
        !string.IsNullOrWhiteSpace(family) && IsAvailable(family) ? family : DefaultFamily;

    /// <summary>
    /// A font at <paramref name="size"/> (in whatever units the canvas is drawing in) in
    /// <paramref name="family"/> (null: <see cref="DefaultFamily"/>). Linear metrics and
    /// subpixel positioning: the page draws at a few units per glyph (millimetres) under a
    /// zoom transform, where hinted metrics would snap widths to whole units and throw the
    /// wrapping off. Bold and italic use the family's real faces when it has them, and fake
    /// them (embolden, slant) when it doesn't, so they always show.
    /// </summary>
    public static SKFont Font(float size, bool bold = false, bool italic = false, string? family = null)
    {
        var typeface = Typeface(family, bold, italic);
        var font = new SKFont(typeface, size) { LinearMetrics = true, Subpixel = true };
        if (bold && typeface.FontStyle.Weight < (int)SKFontStyleWeight.SemiBold)
            font.Embolden = true;
        if (italic && typeface.FontStyle.Slant == SKFontStyleSlant.Upright)
            font.SkewX = -0.2f;
        return font;
    }

    /// <summary>The typeface <see cref="Font"/> draws <paramref name="family"/> with.</summary>
    public static SKTypeface Typeface(string? family, bool bold = false, bool italic = false)
    {
        var key = (family?.Trim() ?? string.Empty, bold, italic);
        lock (Gate)
        {
            if (!Typefaces.TryGetValue(key, out var typeface))
            {
                typeface = Find(key.Item1, bold, italic) ?? Find(_defaultBundled ?? string.Empty, bold, italic) ?? SKTypeface.Default;
                Typefaces[key] = typeface;
            }
            return typeface;
        }
    }

    // Caller holds Gate. Empty = the default: the bundled default, else the system's own default.
    private static SKTypeface? Find(string family, bool bold, bool italic)
    {
        if (family.Length == 0)
            family = _defaultBundled ?? string.Empty;
        if (family.Length > 0 && Bundled.TryGetValue(family, out var faces))
            return Closest(faces, bold, italic);

        var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        if (family.Length == 0)
            return bold || italic ? SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, style) ?? SKTypeface.Default : SKTypeface.Default;

        _systemFamilies ??= LoadSystemFamilies();
        return _systemFamilySet!.Contains(family) ? SKFontManager.Default.MatchFamily(family, style) : null;
    }

    /// <summary>The face nearest the asked-for weight, preferring the asked-for slant.</summary>
    private static SKTypeface Closest(List<SKTypeface> faces, bool bold, bool italic)
    {
        var weight = bold ? (int)SKFontStyleWeight.Bold : (int)SKFontStyleWeight.Normal;
        return faces
            .OrderBy(f => (f.FontStyle.Slant != SKFontStyleSlant.Upright) == italic ? 0 : 1)
            .ThenBy(f => Math.Abs(f.FontStyle.Weight - weight))
            .First();
    }

    /// <summary>
    /// One run of <see cref="Text"/> that draws in one <see cref="Font"/> (docs/sticker-system.md,
    /// prints): see <see cref="FallbackRuns"/>.
    /// </summary>
    public readonly record struct TextRun(string Text, SKFont Font);

    /// <summary>
    /// Splits <paramref name="text"/> into <see cref="TextRun"/>s, each in a font that can draw
    /// it, a grapheme cluster at a time (so ❤️ with its variation selector, a skin tone or a
    /// family joined with zero-width joiners stays one piece): an emoji in the system's colour
    /// emoji font when there is one; anything else in <paramref name="font"/> where it has the
    /// glyph, or the system's best match (<see cref="SKFontManager.MatchCharacter(int)"/>) for
    /// the symbols most lettering fonts lack. Falls back to <paramref name="font"/> itself (its
    /// own missing-glyph box) when nothing installed has the character, so this never throws.
    /// Used by prints; bubbles and free text draw in one font throughout and don't call this.
    /// </summary>
    public static IReadOnlyList<TextRun> FallbackRuns(string text, SKFont font)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var runs = new List<TextRun>();
        var start = 0;
        SKTypeface? currentFace = null;
        var clusters = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (clusters.MoveNext())
        {
            var cluster = (string)clusters.Current;
            var index = clusters.ElementIndex;
            var face = FaceFor(cluster, font);
            if (currentFace is not null && face != currentFace)
            {
                runs.Add(new TextRun(text[start..index], RunFont(currentFace, font)));
                start = index;
            }
            currentFace = face;
        }
        if (currentFace is not null)
            runs.Add(new TextRun(text[start..], RunFont(currentFace, font)));
        return runs;
    }

    private static readonly Dictionary<int, SKTypeface?> FallbackFaces = [];
    private static SKTypeface? _emojiFace;
    private static bool _emojiLooked;

    /// <summary>The typeface one grapheme cluster draws in (see <see cref="FallbackRuns"/>).</summary>
    private static SKTypeface FaceFor(string cluster, SKFont font)
    {
        Rune.DecodeFromUtf16(cluster, out var first, out _);
        var cp = first.Value;
        if (IsEmoji(cluster, cp) && EmojiFace() is { } emoji && emoji.ContainsGlyph(cp))
            return emoji;
        if (font.ContainsGlyph(cp))
            return font.Typeface;
        lock (Gate)
        {
            if (!FallbackFaces.TryGetValue(cp, out var match))
                FallbackFaces[cp] = match = SKFontManager.Default.MatchCharacter(cp);
            return match ?? font.Typeface;
        }
    }

    /// <summary>A cluster that reads as an emoji: asked for as one (a variation selector, a joiner), or a pictograph that normally is one.</summary>
    private static bool IsEmoji(string cluster, int cp) =>
        cluster.Contains('\uFE0F') || cluster.Contains('\u200D') || cp >= 0x1F000
        || cp is >= 0x2300 and <= 0x23FF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF;

    /// <summary>The system's colour emoji font (Noto Color Emoji, Segoe UI Emoji, Apple Color Emoji...), asked for by emoji presentation; null if there's none.</summary>
    private static SKTypeface? EmojiFace()
    {
        lock (Gate)
        {
            if (!_emojiLooked)
            {
                _emojiLooked = true;
                _emojiFace = SKFontManager.Default.MatchCharacter(null, SKFontStyle.Normal, ["und-Zsye"], 0x1F600);
            }
            return _emojiFace;
        }
    }

    private static SKFont RunFont(SKTypeface typeface, SKFont like) =>
        typeface == like.Typeface ? like : new SKFont(typeface, like.Size) { LinearMetrics = true, Subpixel = true };

    /// <summary>The tight bounds of <paramref name="runs"/> drawn left to right from the origin - the box <see cref="DrawFallback"/> fills when called with the same origin.</summary>
    public static SKRect MeasureFallback(IReadOnlyList<TextRun> runs, SKPaint paint)
    {
        var x = 0f;
        var bounds = SKRect.Empty;
        for (var i = 0; i < runs.Count; i++)
        {
            var w = runs[i].Font.MeasureText(runs[i].Text, out var runBounds, paint);
            runBounds.Offset(x, 0);
            bounds = i == 0 ? runBounds : SKRect.Union(bounds, runBounds);
            x += w;
        }
        return bounds;
    }

    /// <summary>Draws <paramref name="runs"/> left to right from (<paramref name="x"/>, <paramref name="y"/>), each in its own font, and returns the total width drawn.</summary>
    public static float DrawFallback(SKCanvas canvas, IReadOnlyList<TextRun> runs, float x, float y, SKPaint paint)
    {
        var start = x;
        foreach (var run in runs)
        {
            canvas.DrawText(run.Text, x, y, SKTextAlign.Left, run.Font, paint);
            x += run.Font.MeasureText(run.Text, paint);
        }
        return x - start;
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
