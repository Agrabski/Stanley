using Stanley.ProjectModel.Characters;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Reads and writes a flat set of <see cref="ArtFile"/>s under one folder, shared by every
/// per-kind store (<see cref="CharacterStore"/>'s stickers and pattern tiles,
/// <see cref="ObjectGroupStore"/>'s <c>art/</c>) so the "write only what changed, delete
/// what's gone" logic lives in one place.
/// </summary>
internal static class ArtFileIO
{
    public static ArtFile ReadArtFile(string path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase)
            ? ArtFile.Svg(File.ReadAllText(path))
            : ArtFile.Png(File.ReadAllBytes(path));

    /// <summary>Writes <paramref name="files"/> (relative paths) under <paramref name="dir"/> where their content differs, and deletes every other file there except <paramref name="keep"/>.</summary>
    public static void WriteFiles(string dir, IReadOnlyDictionary<string, ArtFile> files, string? keep)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (relative, file) in files)
        {
            var path = Path.GetFullPath(Path.Combine(dir, relative));
            if (!path.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue; // never write outside the folder, whatever a hand-edited name says
            wanted.Add(path);
            if (File.Exists(path) && ReadArtFile(path).SameContent(file))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.ToBytes());
        }
        foreach (var existing in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
        {
            var path = Path.GetFullPath(existing);
            if (!wanted.Contains(path) && (keep is null || path != Path.GetFullPath(keep)))
                File.Delete(path);
        }
        foreach (var empty in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length)
                     .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
                     .ToList())
            Directory.Delete(empty);
    }

    /// <summary>Every file directly under <paramref name="dir"/> (non-recursive), keyed by file name - for a flat art folder like an object group's or a character's pattern tiles.</summary>
    public static IReadOnlyDictionary<string, ArtFile> ReadFlat(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).ToDictionary(f => Path.GetFileName(f), ReadArtFile, StringComparer.Ordinal)
            : new Dictionary<string, ArtFile>(StringComparer.Ordinal);
}
