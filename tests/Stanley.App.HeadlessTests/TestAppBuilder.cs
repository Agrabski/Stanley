using Avalonia;
using Avalonia.Headless;
using Stanley.App;
using Stanley.Editors;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Provides static utility methods for building the Avalonia app in headless/test contexts.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Builds an AppBuilder configured for headless testing with real Skia rendering. Points Stanley's per-user data (settings, recent list, crash recovery) at a throwaway folder, so tests never touch the real profile.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable,
            Path.Combine(Path.GetTempPath(), "stanley-headless-data-" + Guid.NewGuid().ToString("N")));
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithLetteringFonts()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
