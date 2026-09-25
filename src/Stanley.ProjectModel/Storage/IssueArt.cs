using System.Security.Cryptography;
using Stanley.ProjectModel.Characters;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Names for the pictures a comic's panels use, kept in the issue's <c>art/</c> folder.
/// A picture is named after its content - a hash plus its type's extension - so importing
/// the same picture twice keeps one file, two different pictures never collide, and the
/// name alone says whether a file on disk is still the picture a panel means.
/// </summary>
public static class IssueArt
{
    /// <summary>The picture types a panel can use, by extension (lower case, no dot).</summary>
    public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.Ordinal) { "png", "jpg", "jpeg", "webp", "gif", "bmp", "svg" };

    /// <summary>The name <paramref name="file"/> is stored under: the first 16 hex digits of its content's SHA-256, then <paramref name="extension"/>.</summary>
    public static string NameFor(ArtFile file, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (!Extensions.Contains(ext))
            throw new ArgumentException($"'{extension}' isn't a picture type a panel can use.", nameof(extension));
        var hash = SHA256.HashData(file.ToBytes());
        return Convert.ToHexStringLower(hash.AsSpan(0, 8)) + "." + ext;
    }

    /// <summary>Whether <paramref name="name"/> is a plain file name of a supported picture type - never a path, so a hand-edited panel file can't point outside the art folder.</summary>
    public static bool IsValidName(string name) =>
        name.Length > 0 && name.IndexOfAny(['/', '\\']) < 0 && name != "." && name != ".."
        && Path.GetFileName(name) == name && Extensions.Contains(Path.GetExtension(name).TrimStart('.').ToLowerInvariant());

    public static bool IsSvg(string name) => string.Equals(Path.GetExtension(name), ".svg", StringComparison.OrdinalIgnoreCase);
}
