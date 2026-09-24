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
    /// Writes <paramref name="text"/> to a file named <paramref name="fileName"/>, opens it
    /// in the system's editor for SVG files, and calls <paramref name="saved"/> (on the UI
    /// thread) with the file's new text each time it's saved, until the result is disposed.
    /// Returns the file's path, or null (with the reason) if it couldn't be written.
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
/// with whatever the desktop uses for SVG (<c>xdg-open</c> and friends, through the
/// shell), and a <see cref="FileSystemWatcher"/> brings saves back - debounced, since
/// editors often write a file in several steps or replace it by renaming.
/// </summary>
public sealed class SystemArtEditing(string folder) : IArtEditing
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    public ArtEditSession? Edit(string fileName, string text, Action<string> saved, out string? error)
    {
        string path;
        try
        {
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, fileName);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }

        var last = text;
        var watcher = new FileSystemWatcher(folder, fileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
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
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            error = null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Nothing opens SVG files here: the file is still watched, so opening it by hand works.
            error = $"couldn't open an SVG editor ({e.Message}) - open {path} yourself; saves still come back";
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
