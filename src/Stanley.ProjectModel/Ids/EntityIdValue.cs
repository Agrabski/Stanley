using System.Security.Cryptography;

namespace Stanley.ProjectModel.Ids;

/// <summary>
/// Shared validation/generation logic for every strong id's opaque token. Kept in one
/// place so every id type enforces the same "safe to embed in a filename" rule.
/// </summary>
internal static class EntityIdValue
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string Validate(string value, string paramName)
    {
        if (TryValidate(value, out var validated))
            return validated;

        throw new ArgumentException(
            $"'{value}' is not a valid entity id: it must be non-empty, contain no whitespace, " +
            "contain no character that is illegal in a file name, and contain no '-' (reserved as " +
            "the delimiter between an id and its folder/file name's cosmetic slug, so an id can " +
            "never be a false-positive prefix match for another, longer id).",
            paramName);
    }

    public static bool TryValidate(string? value, out string validated)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.IndexOfAny(InvalidChars) < 0)
        {
            validated = value;
            return true;
        }

        validated = string.Empty;
        return false;
    }

    /// <summary>A short, opaque, filename-safe random token used as a new entity's id.</summary>
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);

        var token = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            token[i] = Alphabet[bytes[i] % Alphabet.Length];

        return new string(token);
    }

    private static readonly char[] InvalidChars = BuildInvalidChars();

    private static char[] BuildInvalidChars()
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\', ' ', '-' };
        return [.. invalid];
    }
}
