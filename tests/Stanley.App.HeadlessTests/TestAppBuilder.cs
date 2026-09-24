using Avalonia;
using Avalonia.Headless;
using Stanley.App;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Provides static utility methods for building the Avalonia app in headless/test contexts.
/// The actual test framework initialization is now handled via IAsyncLifetime in individual test classes.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Builds an AppBuilder configured for headless testing with real Skia rendering.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
