using System.Globalization;
using System.Text;

namespace Stanley.App.Diagnostics;

/// <summary>
/// A plain-text log file per day (<c>Logs/stanley-yyyy-MM-dd.log</c> under
/// <see cref="AppPaths.DataDirectory"/>), for "what happened before it went wrong" when a
/// user reports a problem. Every line is appended and closed immediately, so nothing is
/// lost in a crash. Old files are pruned after <see cref="RetentionDays"/> days. Until
/// <see cref="Initialize"/> is called (tests, the CLI) logging is a no-op.
/// </summary>
public static class AppLog
{
    public const int RetentionDays = 14;

    private static readonly Lock Gate = new();
    private static string? _directory;

    /// <summary>Today's log file, or null when logging isn't initialised.</summary>
    public static string? CurrentFile => _directory is null ? null : FileFor(DateTime.Now);

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _directory = directory;
            PruneOldFiles(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _directory = null; // no log is better than no app
        }
    }

    /// <summary>Stops logging (tests that initialised it into a temporary folder).</summary>
    internal static void Disable() => _directory = null;

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_directory is null)
            return;

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (exception != null)
            line.AppendLine().Append(exception);
        line.AppendLine();

        lock (Gate)
        {
            try
            {
                File.AppendAllText(FileFor(DateTime.Now), line.ToString());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Logging must never be the thing that breaks the app.
            }
        }
    }

    private static string FileFor(DateTime day) =>
        Path.Combine(_directory!, $"stanley-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");

    private static void PruneOldFiles(string directory)
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, "stanley-*.log"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
                File.Delete(file);
        }
    }
}
