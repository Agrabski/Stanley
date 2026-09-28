namespace Stanley.App;

/// <summary>
/// Where Stanley keeps its own per-user files (settings, recent comics, logs, crash
/// recovery) - never inside a project folder. <c>STANLEY_DATA_DIR</c> overrides it, which
/// tests use so they never touch the real profile.
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "STANLEY_DATA_DIR";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stanley");

    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public static string RecoveryDirectory => Path.Combine(DataDirectory, "Recovery");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.txt");
    public static string RecentProjectsFile => Path.Combine(DataDirectory, "recent-projects.txt");

    /// <summary>
    /// The default My Assets folder (docs/asset-packs.md §7.2): <c>Documents/Stanley/My Assets</c>,
    /// somewhere people look for their own things and back up, unlike the hidden app-data
    /// folder. File › Options can move it (<see cref="Documents.AppSettings.MyAssetsDirectory"/>).
    /// With <c>STANLEY_DATA_DIR</c> set it lives in there instead, so tests never touch it.
    /// </summary>
    public static string MyAssetsDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? Path.Combine(overridden, "My Assets")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stanley", "My Assets");

    /// <summary>Where sticker art being drawn in the user's SVG editor ("Draw your own") is written and watched.</summary>
    public static string ArtEditingDirectory => Path.Combine(DataDirectory, "Drawing");

    /// <summary>The user's own, optional GitHub personal access token for update checks (see <see cref="Updates.GithubTokenStore"/>) - kept separate from <see cref="SettingsFile"/>, which is plain preferences meant to be freely read.</summary>
    public static string GithubTokenFile => Path.Combine(DataDirectory, "github-token.txt");
}
