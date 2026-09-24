using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stanley.ProjectModel.Serialization;

/// <summary>
/// <see cref="JsonStringEnumConverter{TEnum}"/> with camelCase naming, so enum values
/// (e.g. <c>"front"</c>, <c>"buildStretch"</c>) match every other property name's
/// casing. A subclass rather than passing the naming policy at the use site, because
/// <see cref="JsonConverterAttribute"/> can only instantiate a converter type via its
/// parameterless constructor.
/// </summary>
public sealed class CamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
    public CamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase)
    {
    }
}
