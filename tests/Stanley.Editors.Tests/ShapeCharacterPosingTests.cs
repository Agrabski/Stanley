using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>
/// A character built from shapes posed on the page (docs/shape-characters.md §7): the green
/// dots and rings are only on the parts it has, so there's nothing to grab where a part is
/// missing, and the pose gallery previews it.
/// </summary>
public sealed class ShapeCharacterPosingTests
{
    /// <summary>A comic with one panel holding a character built as the creature preset <paramref name="creature"/>, selected.</summary>
    private static (PageEditorViewModel Page, PanelId Panel, CharacterDefinition Character) Placed(string creature)
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var page = session.Navigator.CurrentPage.Editor;
        var created = session.Characters.CreateCharacter();
        session.Characters.EditCharacter(created.Id, "Build from shapes", c => c with { Modules = CreaturePresets.Get(creature)!.Body }, null);
        var panel = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panel);
        return (page, panel, page.CharacterSnapshot[created.Id]);
    }

    private static CharacterInstance Instance(PageEditorViewModel page, PanelId panel) => page.Working.Panels[panel].CharacterInstances[0];

    [Theory]
    [InlineData("Head-on-legs", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Head })]
    [InlineData("Bean", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest })]
    [InlineData("Blob", new[] { Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest })]
    [InlineData("Hero", new[] { Limb.LeftArm, Limb.RightArm, Limb.LeftLeg, Limb.RightLeg }, new[] { TrunkPart.Hips, TrunkPart.Chest, TrunkPart.Head })]
    public void The_page_shows_handles_only_for_the_parts_the_character_has(string creature, Limb[] limbs, TrunkPart[] trunk)
    {
        var (page, panel, _) = Placed(creature);
        foreach (var view in new[] { ViewAngle.Front, ViewAngle.Profile })
        {
            page.SetCharacterView(panel, 0, view);
            var instance = Instance(page, panel);

            Assert.Equal(limbs, page.LimbHandles(instance).Select(h => h.Limb));
            Assert.Equal(limbs, page.BendHandles(instance).Select(h => h.Limb));
            Assert.Equal(trunk, page.TrunkHandles(instance).Select(h => h.Part));
            Assert.All(page.LimbHandles(instance).Concat(page.BendHandles(instance)), h => Assert.True(double.IsFinite(h.Point.X) && double.IsFinite(h.Point.Y)));
        }
    }

    [Fact]
    public void A_generated_character_keeps_every_handle()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var page = session.Navigator.CurrentPage.Editor;
        var created = session.Characters.CreateCharacter();
        var panel = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panel);
        var instance = Instance(page, panel);

        Assert.Equal(Enum.GetValues<Limb>(), page.LimbHandles(instance).Select(h => h.Limb));
        Assert.Equal(Enum.GetValues<Limb>(), page.BendHandles(instance).Select(h => h.Limb));
        Assert.Equal(Enum.GetValues<TrunkPart>(), page.TrunkHandles(instance).Select(h => h.Part));
        Assert.Contains("hands/feet to reach, hips to crouch (feet stay put), chest to lean, head to tilt", page.Hint);
    }

    [Fact]
    public void The_status_hint_names_only_the_dots_the_character_has()
    {
        var (blob, _, _) = Placed("Blob");
        Assert.Contains("feet to reach, hips to crouch (feet stay put), chest to lean ·", blob.Hint);

        var (headOnLegs, _, _) = Placed("Head-on-legs");
        Assert.Contains("hands/feet to reach, hips to crouch (feet stay put), head to tilt ·", headOnLegs.Hint);
        Assert.DoesNotContain("chest", headOnLegs.Hint);
    }

    [Fact]
    public void Dragging_a_hand_the_character_hasnt_got_or_tilting_a_missing_head_changes_nothing()
    {
        var (page, panel, _) = Placed("Blob");
        var before = Instance(page, panel);

        page.BeginPoseLimb(panel, 0, Limb.RightArm);
        page.UpdatePoseLimb(panel, 0, Limb.RightArm, new Point2D(10, 10));
        page.EndGesture(commit: true);
        page.BeginPoseBend(panel, 0, Limb.LeftArm);
        page.UpdatePoseBend(panel, 0, Limb.LeftArm, new Point2D(10, 10));
        page.EndGesture(commit: true);

        Assert.Equal(before, Instance(page, panel));

        var (bean, beanPanel, _) = Placed("Bean");
        var standing = Instance(bean, beanPanel);
        bean.BeginPoseTrunk(beanPanel, 0, TrunkPart.Head);
        bean.UpdatePoseTrunk(beanPanel, 0, TrunkPart.Head, new Point2D(0, 0), new Point2D(5, 5));
        bean.EndGesture(commit: true);
        Assert.Equal(standing, Instance(bean, beanPanel));
    }

    [Fact]
    public void Dragging_a_foot_poses_a_shape_character_in_one_undo_step()
    {
        var (page, panel, _) = Placed("Head-on-legs");
        var foot = page.LimbHandles(Instance(page, panel)).Single(h => h.Limb == Limb.LeftLeg).Point;
        var lifted = new Point2D(foot.X + 3, foot.Y - 6);

        page.BeginPoseLimb(panel, 0, Limb.LeftLeg);
        page.UpdatePoseLimb(panel, 0, Limb.LeftLeg, lifted);
        page.EndGesture(commit: true);

        var moved = page.LimbHandles(Instance(page, panel)).Single(h => h.Limb == Limb.LeftLeg).Point;
        Assert.True(Math.Abs(moved.X - lifted.X) < 0.1 && Math.Abs(moved.Y - lifted.Y) < 0.1, $"the foot went to {moved}, not {lifted}");
        Assert.True(page.SelectedCharacterIsPosed);
    }

    [Theory]
    [InlineData("Bean")]
    [InlineData("Blob")]
    [InlineData("Robot")]
    [InlineData("Head-on-legs")]
    public void The_pose_gallery_previews_every_preset_on_a_shape_character_and_applies_it(string creature)
    {
        var (page, panel, character) = Placed(creature);

        var choices = page.PoseChoices;

        Assert.Equal(PosePresets.All.Count, choices.Count);
        using var bitmap = new SKBitmap(60, 80);
        using var canvas = new SKCanvas(bitmap);
        foreach (var choice in choices)
        {
            Assert.Same(character, choice.Character);
            // As the gallery's preview draws it: standing in its button, the preset's pose on it.
            var extent = CharacterRenderers.Default.Extent(choice.Character, choice.Pose.ViewAngle, choice.Pose);
            Assert.True(extent.Height > 0 && double.IsFinite(extent.Height), $"{choice.Name}: {extent}");
            canvas.Clear(SKColors.White);
            CharacterRenderers.Default.Draw(canvas, choice.Character, new CharacterPlacement(new Point2D(30, 76), 70 / extent.Height, false), 1, choice.Pose.ViewAngle, choice.Pose);
            Assert.Contains(bitmap.Pixels, p => p != SKColors.White);
        }

        page.ApplyPosePresetCommand.Execute(choices.Single(c => c.Preset.Preset == PosePreset.Walk));
        Assert.Equal(ViewAngle.Profile, Instance(page, panel).Pose.ViewAngle);
        Assert.True(page.SelectedCharacterIsPosed);
    }
}
