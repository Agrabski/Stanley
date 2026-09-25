using System.Globalization;
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
        TextStylePreset.Caption => new TextStyle(10, Ink, Align: TextAlign.Left, BoxFill: CaptionYellow, BoxStroke: Ink),
        TextStylePreset.Plain => new TextStyle(10, Ink),
        TextStylePreset.SoundEffect => new TextStyle(28, EffectYellow, Bold: true, Italic: true, Outline: Ink),
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
        All.Cast<TextStylePreset?>().FirstOrDefault(p => Style(p!.Value) with { FontSizePt = style.FontSizePt, FontFamily = style.FontFamily } == style);
}

/// <summary>
/// Every valid way to change free text (<see cref="TextElement"/>): pure functions, one
/// implementation for live preview and commit alike, like <see cref="BubbleEditing"/>.
/// </summary>
public static class TextEditing
{
    public const double MinWidthMm = 5;
    public const double MinHeightMm = 2;
    /// <summary>Word's smallest and largest font sizes, in points.</summary>
    public const double MinFontSizePt = 1;
    public const double MaxFontSizePt = 1638;
    public const int MaxTextLength = 2000;
    public const int MaxFontNameLength = 100;

    /// <summary>Word's font size list, in points (10 is the usual dialogue size) - what the size box lists and Bigger/Smaller step through.</summary>
    public static IReadOnlyList<double> SizeSteps { get; } = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>Why a size was refused: Word's range.</summary>
    public static string SizeRangeError { get; } = $"Font size must be between {MinFontSizePt} and {MaxFontSizePt} points.";

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
        if (!IsValidSize(style.FontSizePt))
            return EditResult<TextElement>.Failure(SizeRangeError);
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

    public static bool IsValidSize(double points) => double.IsFinite(points) && points >= MinFontSizePt && points <= MaxFontSizePt;

    /// <summary>
    /// A size typed into the font size box, in points as in Word: "12", "10.5", "10,5" or
    /// "12 pt", rounded to the half point like Word's; "5 mm" is taken too, and turned into
    /// points. Refused, with a reason, when it isn't a number or Word wouldn't take it.
    /// </summary>
    public static EditResult<double> ParseSize(string? entry)
    {
        var text = (entry ?? string.Empty).Trim();
        var millimetres = false;
        if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2].TrimEnd();
        }
        else if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2].TrimEnd();
            millimetres = true;
        }
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size))
            return EditResult<double>.Failure("Type a font size in points, like 12.");
        if (millimetres)
            size = FontPoints.FromMm(size);
        size = Math.Round(size * 2, MidpointRounding.AwayFromZero) / 2;
        return IsValidSize(size) ? EditResult<double>.Success(size) : EditResult<double>.Failure(SizeRangeError);
    }

    /// <summary>
    /// Word's Grow Font: the next size up <see cref="SizeSteps"/>; below it, a point at a
    /// time; above it, to the next ten, up to <see cref="MaxFontSizePt"/>.
    /// </summary>
    public static double Bigger(double size)
    {
        if (size < SizeSteps[0])
            return Math.Min(Math.Floor(size) + 1, SizeSteps[0]);
        var next = SizeSteps.FirstOrDefault(s => s > size + 1e-6);
        return next > 0 ? next : Math.Min(Math.Floor(size / 10 + 1e-9) * 10 + 10, MaxFontSizePt);
    }

    /// <summary>Word's Shrink Font: the next size down <see cref="SizeSteps"/>; below it, a point at a time down to <see cref="MinFontSizePt"/>; above it, to the ten below, down to the list's last size.</summary>
    public static double Smaller(double size)
    {
        if (size <= SizeSteps[0] + 1e-6)
            return Math.Max(Math.Ceiling(size - 1e-9) - 1, MinFontSizePt);
        var last = SizeSteps[^1];
        if (size > last + 1e-6)
            return Math.Max(Math.Ceiling(size / 10 - 1e-9) * 10 - 10, last);
        return SizeSteps.Last(s => s < size - 1e-6);
    }
}
