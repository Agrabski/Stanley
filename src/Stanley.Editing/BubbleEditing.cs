using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editing;

/// <summary>
/// Every valid way to change a <see cref="Bubble"/>: pure functions from a current
/// value (plus whatever the edit needs) to a new value or a validation
/// <see cref="EditResult{T}.Error"/>. These are the same functions a live-preview drag
/// calls on every pointer move and a commit calls once - there is exactly one
/// implementation of "what does resizing a bubble mean," not one for preview and
/// another for the real thing.
/// </summary>
public static class BubbleEditing
{
    public const double MinWidthMm = 15;
    public const double MinHeightMm = 10;
    public const int MaxTextLength = 500;

    public static EditResult<Bubble> Create(Rect2D bounds, BubbleStylePreset style)
    {
        if (bounds.Width < MinWidthMm || bounds.Height < MinHeightMm)
            return EditResult<Bubble>.Failure($"A bubble must be at least {MinWidthMm}x{MinHeightMm}mm.");

        return EditResult<Bubble>.Success(new Bubble(
            BubbleId.New(),
            BubbleStylePresets.GenerateShape(style, bounds),
            style,
            [],
            ""));
    }

    /// <summary>Rescales the shape to <paramref name="newBounds"/>. Tail targets are left alone: resizing the bubble shouldn't drag whatever a tail points at.</summary>
    public static EditResult<Bubble> Resize(Bubble bubble, Rect2D newBounds)
    {
        if (newBounds.Width < MinWidthMm || newBounds.Height < MinHeightMm)
            return EditResult<Bubble>.Failure($"A bubble must be at least {MinWidthMm}x{MinHeightMm}mm.");

        var currentBounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var rescaled = AnchorRing.Rescale(bubble.Shape.Anchors, currentBounds, newBounds);
        return EditResult<Bubble>.Success(bubble with { Shape = new BubbleShape(rescaled) });
    }

    /// <summary>Regenerates the shape from the current bounds under the new preset, and re-styles existing tails, but keeps their attachment/target so they don't jump.</summary>
    public static EditResult<Bubble> SetStyle(Bubble bubble, BubbleStylePreset style)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var newShape = BubbleStylePresets.GenerateShape(style, bounds);
        var kind = BubbleStylePresets.TailKindFor(style);
        var tails = bubble.Tails.Select(t => t with { Kind = kind }).ToList();
        return EditResult<Bubble>.Success(bubble with { Shape = newShape, Style = style, Tails = tails });
    }

    /// <summary>Spreads each new tail's default attachment point away from the previous one (golden-angle step) so repeated inserts don't stack tails on top of each other.</summary>
    public static EditResult<Bubble> AddTail(Bubble bubble, Point2D target)
    {
        var attachmentT = NextAttachmentT(bubble.Tails);
        var tail = new BubbleTail(attachmentT, target, BubbleStylePresets.TailKindFor(bubble.Style));
        return EditResult<Bubble>.Success(bubble with { Tails = [.. bubble.Tails, tail] });
    }

    public static EditResult<Bubble> RemoveTail(Bubble bubble, int tailIndex)
    {
        if (tailIndex < 0 || tailIndex >= bubble.Tails.Count)
            return EditResult<Bubble>.Failure("No such tail.");

        var tails = bubble.Tails.Where((_, i) => i != tailIndex).ToList();
        return EditResult<Bubble>.Success(bubble with { Tails = tails });
    }

    public static EditResult<Bubble> MoveTailTarget(Bubble bubble, int tailIndex, Point2D newTarget)
    {
        if (tailIndex < 0 || tailIndex >= bubble.Tails.Count)
            return EditResult<Bubble>.Failure("No such tail.");

        var tails = bubble.Tails.ToList();
        tails[tailIndex] = tails[tailIndex] with { Target = newTarget };
        return EditResult<Bubble>.Success(bubble with { Tails = tails });
    }

    /// <summary>Slides a tail's base to wherever on the shape is nearest <paramref name="pointer"/>.</summary>
    public static EditResult<Bubble> SlideTailAttachment(Bubble bubble, int tailIndex, Point2D pointer)
    {
        if (tailIndex < 0 || tailIndex >= bubble.Tails.Count)
            return EditResult<Bubble>.Failure("No such tail.");

        var attachmentT = AnchorRing.NearestT(bubble.Shape.Anchors, pointer);
        var tails = bubble.Tails.ToList();
        tails[tailIndex] = tails[tailIndex] with { AttachmentT = attachmentT };
        return EditResult<Bubble>.Success(bubble with { Tails = tails });
    }

    public static EditResult<Bubble> SetText(Bubble bubble, string text)
    {
        if (text.Length > MaxTextLength)
            return EditResult<Bubble>.Failure($"Bubble text can't exceed {MaxTextLength} characters.");

        return EditResult<Bubble>.Success(bubble with { Text = text });
    }

    private static double NextAttachmentT(IReadOnlyList<BubbleTail> tails)
    {
        const double goldenStep = 0.61803398875;
        if (tails.Count == 0)
            return 0.75;
        var t = tails[^1].AttachmentT + goldenStep;
        return t - Math.Floor(t);
    }
}
