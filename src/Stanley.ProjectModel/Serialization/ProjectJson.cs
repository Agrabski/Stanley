using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stanley.ProjectModel.Serialization;

/// <summary>
/// Reads/writes one entity's JSON file using <see cref="ProjectJsonOptions"/>, always
/// through a <see cref="JsonTypeInfo{T}"/> (never the reflection-based
/// <c>Serialize&lt;T&gt;(value, options)</c> overload) so this stays trim/AOT safe.
/// </summary>
public static class ProjectJson
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, TypeInfo<T>());
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, json + "\n", Utf8NoBom);
    }

    public static T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, TypeInfo<T>()) ??
               throw new JsonException($"'{path}' deserialized to null; expected a {typeof(T).Name}.");
    }

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)ProjectJsonOptions.Value.GetTypeInfo(typeof(T));
}
