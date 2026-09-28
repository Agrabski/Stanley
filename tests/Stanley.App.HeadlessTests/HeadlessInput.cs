using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stanley.App.HeadlessTests;

/// <summary>Input the way the system reports it, where sending it headlessly differs.</summary>
internal static class HeadlessInput
{
    /// <summary>
    /// A double-click at <paramref name="point"/> (window coordinates): a click, then a second
    /// press reported with ClickCount 2, as the system does. Headless input is timed when it's
    /// sent, so the second of two plain clicks lands outside the double-click time whenever the
    /// first click's work (a ribbon tab or an editor opening) is slow - on a busy test run, say -
    /// where a real system times each click when it happens.
    /// </summary>
    public static void DoubleClick(this Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        var target = window.InputHitTest(point) as Interactive
            ?? throw new InvalidOperationException($"Nothing to double-click at {point}.");
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, point, timestamp: 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, clickCount: 2));
        window.MouseUp(point, MouseButton.Left);
        pointer.Capture(null);
    }
}
