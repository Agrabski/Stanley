using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// The choice that's on is unmistakable in every gallery (issues #62, #126): framed in the selection
/// colour on a tint, with a check in the corner, and still so under the pointer.
/// </summary>
[Collection("Page Editor Tests")]
public class ChoiceHighlightTests
{
    private const string Tint = "StanleyChoiceTintBrush";
    private const string HoverTint = "StanleyChoiceHoverTintBrush";

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

    /// <summary>Runs <paramref name="check"/> in the light theme, then the dark one, putting the app's theme back afterwards.</summary>
    private static void InBothThemes(Action check)
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                app.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                check();
            }
        }
        finally
        {
            app.RequestedThemeVariant = before;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ContentPresenter Presenter(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    private static bool BadgeShown(Button button) =>
        button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_WornBadge").IsVisible;

    private static Color Colour(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

    /// <summary>A brush of the current theme, by key, as the button itself resolves it.</summary>
    private static Color Theme(Control from, string key)
    {
        Assert.True(from.TryFindResource(key, from.ActualThemeVariant, out var brush), key);
        return Colour((IBrush)brush!);
    }

    private static void Hover(Button button, bool on)
    {
        ((IPseudoClasses)button.Classes).Set(":pointerover", on);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What a chosen button looks like, measured on what is drawn: framed in the theme's selection colour, tinted (<paramref name="tint"/>), and checked unless it's words.</summary>
    private static void AssertLooksChosen(Button button, string tint = Tint, bool check = true)
    {
        var presenter = Presenter(button);
        Assert.Equal(Theme(button, "StanleySelectionBrush"), Colour(presenter.BorderBrush));
        Assert.Equal(Theme(button, tint), Colour(presenter.Background));
        Assert.True(presenter.BorderThickness.Left >= 2);
        Assert.Equal(check, BadgeShown(button));
    }

    private static void AssertLooksPlain(Button button)
    {
        var presenter = Presenter(button);
        Assert.NotEqual(Theme(button, "StanleySelectionBrush"), Colour(presenter.BorderBrush));
        Assert.NotEqual(Theme(button, Tint), Colour(presenter.Background));
        Assert.NotEqual(Theme(button, HoverTint), Colour(presenter.Background));
        Assert.False(BadgeShown(button));
    }

    [Fact]
    public void A_worn_item_in_a_slot_gallery_is_framed_tinted_and_checked_in_both_themes_and_stays_so_under_the_pointer()
    {
        var (_, editor, ribbon) = OpenCharacter();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        Dispatcher.UIThread.RunJobs();
        var top = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Top" });
        top.Flyout!.ShowAt(top);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)top.Flyout!).Content!;
        var buttons = content.GetLogicalDescendants().OfType<Button>().Where(b => b.DataContext is StickerChoice).ToList();
        var tee = buttons.Single(b => ((StickerChoice)b.DataContext!).Label == "T-shirt");
        var shirt = buttons.Single(b => ((StickerChoice)b.DataContext!).Label == "Shirt");

        InBothThemes(() =>
        {
            AssertLooksChosen(tee);
            AssertLooksPlain(shirt);

            // Under the pointer the worn one deepens but is still the framed, checked one; the other only lights up.
            Hover(tee, true);
            AssertLooksChosen(tee, HoverTint);
            Hover(tee, false);
            AssertLooksChosen(tee);

            var before = Colour(Presenter(shirt).Background);
            Hover(shirt, true);
            AssertLooksPlain(shirt);
            Assert.NotEqual(before, Colour(Presenter(shirt).Background));
            Hover(shirt, false);
        });
        top.Flyout.Hide();
    }

    [Fact]
    public void A_click_that_layers_a_second_item_marks_both_and_one_that_takes_it_off_unmarks_it()
    {
        var (_, editor, ribbon) = OpenCharacter();

        // A click rebuilds the gallery buttons, so each look at the gallery opens the current one.
        Button Choice(string label)
        {
            var top = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is SlotGallery { Label: "Top" });
            top.Flyout!.ShowAt(top);
            Dispatcher.UIThread.RunJobs();
            var content = (Control)((Flyout)top.Flyout!).Content!;
            return content.GetLogicalDescendants().OfType<Button>().Single(b => b.DataContext is StickerChoice c && c.Label == label);
        }
        void Wear(string label) => editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == label));

        // With nothing on, None is the chosen one.
        AssertLooksChosen(Choice("None"));

        Wear("T-shirt");
        Wear("Hoodie");
        Dispatcher.UIThread.RunJobs();
        AssertLooksChosen(Choice("T-shirt"));
        AssertLooksChosen(Choice("Hoodie"));
        AssertLooksPlain(Choice("None"));
        AssertLooksPlain(Choice("Shirt"));

        Wear("T-shirt"); // off again
        Dispatcher.UIThread.RunJobs();
        AssertLooksPlain(Choice("T-shirt"));
        AssertLooksChosen(Choice("Hoodie"));
    }

    [Fact]
    public void The_chosen_texture_and_pattern_in_a_colour_dropdown_are_marked()
    {
        var (_, editor, ribbon) = OpenCharacter();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        Dispatcher.UIThread.RunJobs();
        var colour = ribbon.GetVisualDescendants().OfType<DropDownButton>().First(b => b.DataContext is ColorSlotEditor { Slot: "top" });
        colour.Flyout!.ShowAt(colour);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)colour.Flyout!).Content!;

        foreach (var label in new[] { "Stripes", "Knit" })
        {
            var choice = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is FabricChoice c && c.Label == label);
            choice.Command!.Execute(choice.CommandParameter);
            Dispatcher.UIThread.RunJobs();
        }

        InBothThemes(() =>
        {
            var chosen = content.GetLogicalDescendants().OfType<Button>().Where(b => b.DataContext is FabricChoice && b.Classes.Contains("worn")).ToList();
            Assert.Contains(chosen, b => ((FabricChoice)b.DataContext!).Label == "Stripes");
            Assert.Contains(chosen, b => ((FabricChoice)b.DataContext!).Label == "Knit");
            Assert.All(chosen, b => AssertLooksChosen(b));

            // Everything else in those galleries is plain: Denim, Checks...
            var others = content.GetLogicalDescendants().OfType<Button>().Where(b => b.DataContext is FabricChoice && !b.Classes.Contains("worn")).ToList();
            Assert.NotEmpty(others);
            Assert.All(others, AssertLooksPlain);
        });
        colour.Flyout.Hide();
    }

    [Fact]
    public void The_Hair_flyouts_current_tab_is_framed_and_tinted_but_has_no_check_over_its_words()
    {
        var (_, _, ribbon) = OpenCharacter();
        var hair = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "HairButton");
        hair.Flyout!.ShowAt(hair);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)hair.Flyout!).Content!;
        var tabs = content.GetLogicalDescendants().OfType<Button>().Where(b => b.DataContext is HairTab).ToList();

        InBothThemes(() =>
        {
            AssertLooksChosen(tabs[0], check: false);
            Assert.All(tabs.Skip(1), AssertLooksPlain);
        });
        hair.Flyout.Hide();
    }
}
