using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// A thought cloud is a panel like any other - the same background, characters, bubbles and
/// elements - just shaped like a scallop (<see cref="PanelShapes.Cloud"/>) instead of a
/// rectangle, and marked <see cref="PanelKind.Cloud"/> so <see cref="PanelLayoutEditing.Resize"/>
/// regenerates that outline instead of stretching it, and the grid tools
/// (<see cref="PanelGutters"/>, split, re-tiling) leave it floating above the page. Its own
/// creation and its optional trail live here; everything else about editing it is the same
/// panel machinery every other panel uses.
/// </summary>
public static class ThoughtCloudEditing
{
    /// <summary>A brand-new, empty thought cloud filling <paramref name="bounds"/>, with no trail yet.</summary>
    public static EditResult<Panel> Create(Rect2D bounds)
    {
        if (bounds.Width < PanelLayoutEditing.MinPanelSizeMm - 1e-9 || bounds.Height < PanelLayoutEditing.MinPanelSizeMm - 1e-9)
            return EditResult<Panel>.Failure($"A thought cloud must be at least {PanelLayoutEditing.MinPanelSizeMm}x{PanelLayoutEditing.MinPanelSizeMm}mm.");

        return EditResult<Panel>.Success(new Panel(
            PanelId.New(),
            PanelShapes.Cloud(bounds),
            Background: null,
            CharacterInstances: [],
            Bubbles: [],
            Kind: PanelKind.Cloud));
    }

    /// <summary>
    /// Where a new cloud lands with nothing else to say where: about a third of
    /// <paramref name="reference"/> (the selected panel's bounds), tucked just inside its top
    /// edge - or, with no panel selected, a third of the live area centred at the top of the
    /// page. Always at least <see cref="PanelLayoutEditing.MinPanelSizeMm"/> and clamped onto
    /// the page, the way every other panel bounds is.
    /// </summary>
    public static Rect2D DefaultBounds(Rect2D? reference, Rect2D pageBounds, PanelGrid grid)
    {
        var live = grid.LiveArea(pageBounds);
        var basis = reference ?? live;
        var width = Math.Clamp(basis.Width / 3, PanelLayoutEditing.MinPanelSizeMm, Math.Max(basis.Width, PanelLayoutEditing.MinPanelSizeMm));
        var height = Math.Clamp(basis.Height / 3, PanelLayoutEditing.MinPanelSizeMm, Math.Max(basis.Height, PanelLayoutEditing.MinPanelSizeMm));
        var x = basis.MidX - width / 2;
        // Near the reference panel's own top when there is one; otherwise right at the page's.
        var y = reference is null ? basis.Top : basis.Top + basis.Height * 0.06;
        return ClampToPage(new Rect2D(x, y, width, height), pageBounds);
    }

    private static Rect2D ClampToPage(Rect2D bounds, Rect2D pageBounds)
    {
        var width = Math.Min(bounds.Width, pageBounds.Width);
        var height = Math.Min(bounds.Height, pageBounds.Height);
        var x = Math.Clamp(bounds.X, pageBounds.Left, Math.Max(pageBounds.Left, pageBounds.Right - width));
        var y = Math.Clamp(bounds.Y, pageBounds.Top, Math.Max(pageBounds.Top, pageBounds.Bottom - height));
        return new Rect2D(x, y, width, height);
    }

    // ---------------------------------------------------------------- thought trail

    /// <summary>Adds the cloud's trail (replacing one already there), aimed at <paramref name="target"/> from whichever point on the outline is nearest it.</summary>
    public static Panel SetTrail(Panel panel, Point2D target) =>
        panel with { Trail = new ThoughtTrail(AnchorRing.NearestT(panel.Shape.Anchors, target), target) };

    /// <summary>Takes the trail off; Insert › Thought trail adds a fresh one back.</summary>
    public static Panel RemoveTrail(Panel panel) => panel with { Trail = null };

    /// <summary>Drags the trail's tip - where it points, towards the thinker.</summary>
    public static Panel MoveTrailTarget(Panel panel, Point2D target) =>
        panel.Trail is { } trail ? panel with { Trail = trail with { Target = target } } : panel;

    /// <summary>Slides the trail's base to wherever on the outline is nearest <paramref name="pointer"/>, the same gesture a bubble's tail base takes.</summary>
    public static Panel SlideTrailAttachment(Panel panel, Point2D pointer) =>
        panel.Trail is { } trail ? panel with { Trail = trail with { AttachmentT = AnchorRing.NearestT(panel.Shape.Anchors, pointer) } } : panel;

    /// <summary>
    /// Carries the trail's target along when its cloud moves or resizes from
    /// <paramref name="oldBounds"/> to <paramref name="newBounds"/>: the same proportional
    /// mapping <see cref="BubbleEditing.Refit"/> gives a bubble's tail targets, so the trail
    /// keeps pointing at roughly the same relative spot rather than the cloud sliding away
    /// from under it. The attachment fraction needs no such mapping - it's already
    /// resolution-independent.
    /// </summary>
    public static ThoughtTrail? RefitTrail(ThoughtTrail? trail, Rect2D oldBounds, Rect2D newBounds)
    {
        if (trail is null)
            return null;
        if (oldBounds.Width <= 0 || oldBounds.Height <= 0)
            return trail;

        var x = newBounds.Left + (trail.Target.X - oldBounds.Left) / oldBounds.Width * newBounds.Width;
        var y = newBounds.Top + (trail.Target.Y - oldBounds.Top) / oldBounds.Height * newBounds.Height;
        return trail with { Target = new Point2D(x, y) };
    }
}
