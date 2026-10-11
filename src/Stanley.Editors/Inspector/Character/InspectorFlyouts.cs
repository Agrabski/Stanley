using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Stanley.Editors;

/// <summary>Where the character inspector's galleries open.</summary>
internal static class InspectorFlyouts
{
    /// <summary>
    /// Shifts a flyout placed at its button's left edge (<c>LeftEdgeAlignedTop</c>) out to the
    /// left edge of <paramref name="section"/>, so a gallery opens beside the inspector, over the
    /// stage, whichever row or column its button is in - and never covers the inspector itself.
    /// Call it from the flyout's <c>Opening</c>, before it's positioned.
    /// </summary>
    public static void BesideInspector(object? flyout, Visual section)
    {
        if (flyout is PopupFlyoutBase { Target: { } target } popup && target.TranslatePoint(new Point(0, 0), section) is { } at)
            popup.HorizontalOffset = -at.X;
    }
}
