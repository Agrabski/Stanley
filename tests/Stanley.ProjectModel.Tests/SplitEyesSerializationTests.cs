using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>Split eyes in the project files (docs/sticker-system.md §21): which side each sticker is restricted to, keyed by sticker id, absent when nothing is split.</summary>
public class SplitEyesSerializationTests
{
    private static readonly StickerId LeftEye = StickerId.FromValue("eyeleft001");
    private static readonly StickerId RightEye = StickerId.FromValue("eyeright01");

    [Fact]
    public void Sticker_sides_are_keyed_by_sticker_id_and_round_trip()
    {
        var character = CharacterDefinition.Create("A") with
        {
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [LeftEye] = LimbSide.Left, [RightEye] = LimbSide.Right }
        };

        var json = ProjectJson.Serialize(character);
        var read = ProjectJson.Deserialize<CharacterDefinition>(json);

        Assert.Contains("\"stickerSides\": {", json);
        Assert.Contains("\"eyeleft001\": \"left\"", json);
        Assert.Contains("\"eyeright01\": \"right\"", json);
        Assert.Equal(character.StickerSides, read.StickerSides);
    }

    [Fact]
    public void An_unsplit_character_writes_nothing_for_sticker_sides()
    {
        var character = CharacterDefinition.Create("A");

        var json = ProjectJson.Serialize(character);

        Assert.DoesNotContain("stickerSides", json);
        Assert.Null(ProjectJson.Deserialize<CharacterDefinition>(json).StickerSides);
    }
}
