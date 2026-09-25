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

        window.DoubleClick(chest);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);
    }

    [Fact]
    public void ClickingTheAlreadyCurrentPage_WhileACharacterEditorIsShowing_BringsThePageBack()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        characters.OpenCharacter(created.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);

        var navigatorPane = window.GetVisualDescendants().OfType<PageNavigatorView>().Single();
        var container = (Control)navigatorPane.List.ContainerFromIndex(0)!;
        var point = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
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
    public void Double_clicking_a_character_in_the_pane_puts_it_on_the_page_and_the_page_stays_showing()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        var page = window.Editor;
        var (_, itemCenter) = ShowPane(window, characters);

        window.DoubleClick(itemCenter);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(page, window.Workspace.ActiveEditor);
        Assert.Empty(window.GetVisualDescendants().OfType<CharacterEditorView>()); // the tab the first click opened is closed again
        var placed = Assert.Single(page.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
        Assert.Equal(characters.Items[0].Id, placed.CharacterId);
        Assert.Null(characters.Current);
        Assert.True(Single<PageEditorRibbon>(window.RibbonBarControl).FindControl<TabItem>("CharacterTab")!.IsVisible);
        Assert.True(Single<PageCanvasControl>(window).IsFocused); // Delete, arrows and F/S reach the new character
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
    public void Dragging_a_selected_characters_elbow_or_knee_dot_moves_the_joint_to_the_pointer()
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
        var elbow = page.BendHandles(instance).Single(h => h.Limb == Editing.Limb.LeftArm).Point;
        Point ToWindow(ProjectModel.Geometry.Point2D p) => canvas.TranslatePoint(canvas.PageToControl(p), window)!.Value;
        // Well out to the side of the hanging elbow: the upper arm swings out towards it.
        var forearm = Math.Sqrt((elbow.X - hand.X) * (elbow.X - hand.X) + (elbow.Y - hand.Y) * (elbow.Y - hand.Y));
        var target = new ProjectModel.Geometry.Point2D(elbow.X + 3 * forearm, elbow.Y);

        window.MouseDown(ToWindow(elbow), MouseButton.Left);
        window.MouseMove(ToWindow(target));
        window.MouseUp(ToWindow(target), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var posed = page.Working.Panels[panelId].CharacterInstances[0];
        var movedElbow = page.BendHandles(posed).Single(h => h.Limb == Editing.Limb.LeftArm).Point;
        var movedHand = page.LimbHandles(posed).Single(h => h.Limb == Editing.Limb.LeftArm).Point;
        static double Dist(ProjectModel.Geometry.Point2D a, ProjectModel.Geometry.Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        Assert.True(Dist(movedElbow, target) < Dist(elbow, target) - forearm / 2, "the elbow follows the pointer");
        Assert.NotEqual(hand, movedHand);
        Assert.NotEmpty(posed.Pose.BoneRotations);
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

        // Mix your own: the same flyout has a row per face slot - here, a smile under the surprised eyes.
        var smile = content.GetLogicalDescendants().OfType<Button>().Single(b => b.DataContext is ExpressionVariantChoice { Slot: "mouth", Variant: "smile" });
        Assert.True(smile.GetVisualDescendants().OfType<CharacterFigure>().Single().Closeup);
        smile.Command!.Execute(smile.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "page-expression-mixer");

        var face = page.Working.Panels[panelId].CharacterInstances[0].Pose.Expression;
        Assert.Equal(("wide", "smile"), (face["eyes"], face["mouth"]));
        Assert.Equal("Custom", page.SelectedExpressionName);
    }

    [Fact]
    public void A_face_saved_from_the_expression_gallery_is_one_click_there_afterwards()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId);
        page.SetExpressionVariant(panelId, 0, "eyes", "happy");
        page.SetExpressionVariant(panelId, 0, "mouth", "open");
        var ribbon = Single<PageEditorRibbon>(window.RibbonBarControl);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "CharacterTab");
        Dispatcher.UIThread.RunJobs();
        var gallery = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "ExpressionGallery");
        gallery.Flyout!.ShowAt(gallery);
        Dispatcher.UIThread.RunJobs();
        var content = (Control)((Flyout)gallery.Flyout!).Content!;

        var name = content.GetLogicalDescendants().OfType<TextBox>().Single(b => b.Name == "FaceNameBox");
        name.Text = "Cheeky";
        var save = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveFaceButton");
        Assert.True(save.Command!.CanExecute(null));
        save.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "page-saved-face");

        Assert.Equal("Cheeky", page.SelectedExpressionName);
        Assert.Equal("", name.Text);
        Assert.Contains(content.GetLogicalDescendants().OfType<Button>(), b => b.Name == "DrawNewExpressionButton" && b.IsEffectivelyVisible);

        page.ApplyExpression(panelId, 0, Editing.ExpressionPresets.Get(Editing.ExpressionPreset.Neutral));
        Dispatcher.UIThread.RunJobs();
        var cheeky = content.GetLogicalDescendants().OfType<Button>().Single(b => b.DataContext is SavedFaceChoice { Name: "Cheeky" });
        cheeky.Command!.Execute(cheeky.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        var face = page.Working.Panels[panelId].CharacterInstances[0].Pose.Expression;
        Assert.Equal(("happy", "open"), (face["eyes"], face["mouth"]));
    }

    [Fact]
    public void The_character_editors_Front_and_Side_switch_is_in_its_status_bar_whichever_tab_is_open()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        characters.OpenCharacter(created.Id);
        Dispatcher.UIThread.RunJobs();
        var ribbon = Single<CharacterEditorRibbon>(window.RibbonBarControl);
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("LookTab");
        Dispatcher.UIThread.RunJobs();

        var view = Single<CharacterEditorView>(window);
        var side = view.FindControl<RadioButton>("StatusSideButton")!;
        Assert.True(side.IsEffectivelyVisible);
        var point = side.TranslatePoint(new Point(side.Bounds.Width / 2, side.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        LookTabTests.Snapshot(window, "character-status-view-switch");
        var editor = characters.Items.Single().Editor;
        Assert.Equal(ProjectModel.Geometry.ViewAngle.Profile, editor.PreviewAngle);
        Assert.Equal(ProjectModel.Geometry.ViewAngle.Profile, view.Figure.Angle);
        Assert.False(view.FindControl<RadioButton>("StatusFrontButton")!.IsChecked);
        Assert.True(ribbon.FindControl<RadioButton>("SidePreviewButton")!.IsChecked); // the Body tab's switch follows
    }

    [Fact]
    public void A_character_with_named_looks_gets_a_Look_dropdown_and_a_this_panel_only_menu()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var editor = characters.Items.Single(i => i.Id == created.Id).Editor;
        editor.NewLookCommand.Execute(null);
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Headwear).Choices.Single(c => c.Label == "Beanie"));
        var page = window.Editor;
        var panelId = page.Working.PanelOrder[0];
        page.InsertCharacter(created.Id, panelId);
        var ribbon = Single<PageEditorRibbon>(window.RibbonBarControl);
        ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "CharacterTab");
        Dispatcher.UIThread.RunJobs();

        var looks = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "LookGallery");
        Assert.True(looks.IsEffectivelyVisible);
        looks.Flyout!.ShowAt(looks);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "page-look-gallery");
        var content = (Control)((Flyout)looks.Flyout!).Content!;
        var named = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is LookChoice { Look: { } id } && id == editor.CurrentLook);
        named.Command!.Execute(named.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(editor.CurrentLook, page.Working.Panels[panelId].CharacterInstances[0].RevisionOverride);
        looks.Flyout!.Hide();

        // Right-click › This panel only › Take off › Beanie.
        var canvas = Single<PageCanvasControl>(window);
        var bounds = page.CharacterBounds(page.Working.Panels[panelId].CharacterInstances[0]);
        var items = canvas.ContextMenuItems(new ProjectModel.Geometry.Point2D(bounds.MidX, bounds.Top + bounds.Height * 0.35));
        var panelOnly = items.OfType<MenuItem>().Single(m => (string?)m.Header == "This panel only");
        var takeOff = panelOnly.ItemsSource!.OfType<MenuItem>().Single(m => (string?)m.Header == "Take off");
        var beanie = takeOff.ItemsSource!.OfType<MenuItem>().Single(m => (string?)m.Header == "Beanie");
        beanie.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(page.Working.Panels[panelId].CharacterInstances[0].Overrides?.ActiveStickerOverrides);
        Assert.Contains(items.OfType<MenuItem>(), m => (string?)m.Header == "Look");
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

    [Fact]
    public void A_compared_character_can_be_clicked_to_switch_to_it()
    {
        var (window, characters) = Open();
        var first = characters.CreateCharacter();
        var second = characters.CreateCharacter();
        characters.OpenCharacter(first.Id);
        Dispatcher.UIThread.RunJobs();

        var editor = characters.Items.Single(i => i.Id == first.Id).Editor;
        Assert.True(editor.ShowLineUp); // line-up is shown by default
        var figure = Single<CharacterEditorView>(window).Figure;
        Assert.NotNull(figure.LineUp);
        Assert.Contains(figure.LineUp, c => c.Id == second.Id);

        // LineUpCharacterAt returns the clicked character for a point in its bounds
        var bounds = figure.Bounds;
        var clickPoint = new Point(bounds.Right - 30, bounds.Bottom - 40);
        var lineUpChar = figure.LineUpCharacterAt(clickPoint);
        Assert.NotNull(lineUpChar);
        Assert.Equal(second.Id, lineUpChar.Id);
    }
}
