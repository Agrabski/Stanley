using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Headless smoke tests for Insert › Thought cloud (issue #68): the ribbon command actually
/// reaches the live page, and a click through the floating cloud lands on it rather than on
/// the panel it overlaps - the one thing that can't be checked without a real hit test.
/// </summary>
[Collection("Page Editor Tests")]
public class ThoughtCloudTests
{
    [Fact]
    public void InsertThoughtCloudCommand_AddsASelectedCloudAsOneUndoStep()
    {
        var window = new MainWindow();
        window.Show();
        var panelId = window.Editor.Working.PanelOrder[0];
        window.Editor.Select(panelId);
        Assert.False(window.History.CanUndo, "a freshly opened comic should have nothing to undo yet");

        window.Editor.InsertThoughtCloudCommand.Execute(null);

        var cloudId = window.Editor.SelectedPanelId;
        Assert.NotNull(cloudId);
        Assert.NotEqual(panelId, cloudId);
        Assert.Equal(PanelKind.Cloud, window.Editor.Working.Panels[cloudId!.Value].Kind);
        Assert.True(window.History.CanUndo);

        window.History.Undo();
        Assert.False(window.History.CanUndo, "inserting the cloud should have been exactly one undo step");
        Assert.DoesNotContain(cloudId.Value, window.Editor.Working.PanelOrder);
        Assert.Single(window.Editor.Working.PanelOrder);
    }

    [Fact]
    public void ClickingInsideAFloatingCloud_SelectsTheCloudNotThePanelUnderneath()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var panelId = window.Editor.Working.PanelOrder[0];
        window.Editor.Select(panelId);

        window.Editor.InsertThoughtCloudCommand.Execute(null);
        var cloudId = window.Editor.SelectedPanelId!.Value;
        var cloudBounds = window.Editor.PanelBounds(cloudId);
        window.Editor.ClearSelection();
        Assert.Null(window.Editor.SelectedPanelId);

        // The cloud's own centre: inside its outline, and inside the panel it floats over too -
        // this is the point a plain "which panel's box contains it" check would get wrong.
        var point = canvas.TranslatePoint(canvas.PageToControl(new Point2D(cloudBounds.MidX, cloudBounds.MidY)), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(cloudId, window.Editor.SelectedPanelId);
    }

    private static PageCanvasControl? GetPageCanvasControl(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<PageCanvasControl>().FirstOrDefault();
    }
}
