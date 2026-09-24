using System.Text.Json.Serialization;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Issues;

[JsonConverter(typeof(CamelCaseEnumConverter<PageNumberPosition>))]
public enum PageNumberPosition
{
    /// <summary>No page numbers printed.</summary>
    None,
    BottomCenter,

    /// <summary>The bottom corner away from the spine: right on right-hand (odd) pages, left on left-hand (even) pages.</summary>
    BottomOuter,

    /// <summary>The top corner away from the spine.</summary>
    TopOuter
}

/// <summary>
/// How an issue prints its page numbers (folios). <see cref="StartAt"/> is the number the
/// first page would carry, so a book that starts inside a larger run can continue its
/// numbering; <see cref="NumberFirstPage"/> is off for the usual unnumbered cover, which
/// still counts towards the numbering.
/// </summary>
public sealed record PageNumbering(PageNumberPosition Position, int StartAt = 1, bool NumberFirstPage = false)
{
    public static PageNumbering Off { get; } = new(PageNumberPosition.None);
}
