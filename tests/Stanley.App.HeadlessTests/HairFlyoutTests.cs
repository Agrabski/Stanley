using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.StickerLibrary;

namespace Stanley.App.HeadlessTests;

/// <summary>The Look tab's one Hair button: its flyout with tabs, and the two small bars in the character editor.</summary>
[Collection("Page Editor Tests")]
public class HairFlyoutTests
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
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();
        return (window, editor, ribbon);
    }

    private static StickerAsset Piece(string slot, string name) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue>(), ["default"]), new Dictionary<string, ArtFile>());

    private static Control FlyoutContent(DropDownButton button) => (Control)((Flyout)button.Flyout!).Content!;

    private static List<Button> Buttons(Control content) => content.GetLogicalDescendants().OfType<Button>().ToList();

    [Fact]
    public void The_Hair_button_opens_a_flyout_with_its_tabs_and_only_the_open_tab_is_built()
    {
        var (window, editor, ribbon) = OpenCharacter();

        // One Hair button in the Hair & face group, saying what is worn on its second line.
        var hair = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "HairButton");
        Assert.Equal(["Hair", "None"], hair.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        hair.Flyout!.ShowAt(hair);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "hair-flyout-hairstyles");

        var content = FlyoutContent(hair);
        var tabs = Buttons(content).Where(b => b.DataContext is HairTab).ToList();
        Assert.Equal(["Hairstyles", "Top", "Fringe", "Sides", "Back", "Extras", "Streaks"], tabs.Select(t => ((HairTab)t.DataContext!).Label));
        Assert.Contains("worn", tabs[0].Classes);

        // Hairstyles is open first: Bald, the presets, and Draw your own / Import for a whole hairstyle in one file.
        var choices = Buttons(content).Where(b => b.DataContext is HairstyleChoice).ToList();
        Assert.Equal("Bald", ((HairstyleChoice)choices[0].DataContext!).Label);
        Assert.Equal(1 + Hairstyles.Available.Count(), choices.Count);
        Assert.Contains(Buttons(content), b => b.Name == "DrawYourOwnButton" && b.Command is not null);
        Assert.Contains(Buttons(content), b => b.Name == "ImportArtButton" && b.Command is not null);
        Assert.All(content.GetVisualDescendants().OfType<CharacterFigure>(), f => Assert.True(f.Closeup));
        Assert.Empty(Buttons(content).Where(b => b.DataContext is StickerChoice)); // the piece tabs aren't built

        // A piece tab is an ordinary slot gallery: None first, Draw your own and Import under it - and the hairstyles are gone from the tree.
        var fringe = tabs.Single(t => ((HairTab)t.DataContext!).Label == "Fringe");
        fringe.Command!.Execute(fringe.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "hair-flyout-fringe");
        Assert.Equal(StickerSlots.HairFringe, editor.HairTabKey);
        Assert.Empty(Buttons(content).Where(b => b.DataContext is HairstyleChoice));
        Assert.True(Buttons(content).First(b => b.DataContext is StickerChoice).DataContext is StickerChoice { IsNone: true });
        Assert.Contains(Buttons(content), b => b.Name == "DrawYourOwnButton" && b.Command is not null);
        Assert.Contains(Buttons(content), b => b.Name == "ImportArtButton" && b.Command is not null);

        // The flyout remembers the tab it was left on.
        hair.Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();
        hair.Flyout!.ShowAt(hair);
        Dispatcher.UIThread.RunJobs();
        var again = Buttons(FlyoutContent(hair)).Where(b => b.DataContext is HairTab).ToList();
        Assert.Contains("worn", again.Single(t => ((HairTab)t.DataContext!).Label == "Fringe").Classes);
        Assert.DoesNotContain("worn", again[0].Classes);
        hair.Flyout!.Hide();
    }

    [Fact]
    public void A_hairstyle_click_dresses_the_head_and_the_button_names_it()
    {
        var (_, editor, ribbon) = OpenCharacter();
        var hair = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "HairButton");
        hair.Flyout!.ShowAt(hair);
        Dispatcher.UIThread.RunJobs();
        var preset = Hairstyles.Available.FirstOrDefault();
        Assert.SkipUnless(preset is not null, "the hair pieces aren't in the library yet");

        var choice = Buttons(FlyoutContent(hair)).Single(b => b.DataContext is HairstyleChoice c && c.Label == preset!.Name);
        choice.Command!.Execute(choice.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(preset!.Name, editor.HairCurrent);
        Assert.Contains(hair.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == preset.Name);
        Assert.Contains("worn", Buttons(FlyoutContent(hair)).Single(b => b.DataContext is HairstyleChoice c && c.Label == preset.Name).Classes);
        hair.Flyout!.Hide();
    }

    [Fact]
    public void The_replaced_bar_shows_over_the_stage_and_Add_to_my_mix_takes_it_down()
    {
        var (window, editor, _) = OpenCharacter();
        var view = window.GetVisualDescendants().OfType<CharacterEditorView>().Single();
        var bar = view.FindControl<Border>("HairReplacedBar")!;
        Assert.False(bar.IsEffectivelyVisible);
        var ribbon = Piece(StickerSlots.HairExtras, "Ribbon");
        var plain = Piece(StickerSlots.HairTop, "Plain");
        editor.Apply(EditResult<CharacterDefinition>.Success(LookEditing.Wear(editor.Committed, ribbon)));

        editor.WearHairstyle("Plain", [(plain, null)]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsEffectivelyVisible);
        Assert.Equal("Replaced your hair with Plain.", view.FindControl<TextBlock>("HairReplacedText")!.Text);
        LookTabTests.Snapshot(window, "hair-replaced-bar");
        var add = view.FindControl<Button>("AddToMixButton")!;
        add.Command!.Execute(add.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(bar.IsEffectivelyVisible);
        Assert.Equal([plain.Id, ribbon.Id], HairEditing.WornHairdo(editor.Working));
    }

    [Fact]
    public void The_upgrade_bar_offers_Switch_and_Keep_for_an_old_library_hairstyle()
    {
        Assert.SkipUnless(Hairstyles.Get("Bob") is { } bob && Hairstyles.IsAvailable(bob), "the hair pieces aren't in the library yet");
        var (window, editor, _) = OpenCharacter();
        var view = window.GetVisualDescendants().OfType<CharacterEditorView>().Single();
        var bar = view.FindControl<Border>("HairUpgradeBar")!;
        Assert.False(bar.IsEffectivelyVisible);
        var old = Piece(StickerSlots.Hair, "Bob");
        old = old with { Sticker = old.Sticker with { Source = Stanley.StickerLibrary.StickerLibrary.SourcePrefix + "hair/bob" } };

        editor.Apply(EditResult<CharacterDefinition>.Success(LookEditing.Wear(editor.Committed, old)));
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsEffectivelyVisible);
        Assert.Equal("This is the old Bob - switch to the new Bob made of pieces?", view.FindControl<TextBlock>("HairUpgradeText")!.Text);
        LookTabTests.Snapshot(window, "hair-upgrade-bar");
        var keep = view.FindControl<Button>("KeepHairButton")!;
        keep.Command!.Execute(keep.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(bar.IsEffectivelyVisible);
        Assert.Contains(old.Id, editor.Working.Stickers[StickerSlots.Hair]); // Keep changes nothing about the hair
    }
}
