using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stanley.ProjectModel.Serialization;

/// <summary>
/// The JSON conventions the whole project format follows
/// (docs/character-and-project-plan.md, "Git-friendliness rules"): 2-space indent
/// (System.Text.Json's default when <c>WriteIndented</c> is set), alphabetically sorted
/// object keys, and a trailing newline - so a no-op save produces an empty diff. Built
/// as a modifier on top of the source-generated <see cref="StanleyJsonContext"/> so
/// sorting adds no reflection and stays AOT/trim safe.
/// </summary>
public static class ProjectJsonOptions
{
    public static readonly JsonSerializerOptions Value = Build();

    private static JsonSerializerOptions Build() => new(StanleyJsonContext.Default.Options)
    {
        TypeInfoResolver = StanleyJsonContext.Default.WithAddedModifier(SortPropertiesAlphabetically)
    };

    private static void SortPropertiesAlphabetically(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        var sorted = typeInfo.Properties.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        typeInfo.Properties.Clear();
        foreach (var property in sorted)
            typeInfo.Properties.Add(property);
    }
}
