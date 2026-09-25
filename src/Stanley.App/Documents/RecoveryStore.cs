using System.Globalization;
using Stanley.App.Diagnostics;
using Stanley.Editors;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.Documents;

/// <summary>Unsaved work left behind by a Stanley session that didn't shut down properly.</summary>
public sealed record RecoveredComic(string SessionDirectory, string SnapshotDirectory, string Title, string? OriginalLocation, DateTime SavedAt)
{
    public string LocationText => OriginalLocation ?? "Never saved";
    public string SavedAtText => SavedAt.ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>
/// Crash recovery, after Word's AutoRecover. Each running session gets its own folder
/// under <see cref="AppPaths.RecoveryDirectory"/> holding an exclusively-locked
/// <c>session.lock</c> and, while there are unsaved changes, a snapshot of the comic
/// (a self-contained project written by <see cref="ComicProject.WriteCopy"/>) plus an
/// <c>info.txt</c>. A clean exit deletes the folder. The operating system releases the
/// lock when a process dies, however it dies - so on the next start, a session folder
/// whose lock can be taken belongs to a session that crashed, and its snapshot is
/// offered back.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private const string LockFileName = "session.lock";
    private const string InfoFileName = "info.txt";
    private const string SnapshotDirName = "snapshot";

    private readonly string _root;
    private FileStream? _lock;

    public RecoveryStore(string rootDirectory) => _root = rootDirectory;

    /// <summary>This session's folder, once <see cref="StartSession"/> has run.</summary>
    public string? SessionDirectory { get; private set; }

    public bool HasSnapshot => SessionDirectory != null && Directory.Exists(Path.Combine(SessionDirectory, SnapshotDirName));

    public void StartSession()
    {
        if (SessionDirectory != null)
            return;

        try
        {
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}"[..40];
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            _lock = new FileStream(Path.Combine(dir, LockFileName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            SessionDirectory = dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Crash recovery is unavailable: couldn't create a recovery session.", e);
        }
    }

    /// <summary>Replaces this session's snapshot with the comic's current state. Written beside the old one, then swapped in, so a crash mid-write never leaves nothing.</summary>
    public void Write(ComicProject project, IReadOnlyList<(PageId Id, Editors.PageDocument Document)> pages, PageNumbering numbering,
        IReadOnlyList<ProjectModel.Characters.CharacterDefinition>? characters = null,
        IReadOnlyDictionary<ProjectModel.Ids.CharacterId, ProjectModel.Ids.CharacterRevisionId>? issueLooks = null)
    {
        if (SessionDirectory is not { } dir)
            return;

        var fresh = Path.Combine(dir, SnapshotDirName + ".new");
        var current = Path.Combine(dir, SnapshotDirName);
        if (Directory.Exists(fresh))
            Directory.Delete(fresh, recursive: true);

        project.WriteCopy(fresh, pages, numbering, characters, issueLooks);
        if (Directory.Exists(current))
            Directory.Delete(current, recursive: true);
        Directory.Move(fresh, current);

        File.WriteAllLines(Path.Combine(dir, InfoFileName),
        [
            $"title={OneLine(project.Title)}",
            $"location={OneLine(project.Location ?? "")}",
            $"savedAt={DateTime.Now.ToString("o", CultureInfo.InvariantCulture)}"
        ]);
    }

    /// <summary>Drops this session's snapshot - there's nothing unsaved any more.</summary>
    public void Clear()
    {
        if (SessionDirectory is not { } dir)
            return;
        TryDelete(Path.Combine(dir, SnapshotDirName));
        TryDelete(Path.Combine(dir, SnapshotDirName + ".new"));
        try
        {
            File.Delete(Path.Combine(dir, InfoFileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Snapshots left by sessions that are no longer running. Their empty folders (a crash with nothing unsaved) are cleaned up on the way.</summary>
    public IReadOnlyList<RecoveredComic> FindAbandoned()
    {
        var found = new List<RecoveredComic>();
        if (!Directory.Exists(_root))
            return found;

        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            if (string.Equals(dir, SessionDirectory, StringComparison.Ordinal) || IsAlive(dir))
                continue;

            var snapshot = Path.Combine(dir, SnapshotDirName);
            var info = ReadInfo(Path.Combine(dir, InfoFileName));
            if (info is null || !Directory.Exists(snapshot))
            {
                TryDelete(dir);
                continue;
            }

            found.Add(new RecoveredComic(dir, snapshot,
                info.GetValueOrDefault("title", ComicProject.UntitledTitle),
                info.GetValueOrDefault("location") is { Length: > 0 } location ? location : null,
                DateTime.TryParse(info.GetValueOrDefault("savedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : Directory.GetLastWriteTime(dir)));
        }
        return found.OrderByDescending(r => r.SavedAt).ToList();
    }

    public void Discard(RecoveredComic comic) => TryDelete(comic.SessionDirectory);

    /// <summary>Clean exit: release the lock and remove this session's folder, snapshot and all.</summary>
    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
        if (SessionDirectory != null)
            TryDelete(SessionDirectory);
        SessionDirectory = null;
    }

    /// <summary>For tests: what a crash leaves behind - the lock released by the OS, the folder still there.</summary>
    internal void AbandonForTests()
    {
        _lock?.Dispose();
        _lock = null;
        SessionDirectory = null;
    }

    /// <summary>A session is alive while its process holds the lock file exclusively.</summary>
    private static bool IsAlive(string sessionDir)
    {
        var lockPath = Path.Combine(sessionDir, LockFileName);
        if (!File.Exists(lockPath))
            return false;
        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // can't tell - leave it alone
        }
    }

    private static Dictionary<string, string>? ReadInfo(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return File.ReadAllLines(path)
                .Select(l => l.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Couldn't remove recovery data at {path}", e);
        }
    }
}
