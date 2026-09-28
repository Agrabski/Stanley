using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Where hair pieces meet (docs: modular hair), in template units on the default head
/// (front: centre (0, -933), radii 58 x 67, crown at y -1000; profile: centre (8, -933),
/// facing +x). A piece drawn to reach its lines meets any other piece that does - the
/// library's are drawn to them, and every hair template shows them as a locked guide layer.
/// </summary>
public static class HairJoins
{
    /// <summary>One join: a name for the guide ("hairline"), and its points, drawn as a smooth-enough polyline.</summary>
    public sealed record Join(string Name, IReadOnlyList<Point2D> Points);

    private static Join J(string name, params (double X, double Y)[] points) => new(name, points.Select(p => new Point2D(p.X, p.Y)).ToList());

    /// <summary>
    /// Front view. The hairline is where a top ends over the forehead and a fringe starts
    /// (a fringe overlaps it by ~10 units); the partings are where a top's style splits it;
    /// the temples are where sides hang from (overlapping the top); the brow and eye lines
    /// are how far a fringe falls; the nape is the lowest a top reaches behind the ears.
    /// </summary>
    public static IReadOnlyList<Join> Front { get; } =
    [
        J("hairline", (-50, -945), (-44, -962), (-32, -973), (-16, -979), (0, -980), (16, -979), (32, -973), (44, -962), (50, -945)),
        J("parting-middle", (0, -1000), (0, -980)),
        J("parting-left", (22, -997), (18, -978)),
        J("parting-right", (-22, -997), (-18, -978)),
        J("temple-left", (58, -962), (58, -900)),
        J("temple-right", (-58, -962), (-58, -900)),
        J("brow-line", (-40, -950), (40, -950)),
        J("eye-line", (-40, -930), (40, -930)),
        J("nape", (-40, -885), (40, -885)),
    ];

    /// <summary>
    /// Profile view (the character's right side, facing +x): the hairline runs from the
    /// forehead down to the sideburn in front of the ear; the ear sits under the temple;
    /// the nape is where the back of the head meets the neck.
    /// </summary>
    public static IReadOnlyList<Join> Profile { get; } =
    [
        J("hairline", (60, -988), (50, -978), (40, -965), (34, -950), (32, -930), (30, -912)),
        J("parting", (8, -1000), (40, -990)),
        J("ear", (22, -945), (22, -905), (0, -905), (0, -945), (22, -945)),
        J("brow-line", (50, -950), (74, -950)),
        J("eye-line", (50, -930), (74, -930)),
        J("nape", (-50, -900), (-38, -880), (-25, -870)),
    ];

    public static IReadOnlyList<Join> For(ViewAngle view) => view == ViewAngle.Profile ? Profile : Front;
}
