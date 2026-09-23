using SkiaSharp;

namespace Stanley.Bubbles;

/// <summary>
/// One speech bubble: bounds, a bezier outline, a style preset, and any number of
/// tails. <see cref="BuildRenderPath"/> is the outline unioned with every tail's
/// own polygon, which is why adding another tail needs no special case.
/// </summary>
public sealed class SpeechBubble
{
    public SKRect Bounds { get; private set; }
    public BubbleOutline Outline { get; private set; }
    public BubbleStylePreset Style { get; private set; }
    public List<BubbleTail> Tails { get; } = new();

    public SpeechBubble(SKRect bounds, BubbleStylePreset style)
    {
        Bounds = bounds;
        Style = style;
        Outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(style, bounds));
    }

    /// <summary>Resizes the bubble by affine-scaling the outline to the new bounds. Tail targets are left alone: resizing the bubble shouldn't drag whatever a tail points at.</summary>
    public void Resize(SKRect newBounds)
    {
        Outline.Rescale(Bounds, newBounds);
        Bounds = newBounds;
    }

    /// <summary>Regenerates the outline from the current bounds under the new preset and re-styles existing tails, but keeps their attachment/target so they don't jump.</summary>
    public void SetStyle(BubbleStylePreset style)
    {
        Style = style;
        Outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(style, Bounds));
        var kind = BubbleStylePresets.TailKindFor(style);
        foreach (var tail in Tails)
            tail.Kind = kind;
    }

    public BubbleTail AddTail(SKPoint target)
    {
        var tail = new BubbleTail(NextAttachmentT(), target, BubbleStylePresets.TailKindFor(Style));
        Tails.Add(tail);
        return tail;
    }

    public bool RemoveTail(BubbleTail tail) => Tails.Remove(tail);

    /// <summary>Spreads each new tail's default attachment point away from the previous one (golden-angle step) so repeated "Add Tail" clicks don't stack tails on top of each other.</summary>
    private float NextAttachmentT()
    {
        const float goldenStep = 0.61803398875f;
        if (Tails.Count == 0)
            return 0.75f;
        var t = Tails[^1].AttachmentT + goldenStep;
        return t - MathF.Floor(t);
    }

    public SKPath BuildRenderPath()
    {
        var current = Outline.ToPath();
        foreach (var tail in Tails)
        {
            using var tailPath = tail.GeneratePath(Outline);
            var next = current.Op(tailPath, SKPathOp.Union);
            current.Dispose();
            current = next;
        }
        return current;
    }
}
