namespace Stanley.ProjectModel.Issues;

/// <summary>
/// Stanley's fields, like Word's: a name in braces inside a text - <c>{title}</c>,
/// <c>{issue}</c> - that shows the comic's own value wherever the text is drawn, printed
/// or exported. The text keeps the field, so it follows the comic when its title or issue
/// number changes, while the words around it are the user's own ("Wydanie #{issue}").
/// Names are matched ignoring case; anything else in braces is left as typed.
/// </summary>
/// <param name="Title">The comic's title (File › Info).</param>
/// <param name="Issue">The issue's number as displayed ("1", "0", "1.5").</param>
public sealed record TextFields(string Title, string Issue)
{
    public const string TitleField = "{title}";
    public const string IssueField = "{issue}";

    /// <summary><paramref name="text"/> with every field replaced by its value, in one pass (a title that itself contains "{issue}" stays as it is).</summary>
    public string Fill(string text)
    {
        if (!text.Contains('{', StringComparison.Ordinal))
            return text;

        var filled = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (At(text, i, TitleField))
            {
                filled.Append(Title);
                i += TitleField.Length;
            }
            else if (At(text, i, IssueField))
            {
                filled.Append(Issue);
                i += IssueField.Length;
            }
            else
            {
                filled.Append(text[i++]);
            }
        }
        return filled.ToString();
    }

    private static bool At(string text, int index, string field) =>
        index + field.Length <= text.Length && string.Compare(text, index, field, 0, field.Length, StringComparison.OrdinalIgnoreCase) == 0;
}
