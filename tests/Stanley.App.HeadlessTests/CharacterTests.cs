using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.HeadlessTests;

/// <summary>The Characters pane, the character editor and placed characters on the page.</summary>
[Collection("Page Editor Tests")]
public class CharacterTests
{
    private static (MainWindow Window, CharacterLibraryViewModel Characters) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.ViewModel.Characters!);
    }

    private static T Single<T>(Visual root) => root.GetVisualDescendants().OfType<T>().Single();

    [Fact]
    public void The_Characters_pane_is_a_tab_beside_Pages_and_New_character_opens_its_editor_with_its_own_ribbon()
    {
        var (window, characters) = Open();
        window.Workspace.Factory.SetActiveDockable(characters);
        Dispatcher.UIThread.RunJobs();
        var pane = Single<CharacterLibraryView>(window);

        pane.FindControl<Button>("NewCharacterButton")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(characters.Items);
        Assert.Same(item.Editor, window.Workspace.ActiveEditor);
        Assert.Single(window.GetVisualDescendants().OfType<CharacterEditorView>());
        Assert.Single(window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>());
        Assert.Empty(window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>());
        Assert.Single(pane.GetVisualDescendants().OfType<CharacterFigure>());

        item.Editor.BackToPageCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        Assert.Single(window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>());
    }

    [Fact]
    public void Clicking_a_placed_character_selects_it_and_shows_the_Character_tab_and_double_clicking_opens_its_body()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId);
        page.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        var canvas = Single<PageCanvasControl>(window);
        var bounds = page.CharacterBounds(page.Working.Panels[panelId].CharacterInstances[0]);
        var chest = canvas.TranslatePoint(canvas.PageToControl(new ProjectModel.Geometry.Point2D(bounds.MidX, bounds.Top + bounds.Height * 0.35)), window)!.Value;

        window.MouseDown(chest, MouseButton.Left);
        window.MouseUp(chest, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, page.SelectedCharacterIndex);
        var ribbon = Single<PageEditorRibbon>(window.RibbonBarControl);
        Assert.True(ribbon.FindControl<TabItem>("CharacterTab")!.IsVisible);

        // S turns it side on; the ribbon's toggle follows.
        canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ProjectModel.Geometry.ViewAngle.Profile, page.Working.Panels[panelId].CharacterInstances[0].Pose.ViewAngle);
        Assert.True(ribbon.FindControl<RadioButton>("SideViewButton")!.IsChecked);
        page.ClearSelection();
        window.MouseDown(chest, MouseButton.Left);
        window.MouseUp(chest, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, page.SelectedCharacterIndex); // a side-on character is still clickable through its near arm

        window.MouseDown(chest, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(chest, MouseButton.Left);
        window.MouseDown(chest, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(chest, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);
    }

    private static (CharacterLibraryView Pane, Point ItemCenter) ShowPane(MainWindow window, CharacterLibraryViewModel characters)
    {
        window.Workspace.Factory.SetActiveDockable(characters);
        Dispatcher.UIThread.RunJobs();
        var pane = Single<CharacterLibraryView>(window);
        var item = (Control)pane.List.ContainerFromIndex(0)!;
        return (pane, item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value);
    }

    [Fact]
    public void Pressing_a_character_in_the_pane_to_drag_it_leaves_the_page_showing_and_a_click_opens_it()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        var (_, itemCenter) = ShowPane(window, characters);

        // Press and start moving: the start of a drag onto the page. The page must stay
        // on screen - there'd be nowhere to drop it otherwise.
        window.MouseDown(itemCenter, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        window.MouseMove(new Point(itemCenter.X + 40, itemCenter.Y));
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(new Point(itemCenter.X + 40, itemCenter.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);

        // A plain click (no drag) opens the character.
        window.MouseDown(itemCenter, MouseButton.Left);
        window.MouseUp(itemCenter, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(characters.Items[0].Editor, window.Workspace.ActiveEditor);
    }

    [Fact]
    public void Dropping_a_character_from_the_pane_onto_a_panel_places_it_standing_where_it_was_dropped()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        var canvas = Single<PageCanvasControl>(window);
        var bounds = page.PanelBounds(panelId);
        var dropPage = new ProjectModel.Geometry.Point2D(bounds.Left + bounds.Width * 0.3, bounds.Top + bounds.Height * 0.7);
        var drop = canvas.TranslatePoint(canvas.PageToControl(dropPage), window)!.Value;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(CharacterDrag.Format, created.Id.Value));
        window.DragDrop(drop, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        window.DragDrop(drop, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        window.DragDrop(drop, RawDragEventType.Drop, data, DragDropEffects.Copy);
        Dispatcher.UIThread.RunJobs();

        var placed = Assert.Single(page.Working.Panels[panelId].CharacterInstances);
        Assert.Equal(created.Id, placed.CharacterId);
        Assert.Equal(dropPage.X, placed.Placement.Ground.X, 1);
        Assert.Equal(dropPage.Y, placed.Placement.Ground.Y, 1);
        Assert.Equal(0, page.SelectedCharacterIndex);
    }

    [Fact]
    public void Dragging_a_selected_characters_hand_dot_poses_its_arm()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId); // selected
        Dispatcher.UIThread.RunJobs();
        var canvas = Single<PageCanvasControl>(window);
        var instance = page.Working.Panels[panelId].CharacterInstances[0];
        var hand = page.LimbHandles(instance).Single(h => h.Limb == Editing.Limb.LeftArm).Point;
        Point ToWindow(ProjectModel.Geometry.Point2D p) => canvas.TranslatePoint(canvas.PageToControl(p), window)!.Value;

        window.MouseDown(ToWindow(hand), MouseButton.Left);
        window.MouseMove(ToWindow(new ProjectModel.Geometry.Point2D(hand.X + 15, hand.Y - 40)));
        window.MouseUp(ToWindow(new ProjectModel.Geometry.Point2D(hand.X + 15, hand.Y - 40)), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var posed = page.Working.Panels[panelId].CharacterInstances[0];
        Assert.Equal(instance.Placement, posed.Placement); // posed, not moved
        Assert.NotEmpty(posed.Pose.BoneRotations);
        Assert.True(page.LimbHandles(posed).Single(h => h.Limb == Editing.Limb.LeftArm).Point.Y < hand.Y - 20);
        Assert.True(window.Workspace.History.CanUndo);
    }

    [Fact]
    public void The_pose_gallery_on_the_Character_tab_poses_the_selected_character()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId);
        var ribbon = Single<PageEditorRibbon>(window.RibbonBarControl);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "CharacterTab");
        Dispatcher.UIThread.RunJobs();

        var buttons = ribbon.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is PosePresetChoice).ToList();
        Assert.Equal(Editing.PosePresets.All.Count, buttons.Count);
        Assert.All(buttons, b => Assert.True(b.GetVisualDescendants().OfType<CharacterFigure>().Single().Pose is not null));

        var cheer = buttons.Single(b => ((PosePresetChoice)b.DataContext!).Preset.Preset == Editing.PosePreset.Cheer);
        cheer.Command!.Execute(cheer.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(page.SelectedCharacterIsPosed);
        var instance = page.Working.Panels[panelId].CharacterInstances[0];
        var head = page.TrunkHandles(instance).Single(h => h.Part == Editing.TrunkPart.Head).Point;
        Assert.All(page.LimbHandles(instance).Where(h => h.Limb is Editing.Limb.LeftArm or Editing.Limb.RightArm),
            h => Assert.True(h.Point.Y < head.Y + 5, "both hands up"));
    }

    [Fact]
    public void The_expression_gallery_on_the_Character_tab_changes_the_face()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId);
        var ribbon = Single<PageEditorRibbon>(window.RibbonBarControl);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "CharacterTab");
        Dispatcher.UIThread.RunJobs();

        var gallery = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "ExpressionGallery");
        gallery.Flyout!.ShowAt(gallery);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "page-expression-gallery");
        var content = (Control)((Flyout)gallery.Flyout!).Content!;
        var surprised = content.GetLogicalDescendants().OfType<Button>().Single(b => b.DataContext is ExpressionPresetChoice { Name: "Surprised" });
        Assert.True(surprised.GetVisualDescendants().OfType<CharacterFigure>().Single().Closeup);
        surprised.Command!.Execute(surprised.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Surprised", page.SelectedExpressionName);
        Assert.Equal("wide", page.Working.Panels[panelId].CharacterInstances[0].Pose.Expression["eyes"]);
    }

    [Fact]
    public void The_character_editor_ribbon_applies_a_body_type_and_renames()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        characters.OpenCharacter(created.Id);
        Dispatcher.UIThread.RunJobs();
        var ribbon = Single<CharacterEditorRibbon>(window.RibbonBarControl);

        var chibi = ribbon.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is BodyPresetChoice { Preset: BodyPreset.Chibi });
        chibi.Command!.Execute(chibi.CommandParameter);
        var nameBox = ribbon.FindControl<TextBox>("NameBox")!;
        nameBox.Text = "Pip";
        nameBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();

        var editor = characters.Items.Single().Editor;
        Assert.Equal(BodyPresets.Shape(BodyPreset.Chibi), editor.Committed.Body);
        Assert.Equal("Pip", editor.Committed.Name);
        Assert.Equal("Pip", characters.Items.Single().Name);
    }
}
