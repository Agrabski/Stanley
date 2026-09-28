using Avalonia;
using Avalonia.Styling;
using Stanley.App.Documents;

namespace Stanley.App;

/// <summary>Applies the <see cref="AppTheme"/> preference to the running app. A no-op without one (the CLI and plain unit tests).</summary>
public static class ThemeSwitcher
{
    public static ThemeVariant ToVariant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default, // follow the operating system
    };

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(theme);
    }
}
