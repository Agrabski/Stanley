using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class BodyRigTests
{
    public static TheoryData<BodyPreset, ViewAngle> PresetsAndViews()
    {
        var data = new TheoryData<BodyPreset, ViewAngle>();
        foreach (var preset in BodyPresets.All)
        {
            data.Add(preset, ViewAngle.Front);
            data.Add(preset, ViewAngle.Profile);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PresetsAndViews))]
    public void Every_preset_stands_on_the_ground_with_its_head_top_exactly_at_its_height_from_either_side(BodyPreset preset, ViewAngle angle)
    {
        var body = BodyPresets.Shape(preset);
        var extent = BodyRig.Extent(body, angle);

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
    public void The_side_view_faces_right_with_its_nose_and_toes_ahead_and_its_near_arm_drawn_on_top()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile);

        Assert.Equal(ViewAngle.Profile, figure.Angle);
        Assert.Equal(ViewAngle.Profile, figure.RestLayout.Angle);
        Assert.True(figure.Extent.Right > -figure.Extent.Left, "the front (+x) reaches further than the back");
        var head = figure.Blobs[0];
        var nose = figure.Blobs[1];
        Assert.True(nose.Center.X > head.Center.X + head.RadiusX * 0.8, "a nose on the front of the head");
        Assert.Equal(2, figure.NearLimbs.Count); // near upper arm and forearm
        Assert.Equal(2, figure.NearBlobs.Count); // near hand and foot
        Assert.Empty(BodyRig.Build(BodyShape.Default).NearLimbs);
    }

    [Fact]
    public void Facing_right_the_characters_own_right_side_is_nearest_the_viewer()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile);
        Point2D Joint(HumanoidBone bone) => figure.Layout.Bones.Single(b => b.Bone == bone).Position;

        var nearArm = figure.NearLimbs[0];
        Assert.Equal(Joint(HumanoidBone.RightUpperArm), nearArm.From);
        Assert.Equal(Joint(HumanoidBone.RightHand), figure.NearLimbs[1].To);
        Assert.DoesNotContain(figure.NearLimbs, l => l.From == Joint(HumanoidBone.LeftUpperArm));
        Assert.True(Joint(HumanoidBone.RightUpperLeg).X > Joint(HumanoidBone.LeftUpperLeg).X, "the far (left) leg is set back");
    }

    [Fact]
    public void In_the_side_view_weight_shows_as_depth_and_the_front_view_width_is_unchanged_by_it()
    {
        var slim = BodyRig.Extent(BodyShape.Default with { Build = 0 }, ViewAngle.Profile);
        var heavy = BodyRig.Extent(BodyShape.Default with { Build = 1 }, ViewAngle.Profile);

        Assert.True(heavy.Right > slim.Right, "the belly sticks out further");
        Assert.Equal(slim.Height, heavy.Height, 9);
    }

    [Fact]
    public void A_skeleton_override_only_applies_to_its_own_view()
    {
        var raisedHand = new Point2D(0.3, -1.1);
        var overrides = new Skeleton([new ViewAngleRestLayout(ViewAngle.Profile, [new BoneRestPose(HumanoidBone.RightHand, raisedHand)])]);

        Assert.Contains(BodyRig.Build(BodyShape.Default, ViewAngle.Profile, overrides).NearLimbs, l => l.To == raisedHand);
        Assert.DoesNotContain(BodyRig.Build(BodyShape.Default, ViewAngle.Front, overrides).Limbs, l => l.To == raisedHand);
    }

    [Fact]
    public void A_pose_turns_each_limb_rigidly_about_its_joint_and_no_pose_is_the_rest_layout()
    {
        var rest = BodyRig.Build(BodyShape.Default).Layout;
        Assert.Equal(rest.Bones, BodyRig.Build(BodyShape.Default, ViewAngle.Front, null, new Poses.PoseData(ViewAngle.Front, [], [])).Layout.Bones);

        var posed = BodyRig.Build(BodyShape.Default, ViewAngle.Front, null, new Poses.PoseData(ViewAngle.Front,
            [new Poses.BoneRotation(HumanoidBone.LeftUpperArm, -90), new Poses.BoneRotation(HumanoidBone.LeftLowerArm, 30)], []));
        Point2D R(HumanoidBone b) => rest.Bones.Single(p => p.Bone == b).Position;
        Point2D P(HumanoidBone b) => posed.Layout.Bones.Single(p => p.Bone == b).Position;
        double Len(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        Assert.Equal(R(HumanoidBone.LeftUpperArm), P(HumanoidBone.LeftUpperArm)); // the shoulder stays
        Assert.Equal(Len(R(HumanoidBone.LeftUpperArm), R(HumanoidBone.LeftLowerArm)), Len(P(HumanoidBone.LeftUpperArm), P(HumanoidBone.LeftLowerArm)), 9);
        Assert.Equal(Len(R(HumanoidBone.LeftLowerArm), R(HumanoidBone.LeftHand)), Len(P(HumanoidBone.LeftLowerArm), P(HumanoidBone.LeftHand)), 9);
        Assert.True(P(HumanoidBone.LeftHand).Y < R(HumanoidBone.LeftHand).Y - 0.2, "a -90 degree (anticlockwise) upper arm lifts the arm");
        Assert.Equal(R(HumanoidBone.RightHand), P(HumanoidBone.RightHand));
        Assert.Equal(rest.Bones, posed.RestLayout.Bones); // rest is what rotations are measured from
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

public class BodyRigTrunkTests
{
    [Fact]
    public void Shifting_the_hips_moves_the_upper_body_by_that_fraction_of_the_height()
    {
        var body = BodyShape.Default with { Height = 1.2 };
        var rest = BodyRig.Build(body, ViewAngle.Front);
        var shifted = BodyRig.Build(body, ViewAngle.Front, null, new Poses.PoseData(ViewAngle.Front, [], [], new Point2D(0.1, 0.25)));

        Assert.Equal(rest.Extent.Top + 0.25 * 1.2, shifted.Extent.Top, 9);
        var restHead = rest.Layout.Bones.Single(b => b.Bone == HumanoidBone.Head).Position;
        var head = shifted.Layout.Bones.Single(b => b.Bone == HumanoidBone.Head).Position;
        Assert.Equal(restHead.X + 0.12, head.X, 9);
        Assert.Equal(rest.RestLayout.Bones, shifted.RestLayout.Bones);
    }

    [Fact]
    public void Hands_lie_along_the_forearm_and_a_lifted_foot_tips_with_its_shin_while_a_planted_one_stays_flat()
    {
        var pose = new Poses.PoseData(ViewAngle.Profile,
            [new Poses.BoneRotation(HumanoidBone.RightUpperArm, -90), new Poses.BoneRotation(HumanoidBone.RightUpperLeg, -40), new Poses.BoneRotation(HumanoidBone.RightLowerLeg, 60)], []);
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile, null, pose);

        var hand = figure.NearBlobs[0];
        var elbow = figure.Layout.Bones.Single(b => b.Bone == HumanoidBone.RightLowerArm).Position;
        var wrist = figure.Layout.Bones.Single(b => b.Bone == HumanoidBone.RightHand).Position;
        var forearm = Math.Atan2(wrist.Y - elbow.Y, wrist.X - elbow.X) * 180 / Math.PI;
        Assert.Equal(forearm - 90, hand.RotationDegrees, 6);

        var liftedFoot = figure.NearBlobs[1];
        Assert.NotEqual(0, liftedFoot.RotationDegrees);
        var plantedFoot = figure.Blobs.Last();
        Assert.Equal(0, plantedFoot.RotationDegrees);
    }

    [Fact]
    public void A_pose_without_a_hips_shift_leaves_it_out_of_the_file_and_one_with_it_round_trips()
    {
        var root = Directory.CreateTempSubdirectory("stanley-pose").FullName;
        try
        {
            var repository = Storage.ProjectRepository.Initialize(root, "C", new PageTrim(new PageSize(210, 297), 3));
            var issue = new Issues.Issue(Ids.IssueId.New(), "1", "", [], new SortedDictionary<Ids.CharacterId, Ids.CharacterRevisionId>());
            repository.SaveIssue(issue);
            var page = new Issues.Page(Ids.PageId.New(), "p", null, []);
            repository.SavePage(issue.Id, page);
            Issues.CharacterInstance Instance(Point2D? shift) => new(Ids.CharacterId.New(), new Issues.CharacterPlacement(new Point2D(1, 2), 3, false), null,
                new Poses.PoseData(ViewAngle.Front, [new Poses.BoneRotation(HumanoidBone.Spine, 12.5)], [], shift), null);
            var panel = new Issues.Panel(Ids.PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 50, 50)), null,
                [Instance(null), Instance(new Point2D(0.05, 0.2))], []);
            repository.SavePanel(issue.Id, page.Id, panel);

            var loaded = repository.LoadPanel(issue.Id, page.Id, panel.Id);
            Assert.Null(loaded.CharacterInstances[0].Pose.HipsShift);
            Assert.Equal(new Point2D(0.05, 0.2), loaded.CharacterInstances[1].Pose.HipsShift);
            var file = File.ReadAllText(Directory.GetFiles(root, $"{panel.Id.Value}.json", SearchOption.AllDirectories).Single());
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(file, "hipsShift"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
