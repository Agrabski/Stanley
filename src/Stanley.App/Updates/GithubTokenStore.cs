using Stanley.App.Diagnostics;

namespace Stanley.App.Updates;

/// <summary>
/// The user's own GitHub personal access token, optionally used to check for and download
/// updates - Stanley is a public repository, so update checks work anonymously without one;
/// a token only helps get past GitHub's anonymous rate limit or point at a private fork.
/// Kept out of <see cref="Documents.AppSettings"/> (a plain-text preferences file meant to be
/// freely read/copied) in its own file, written owner-only where the platform supports it
/// (POSIX; this doesn't touch Windows ACLs). A missing or unreadable file means no token,
/// same as a settings file that isn't there yet.
/// </summary>
public sealed class GithubTokenStore
{
    private readonly string? _path;
    private string? _token;

    /// <param name="path">Where to persist; null keeps the token in memory only (tests).</param>
    public GithubTokenStore(string? path)
    {
        _path = path;
        if (path is null)
            return;

        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } text)
                _token = text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string? Token
    {
        get => _token;
        set
        {
            _token = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_path is null)
                return;

            try
            {
                if (_token is null)
                {
                    File.Delete(_path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, _token);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Couldn't save the GitHub token to {_path}", e);
            }
        }
    }
}
