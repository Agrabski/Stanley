using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.App.HeadlessTests;

/// <summary>Word's font box on the ribbon: every font by name in its own face, lettering the selected bubble or text, and the inline editor typing in it.</summary>
[Collection("Page Editor Tests")]
public class FontBoxTests
{
    private static PageEditorRibbon Ribbon(MainWindow window) => window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();

    private static ComboBox FontBox(MainWindow window, string tab, string name)
    {
        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == tab);
        Dispatcher.UIThread.RunJobs();
        return ribbon.GetVisualDescendants().OfType<ComboBox>().Single(b => b.Name == name);
    }

    private static FontChoice AnotherFont(ComboBox box) =>
        box.Items.OfType<FontChoice>().FirstOrDefault(c => c.Name.Contains("Serif", StringComparison.Ordinal)) ?? box.Items.OfType<FontChoice>().Skip(1).First();

    [Fact]
    public void The_bubble_font_box_letters_the_selected_bubble_and_the_inline_editor_types_in_that_font()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        editor.SetBubbleText(panelId, index, "Who's there?");

        var box = FontBox(window, "BubbleTab", "BubbleFontBox");
        var fonts = box.Items.OfType<FontChoice>().ToList();
        Assert.Equal(LetteringFonts.Inter, fonts[0].Name); // Stanley's own font first
        Assert.True(fonts.Count > 1, "the computer's own fonts are listed too");
        Assert.Same(fonts[0], box.SelectedItem);

        var font = AnotherFont(box);
        box.SelectedItem = font;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(font.Name, editor.Working.Panels[panelId].Bubbles[index].FontFamily);

        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "font-box-open");
        box.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();

        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        editor.EditTextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.TextEditor.IsVisible);
        Assert.Equal(font.Name, view.TextEditor.FontFamily.Name);
        LookTabTests.Snapshot(window, "font-bubble-editing");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        window.History.Undo();
        Assert.Null(editor.Working.Panels[panelId].Bubbles[index].FontFamily);
        Assert.Same(fonts[0], box.SelectedItem);
    }

    [Fact]
    public void The_text_tab_font_box_letters_the_selected_text_and_home_shows_the_same_font()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateText(panelId, new Point2D(bounds.MidX, bounds.MidY));
        editor.SetElementText(panelId, index, "Meanwhile...");

        var box = FontBox(window, "TextTab", "TextFontBox");
        var font = AnotherFont(box);
        box.SelectedItem = font;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(font.Name, ((TextElement)editor.Working.Panels[panelId].Elements[index]).Style.FontFamily);
        LookTabTests.Snapshot(window, "font-text-tab");

        var home = FontBox(window, "HomeTab", "HomeFontBox");
        Assert.Same(font, home.SelectedItem);
    }

    [Fact]
    public void A_font_missing_from_this_computer_is_named_in_the_box_and_drawn_in_the_default()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        editor.SetBubbleFont(panelId, index, "Wild Words Pro");

        var box = FontBox(window, "BubbleTab", "BubbleFontBox");

        Assert.Null(box.SelectedItem);
        Assert.Equal("Wild Words Pro (missing)", box.PlaceholderText);
        Assert.Equal(LetteringFonts.Inter, Lettering.Resolve("Wild Words Pro"));
        Assert.Equal("Wild Words Pro", editor.Working.Panels[panelId].Bubbles[index].FontFamily); // the name is kept
    }
}
