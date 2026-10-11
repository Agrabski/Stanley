using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.HeadlessTests;

/// <summary>The character editor in the canvas layout: the inspector's Body, Look and Sticker sections, the top bar's tools, and the dialogs the ribbon used to answer.</summary>
[Collection("Page Editor Tests")]
public class CanvasCharacterEditorTests
{
    private static (MainWindow Window, CharacterEditorViewModel Editor, CharacterInspector Inspector) OpenCharacter()
    {
        var window = CanvasLayoutHarness.Open();
        var characters = window.ViewModel.Characters!;
        characters.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (window, characters.Items.Single().Editor, CanvasLayoutHarness.CharacterInspector(window));
    }

    private static CharacterToolbar Toolbar(MainWindow window) =>
        CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<CharacterToolbar>().Single();

    private static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static DropDownButton SlotButton(CharacterInspector inspector, string label) =>
        inspector.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.DataContext is SlotGallery g && g.Label == label);

    /// <summary>Opens a dropdown's flyout and returns what's in it (a flyout's content isn't under the button in the visual tree).</summary>
    private static Control OpenFlyout(Button button)
    {
        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return (Control)((Flyout)button.Flyout!).Content!;
    }

    /// <summary>A click in the middle of <paramref name="control"/>, scrolled into view first (the inspector scrolls).</summary>
    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point FindPoint(Control control, Func<Point, bool> test)
    {
        for (var y = 0.0; y < control.Bounds.Height; y += 4)
            for (var x = 0.0; x < control.Bounds.Width; x += 4)
                if (test(new Point(x, y)))
                    return new Point(x, y);
        throw new InvalidOperationException("No such point on the stage.");
    }

    [Fact]
    public void Opening_a_character_shows_its_inspector_and_the_character_tools()
    {
        var (window, editor, inspector) = OpenCharacter();

        Assert.Equal(editor.Name, inspector.FindInSections<TextBlock>("TitleText")!.Text);
        Assert.True(inspector.FindInSections<BodySection>("BodyProperties")!.IsVisible);
        Assert.True(inspector.FindInSections<LookSection>("LookProperties")!.IsVisible);
        Assert.False(inspector.FindInSections<StickerSection>("StickerProperties")!.IsVisible); // nothing picked yet
        var toolbar = Toolbar(window);
        Assert.True(Named<Button>(toolbar, "BackToPageButton").IsEffectivelyVisible);
        Assert.Empty(window.GetVisualDescendants().OfType<CharacterEditorRibbon>());

        AssertFitsColumn(window, inspector);
    }

    /// <summary>Everything showing in the inspector fits its column - nothing runs off the right.</summary>
    private static void AssertFitsColumn(MainWindow window, CharacterInspector inspector)
    {
        var column = CanvasLayoutHarness.InspectorColumn(window);
        var right = column.Bounds.Width;
        foreach (var control in inspector.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
            Assert.True(control.TranslatePoint(new Point(control.Bounds.Width, 0), column)!.Value.X <= right + 0.5, $"{control.GetType().Name} {control.Name} runs off the inspector");
    }

    [Fact]
    public void The_body_sliders_name_body_types_and_skin_change_the_character()
    {
        var (window, editor, inspector) = OpenCharacter();

        // A slider drag is one undo step, however far it goes.
        var height = inspector.FindInSections<Slider>("HeightSlider")!;
        var before = editor.Working.Body.Height;
        var from = height.TranslatePoint(new Point(height.Bounds.Width * 0.2, height.Bounds.Height / 2), window)!.Value;
        var to = height.TranslatePoint(new Point(height.Bounds.Width * 0.9, height.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, from.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.Working.Body.Height > before, $"{before} -> {editor.Working.Body.Height}");
        window.Workspace.History.Undo();
        Assert.Equal(before, editor.Working.Body.Height);

        inspector.FindInSections<Slider>("WeightSlider")!.Value = 80;
        Assert.Equal(0.8, editor.Working.Body.Build, 3);

        // The name: Enter applies it, Escape puts it back.
        var name = inspector.FindInSections<TextBox>("NameBox")!;
        name.Text = "Mira";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("Mira", editor.Working.Name);
        Assert.Equal("Mira", inspector.FindInSections<TextBlock>("TitleText")!.Text);
        name.Text = "Nobody";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal("Mira", name.Text);

        // A body type from its gallery, and a skin swatch.
        var presets = OpenFlyout(inspector.FindInSections<DropDownButton>("BodyTypeButton")!);
        var chibi = presets.GetLogicalDescendants().OfType<Button>().Single(b => b.DataContext is BodyPresetChoice { Preset: BodyPreset.Chibi });
        chibi.Command!.Execute(chibi.CommandParameter);
        Assert.Equal(BodyPresets.Shape(BodyPreset.Chibi).Normalized(), editor.Working.Body);
        inspector.FindInSections<DropDownButton>("BodyTypeButton")!.Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();
        var dark = inspector.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is SkinSwatch { Name: "Dark" });
        Click(window, dark);
        Assert.Equal(((SkinSwatch)dark.DataContext!).Color, editor.Working.Skin);
    }

    [Fact]
    public void A_pick_in_a_Look_gallery_dresses_the_character_and_a_worn_sticker_gets_the_Sticker_section()
    {
        var (window, editor, inspector) = OpenCharacter();
        var stickerSection = inspector.FindInSections<StickerSection>("StickerProperties")!;

        var top = SlotButton(inspector, "Top");
        var topAt = top.TranslatePoint(new Point(0, 0), window)!.Value;
        var content = OpenFlyout(top);
        // The gallery opens beside the inspector, not over it.
        Assert.True(content.TranslatePoint(new Point(content.Bounds.Width, 0), window)!.Value.X <= inspector.TranslatePoint(new Point(0, 0), window)!.Value.X + 1);
        var tee = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is StickerChoice { Label: "T-shirt" });
        tee.Command!.Execute(tee.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("T-shirt", editor.Gallery(StickerSlots.Top).Current);
        Assert.Equal("T-shirt", ((SlotGallery)SlotButton(inspector, "Top").DataContext!).Current);
        Assert.Contains(inspector.GetVisualDescendants().OfType<DropDownButton>(), b => b.DataContext is ColorSlotEditor { Slot: "top" });
        // Put on from the gallery, it's picked: the Sticker section shows it, with its fit - below
        // the galleries, which haven't moved from under the pointer for the next pick.
        Assert.True(stickerSection.IsVisible);
        Assert.True(inspector.FindInSections<Slider>("FitSlider")!.IsEffectivelyVisible);
        Assert.Equal(topAt, SlotButton(inspector, "Top").TranslatePoint(new Point(0, 0), window)!.Value);
        top.Flyout!.Hide();

        // Done lets go of it; a click on it on the stage picks it again.
        Click(window, inspector.FindInSections<Button>("DeselectStickerButton")!);
        Assert.False(editor.HasSelectedSticker);
        Assert.False(stickerSection.IsVisible);
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        var worn = editor.Working.Stickers[StickerSlots.Top].Single();
        var chest = stage.TranslatePoint(FindPoint(stage, p => stage.StickerAt(p) == worn), window)!.Value;
        window.MouseDown(chest, MouseButton.Left);
        window.MouseUp(chest, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.HasSelectedSticker);
        Assert.True(stickerSection.IsVisible);
        // Picked on the stage, the section comes into view.
        var scroller = inspector.FindInSections<ScrollViewer>("Scroller")!;
        Assert.InRange(stickerSection.TranslatePoint(new Point(0, 0), scroller)!.Value.Y, 0, scroller.Viewport.Height - 40);

        // Its fit slider changes the T-shirt, and Take off takes it off.
        AssertFitsColumn(window, inspector);
        var fit = inspector.FindInSections<Slider>("FitSlider")!;
        fit.Value = 40;
        Assert.Equal(40, editor.SelectedFit, 0);
        Click(window, inspector.FindInSections<Button>("TakeOffButton")!);
        Assert.Equal("None", editor.Gallery(StickerSlots.Top).Current);

        // A cap has ways to wear it - the brim forward, back or to a side - each previewed.
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Headwear).Choices.Single(c => c.Label == "Cap"));
        Dispatcher.UIThread.RunJobs();
        var styles = inspector.FindInSections<ItemsControl>("StyleGallery")!;
        Assert.True(styles.IsEffectivelyVisible);
        var other = styles.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is StickerStyleChoice { IsCurrent: false });
        var style = (StickerStyleChoice)other.DataContext!;
        Click(window, other);
        Assert.Contains(inspector.FindInSections<ItemsControl>("StyleGallery")!.GetVisualDescendants().OfType<Button>(),
            b => b.DataContext is StickerStyleChoice { IsCurrent: true } current && current.Label == style.Label);
        AssertFitsColumn(window, inspector);
    }

    [Fact]
    public void A_gallery_opens_beside_its_button_without_scrolling_the_inspector_or_taking_focus()
    {
        var (window, editor, inspector) = OpenCharacter();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt")); // the Sticker section makes it scroll
        Dispatcher.UIThread.RunJobs();
        var scroller = inspector.FindInSections<ScrollViewer>("Scroller")!;
        var inside = scroller.TranslatePoint(new Point(100, 300), window)!.Value;
        for (var i = 0; i < 10; i++)
            window.MouseWheel(inside, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        var scrolled = scroller.Offset.Y;
        Assert.True(scrolled > 0);

        var eyes = SlotButton(inspector, "Eyes");
        Click(window, eyes);
        Assert.True(eyes.Flyout!.IsOpen);
        Assert.Equal(scrolled, scroller.Offset.Y);
        Assert.Null(window.FocusManager?.GetFocusedElement() as TextBox);
        var content = (Control)((Flyout)eyes.Flyout).Content!;
        var top = eyes.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.InRange(content.TranslatePoint(new Point(0, 0), window)!.Value.Y, top, top + 20);

        // A click elsewhere still closes it.
        var stage = window.GetVisualDescendants().OfType<CharacterEditorView>().Single().Figure;
        Click(window, stage);
        Assert.False(eyes.Flyout.IsOpen);
    }

    [Fact]
    public void The_Hair_button_dresses_the_head_and_stays_open_for_the_next_try()
    {
        var (_, editor, inspector) = OpenCharacter();
        if (Stanley.StickerLibrary.Hairstyles.Available.FirstOrDefault() is not { } preset)
            return;

        var hair = inspector.FindInSections<DropDownButton>("HairButton")!;
        var content = OpenFlyout(hair);
        var choice = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is HairstyleChoice c && c.Label == preset.Name);
        choice.Command!.Execute(choice.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(preset.Name, editor.HairCurrent);
        Assert.True(hair.Flyout!.IsOpen);
        Assert.Contains(inspector.GetVisualDescendants().OfType<DropDownButton>(), b => b.DataContext is ColorSlotEditor { Slot: "hair" });
        hair.Flyout.Hide();
    }

    [Fact]
    public void The_Text_button_in_the_Prints_gallery_opens_the_print_text_box_to_type_over()
    {
        var (window, editor, inspector) = OpenCharacter();
        var scroller = inspector.FindInSections<ScrollViewer>("Scroller")!;
        Assert.Equal(0, scroller.Offset.Y); // up at the top, above the galleries

        var prints = SlotButton(inspector, "Prints");
        var content = OpenFlyout(prints);
        var text = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "WearTextButton");
        text.Command!.Execute(text.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        var box = inspector.FindInSections<TextBox>("PrintTextBox")!;
        Assert.True(editor.SelectedIsTextPrint);
        Assert.Equal("HELLO", box.Text);
        Assert.True(box.IsFocused);
        Assert.False(prints.Flyout!.IsOpen);
        var at = box.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
        Assert.InRange(at, 0, scroller.Viewport.Height - box.Bounds.Height);

        // Typing over it and pressing Enter renames the print.
        box.Text = "SKATE";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("SKATE", editor.SelectedStickerName);
        AssertFitsColumn(window, inspector);
    }

    [Fact]
    public void The_inspector_answers_the_editors_requests_for_a_window_as_the_ribbon_did()
    {
        var (_, editor, inspector) = OpenCharacter();

        // Import..., a pattern's Custom... and Draw your own... are plain events: without a listener they'd do nothing.
        foreach (var name in new[] { nameof(CharacterEditorViewModel.ArtImportRequested), nameof(CharacterEditorViewModel.TileImportRequested),
                     nameof(CharacterEditorViewModel.SvgEditorConfigurationRequested) })
            Assert.Contains(Listeners(editor, name), target => target?.GetType().Name == "CharacterEditorDialogs");

        // The Other gallery's Import... asks for a file.
        var content = OpenFlyout(SlotButton(inspector, "Other"));
        var import = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ImportArtButton");
        ArtImportRequest? asked = null;
        editor.ArtImportRequested += (_, r) => asked = r;
        import.Command!.Execute(import.CommandParameter);
        Assert.Equal(new ArtImportRequest(StickerSlots.Accessory), asked);
    }

    private static IEnumerable<object?> Listeners(object source, string eventName) =>
        (source.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) as Delegate)?.GetInvocationList().Select(d => d.Target) ?? [];

    [Fact]
    public void The_toolbar_switches_front_and_side_and_takes_you_back_to_the_page()
    {
        var (window, editor, _) = OpenCharacter();
        var toolbar = Toolbar(window);

        Click(window, Named<RadioButton>(toolbar, "SidePreviewButton"));
        Assert.True(editor.IsSidePreview);
        Click(window, Named<RadioButton>(toolbar, "FrontPreviewButton"));
        Assert.True(editor.IsFrontPreview);

        // The comparison line-up and the expression preview are view too.
        Click(window, Named<ToggleButton>(toolbar, "LineUpToggle"));
        Assert.False(editor.ShowLineUp);
        var expressions = OpenFlyout(Named<Button>(toolbar, "PreviewExpressionButton"));
        var other = expressions.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is ExpressionPresetChoice { IsCurrent: false });
        var picked = (ExpressionPresetChoice)other.DataContext!;
        other.Command!.Execute(other.CommandParameter);
        Assert.Equal(picked.Preset, editor.PreviewExpression);
        Assert.Equal(picked.Name, Named<TextBlock>(toolbar, "PreviewExpressionText").Text);
        Named<Button>(toolbar, "PreviewExpressionButton").Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();

        Click(window, Named<Button>(toolbar, "BackToPageButton"));
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        Assert.Single(CanvasLayoutHarness.InspectorColumn(window).GetVisualDescendants().OfType<PageInspector>());
        Assert.Single(CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<PageToolbar>());
    }
}
