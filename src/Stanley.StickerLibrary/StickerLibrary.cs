using System.Reflection;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.StickerLibrary;

/// <summary>A sticker in the starter library: its key (<c>&lt;slot&gt;/&lt;name&gt;</c>) and its files, as shipped.</summary>
public sealed record LibrarySticker(string Key, StickerAsset Asset)
{
    public string Name => Asset.Sticker.Name;

    public string Slot => Asset.Sticker.Slot;

    /// <summary>A copy to put in a character's wardrobe: a fresh id, marked as an unmodified library copy (docs/sticker-system.md §11).</summary>
    public StickerAsset Instantiate() =>
        Asset with { Sticker = Asset.Sticker with { Id = StickerId.New(), Source = StickerLibrary.SourcePrefix + Key } };

    /// <summary>The same sticker under a fixed id - for gallery previews, which never reach a file.</summary>
    public StickerAsset Preview { get; } = Asset with { Sticker = Asset.Sticker with { Id = StickerId.FromValue("preview" + Key.Replace("/", "").Replace("-", "")), Source = StickerLibrary.SourcePrefix + Key } };
}

/// <summary>
/// The starter stickers and pattern tiles that ship with Stanley (art under CC0 1.0 - see
/// <c>Library/LICENSE</c>), stored in the project format itself as embedded resources, so
/// copying one into a character is writing the same files.
/// </summary>
public static class StickerLibrary
{
    public const string SourcePrefix = "library:";

    private static readonly Lazy<IReadOnlyList<LibrarySticker>> Stickers = new(Load);
    private static readonly Lazy<IReadOnlyDictionary<string, ArtFile>> Tiles = new(LoadTiles);

    /// <summary>Every library sticker, by slot then name.</summary>
    public static IReadOnlyList<LibrarySticker> All => Stickers.Value;

    /// <summary>The library's pattern tiles, by file name.</summary>
    public static IReadOnlyDictionary<string, ArtFile> PatternTiles => Tiles.Value;

    public static IEnumerable<LibrarySticker> ForSlot(string slot) => All.Where(s => s.Slot == slot);

    /// <summary>What a new character's face starts as - simple, so it reads at any size; each is one click to change or take off.</summary>
    public static IReadOnlyList<string> DefaultFace { get; } = ["eyes/dots", "brows/thin", "mouth/simple"];

    public static LibrarySticker? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>The library sticker a wardrobe sticker was copied from, if it's still an unmodified copy.</summary>
    public static LibrarySticker? SourceOf(Sticker sticker) =>
        sticker.Source is { } source && source.StartsWith(SourcePrefix, StringComparison.Ordinal) ? Find(source[SourcePrefix.Length..]) : null;

    private static IReadOnlyList<LibrarySticker> Load()
    {
        var assembly = typeof(StickerLibrary).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("library/", StringComparison.Ordinal)).ToList();
        var result = new List<LibrarySticker>();
        foreach (var json in names.Where(n => n.EndsWith("/sticker.json", StringComparison.Ordinal)))
        {
            var folder = json[..^"sticker.json".Length]; // "library/<slot>/<key>/"
            var key = folder["library/".Length..].TrimEnd('/');
            var sticker = ProjectJson.Deserialize<Sticker>(ReadText(assembly, json)).Normalized();
            var files = names
                .Where(n => n.StartsWith(folder, StringComparison.Ordinal) && n != json)
                .ToDictionary(n => n[folder.Length..], n => ReadFile(assembly, n), StringComparer.Ordinal);
            result.Add(new LibrarySticker(key, new StickerAsset(sticker, files)));
        }
        return result
            .OrderBy(s => StickerSlots.ZOrder(s.Slot))
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<string, ArtFile> LoadTiles()
    {
        var assembly = typeof(StickerLibrary).Assembly;
        const string prefix = "library/patterns/";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(n => n[prefix.Length..], n => ReadFile(assembly, n), StringComparer.Ordinal);
    }

    private static ArtFile ReadFile(Assembly assembly, string name) =>
        name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? ArtFile.Svg(ReadText(assembly, name)) : ArtFile.Png(ReadBytes(assembly, name));

    private static string ReadText(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing library resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing library resource '{name}'.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
