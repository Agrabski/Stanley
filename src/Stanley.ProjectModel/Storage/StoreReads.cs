using System.Text.Json;

namespace Stanley.ProjectModel.Storage;

/// <summary>Reading one entity folder or file for a listing, optionally skipping one that's damaged instead of failing the whole list.</summary>
internal static class StoreReads
{
    public static T? Read<T>(string path, Func<string, T> read, bool skipUnreadable) where T : class
    {
        if (!skipUnreadable)
            return read(path);
        try
        {
            return read(path);
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return null;
        }
    }

    /// <summary>What a damaged or half-written file throws: I/O and permission problems, bad JSON, and bad ids or values in otherwise valid JSON.</summary>
    public static bool IsUnreadable(Exception e) =>
        e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or ArgumentException or NotSupportedException;
}
