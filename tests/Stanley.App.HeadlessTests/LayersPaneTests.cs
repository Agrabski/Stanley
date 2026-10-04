using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;

namespace Stanley.App.HeadlessTests;

/// <summary>The Layers pane: docked right of the page, shown from the View tab, and not an editor.</summary>
[Collection("Page Editor Tests")]
public class LayersPaneTests
{
    private static (MainWindow Window, PageEditorViewModel Editor) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.Editor);
    }

    private static IReadOnlyList<LayersView> Views(MainWindow window) => window.GetVisualDescendants().OfType<LayersView>().ToList();

    private static double Left(Visual visual, Visual relativeTo) => visual.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;

    [Fact]
    public void The_Layers_pane_stays_away_until_asked_for_and_then_sits_right_of_the_page()
    {
        var (window, editor) = Open();
        try
        {
            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Views(window));

            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();

            var layers = Assert.Single(Views(window));
            var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
            var pages = window.GetVisualDescendants().OfType<PageNavigatorView>().Single();
            Assert.True(Left(layers, window) >= Left(canvas, window) + canvas.Bounds.Width - 1, "the Layers pane sits to the right of the page");
            Assert.True(Left(pages, window) + pages.Bounds.Width <= Left(canvas, window) + 1, "the Pages pane is still on the left");
            Assert.DoesNotContain(layers, window.RibbonBarControl.GetVisualDescendants());
        }
        finally
        {
            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void Hiding_the_Layers_pane_gives_the_page_its_room_back()
    {
        var (window, editor) = Open();
        try
        {
            var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().Single();
            var before = canvas.Bounds.Width;

            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(canvas.Bounds.Width < before, "the pane takes room from the page");

            editor.ShowLayers = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, canvas.Bounds.Width, 1);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void The_View_tabs_Layers_checkbox_shows_and_hides_the_pane()
    {
        var (window, editor) = Open();
        try
        {
            var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
            ribbon.TabControl.SelectedItem = ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == "ViewTab");
            Dispatcher.UIThread.RunJobs();
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ShowLayersCheck");
            Assert.True(check.IsVisible);
            Assert.False(check.IsChecked);

            check.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.ShowLayers);
            Assert.Single(Views(window));

            check.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.ShowLayers);
            Assert.Empty(Views(window));
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void The_Layers_switch_is_one_setting_for_every_page()
    {
        var (window, editor) = Open();
        try
        {
            var navigator = window.ViewModel.Navigator!;
            navigator.AddPageAfter(navigator.Pages[^1]);
            Dispatcher.UIThread.RunJobs();

            editor.ShowLayers = true;

            Assert.All(navigator.AllPages, p => Assert.True(p.Editor.ShowLayers));
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }

    [Fact]
    public void Showing_the_Layers_pane_never_moves_the_ribbon_off_the_page()
    {
        var (window, editor) = Open();
        try
        {
            editor.ShowLayers = true;
            Dispatcher.UIThread.RunJobs();
            window.Workspace.Factory.SetActiveDockable(window.GetVisualDescendants().OfType<LayersView>().Single().DataContext as Dock.Model.Core.IDockable);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(editor, window.Workspace.ActiveEditor);
        }
        finally
        {
            editor.ShowLayers = false;
        }
    }
}
