using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
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
        editor.SetFillColorCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "Green"));
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
        editor.SetFillColorCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "Sky"));
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
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
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
        editor.SetFillColorCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "Green"));
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
        editor.SetFillColorCommand.Execute(DrawingPalette.Colors.Single(c => c.Name == "Dark green"));
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
        var fill = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "HomeFillButton");
        fill.Flyout!.ShowAt(fill);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "scene-home-fill");

        Assert.Equal(4, editor.Working.Panels[panelId].Elements.Count);
        Assert.Single(editor.Working.Panels[panelId].CharacterInstances);
    }
}
