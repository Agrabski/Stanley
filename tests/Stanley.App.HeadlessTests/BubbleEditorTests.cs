using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Stanley.Bubbles;
using Xunit;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Drives the real <see cref="MainWindow"/> through simulated pointer/click input to
/// prove the four POC behaviours end-to-end: resizing, switching styles, adding
/// tails, and moving tails.
/// </summary>
public class BubbleEditorTests
{
    [AvaloniaFact]
    public void DraggingACornerHandle_ResizesTheBubble()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        var boundsBefore = canvas.Bubble.Bounds;

        var windowStart = canvas.TranslatePoint(new Point(boundsBefore.Left, boundsBefore.Top), window)!.Value;
        var windowEnd = canvas.TranslatePoint(new Point(boundsBefore.Left - 60, boundsBefore.Top - 40), window)!.Value;

        window.MouseDown(windowStart, MouseButton.Left);
        window.MouseMove(windowEnd);
        window.MouseUp(windowEnd, MouseButton.Left);

        var boundsAfter = canvas.Bubble.Bounds;
        Assert.True(boundsAfter.Width > boundsBefore.Width, "dragging the top-left handle outward should widen the bubble");
        Assert.True(boundsAfter.Height > boundsBefore.Height, "dragging the top-left handle outward should heighten the bubble");
        Assert.Equal(boundsBefore.Right, boundsAfter.Right, 1);
        Assert.Equal(boundsBefore.Bottom, boundsAfter.Bottom, 1);
    }

    [AvaloniaFact]
    public void SwitchingStylePreset_RegeneratesOutlineAndKeepsExistingTails()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        var tailCountBefore = canvas.Bubble.Tails.Count;
        var firstTailAttachmentBefore = canvas.Bubble.Tails[0].AttachmentT;

        ClickCenter(window, window.ShoutStyleRadio);

        Assert.Equal(BubbleStylePreset.Shout, canvas.Bubble.Style);
        Assert.Equal(tailCountBefore, canvas.Bubble.Tails.Count);
        Assert.Equal(firstTailAttachmentBefore, canvas.Bubble.Tails[0].AttachmentT);
        Assert.All(canvas.Bubble.Tails, t => Assert.Equal(TailKind.JaggedTriangle, t.Kind));
    }

    [AvaloniaFact]
    public void AddTailButton_AppendsANewTail()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        var countBefore = canvas.Bubble.Tails.Count;

        ClickCenter(window, window.AddTailButtonControl);

        Assert.Equal(countBefore + 1, canvas.Bubble.Tails.Count);
    }

    [AvaloniaFact]
    public void DraggingATailTip_MovesItsTargetAnywhereOnTheCanvas()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        var tail = canvas.Bubble.Tails[0];

        var windowStart = canvas.TranslatePoint(new Point(tail.Target.X, tail.Target.Y), window)!.Value;
        var windowEnd = canvas.TranslatePoint(new Point(20, 20), window)!.Value;

        window.MouseDown(windowStart, MouseButton.Left);
        window.MouseMove(windowEnd);
        window.MouseUp(windowEnd, MouseButton.Left);

        Assert.Equal(20, tail.Target.X, 1);
        Assert.Equal(20, tail.Target.Y, 1);
    }

    [AvaloniaFact]
    public void DraggingATailBase_SlidesItsAttachmentAlongTheOutline()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        var tail = canvas.Bubble.Tails[0];
        var attachmentBefore = tail.AttachmentT;

        var startPoint = canvas.Bubble.Outline.PointAt(attachmentBefore);
        var newT = attachmentBefore + 0.15f > 1f ? attachmentBefore - 0.15f : attachmentBefore + 0.15f;
        var endPoint = canvas.Bubble.Outline.PointAt(newT);

        var windowStart = canvas.TranslatePoint(new Point(startPoint.X, startPoint.Y), window)!.Value;
        var windowEnd = canvas.TranslatePoint(new Point(endPoint.X, endPoint.Y), window)!.Value;

        window.MouseDown(windowStart, MouseButton.Left);
        window.MouseMove(windowEnd);
        window.MouseUp(windowEnd, MouseButton.Left);

        Assert.NotEqual(attachmentBefore, tail.AttachmentT, 2);
    }

    [AvaloniaFact]
    public void RightClickingATailTip_RemovesThatTail()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = window.CanvasControl;
        canvas.AddTail();
        var countBefore = canvas.Bubble.Tails.Count;
        var tail = canvas.Bubble.Tails[^1];

        var windowPoint = canvas.TranslatePoint(new Point(tail.Target.X, tail.Target.Y), window)!.Value;
        window.MouseDown(windowPoint, MouseButton.Right);
        window.MouseUp(windowPoint, MouseButton.Right);

        Assert.Equal(countBefore - 1, canvas.Bubble.Tails.Count);
    }

    /// <summary>Simulates a real pointer click at a control's centre, the same way a user would activate it.</summary>
    private static void ClickCenter(MainWindow window, Control control)
    {
        var center = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var windowPoint = control.TranslatePoint(center, window)!.Value;
        window.MouseDown(windowPoint, MouseButton.Left);
        window.MouseUp(windowPoint, MouseButton.Left);
    }
}
