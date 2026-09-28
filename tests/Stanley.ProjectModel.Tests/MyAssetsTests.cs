using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class MyAssetsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-my-assets-tests").FullName;

    private static CharacterDefinition MakeCharacter(string name = "Alice") =>
        new(CharacterId.New(), name, BodyShape.Default, new Skeleton([]),
            new SortedDictionary<string, ColorValue> { ["skin"] = ColorValue.FromHex("#f2c9a4") },
            new SortedDictionary<string, IReadOnlyList<StickerId>>());

    private static ObjectGroup MakeGroup(string name = "Rocket ship") =>
        new(ObjectGroupId.New(), name, [new ShapeElement(ElementId.New(), ElementLayer.Background, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#123456"), 0))]);

    [Fact]
    public void Characters_and_object_groups_round_trip_through_My_Assets()
    {
        var myAssets = new MyAssets(_root);
        var character = MakeCharacter();
        var group = MakeGroup();

        myAssets.SaveCharacter(character);
        myAssets.SaveObjectGroup(group);

        Assert.Equivalent(character, myAssets.LoadCharacter(character.Id), strict: true);
        Assert.Equivalent(group, myAssets.LoadObjectGroup(group.Id), strict: true);
        Assert.Contains(myAssets.ListCharacters(), c => c.Id == character.Id);
        Assert.Contains(myAssets.ListObjectGroups(), g => g.Id == group.Id);
        Assert.True(File.Exists(Path.Combine(_root, "characters", $"{character.Id.Value}-alice", "character.json")));
        Assert.True(File.Exists(Path.Combine(_root, "objects", $"{group.Id.Value}-rocket-ship", "group.json")));
    }

    [Fact]
    public void MyAssetsVersion_is_left_out_of_the_file_when_null()
    {
        var myAssets = new MyAssets(_root);
        var character = MakeCharacter();
        myAssets.SaveCharacter(character);

        var path = Path.Combine(_root, "characters", $"{character.Id.Value}-alice", "character.json");
        Assert.DoesNotContain("myAssetsVersion", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

        myAssets.SaveCharacter(character with { MyAssetsVersion = "sha256:abc" });
        Assert.Contains("myAssetsVersion", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_pack_round_trips_with_members_sorted_and_adding_one_keeps_the_rest()
    {
        var myAssets = new MyAssets(_root);
        var character = MakeCharacter();
        var group = MakeGroup();
        myAssets.SaveCharacter(character);
        myAssets.SaveObjectGroup(group);

        // Members given out of order are written sorted (by kind, then id).
        var pack = new AssetPack(AssetPackId.New(), "Space Cats crew",
            [new AssetPackMember(AssetKind.Character, character.Id.Value), new AssetPackMember(AssetKind.ObjectGroup, group.Id.Value)]);
        myAssets.SavePack(pack);

        var saved = myAssets.LoadPack(pack.Id);
        Assert.Equal(AssetKind.ObjectGroup, saved.Members[0].Kind);
        Assert.Equal(AssetKind.Character, saved.Members[1].Kind);

        var otherGroup = MakeGroup("Nebula backdrop");
        myAssets.SaveObjectGroup(otherGroup);
        myAssets.SavePack(saved with { Members = [.. saved.Members, new AssetPackMember(AssetKind.ObjectGroup, otherGroup.Id.Value)] });

        // Both original members are still there, sorted alongside the new one - nothing else moved.
        var afterAdding = myAssets.LoadPack(pack.Id);
        Assert.Equal(3, afterAdding.Members.Count);
        Assert.Contains(afterAdding.Members, m => m.Kind == AssetKind.Character && m.Id == character.Id.Value);
        Assert.Contains(afterAdding.Members, m => m.Kind == AssetKind.ObjectGroup && m.Id == group.Id.Value);
        Assert.Contains(afterAdding.Members, m => m.Kind == AssetKind.ObjectGroup && m.Id == otherGroup.Id.Value);
        Assert.Equal([.. afterAdding.Members.OrderBy(m => m.Kind).ThenBy(m => m.Id, StringComparer.Ordinal)], afterAdding.Members);
    }

    [Fact]
    public void Removing_a_character_or_object_group_drops_it_from_every_pack_that_referenced_it()
    {
        var myAssets = new MyAssets(_root);
        var character = MakeCharacter();
        var group = MakeGroup();
        myAssets.SaveCharacter(character);
        myAssets.SaveObjectGroup(group);

        var pack = new AssetPack(AssetPackId.New(), "Space Cats crew",
            [new AssetPackMember(AssetKind.Character, character.Id.Value), new AssetPackMember(AssetKind.ObjectGroup, group.Id.Value)]);
        myAssets.SavePack(pack);

        myAssets.RemoveCharacter(character.Id);

        var reloaded = myAssets.LoadPack(pack.Id);
        Assert.DoesNotContain(reloaded.Members, m => m.Kind == AssetKind.Character);
        Assert.Contains(reloaded.Members, m => m.Kind == AssetKind.ObjectGroup && m.Id == group.Id.Value);
        Assert.Throws<DirectoryNotFoundException>(() => myAssets.LoadCharacter(character.Id));

        myAssets.RemoveObjectGroup(group.Id);
        Assert.Empty(myAssets.LoadPack(pack.Id).Members);
    }

    [Fact]
    public void Removing_a_pack_never_removes_what_it_lists()
    {
        var myAssets = new MyAssets(_root);
        var character = MakeCharacter();
        myAssets.SaveCharacter(character);
        var pack = new AssetPack(AssetPackId.New(), "Space Cats crew", [new AssetPackMember(AssetKind.Character, character.Id.Value)]);
        myAssets.SavePack(pack);

        myAssets.RemovePack(pack.Id);

        Assert.Throws<FileNotFoundException>(() => myAssets.LoadPack(pack.Id));
        Assert.Equivalent(character, myAssets.LoadCharacter(character.Id), strict: true);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
