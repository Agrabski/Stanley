using System.CommandLine;
using Avalonia;
using Stanley.App.Commands;

namespace Stanley.App;

internal static class Program
{
    // No args -> launch the GUI, exactly as before. Any args -> dispatch through
    // System.CommandLine instead (e.g. `stanley init ...`), without ever touching
    // Avalonia/the windowing system, so CLI use works headlessly (CI, no display server).
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        var root = new RootCommand("Stanley - a comic editor.");
        root.Add(InitCommand.Build());
        return root.Parse(args).Invoke();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
