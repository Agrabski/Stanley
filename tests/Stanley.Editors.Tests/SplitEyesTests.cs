using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors.Tests;

/// <summary>The Look tab's split eyes (docs/sticker-system.md §21, GitHub issues #100-#102): the toggle, the two galleries and colours it swaps in.</summary>
public sealed class SplitEyesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-split-eyes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (EditorSession Session, CharacterEditorViewModel Editor) NewCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        return (session, session.Characters.Items.Single(i => i.Id == created.Id).Editor);
    }

    private static StickerChoice Choice(CharacterEditorViewModel editor, string slot, string name) =>
        editor.Gallery(slot).Choices.Single(c => c.Label == name);

    /// <summary>Swaps the default face's "Dots" (which has no colour of its own) for "Round", which does - so a colour swatch shows.</summary>
    private static void WearRoundEyes(CharacterEditorViewModel editor)
    {
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Eyes, "None"));
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Eyes, "Round"));
    }

    [Fact]
    public void A_new_character_can_split_its_default_face_eyes()
    {
        var (_, editor) = NewCharacter();

        Assert.False(editor.IsEyesSplit);
        Assert.True(editor.CanSplitEyes); // the default face already wears eyes
        Assert.Single(editor.FaceGalleries, g => g.Label == "Eyes");
    }

    [Fact]
    public void Splitting_swaps_the_eyes_gallery_for_two_and_is_one_undo_step()
    {
        var (session, editor) = NewCharacter();

        editor.IsEyesSplit = true;

        Assert.True(editor.IsEyesSplit);
        Assert.DoesNotContain(editor.FaceGalleries, g => g.Label == "Eyes");
        Assert.Contains(editor.FaceGalleries, g => g.Label == "Left eye");
        Assert.Contains(editor.FaceGalleries, g => g.Label == "Right eye");

        session.Workspace.History.Undo();
        Assert.False(editor.IsEyesSplit);
        Assert.Contains(editor.FaceGalleries, g => g.Label == "Eyes");
    }

    [Fact]
    public void Picking_a_different_design_for_one_eye_leaves_the_other_alone()
    {
        var (_, editor) = NewCharacter();
        editor.IsEyesSplit = true;
        var rightGallery = editor.EyeSideGallery(LimbSide.Right);
        var lashes = rightGallery.Choices.First(c => c.Label != rightGallery.Current);

        rightGallery.Wear.Execute(lashes);

        Assert.Equal("Dots", editor.EyeSideGallery(LimbSide.Left).Current);
        Assert.Equal(lashes.Label, editor.EyeSideGallery(LimbSide.Right).Current);
    }

    [Fact]
    public void Splitting_shows_left_and_right_colour_swatches_instead_of_one()
    {
        var (_, editor) = NewCharacter();
        WearRoundEyes(editor);

        editor.IsEyesSplit = true;

        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot == StickerSlots.Eyes);
        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.EyesLeft && e.Label == "Left eye");
        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.EyesRight && e.Label == "Right eye");
    }

    [Fact]
    public void Unsplitting_goes_back_to_one_eyes_gallery_and_swatch()
    {
        var (_, editor) = NewCharacter();
        WearRoundEyes(editor);
        editor.IsEyesSplit = true;

        editor.IsEyesSplit = false;

        Assert.False(editor.IsEyesSplit);
        Assert.False(LookEditing.IsEyesSplit(editor.Working));
        Assert.Contains(editor.FaceGalleries, g => g.Label == "Eyes");
        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.Eyes);
    }
}
