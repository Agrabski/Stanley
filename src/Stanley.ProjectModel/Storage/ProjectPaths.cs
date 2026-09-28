using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Pure path computation for the project's folder layout
/// (docs/character-and-project-plan.md, "Repository layout"). Nothing outside this
/// class decides where an entity's file lives, so the layout is enforced, not optional.
///
/// Most entities get a `&lt;id&gt;-slug` folder or file; the slug is cosmetic and
/// recomputed only when an entity is first created; renaming an entity later does not
/// rename its folder, so a rename never cascades into unrelated diffs. Lookups therefore
/// always resolve by id prefix, never by recomputing and matching the current slug.
/// </summary>
internal static class ProjectPaths
{
    public const string ManifestFileName = "stanley.json";
    public const string GitAttributesFileName = ".gitattributes";

    public const string CharactersDirName = "characters";
    public const string PosesDirName = "poses";
    public const string PropsDirName = "props";
    public const string BackgroundsDirName = "backgrounds";
    public const string IssuesDirName = "issues";
    public const string ObjectsDirName = "objects";
    public const string PacksDirName = "packs";

    /// <summary>The comic's title page, which every issue opens with unless it has its own: <c>title-page/page.json</c>, <c>panels/</c> and <c>art/</c>.</summary>
    public const string TitlePageDirName = "title-page";

    public const string RevisionsDirName = "revisions";
    public const string StickersDirName = "stickers";
    public const string VariantsDirName = "variants";
    public const string BackdropsDirName = "backdrops";
    public const string PagesDirName = "pages";
    public const string PanelsDirName = "panels";
    public const string ArtDirName = "art";
    public const string PatternsDirName = "patterns";

    public const string CharacterFileName = "character.json";
    public const string StickerFileName = "sticker.json";
    public const string PropFileName = "prop.json";
    public const string BackgroundFileName = "background.json";
    public const string IssueFileName = "issue.json";
    public const string PageFileName = "page.json";
    public const string GroupFileName = "group.json";

    public const string JsonExtension = "json";

    /// <summary>Lower-case, hyphen-separated, filename-safe slug of a display name; falls back to "untitled" for a name with no letters/digits.</summary>
    public static string Slugify(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;
        var lastWasHyphen = true; // suppress a leading hyphen the same way a trailing one is suppressed below

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                buffer[length++] = '-';
                lastWasHyphen = true;
            }
        }

        if (length > 0 && buffer[length - 1] == '-')
            length--;

        return length > 0 ? new string(buffer[..length]) : "untitled";
    }

    public static string EntityDirName<TId>(TId id, string name) where TId : struct, IStrongId<TId> =>
        $"{id.Value}-{Slugify(name)}";

    public static string EntityFileName<TId>(TId id, string name, string extension) where TId : struct, IStrongId<TId> =>
        $"{id.Value}-{Slugify(name)}.{extension}";

    /// <summary>Finds an existing `&lt;id&gt;-*` folder under <paramref name="parentDir"/>, or null if none exists yet.</summary>
    public static string? FindEntityDir<TId>(string parentDir, TId id) where TId : struct, IStrongId<TId>
    {
        if (!Directory.Exists(parentDir))
            return null;

        var prefix = id.Value + "-";
        return Directory.EnumerateDirectories(parentDir)
            .FirstOrDefault(dir => Path.GetFileName(dir).StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Finds an existing `&lt;id&gt;-*.extension` file under <paramref name="parentDir"/>, or null if none exists yet.</summary>
    public static string? FindEntityFile<TId>(string parentDir, TId id, string extension) where TId : struct, IStrongId<TId>
    {
        if (!Directory.Exists(parentDir))
            return null;

        var prefix = id.Value + "-";
        var suffix = "." + extension;
        return Directory.EnumerateFiles(parentDir).FirstOrDefault(file =>
        {
            var fileName = Path.GetFileName(file);
            return fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName.EndsWith(suffix, StringComparison.Ordinal);
        });
    }

    /// <summary>The existing folder for <paramref name="id"/> if one exists, otherwise a freshly created `&lt;id&gt;-slug` folder.</summary>
    public static string ResolveOrCreateEntityDir<TId>(string parentDir, TId id, string name) where TId : struct, IStrongId<TId>
    {
        var existing = FindEntityDir(parentDir, id);
        if (existing is not null)
            return existing;

        var path = Path.Combine(parentDir, EntityDirName(id, name));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The existing file path for <paramref name="id"/> if one exists, otherwise the path a freshly created `&lt;id&gt;-slug.extension` file should be written to.</summary>
    public static string ResolveOrCreateEntityFilePath<TId>(string parentDir, TId id, string name, string extension) where TId : struct, IStrongId<TId>
    {
        var existing = FindEntityFile(parentDir, id, extension);
        if (existing is not null)
            return existing;

        Directory.CreateDirectory(parentDir);
        return Path.Combine(parentDir, EntityFileName(id, name, extension));
    }

    /// <summary>The id part of an `&lt;id&gt;-slug` folder or file name (everything before the first '-', minus any extension).</summary>
    public static string EntityIdPart(string fileName)
    {
        var dash = fileName.IndexOf('-');
        var stem = dash >= 0 ? fileName[..dash] : Path.GetFileNameWithoutExtension(fileName);
        return stem;
    }

    /// <summary>Panels are the one entity with no slug: `panels/&lt;id&gt;.json`, since panels aren't user-named.</summary>
    public static string PanelFilePath(string panelsDir, PanelId id) => Path.Combine(panelsDir, $"{id.Value}.json");
}
