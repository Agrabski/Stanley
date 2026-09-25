using System.Runtime.CompilerServices;
using Stanley.EditorFramework;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors.Tests;

internal static class BundledFonts
{
    /// <summary>The app installs Stanley's own fonts before anything draws; so do these tests, once, so the default font never changes under a running test.</summary>
    [ModuleInitializer]
    internal static void Install() => LetteringFonts.Install();
}

/// <summary>The font box: bundled and installed fonts, for the selected bubble or text and for what's added next.</summary>
public class FontsTests
{
    private static readonly Rect2D PanelBounds = new(10, 10, 120, 100);

    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) NewEditor()
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var panel = new Panel(panelId, PanelShapes.Rectangle(PanelBounds), null, [], []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), history, panelId);
    }

    /// <summary>A font installed on this computer (not the default), or null on a computer with only one.</summary>
    private static FontChoice? AnotherFont(PageEditorViewModel editor) => editor.FontChoices.FirstOrDefault(c => !c.IsDefault && !c.IsBundled);

    [Fact]
    public void Inter_ships_with_Stanley_as_the_default_and_heads_the_list()
    {
        Assert.True(Lettering.IsBundled(LetteringFonts.Inter));
        Assert.Equal(LetteringFonts.Inter, Lettering.DefaultFamily);
        var inter = LetteringFonts.Choices[0];
        Assert.Equal(LetteringFonts.Inter, inter.Name);
        Assert.True(inter.IsBundled);
        Assert.True(inter.IsDefault);
        Assert.Equal("default", inter.Note);
        Assert.Equal(LetteringFonts.Inter, Lettering.Typeface(null).FamilyName);
        Assert.True(Lettering.Typeface(null, bold: true).FontStyle.Weight >= 600); // its real bold face
    }

    [Fact]
    public void With_nothing_selected_the_font_box_sets_the_font_of_the_next_bubble_and_text()
    {
        var (editor, _, panel) = NewEditor();
        var font = AnotherFont(editor);
        Assert.SkipWhen(font is null, "this computer has no font besides the default");

        Assert.Equal(LetteringFonts.Inter, editor.SelectedFont?.Name); // no font chosen = the default
        editor.SelectedFont = font;

        var bubbleIndex = editor.CreateBubble(panel, new Point2D(50, 50));
        Assert.True(bubbleIndex >= 0, editor.LastError);
        Assert.Equal(font!.Name, editor.Working.Panels[panel].Bubbles[bubbleIndex].FontFamily);
        editor.Select(null);
        var textIndex = editor.CreateText(panel, new Point2D(40, 90));
        Assert.True(textIndex >= 0, editor.LastError);
        var text = (TextElement)editor.Working.Panels[panel].Elements[textIndex];
        Assert.Equal(font.Name, text.Style.FontFamily);
    }

    [Fact]
    public void Picking_a_font_changes_the_selected_bubble_in_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        var font = AnotherFont(editor);
        Assert.SkipWhen(font is null, "this computer has no font besides the default");
        var index = editor.CreateBubble(panel, new Point2D(50, 50));
        var changes = new List<string?>();
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        editor.SelectedFont = font;

        Assert.Equal(font!.Name, editor.Working.Panels[panel].Bubbles[index].FontFamily);
        Assert.Same(font, editor.SelectedFont);
        Assert.Contains(nameof(PageEditorViewModel.SelectedFont), changes);

        history.Undo();
        Assert.Null(editor.Working.Panels[panel].Bubbles[index].FontFamily);
        Assert.Equal(LetteringFonts.Inter, editor.SelectedFont?.Name);
    }

    [Fact]
    public void A_texts_font_survives_changing_its_kind_and_picking_the_default_stores_none()
    {
        var (editor, _, panel) = NewEditor();
        var font = AnotherFont(editor);
        Assert.SkipWhen(font is null, "this computer has no font besides the default");
        var index = editor.CreateText(panel, new Point2D(40, 40));
        editor.SelectElement(panel, index);

        editor.SelectedFont = font;
        editor.ApplyTextPresetCommand.Execute(TextStylePreset.SoundEffect);

        var style = ((TextElement)editor.Working.Panels[panel].Elements[index]).Style;
        Assert.Equal(font!.Name, style.FontFamily);
        Assert.True(style.Bold);
        Assert.Equal("Sound effect", editor.TextPresetName);

        editor.SelectedFont = LetteringFonts.Choices[0];
        Assert.Null(((TextElement)editor.Working.Panels[panel].Elements[index]).Style.FontFamily);
    }

    [Fact]
    public void A_size_picked_or_typed_resizes_the_selected_text_in_one_undo_step_and_a_bad_one_says_why()
    {
        var (editor, history, panel) = NewEditor();
        var index = editor.CreateText(panel, new Point2D(40, 40));
        editor.SelectElement(panel, index);
        var before = editor.TextSizePt;
        var changes = new List<string?>();
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        editor.SetTextSizeCommand.Execute("10,5 pt");

        Assert.Equal(10.5, ((TextElement)editor.Working.Panels[panel].Elements[index]).Style.FontSizePt);
        Assert.Equal(10.5, editor.TextSizePt);
        Assert.Contains(nameof(PageEditorViewModel.TextSizePt), changes);
        Assert.Contains(12, editor.TextSizeChoices);
        Assert.Contains(72, editor.TextSizeChoices);

        editor.SetTextSizeCommand.Execute("5 mm"); // millimetres still work, turned into points
        Assert.Equal(14, editor.TextSizePt);

        editor.SetTextSizeCommand.Execute("huge");
        Assert.Equal(14, editor.TextSizePt);
        Assert.NotNull(editor.LastError);

        history.Undo();
        history.Undo();
        Assert.Equal(before, editor.TextSizePt);
    }

    [Fact]
    public void With_nothing_selected_a_typed_size_is_the_size_of_the_next_text()
    {
        var (editor, _, panel) = NewEditor();

        editor.SetTextSizeCommand.Execute("9");
        var index = editor.CreateText(panel, new Point2D(40, 40));

        Assert.Equal(9, ((TextElement)editor.Working.Panels[panel].Elements[index]).Style.FontSizePt);
    }

    [Fact]
    public void The_font_group_letters_the_selected_bubble_one_undo_step_per_change_and_new_bubbles_follow()
    {
        var (editor, history, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(50, 50));
        Stanley.ProjectModel.Bubbles.Bubble Bubble(int i) => editor.Working.Panels[panel].Bubbles[i];
        Assert.Equal(Stanley.ProjectModel.Bubbles.Bubble.DefaultFontSizePt, editor.TextSizePt);
        Assert.True(editor.IsTextAlignCenter);

        editor.IsTextBold = true;
        editor.SetTextSizeCommand.Execute("6");
        editor.IsTextAlignRight = true;

        Assert.True(Bubble(index).Bold);
        Assert.Equal(6, Bubble(index).FontSizePt);
        Assert.Equal(TextAlign.Right, Bubble(index).Align);
        Assert.Equal(6, editor.TextSizePt);
        Assert.True(editor.IsTextAlignRight);

        history.Undo();
        Assert.Null(Bubble(index).Align);
        history.Undo();
        Assert.Null(Bubble(index).FontSizePt);
        history.Undo();
        Assert.False(Bubble(index).Bold);

        var next = editor.CreateBubble(panel, new Point2D(80, 80));
        Assert.True(Bubble(next).Bold);
        Assert.Equal(6, Bubble(next).FontSizePt);
    }

    [Fact]
    public void With_nothing_selected_the_font_group_sets_the_next_bubble_and_the_next_text()
    {
        var (editor, _, panel) = NewEditor();

        editor.IsTextItalic = true;
        editor.BiggerTextCommand.Execute(null);

        var bubbleIndex = editor.CreateBubble(panel, new Point2D(50, 50));
        var bubble = editor.Working.Panels[panel].Bubbles[bubbleIndex];
        Assert.True(bubble.Italic);
        Assert.Equal(TextEditing.Bigger(Stanley.ProjectModel.Bubbles.Bubble.DefaultFontSizePt), bubble.FontSizePt);
        editor.Select(null);
        var textIndex = editor.CreateText(panel, new Point2D(40, 90));
        var text = ((TextElement)editor.Working.Panels[panel].Elements[textIndex]).Style;
        Assert.True(text.Italic);
        Assert.Equal(TextEditing.Bigger(Stanley.ProjectModel.Bubbles.Bubble.DefaultFontSizePt), text.FontSizePt);
    }

    [Fact]
    public void A_font_this_computer_lacks_is_kept_and_named_in_the_box()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(50, 50));
        editor.SetBubbleFont(panel, index, "Wild Words Pro");

        Assert.Equal("Wild Words Pro", editor.CurrentFontFamily);
        Assert.Null(editor.SelectedFont);
        Assert.Equal("Wild Words Pro (missing)", editor.FontPlaceholder);
        Assert.Contains("isn't on this computer", editor.FontTip, StringComparison.Ordinal);
        Assert.Equal(LetteringFonts.Inter, Lettering.Resolve("Wild Words Pro"));
    }
}
