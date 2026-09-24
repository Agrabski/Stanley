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
}
