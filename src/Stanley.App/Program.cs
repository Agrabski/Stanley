using System.CommandLine;
using System.Runtime.InteropServices;
using Avalonia;
using Stanley.App.Commands;
using Stanley.App.Diagnostics;
using Velopack;

namespace Stanley.App;

internal static class Program
{
    // No args -> launch the GUI, exactly as before. Any args -> dispatch through
    // System.CommandLine instead (e.g. `stanley init ...`), without ever touching
    // Avalonia/the windowing system, so CLI use works headlessly (CI, no display server).
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: Velopack intercepts its own install/update/uninstall lifecycle
        // through specific recognised args and returns immediately for anything else, so it
        // needs first refusal on `args` before the no-args-vs-CLI dispatch below ever sees them.
        VelopackApp.Build().Run();

        if (args.Length == 0)
            return RunGui(args);

        var root = new RootCommand("Stanley - a comic editor.");
        root.Add(InitCommand.Build());
        return root.Parse(args).Invoke();
    }

    private static int RunGui(string[] args)
    {
        AppLog.Initialize(AppPaths.LogsDirectory);
        AppLog.Info($"Stanley {typeof(Program).Assembly.GetName().Version} starting on {RuntimeInformation.OSDescription} " +
                    $"({RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}); data in {AppPaths.DataDirectory}.");

        // Last line of defence: whatever escapes, get it into the log (and the open
        // comic into a recovery snapshot - see App) before the process goes.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error($"Unhandled exception (terminating: {e.IsTerminating}).", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLog.Info($"Stanley exited with code {exitCode}.");
            return exitCode;
        }
        catch (Exception e)
        {
            AppLog.Error("Stanley crashed.", e);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
