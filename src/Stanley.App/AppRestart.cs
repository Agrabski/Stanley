using System.Diagnostics;
using System.Reflection;
using Stanley.App.Diagnostics;

namespace Stanley.App;

/// <summary>Starts a fresh copy of Stanley, for Options' "Restart now" (a new layout applies at startup).</summary>
public static class AppRestart
{
    /// <summary>
    /// Starts Stanley again, with no arguments so it opens the window rather than the CLI. Inside
    /// an AppImage that's the AppImage itself (<c>APPIMAGE</c>), not the binary mounted from it;
    /// run as <c>dotnet stanley.dll</c> it's the same dll again. Never throws: if it can't, the
    /// user starts Stanley themselves, as they would have anyway.
    /// </summary>
    public static void StartNewInstance()
    {
        try
        {
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            var path = !string.IsNullOrEmpty(appImage) ? appImage : Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
                return;

            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                && Assembly.GetEntryAssembly()?.Location is { Length: > 0 } entry)
                start.ArgumentList.Add(entry);
            Process.Start(start);
            AppLog.Info($"Restarting: started {path}.");
        }
        catch (Exception e)
        {
            AppLog.Warn("Couldn't start Stanley again after closing; it has to be started by hand.", e);
        }
    }
}
