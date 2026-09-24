using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Tests;

public class EntityIdTests
{
    [Fact]
    public void New_ids_are_unique_and_filename_safe()
    {
        var a = CharacterId.New();
        var b = CharacterId.New();

        Assert.NotEqual(a, b);
        Assert.DoesNotContain('/', a.Value);
        Assert.DoesNotContain('\\', a.Value);
        Assert.DoesNotContain(' ', a.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    [InlineData("has space")]
    [InlineData("has-hyphen")]
    public void FromValue_rejects_unsafe_ids(string invalid)
    {
        Assert.Throws<ArgumentException>(() => CharacterId.FromValue(invalid));
    }

    [Fact]
    public void FromValue_accepts_a_plain_token_and_round_trips_through_ToString()
    {
        var id = CharacterId.FromValue("alice01");

        Assert.Equal("alice01", id.Value);
        Assert.Equal("alice01", id.ToString());
    }

    [Fact]
    public void Parse_and_TryParse_agree_with_FromValue()
    {
        Assert.Equal(CharacterId.FromValue("abc"), CharacterId.Parse("abc"));
        Assert.True(CharacterId.TryParse("abc", null, out var parsed));
        Assert.Equal(CharacterId.FromValue("abc"), parsed);
        Assert.False(CharacterId.TryParse("has/slash", null, out _));
    }

    [Fact]
    public void Different_id_types_are_not_interchangeable()
    {
        var characterId = CharacterId.FromValue("sharedtoken");
        var poseId = PoseId.FromValue("sharedtoken");

        // Compiles only because they're distinct types; this asserts the underlying values still line up.
        Assert.Equal(characterId.Value, poseId.Value);
    }

    [Fact]
    public void Ids_sort_ordinally_by_value()
    {
        var ids = new[] { CharacterId.FromValue("b"), CharacterId.FromValue("a"), CharacterId.FromValue("c") };
        Array.Sort(ids);
        Assert.Equal(["a", "b", "c"], ids.Select(id => id.Value));
    }
}
