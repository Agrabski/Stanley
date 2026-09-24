using System.Globalization;

namespace Stanley.App.Documents;

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
