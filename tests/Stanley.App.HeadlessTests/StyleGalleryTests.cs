using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing.Abstractions;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.App.HeadlessTests;

/// <summary>The Sticker tab's "Worn" gallery (docs/sticker-system.md §20): a styled sticker's ways to wear it, previewed and clicked.</summary>
[Collection("Page Editor Tests")]
public class StyleGalleryTests
{
    [Fact]
    public void A_hood_shows_its_styles_on_the_Sticker_tab_and_a_face_does_not()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var characters = window.ViewModel.Characters!;
        characters.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = characters.Items.Single().Editor;
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>().Single();

        var hood = new Sticker(StickerId.New(), "Hood", StickerSlots.Headwear, [new StickerPart("hood", BodyRegion.Head, Cover: new PartCover("hat", 0, 0.4))],
            new SortedDictionary<string, ColorValue> { ["hat"] = ColorValue.FromHex("#556b2f") }, ["down", "up"]);
        var character = editor.Committed;
        editor.Apply(EditResult<CharacterDefinition>.Success(character with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(character.Stickers, StringComparer.Ordinal) { [StickerSlots.Headwear] = [hood.Id] },
            Wardrobe = character.Wardrobe.With(new StickerAsset(hood, new Dictionary<string, ArtFile>())),
        }));
        editor.SelectSticker(hood.Id);
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("StickerTab");
        Dispatcher.UIThread.RunJobs();

        var gallery = ribbon.FindControl<ItemsControl>("StyleGallery")!;
        Assert.True(gallery.IsEffectivelyVisible);
        var buttons = gallery.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is StickerStyleChoice).ToList();
        Assert.Equal(["Down", "Up"], buttons.Select(b => ((StickerStyleChoice)b.DataContext!).Label));
        Assert.Contains("worn", buttons[0].Classes);
        LookTabTests.Snapshot(window, "sticker-styles");

        var up = buttons[1];
        up.Command!.Execute(up.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("up", editor.Working.StickerVariants![hood.Id]);
        Assert.Contains(gallery.GetVisualDescendants().OfType<Button>(), b => b.DataContext is StickerStyleChoice { Variant: "up", IsCurrent: true } && b.Classes.Contains("worn"));

        // A face's variants are its expressions: no "Worn" gallery for them.
        editor.SelectSticker(editor.Working.Stickers[StickerSlots.Eyes].Single());
        Dispatcher.UIThread.RunJobs();
        Assert.False(gallery.IsEffectivelyVisible);
    }
}
