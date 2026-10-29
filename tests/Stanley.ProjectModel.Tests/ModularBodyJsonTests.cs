using System.Text.Json;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>A body built from shapes in <c>character.json</c> (docs/shape-characters.md §10): sorted camelCase keys, nothing written that isn't set, and characters from before it read unchanged.</summary>
public class ModularBodyJsonTests
{
    private static CharacterDefinition Creature() => CharacterDefinition.Create("Blob") with
    {
        Modules = new ModularBody(
            Head: new ShapeModule(ModuleShapes.RoundedRectangle() with { Source = "library:shapes/head/geometric/rounded-square" }, 0.3, 0.3),
            Torso: new ShapeModule(ModuleShapes.Triangle() with { Sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal) { [ModuleSockets.Neck] = new(0.5, 0.1) } }, 0.4, 0.35, FlipY: true, Color: "shirt"),
            Arms: new LimbModule(LimbStyle.Tube, 0.32, 0.035),
            Legs: new LimbModule(LimbStyle.Taper, 0.3, 0.05, Taper: 0.4, End: ModuleShapes.Ellipse(), EndSize: 0.08, Sides: LimbSides.Left))
    };

    [Fact]
    public void A_shape_body_round_trips()
    {
        var character = Creature();

        var read = ProjectJson.Deserialize<CharacterDefinition>(ProjectJson.Serialize(character));

        Assert.Equivalent(character.Modules, read.Modules, strict: true);
    }

    [Fact]
    public void An_unchanged_save_is_byte_identical()
    {
        var json = ProjectJson.Serialize(Creature());

        Assert.Equal(json, ProjectJson.Serialize(ProjectJson.Deserialize<CharacterDefinition>(json)));
    }

    [Fact]
    public void Keys_are_camel_case_and_sorted_and_unset_values_are_left_out()
    {
        var json = ProjectJson.Serialize(Creature());
        using var document = JsonDocument.Parse(json);
        var modules = document.RootElement.GetProperty("modules");

        void Sorted(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = element.EnumerateObject().Select(p => p.Name).ToList();
                Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
                Assert.All(names, n => Assert.True(char.IsLower(n[0]), n));
                foreach (var property in element.EnumerateObject())
                    Sorted(property.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    Sorted(item);
        }
        Sorted(modules);

        Assert.Equal(["arms", "head", "legs", "torso"], modules.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["length", "style", "thickness"], modules.GetProperty("arms").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["height", "shape", "width"], modules.GetProperty("head").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["color", "flipY", "height", "shape", "width"], modules.GetProperty("torso").EnumerateObject().Select(p => p.Name));
        Assert.Equal("tube", modules.GetProperty("arms").GetProperty("style").GetString());
        Assert.Equal("left", modules.GetProperty("legs").GetProperty("sides").GetString());
        Assert.Equal(0.5, modules.GetProperty("torso").GetProperty("shape").GetProperty("sockets").GetProperty("neck").GetProperty("x").GetDouble());
        // Read-only conveniences (which slot a part is filled with, whether it's separate) aren't stored.
        var written = modules.GetRawText();
        Assert.DoesNotContain("colorSlot", written, StringComparison.Ordinal);
        Assert.DoesNotContain("isSeparate", written, StringComparison.Ordinal);
        Assert.DoesNotContain("separate", written, StringComparison.Ordinal);
        Assert.DoesNotContain("null", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_without_modules_writes_nothing_for_them_and_reads_back_without_them()
    {
        var json = ProjectJson.Serialize(CharacterDefinition.Create("Alice"));

        Assert.DoesNotContain("modules", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isModular", json, StringComparison.Ordinal);
        var read = ProjectJson.Deserialize<CharacterDefinition>(json);
        Assert.Null(read.Modules);
        Assert.False(read.IsModular);
    }

    [Fact]
    public void A_character_file_from_before_shape_bodies_reads_with_no_modules()
    {
        const string json = """
            {
              "body": { "build": 0.3, "frame": 0.5, "headsTall": 7.5, "height": 1, "muscle": 0.3 },
              "colorSlots": { "skin": "#f2c9a4" },
              "id": "abcdefghij",
              "name": "Alice",
              "skeleton": { "restLayouts": [] },
              "stickers": {}
            }
            """;

        var read = ProjectJson.Deserialize<CharacterDefinition>(json);

        Assert.Null(read.Modules);
        Assert.Equal(1.0, CharacterRig.Height(read), 9);
    }

    [Fact]
    public void A_separate_body_says_so()
    {
        var character = Creature() with { Modules = Creature().Modules! with { Separate = true } };

        var json = ProjectJson.Serialize(character);

        Assert.Contains("\"separate\": true", json, StringComparison.Ordinal);
        Assert.True(ProjectJson.Deserialize<CharacterDefinition>(json).Modules!.IsSeparate);
    }
}
