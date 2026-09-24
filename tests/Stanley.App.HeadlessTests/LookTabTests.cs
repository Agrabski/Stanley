using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
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
        var chest = FindPoint(stage, p => stage.StickerAt(p) is not null);
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

    private static Point FindPoint(Control control, Func<Point, bool> test)
    {
        for (var y = 0.0; y < control.Bounds.Height; y += 4)
            for (var x = 0.0; x < control.Bounds.Width; x += 4)
                if (test(new Point(x, y)))
                    return new Point(x, y);
        throw new InvalidOperationException("No such point on the stage.");
    }
}
