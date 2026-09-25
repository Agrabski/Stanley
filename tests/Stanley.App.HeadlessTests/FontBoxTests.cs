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

    /// <summary>The Font group on <paramref name="tab"/> - the one shared control every tab that letters something shows.</summary>
    private static FontGroup FontGroup(MainWindow window, string tab)
    {
        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == tab);
        Dispatcher.UIThread.RunJobs();
        return ribbon.GetVisualDescendants().OfType<FontGroup>().Single();
    }

    private static ComboBox FontBox(MainWindow window, string tab) => FontGroup(window, tab).Fonts;

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

        var box = FontBox(window, "BubbleTab");
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

        var box = FontBox(window, "TextTab");
        var font = AnotherFont(box);
        box.SelectedItem = font;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(font.Name, ((TextElement)editor.Working.Panels[panelId].Elements[index]).Style.FontFamily);
        LookTabTests.Snapshot(window, "font-text-tab");

        var home = FontBox(window, "HomeTab");
        Assert.Same(font, home.SelectedItem);
    }

    /// <summary>Word's font size box: type any size and press Enter, or pick one from the arrow's list; a bad entry puts the size back.</summary>
    [Fact]
    public void The_size_box_takes_a_typed_size_or_a_picked_one_and_gives_the_keyboard_back_to_the_page()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateText(panelId, new Point2D(bounds.MidX, bounds.MidY));
        editor.SetElementText(panelId, index, "BOOM");
        double Size() => ((TextElement)editor.Working.Panels[panelId].Elements[index]).Style.FontSizeMm;

        var box = FontGroup(window, "TextTab").Sizes;
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        Assert.Equal(FontSizeBox.Format(Size()), box.Entry.Text);

        // Click in, type, Enter: the whole entry is replaced, the size applies, the page has the keyboard again.
        box.Entry.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("7.5");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7.5, Size());
        Assert.Equal(FontSizeBox.Format(7.5), box.Entry.Text);
        Assert.True(view.Canvas.IsFocused);

        // Nonsense is refused: the box shows the real size again and the status bar says why.
        box.Entry.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("huge");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7.5, Size());
        Assert.Equal(FontSizeBox.Format(7.5), box.Entry.Text);
        Assert.NotNull(editor.LastError);

        // Esc cancels what's typed.
        box.Entry.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("40");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7.5, Size());
        Assert.Equal(FontSizeBox.Format(7.5), box.Entry.Text);

        // The arrow lists the usual sizes with the current one ticked once it's one of them; a click applies.
        editor.SetTextSizeCommand.Execute("12");
        box.Menu.ShowAt(box);
        Dispatcher.UIThread.RunJobs();
        var items = box.Menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(FontSizeBox.Format(12), Assert.Single(items, i => i.IsChecked).Header);
        LookTabTests.Snapshot(window, "font-size-menu");
        items.Single(i => (string?)i.Header == FontSizeBox.Format(20)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        box.Menu.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(20, Size());
        Assert.Equal(FontSizeBox.Format(20), box.Entry.Text);
    }

    /// <summary>Home, Bubble and Text all show the same Font group, and on a bubble every part of it works: size, bold, italic, alignment.</summary>
    [Fact]
    public void Every_tab_shows_the_same_Font_group_and_it_letters_a_bubble_too()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panelId);
        var index = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        editor.SetBubbleText(panelId, index, "Look out!");
        Stanley.ProjectModel.Bubbles.Bubble Bubble() => editor.Working.Panels[panelId].Bubbles[index];

        var home = FontGroup(window, "HomeTab");
        var group = FontGroup(window, "BubbleTab");
        Assert.NotSame(home, group); // one control type, placed on each tab

        Assert.Equal(FontSizeBox.Format(Stanley.ProjectModel.Bubbles.Bubble.DefaultFontSizeMm), group.Sizes.Entry.Text);
        group.Sizes.Entry.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("5");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        FindButton(group, "BiggerButton").Command!.Execute(null);
        Toggle(group, "BoldButton");
        Toggle(group, "ItalicButton");
        Toggle(group, "AlignLeftButton");
        Dispatcher.UIThread.RunJobs();

        var bubble = Bubble();
        Assert.Equal(6, bubble.FontSizeMm); // 5, then one step bigger
        Assert.True(bubble.Bold);
        Assert.True(bubble.Italic);
        Assert.Equal(TextAlign.Left, bubble.Align);
        LookTabTests.Snapshot(window, "font-group-bubble");

        // Home's copy shows the same bubble's lettering.
        home = FontGroup(window, "HomeTab");
        Assert.Equal(FontSizeBox.Format(6), home.Sizes.Entry.Text);
        Assert.True(home.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Name == "BoldButton").IsChecked);

        // The inline editor types in it.
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        editor.EditTextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Avalonia.Media.FontWeight.Bold, view.TextEditor.FontWeight);
        Assert.Equal(Avalonia.Media.FontStyle.Italic, view.TextEditor.FontStyle);
        Assert.Equal(Avalonia.Media.TextAlignment.Left, view.TextEditor.TextAlignment);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button FindButton(Control group, string name) => group.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Toggle(Control group, string name)
    {
        var toggle = group.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Name == name);
        toggle.IsChecked = true;
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

        var box = FontBox(window, "BubbleTab");

        Assert.Null(box.SelectedItem);
        Assert.Equal("Wild Words Pro (missing)", box.PlaceholderText);
        Assert.Equal(LetteringFonts.Inter, Lettering.Resolve("Wild Words Pro"));
        Assert.Equal("Wild Words Pro", editor.Working.Panels[panelId].Bubbles[index].FontFamily); // the name is kept
    }
}
