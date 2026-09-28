using System.Diagnostics;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>
/// Editing sticker art in the user's own SVG editor (docs/sticker-system.md §13.2): the
/// art is written to a file, the file is opened, and every save there comes back. Behind
/// an interface so tests can play the editor.
/// </summary>
public interface IArtEditing
{
    /// <summary>
    /// The SVG editor to open drawings in - an executable's path, or null until the user has
    /// chosen one (File &gt; Options &gt; SVG editor, or the first-run picker). Stanley never
    /// guesses at the operating system's default app for <c>.svg</c> files, which is often
    /// just a viewer.
    /// </summary>
    string? EditorPath { get; set; }

    /// <summary>
    /// Writes <paramref name="text"/> to a file named <paramref name="fileName"/>, opens it in
    /// <see cref="EditorPath"/>, and calls <paramref name="saved"/> (on the UI thread) with the
    /// file's new text each time it's saved, until the result is disposed. Returns the file's
    /// path, or null (with the reason) if it couldn't be written or opened. Callers should check
    /// <see cref="EditorPath"/> first and ask the user to set one rather than relying on the
    /// error this gives when it's still unset.
    /// </summary>
    ArtEditSession? Edit(string fileName, string text, Action<string> saved, out string? error);
}

/// <summary>One file being edited outside Stanley; dispose it to stop listening.</summary>
public sealed class ArtEditSession(string path, IDisposable? watch) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose() => watch?.Dispose();
}

/// <summary>
/// The real thing: files go in a folder of their own (under the app's data folder), open
/// directly in <see cref="EditorPath"/> (never a shell/file-association guess - see
/// <see cref="IArtEditing.EditorPath"/>), and a <see cref="FileSystemWatcher"/> brings saves
/// back - debounced, since editors often write a file in several steps or replace it by
/// renaming.
/// </summary>
public sealed class SystemArtEditing : IArtEditing
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly string _folder;
    private readonly Func<string?> _getEditorPath;
    private readonly Action<string?> _setEditorPath;

    /// <param name="folder">Where drawing files are written and watched.</param>
    /// <param name="getEditorPath">Reads the persisted editor path (<see cref="Documents.AppSettings.SvgEditorPath"/> in the real app); omit to keep it in memory only (tests, or a character editor opened without a project).</param>
    /// <param name="setEditorPath">Persists a newly chosen editor path.</param>
    public SystemArtEditing(string folder, Func<string?>? getEditorPath = null, Action<string?>? setEditorPath = null)
    {
        _folder = folder;
        string? memory = null;
        _getEditorPath = getEditorPath ?? (() => memory);
        _setEditorPath = setEditorPath ?? (v => memory = v);
    }

    public string? EditorPath
    {
        get => _getEditorPath();
        set => _setEditorPath(value);
    }

    public ArtEditSession? Edit(string fileName, string text, Action<string> saved, out string? error)
    {
        if (EditorPath is not { Length: > 0 } editorPath)
        {
            error = "no SVG editor is set up (File › Options › SVG editor)";
            return null;
        }

        string path;
        try
        {
            Directory.CreateDirectory(_folder);
            path = Path.Combine(_folder, fileName);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }

        var last = text;
        var watcher = new FileSystemWatcher(_folder, fileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        Timer? pending = null;
        void Changed()
        {
            pending?.Dispose();
            pending = new Timer(_ =>
            {
                string now;
                try
                {
                    now = File.ReadAllText(path);
                }
                catch (IOException)
                {
                    return; // still being written; the next change event tries again
                }
                if (now.Length == 0 || now == last)
                    return;
                last = now;
                Dispatcher.UIThread.Post(() => saved(now));
            }, null, Settle, Timeout.InfiniteTimeSpan);
        }
        watcher.Changed += (_, _) => Changed();
        watcher.Created += (_, _) => Changed();
        watcher.Renamed += (_, e) =>
        {
            if (string.Equals(e.Name, fileName, StringComparison.Ordinal))
                Changed();
        };
        watcher.EnableRaisingEvents = true;

        try
        {
            var start = new ProcessStartInfo(editorPath) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
            error = null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // The configured editor didn't start: the file is still watched, so opening it by hand works.
            error = $"couldn't start {editorPath} ({e.Message}) - open {path} yourself; saves still come back";
        }
        return new ArtEditSession(path, new Disposables(watcher, () => pending?.Dispose()));
    }

    private sealed class Disposables(IDisposable first, Action then) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            then();
        }
    }
}
