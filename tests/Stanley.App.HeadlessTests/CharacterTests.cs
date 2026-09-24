using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
