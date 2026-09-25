namespace Stanley.Editors;

/// <summary>One program on this computer that can plausibly edit an SVG file.</summary>
public sealed record SvgEditorCandidate(string Name, string Path);

/// <summary>
/// Suggestions for File &gt; Options &gt; SVG editor and the first-run picker
/// (<see cref="SvgEditorPicker"/>): a short, best-effort search of <c>PATH</c> for a handful
/// of well-known vector editors. It's not exhaustive - a Flatpak/Snap install under its own
/// launcher, an AppImage, or anything else not on <c>PATH</c> won't show up here - so
/// "Browse..." to any executable always works regardless of what this finds.
/// </summary>
public static class SvgEditorCandidates
{
    // (display name, executable names to look for, in order - first match wins)
    private static readonly (string Name, string[] ExecutableNames)[] Known =
    [
        ("Inkscape", ["inkscape"]),
        ("Karbon", ["karbon", "karbon5"]),
        ("Boxy SVG", ["boxy-svg", "boxysvg"]),
        ("sK1", ["sk1"]),
    ];

    /// <summary>What's on <c>PATH</c> right now, in the order above.</summary>
    public static IReadOnlyList<SvgEditorCandidate> Detect()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var found = new List<SvgEditorCandidate>();
        foreach (var (name, executableNames) in Known)
        {
            foreach (var executableName in executableNames)
            {
                if (FindOnPath(directories, executableName) is { } path)
                {
                    found.Add(new SvgEditorCandidate(name, path));
                    break;
                }
            }
        }
        return found;
    }

    private static string? FindOnPath(string[] directories, string executableName)
    {
        var names = OperatingSystem.IsWindows() ? [executableName + ".exe"] : new[] { executableName };
        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
