using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Serialization;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

/// <summary>
/// Styles in the project files (docs/sticker-system.md §20): which variant a sticker is worn in,
/// per sticker id, on the character, a named look and a panel - and absent when unused, so
/// existing files read and write exactly as before.
/// </summary>
public class StickerVariantSerializationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-style-json-tests").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly StickerId Cap = StickerId.FromValue("cap0000001");
    private static readonly StickerId Hood = StickerId.FromValue("hood000001");

    private static SortedDictionary<StickerId, string> Styles(params (StickerId Id, string Variant)[] styles) =>
        new(styles.ToDictionary(s => s.Id, s => s.Variant));

    private static CharacterRevision Revision(CharacterId character, SortedDictionary<StickerId, string>? styles = null) =>
        new(CharacterRevisionId.New(), character, "Winter", new SortedDictionary<string, IReadOnlyList<StickerId>>(), new SortedDictionary<string, ColorValue>(),
            null, null, StickerVariantValues: styles);

    [Fact]
    public void A_characters_styles_are_keyed_by_sticker_id_and_round_trip()
    {
        var character = CharacterDefinition.Create("A") with { StickerVariants = Styles((Hood, "up"), (Cap, "brim-back")) };

        var json = ProjectJson.Serialize(character);
        var read = ProjectJson.Deserialize<CharacterDefinition>(json);

        Assert.Contains("\"stickerVariants\": {", json);
        Assert.Contains("\"cap0000001\": \"brim-back\"", json);
        Assert.True(json.IndexOf("cap0000001", StringComparison.Ordinal) < json.IndexOf("hood000001", StringComparison.Ordinal), "keys are sorted");
        Assert.Equal(character.StickerVariants, read.StickerVariants);
    }

    [Fact]
    public void A_looks_and_a_panels_styles_round_trip()
    {
        var revision = Revision(CharacterId.New(), Styles((Hood, "down")));
        var overrides = new CharacterInstanceOverrides(null, null, StickerVariantOverrides: Styles((Cap, "brim-left")));

        var revisionJson = ProjectJson.Serialize(revision);
        var overridesJson = ProjectJson.Serialize(overrides);

        Assert.Contains("\"stickerVariantValues\": {", revisionJson);
        Assert.Contains("\"stickerVariantOverrides\": {", overridesJson);
        Assert.Equal(revision.StickerVariantValues, ProjectJson.Deserialize<CharacterRevision>(revisionJson).StickerVariantValues);
        Assert.Equal(overrides.StickerVariantOverrides, ProjectJson.Deserialize<CharacterInstanceOverrides>(overridesJson).StickerVariantOverrides);
        Assert.False(overrides.IsEmpty); // a style alone is a panel change worth keeping
    }

    [Fact]
    public void Without_styles_no_new_keys_are_written()
    {
        var character = CharacterDefinition.Create("A");
        var revision = Revision(character.Id);
        var overrides = new CharacterInstanceOverrides(null, new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#ff0000") });

        Assert.DoesNotContain("stickerVariant", ProjectJson.Serialize(character));
        Assert.DoesNotContain("stickerVariant", ProjectJson.Serialize(revision));
        Assert.DoesNotContain("stickerVariant", ProjectJson.Serialize(overrides));
        Assert.True(new CharacterInstanceOverrides(null, null, StickerVariantOverrides: []).IsEmpty);
    }

    [Fact]
    public void Files_from_before_styles_load_with_none()
    {
        var id = CharacterId.New();
        var character = ProjectJson.Deserialize<CharacterDefinition>(
            "{\"colorSlots\":{\"skin\":\"#f2c9a4\"},\"id\":\"" + id.Value + "\",\"name\":\"Old\",\"skeleton\":{\"restLayouts\":[]},\"stickers\":{}}");
        var revision = ProjectJson.Deserialize<CharacterRevision>(
            "{\"activeStickers\":{},\"characterId\":\"" + id.Value + "\",\"colorSlotValues\":{},\"id\":\"" + CharacterRevisionId.New().Value + "\",\"name\":\"Old look\"}");
        var overrides = ProjectJson.Deserialize<CharacterInstanceOverrides>("{\"colorSlotOverrides\":{\"top\":\"#ff0000\"}}");

        Assert.Null(character.StickerVariants);
        Assert.Null(revision.StickerVariantValues);
        Assert.Null(overrides.StickerVariantOverrides);
    }

    [Fact]
    public void A_character_and_its_looks_save_and_load_with_their_styles()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var character = CharacterDefinition.Create("Alice") with { StickerVariants = Styles((Cap, "brim-back")) };
        var revision = Revision(character.Id, Styles((Cap, "brim-right")));
        character = character with { Revisions = new Dictionary<CharacterRevisionId, CharacterRevision> { [revision.Id] = revision } };

        repository.SaveCharacter(character);
        var loaded = repository.LoadCharacter(character.Id);

        Assert.Equal(character.StickerVariants, loaded.StickerVariants);
        Assert.Equal(revision.StickerVariantValues, loaded.Revisions[revision.Id].StickerVariantValues);
    }
}
