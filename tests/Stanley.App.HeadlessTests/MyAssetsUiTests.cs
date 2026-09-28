using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>My Assets in the window (docs/asset-packs.md §10 slice 3): keeping from the page and the Characters pane, the pane's gallery, File › My Assets.</summary>
[Collection("Page Editor Tests")]
public class MyAssetsUiTests
{
    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Two rectangles grouped and selected on the first panel; returns the group's middle.</summary>
    private static Point2D MakeGroup(PageEditorViewModel editor)
    {
        var panel = editor.Working.PanelOrder[0];
        editor.Tool = PageEditorTool.Rectangle;
        foreach (var (x, y) in new[] { (40.0, 40.0), (70.0, 60.0) })
        {
            editor.BeginDrawShape(panel);
            editor.UpdateDrawShape(new Point2D(x, y), new Point2D(x + 20, y + 15));
            editor.CommitDrawShape();
        }
        editor.Tool = PageEditorTool.Select;
        editor.SelectElement(panel, 0);
        editor.ToggleSelect(panel, elementIndex: 1);
        editor.GroupSelectionCommand.Execute(null);
        var box = PanelElements.Bounds(editor.SelectedElement!);
        return new Point2D(box.MidX, box.MidY);
    }

    [Fact]
    public void Right_clicking_a_group_keeps_it_in_My_Assets_and_it_shows_on_File_My_Assets_and_in_Insert()
    {
        var window = Open();
        var editor = window.Editor;
        var middle = MakeGroup(editor);
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();

        var keep = canvas.ContextMenuItems(middle).OfType<MenuItem>().Single(i => i.Header as string is "Keep in My Assets");
        keep.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var group = Assert.IsType<GroupElement>(editor.SelectedElement);
        Assert.NotNull(group.SourceId);
        var kept = canvas.ContextMenuItems(middle).OfType<MenuItem>().Single(i => i.Header as string is "Kept in My Assets");
        Assert.False(kept.IsEnabled);
        Assert.Contains(editor.MyAssetsObjectGroups, g => g.Id == group.SourceId);

        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        ribbon.TabControl.SelectedItem = ribbon.FindControl<TabItem>("InsertTab");
        Dispatcher.UIThread.RunJobs();
        var insert = ribbon.FindControl<DropDownButton>("InsertFromMyAssetsButton")!;
        Assert.True(insert.IsEffectivelyVisible);
        insert.Flyout!.ShowAt(insert);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "insert-from-my-assets");
        insert.Flyout.Hide();

        window.ViewModel.ShowBackstage(BackstagePage.MyAssets);
        Dispatcher.UIThread.RunJobs();
        var grid = window.BackstageControl.FindControl<ItemsControl>("MyAssetsGrid")!;
        Assert.Contains(grid.Items.OfType<MyAssetTile>(), t => t.Id == group.SourceId!.Value.Value);
        Assert.NotEmpty(grid.GetVisualDescendants().OfType<ObjectGroupPreview>());
        LookTabTests.Snapshot(window, "backstage-my-assets");
    }

    [Fact]
    public void A_character_kept_in_one_comic_is_added_to_another_from_the_Characters_panes_gallery()
    {
        var window = Open();
        var characters = window.ViewModel.Characters!;
        characters.NewCharacterCommand.Execute(null);
        var alice = characters.Items[0];
        alice.Editor.Name = "Alice " + Guid.NewGuid().ToString("N")[..6];
        characters.ReturnToPage();
        Dispatcher.UIThread.RunJobs();
        characters.KeepInMyAssetsCommand.Execute(alice);
        Assert.True(alice.IsKept);

        window = Open(); // another comic, the same My Assets
        characters = window.ViewModel.Characters!;
        Assert.Empty(characters.Items);
        window.Workspace.Factory.SetActiveDockable(characters);
        Dispatcher.UIThread.RunJobs();
        var pane = window.GetVisualDescendants().OfType<CharacterLibraryView>().Single();

        var add = pane.FindControl<DropDownButton>("AddCharacterButton")!;
        Assert.True(add.IsEffectivelyVisible);
        add.Flyout!.ShowAt(add);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "characters-add-gallery");

        var content = (Control)((Flyout)add.Flyout).Content!;
        var tile = content.GetLogicalDescendants().OfType<Button>().First(b => b.DataContext is CharacterChoice c && c.Character.Id == alice.Id);
        Assert.True(tile.Command!.CanExecute(tile.CommandParameter)); // reaches the pane's command from the flyout's popup
        tile.Command.Execute(tile.CommandParameter);
        tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // and the click closes the gallery
        Dispatcher.UIThread.RunJobs();

        var added = Assert.Single(characters.Items);
        Assert.Equal(alice.Id, added.Id);
        Assert.True(added.IsKept);
        Assert.False(add.Flyout.IsOpen);
    }
}
