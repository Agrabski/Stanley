using SkiaSharp;

namespace Stanley.Bubbles;

public enum AnchorHandleType
{
    /// <summary>Handles are kept collinear through the anchor point (rounded corners).</summary>
    Smooth,

    /// <summary>Handles move independently (sharp corners, e.g. a jagged shout outline).</summary>
    Corner
}

/// <summary>
/// One point on a <see cref="BubbleOutline"/>, with absolute (not relative) bezier
/// handle positions for the incoming and outgoing curve segments.
/// </summary>
public sealed class BubbleAnchor
{
    public SKPoint Point { get; set; }
    public SKPoint InHandle { get; set; }
    public SKPoint OutHandle { get; set; }
    public AnchorHandleType HandleType { get; set; }

    public BubbleAnchor(SKPoint point, SKPoint inHandle, SKPoint outHandle, AnchorHandleType handleType = AnchorHandleType.Smooth)
    {
        Point = point;
        InHandle = inHandle;
        OutHandle = outHandle;
        HandleType = handleType;
    }
}
