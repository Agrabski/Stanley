using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// What the ribbon's Font group sets - typeface, size, bold, italic and how lines line up -
/// for a bubble's lettering and free text alike, so one set of controls serves both.
/// </summary>
/// <param name="Family">The typeface family; null is the default lettering font.</param>
/// <param name="SizeMm">The letters' size in page millimetres.</param>
public sealed record LetteringFont(string? Family, double SizeMm, bool Bold, bool Italic, TextAlign Align)
{
    /// <summary>A bubble's lettering when nothing's been chosen: the default font at the usual dialogue size, upright and centred.</summary>
    public static LetteringFont BubbleDefault { get; } = new(null, Bubble.DefaultFontSizeMm, false, false, TextAlign.Center);

    public static LetteringFont Of(Bubble bubble) =>
        new(bubble.FontFamily, bubble.FontSizeMm ?? Bubble.DefaultFontSizeMm, bubble.Bold, bubble.Italic, bubble.Align ?? TextAlign.Center);

    public static LetteringFont Of(TextStyle style) => new(style.FontFamily, style.FontSizeMm, style.Bold, style.Italic, style.Align);

    /// <summary>The bubble lettered this way. What's the default is stored as nothing, so the bubble's file only mentions choices someone made.</summary>
    public Bubble ApplyTo(Bubble bubble) => bubble with
    {
        FontFamily = Family,
        FontSizeMm = Math.Abs(SizeMm - Bubble.DefaultFontSizeMm) < 1e-9 ? null : SizeMm,
        Bold = Bold,
        Italic = Italic,
        Align = Align == TextAlign.Center ? null : Align
    };

    public TextStyle ApplyTo(TextStyle style) => style with { FontFamily = Family, FontSizeMm = SizeMm, Bold = Bold, Italic = Italic, Align = Align };

    /// <summary>This font with its family name tidied (<see cref="TextEditing.CleanFontName"/>), or why it can't be used: a size letters can't be, or a name that isn't one.</summary>
    public EditResult<LetteringFont> Validate()
    {
        if (SizeMm < TextEditing.MinFontSizeMm || SizeMm > TextEditing.MaxFontSizeMm || !double.IsFinite(SizeMm))
            return EditResult<LetteringFont>.Failure($"Letters must be {TextEditing.MinFontSizeMm}-{TextEditing.MaxFontSizeMm}mm tall.");
        var family = TextEditing.CleanFontName(Family);
        return family.IsValid
            ? EditResult<LetteringFont>.Success(this with { Family = family.Value })
            : EditResult<LetteringFont>.Failure(family.Error!);
    }
}
