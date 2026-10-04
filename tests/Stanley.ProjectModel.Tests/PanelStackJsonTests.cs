using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>The fields arranging a panel's stacking by hand (Layers pane, issue #17) added: written only when set, so older panel files read - and write back - unchanged.</summary>
public class PanelStackJsonTests
{
    private static CharacterInstance Character(CharacterInstanceId? id = null) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(50, 80), 1, false), null, new PoseData(ViewAngle.Front, [], []), null, Id: id);

    private static Bubble Bubble() =>
        new(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(20, 20, 40, 20)).Anchors), BubbleStylePreset.Speech, [], "Hi");

    private static Panel Panel(IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, IReadOnlyList<string>? stack = null) =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 80)), null, characters, bubbles, Stack: stack);

    [Fact]
    public void A_panel_with_the_usual_stacking_writes_neither_a_stack_nor_character_ids()
    {
        var json = ProjectJson.Serialize(Panel([Character()], [Bubble()]));

        Assert.DoesNotContain("stack", json, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var character = document.RootElement.GetProperty("characterInstances")[0];
        Assert.False(character.TryGetProperty("id", out _));
    }

    [Fact]
    public void A_character_with_an_id_writes_it_beside_its_other_fields()
    {
        var id = CharacterInstanceId.New();

        using var document = System.Text.Json.JsonDocument.Parse(ProjectJson.Serialize(Panel([Character(id)], [])));

        Assert.Equal(id.Value, document.RootElement.GetProperty("characterInstances")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void A_panel_file_from_before_arranged_stacking_reads_as_the_usual_stacking()
    {
        var json = ProjectJson.Serialize(Panel([Character()], [Bubble()]));

        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Null(read.Stack);
        Assert.Null(read.CharacterInstances[0].Id);
        Assert.Equal(ProjectJson.Serialize(read), json); // and writes back byte for byte the same
    }

    [Fact]
    public void An_arranged_stack_and_the_character_ids_it_names_round_trip()
    {
        var character = Character(CharacterInstanceId.New());
        var bubble = Bubble();
        var panel = Panel([character], [bubble], [PanelStack.Token(bubble), PanelStack.Token(character)!]);

        var json = ProjectJson.Serialize(panel);
        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Contains("\"stack\": [", json, StringComparison.Ordinal);
        Assert.Contains("b:" + bubble.Id.Value, json, StringComparison.Ordinal);
        Assert.Contains("c:" + character.Id!.Value.Value, json, StringComparison.Ordinal);
        Assert.Equivalent(panel, read, strict: true);
        Assert.Equal(PanelStack.Order(panel), PanelStack.Order(read));
    }

    [Fact]
    public void An_empty_stack_in_a_file_reads_as_none()
    {
        var json = ProjectJson.Serialize(Panel([], [Bubble()])).Replace("\"shape\"", "\"stack\": [],\n  \"shape\"", StringComparison.Ordinal);
        Assert.Contains("\"stack\": []", json, StringComparison.Ordinal);

        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Null(read.Stack);
        Assert.DoesNotContain("stack", ProjectJson.Serialize(read), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_token_that_makes_no_sense_does_not_stop_the_panel_reading()
    {
        var bubble = Bubble();
        var panel = Panel([], [bubble], ["not a token at all", "b:", "c:with space", PanelStack.Token(bubble)]);

        var read = ProjectJson.Deserialize<Panel>(ProjectJson.Serialize(panel));

        Assert.Equal([new StackItem(StackKind.Bubble, 0)], PanelStack.Order(read));
    }

    [Fact]
    public void Properties_this_version_does_not_know_are_ignored_when_a_panel_is_read()
    {
        var json = ProjectJson.Serialize(Panel([], [Bubble()])).Replace("\"shape\"", "\"somethingFromTheFuture\": {\"a\": [1, 2]},\n  \"shape\"", StringComparison.Ordinal);

        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Single(read.Bubbles);
    }
}
