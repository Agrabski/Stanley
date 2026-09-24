using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stanley.ProjectModel.Ids;

/// <summary>
/// Serializes any <see cref="IStrongId{TSelf}"/> as its underlying string, both as a
/// value and as a JSON object property name (needed for id-keyed maps such as
/// <c>Issue.CharacterRevisions</c>). Uses no reflection, so it is safe under the
/// source-generated <see cref="JsonSerializerContext"/> used for AOT.
/// </summary>
public sealed class StrongIdJsonConverter<TId> : JsonConverter<TId> where TId : struct, IStrongId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TId.FromValue(reader.GetString() ?? throw new JsonException($"Expected a string value for {typeof(TId).Name}."));

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TId.FromValue(reader.GetString() ?? throw new JsonException($"Expected a string property name for {typeof(TId).Name}."));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value);
}
