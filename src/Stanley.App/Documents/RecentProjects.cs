namespace Stanley.App.Documents;

/// <summary>
/// The File &gt; Open "Recent" list: most recently opened/saved project folders first,
/// persisted as a plain one-path-per-line text file (trivially AOT-safe, and readable if
/// anyone goes looking). A missing or unreadable file just means an empty list.
/// </summary>
public sealed class RecentProjects
{
    public const int MaxEntries = 10;

    private readonly string? _storePath;
    private readonly List<string> _paths = [];

    /// <param name="storePath">Where to persist the list; null keeps it in memory only (tests).</param>
    public RecentProjects(string? storePath)
    {
        _storePath = storePath;
        if (storePath is null)
            return;

        try
        {
            if (File.Exists(storePath))
                _paths.AddRange(File.ReadAllLines(storePath).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().Take(MaxEntries));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string DefaultStorePath =>
        AppPaths.RecentProjectsFile;

    public IReadOnlyList<string> Paths => _paths;

    public void Add(string path)
    {
        var full = Path.GetFullPath(path);
        _paths.Remove(full);
        _paths.Insert(0, full);
        if (_paths.Count > MaxEntries)
            _paths.RemoveRange(MaxEntries, _paths.Count - MaxEntries);
        Persist();
    }

    public void Remove(string path)
    {
        if (_paths.Remove(path))
            Persist();
    }

    private void Persist()
    {
        if (_storePath is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllLines(_storePath, _paths);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
