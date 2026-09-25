using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>The ready-made kinds of free text - the "ease of use" default path; every setting stays adjustable afterwards.</summary>
public enum TextStylePreset
{
    /// <summary>Narration in a box, usually in a panel's corner.</summary>
    Caption,

    /// <summary>Bare lettering - a sign, a label, a thought.</summary>
    Plain,

    /// <summary>Big, bold, outlined - BLAM.</summary>
    SoundEffect
}

/// <summary>What each <see cref="TextStylePreset"/> looks like, and how wide it starts out. Same enum-plus-static-lookup shape as <c>BubbleStylePresets</c>.</summary>
public static class TextStylePresets
{
    public static readonly ColorValue Ink = ColorValue.FromHex("#1c1c1c");
    public static readonly ColorValue CaptionYellow = ColorValue.FromHex("#fff4c2");
    public static readonly ColorValue EffectYellow = ColorValue.FromHex("#ffd21f");

    public static IReadOnlyList<TextStylePreset> All { get; } = Enum.GetValues<TextStylePreset>();

    public static TextStyle Style(TextStylePreset preset) => preset switch
    {
        TextStylePreset.Caption => new TextStyle(3.5, Ink, Align: TextAlign.Left, BoxFill: CaptionYellow, BoxStroke: Ink),
        TextStylePreset.Plain => new TextStyle(3.5, Ink),
        TextStylePreset.SoundEffect => new TextStyle(10, EffectYellow, Bold: true, Italic: true, Outline: Ink),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    public static string Name(TextStylePreset preset) => preset switch
    {
        TextStylePreset.SoundEffect => "Sound effect",
        _ => preset.ToString()
    };

    /// <summary>A new text's width before the user sizes it: roughly one short line.</summary>
    public static double DefaultWidthMm(TextStylePreset preset) => preset switch
    {
        TextStylePreset.Caption => 45,
        TextStylePreset.SoundEffect => 50,
        _ => 35
    };

    /// <summary>The preset <paramref name="style"/> is, if it's exactly one (size and font aside - a bigger caption, or one in another font, is still a caption).</summary>
    public static TextStylePreset? Of(TextStyle style) =>
        All.Cast<TextStylePreset?>().FirstOrDefault(p => Style(p!.Value) with { FontSizeMm = style.FontSizeMm, FontFamily = style.FontFamily } == style);
}

/// <summary>
/// Every valid way to change free text (<see cref="TextElement"/>): pure functions, one
/// implementation for live preview and commit alike, like <see cref="BubbleEditing"/>.
/// </summary>
public static class TextEditing
{
    public const double MinWidthMm = 5;
    public const double MinHeightMm = 2;
    public const double MinFontSizeMm = 1;
    public const double MaxFontSizeMm = 60;
    public const int MaxTextLength = 2000;
    public const int MaxFontNameLength = 100;

    /// <summary>The letter sizes Bigger/Smaller step through, in mm (3.5 is the usual ~10pt dialogue size).</summary>
    public static IReadOnlyList<double> SizeSteps { get; } = [2.5, 3, 3.5, 4, 5, 6, 8, 10, 12, 16, 20, 28, 40, 60];

    public static EditResult<TextElement> Create(Rect2D bounds, TextStyle style, ElementLayer layer = ElementLayer.Foreground, string text = "")
    {
        if (bounds.Width < MinWidthMm || bounds.Height < MinHeightMm)
            return EditResult<TextElement>.Failure($"A text box must be at least {MinWidthMm}x{MinHeightMm}mm.");
        return EditResult<TextElement>.Success(new TextElement(ElementId.New(), layer, bounds, text, style));
    }

    public static EditResult<TextElement> SetText(TextElement text, string value) =>
        value.Length > MaxTextLength
            ? EditResult<TextElement>.Failure($"Text can't exceed {MaxTextLength} characters.")
            : EditResult<TextElement>.Success(text with { Text = value });

    public static EditResult<TextElement> SetStyle(TextElement text, TextStyle style)
    {
        if (style.FontSizeMm < MinFontSizeMm || style.FontSizeMm > MaxFontSizeMm)
            return EditResult<TextElement>.Failure($"Letters must be {MinFontSizeMm}-{MaxFontSizeMm}mm tall.");
        var font = CleanFontName(style.FontFamily);
        return font.IsValid
            ? EditResult<TextElement>.Success(text with { Style = style with { FontFamily = font.Value } })
            : EditResult<TextElement>.Failure(font.Error!);
    }

    /// <summary>A font family name as it's stored: trimmed, and null (the default lettering font) when blank. Names are free text - any installed family, even one this computer lacks - but short and on one line.</summary>
    public static EditResult<string?> CleanFontName(string? family)
    {
        var name = family?.Trim();
        if (string.IsNullOrEmpty(name))
            return EditResult<string?>.Success(null);
        if (name.Length > MaxFontNameLength || name.Any(char.IsControl))
            return EditResult<string?>.Failure("That isn't a font name.");
        return EditResult<string?>.Success(name);
    }

    public static EditResult<TextElement> Resize(TextElement text, Rect2D bounds) =>
        bounds.Width < MinWidthMm - 1e-9 || bounds.Height < MinHeightMm - 1e-9
            ? EditResult<TextElement>.Failure($"A text box must be at least {MinWidthMm}x{MinHeightMm}mm.")
            : EditResult<TextElement>.Success(text with { Bounds = bounds });

    public static TextElement Move(TextElement text, double dx, double dy) =>
        text with { Bounds = text.Bounds with { X = text.Bounds.X + dx, Y = text.Bounds.Y + dy } };

    /// <summary>Grows the box downwards to <paramref name="neededHeight"/> (from the renderer's measurement) so typed text never has to shrink to fit; never shrinks a box the user made bigger.</summary>
    public static TextElement GrowToFit(TextElement text, double neededHeight) =>
        neededHeight > text.Bounds.Height + 1e-6 ? text with { Bounds = text.Bounds with { Height = neededHeight } } : text;

    /// <summary>The next size up the <see cref="SizeSteps"/> ladder (Word's "Grow font").</summary>
    public static double Bigger(double size) => SizeSteps.FirstOrDefault(s => s > size + 1e-6, Math.Min(size * 1.25, MaxFontSizeMm));

    public static double Smaller(double size) => SizeSteps.LastOrDefault(s => s < size - 1e-6, Math.Max(size / 1.25, MinFontSizeMm));
}
