using System.Globalization;
using Stanley.App.Updates;

namespace Stanley.App.Documents;

/// <summary>Light or dark UI. <see cref="System"/> follows the operating system's setting, so most people never need to touch it.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// Per-user preferences, as <c>key=value</c> lines (trivially AOT-safe, readable by hand).
/// Unknown keys are kept, so an older version doesn't drop a newer one's settings. A
/// missing or unreadable file means defaults.
/// </summary>
public sealed class AppSettings
{
    private readonly string? _path;
    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <param name="path">Where to persist; null keeps settings in memory only (tests).</param>
    public AppSettings(string? path)
    {
        _path = path;
        if (path is null)
            return;

        try
        {
            if (!File.Exists(path))
                return;
            foreach (var line in File.ReadAllLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                    _values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Save automatically a moment after each change, once the comic has a folder. On by default - losing work is worse than an extra write.</summary>
    public bool AutoSave
    {
        get => !_values.TryGetValue(nameof(AutoSave), out var value) || !bool.TryParse(value, out var on) || on;
        set => Set(nameof(AutoSave), value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Light or dark UI; follows the system unless changed.</summary>
    public AppTheme Theme
    {
        get => _values.TryGetValue(nameof(Theme), out var value) && Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme) && Enum.IsDefined(theme)
            ? theme
            : AppTheme.System;
        set => Set(nameof(Theme), value.ToString());
    }

    /// <summary>File &gt; Options &gt; Updates: check automatically on startup. Off by default - Stanley
    /// doesn't reach out to GitHub on its own until the user opts in.</summary>
    public bool AutoCheckForUpdates
    {
        get => _values.TryGetValue(nameof(AutoCheckForUpdates), out var value) && bool.TryParse(value, out var on) && on;
        set => Set(nameof(AutoCheckForUpdates), value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Stable (tagged releases only) or Nightly (every change to `develop`) release track.</summary>
    public AppUpdateChannel UpdateChannel
    {
        get => _values.TryGetValue(nameof(UpdateChannel), out var value) && Enum.TryParse<AppUpdateChannel>(value, ignoreCase: true, out var channel) && Enum.IsDefined(channel)
            ? channel
            : AppUpdateChannel.Stable;
        set => Set(nameof(UpdateChannel), value.ToString());
    }

    /// <summary>File &gt; Options &gt; SVG editor: the program "Draw your own..."/"Edit drawing..." opens sticker art in.
    /// Null until the user sets one up - Stanley never guesses at the OS's default app for SVG files. Not a secret, so
    /// this plain preferences file (rather than <see cref="Updates.GithubTokenStore"/>'s owner-only one) is fine for it.</summary>
    public string? SvgEditorPath
    {
        get => _values.TryGetValue(nameof(SvgEditorPath), out var value) && value.Length > 0 ? value : null;
        set => Set(nameof(SvgEditorPath), value?.Trim() ?? "");
    }

    /// <summary>File &gt; Options &gt; My Assets: a folder of the user's choosing for My Assets (a synced or backed-up one,
    /// or a git repository for its history). Null means the default, <see cref="AppPaths.MyAssetsDirectory"/>.</summary>
    public string? MyAssetsDirectory
    {
        get => _values.TryGetValue(nameof(MyAssetsDirectory), out var value) && value.Length > 0 ? value : null;
        set => Set(nameof(MyAssetsDirectory), value?.Trim() ?? "");
    }

    private void Set(string key, string value)
    {
        _values[key] = value;
        if (_path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, _values.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Warn($"Couldn't save settings to {_path}", e);
        }
    }
}
