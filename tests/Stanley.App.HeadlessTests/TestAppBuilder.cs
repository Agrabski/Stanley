using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Stanley.App;
using Stanley.App.HeadlessTests;

[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Stanley.App.HeadlessTests;

public class TestAppBuilder
{
    /// <summary><c>UseHeadlessDrawing = false</c> so the real Skia backend runs (needed for <see cref="HeadlessWindowExtensions.CaptureRenderedFrame"/> to return actual pixels of our custom draw operation, not a stub).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
