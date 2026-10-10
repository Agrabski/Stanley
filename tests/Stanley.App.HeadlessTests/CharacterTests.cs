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
        Assert.Same(item, pane.List.SelectedItem); // the highlight follows a character that was just opened
        Assert.Single(window.GetVisualDescendants().OfType<CharacterEditorView>());
        Assert.Single(window.RibbonBarControl.GetVisualDescendants().OfType<CharacterEditorRibbon>());
        Assert.Empty(window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>());
        Assert.Single(pane.GetVisualDescendants().OfType<CharacterFigure>());

        item.Editor.BackToPageCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        Assert.Null(pane.List.SelectedItem); // nothing is open, and nothing was picked since
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

    /// <summary>
    /// #30: a character standing on the bottom of a panel, with a panel below, couldn't be
    /// clicked by her feet - the gutter's grab band reaching into the panel won, and the
    /// press went to the gutter instead. Things in a panel now win over it there, as over
    /// the panel's own edge band; the gutter between the panels still drags.
    /// </summary>
    [Fact]
    public void A_character_standing_on_a_panels_bottom_edge_is_selected_by_a_click_on_her_feet()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var page = window.Editor;
        page.SplitPanel(page.Working.PanelOrder[0], Stanley.Editing.BoundaryOrientation.Horizontal, 0.5);
        var (top, below) = (page.Working.PanelOrder[0], page.Working.PanelOrder[1]);
        var bounds = page.PanelBounds(top);
        Assert.True(page.PanelBounds(below).Top > bounds.Bottom, "a gutter between the two panels");
        page.InsertCharacter(created.Id, top, new ProjectModel.Geometry.Point2D(bounds.MidX, bounds.Bottom));
        page.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        var canvas = Single<PageCanvasControl>(window);
        var instance = page.Working.Panels[top].CharacterInstances[0];
        // On her foot, two screen pixels above the floor - well inside the gutter's grab band.
        var ankle = Stanley.Editing.CharacterPosing.EndPoint(page.CharacterSnapshot[instance.CharacterId], instance, Stanley.Editing.Limb.LeftLeg);
        var foot = new ProjectModel.Geometry.Point2D(ankle.X, bounds.Bottom - 2 / canvas.Zoom);
        using (var silhouette = Stanley.Rendering.CharacterRenderers.Default.BuildSilhouette(page.CharacterSnapshot[instance.CharacterId], instance.Placement, instance.Pose.ViewAngle, instance.Pose))
            Assert.True(silhouette.Contains((float)foot.X, (float)foot.Y), "the point is on her foot");
        var at = canvas.TranslatePoint(canvas.PageToControl(foot), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(top, page.SelectedPanelId);
        Assert.Equal(0, page.SelectedCharacterIndex);
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
        return (pane, RowCenter(window, pane, 0));
    }

    private static Point RowCenter(MainWindow window, CharacterLibraryView pane, int index) =>
        CenterOf(window, (Control)pane.List.ContainerFromIndex(index)!);

    private static Point CenterOf(MainWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>The pencil on a character's row.</summary>
    private static Button EditButton(CharacterLibraryView pane, CharacterItem item) =>
        pane.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("editBody") && ReferenceEquals(b.DataContext, item));

    [Fact]
    public void Pressing_a_character_in_the_pane_to_drag_it_leaves_the_page_showing_and_a_click_only_picks_it()
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

        // A plain click (no drag) picks it - and leaves the page where it is.
        window.MouseDown(itemCenter, MouseButton.Left);
        window.MouseUp(itemCenter, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
    }

    [Fact]
    public void Clicking_a_character_in_the_pane_picks_it_without_opening_its_editor()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        characters.CreateCharacter();
        var (pane, _) = ShowPane(window, characters);
        var opened = new List<CharacterItem>();
        characters.CharacterShown += opened.Add;

        var first = RowCenter(window, pane, 0);
        window.MouseDown(first, MouseButton.Left);
        window.MouseUp(first, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(characters.Items[0], pane.List.SelectedItem);
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        Assert.Null(characters.Current);
        Assert.Empty(window.GetVisualDescendants().OfType<CharacterEditorView>());

        // Another row: the highlight moves with the click, and still nothing opens.
        var second = RowCenter(window, pane, 1);
        window.MouseDown(second, MouseButton.Left);
        window.MouseUp(second, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(characters.Items[1], pane.List.SelectedItem);
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        Assert.Null(characters.Current);
        Assert.Empty(opened);
    }

    [Fact]
    public void Clicking_another_row_while_a_character_is_open_picks_it_and_leaves_the_open_one_showing()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var open = characters.Items.Single(i => i.Id == created.Id);
        characters.CreateCharacter();
        characters.Show(open);
        var (pane, _) = ShowPane(window, characters);
        Assert.Same(open, pane.List.SelectedItem);

        var other = RowCenter(window, pane, 1);
        window.MouseDown(other, MouseButton.Left);
        window.MouseUp(other, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(characters.Items[1], pane.List.SelectedItem);
        Assert.Same(open, characters.Current);
        Assert.Same(open.Editor, window.Workspace.ActiveEditor);
    }

    [Fact]
    public void The_pencil_on_a_character_row_shows_on_the_picked_row_and_opens_the_character_when_clicked()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        characters.CreateCharacter();
        var (pane, _) = ShowPane(window, characters);
        var item = characters.Items[0];

        // Not on a row nobody has picked or pointed at: invisible and not clickable.
        var pencil = EditButton(pane, item);
        Assert.Equal(0, pencil.Opacity);
        Assert.False(pencil.IsHitTestVisible);

        var row = RowCenter(window, pane, 0);
        window.MouseDown(row, MouseButton.Left);
        window.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, pencil.Opacity);
        Assert.True(pencil.IsHitTestVisible);
        Assert.Equal(0, EditButton(pane, characters.Items[1]).Opacity);
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);
        LookTabTests.Snapshot(window, "characters-pane-edit-pencil");

        var point = CenterOf(window, pencil);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(item.Editor, window.Workspace.ActiveEditor);
        Assert.Same(item, characters.Current);
        Assert.Same(item, pane.List.SelectedItem);
        Assert.Empty(window.Editor.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
    }

    [Fact]
    public void Double_clicking_the_pencil_opens_the_character_and_does_not_put_it_on_the_page()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        var page = window.Editor;
        var (pane, row) = ShowPane(window, characters);
        var item = characters.Items[0];

        window.MouseDown(row, MouseButton.Left);
        window.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.DoubleClick(CenterOf(window, EditButton(pane, item)));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(item.Editor, window.Workspace.ActiveEditor);
        Assert.Empty(page.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
    }

    [Fact]
    public void Pressing_Enter_on_the_picked_character_in_the_pane_opens_its_editor()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        var (pane, row) = ShowPane(window, characters);

        // Nothing picked yet: Enter has nothing to open.
        pane.List.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);

        window.MouseDown(row, MouseButton.Left);
        window.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);

        pane.List.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
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
        var opened = new List<CharacterItem>();
        characters.CharacterShown += opened.Add;

        window.DoubleClick(itemCenter);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(page, window.Workspace.ActiveEditor);
        Assert.Empty(window.GetVisualDescendants().OfType<CharacterEditorView>()); // the first click only picked it: no tab opened, none to close
        Assert.Empty(opened);
        var placed = Assert.Single(page.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
        Assert.Equal(characters.Items[0].Id, placed.CharacterId);
        Assert.Null(characters.Current);
        Assert.True(Single<PageEditorRibbon>(window.RibbonBarControl).FindControl<TabItem>("CharacterTab")!.IsVisible);
        Assert.True(Single<PageCanvasControl>(window).IsFocused); // Delete, arrows and F/S reach the new character
    }

    [Fact]
    public void Clicking_or_dragging_inside_a_renaming_boxs_text_only_edits_it_and_never_opens_or_drags_the_character()
    {
        var (window, characters) = Open();
        characters.CreateCharacter();
        var page = window.Editor;
        var (pane, _) = ShowPane(window, characters);
        var item = characters.Items[0];

        item.IsEditingName = true;
        Dispatcher.UIThread.RunJobs();
        var nameBox = pane.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext == item);
        var boxPoint = nameBox.TranslatePoint(new Point(nameBox.Bounds.Width / 2, nameBox.Bounds.Height / 2), window)!.Value;

        // A click to place the caret must stay inside the text box - it must not also be read as
        // the list's own click (the first half of a double-click that would place the character).
        window.MouseDown(boxPoint, MouseButton.Left);
        window.MouseUp(boxPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page, window.Workspace.ActiveEditor);
        Assert.True(item.IsEditingName);

        // Dragging across the box to select text must not be hijacked into the list's drag, which
        // would otherwise place the character on the page.
        window.MouseDown(boxPoint, MouseButton.Left);
        window.MouseMove(new Point(boxPoint.X + 30, boxPoint.Y));
        window.MouseUp(new Point(boxPoint.X + 30, boxPoint.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page, window.Workspace.ActiveEditor);
        Assert.Empty(page.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
        Assert.True(item.IsEditingName);
    }

    [Fact]
    public void Renaming_a_character_commits_on_Enter_and_cancels_on_Escape_even_as_the_only_character()
    {
        var (window, characters) = Open();
        var created = characters.CreateCharacter();
        var (pane, _) = ShowPane(window, characters);
        var item = characters.Items[0];

        item.IsEditingName = true;
        Dispatcher.UIThread.RunJobs();
        var nameBox = pane.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext == item);

        // Enter used to move focus to the next character to close the box - with only one
        // character (no "next"), that silently did nothing and left it stuck open.
        nameBox.Text = "Pip";
        nameBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.False(item.IsEditingName);
        Assert.Equal("Pip", item.Name);

        // Escape backs out without applying whatever was typed.
        item.IsEditingName = true;
        Dispatcher.UIThread.RunJobs();
        nameBox.Text = "Not this";
        nameBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(item.IsEditingName);
        Assert.Equal("Pip", item.Name);
        Assert.NotEqual(created.Name, item.Name); // sanity: the rename above did take effect
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

        // Somewhere on the faded figure beside this one - never on the character being edited.
        var point = Enumerable.Range(0, (int)figure.Bounds.Height / 4).SelectMany(y => Enumerable.Range(0, (int)figure.Bounds.Width / 4).Select(x => new Point(x * 4, y * 4)))
            .First(p => figure.LineUpCharacterAt(p) is not null);
        Assert.Equal(second.Id, figure.LineUpCharacterAt(point)!.Id);
        Assert.DoesNotContain(Enumerable.Range(0, 20).Select(i => new Point(figure.Bounds.Width / 2, figure.Bounds.Height * (0.3 + i * 0.03))),
            p => figure.LineUpCharacterAt(p)?.Id == first.Id);

        // A click there switches the editor to it.
        var inWindow = figure.TranslatePoint(point, window)!.Value;
        window.MouseDown(inWindow, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(inWindow, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(second.Id, characters.Current?.Id);
        Assert.Same(characters.Items.Single(i => i.Id == second.Id).Editor, window.Workspace.ActiveEditor);
    }
}
