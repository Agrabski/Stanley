using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.HeadlessTests;

/// <summary>The character editor's Look tab: dressing a character from the slot galleries, and the Sticker tab.</summary>
[Collection("Page Editor Tests")]
public class LookTabTests
{
    private static (MainWindow Window, CharacterEditorViewModel Editor, CharacterEditorRibbon Ribbon) OpenCharacter()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var characters = window.ViewModel.Characters!;
        characters.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = characters.Items.Single().Editor;
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().Single();
        return (window, editor, ribbon);
    }

    internal static void Snapshot(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("STANLEY_UI_SNAPSHOTS") is not { Length: > 0 } dir)
            return;
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
#pragma warning disable CS0618 // the non-obsolete overload needs an encoder options type we have no concrete one of
        frame?.Save(Path.Combine(dir, name + ".png"));
#pragma warning restore CS0618
    }

    [Fact]
    public void Picking_a_T_shirt_from_the_Top_gallery_dresses_the_character_and_clicking_it_opens_the_Sticker_tab()
    {
        var (window, editor, ribbon) = OpenCharacter();
        var look = ribbon.FindControl<TabItem>("LookTab")!;
        ribbon.TabControl.SelectedItem = look;
        Dispatcher.UIThread.RunJobs();

        var top = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Top" });
        top.Flyout!.ShowAt(top);
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "look-top-gallery");

        // The choices live in the flyout's popup - their buttons must reach the gallery's command from there.
        var popup = window.GetVisualDescendants().OfType<Popup>().Concat(TopLevel.GetTopLevel(top)!.GetVisualDescendants().OfType<Popup>())
            .Select(p => p.Child).OfType<Control>().FirstOrDefault()
            ?? ((Control)((Flyout)top.Flyout!).Content!);
        var choice = popup.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is StickerChoice { Label: "T-shirt" });
        Assert.True(choice.Command?.CanExecute(choice.CommandParameter));
        choice.Command!.Execute(choice.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("T-shirt", editor.Gallery(StickerSlots.Top).Current);
        Assert.Contains(ribbon.GetVisualDescendants().OfType<DropDownButton>(), b => b.DataContext is ColorSlotEditor { Slot: "top" });

        // Clicking the T-shirt on the stage selects it: the Sticker tab appears with its sliders.
        var view = window.GetVisualDescendants().OfType<CharacterEditorView>().Single();
        var stage = view.Figure;
        var tee = editor.Working.Stickers[StickerSlots.Top].Single();
        var chest = FindPoint(stage, p => stage.StickerAt(p) == tee);
        window.MouseDown(stage.TranslatePoint(chest, window)!.Value, MouseButton.Left);
        window.MouseUp(stage.TranslatePoint(chest, window)!.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.HasSelectedSticker);
        var stickerTab = ribbon.FindControl<TabItem>("StickerTab")!;
        Assert.True(stickerTab.IsVisible);
        ribbon.TabControl.SelectedItem = stickerTab;
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "look-sticker-tab");

        ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TakeOffButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("None", editor.Gallery(StickerSlots.Top).Current);
    }

    [Fact]
    public void A_colour_dropdown_offers_patterns_and_textures_and_stays_open_while_you_pick()
    {
        var (window, editor, ribbon) = OpenCharacter();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Bottom).Choices.Single(c => c.Label == "Jeans"));
        Dispatcher.UIThread.RunJobs();

        var top = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is ColorSlotEditor { Slot: "top" });
        top.Flyout!.ShowAt(top);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)top.Flyout!).Content!;
        var stripes = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is FabricChoice { Label: "Stripes" });
        stripes.Command!.Execute(stripes.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(top.Flyout!.IsOpen);
        Assert.Equal(PatternKind.Stripes, ((ColorSlotEditor)top.DataContext!).Fabric?.Pattern?.Kind);
        Snapshot(window, "look-fabric-dropdown");
    }

    [Fact]
    public void The_Text_button_opens_the_text_box_and_typing_over_it_keeps_the_print_selected()
    {
        var (_, editor, ribbon) = OpenCharacter();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();

        editor.Gallery(StickerSlots.Print).WearText!.Execute("HELLO");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(ribbon.FindControl<TabItem>("StickerTab"), ribbon.TabControl.SelectedItem);
        var box = ribbon.FindControl<TextBox>("PrintTextBox")!;
        Assert.Equal("HELLO", box.Text);

        // Typing over it renames the print; the Sticker tab's picker refreshing must not let go of it.
        editor.SelectedText = "SKATE";
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(editor.SelectedSticker);
        Assert.Equal("SKATE", editor.SelectedStickerName);
        Assert.Equal("SKATE", box.Text);
    }

    [Fact]
    public void The_hair_gallery_shows_close_ups_and_dresses_the_head()
    {
        var (window, editor, ribbon) = OpenCharacter();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();

        var hair = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Hair" });
        hair.Flyout!.ShowAt(hair);
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "look-hair-gallery");
        var content = (Control)((Flyout)hair.Flyout!).Content!;
        var figures = content.GetVisualDescendants().OfType<CharacterFigure>().ToList();
        Assert.NotEmpty(figures);
        Assert.All(figures, f => Assert.True(f.Closeup));
        var bob = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is StickerChoice { Label: "Bob" });
        bob.Command!.Execute(bob.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bob", editor.Gallery(StickerSlots.Hair).Current);
        Assert.Contains(ribbon.GetVisualDescendants().OfType<DropDownButton>(), b => b.DataContext is ColorSlotEditor { Slot: "hair" });

        // The face galleries sit beside it, half height.
        var eyes = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Eyes" });
        Assert.Contains("compact", eyes.Classes);
        // A click on what's worn takes it off, and a click on another puts that on.
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Eyes).Choices.First(c => c.Label == "Dots"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Eyes).Choices.First(c => c.Label == "Round"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Mouth).Choices.First(c => c.Label == "Simple"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Mouth).Choices.First(c => c.Label == "Lips"));
        Assert.Equal("Round", editor.Gallery(StickerSlots.Eyes).Current);
        Assert.Equal("Lips", editor.Gallery(StickerSlots.Mouth).Current);
        Dispatcher.UIThread.RunJobs();
        hair.Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "look-hair-and-face");
    }

    [Fact]
    public void Galleries_offer_draw_your_own_and_import_and_imported_art_drags_on_the_stage()
    {
        var (window, editor, ribbon) = OpenCharacter();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();
        var other = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Other" });
        other.Flyout!.ShowAt(other);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)other.Flyout!).Content!;
        Assert.Contains(content.GetLogicalDescendants().OfType<Button>(), b => b.Name == "DrawYourOwnButton" && b.Command is not null);
        var import = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ImportArtButton");
        ArtImportRequest? asked = null;
        editor.ArtImportRequested += (_, r) => asked = r;
        import.Command!.Execute(import.CommandParameter);
        Assert.Equal(new ArtImportRequest(StickerSlots.Accessory), asked);
        other.Flyout!.Hide();

        editor.ImportArt(StickerSlots.Accessory, "badge.svg", ArtFile.Svg("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="45" fill="#ff9900" stroke="#000" stroke-width="3"/></svg>"""));
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("StickerTab");
        Dispatcher.UIThread.RunJobs();
        Assert.True(ribbon.FindControl<Slider>("ArtScaleSlider")!.IsEffectivelyVisible);
        Snapshot(window, "look-imported-art");

        // Drag the badge across the chest: one undo step.
        var id = editor.SelectedStickerId!.Value;
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        var on = FindPoint(stage, p => stage.StickerAt(p) == id);
        var offset = editor.SelectedSticker!.Sticker.Parts[0].Art!.Offset;
        window.MouseDown(stage.TranslatePoint(on, window)!.Value, MouseButton.Left);
        window.MouseMove(stage.TranslatePoint(on + new Point(10, 0), window)!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseMove(stage.TranslatePoint(on + new Point(30, 0), window)!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(stage.TranslatePoint(on + new Point(30, 0), window)!.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var moved = editor.SelectedSticker!.Sticker.Parts[0].Art!.Offset!.Value;
        Assert.True(moved.X > (offset?.X ?? 0) + 5, $"{offset} -> {moved}");
        window.Workspace.History.Undo();
        Assert.Equal(offset, editor.Working.Wardrobe.Find(id)!.Sticker.Parts[0].Art!.Offset);
    }

    private static Point FindPoint(Control control, Func<Point, bool> test)
    {
        for (var y = 0.0; y < control.Bounds.Height; y += 4)
            for (var x = 0.0; x < control.Bounds.Width; x += 4)
                if (test(new Point(x, y)))
                    return new Point(x, y);
        throw new InvalidOperationException("No such point on the stage.");
    }
}
