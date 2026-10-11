using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Stanley.Editors;
using Xunit;

namespace Stanley.App.Tests;

/// <summary>
/// The two layouts (File › Options › Layout) must offer the same features: whatever an editor's
/// view model does through the ribbon, the canvas layout's toolbar, menu and inspector must reach
/// too. Read straight from the XAML (and code-behind) - every view-model member a ribbon binds to
/// or calls has to turn up in the canvas layout's files as well, or be listed below with the
/// reason it doesn't. So a feature added to one layout and not the other fails here, not in a
/// user's hands (docs/ui-shell.md §6).
/// </summary>
public sealed class LayoutParityTests
{
    /// <summary>Page-editor members only the ribbon needs, and why.</summary>
    private static readonly Dictionary<string, string> RibbonOnlyPageMembers = new()
    {
    };

    /// <summary>Character-editor members only the ribbon needs, and why.</summary>
    private static readonly Dictionary<string, string> RibbonOnlyCharacterMembers = new()
    {
        [nameof(CharacterEditorViewModel.SelectedStickerName)] =
            "The Sticker tab's label names the picked sticker; the inspector's Sticker section shows it in its picker (SelectedWorn) instead.",
    };

    [Fact]
    public void Everything_the_page_ribbon_does_the_canvas_layout_does_too() =>
        AssertParity<PageEditorViewModel>(
            ribbon: ["src/Stanley.Editors/Page/PageEditorRibbon.axaml", "src/Stanley.Editors/Page/Ribbon/*.axaml"],
            canvas: ["src/Stanley.Editors/Inspector/PageInspector.axaml", "src/Stanley.Editors/Inspector/Page/*.axaml",
                "src/Stanley.Editors/Toolbar/PageToolbar.axaml", "src/Stanley.Editors/Toolbar/PageZoomControl.axaml",
                "src/Stanley.App/Canvas/CanvasTopBar.axaml"],
            RibbonOnlyPageMembers);

    [Fact]
    public void Everything_the_character_ribbon_does_the_canvas_layout_does_too() =>
        AssertParity<CharacterEditorViewModel>(
            ribbon: ["src/Stanley.Editors/Characters/CharacterEditorRibbon.axaml", "src/Stanley.Editors/Characters/Ribbon/*.axaml"],
            canvas: ["src/Stanley.Editors/Inspector/CharacterInspector.axaml", "src/Stanley.Editors/Inspector/Character/*.axaml",
                "src/Stanley.Editors/Toolbar/CharacterToolbar.axaml"],
            RibbonOnlyCharacterMembers);

    private static void AssertParity<TViewModel>(string[] ribbon, string[] canvas, Dictionary<string, string> ribbonOnly)
    {
        var members = typeof(TViewModel).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m is PropertyInfo or MethodInfo { IsSpecialName: false } or EventInfo)
            .Select(m => m.Name).Where(name => name is not ("PropertyChanged" or "PropertyChanging"))
            .ToHashSet(StringComparer.Ordinal);
        var type = typeof(TViewModel).Name;
        var inRibbon = MembersUsed(Expand(ribbon), type, members);
        var inCanvas = MembersUsed(Expand(canvas), type, members);

        var missing = inRibbon.Except(inCanvas).Except(ribbonOnly.Keys).Order().ToList();
        Assert.True(missing.Count == 0,
            $"The ribbon uses these {type} members but the canvas layout doesn't - add them to its toolbar, menu or inspector, " +
            $"or list them as ribbon-only with a reason: {string.Join(", ", missing)}");

        var stale = ribbonOnly.Keys.Where(m => !inRibbon.Contains(m) || inCanvas.Contains(m)).Order().ToList();
        Assert.True(stale.Count == 0, $"No longer ribbon-only - take them off the list: {string.Join(", ", stale)}");
    }

    // ---------------------------------------------------------------- reading the files

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex BindingPath = new(@"\{(?:Binding|CompiledBinding)\s+(?:Path=)?(?<path>[^,}\s]+)", RegexOptions.Compiled);
    private static readonly Regex CastPath = new(@"\(\(\w+:(?<type>\w+)\)DataContext\)\.(?<member>\w+)", RegexOptions.Compiled);
    private static readonly Regex UserControlUse = new(@"<(?:local|editors):(?<name>\w+)\b", RegexOptions.Compiled);
    private static readonly Regex MemberAccess = new(@"\.(?<member>[A-Z]\w*)", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Stanley.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Couldn't find the repository root (Stanley.slnx).");
    }

    /// <summary>The files a layout is made of: the patterns given, plus every Stanley control they use that has XAML of its own (FontGroup, ColorSlotDropdown...), recursively.</summary>
    private static IReadOnlyCollection<string> Expand(string[] patterns)
    {
        var root = RepoRoot();
        var editors = Directory.GetFiles(Path.Combine(root, "src", "Stanley.Editors"), "*.axaml", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileNameWithoutExtension(f)).ToDictionary(g => g.Key, g => g.First());
        var pending = new Queue<string>(patterns.SelectMany(pattern =>
        {
            var full = Path.Combine(root, pattern);
            return Directory.GetFiles(Path.GetDirectoryName(full)!, Path.GetFileName(full));
        }));
        var files = new HashSet<string>();
        while (pending.TryDequeue(out var file))
        {
            if (!files.Add(file))
                continue;
            foreach (Match use in UserControlUse.Matches(File.ReadAllText(file)))
                if (editors.TryGetValue(use.Groups["name"].Value, out var control))
                    pending.Enqueue(control);
        }
        return files;
    }

    /// <summary>
    /// The members of <paramref name="type"/> the files reach: binding paths whose data type is it
    /// (or the window's, through <c>Editor.</c> - the page being edited), casts to it, and member
    /// accesses in the code-behind.
    /// </summary>
    private static HashSet<string> MembersUsed(IEnumerable<string> files, string type, HashSet<string> members)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var document = XDocument.Load(file);
            foreach (var element in document.Descendants())
            {
                var dataType = element.AncestorsAndSelf().Select(e => e.Attribute(Xaml + "DataType")?.Value).FirstOrDefault(v => v != null);
                var scope = dataType?[(dataType.IndexOf(':') + 1)..];
                var values = element.Attributes().Select(a => a.Value);
                if (element.Name.LocalName == "Binding" && element.Attribute("Path") is { } path)
                    values = values.Append("{Binding " + path.Value + "}");
                foreach (var value in values)
                {
                    foreach (Match cast in CastPath.Matches(value))
                        if (cast.Groups["type"].Value == type)
                            used.Add(cast.Groups["member"].Value);
                    foreach (Match binding in BindingPath.Matches(value))
                    {
                        var bound = binding.Groups["path"].Value.TrimStart('!');
                        if (bound.StartsWith('$') || bound.StartsWith('#'))
                            continue;
                        if (scope == "MainWindowViewModel" && type == nameof(PageEditorViewModel) && bound.StartsWith("Editor.", StringComparison.Ordinal))
                            (scope, bound) = (type, bound["Editor.".Length..]);
                        if (scope == type)
                            used.Add(bound.Split('.', '[', ')')[0]);
                        scope = dataType?[(dataType.IndexOf(':') + 1)..];
                    }
                }
            }

            if (File.Exists(file + ".cs"))
                foreach (Match access in MemberAccess.Matches(File.ReadAllText(file + ".cs")))
                    if (members.Contains(access.Groups["member"].Value))
                        used.Add(access.Groups["member"].Value);
        }
        used.IntersectWith(members);
        return used;
    }
}
