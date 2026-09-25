using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
namespace Stanley.Editors;

/// <summary>
/// A pattern or texture on offer for a colour slot (or none), previewed in the slot's
/// colour: a generated one, a tile (<paramref name="Tile"/>, from the library or the
/// character's own), or <paramref name="IsCustom"/> - "Custom...", which asks for a file.
/// </summary>
public sealed record FabricChoice(string Label, ColorValue Ground, Fabric Preview, PatternKind? Pattern, TextureKind? Texture, bool IsCurrent,
	string? Tile = null, ArtFile? TileFile = null, bool IsCustom = false)
{
	/// <summary>The tile the preview draws, by name.</summary>
	public IReadOnlyDictionary<string, ArtFile>? Tiles => Tile is { } name && TileFile is { } file ? new Dictionary<string, ArtFile> { [name] = file } : null;
}