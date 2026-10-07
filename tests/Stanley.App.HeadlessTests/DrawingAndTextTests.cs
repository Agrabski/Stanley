using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>Drawing shapes, lettering free text and picking panel backgrounds, through the real window: pointer, keyboard and ribbon.</summary>
[Collection("Page Editor Tests")]
public class DrawingAndTextTests
{
    private static (MainWindow Window, PageCanvasControl Canvas, PanelId Panel, Rect2D Bounds) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().First();
        var panelId = window.Editor.Working.PanelOrder[0];
        return (window, canvas, panelId, window.Editor.PanelBounds(panelId));
    }

    private static Point At(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    private static void Press(MainWindow window, Key key, PhysicalKey physical, string text)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        Dispatcher.UIThread.RunJobs();
    }

    private static PageEditorRibbon Ribbon(MainWindow window) => window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();

    [Fact]
    public void Drawing_a_loop_with_the_pen_closes_it_into_a_shape_and_the_pen_stays_on()
    {
        var (window, canvas, panelId, bounds) = Open();
        canvas.Focus();
        Press(window, Key.D, PhysicalKey.D, "d");
        Assert.Equal(PageEditorTool.Draw, window.Editor.Tool);

        var (cx, cy) = (bounds.MidX, bounds.MidY);
        window.MouseDown(At(window, canvas, cx + 25, cy), MouseButton.Left);
        for (var i = 1; i <= 40; i++)
        {
            var a = i * Math.PI * 2 / 40;
            window.MouseMove(At(window, canvas, cx + Math.Cos(a) * 25, cy + Math.Sin(a) * 25));
        }
        window.MouseUp(At(window, canvas, cx + 25, cy), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var shape = Assert.IsType<ShapeElement>(Assert.Single(window.Editor.Working.Panels[panelId].Elements));
        Assert.True(shape.Closed);
        Assert.InRange(PanelElements.Bounds(shape).Width, 45, 55);
        Assert.Equal(PageEditorTool.Draw, window.Editor.Tool);
        Assert.True(window.History.CanUndo);
    }

    [Fact]
    public void A_rectangle_dragged_out_is_selected_and_shows_the_Shape_tab_whose_buttons_restyle_it()
    {
        var (window, canvas, panelId, bounds) = Open();
        canvas.Focus();
        Press(window, Key.R, PhysicalKey.R, "r");

        window.MouseDown(At(window, canvas, bounds.Left + 20, bounds.Top + 20), MouseButton.Left);
        window.MouseMove(At(window, canvas, bounds.Left + 50, bounds.Top + 40));
        window.MouseMove(At(window, canvas, bounds.Left + 60, bounds.Top + 45));
        window.MouseUp(At(window, canvas, bounds.Left + 60, bounds.Top + 45), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var shape = Assert.IsType<ShapeElement>(Assert.Single(window.Editor.Working.Panels[panelId].Elements));
        Assert.Equal(40, PanelElements.Bounds(shape).Width, 1);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool);
        var ribbon = Ribbon(window);
        var shapeTab = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ShapeTab");
        Assert.True(shapeTab.IsVisible);

        ribbon.TabControl.SelectedItem = shapeTab;
        Dispatcher.UIThread.RunJobs();
        var inFront = ribbon.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "ShapeInFrontButton");
        inFront.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ElementLayer.Foreground, window.Editor.Working.Panels[panelId].Elements[0].Layer);

        window.Editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shapeTab.IsVisible);
        Assert.Equal("HomeTab", ((TabItem)ribbon.TabControl.SelectedItem!).Name);
    }

    [Fact]
    public void Clicking_a_drawn_shape_selects_it_and_dragging_moves_it()
    {
        var (window, canvas, panelId, bounds) = Open();
        var editor = window.Editor;
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(bounds.Left + 20, bounds.Top + 20), new Point2D(bounds.Left + 60, bounds.Top + 50));
        editor.CommitDrawShape();
        editor.SetFillColorCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Green"));
        editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        var start = At(window, canvas, bounds.Left + 40, bounds.Top + 35);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 30, start.Y + 20));
        window.MouseUp(new Point(start.X + 30, start.Y + 20), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.IsShapeContext);
        Assert.True(PanelElements.Bounds(editor.Working.Panels[panelId].Elements[0]).Left > bounds.Left + 20 + 1);
        Assert.Equal(editor.Working, editor.Committed);
    }

    [Fact]
    public void Scenery_covering_a_panel_still_leaves_its_edge_draggable()
    {
        var (window, canvas, panelId, bounds) = Open();
        var editor = window.Editor;
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(bounds.Left - 5, bounds.Top - 5), new Point2D(bounds.Right + 5, bounds.Bottom + 5));
        editor.CommitDrawShape();
        editor.SetFillColorCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Light Blue"));
        editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        var edge = At(window, canvas, bounds.Right - 0.3, bounds.MidY);
        window.MouseDown(edge, MouseButton.Left);
        window.MouseMove(new Point(edge.X - 40, edge.Y));
        window.MouseUp(new Point(edge.X - 40, edge.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.PanelBounds(panelId).Right < bounds.Right - 5, "the panel's edge should have been dragged in");

        var middle = At(window, canvas, bounds.Left + 30, bounds.MidY);
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsShapeContext, "a click inside picks the scenery");
    }

    [Fact]
    public void The_text_tool_places_text_where_clicked_and_typing_letters_it()
    {
        var (window, canvas, panelId, bounds) = Open();
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        canvas.Focus();
        Press(window, Key.T, PhysicalKey.T, "t");
        Assert.Equal(PageEditorTool.Text, window.Editor.Tool);

        var point = At(window, canvas, bounds.Left + 20, bounds.Top + 30);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible, "the inline text editor should open over the new text");
        Assert.True(view.TextEditor.IsFocused);
        var element = Assert.IsType<TextElement>(Assert.Single(window.Editor.Working.Panels[panelId].Elements));
        Assert.Equal(element.Id, view.Canvas.EditingText);
        var boxOnScreen = canvas.PageToControl(element.Bounds);
        Assert.True(boxOnScreen.Inflate(2).Contains(view.TextEditor.Bounds.Center), "the editor sits over the text box");

        window.KeyTextInput("Meanwhile...");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.TextEditor.IsVisible);
        Assert.Null(view.Canvas.EditingText);
        Assert.Equal("Meanwhile...", ((TextElement)window.Editor.Working.Panels[panelId].Elements[0]).Text);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool);
        Assert.True(window.Editor.IsTextContext);
    }

    [Fact]
    public void Bare_text_left_empty_is_removed_when_the_editor_closes()
    {
        var (window, canvas, panelId, bounds) = Open();
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        window.Editor.ApplyTextPresetCommand.Execute(TextStylePreset.Plain);
        window.Editor.Tool = PageEditorTool.Text;

        var point = At(window, canvas, bounds.MidX, bounds.MidY);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(window.Editor.Working.Panels[panelId].Elements);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.TextEditor.IsVisible);
        Assert.Empty(window.Editor.Working.Panels[panelId].Elements);
    }

    [Fact]
    public void Double_clicking_text_opens_it_for_editing()
    {
        var (window, canvas, panelId, bounds) = Open();
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var index = window.Editor.CreateText(panelId, new Point2D(bounds.Left + 20, bounds.Top + 30));
        window.Editor.SetElementText(panelId, index, "Later");
        window.Editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        var box = ((TextElement)window.Editor.Working.Panels[panelId].Elements[index]).Bounds;
        var point = At(window, canvas, box.MidX, box.MidY);
        window.DoubleClick(point);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible);
        Assert.Equal("Later", view.TextEditor.Text);
    }

    [Fact]
    public void Insert_caption_on_the_ribbon_opens_the_text_editor_in_the_pane()
    {
        var (window, _, panelId, _) = Open();
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "InsertTab");
        Dispatcher.UIThread.RunJobs();
        var button = ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "InsertCaptionButton");

        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible);
        var caption = Assert.IsType<TextElement>(Assert.Single(window.Editor.Working.Panels[panelId].Elements));
        Assert.NotNull(caption.Style.BoxFill);
        Assert.True(window.Editor.IsTextContext);
    }

    [Fact]
    public void Right_clicking_a_panel_offers_backgrounds_even_on_a_locked_layout()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.IsLayoutLocked = true;
        Dispatcher.UIThread.RunJobs();

        var items = canvas.ContextMenuItems(new Point2D(bounds.MidX, bounds.MidY));
        var background = items.OfType<MenuItem>().Single(i => i.Header as string == "Background");
        var night = ((IEnumerable<object>)background.ItemsSource!).OfType<MenuItem>().Single(i => i.Header as string == "Night");
        night.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.IsType<GradientBackground>(window.Editor.Working.Panels[panelId].Background);
        Assert.Contains(items.OfType<MenuItem>(), i => i.Header as string == "Add text here");
    }

    [Fact]
    public void The_panel_tab_background_picker_fills_the_selected_panel()
    {
        var (window, _, panelId, _) = Open();
        window.Editor.Select(panelId);
        Dispatcher.UIThread.RunJobs();
        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "PanelTab");
        Dispatcher.UIThread.RunJobs();
        var dropDown = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "PanelBackgroundButton");
        var picker = Assert.IsType<BackgroundPicker>(((Flyout)dropDown.Flyout!).Content);
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();

        var sky = picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackgroundDaysky");
        sky.Command!.Execute(sky.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<GradientBackground>(window.Editor.Working.Panels[panelId].Background);
        Assert.Equal("Day sky", window.Editor.BackgroundName);
    }

    [Fact]
    public void An_imported_picture_shows_the_Picture_tab_and_panels_offer_a_background_picture()
    {
        var (window, canvas, panelId, bounds) = Open();
        var editor = window.Editor;
        using var bitmap = new SkiaSharp.SKBitmap(20, 10);
        bitmap.Erase(SkiaSharp.SKColors.Purple);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        Assert.True(editor.ImportPicture(new PictureImportRequest(panelId, AsBackground: false), "p.png", Stanley.ProjectModel.Characters.ArtFile.Png(data.ToArray())));
        Dispatcher.UIThread.RunJobs();
        var tab = Ribbon(window).TabControl.Items.OfType<TabItem>().Single(t => t.Name == "PictureTab");
        Assert.True(tab.IsVisible);
        Ribbon(window).TabControl.SelectedItem = tab;
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "picture-tab");
        Assert.False(Ribbon(window).TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ShapeTab").IsVisible);

        editor.ClearSelection();
        PictureImportRequest? asked = null;
        editor.PictureImportRequested += r => asked = r;
        var corner = new Point2D(bounds.Left + 3, bounds.Bottom - 3);
        var background = canvas.ContextMenuItems(corner).OfType<MenuItem>().Single(i => i.Header as string == "Background");
        var picture = ((IEnumerable<object>)background.ItemsSource!).OfType<MenuItem>().Single(i => i.Header as string == "Picture…");
        picture.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(new PictureImportRequest(panelId, AsBackground: true), asked);
    }

    private static ShapeElement DrawRectangle(MainWindow window, PanelId panelId, Rect2D box)
    {
        var editor = window.Editor;
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom));
        return (ShapeElement)editor.Working.Panels[panelId].Elements[editor.CommitDrawShape()];
    }

    private static ColorMenuButton ColorButton(PageEditorRibbon ribbon, string tab, string name)
    {
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == tab);
        Dispatcher.UIThread.RunJobs();
        return ribbon.GetVisualDescendants().OfType<ColorMenuButton>().Single(b => b.Name == name);
    }

    /// <summary>Word's Shape Fill: the arrow opens Theme/Standard colours; a swatch fills the shape and closes the menu; the button's face then applies that colour again.</summary>
    [Fact]
    public void Shape_Fill_picks_from_the_theme_palette_and_its_face_repeats_the_last_colour()
    {
        var (window, _, panelId, bounds) = Open();
        var editor = window.Editor;
        DrawRectangle(window, panelId, new Rect2D(bounds.Left + 10, bounds.Top + 10, 30, 20));
        var ribbon = Ribbon(window);
        var fill = ColorButton(ribbon, "ShapeTab", "ShapeFillButton");

        fill.Menu.ShowAt(fill.Button);
        Dispatcher.UIThread.RunJobs();
        var palette = (Control)((MenuItem)fill.Menu.Items[0]!).Header!;
        Assert.Contains(palette.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Theme Colors");
        Assert.Contains(palette.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Standard Colors");
        var gold = palette.GetLogicalDescendants().OfType<Button>().Single(b => (b.Tag as PaletteColor)?.Name == "Gold, Accent 4, Lighter 40%");
        gold.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(fill.Menu.IsOpen);
        Assert.Equal(((PaletteColor)gold.Tag!).Color, editor.SelectedShape!.Style.Fill);
        Assert.Same(gold.Tag, fill.LastColor);

        DrawRectangle(window, panelId, new Rect2D(bounds.Left + 50, bounds.Top + 10, 30, 20));
        editor.SetFillColorCommand.Execute(DrawingPalette.None);
        fill.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(SplitButton.ClickEvent));
        Assert.Equal(((PaletteColor)gold.Tag!).Color, editor.SelectedShape!.Style.Fill);
    }

    /// <summary>Word's Shape Outline: No Outline, Weight ▸ and Dashes ▸ - the current weight and dash checked.</summary>
    [Fact]
    public void Shape_Outline_offers_no_outline_weight_and_dashes()
    {
        var (window, _, panelId, bounds) = Open();
        var editor = window.Editor;
        DrawRectangle(window, panelId, new Rect2D(bounds.Left + 10, bounds.Top + 10, 30, 20));
        var outline = ColorButton(Ribbon(window), "ShapeTab", "ShapeOutlineButton");
        outline.Menu.ShowAt(outline.Button);
        Dispatcher.UIThread.RunJobs();
        var items = outline.Menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["No Outline", "More Outline Colors…", "Weight", "Dashes"], items.Skip(1).Select(i => i.Header as string));

        var weight = items.Single(i => i.Header as string == "Weight");
        weight.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var weights = weight.Items.OfType<MenuItem>().ToList();
        Assert.True(weights.Single(w => ((ShapeWeightChoice)w.CommandParameter!).Mm == 0.7).IsChecked); // the default pen
        weights.Single(w => ((ShapeWeightChoice)w.CommandParameter!).Mm == 2).Command!.Execute(weights.Single(w => ((ShapeWeightChoice)w.CommandParameter!).Mm == 2).CommandParameter);
        Assert.Equal(2, editor.SelectedShape!.Style.StrokeWidthMm);

        var dashes = items.Single(i => i.Header as string == "Dashes");
        dashes.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var dash = dashes.Items.OfType<MenuItem>().Single(d => (LineDash)d.CommandParameter! == LineDash.LongDash);
        dash.Command!.Execute(dash.CommandParameter);
        Assert.Equal(LineDash.LongDash, editor.SelectedShape!.Style.Dash);

        var none = items.Single(i => i.Header as string == "No Outline");
        none.Command!.Execute(none.CommandParameter);
        Assert.Null(editor.SelectedShape!.Style.Stroke);
    }

    /// <summary>#46: the Weight ▸ and Dashes ▸ samples were drawn black, and all but vanished on a dark theme's menu.</summary>
    [Fact]
    public void Shape_Outlines_weight_and_dash_samples_are_drawn_in_the_menus_text_colour_in_a_dark_theme()
    {
        var (window, _, _, _) = Open();
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        try
        {
            Dispatcher.UIThread.RunJobs();
            var outline = ColorButton(Ribbon(window), "HomeTab", "HomeShapeOutlineButton");
            outline.Menu.ShowAt(outline.Button);
            Dispatcher.UIThread.RunJobs();
            var items = outline.Menu.Items.OfType<MenuItem>().ToList();
            foreach (var name in new[] { "Weight", "Dashes" })
            {
                var submenu = items.Single(i => i.Header as string == name);
                submenu.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                foreach (var item in submenu.Items.OfType<MenuItem>())
                {
                    var sample = ((Control)item.Header!).GetVisualDescendants().Prepend((Visual)item.Header!).OfType<Avalonia.Controls.Shapes.Shape>().Single();
                    var ink = (sample is Avalonia.Controls.Shapes.Line ? sample.Stroke : sample.Fill) as Avalonia.Media.ISolidColorBrush;
                    Assert.Equal((item.Foreground as Avalonia.Media.ISolidColorBrush)?.Color, ink?.Color);
                    Assert.True(ink!.Color.R > 128, $"{name}: a light sample on the dark menu, not {ink.Color}");
                }
                submenu.IsSubMenuOpen = false;
            }
            outline.Menu.Hide();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The Text tab has Word's Shape Fill / Shape Outline for the box and Text Fill / Text Outline for the letters.</summary>
    [Fact]
    public void The_Text_tab_sets_box_and_letters_separately()
    {
        var (window, _, panelId, bounds) = Open();
        var editor = window.Editor;
        var index = editor.CreateText(panelId, new Point2D(bounds.MidX, bounds.MidY), style: TextStylePresets.Style(TextStylePreset.Plain));
        editor.SetElementText(panelId, index, "BOOM");
        var ribbon = Ribbon(window);
        TextStyle Style() => ((TextElement)editor.Working.Panels[panelId].Elements[index]).Style;

        var textFill = ColorButton(ribbon, "TextTab", "TextFillButton");
        textFill.Menu.ShowAt(textFill.Button);
        Dispatcher.UIThread.RunJobs();
        var noFill = textFill.Menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "No Fill");
        noFill.Command!.Execute(noFill.CommandParameter);
        Assert.Null(Style().Color);
        textFill.Menu.Hide();

        var boxFill = ribbon.GetVisualDescendants().OfType<ColorMenuButton>().Single(b => b.Name == "TextBoxFillButton");
        boxFill.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(SplitButton.ClickEvent)); // the face: its default colour
        Assert.Equal(ColorValue.FromHex("#4472c4"), Style().BoxFill);
        Assert.Null(Style().BoxStroke); // the border is Shape Outline's business

        var textOutline = ribbon.GetVisualDescendants().OfType<ColorMenuButton>().Single(b => b.Name == "TextOutlineButton");
        textOutline.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(SplitButton.ClickEvent));
        Assert.Equal(ColorValue.FromHex("#000000"), Style().Outline);
    }

    /// <summary>Right-clicking a shape offers the same colour menus as the ribbon.</summary>
    [Fact]
    public void The_right_click_menu_has_the_same_fill_and_outline_menus()
    {
        var (window, canvas, panelId, bounds) = Open();
        var shape = DrawRectangle(window, panelId, new Rect2D(bounds.Left + 10, bounds.Top + 10, 30, 20));
        window.Editor.SetFillColorCommand.Execute(DrawingPalette.StandardColors[0]);
        var box = PanelElements.Bounds(shape);

        var items = canvas.ContextMenuItems(new Point2D(box.MidX, box.MidY)).OfType<MenuItem>().ToList();
        var fill = items.Single(i => i.Header as string == "Fill");
        var fillItems = ((IEnumerable<object>)fill.ItemsSource!).ToList();
        Assert.IsType<MenuItem>(fillItems[0]);
        Assert.Contains(fillItems.OfType<MenuItem>(), i => i.Header as string == "No Fill");
        var outline = items.Single(i => i.Header as string == "Outline");
        Assert.Contains(((IEnumerable<object>)outline.ItemsSource!).OfType<MenuItem>(), i => i.Header as string == "Dashes");

        var palette = (Control)((MenuItem)fillItems[0]).Header!;
        var current = palette.GetLogicalDescendants().OfType<Border>().Single(b => b.Classes.Contains("current"));
        Assert.Equal(DrawingPalette.StandardColors[0], current.Tag);
    }

    /// <summary>
    /// A whole scene - a sky, a hill behind a character, a bush in front, a caption and a
    /// sound effect - with each contextual tab open in turn. Set STANLEY_UI_SNAPSHOTS to a
    /// folder to see the frames.
    /// </summary>
    [Fact]
    public void A_scene_with_scenery_captions_and_effects_renders_with_each_tab()
    {
        var (window, _, panelId, bounds) = Open();
        var editor = window.Editor;
        editor.SetPanelBackground(panelId, DrawingPalette.Backgrounds.Single(b => b.Name == "Day sky").Background);

        editor.Tool = PageEditorTool.Draw;
        editor.SetFillColorCommand.Execute(DrawingPalette.StandardColors.Single(c => c.Name == "Green"));
        editor.BeginDrawShape(panelId);
        var hill = new List<Point2D> { new(bounds.Left - 5, bounds.Bottom + 5) };
        for (var x = bounds.Left - 5; x <= bounds.Right + 5; x += 2)
            hill.Add(new Point2D(x, bounds.Bottom - 60 + 15 * Math.Sin((x - bounds.Left) / 25)));
        hill.Add(new Point2D(bounds.Right + 5, bounds.Bottom + 5));
        hill.Add(new Point2D(bounds.Left - 5, bounds.Bottom + 5));
        editor.UpdateDrawFreehand(hill, 0.2, 3);
        editor.CommitDrawShape();

        var character = window.ViewModel.Characters!.CreateCharacter();
        editor.InsertCharacter(character.Id, panelId, new Point2D(bounds.MidX, bounds.Bottom - 30));

        editor.Tool = PageEditorTool.Ellipse;
        editor.SetFillColorCommand.Execute(DrawingPalette.ThemeShades[4].Single(c => c.Name == "Green, Accent 6, Darker 50%"));
        editor.BeginDrawShape(panelId);
        editor.UpdateDrawShape(new Point2D(bounds.MidX - 40, bounds.Bottom - 45), new Point2D(bounds.MidX + 5, bounds.Bottom + 10));
        editor.CommitDrawShape();
        editor.IsElementInFront = true;

        var caption = editor.CreateText(panelId, default, new Rect2D(bounds.Left + 1.5, bounds.Top + 1.5, 60, 5), TextStylePresets.Style(TextStylePreset.Caption));
        editor.SetElementText(panelId, caption, "Meanwhile, on the hill...");
        var effect = editor.CreateText(panelId, new Point2D(bounds.MidX + 30, bounds.Top + 60), style: TextStylePresets.Style(TextStylePreset.SoundEffect));
        editor.SetElementText(panelId, effect, "WHOOSH!");
        Dispatcher.UIThread.RunJobs();

        var ribbon = Ribbon(window);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "TextTab");
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "scene-text-tab");

        editor.SelectElement(panelId, 1);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ShapeTab");
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "scene-shape-tab");

        editor.Select(panelId);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "InsertTab");
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "scene-insert-tab");

        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "HomeTab");
        var fill = ribbon.GetVisualDescendants().OfType<ColorMenuButton>().Single(b => b.Name == "HomeShapeFillButton");
        fill.Menu.ShowAt(fill.Button);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "scene-home-fill");

        Assert.Equal(4, editor.Working.Panels[panelId].Elements.Count);
        Assert.Single(editor.Working.Panels[panelId].CharacterInstances);
    }
}
