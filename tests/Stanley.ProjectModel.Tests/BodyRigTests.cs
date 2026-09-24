using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class BodyRigTests
{
    public static TheoryData<BodyPreset> Presets() => new(BodyPresets.All);

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_preset_stands_on_the_ground_with_its_head_top_exactly_at_its_height(BodyPreset preset)
    {
        var body = BodyPresets.Shape(preset);
        var extent = BodyRig.Extent(body);

        Assert.Equal(-body.Height, extent.Top, 9);
        Assert.Equal(0, extent.Bottom, 9);
    }

    [Fact]
    public void The_head_is_height_over_heads_tall()
    {
        var body = BodyShape.Default with { Height = 1.2, HeadsTall = 6 };
        var head = BodyRig.Build(body).Blobs[0];

        Assert.Equal(1.2 / 6, head.RadiusY * 2, 9);
        Assert.Equal(-1.2, head.Center.Y - head.RadiusY, 9);
    }

    [Fact]
    public void Weight_muscle_and_frame_change_widths_never_heights()
    {
        var slim = BodyRig.Extent(BodyShape.Default with { Build = 0, Muscle = 0, Frame = 0 });
        var heavy = BodyRig.Extent(BodyShape.Default with { Build = 1, Muscle = 1, Frame = 1 });

        Assert.Equal(slim.Top, heavy.Top, 9);
        Assert.Equal(slim.Bottom, heavy.Bottom, 9);
        Assert.True(heavy.Width > slim.Width * 1.2);
    }

    [Fact]
    public void Widths_grow_with_weight()
    {
        var widths = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }.Select(b => BodyRig.Extent(BodyShape.Default with { Build = b }).Width).ToList();
        Assert.Equal(widths.OrderBy(w => w), widths);
    }

    [Fact]
    public void The_generated_body_is_left_right_symmetric()
    {
        var extent = BodyRig.Extent(BodyPresets.Shape(BodyPreset.Heroic));
        Assert.Equal(-extent.Left, extent.Right, 9);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_rather_than_breaking_the_body()
    {
        var body = new BodyShape(double.NaN, 5, -1, 100, 0.5).Normalized();

        Assert.Equal(1.0, body.Height);
        Assert.Equal(1, body.Build);
        Assert.Equal(0, body.Muscle);
        Assert.Equal(BodyShape.MaxHeadsTall, body.HeadsTall);
    }

    [Fact]
    public void A_skeleton_override_moves_that_joint_and_the_limb_follows()
    {
        var body = BodyShape.Default;
        var raisedHand = new Point2D(0.4, -1.1);
        var overrides = new Skeleton([new ViewAngleRestLayout(ViewAngle.Front, [new BoneRestPose(HumanoidBone.LeftHand, raisedHand)])]);

        var figure = BodyRig.Build(body, overrides);

        Assert.Equal(raisedHand, figure.RestLayout.Bones.Single(b => b.Bone == HumanoidBone.LeftHand).Position);
        Assert.Contains(figure.Limbs, l => l.To == raisedHand);
        Assert.True(figure.Extent.Right > BodyRig.Extent(body).Right, "the raised arm reaches further out");
    }

    [Fact]
    public void Placement_maps_figure_space_onto_the_page_scaled_about_the_feet_and_mirrored()
    {
        var placement = new CharacterPlacement(new Point2D(100, 200), UnitHeightMm: 50, Mirrored: true);

        Assert.Equal(new Point2D(90, 150), placement.ToPage(new Point2D(0.2, -1)));
        Assert.Equal(Rect2D.FromEdges(95, 150, 110, 200), placement.ToPage(Rect2D.FromEdges(-0.2, -1, 0.1, 0)));
    }
}

public sealed class CharacterListingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-character-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListCharacters_finds_every_character_folder_sorted_by_name_and_DeleteCharacter_removes_one()
    {
        var repository = ProjectRepository.Initialize(_root, "Comic", new PageTrim(new PageSize(210, 297), 3));
        var zed = CharacterDefinition.Create("Zed", BodyPresets.Shape(BodyPreset.Heavy));
        var amy = CharacterDefinition.Create("amy");
        repository.SaveCharacter(zed);
        repository.SaveCharacter(amy);

        Assert.Equal(["amy", "Zed"], repository.ListCharacters().Select(c => c.Name));
        Assert.Equal(zed.Body, repository.ListCharacters()[1].Body);

        repository.DeleteCharacter(zed.Id);
        Assert.Equal(["amy"], repository.ListCharacters().Select(c => c.Name));
        repository.DeleteCharacter(zed.Id); // already gone: a no-op
    }

    [Fact]
    public void A_character_file_written_before_bodies_existed_gets_the_default_body()
    {
        var repository = ProjectRepository.Initialize(_root, "Comic", new PageTrim(new PageSize(210, 297), 3));
        var character = CharacterDefinition.Create("Old");
        repository.SaveCharacter(character);
        var file = Directory.GetFiles(Path.Combine(_root, "characters"), "character.json", SearchOption.AllDirectories).Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json.Remove("body");
        File.WriteAllText(file, json.ToJsonString());

        Assert.Equal(BodyShape.Default, repository.LoadCharacter(character.Id).Body);
    }

    [Fact]
    public void A_character_saves_its_body_as_plain_sorted_numbers()
    {
        var repository = ProjectRepository.Initialize(_root, "Comic", new PageTrim(new PageSize(210, 297), 3));
        repository.SaveCharacter(CharacterDefinition.Create("Alice", new BodyShape(0.9, 0.2, 0.4, 7, 0.6)));
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "characters"), "character.json", SearchOption.AllDirectories).Single());

        Assert.Contains("\"body\": {\n    \"build\": 0.2,\n    \"frame\": 0.6,\n    \"headsTall\": 7,\n    \"height\": 0.9,\n    \"muscle\": 0.4\n  }", text.Replace("\r\n", "\n"));
        Assert.Contains("\"skin\": \"#f2c9a4\"", text);
    }
}
