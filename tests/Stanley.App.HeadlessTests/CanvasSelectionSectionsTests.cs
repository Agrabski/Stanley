using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// The canvas layout's inspector sections for a panel, a bubble, free text, a picture and speed
/// lines: each shows for its selection (the bubble's and the text's also for their tool in hand,
/// for what's added next) and does what the ribbon's tab for it does, through the same view model.
/// </summary>
[Collection("Page Editor Tests")]
public class CanvasSelectionSectionsTests
{
    private static (MainWindow Window, PageEditorViewModel Editor, PanelId Panel, Rect2D Bounds) Open()
    {
        var window = CanvasLayoutHarness.Open();
        var panelId = window.Editor.Working.PanelOrder[0];
        return (window, window.Editor, panelId, window.Editor.PanelBounds(panelId));
    }

    private static T Section<T>(MainWindow window) where T : UserControl
    {
        Dispatcher.UIThread.RunJobs();
        return CanvasLayoutHarness.PageInspector(window).GetVisualDescendants().OfType<T>().Single();
    }

    private static T Named<T>(Control scope, string name) where T : Control =>
        scope.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>A real click in the middle of <paramref name="control"/>, scrolled into the inspector's view first.</summary>
    private static void Click(MainWindow window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var middle = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Execute(Button button)
    {
        Assert.True(button.IsEffectivelyVisible, $"{button.Name} isn't showing");
        Assert.True(button.Command!.CanExecute(button.CommandParameter), $"{button.Name} can't do anything now");
        button.Command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Nothing in a section's content may stick out past the inspector's content width (the column less a section's padding).</summary>
    private static void AssertFitsTheColumn(MainWindow window, UserControl section)
    {
        Dispatcher.UIThread.RunJobs();
        var column = CanvasLayoutHarness.InspectorColumn(window);
        var right = column.TranslatePoint(new Point(column.Bounds.Width, 0), window)!.Value.X;
        var content = section.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("inspectorSection") && b.Child != null)
            .SelectMany(b => b.Child!.GetVisualDescendants().Prepend(b.Child));
        Assert.NotEmpty(content);
        foreach (var control in content.OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
        {
            var edge = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
            Assert.True(edge <= right - 11.5, $"{control.GetType().Name} {control.Name} in {section.GetType().Name} reaches {edge:0.#}, past the column's content edge {right - 12:0.#}");
        }
    }

    private static int AddText(PageEditorViewModel editor, PanelId panelId, Rect2D bounds, TextStylePreset preset = TextStylePreset.Plain)
    {
        var index = editor.CreateText(panelId, new Point2D(bounds.MidX, bounds.MidY), style: TextStylePresets.Style(preset));
        editor.SetElementText(panelId, index, "BOOM");
        editor.SelectElement(panelId, index);
        return index;
    }

    // ---------------------------------------------------------------- panel

    [Fact]
    public void The_Panel_section_splits_fills_and_borders_the_selected_panel()
    {
        var (window, editor, panelId, _) = Open();
        editor.Select(panelId);
        var section = Section<PanelSection>(window);
        Assert.True(section.IsVisible);
        Assert.False(Section<BubbleSection>(window).IsVisible);
        AssertFitsTheColumn(window, section);

        // Background: the gallery opens from the drop-down and fills the panel.
        var dropDown = Named<DropDownButton>(section, "PanelBackgroundButton");
        var picker = Assert.IsType<BackgroundPicker>(((Flyout)dropDown.Flyout!).Content);
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();
        var sky = picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackgroundDaysky");
        sky.Command!.Execute(sky.CommandParameter);
        dropDown.Flyout.Hide();
        Assert.IsType<GradientBackground>(editor.Working.Panels[panelId].Background);
        Assert.Equal("Day sky", editor.BackgroundName);

        // Shape Fill's face applies its colour to the panel; Shape Outline offers weight and dashes.
        var fill = Named<ColorMenuButton>(section, "PanelFillButton");
        fill.Button.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Assert.Equal(ColorValue.FromHex("#4472c4"), editor.CurrentPanelStyle.Fill);
        var outline = Named<ColorMenuButton>(section, "PanelOutlineButton");
        Assert.Contains(outline.Menu.Items.OfType<MenuItem>(), i => i.Header as string == "Dashes");

        // The border switch, by a real click.
        Click(window, Named<CheckBox>(section, "PanelBorderButton"));
        Assert.True(editor.Working.Panels[panelId].Borderless);
        Click(window, Named<CheckBox>(section, "PanelBorderButton"));
        Assert.False(editor.Working.Panels[panelId].Borderless);

        Click(window, Named<Button>(section, "SplitColumnsButton"));
        Assert.Equal(2, editor.Working.PanelOrder.Count);
    }

    [Fact]
    public void The_Panel_sections_add_bubble_and_delete_act_on_the_panel()
    {
        var (window, editor, panelId, _) = Open();
        editor.Select(panelId);
        var section = Section<PanelSection>(window);

        Execute(Named<Button>(section, "PanelAddBubbleButton"));
        Assert.Single(editor.Working.Panels[panelId].Bubbles);

        editor.Select(panelId);
        Dispatcher.UIThread.RunJobs();
        var panels = editor.Working.PanelOrder.Count;
        // Delete is the Arrange row's, for whatever is selected.
        Execute(Named<Button>(Section<ArrangeSection>(window), "DeleteButton"));
        Assert.Equal(panels - 1, editor.Working.PanelOrder.Count);
    }

    [Fact]
    public void A_thought_cloud_has_no_split_but_keeps_its_fill_and_border()
    {
        var (window, editor, _, _) = Open();
        editor.ClearSelection();
        editor.InsertThoughtCloud();
        Assert.True(editor.IsSelectedPanelCloud);
        var section = Section<PanelSection>(window);

        Assert.True(section.IsVisible);
        Assert.False(Named<Button>(section, "SplitColumnsButton").IsEffectivelyVisible);
        Assert.False(Named<Button>(section, "SplitRowsButton").IsEffectivelyVisible);
        Assert.True(Named<ColorMenuButton>(section, "PanelFillButton").IsEffectivelyVisible);
        Assert.True(Named<CheckBox>(section, "PanelBorderButton").IsEffectivelyVisible);
    }

    // ---------------------------------------------------------------- bubble

    [Fact]
    public void The_Bubble_section_restyles_letters_tails_and_arranges_the_selected_bubble()
    {
        var (window, editor, panelId, bounds) = Open();
        var index = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        var section = Section<BubbleSection>(window);
        Assert.True(section.IsVisible);
        Assert.False(Section<PanelSection>(window).IsVisible);
        Assert.True(Named<Button>(section, "EditTextButton").IsEffectivelyVisible);
        AssertFitsTheColumn(window, section);
        Bubble Bubble() => editor.Working.Panels[panelId].Bubbles[index];

        Click(window, Named<RadioButton>(section, "ShoutStyleButton"));
        Assert.Equal(BubbleStylePreset.Shout, Bubble().Style);
        Click(window, Named<RadioButton>(section, "ThoughtStyleButton"));
        Assert.Equal(BubbleStylePreset.Thought, Bubble().Style);
        Assert.True(Named<RadioButton>(section, "ThoughtStyleButton").IsChecked);
        Assert.False(Named<RadioButton>(section, "ShoutStyleButton").IsChecked);

        // Lettering: bold by a click, a size typed into the box.
        var lettering = Named<LetteringRows>(section, "BubbleLettering");
        Click(window, Named<ToggleButton>(lettering, "BoldButton"));
        Assert.True(Bubble().Bold);
        lettering.Sizes.Entry.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("14");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(14, Bubble().FontSizePt);
        Assert.True(window.GetVisualDescendants().OfType<PageEditorView>().Single().Canvas.IsFocused); // the keyboard went back to the page
        Click(window, Named<RadioButton>(lettering, "AlignLeftButton"));
        Assert.Equal(TextAlign.Left, Bubble().Align);

        // Tails: add one, and Remove tail says which it takes off.
        var tails = Bubble().Tails.Count;
        Execute(Named<Button>(section, "AddTailButton"));
        Assert.Equal(tails + 1, Bubble().Tails.Count);
        var remove = Named<Button>(section, "RemoveTailButton");
        Assert.Contains("ringed tail", ToolTip.GetTip(remove) as string, StringComparison.Ordinal);
        Execute(remove);
        Assert.Equal(tails, Bubble().Tails.Count);

        // Arrange: a second bubble on top, then this one to the front.
        var other = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        var id = Bubble().Id;
        editor.Select(panelId, bubbleIndex: index);
        Dispatcher.UIThread.RunJobs();
        Execute(Named<Button>(section, "BubbleToFrontButton"));
        Assert.Equal(id, editor.Working.Panels[panelId].Bubbles[^1].Id);
        Assert.NotEqual(-1, other);

        var count = editor.Working.Panels[panelId].Bubbles.Count;
        // Delete is the Arrange row's, for whatever is selected.
        Execute(Named<Button>(Section<ArrangeSection>(window), "DeleteButton"));
        Assert.Equal(count - 1, editor.Working.Panels[panelId].Bubbles.Count);
    }

    [Fact]
    public void With_the_Bubble_tool_and_no_bubble_the_section_sets_the_next_bubbles_style_and_lettering()
    {
        var (window, editor, panelId, bounds) = Open();
        editor.ClearSelection();
        var section = Section<BubbleSection>(window);
        Assert.False(section.IsVisible);

        editor.Tool = PageEditorTool.Bubble;
        Dispatcher.UIThread.RunJobs();
        Assert.True(section.IsVisible);
        Assert.True(Named<TextBlock>(section, "NewBubbleHeader").IsEffectivelyVisible);
        // Only what a bubble that doesn't exist yet can have.
        Assert.False(Named<Button>(section, "EditTextButton").IsEffectivelyVisible);
        Assert.False(Named<Button>(section, "AddTailButton").IsEffectivelyVisible);
        Assert.False(Named<Button>(section, "BubbleToFrontButton").IsEffectivelyVisible);
        AssertFitsTheColumn(window, section);

        Click(window, Named<RadioButton>(section, "WhisperStyleButton"));
        Click(window, Named<ToggleButton>(Named<LetteringRows>(section, "BubbleLettering"), "ItalicButton"));
        var index = editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        var bubble = editor.Working.Panels[panelId].Bubbles[index];
        Assert.Equal(BubbleStylePreset.Whisper, bubble.Style);
        Assert.True(bubble.Italic);
    }

    // ---------------------------------------------------------------- text

    [Fact]
    public void The_Text_section_sets_the_kind_colours_layer_and_fields_of_the_selected_text()
    {
        var (window, editor, panelId, bounds) = Open();
        var index = AddText(editor, panelId, bounds);
        var section = Section<TextSection>(window);
        Assert.True(section.IsVisible);
        Assert.False(Section<BubbleSection>(window).IsVisible);
        AssertFitsTheColumn(window, section);
        TextElement Text() => (TextElement)editor.Working.Panels[panelId].Elements[index];

        Click(window, Named<Button>(section, "CaptionKindButton"));
        Assert.Equal(TextStylePresets.Style(TextStylePreset.Caption).BoxFill, Text().Style.BoxFill);
        Assert.Equal("Caption", Named<TextBlock>(section, "TextKindName").Text);

        // The letters and the box are coloured separately, as on the ribbon's Text tab.
        var textFill = Named<ColorMenuButton>(section, "TextFillButton");
        textFill.Menu.ShowAt(textFill.Button);
        Dispatcher.UIThread.RunJobs();
        var noFill = textFill.Menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "No Fill");
        noFill.Command!.Execute(noFill.CommandParameter);
        textFill.Menu.Hide();
        Assert.Null(Text().Style.Color);
        Named<ColorMenuButton>(section, "TextBoxOutlineButton").Button.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Assert.Equal(ColorValue.FromHex("#000000"), Text().Style.BoxStroke);
        Named<ColorMenuButton>(section, "TextOutlineButton").Button.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Assert.Equal(ColorValue.FromHex("#000000"), Text().Style.Outline);

        Click(window, Named<RadioButton>(section, "TextInFrontButton"));
        Assert.Equal(ElementLayer.Foreground, Text().Layer);
        Click(window, Named<RadioButton>(section, "TextBehindButton"));
        Assert.Equal(ElementLayer.Background, Text().Layer);

        var title = Named<ItemsControl>(section, "FieldButtons").GetVisualDescendants().OfType<Button>().First();
        Execute(title);
        Assert.EndsWith(TextFields.TitleField, Text().Text, StringComparison.Ordinal);

        Assert.True(Named<Button>(section, "EditElementTextButton").Command!.CanExecute(null));
        var count = editor.Working.Panels[panelId].Elements.Count;
        // Delete is the Arrange row's, for whatever is selected.
        Execute(Named<Button>(Section<ArrangeSection>(window), "DeleteButton"));
        Assert.Equal(count - 1, editor.Working.Panels[panelId].Elements.Count);
    }

    [Fact]
    public void The_Text_section_lettering_changes_the_selected_text()
    {
        var (window, editor, panelId, bounds) = Open();
        var index = AddText(editor, panelId, bounds);
        var lettering = Named<LetteringRows>(Section<TextSection>(window), "TextLettering");

        Click(window, Named<Button>(lettering, "BiggerButton"));
        Click(window, Named<RadioButton>(lettering, "AlignRightButton"));

        var style = ((TextElement)editor.Working.Panels[panelId].Elements[index]).Style;
        Assert.True(style.FontSizePt > TextStylePresets.Style(TextStylePreset.Plain).FontSizePt);
        Assert.Equal(TextAlign.Right, style.Align);
        Assert.Equal(FontSizeBox.Format(style.FontSizePt), lettering.Sizes.Entry.Text);
    }

    [Fact]
    public void With_the_Text_tool_and_no_text_the_section_sets_what_the_next_text_gets()
    {
        var (window, editor, panelId, bounds) = Open();
        editor.ClearSelection();
        editor.Tool = PageEditorTool.Text;
        var section = Section<TextSection>(window);
        Assert.True(section.IsVisible);
        Assert.True(Named<TextBlock>(section, "NewTextHeader").IsEffectivelyVisible);
        Assert.False(Named<Button>(section, "EditElementTextButton").IsEffectivelyVisible);
        Assert.False(Named<Button>(section, "TextToFrontButton").IsEffectivelyVisible);
        Assert.False(Named<ItemsControl>(section, "FieldButtons").IsEffectivelyVisible);

        Click(window, Named<Button>(section, "SoundEffectKindButton"));
        Click(window, Named<RadioButton>(section, "TextInFrontButton"));
        var index = editor.CreateText(panelId, new Point2D(bounds.MidX, bounds.MidY));

        var text = (TextElement)editor.Working.Panels[panelId].Elements[index];
        Assert.Equal(TextStylePreset.SoundEffect, TextStylePresets.Of(text.Style));
        Assert.Equal(ElementLayer.Foreground, text.Layer);
    }

    // ---------------------------------------------------------------- picture and speed lines

    [Fact]
    public void The_Picture_section_moves_the_selected_picture_between_layers_and_deletes_it()
    {
        var (window, editor, panelId, _) = Open();
        using var bitmap = new SkiaSharp.SKBitmap(20, 10);
        bitmap.Erase(SkiaSharp.SKColors.Purple);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        Assert.True(editor.ImportPicture(new PictureImportRequest(panelId, AsBackground: false), "p.png", Stanley.ProjectModel.Characters.ArtFile.Png(data.ToArray())));
        var section = Section<PictureSection>(window);
        Assert.True(section.IsVisible);
        Assert.False(Section<TextSection>(window).IsVisible);
        Assert.False(Section<SpeedLinesSection>(window).IsVisible);
        AssertFitsTheColumn(window, section);
        var index = editor.SelectedElementIndex;

        Click(window, Named<RadioButton>(section, "PictureInFrontButton"));
        Assert.Equal(ElementLayer.Foreground, editor.Working.Panels[panelId].Elements[index].Layer);
        Assert.True(Named<RadioButton>(section, "PictureInFrontButton").IsChecked);
        Assert.False(Named<RadioButton>(section, "PictureBehindButton").IsChecked);

        Assert.True(Named<Button>(section, "PictureToBackButton").Command!.CanExecute(null));
        // Delete is the Arrange row's, for whatever is selected.
        Execute(Named<Button>(Section<ArrangeSection>(window), "DeleteButton"));
        Assert.DoesNotContain(editor.Working.Panels[panelId].Elements, e => e is PictureElement);
    }

    [Fact]
    public void The_Speed_lines_section_sets_colour_count_and_thickness_and_shuffles()
    {
        var (window, editor, panelId, _) = Open();
        editor.Select(panelId);
        var index = editor.InsertSpeedLines();
        var section = Section<SpeedLinesSection>(window);
        Assert.True(section.IsVisible);
        Assert.False(Section<PanelSection>(window).IsVisible);
        AssertFitsTheColumn(window, section);
        SpeedLinesStyle Style() => ((SpeedLinesElement)editor.Working.Panels[panelId].Elements[index]).Style;

        Named<Slider>(section, "SpeedLinesCountSlider").Value = 150;
        Named<Slider>(section, "SpeedLinesThicknessSlider").Value = 2.5;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(150, Style().Count);
        Assert.Equal(2.5, Style().WidthMm, 3);

        var seed = Style().Seed;
        Click(window, Named<Button>(section, "ShuffleSpeedLinesButton"));
        Assert.NotEqual(seed, Style().Seed);

        var color = Named<ColorMenuButton>(section, "SpeedLinesColorButton");
        color.Menu.ShowAt(color.Button);
        Dispatcher.UIThread.RunJobs();
        var red = DrawingPalette.StandardColors.Single(c => c.Name == "Red");
        color.Command!.Execute(red);
        color.Menu.Hide();
        Assert.Equal(red.Color, Style().Color);

        Click(window, Named<RadioButton>(section, "SpeedLinesInFrontButton"));
        Assert.Equal(ElementLayer.Foreground, editor.Working.Panels[panelId].Elements[index].Layer);

        // Delete is the Arrange row's, for whatever is selected.
        Execute(Named<Button>(Section<ArrangeSection>(window), "DeleteButton"));
        Assert.DoesNotContain(editor.Working.Panels[panelId].Elements, e => e is SpeedLinesElement);
    }
}
