using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class AssetFingerprintTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-fingerprint-tests").FullName;

    private static CharacterDefinition MakeCharacter(string name = "Alice", string skinHex = "#f2c9a4") =>
        new(CharacterId.New(), name, BodyShape.Default, new Skeleton([]),
            new SortedDictionary<string, ColorValue> { ["skin"] = ColorValue.FromHex(skinHex) },
            new SortedDictionary<string, IReadOnlyList<StickerId>>());

    private static CharacterDefinition WithHair(CharacterDefinition character, string svg, StickerId? stickerId = null)
    {
        var id = stickerId ?? StickerId.New();
        var sticker = new Sticker(id, "Hair", StickerSlots.Hair, [], new SortedDictionary<string, ColorValue>(), ["default"]);
        var wardrobe = new Wardrobe(
            new Dictionary<StickerId, StickerAsset> { [id] = new StickerAsset(sticker, new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(svg) }) },
            new Dictionary<string, ArtFile>());
        return character with { Wardrobe = wardrobe };
    }

    [Fact]
    public void Character_fingerprint_is_unchanged_by_a_save_load_round_trip_and_a_renamed_folder_slug()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var character = MakeCharacter();
        var before = AssetFingerprint.CharacterFingerprint(character);

        repository.SaveCharacter(character);
        Assert.Equal(before, AssetFingerprint.CharacterFingerprint(repository.LoadCharacter(character.Id)));

        var dir = Path.Combine(_root, "characters", $"{character.Id.Value}-alice");
        var renamed = Path.Combine(_root, "characters", $"{character.Id.Value}-something-else");
        Directory.Move(dir, renamed);
        Assert.Equal(before, AssetFingerprint.CharacterFingerprint(repository.LoadCharacter(character.Id)));
    }

    [Fact]
    public void Character_fingerprint_is_unchanged_by_CRLF_line_endings_in_svg_art()
    {
        var lf = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>\n";
        var crlf = lf.Replace("\n", "\r\n");
        var character = MakeCharacter();
        var stickerId = StickerId.New();

        var withLf = WithHair(character, lf, stickerId);
        var withCrlf = WithHair(character, crlf, stickerId);

        Assert.Equal(AssetFingerprint.CharacterFingerprint(withLf), AssetFingerprint.CharacterFingerprint(withCrlf));
    }

    [Fact]
    public void Character_fingerprint_changes_with_the_name_a_colour_sticker_art_or_a_look()
    {
        var baseline = MakeCharacter();
        var byName = baseline with { Name = "Bob" };
        var byColour = baseline with { ColorSlots = new SortedDictionary<string, ColorValue> { ["skin"] = ColorValue.FromHex("#000000") } };
        var withArtA = WithHair(baseline, "<svg>A</svg>");
        var withArtB = WithHair(baseline, "<svg>B</svg>");

        var revisionId = CharacterRevisionId.New();
        var revision = new CharacterRevision(revisionId, baseline.Id, "Winter",
            new SortedDictionary<string, IReadOnlyList<StickerId>>(), new SortedDictionary<string, ColorValue>(), ProportionOverride: null, Build: null);
        var withLook = baseline with { Revisions = new Dictionary<CharacterRevisionId, CharacterRevision> { [revisionId] = revision } };

        var fpBase = AssetFingerprint.CharacterFingerprint(baseline);
        Assert.NotEqual(fpBase, AssetFingerprint.CharacterFingerprint(byName));
        Assert.NotEqual(fpBase, AssetFingerprint.CharacterFingerprint(byColour));
        Assert.NotEqual(AssetFingerprint.CharacterFingerprint(withArtA), AssetFingerprint.CharacterFingerprint(withArtB));
        Assert.NotEqual(fpBase, AssetFingerprint.CharacterFingerprint(withLook));
    }

    [Fact]
    public void MyAssetsVersion_is_excluded_from_the_character_fingerprint()
    {
        var character = MakeCharacter();
        var withVersion = character with { MyAssetsVersion = "sha256:whatever" };
        Assert.Equal(AssetFingerprint.CharacterFingerprint(character), AssetFingerprint.CharacterFingerprint(withVersion));
    }

    [Fact]
    public void A_no_op_comic_save_produces_an_identical_character_fingerprint()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var character = MakeCharacter();
        repository.SaveCharacter(character);
        var first = AssetFingerprint.CharacterFingerprint(repository.LoadCharacter(character.Id));

        repository.SaveCharacter(repository.LoadCharacter(character.Id));
        var second = AssetFingerprint.CharacterFingerprint(repository.LoadCharacter(character.Id));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Object_group_fingerprint_changes_with_a_child_element_but_not_with_an_unrelated_edit_elsewhere_in_the_comic()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var leaf = new ShapeElement(ElementId.New(), ElementLayer.Background, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#111111"), 0));
        var group = new ObjectGroup(ObjectGroupId.New(), "Rocket ship", [leaf]);
        repository.SaveObjectGroup(group);
        var before = AssetFingerprint.ObjectGroupFingerprint(repository.LoadObjectGroup(group.Id));

        // An edit elsewhere in the comic (a different character) never touches this group's fingerprint.
        repository.SaveCharacter(MakeCharacter("Someone else"));
        Assert.Equal(before, AssetFingerprint.ObjectGroupFingerprint(repository.LoadObjectGroup(group.Id)));

        // Changing the group's own child does.
        var changed = group with { Children = [leaf with { Style = leaf.Style with { Fill = ColorValue.FromHex("#222222") } }] };
        repository.SaveObjectGroup(changed);
        Assert.NotEqual(before, AssetFingerprint.ObjectGroupFingerprint(repository.LoadObjectGroup(group.Id)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
