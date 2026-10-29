using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.StickerLibrary;

namespace Stanley.Editing.Tests;

/// <summary>
/// Posing every shipped creature preset (docs/shape-characters.md §7) - and bodies missing
/// one part or another - with the mannequin's drags, presets, Turn and Mirror: the same
/// guarantees <see cref="CharacterPosingTests"/> pins down for a generated body, on bodies
/// whose limbs are stubby, on a head, or not there at all.
/// </summary>
public class CreaturePosingTests
{
    /// <summary>Every creature preset, the default shape body, and a few with parts left out the presets don't leave out.</summary>
    private static readonly IReadOnlyDictionary<string, ModularBody> Bodies = new Dictionary<string, ModularBody>(
        CreaturePresets.All.Select(p => KeyValuePair.Create(p.Name, p.Body)))
    {
        ["Default"] = CreaturePresets.Default,
        ["No legs"] = CreaturePresets.Default with { Legs = null },
        ["One arm, one leg"] = CreaturePresets.Default with
        {
            Arms = CreaturePresets.DefaultArms with { Sides = LimbSides.Left },
            Legs = CreaturePresets.DefaultLegs with { Sides = LimbSides.Right },
        },
        ["Just a head"] = new ModularBody(CreaturePresets.DefaultHead, null, null, null),
    };

    public static TheoryData<string> BodyNames() => [.. Bodies.Keys];

    public static TheoryData<string, ViewAngle, bool> BodyCases()
    {
        var data = new TheoryData<string, ViewAngle, bool>();
        foreach (var name in Bodies.Keys)
            foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
                foreach (var mirrored in new[] { false, true })
                    data.Add(name, angle, mirrored);
        return data;
    }

    internal static CharacterDefinition Creature(string name) => CharacterDefinition.Create(name) with { Modules = Bodies[name] };

    internal static CharacterInstance Placed(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, bool mirrored = false) =>
        new(character.Id, new CharacterPlacement(new Point2D(100, 200), 100, mirrored), null, new PoseData(angle, [], []), null);

    internal static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    internal static bool Has(CharacterDefinition character, CharacterInstance instance, Limb limb) =>
        CharacterPosing.Has(CharacterPosing.Figure(character, instance), limb);

    internal static void AssertSamePose(CharacterDefinition character, CharacterInstance expected, CharacterInstance actual, string what)
    {
        Assert.Equal(expected.Pose.ViewAngle, actual.Pose.ViewAngle);
        foreach (var limb in CharacterPosing.Limbs(character, expected))
        {
            Assert.True(Distance(CharacterPosing.EndPoint(character, expected, limb), CharacterPosing.EndPoint(character, actual, limb)) < 0.01, $"{what}: {limb} end");
            Assert.True(Distance(CharacterPosing.BendPoint(character, expected, limb), CharacterPosing.BendPoint(character, actual, limb)) < 0.01, $"{what}: {limb} bend");
        }
        foreach (var part in CharacterPosing.TrunkParts(character))
            Assert.True(Distance(CharacterPosing.TrunkPoint(character, expected, part), CharacterPosing.TrunkPoint(character, actual, part)) < 0.01, $"{what}: {part}");
    }

    /// <summary>#76 on shape bodies: grabbing a hand or foot and letting go where it was leaves the elbow or knee where it was, in every preset pose.</summary>
    [Theory]
    [MemberData(nameof(BodyCases))]
    public void Grabbing_a_hand_or_foot_of_any_preset_without_moving_it_leaves_the_elbow_or_knee_where_it_is(string body, ViewAngle angle, bool mirrored)
    {
        var character = Creature(body);
        foreach (var preset in PosePresets.All)
        {
            var posed = PosePresets.Apply(character, Placed(character, angle, mirrored), preset, angle);

            foreach (var limb in Enum.GetValues<Limb>())
            {
                var held = CharacterPosing.Reach(character, posed, limb, CharacterPosing.EndPoint(character, posed, limb), CharacterPosing.BendSign(character, posed, limb));

                if (!Has(character, posed, limb))
                {
                    Assert.Same(posed, held);
                    continue;
                }
                var (before, after) = (CharacterPosing.BendPoint(character, posed, limb), CharacterPosing.BendPoint(character, held, limb));
                Assert.True(Distance(before, after) < 0.05, $"{body} {preset.Name} {angle}: the {limb}'s joint moved from {before} to {after}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void Every_pose_preset_moves_the_hands_and_feet_the_body_has_and_leaves_out_the_ones_it_hasnt(string body)
    {
        var character = Creature(body);
        foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
        {
            var standing = Placed(character, angle);
            foreach (var preset in PosePresets.All)
            {
                var posed = PosePresets.Apply(character, standing, preset, angle);

                Assert.All(posed.Pose.BoneRotations, r => Assert.True(double.IsFinite(r.Degrees), $"{body} {preset.Name}: {r}"));
                Assert.True(double.IsFinite(CharacterPosing.Figure(character, posed).Extent.Height));
                foreach (var limb in Enum.GetValues<Limb>())
                {
                    var (root, middle, _) = CharacterPosing.Chain(limb);
                    if (!Has(character, standing, limb))
                    {
                        Assert.DoesNotContain(posed.Pose.BoneRotations, r => r.Bone == root || r.Bone == middle);
                        continue;
                    }
                    if (preset.In(angle).Goals.All(g => g.Limb != limb))
                        continue;
                    var moved = Distance(CharacterPosing.EndPoint(character, standing, limb), CharacterPosing.EndPoint(character, posed, limb));
                    Assert.True(moved > 0.2, $"{body} {preset.Name} {angle}: the {limb} moved only {moved}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void Mirroring_a_preset_twice_gives_it_back(string body)
    {
        var character = Creature(body);
        foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
            foreach (var preset in PosePresets.All)
            {
                var posed = PosePresets.Apply(character, Placed(character, angle), preset, angle);

                var twice = CharacterPosing.MirrorPose(CharacterPosing.MirrorPose(posed));

                Assert.Equal(posed.Pose.HipsShift, twice.Pose.HipsShift);
                Assert.Equal(posed.Pose.BoneRotations.OrderBy(r => r.Bone), twice.Pose.BoneRotations.OrderBy(r => r.Bone));
                AssertSamePose(character, posed, twice, $"{body} {preset.Name} {angle}");
            }
    }

    [Theory]
    [InlineData("Head-on-legs", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Head })]
    [InlineData("Bean", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest })]
    [InlineData("Blob", new[] { Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest })]
    [InlineData("No legs", new[] { Limb.LeftArm, Limb.RightArm }, new[] { TrunkPart.Hips, TrunkPart.Chest, TrunkPart.Head })]
    [InlineData("One arm, one leg", new[] { Limb.LeftArm, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest, TrunkPart.Head })]
    [InlineData("Just a head", new Limb[0], new[] { TrunkPart.Hips, TrunkPart.Head })]
    [InlineData("Default", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest, TrunkPart.Head })]
    public void The_parts_to_pose_are_the_ones_the_body_has(string body, Limb[] limbs, TrunkPart[] trunk)
    {
        var character = Creature(body);
        foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
            Assert.Equal(limbs, CharacterPosing.Limbs(character, Placed(character, angle)));
        Assert.Equal(trunk, CharacterPosing.TrunkParts(character));
    }

    [Fact]
    public void A_generated_body_has_every_part_to_pose()
    {
        var alice = CharacterDefinition.Create("Alice");
        Assert.Equal(Enum.GetValues<Limb>(), CharacterPosing.Limbs(alice, Placed(alice)));
        Assert.Equal(Enum.GetValues<TrunkPart>(), CharacterPosing.TrunkParts(alice));
    }

    [Fact]
    public void Dragging_a_hand_or_elbow_the_body_hasnt_got_changes_nothing()
    {
        var blob = Creature("Blob");
        var instance = Placed(blob);
        foreach (var arm in new[] { Limb.LeftArm, Limb.RightArm })
        {
            var at = CharacterPosing.EndPoint(blob, instance, arm);
            Assert.Same(instance, CharacterPosing.Reach(blob, instance, arm, new Point2D(at.X + 10, at.Y - 10), CharacterPosing.BendSign(blob, instance, arm)));
            Assert.Same(instance, CharacterPosing.Bend(blob, instance, arm, new Point2D(at.X + 10, at.Y - 10)));
        }
    }

    [Fact]
    public void Leaning_a_body_with_no_torso_tilts_its_head_about_the_hips_and_the_feet_stay()
    {
        var character = Creature("Head-on-legs");
        var standing = Placed(character);
        var leaned = standing with { Pose = standing.Pose with { BoneRotations = [new BoneRotation(HumanoidBone.Spine, 15)] } };

        var (hips, head) = (CharacterPosing.TrunkPoint(character, standing, TrunkPart.Hips), CharacterPosing.TrunkPoint(character, standing, TrunkPart.Head));
        var tilted = CharacterPosing.TrunkPoint(character, leaned, TrunkPart.Head);

        Assert.True(Distance(hips, CharacterPosing.TrunkPoint(character, leaned, TrunkPart.Hips)) < 0.01, "the hips stay");
        Assert.True(tilted.X > head.X + 3, $"the head leans over, from {head} to {tilted}");
        Assert.Equal(Distance(hips, head), Distance(hips, tilted), 1);
        foreach (var leg in new[] { Limb.LeftLeg, Limb.RightLeg })
            Assert.True(Distance(CharacterPosing.EndPoint(character, standing, leg), CharacterPosing.EndPoint(character, leaned, leg)) < 0.01, $"the {leg} stays");
    }

    [Fact]
    public void Tilting_the_head_of_a_body_with_no_torso_turns_it_on_its_legs()
    {
        var character = Creature("Head-on-legs");
        var instance = Placed(character);
        var head = CharacterPosing.TrunkPoint(character, instance, TrunkPart.Head);

        var tilted = CharacterPosing.TiltHead(character, instance, new Point2D(head.X - 10, head.Y + 2));

        Assert.True(CharacterPosing.TrunkPoint(character, tilted, TrunkPart.Head).X < head.X - 3);
    }

    [Fact]
    public void Tilting_the_head_of_a_body_with_no_head_does_nothing()
    {
        var bean = Creature("Bean");
        var instance = Placed(bean);
        var top = CharacterPosing.TrunkPoint(bean, instance, TrunkPart.Head);

        Assert.Same(instance, CharacterPosing.TiltHead(bean, instance, new Point2D(top.X + 20, top.Y)));
    }

    [Fact]
    public void Moving_the_hips_of_a_body_with_no_legs_shifts_the_whole_body_without_sinking_it()
    {
        var character = Creature("No legs");
        var instance = Placed(character);
        var hips = CharacterPosing.TrunkPoint(character, instance, TrunkPart.Hips);

        Assert.Equal(0, CharacterPosing.MaxHipsDrop(character, instance));
        var moved = CharacterPosing.MoveHips(character, instance, new Point2D(6, 10));

        var after = CharacterPosing.TrunkPoint(character, moved, TrunkPart.Hips);
        Assert.True(after.X > hips.X + 3, "it shifts sideways");
        Assert.True(after.Y <= hips.Y + 1e-6, "it doesn't sink into the floor");
    }

    [Fact]
    public void A_body_with_one_leg_crouches_on_that_leg()
    {
        var character = Creature("One arm, one leg");
        var instance = Placed(character);
        var foot = CharacterPosing.EndPoint(character, instance, Limb.RightLeg);

        Assert.True(CharacterPosing.MaxHipsDrop(character, instance) > 0.05);
        var crouched = CharacterPosing.MoveHips(character, instance, new Point2D(0, 6));

        Assert.True(CharacterPosing.TrunkPoint(character, crouched, TrunkPart.Hips).Y > CharacterPosing.TrunkPoint(character, instance, TrunkPart.Hips).Y + 3);
        Assert.True(Distance(foot, CharacterPosing.EndPoint(character, crouched, Limb.RightLeg)) < 0.1, "the foot stays planted");
    }
}

/// <summary>Turning creature presets side on and back (<see cref="CharacterPosing.Turn"/>) - apart from <see cref="CreaturePosingTests"/> so the two run side by side: recognising a preset is the slow part.</summary>
public class CreatureTurnTests
{
    public static TheoryData<string> BodyNames() => CreaturePosingTests.BodyNames();

    private static CharacterDefinition Creature(string name) => CreaturePosingTests.Creature(name);

    private static CharacterInstance Placed(CharacterDefinition character, ViewAngle angle = ViewAngle.Front) => CreaturePosingTests.Placed(character, angle);

    private static void AssertSamePose(CharacterDefinition character, CharacterInstance expected, CharacterInstance actual, string what) =>
        CreaturePosingTests.AssertSamePose(character, expected, actual, what);

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void Turning_a_preset_side_on_and_back_gives_the_preset_back(string body)
    {
        var character = Creature(body);
        // Mirrored presets turn as the mannequin's do (CharacterPosingTests); a body with one
        // arm and one leg is the one where mirroring could go wrong, and recognising a preset
        // is slow, so only it is turned mirrored as well.
        bool[] mirroring = body == "One arm, one leg" ? [false, true] : [false];
        foreach (var preset in PosePresets.All)
            foreach (var mirrored in mirroring)
                foreach (var from in new[] { ViewAngle.Front, ViewAngle.Profile })
                {
                    var to = from == ViewAngle.Front ? ViewAngle.Profile : ViewAngle.Front;
                    CharacterInstance Posed(ViewAngle view)
                    {
                        var applied = PosePresets.Apply(character, Placed(character, view), preset, view);
                        return mirrored ? CharacterPosing.MirrorPose(applied) : applied;
                    }
                    var posed = Posed(from);
                    // A preset this body draws in the other view just as it draws another one -
                    // an armless blob's cheer from the front is standing, a lone head's shrug
                    // side on is its wave - can only come back as whichever the gallery lists
                    // first; and one drawn the same either way round there (a front cheer)
                    // comes back the way the gallery gives it, not mirrored.
                    if (PosePresets.Recognize(character, Posed(to)) is not { } known || known.Preset.Preset != preset.Preset || known.Mirrored != mirrored)
                        continue;

                    var turned = CharacterPosing.Turn(character, posed, to);
                    var back = CharacterPosing.Turn(character, turned, from);

                    Assert.Equal(posed.Placement, turned.Placement);
                    Assert.Equal(to, turned.Pose.ViewAngle);
                    AssertSamePose(character, posed, back, $"{body} {preset.Name} from {from} mirrored={mirrored}");
                }
    }

    [Theory]
    [MemberData(nameof(BodyNames))]
    public void Turning_a_preset_on_a_body_with_all_its_limbs_gives_the_preset_as_the_other_view_draws_it(string body)
    {
        var character = Creature(body);
        if (CharacterPosing.Limbs(character, Placed(character)).Count < 4)
            return; // a preset that differs from another only in a limb this body hasn't got poses it alike
        foreach (var preset in PosePresets.All)
        {
            var posed = PosePresets.Apply(character, Placed(character), preset, ViewAngle.Front);

            var turned = CharacterPosing.Turn(character, posed, ViewAngle.Profile);

            AssertSamePose(character, PosePresets.Apply(character, Placed(character, ViewAngle.Profile), preset, ViewAngle.Profile), turned, $"{body} {preset.Name}");
        }
    }
}
