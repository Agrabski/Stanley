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

    /// <summary>
    /// Regenerates the shape from the current bounds under the new preset and re-styles existing
    /// tails, without moving them: each keeps its target, and its base leaves the new outline in
    /// the same direction from the bubble's middle as it left the old one. Keeping the bare
    /// <see cref="BubbleTail.AttachmentT"/> wouldn't do that - it's a fraction along the outline,
    /// and an oval and a Shout star count from different places, so a tail on an oval's left
    /// would jump to the star's top.
    /// </summary>
    public static EditResult<Bubble> SetStyle(Bubble bubble, BubbleStylePreset style)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var newShape = BubbleStylePresets.GenerateShape(style, bounds);
        var centre = new Point2D(bounds.MidX, bounds.MidY);
        var kind = BubbleStylePresets.TailKindFor(style);
        var tails = bubble.Tails.Select(t => t with
        {
            AttachmentT = AnchorRing.TowardsT(newShape.Anchors, centre, AnchorRing.PointAt(bubble.Shape.Anchors, t.AttachmentT)),
            Kind = kind
        }).ToList();
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

    /// <summary>
    /// Translates the shape (and with it every tail's base). Tail targets stay put, so a tail
    /// keeps pointing at whoever is speaking while the bubble moves - unless
    /// <paramref name="withTails"/>, which carries the tips along too: the whole bubble moves as one.
    /// </summary>
    public static EditResult<Bubble> Move(Bubble bubble, double dx, double dy, bool withTails = false)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var moved = bounds with { X = bounds.X + dx, Y = bounds.Y + dy };
        var tails = withTails ? bubble.Tails.Select(t => t with { Target = new Point2D(t.Target.X + dx, t.Target.Y + dy) }).ToList() : bubble.Tails;
        return EditResult<Bubble>.Success(bubble with { Shape = new BubbleShape(AnchorRing.Rescale(bubble.Shape.Anchors, bounds, moved)), Tails = tails });
    }

    /// <summary>How far a new bubble steps aside from one already in its spot (<see cref="OutOfTheWay"/>), in mm.</summary>
    public const double CascadeStepMm = 6;

    /// <summary>
    /// Where a new bubble of <paramref name="bounds"/> should go so it doesn't land exactly on
    /// top of one already there (adding two in a row would otherwise stack them, the second
    /// hiding the first): stepped diagonally aside by <see cref="CascadeStepMm"/> until its
    /// corner is clear of every one in <paramref name="taken"/>, as Office cascades pasted
    /// shapes - down and right, else up and left, staying inside <paramref name="container"/>.
    /// Returns <paramref name="bounds"/> itself when it's clear already or there's no room.
    /// </summary>
    public static Rect2D OutOfTheWay(Rect2D bounds, IReadOnlyCollection<Rect2D> taken, Rect2D container, double step = CascadeStepMm)
    {
        bool Clear(Rect2D r) => taken.All(t => Math.Abs(t.Left - r.Left) >= step / 2 || Math.Abs(t.Top - r.Top) >= step / 2);
        bool Fits(Rect2D r) => r.Left >= container.Left - 1e-9 && r.Top >= container.Top - 1e-9 && r.Right <= container.Right + 1e-9 && r.Bottom <= container.Bottom + 1e-9;

        if (Clear(bounds))
            return bounds;
        foreach (var direction in new[] { 1, -1 })
        {
            for (var k = 1; k <= 50; k++)
            {
                var candidate = bounds with { X = bounds.X + direction * k * step, Y = bounds.Y + direction * k * step };
                if (!Fits(candidate))
                    break;
                if (Clear(candidate))
                    return candidate;
            }
        }
        return bounds;
    }

    /// <summary>
    /// Pulls a bubble fully inside <paramref name="container"/> (its panel): shrinks it if
    /// it's bigger than the panel, slides it in if it pokes out, and clamps every tail
    /// target into the panel too. Always succeeds - it's the normaliser every panel/bubble
    /// edit runs through so a bubble can never drift out of the panel it belongs to.
    /// </summary>
    public static Bubble KeepInside(Bubble bubble, Rect2D container)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var width = Math.Min(bounds.Width, container.Width);
        var height = Math.Min(bounds.Height, container.Height);
        var left = Math.Clamp(bounds.Left, container.Left, container.Right - width);
        var top = Math.Clamp(bounds.Top, container.Top, container.Bottom - height);
        var fitted = new Rect2D(left, top, width, height);

        var shape = fitted == bounds ? bubble.Shape : new BubbleShape(AnchorRing.Rescale(bubble.Shape.Anchors, bounds, fitted));
        var tails = bubble.Tails.Select(t => t with { Target = Clamp(t.Target, container) }).ToList();
        return bubble with { Shape = shape, Tails = tails };
    }

    /// <summary>
    /// Carries a bubble along when its panel is resized (<see cref="PanelContentScale"/>): its
    /// outline and tail tips go where the scaled picture puts them, and its lettering scales
    /// with it, so it reads exactly as before, just bigger or smaller. Its outline's weight is
    /// its style's, the same at any size. The tails' attachments are fractions of the outline,
    /// which a uniform scale leaves where they were.
    /// </summary>
    public static Bubble Scale(Bubble bubble, PanelContentScale scale)
    {
        var size = scale.Scale == 1 ? bubble.FontSizePt : TextEditing.ScaleFontSize(bubble.FontSizePt ?? Bubble.DefaultFontSizePt, scale.Scale);
        return bubble with
        {
            Shape = new BubbleShape(scale.Map(bubble.Shape.Anchors)),
            Tails = bubble.Tails.Select(t => t with { Target = scale.Map(t.Target) }).ToList(),
            FontSizePt = size is { } points && Math.Abs(points - Bubble.DefaultFontSizePt) < 1e-9 ? null : size
        };
    }

    /// <summary>Brings a bubble into another panel (pasting it there) from <paramref name="oldContainer"/> to <paramref name="newContainer"/>: its centre and tail targets keep the same relative position within the panel, its size is kept (unless the panel is too small for it). A panel's own resize goes through <see cref="Scale"/> instead.</summary>
    public static Bubble Refit(Bubble bubble, Rect2D oldContainer, Rect2D newContainer)
    {
        if (oldContainer.Width <= 0 || oldContainer.Height <= 0)
            return KeepInside(bubble, newContainer);

        Point2D Map(Point2D p) => new(
            newContainer.Left + (p.X - oldContainer.Left) / oldContainer.Width * newContainer.Width,
            newContainer.Top + (p.Y - oldContainer.Top) / oldContainer.Height * newContainer.Height);

        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var center = Map(new Point2D(bounds.MidX, bounds.MidY));
        var moved = bubble with
        {
            Shape = new BubbleShape(AnchorRing.Rescale(bubble.Shape.Anchors, bounds, bounds with
            {
                X = center.X - bounds.Width / 2,
                Y = center.Y - bounds.Height / 2
            })),
            Tails = bubble.Tails.Select(t => t with { Target = Map(t.Target) }).ToList()
        };
        return KeepInside(moved, newContainer);
    }

    public static Point2D Clamp(Point2D point, Rect2D container) => new(
        Math.Clamp(point.X, container.Left, container.Right),
        Math.Clamp(point.Y, container.Top, container.Bottom));

    /// <summary>
    /// Grows the shape around its own centre by <paramref name="scale"/> (from the renderer's
    /// measurement - see <c>Stanley.Rendering.BubbleTextRenderer.NeededScale</c>), keeping its
    /// aspect ratio so an oval stays an oval, so typed text never has to shrink to fit; never
    /// shrinks a bubble the user made bigger, the same rule <c>TextEditing.GrowToFit</c> follows
    /// for free text. Tails keep their targets - resizing a bubble shouldn't drag whatever it
    /// points at - and their attachment stays the same fraction along the outline, so it slides
    /// out to the bigger shape rather than jumping.
    /// </summary>
    public static Bubble GrowToFit(Bubble bubble, double scale)
    {
        if (scale <= 1 + 1e-9)
            return bubble;

        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var grown = new Rect2D(
            bounds.MidX - bounds.Width * scale / 2,
            bounds.MidY - bounds.Height * scale / 2,
            bounds.Width * scale,
            bounds.Height * scale);
        var rescaled = AnchorRing.Rescale(bubble.Shape.Anchors, bounds, grown);
        return bubble with { Shape = new BubbleShape(rescaled) };
    }

    public static EditResult<Bubble> SetText(Bubble bubble, string text)
    {
        if (text.Length > MaxTextLength)
            return EditResult<Bubble>.Failure($"Bubble text can't exceed {MaxTextLength} characters.");

        return EditResult<Bubble>.Success(bubble with { Text = text });
    }

    /// <summary>Letters the bubble in <paramref name="family"/> (null or blank: the default lettering font).</summary>
    public static EditResult<Bubble> SetFont(Bubble bubble, string? family) =>
        SetLettering(bubble, LetteringFont.Of(bubble) with { Family = family });

    /// <summary>Letters the bubble in <paramref name="font"/> - typeface, size, bold, italic, alignment - or says why it can't.</summary>
    public static EditResult<Bubble> SetLettering(Bubble bubble, LetteringFont font) =>
        font.Validate() is { IsValid: true } valid
            ? EditResult<Bubble>.Success(valid.Value.ApplyTo(bubble))
            : EditResult<Bubble>.Failure(font.Validate().Error!);

    private static double NextAttachmentT(IReadOnlyList<BubbleTail> tails)
    {
        const double goldenStep = 0.61803398875;
        if (tails.Count == 0)
            return 0.75;
        var t = tails[^1].AttachmentT + goldenStep;
        return t - Math.Floor(t);
    }
}
