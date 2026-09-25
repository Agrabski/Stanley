using System.Text.Json.Serialization;
using Stanley.ProjectModel.Backgrounds;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.ProjectModel.Props;

namespace Stanley.ProjectModel.Serialization;

/// <summary>
/// Source-generated serialization metadata for every JSON file the project format
/// writes, so no part of (de)serialization relies on runtime reflection - required for
/// NativeAOT. Property ordering/casing here is a starting point only; the actual
/// git-friendliness conventions (alphabetical keys, indent, trailing newline) are
/// applied on top by <see cref="ProjectJsonOptions"/>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeriesManifest))]
[JsonSerializable(typeof(CharacterDefinition))]
[JsonSerializable(typeof(CharacterRevision))]
[JsonSerializable(typeof(Sticker))]
[JsonSerializable(typeof(CharacterInstanceOverrides))]
[JsonSerializable(typeof(Pose))]
[JsonSerializable(typeof(Prop))]
[JsonSerializable(typeof(Background))]
[JsonSerializable(typeof(BackgroundRevision))]
[JsonSerializable(typeof(Issue))]
[JsonSerializable(typeof(Page))]
[JsonSerializable(typeof(Panel))]
public sealed partial class StanleyJsonContext : JsonSerializerContext;
