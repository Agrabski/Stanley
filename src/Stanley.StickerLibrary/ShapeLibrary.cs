using System.Globalization;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.StickerLibrary;

/// <summary>
/// A shape in the starter library (docs/shape-characters.md §8): its key
/// (<c>&lt;kind&gt;/&lt;category&gt;/&lt;name&gt;</c>, e.g. <c>head/organic/egg</c>) and its
/// outline, as shipped.
/// </summary>
public sealed record LibraryShape(string Key, ModuleShape Shape)
{
    /// <summary><see cref="ShapeLibrary.Head"/>, <see cref="ShapeLibrary.Torso"/>, <see cref="ShapeLibrary.Hand"/>, <see cref="ShapeLibrary.Foot"/> or <see cref="ShapeLibrary.Piece"/>.</summary>
    public string Kind => Key.Split('/')[0];

    /// <summary><see cref="ShapeLibrary.Organic"/>, <see cref="ShapeLibrary.Geometric"/> or <see cref="ShapeLibrary.Abstract"/>.</summary>
    public string Category => Key.Split('/')[1];

    /// <summary>The file name: <c>rounded-square</c>.</summary>
    public string Name => Key.Split('/')[2];

    /// <summary>What the gallery calls it: "Rounded square".</summary>
    public string DisplayName => ShapeLibrary.DisplayName(Name);

    /// <summary>The source a copy of it carries while unmodified: <c>library:shapes/head/organic/egg</c>.</summary>
    public string Source => ShapeLibrary.SourcePrefix + Key;

    /// <summary>
    /// A copy to put in a character: its own anchors and sockets, marked as an unmodified
    /// library copy (docs/sticker-system.md §11), so a later library change never repaints it.
    /// </summary>
    public ModuleShape Instantiate() => new(
        Shape.Anchors.ToList(),
        Shape.Sockets is { } sockets ? new SortedDictionary<string, Point2D>(sockets, StringComparer.Ordinal) : null,
        Source);
}

/// <summary>
/// The starter shapes characters are built from (docs/shape-characters.md §8; art under
/// CC0 1.0 - see <c>Library/LICENSE</c>): heads, torsos, hands, feet and limb pieces, each
/// in an organic, a geometric and an abstract style. Stored in the project format itself
/// (one <see cref="ModuleShape"/> per file under <c>Library/shapes/</c>), so putting one on
/// a character is writing the same JSON.
/// </summary>
public static class ShapeLibrary
{
    /// <summary>What a library copy's <see cref="ModuleShape.Source"/> starts with, before the shape's key.</summary>
    public const string SourcePrefix = StickerLibrary.SourcePrefix + "shapes/";

    public const string Head = "head";
    public const string Torso = "torso";
    public const string Hand = "hand";
    public const string Foot = "foot";

    /// <summary>A piece of a <see cref="LimbStyle.Pieces"/> limb: an upper arm, a shin.</summary>
    public const string Piece = "piece";

    public const string Organic = "organic";
    public const string Geometric = "geometric";
    public const string Abstract = "abstract";

    /// <summary>The kinds, in the order the galleries show them.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [Head, Torso, Hand, Foot, Piece];

    /// <summary>The categories, in the order the galleries show them.</summary>
    public static IReadOnlyList<string> Categories { get; } = [Organic, Geometric, Abstract];

    private const string ResourcePrefix = "library/shapes/";

    private static readonly Lazy<IReadOnlyList<LibraryShape>> Shapes = new(Load);

    /// <summary>Every library shape, by kind, then category, then name.</summary>
    public static IReadOnlyList<LibraryShape> All => Shapes.Value;

    public static IEnumerable<LibraryShape> ForKind(string kind) => All.Where(s => s.Kind == kind);

    public static IEnumerable<LibraryShape> ForKind(string kind, string category) => All.Where(s => s.Kind == kind && s.Category == category);

    /// <summary>The shape kind that fits a part's own outline: heads for the head, torsos for the torso. Limbs have no outline of their own.</summary>
    public static string? KindFor(ModulePart part) => part switch
    {
        ModulePart.Head => Head,
        ModulePart.Torso => Torso,
        _ => null
    };

    /// <summary>The shape kind for one of a limb's own shapes: hands or feet for its end, pieces for its upper and lower part.</summary>
    public static string KindFor(ModulePart part, LimbPiece piece) =>
        piece == LimbPiece.End ? (part == ModulePart.Legs ? Foot : Hand) : Piece;

    /// <summary>The library shape <paramref name="key"/> (<c>head/organic/egg</c>), or null.</summary>
    public static LibraryShape? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>A fresh copy of library shape <paramref name="key"/>; throws if the library hasn't got it (for presets, whose keys the tests check).</summary>
    public static ModuleShape Instantiate(string key) =>
        (Find(key) ?? throw new KeyNotFoundException($"No library shape '{key}'.")).Instantiate();

    /// <summary>The library shape a character's shape was copied from, if it's still an unmodified copy.</summary>
    public static LibraryShape? SourceOf(ModuleShape shape) =>
        shape.Source is { } source && source.StartsWith(SourcePrefix, StringComparison.Ordinal) ? Find(source[SourcePrefix.Length..]) : null;

    /// <summary>A file or category name as a user reads it: <c>rounded-square</c> -&gt; "Rounded square".</summary>
    public static string DisplayName(string name)
    {
        var words = name.Replace('-', ' ');
        return words.Length == 0 ? words : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    private static IReadOnlyList<LibraryShape> Load()
    {
        var assembly = typeof(ShapeLibrary).Assembly;
        var result = new List<LibraryShape>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
                continue;
            var key = name[ResourcePrefix.Length..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing library resource '{name}'.");
            using var reader = new StreamReader(stream);
            // A library file carries no source of its own: a copy is given one when it's taken.
            var shape = ProjectJson.Deserialize<ModuleShape>(reader.ReadToEnd()) with { Source = null };
            result.Add(new LibraryShape(key, shape));
        }
        return result
            .OrderBy(s => Order(Kinds, s.Kind))
            .ThenBy(s => Order(Categories, s.Category))
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static int Order(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return i;
        }
        return list.Count;
    }
}
