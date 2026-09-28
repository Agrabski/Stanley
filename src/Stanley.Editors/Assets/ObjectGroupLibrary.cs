using System.Text.Json.Nodes;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Serialization;

namespace Stanley.Editors;

/// <summary>
/// The open comic's own copies of the object groups it has kept in My Assets or taken from
/// there - its <c>objects/</c> folder (docs/asset-packs.md §7.1), shared by every page the
/// way <see cref="PictureLibrary"/> is. Each copy carries the My Assets fingerprint it last
/// matched, which is what later tells "changed here" from "changed in My Assets". Like
/// pictures, copies only ever come in: a page that stops using one (an undo, a delete) can
/// get it back, and another issue of the comic may still use it.
/// </summary>
public sealed class ObjectGroupLibrary
{
    private IReadOnlyDictionary<ObjectGroupId, ObjectGroup> _groups;

    public ObjectGroupLibrary(IEnumerable<ObjectGroup>? groups = null) =>
        _groups = (groups ?? []).ToDictionary(g => g.Id);

    /// <summary>Every copy, by id - a snapshot, replaced rather than changed.</summary>
    public IReadOnlyDictionary<ObjectGroupId, ObjectGroup> Groups => _groups;

    public event Action? Changed;

    /// <summary>Adds or replaces the comic's copy of <paramref name="group"/>.</summary>
    public void Put(ObjectGroup group)
    {
        _groups = new Dictionary<ObjectGroupId, ObjectGroup>(_groups) { [group.Id] = group };
        Changed?.Invoke();
    }

    /// <summary>
    /// A group on a page as an out-of-line object group, plus the pictures its children show
    /// from <paramref name="pictures"/> so the copy is self-contained, like a sticker's folder.
    /// Neither where it sits nor which copy it is is part of what it is: the children are
    /// stored with the group's top-left corner at 0,0, every number rounded to 0.1 µm, and
    /// ids derived from the group's own id and their place in it (a copy on a page has fresh
    /// ones) - so the same group dragged anywhere, on any page, in any comic, fingerprints the
    /// same, and floating-point drift from moving it back and forth never reads as a change.
    /// </summary>
    public static ObjectGroup FromElement(GroupElement element, ObjectGroupId id, string name, IReadOnlyDictionary<string, ArtFile> pictures)
    {
        var art = PanelElements.ArtFileNames(element)
            .Distinct()
            .Where(pictures.ContainsKey)
            .ToDictionary(n => n, n => pictures[n], StringComparer.Ordinal);
        var origin = PanelElements.Bounds(element);
        var children = element.Children
            .Select((c, i) => WithStableIds(ElementEditing.Move(c, -origin.X, -origin.Y), $"{id.Value}_{i}"))
            .ToList();
        return Rounded(new ObjectGroup(id, name, children)) with { ArtFiles = art };
    }

    private static PanelElement WithStableIds(PanelElement element, string id) => element switch
    {
        GroupElement group => group with
        {
            Id = ElementId.FromValue(id),
            Children = group.Children.Select((c, i) => WithStableIds(c, $"{id}_{i}")).ToList()
        },
        _ => element with { Id = ElementId.FromValue(id) }
    };

    private static ObjectGroup Rounded(ObjectGroup group)
    {
        var node = JsonNode.Parse(ProjectJson.Serialize(group))!;
        return ProjectJson.Deserialize<ObjectGroup>(Round(node)!.ToJsonString());
    }

    private static JsonNode? Round(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                    obj[key] = Round(obj[key]?.DeepClone());
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    array[i] = Round(array[i]?.DeepClone());
                return array;
            case JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.Number && value.TryGetValue<double>(out var number):
                return JsonValue.Create(Math.Round(number, 4));
            default:
                return node;
        }
    }

    /// <summary>A kept object group as a group element to put on a page, top-left at 0,0 and pointing back at <paramref name="group"/> by id - move it into place (and copy it for fresh element ids) before it goes on a page.</summary>
    public static GroupElement ToElement(ObjectGroup group) =>
        new(ElementId.New(), group.Children.FirstOrDefault()?.Layer ?? ElementLayer.Foreground, group.Children, group.Id);
}
