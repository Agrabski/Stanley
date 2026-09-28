using System.Text.Json.Serialization;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// What a panel's outline means, beyond its raw anchors - so a resize regenerates the
/// right shape (<see cref="PanelShapes"/>) instead of stretching it. Kept on <see cref="Issues.Panel"/>
/// itself, the same way <c>BubbleStylePreset</c> sits beside a bubble's shape.
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PanelKind>))]
public enum PanelKind
{
    /// <summary>The default: an axis-aligned box, tiling the page grid.</summary>
    Rectangle,

    /// <summary>A scalloped thought cloud (Insert › Thought cloud): floats over the grid rather than tiling it.</summary>
    Cloud
}

/// <summary>Pure <c>bounds -&gt; anchors</c> generators for panel shapes, the same shape the preset tables (<see cref="Bubbles.BubbleStylePresets"/>, <see cref="MetricPaperSizes"/>) use.</summary>
public static class PanelShapes
{
    /// <summary>An axis-aligned rectangular panel - the default a panel-layout editor creates and resizes; an arbitrary <see cref="PanelShape"/> is still the escape hatch for hand-edited panel outlines.</summary>
    public static PanelShape Rectangle(Rect2D bounds) => new([
        Corner(bounds.Left, bounds.Top),
        Corner(bounds.Right, bounds.Top),
        Corner(bounds.Right, bounds.Bottom),
        Corner(bounds.Left, bounds.Bottom)
    ]);

    /// <summary>
    /// A thought-cloud outline filling <paramref name="bounds"/>: a ring of <see cref="Lobes"/>
    /// rounded bumps around a base ellipse, each bump's peak touching the ellipse
    /// (<c>r=1</c>) and each valley between two bumps riding <see cref="BumpAmplitude"/>
    /// inward from it - "puffy", not "spiky", and never bulging past <paramref name="bounds"/>
    /// the way a symmetric wave (peaks riding out as far as valleys ride in) would. Deterministic
    /// (same bounds always give the same anchors), so a resize just calls this again at the new
    /// bounds instead of stretching the old scallops into ovals.
    /// </summary>
    public static PanelShape Cloud(Rect2D bounds)
    {
        const int lobes = Lobes;
        const int perLobe = AnchorsPerLobe;
        const double amplitude = BumpAmplitude;

        var n = lobes * perLobe;
        var cx = bounds.MidX;
        var cy = bounds.MidY;
        var rx = bounds.Width / 2;
        var ry = bounds.Height / 2;
        var dTheta = Math.PI * 2 / n;
        // The standard cubic-bezier handle length for a sampled parametric curve: a third of
        // the step, scaled by the curve's own speed there, so consecutive segments join smoothly.
        var handleScale = dTheta / 3.0;

        // r ranges from 1 (a bump's peak, cos = 1) down to 1 - 2*amplitude (a valley, cos = -1) -
        // never above 1, so the ring never reaches past the ellipse bounds already inscribes.
        double RadiusAt(double theta) => 1 - amplitude + amplitude * Math.Cos(lobes * theta);

        Point2D PositionAt(double theta)
        {
            var r = RadiusAt(theta);
            return new Point2D(cx + rx * r * Math.Cos(theta), cy + ry * r * Math.Sin(theta));
        }

        Point2D TangentAt(double theta)
        {
            var r = RadiusAt(theta);
            var dr = -amplitude * lobes * Math.Sin(lobes * theta);
            return new Point2D(
                rx * (dr * Math.Cos(theta) - r * Math.Sin(theta)),
                ry * (dr * Math.Sin(theta) + r * Math.Cos(theta)));
        }

        var anchors = new List<ShapeAnchor>(n);
        for (var i = 0; i < n; i++)
        {
            var theta = dTheta * i;
            var p = PositionAt(theta);
            var t = TangentAt(theta);
            var inHandle = new Point2D(p.X - t.X * handleScale, p.Y - t.Y * handleScale);
            var outHandle = new Point2D(p.X + t.X * handleScale, p.Y + t.Y * handleScale);
            anchors.Add(new ShapeAnchor(p, inHandle, outHandle, AnchorHandleKind.Smooth));
        }
        return new PanelShape(anchors);
    }

    /// <summary>
    /// How many bumps a thought cloud's outline has around its perimeter. A multiple of 4 so an
    /// anchor always lands exactly on all four compass points (0/90/180/270 degrees) at a bump's
    /// full radius - which keeps <see cref="AnchorRing.BoundingBox"/> of the result exactly the
    /// size of the bounds given to <see cref="Cloud"/>, rather than a little short of it on some
    /// axis. That matters beyond looks: a panel-layout move reads a panel's current size from
    /// that same bounding box before calling <see cref="Cloud"/> again, so any systematic
    /// shortfall would compound into a slow shrink every time a cloud panel is dragged.
    /// </summary>
    public const int Lobes = 8;

    /// <summary>Anchors sampled per bump (a peak and a valley) - enough to keep every bump visibly round rather than faceted.</summary>
    public const int AnchorsPerLobe = 2;

    /// <summary>How far a valley dips in from the peaks' ellipse, as a fraction of that ellipse's own radius.</summary>
    public const double BumpAmplitude = 0.14;

    private static ShapeAnchor Corner(double x, double y)
    {
        var p = new Point2D(x, y);
        return new ShapeAnchor(p, p, p, AnchorHandleKind.Corner);
    }
}
