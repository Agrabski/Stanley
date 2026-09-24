using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// A colour slot value: <c>#RRGGBB</c> or <c>#RRGGBBAA</c>, validated and normalized to
/// lowercase on construction so the same colour always serializes identically.
/// </summary>
[JsonConverter(typeof(ColorValueJsonConverter))]
public readonly record struct ColorValue : IParsable<ColorValue>
{
    public string Hex { get; }

    private ColorValue(string hex) => Hex = hex;

    public static ColorValue FromHex(string hex) => new(Validate(hex, nameof(hex)));

    public static ColorValue Parse(string s, IFormatProvider? provider = null) => FromHex(s);

    public static bool TryParse(string? s, IFormatProvider? provider, out ColorValue result)
    {
        if (s is not null && TryNormalize(s, out var normalized))
        {
            result = new ColorValue(normalized);
            return true;
        }

        result = default;
        return false;
    }

    public override string ToString() => Hex;

    private static string Validate(string hex, string paramName)
    {
        if (TryNormalize(hex, out var normalized))
            return normalized;

        throw new ArgumentException($"'{hex}' is not a valid #RRGGBB or #RRGGBBAA colour.", paramName);
    }

    private static bool TryNormalize(string hex, out string normalized)
    {
        if (hex.Length is 7 or 9 && hex[0] == '#' && IsHex(hex.AsSpan(1)))
        {
            normalized = hex.ToLowerInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool IsHex(ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}

public sealed class ColorValueJsonConverter : JsonConverter<ColorValue>
{
    public override ColorValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ColorValue.Parse(reader.GetString() ?? throw new JsonException("Expected a string colour value."), CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, ColorValue value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Hex);
}
