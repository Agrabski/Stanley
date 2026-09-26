using System.Globalization;

namespace Stanley.Editing;

/// <summary>Picking a sensible default number for a new issue - "New issue" needs no dialog to use.</summary>
public static class IssueNumbering
{
    /// <summary>
    /// One past the highest number among <paramref name="existingNumbers"/> that parses as a
    /// plain integer ("2" after "1", "2" after "1" and "1.5" - the annual doesn't move the
    /// count on). With none that parse (a first issue, or numbers like "Annual"/"Special"),
    /// falls back to one past how many issues there already are.
    /// </summary>
    public static string NextNumber(IEnumerable<string> existingNumbers)
    {
        var numbers = existingNumbers as IReadOnlyCollection<string> ?? existingNumbers.ToList();
        var parsed = numbers
            .Select(n => int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (int?)value : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return (parsed.Count > 0 ? parsed.Max() + 1 : numbers.Count + 1).ToString(CultureInfo.InvariantCulture);
    }
}
