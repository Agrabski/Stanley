using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;
using Stanley.Rendering;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>The character editor for a body built from shapes (docs/shape-characters.md §9): New character › From shapes, the Shapes tab and the Module tab.</summary>
public sealed class ShapeCharacterEditorTests
{
    private static (EditorSession Session, CharacterEditorViewModel Editor) NewShapeCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        session.Characters.NewShapeCharacterCommand.Execute(null);
        return (session, session.Characters.Items.Single().Editor);
    }

    private static string Json(ModularBody? body) => body is null ? "" : ProjectJson.Serialize(body);

    [Fact]
    public void From_shapes_makes_the_default_creature_with_the_default_face_and_opens_it()
    {
        var (session, editor) = NewShapeCharacter();

        Assert.True(editor.IsModular);
        Assert.False(editor.IsGeneratedBody);
        Assert.Equal(Json(CreaturePresets.Default), Json(editor.Working.Modules));
        Assert.Same(editor, session.Characters.Current?.Editor);
        foreach (var key in Stanley.StickerLibrary.StickerLibrary.DefaultFace)
            Assert.Contains(editor.Working.Wardrobe.Stickers.Values, s => s.Sticker.Source?.EndsWith(key) == true);
        // The human path is unchanged.
        session.Characters.NewCharacterCommand.Execute(null);
        Assert.False(session.Characters.Items.Last().Editor.IsModular);
    }

    [Fact]
    public void Insert_From_shapes_on_the_page_places_it_and_opens_it()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var page = session.Navigator.CurrentPage.Editor;

        Assert.True(page.NewShapeCharacterCommand.CanExecute(null));
        page.NewShapeCharacterCommand.Execute(null);

        var item = session.Characters.Items.Single();
        Assert.True(item.Character.IsModular);
        Assert.Single(page.Working.Panels[page.Working.PanelOrder[0]].CharacterInstances);
        Assert.Same(item, session.Characters.Current);
    }

    [Fact]
    public void A_creature_preset_is_one_undo_step_and_keeps_each_parts_colour_and_what_is_worn()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        editor.SelectedModuleColorEditor!.SetColor.Execute(new ColorSwatchChoice(editor.SelectedModuleColorEditor.Slot, "Blue", ColorValue.FromHex("#3355ff")));
        Assert.True(editor.SelectedModuleHasOwnColor);
        var worn = editor.Working.Stickers.Values.Sum(v => v.Count);
        var before = Json(editor.Working.Modules);

        var square = editor.CreatureChoices.Single(c => c.Name == "Mr. Square");
        Assert.False(square.IsCurrent);
        editor.ApplyCreatureCommand.Execute(square);

        Assert.Equal(ShapeLibrary.SourcePrefix + "head/geometric/rounded-square", editor.Working.Modules!.Head!.Shape.Source);
        Assert.Equal("bodyHead", editor.Working.Modules.Head.Color); // the head's own colour came along
        Assert.Equal(worn, editor.Working.Stickers.Values.Sum(v => v.Count));
        Assert.True(editor.CreatureChoices.Single(c => c.Name == "Mr. Square").IsCurrent);

        session.Workspace.History.Undo();
        Assert.Equal(before, Json(editor.Working.Modules));
    }

    [Fact]
    public void Swapping_a_shape_keeps_its_size_and_colour_and_marks_the_new_one_current()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Torso);
        editor.ModuleWidth = 40;
        var torso = editor.Working.Modules!.Torso!;

        var gallery = editor.ModuleShapeGallery!;
        Assert.Equal(["Organic", "Geometric", "Abstract", "Head shapes"], gallery.Sections.Select(s => s.Label));
        Assert.Single(gallery.Sections.SelectMany(s => s.Choices), c => c.IsCurrent); // the pear it starts with
        var triangle = gallery.Sections.SelectMany(s => s.Choices).Single(c => c.Library?.Key == "torso/geometric/soft-triangle");
        gallery.Pick.Execute(triangle);

        var swapped = editor.Working.Modules!.Torso!;
        Assert.Equal(triangle.Library!.Source, swapped.Shape.Source);
        Assert.Equal(torso.Width, swapped.Width);
        Assert.Equal(torso.Height, swapped.Height);
        Assert.Equal(torso.Color, swapped.Color);
        Assert.True(editor.ModuleShapeGallery!.Sections.SelectMany(s => s.Choices).Single(c => c.Library?.Key == "torso/geometric/soft-triangle").IsCurrent);

        session.Workspace.History.Undo();
        Assert.Equal(torso.Shape.Source, editor.Working.Modules!.Torso!.Shape.Source);
    }

    [Fact]
    public void A_slider_drag_is_one_undo_step()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        var width = editor.Working.Modules!.Head!.Width;

        editor.BeginSliderDrag();
        foreach (var value in new[] { 30.0, 35, 40, 45 })
            editor.ModuleWidth = value;
        editor.EndSliderDrag();
        Assert.Equal(0.45, editor.Working.Modules!.Head!.Width, 6);

        session.Workspace.History.Undo();
        Assert.Equal(width, editor.Working.Modules!.Head!.Width, 6);
    }

    [Fact]
    public void Height_percent_scales_the_whole_body_from_where_the_drag_started()
    {
        var (session, editor) = NewShapeCharacter();
        var start = CharacterRig.Height(editor.Working);
        var head = editor.Working.Modules!.Head!.Width;

        editor.BeginSliderDrag();
        editor.BodyHeightPercent = 120;
        editor.BodyHeightPercent = 150;
        editor.EndSliderDrag();

        Assert.Equal(1.5, CharacterRig.Height(editor.Working), 2);
        Assert.Equal(head * 1.5 / start, editor.Working.Modules!.Head!.Width, 2);
        session.Workspace.History.Undo();
        Assert.Equal(start, CharacterRig.Height(editor.Working), 6);
    }

    [Fact]
    public void A_removed_part_comes_back_through_its_ghost_with_the_library_default()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Arms);
        Assert.True(editor.CanRemoveModule);
        editor.RemoveModuleCommand.Execute(null);
        Assert.Null(editor.Working.Modules!.Arms);
        Assert.False(editor.HasSelectedModule);

        var ghosts = FigureModules.Ghosts(editor.Working.Modules, CharacterRig.Build(editor.Working));
        Assert.Equal(2, ghosts.Count(g => g.Part == ModulePart.Arms));
        Assert.False(editor.ModulePartChoices.Single(c => c.Part == ModulePart.Arms).IsPresent);

        editor.AddModule(ModulePart.Arms);
        Assert.Equal(Json(new ModularBody(null, null, CreaturePresets.DefaultArms, null)), Json(new ModularBody(null, null, editor.Working.Modules!.Arms, null)));
        Assert.Equal(ModulePart.Arms, editor.SelectedModule);
        session.Workspace.History.Undo();
        Assert.Null(editor.Working.Modules!.Arms);
        Assert.False(editor.HasSelectedModule); // undone away
    }

    [Fact]
    public void The_last_of_head_and_torso_cannot_be_removed()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        editor.RemoveModuleCommand.Execute(null);
        editor.SelectModule(ModulePart.Torso);

        Assert.False(editor.CanRemoveModule);
        Assert.False(editor.RemoveModuleCommand.CanExecute(null));
    }

    [Fact]
    public void Picking_a_part_and_a_sticker_are_one_selection()
    {
        var (_, editor) = NewShapeCharacter();
        var eyes = editor.Working.Stickers[StickerSlots.Eyes][0];
        editor.SelectModule(ModulePart.Head);
        Assert.True(editor.HasSelectedModule);

        editor.SelectSticker(eyes);
        Assert.False(editor.HasSelectedModule);
        editor.SelectModule(ModulePart.Legs);
        Assert.False(editor.HasSelectedSticker);
        Assert.True(editor.SelectedModuleIsLimb);
        Assert.Contains("Legs", editor.Hint);
    }

    [Fact]
    public void A_stage_drag_nudges_or_resizes_the_head_as_one_undo_step()
    {
        var (session, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Head);
        var head = editor.Working.Modules!.Head!;

        Assert.True(editor.BeginModuleDrag());
        editor.UpdateModuleNudge(0.01, 0);
        editor.UpdateModuleNudge(0.03, -0.02);
        editor.EndModuleDrag();
        Assert.Equal(new Point2D(0.03, -0.02), editor.Working.Modules!.Head!.Offset);

        Assert.True(editor.BeginModuleDrag());
        editor.UpdateModuleResize(1.5, 2);
        editor.EndModuleDrag();
        Assert.Equal(head.Width * 1.5, editor.Working.Modules!.Head!.Width, 6);
        Assert.Equal(head.Height * 2, editor.Working.Modules!.Head!.Height, 6);

        session.Workspace.History.Undo();
        Assert.Equal(head.Width, editor.Working.Modules!.Head!.Width, 6);
        session.Workspace.History.Undo();
        Assert.Null(editor.Working.Modules!.Head!.Offset);
    }

    [Fact]
    public void A_limb_gets_its_style_hand_pieces_and_one_side_only()
    {
        var (_, editor) = NewShapeCharacter();
        editor.SelectModule(ModulePart.Arms);

        editor.SetLimbStyleCommand.Execute(editor.LimbStyleChoices.Single(c => c.Style == LimbStyle.Pieces));
        Assert.True(editor.HasLimbPieces);
        var upper = editor.LimbUpperGallery!;
        upper.Pick.Execute(upper.Sections.SelectMany(s => s.Choices).First(c => c.Library is not null));
        Assert.NotNull(editor.Working.Modules!.Arms!.Upper);

        var hands = editor.LimbEndGallery!;
        Assert.Equal("Hand", hands.Label);
        hands.Pick.Execute(hands.Sections.SelectMany(s => s.Choices).Single(c => c.Label == "Round"));
        Assert.Null(editor.Working.Modules!.Arms!.End);

        editor.IsLeftSideOnly = true;
        Assert.Equal(LimbSides.Left, editor.Working.Modules!.Arms!.Sides);
        var ghost = Assert.Single(FigureModules.Ghosts(editor.Working.Modules, CharacterRig.Build(editor.Working)));
        Assert.Equal(LimbSide.Right, ghost.Side);
        editor.AddModule(ghost.Part, ghost.Side);
        Assert.Null(editor.Working.Modules!.Arms!.Sides);
    }

    [Fact]
    public void Each_part_is_found_where_it_is_drawn()
    {
        var (_, editor) = NewShapeCharacter();
        var figure = CharacterRig.Build(editor.Working);
        foreach (var part in ModuleParts.All)
        {
            var box = FigureModules.BoundsOf(figure, part);
            Assert.NotNull(box);
            // Somewhere inside its box, it's the topmost thing drawn.
            var hit = Enumerable.Range(1, 19).SelectMany(i => Enumerable.Range(1, 19).Select(j => new Point2D(box!.Value.Left + box.Value.Width * i / 20, box.Value.Top + box.Value.Height * j / 20)))
                .Any(p => FigureModules.At(figure, p) == part);
            Assert.True(hit, $"{part} is never the part under the pointer");
        }
        Assert.Null(FigureModules.At(figure, new Point2D(5, 5)));
    }

    [Fact]
    public void A_generated_body_has_no_shapes_state()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        session.Characters.NewCharacterCommand.Execute(null);
        var editor = session.Characters.Items.Single().Editor;

        Assert.False(editor.IsModular);
        Assert.Empty(editor.CreatureChoices);
        Assert.Empty(editor.ModulePartChoices);
        editor.SelectModule(ModulePart.Head);
        Assert.False(editor.HasSelectedModule);
    }
}
