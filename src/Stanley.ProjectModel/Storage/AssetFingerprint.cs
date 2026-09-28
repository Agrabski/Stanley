using System.Security.Cryptography;
using System.Text;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Whether a kept asset in a comic still matches its My Assets copy
/// (docs/asset-packs.md §7.3), generalising docs/my-characters.md §6.3's
/// <c>CharacterFingerprint</c> to every kind: only the file set differs per kind, built by
/// <see cref="CharacterFingerprint"/> and <see cref="ObjectGroupFingerprint"/> below.
/// </summary>
public static class AssetFingerprint
{
    /// <summary>
    /// A SHA-256 over every entry's path, content length and content, in sorted-path order.
    /// Paths must be keyed by id, not a cosmetic on-disk slug, so a rename or a re-slugged
    /// folder never changes the fingerprint; callers pass already-canonical bytes (JSON via
    /// <see cref="ProjectJson.Serialize{T}"/>, already sorted-key/indented; art via
    /// <see cref="CanonicalBytes"/>, which normalises SVG line endings) so a Windows
    /// checkout with <c>core.autocrlf</c> doesn't look like a change either.
    /// </summary>
    public static string Compute(IReadOnlyDictionary<string, byte[]> files)
    {
        using var buffer = new MemoryStream();
        foreach (var (path, bytes) in files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            buffer.Write(Encoding.UTF8.GetBytes(path));
            buffer.Write(BitConverter.GetBytes(bytes.Length));
            buffer.Write(bytes);
        }
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>The canonical bytes of one art file: raw for a PNG, newline-normalised UTF-8 for an SVG.</summary>
    public static byte[] CanonicalBytes(ArtFile file) =>
        file.IsSvg ? Encoding.UTF8.GetBytes(NormalizeLineEndings(file.Text!)) : file.Bytes ?? [];

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>
    /// <c>character.json</c> (without <see cref="CharacterDefinition.MyAssetsVersion"/>) plus
    /// every sticker's <c>sticker.json</c> and art, the pattern tiles, and every look -
    /// exactly docs/my-characters.md §6.3's file set.
    /// </summary>
    public static string CharacterFingerprint(CharacterDefinition character)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["character.json"] = Encoding.UTF8.GetBytes(ProjectJson.Serialize(character with { MyAssetsVersion = null }))
        };
        foreach (var asset in character.Wardrobe.Stickers.Values)
        {
            files[$"stickers/{asset.Id.Value}/sticker.json"] = Encoding.UTF8.GetBytes(ProjectJson.Serialize(asset.Sticker));
            foreach (var (relative, file) in asset.Files)
                files[$"stickers/{asset.Id.Value}/{relative}"] = CanonicalBytes(file);
        }
        foreach (var (name, tile) in character.Wardrobe.Tiles)
            files[$"patterns/{name}"] = CanonicalBytes(tile);
        foreach (var revision in character.Revisions.Values)
            files[$"revisions/{revision.Id.Value}.json"] = Encoding.UTF8.GetBytes(ProjectJson.Serialize(revision));
        return Compute(files);
    }

    /// <summary><c>group.json</c> (without <see cref="ObjectGroup.MyAssetsVersion"/>) plus its art files.</summary>
    public static string ObjectGroupFingerprint(ObjectGroup group)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["group.json"] = Encoding.UTF8.GetBytes(ProjectJson.Serialize(group with { MyAssetsVersion = null }))
        };
        foreach (var (name, art) in group.ArtFiles)
            files[$"art/{name}"] = CanonicalBytes(art);
        return Compute(files);
    }
}
