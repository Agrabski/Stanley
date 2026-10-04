using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Stanley.Editors;

/// <summary>Turns the name of an icon geometry in the app's resources (<c>SpeechIcon</c>) into the geometry, for a row's <c>PathIcon</c>.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static IconKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var resource))
            return resource as Geometry;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
