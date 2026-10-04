using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Issue #19: what's typed into a bubble (or a text box) has to look the way it will once Enter
/// keeps it - same size of letters, same words on each line, and the bubble already as big as
/// it will be - instead of jumping when the editor closes.
/// </summary>
[Collection("Page Editor Tests")]
public class InlineTextFidelityTests
{
    private const string Long = "I told you the bridge was closed three times already, but nobody on this crew ever listens to a word I say";

    private static (MainWindow Window, PageCanvasControl Canvas, PageEditorView View, PanelId Panel, Rect2D Bounds) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().First();
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var panelId = window.Editor.Working.PanelOrder[0];
        return (window, canvas, view, panelId, window.Editor.PanelBounds(panelId));
    }

    private static Point At(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    private static void TypeBubble(MainWindow window, PageCanvasControl canvas, Rect2D panel, string words)
    {
        window.DoubleClick(At(window, canvas, panel.MidX, panel.MidY));
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput(words);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Some<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private static void PressEnter(MainWindow window)
    {
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The lines the editor actually broke the words into, as Avalonia laid them out.</summary>
    private static List<string> EditorLines(PageEditorView view)
    {
        var words = view.TextEditor.Text ?? "";
        var layout = view.TextEditor.GetVisualDescendants().OfType<TextPresenter>().Single().TextLayout;
        return layout.TextLines.Select(l => words.Substring(l.FirstTextSourceIndex, l.Length).TrimEnd()).ToList();
    }

    [Fact]
    public void A_bubble_being_typed_into_is_lettered_at_the_size_the_page_draws_it()
    {
        var (window, canvas, view, panelId, bounds) = Open();
        TypeBubble(window, canvas, bounds, Long);

        Assert.True(view.TextEditor.IsVisible);
        var expected = FontPoints.ToMm(Bubble.DefaultFontSizePt) * canvas.Zoom;
        Assert.Equal(expected, view.TextEditor.FontSize, 6);
    }

    [Fact]
    public void A_bubble_being_typed_into_breaks_its_lines_at_the_same_words_the_page_does()
    {
        var (window, canvas, view, _, bounds) = Open();
        TypeBubble(window, canvas, bounds, Long);

        var draft = Some(view.Canvas.EditingBubble?.Draft);
        using var font = Lettering.Font(PageRenderer.FontSizeMm);
        using var paint = new SKPaint { IsAntialias = true };
        var onPage = Lettering.Wrap(Long, font, paint, (float)BubbleTextRenderer.TextArea(draft).Width);

        Assert.True(onPage.Count > 1, "the test text should need more than one line");
        Assert.Equal(onPage, EditorLines(view));
    }

    /// <summary>The page sets letters one by one, without kerning pairs or ligatures; a text box that did would set the same words narrower.</summary>
    [Fact]
    public void A_bubble_being_typed_into_sets_its_letters_as_wide_as_the_page_does()
    {
        const string kerned = "AVATAR WAVY Ty To Yo fi fl";
        var (window, canvas, view, _, bounds) = Open();
        TypeBubble(window, canvas, bounds, kerned);

        var layout = view.TextEditor.GetVisualDescendants().OfType<TextPresenter>().Single().TextLayout;
        var lines = EditorLines(view);
        using var font = Lettering.Font(PageRenderer.FontSizeMm);
        using var paint = new SKPaint { IsAntialias = true };
        using var measurer = new Lettering.Measurer(font);

        Assert.Equal(lines.Count, layout.TextLines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var typed = layout.TextLines[i].Width / canvas.Zoom;
            Assert.Equal(measurer.Width(lines[i], paint), typed, typed * 1e-3);
        }
    }

    [Fact]
    public void A_bubble_being_typed_into_is_already_as_big_as_Enter_will_leave_it()
    {
        var (window, canvas, view, panelId, bounds) = Open();
        window.DoubleClick(At(window, canvas, bounds.MidX, bounds.MidY));
        Dispatcher.UIThread.RunJobs();
        var before = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[0].Shape.Anchors);

        window.KeyTextInput(Long);
        Dispatcher.UIThread.RunJobs();
        var draft = Some(view.Canvas.EditingBubble?.Draft);
        var typing = AnchorRing.BoundingBox(draft.Shape.Anchors);
        Assert.True(typing.Width > before.Width, "a long line should have grown the bubble while typing");

        PressEnter(window);
        var kept = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[0].Shape.Anchors);
        Assert.Equal(typing.Left, kept.Left, 6);
        Assert.Equal(typing.Top, kept.Top, 6);
        Assert.Equal(typing.Width, kept.Width, 6);
        Assert.Equal(typing.Height, kept.Height, 6);
    }

    [Fact]
    public void The_editor_sits_where_the_kept_lettering_will_be()
    {
        var (window, canvas, view, panelId, bounds) = Open();
        TypeBubble(window, canvas, bounds, Long);

        var draft = Some(view.Canvas.EditingBubble?.Draft);
        var area = canvas.PageToControl(BubbleTextRenderer.TextArea(draft));
        // Layout rounds the box to whole pixels, so half a pixel either way is the same place.
        Assert.InRange(view.TextEditor.Bounds.Center.X, area.Center.X - 1, area.Center.X + 1);
        Assert.InRange(view.TextEditor.Bounds.Center.Y, area.Center.Y - 1, area.Center.Y + 1);
        Assert.InRange(view.TextEditor.Bounds.Width, area.Width - 1, area.Width + 1);
    }

    [Fact]
    public void Words_left_as_they_were_do_not_grow_the_bubble()
    {
        var (window, canvas, view, panelId, bounds) = Open();
        TypeBubble(window, canvas, bounds, "Hi");
        PressEnter(window);
        Assert.False(view.TextEditor.IsVisible);

        view.BeginTextEdit(panelId, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible);
        Assert.Null(view.Canvas.EditingBubble?.Draft);
    }

    [Fact]
    public void Text_being_typed_is_lettered_and_boxed_as_it_will_be_once_kept()
    {
        var (window, canvas, view, panelId, bounds) = Open();
        window.Editor.ApplyTextPresetCommand.Execute(TextStylePreset.Caption);
        window.Editor.Tool = PageEditorTool.Text;
        var click = At(window, canvas, bounds.Left + 20, bounds.Top + 20);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.TextEditor.IsVisible);

        window.KeyTextInput(Long);
        Dispatcher.UIThread.RunJobs();

        var draft = Some(view.Canvas.EditingTextDraft);
        Assert.Equal(draft.Style.FontSizeMm * canvas.Zoom, view.TextEditor.FontSize, 6);

        using var font = Lettering.Font((float)draft.Style.FontSizeMm, draft.Style.Bold, draft.Style.Italic, draft.Style.FontFamily);
        using var paint = new SKPaint { IsAntialias = true };
        Assert.Equal(Lettering.Wrap(Long, font, paint, (float)ElementRenderer.TextArea(draft).Width), EditorLines(view));

        PressEnter(window);
        var kept = Assert.IsType<TextElement>(Assert.Single(window.Editor.Working.Panels[panelId].Elements));
        Assert.Equal(draft.Bounds, kept.Bounds);
    }
}
