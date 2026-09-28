using System.Collections.Concurrent;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Rendering;

/// <summary>
/// Maps drawn art from template space onto a character (docs/sticker-system.md §3, §5).
/// Every template is the default body standing, 1000 units tall (origin at the ground
/// between the feet, y down), so a template point divided by 1000 is a point of the
/// default figure. Each region maps the template's version of itself onto the
/// character's: the head ellipse onto the head ellipse, the torso row by row, a limb
/// segment along and across.
/// </summary>
public static class RegionMapping
{
    public const double TemplateUnits = 1000;

    private static readonly ConcurrentDictionary<ViewAngle, BodyFigure> Templates = new();

    /// <summary>The default body at rest, as a template draws it for <paramref name="view"/>.</summary>
    public static BodyFigure Template(ViewAngle view) => Templates.GetOrAdd(view, v => BodyRig.Build(BodyShape.Default, v));

    /// <summary>
    /// Every point of the template's region onto the character's (<paramref name="figure"/>),
    /// template units in, figure space out - the art hugs the character's outline.
    /// </summary>
    public static Func<Point2D, Point2D> Warp(BodyFigure figure, BodyRegion region, LimbSide side, LimbSide? partSide)
    {
        var template = Template(figure.Angle);
        var (sourceSide, mirror) = SourceSide(figure.Angle, region, side, partSide);
        return p =>
        {
            var t = new Point2D(p.X / TemplateUnits * (mirror ? -1 : 1), p.Y / TemplateUnits);
            return region switch
            {
                BodyRegion.Head => Ellipse(template.Regions.Head, figure.Regions.Head, t),
                BodyRegion.Hand => Ellipse(template.Regions.Hand(sourceSide), figure.Regions.Hand(side), t),
                BodyRegion.Foot => Ellipse(template.Regions.Foot(sourceSide), figure.Regions.Foot(side), t),
                BodyRegion.Neck => Segment(template.Regions.Neck, figure.Regions.Neck, t),
                BodyRegion.Arm => Limb(template.Regions.Arm(sourceSide), figure.Regions.Arm(side), t),
                BodyRegion.Leg => Limb(template.Regions.Leg(sourceSide), figure.Regions.Leg(side), t),
                _ => Torso(template.Regions.Torso, figure.Regions.Torso, t),
            };
        };
    }

    /// <summary>
    /// The rigid placement of art pinned at <paramref name="centre"/> (template units): moved
    /// to where the region takes that point, turned as the region is turned there, and
    /// scaled by the region's size (head height, torso length, segment length) - so eyes
    /// keep their shape on any head.
    /// </summary>
    public static SKMatrix Pin(BodyFigure figure, BodyRegion region, LimbSide side, LimbSide? partSide, Point2D centre)
    {
        var template = Template(figure.Angle);
        var (sourceSide, mirror) = SourceSide(figure.Angle, region, side, partSide);
        var anchor = Warp(figure, region, side, partSide)(centre);
        var (scale, turn) = region switch
        {
            BodyRegion.Head => (figure.Regions.Head.RadiusY / template.Regions.Head.RadiusY, figure.Regions.Head.RotationDegrees - template.Regions.Head.RotationDegrees),
            BodyRegion.Hand => EllipseScale(template.Regions.Hand(sourceSide), figure.Regions.Hand(side)),
            BodyRegion.Foot => EllipseScale(template.Regions.Foot(sourceSide), figure.Regions.Foot(side)),
            BodyRegion.Neck => SegmentScale(template.Regions.Neck, figure.Regions.Neck),
            BodyRegion.Arm => LimbScale(template.Regions.Arm(sourceSide), figure.Regions.Arm(side), centre, mirror),
            BodyRegion.Leg => LimbScale(template.Regions.Leg(sourceSide), figure.Regions.Leg(side), centre, mirror),
            _ => TorsoScale(template.Regions.Torso, figure.Regions.Torso, centre),
        };
        var c = new SKPoint((float)(centre.X / TemplateUnits), (float)(centre.Y / TemplateUnits));
        return SKMatrix.CreateScale((float)(1 / TemplateUnits), (float)(1 / TemplateUnits))
            .PostConcat(SKMatrix.CreateScale(mirror ? -1 : 1, 1, c.X, c.Y))
            .PostConcat(SKMatrix.CreateTranslation(-c.X, -c.Y))
            .PostConcat(SKMatrix.CreateScale((float)scale, (float)scale))
            .PostConcat(SKMatrix.CreateRotationDegrees((float)turn))
            .PostConcat(SKMatrix.CreateTranslation((float)anchor.X, (float)anchor.Y));
    }

    /// <summary>
    /// A move on the character (<paramref name="figureDelta"/>, figure space) as a move in
    /// template units for art on <paramref name="region"/> - what dragging drawn art on the
    /// stage adds to its offset. Undoes the region's local turn and scale.
    /// </summary>
    public static Point2D ToTemplate(BodyFigure figure, BodyRegion region, LimbSide? partSide, Point2D figureDelta)
    {
        var (centre, _) = StickerImport.RegionBox(figure.Angle, region);
        var pin = Pin(figure, region, partSide ?? LimbSide.Right, partSide, centre);
        if (!pin.TryInvert(out var inverse))
            return default;
        var v = inverse.MapVector((float)figureDelta.X, (float)figureDelta.Y);
        return new Point2D(v.X, v.Y);
    }

    /// <summary>The frame a fabric on art lies in: the region's, near <paramref name="at"/> (figure space).</summary>
    internal static SKMatrix FabricFrame(BodyFigure figure, BodyRegion region, LimbSide side, Point2D at) => region switch
    {
        BodyRegion.Head => StickerCovers.EllipseFrame(figure.Regions.Head),
        BodyRegion.Hand => StickerCovers.EllipseFrame(figure.Regions.Hand(side)),
        BodyRegion.Foot => StickerCovers.EllipseFrame(figure.Regions.Foot(side)),
        BodyRegion.Arm or BodyRegion.Leg => SegmentFrame(Nearest(region == BodyRegion.Arm ? figure.Regions.Arm(side) : figure.Regions.Leg(side), at)),
        BodyRegion.Neck => SegmentFrame(figure.Regions.Neck),
        _ => StickerCovers.Frame(at, figure.Regions.Torso.Bend.AngleAt(figure.Regions.Torso.Bottom - 0.5 * (figure.Regions.Torso.Bottom - figure.Regions.Torso.Top))),
    };

    /// <summary>
    /// Art for both limbs is drawn once, on the template's right limb (the near one side
    /// on). The left limb gets it mirrored across the body in a front view, or carried over
    /// along and across the limb side on. A part for one side is drawn on that side.
    /// </summary>
    private static (LimbSide Source, bool Mirror) SourceSide(ViewAngle view, BodyRegion region, LimbSide side, LimbSide? partSide)
    {
        if (region is not (BodyRegion.Arm or BodyRegion.Leg or BodyRegion.Hand or BodyRegion.Foot))
            return (side, false);
        if (partSide is { } only)
            return (only, false);
        if (side == LimbSide.Right)
            return (LimbSide.Right, false);
        return view == ViewAngle.Profile ? (LimbSide.Right, false) : (LimbSide.Left, true);
    }

    private static Point2D Ellipse(BodyEllipse from, BodyEllipse to, Point2D p)
    {
        var local = Rotate(new Point2D(p.X - from.Center.X, p.Y - from.Center.Y), -from.RotationDegrees);
        var (u, v) = (local.X / from.RadiusX, local.Y / from.RadiusY);
        var mapped = Rotate(new Point2D(u * to.RadiusX, v * to.RadiusY), to.RotationDegrees);
        return new Point2D(to.Center.X + mapped.X, to.Center.Y + mapped.Y);
    }

    private static (double, double) EllipseScale(BodyEllipse from, BodyEllipse to) =>
        (Math.Max(to.RadiusX, to.RadiusY) / Math.Max(from.RadiusX, from.RadiusY), to.RotationDegrees - from.RotationDegrees);

    private static Point2D Segment(BodyCapsule from, BodyCapsule to, Point2D p)
    {
        var (fx, fy, fLength) = Axis(from);
        var (tx, ty, tLength) = Axis(to);
        var (dx, dy) = (p.X - from.From.X, p.Y - from.From.Y);
        var along = (dx * fx + dy * fy) / fLength;
        var across = (-dx * fy + dy * fx);
        var fromRadius = from.FromRadius + (from.ToRadius - from.FromRadius) * Math.Clamp(along, 0, 1);
        var toRadius = to.FromRadius + (to.ToRadius - to.FromRadius) * Math.Clamp(along, 0, 1);
        var a = fromRadius > 1e-9 ? across / fromRadius * toRadius : across;
        return new Point2D(to.From.X + tx * along * tLength - ty * a, to.From.Y + ty * along * tLength + tx * a);
    }

    private static (double, double) SegmentScale(BodyCapsule from, BodyCapsule to)
    {
        var (_, _, fLength) = Axis(from);
        var (_, _, tLength) = Axis(to);
        return (tLength / Math.Max(fLength, 1e-9), AngleOf(to) - AngleOf(from));
    }

    private static Point2D Limb(LimbFrame from, LimbFrame to, Point2D p)
    {
        var upper = Distance(from.Upper, p) <= Distance(from.Lower, p);
        return upper ? Segment(from.Upper, to.Upper, p) : Segment(from.Lower, to.Lower, p);
    }

    private static (double, double) LimbScale(LimbFrame from, LimbFrame to, Point2D centre, bool mirror)
    {
        var t = new Point2D(centre.X / TemplateUnits * (mirror ? -1 : 1), centre.Y / TemplateUnits);
        return Distance(from.Upper, t) <= Distance(from.Lower, t) ? SegmentScale(from.Upper, to.Upper) : SegmentScale(from.Lower, to.Lower);
    }

    private static Point2D Torso(TorsoFrame from, TorsoFrame to, Point2D p)
    {
        var (fTop, fBottom, tTop, tBottom) = (from.Top, from.Bottom, to.Top, to.Bottom);
        var v = (p.Y - fTop) / (fBottom - fTop);
        var (fl, fr) = from.RowAt(p.Y);
        var u = fr - fl > 1e-9 ? (p.X - fl) / (fr - fl) : 0.5;
        var y = tTop + v * (tBottom - tTop);
        var (tl, tr) = to.RowAt(y);
        return to.ToFigure(new Point2D(tl + u * (tr - tl), y));
    }

    private static (double, double) TorsoScale(TorsoFrame from, TorsoFrame to, Point2D centre)
    {
        var v = (centre.Y / TemplateUnits - from.Top) / (from.Bottom - from.Top);
        var y = to.Top + v * (to.Bottom - to.Top);
        return ((to.Bottom - to.Top) / (from.Bottom - from.Top), to.Bend.AngleAt(y));
    }

    private static BodyCapsule Nearest(LimbFrame limb, Point2D p) => Distance(limb.Upper, p) <= Distance(limb.Lower, p) ? limb.Upper : limb.Lower;

    private static SKMatrix SegmentFrame(BodyCapsule c) => StickerCovers.Frame(c.From, AngleOf(c) - 90);

    private static (double X, double Y, double Length) Axis(BodyCapsule c)
    {
        var (dx, dy) = (c.To.X - c.From.X, c.To.Y - c.From.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? (0, 1, 1e-12) : (dx / length, dy / length, length);
    }

    private static double AngleOf(BodyCapsule c) => Math.Atan2(c.To.Y - c.From.Y, c.To.X - c.From.X) * 180 / Math.PI;

    private static double Distance(BodyCapsule c, Point2D p)
    {
        var (dx, dy) = (c.To.X - c.From.X, c.To.Y - c.From.Y);
        var t = Math.Clamp(((p.X - c.From.X) * dx + (p.Y - c.From.Y) * dy) / Math.Max(dx * dx + dy * dy, 1e-12), 0, 1);
        var (x, y) = (c.From.X + dx * t - p.X, c.From.Y + dy * t - p.Y);
        return Math.Sqrt(x * x + y * y);
    }

    private static Point2D Rotate(Point2D v, double degrees)
    {
        var r = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return new Point2D(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}

/// <summary>One element of drawn art, in figure space, ready to paint.</summary>
/// <remarks>An image element (PNG art) is <see cref="Image"/> drawn through <see cref="ImageMatrix"/> (art units to figure space); its <see cref="Path"/> is its outline.</remarks>
internal sealed record ArtStroke(SKPath Path, SKPath? Clip, SKColor? Fill, SKShader? FillShader, SKColor? Stroke, SKShader? StrokeShader,
    float StrokeInk, SKStrokeCap Cap, SKStrokeJoin Join, float[]? Dash, FabricFill? Fabric, bool EvenOdd, SKImage? Image = null, SKMatrix ImageMatrix = default);

/// <summary>
/// A drawn part on the figure: its elements in paint order, each with its own fill and
/// stroke, inside <paramref name="keep"/> (a clip to the body or the sticker) and outside
/// <paramref name="cut"/> (the sticker's cut parts). Line widths are relative to the
/// body's ink (the template's outline is 3 units wide), so they match the body at any
/// size on the page. A readable part (a logo) flips back about its own centre when the
/// placement is mirrored.
/// </summary>
internal sealed class ArtItem(IReadOnlyList<ArtStroke> elements, SKPath area, StickerId owner, SKPoint? readableAnchor, SKPath? keep, SKPath? cut) : FigureItem
{
    /// <summary>The template's own ink width, in template units: a stroke this wide is drawn at the body's ink width.</summary>
    public const float TemplateInk = 3;

    public override SKPath Area { get; } = area;

    public override StickerId? Owner { get; } = owner;

    public override void Draw(SKCanvas canvas, SKPaint ink)
    {
        canvas.Save();
        if (keep != null)
            canvas.ClipPath(keep, antialias: true);
        if (cut is { IsEmpty: false })
            canvas.ClipPath(cut, SKClipOperation.Difference, antialias: true);
        if (readableAnchor is { } anchor && Mirrored(canvas.TotalMatrix))
        {
            canvas.Translate(anchor.X, anchor.Y);
            canvas.Scale(-1, 1);
            canvas.Translate(-anchor.X, -anchor.Y);
        }
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var e in elements)
        {
            canvas.Save();
            if (e.Clip is { } clip)
                canvas.ClipPath(clip, antialias: true);
            if (e.Image is { } image)
            {
                var matrix = e.ImageMatrix;
                canvas.Concat(in matrix);
                canvas.DrawImage(image, SKRect.Create(0, 0, image.Width, image.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            if (e.Fill is { } fill)
            {
                if (e.Fabric is { } fabric)
                {
                    canvas.Save();
                    canvas.ClipPath(e.Path, antialias: true);
                    fabric.Draw(canvas, fill);
                    canvas.Restore();
                }
                else
                {
                    paint.Reset();
                    paint.IsAntialias = true;
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = fill;
                    paint.Shader = e.FillShader;
                    canvas.DrawPath(e.Path, paint);
                }
            }
            if (e.Stroke is { } stroke)
            {
                paint.Reset();
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = stroke;
                paint.Shader = e.StrokeShader;
                paint.StrokeWidth = ink.StrokeWidth * e.StrokeInk;
                paint.StrokeCap = e.Cap;
                paint.StrokeJoin = e.Join;
                if (e.Dash is { } dash)
                    paint.PathEffect = SKPathEffect.CreateDash(dash.Select(d => d * ink.StrokeWidth / TemplateInk).ToArray(), 0);
                canvas.DrawPath(e.Path, paint);
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    internal static bool Mirrored(SKMatrix m) => m.ScaleX * m.ScaleY - m.SkewX * m.SkewY < 0;
}

/// <summary>
/// A typed text part on the figure (docs/sticker-system.md, prints): drawn with per-character
/// font fallback (<see cref="Lettering.DrawFallback"/>) so emoji and symbols the chosen font
/// lacks still show, in colour where the system has a colour emoji font. Vector text
/// throughout, so PDF export stays vector wherever the font itself is one. Its area, for
/// hit-testing and the selection outline, is its bounds rectangle mapped through its
/// placement rather than its glyphs' own shape - cheaper, and close enough to click and
/// highlight (docs/sticker-system.md).
/// </summary>
internal sealed class TextItem(IReadOnlyList<Lettering.TextRun> runs, SKColor color, SKMatrix matrix, SKPath area, StickerId owner, SKPoint? readableAnchor, SKPath? keep, SKPath? cut) : FigureItem
{
    public override SKPath Area { get; } = area;

    public override StickerId? Owner { get; } = owner;

    public override void Draw(SKCanvas canvas, SKPaint ink)
    {
        canvas.Save();
        if (keep != null)
            canvas.ClipPath(keep, antialias: true);
        if (cut is { IsEmpty: false })
            canvas.ClipPath(cut, SKClipOperation.Difference, antialias: true);
        if (readableAnchor is { } anchor && ArtItem.Mirrored(canvas.TotalMatrix))
        {
            canvas.Translate(anchor.X, anchor.Y);
            canvas.Scale(-1, 1);
            canvas.Translate(-anchor.X, -anchor.Y);
        }
        canvas.Concat(in matrix);
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        Lettering.DrawFallback(canvas, runs, 0, 0, paint);
        canvas.Restore();
    }
}

/// <summary>Turns a worn sticker's drawn parts into figure-space art (docs/sticker-system.md §6).</summary>
internal static class StickerArtPieces
{
    /// <summary>
    /// The variant a sticker shows: the pose's expression for its slot if it has that one,
    /// else the style it's worn in (<paramref name="chosen"/>) if it has that one, else
    /// "neutral" if it has one, else its first.
    /// </summary>
    public static string VariantFor(Sticker sticker, string slot, IReadOnlyDictionary<string, string>? expression, string? chosen = null) =>
        sticker.VariantFor(slot, expression, chosen);

    /// <summary>The views to try, nearest first (§6.3): a missing view falls back to the nearest drawn one.</summary>
    public static IEnumerable<ViewAngle> ViewFallback(ViewAngle view) => view switch
    {
        ViewAngle.ThreeQuarter => [ViewAngle.ThreeQuarter, ViewAngle.Front, ViewAngle.Profile],
        ViewAngle.Profile => [ViewAngle.Profile, ViewAngle.ThreeQuarter, ViewAngle.Front],
        _ => [ViewAngle.Front, ViewAngle.ThreeQuarter, ViewAngle.Profile],
    };

    /// <summary>The parsed art a sticker shows for <paramref name="variant"/> and <paramref name="view"/> (falling back as §6.3 says, then to the default variant), or null.</summary>
    public static ParsedArt? ArtFor(StickerAsset asset, string variant, ViewAngle view)
    {
        foreach (var v in new[] { variant }.Concat(asset.Sticker.Variants).Distinct())
        {
            foreach (var angle in ViewFallback(view))
            {
                if (asset.ArtFor(v, angle) is { } file && StickerSvg.ParseAny(file) is { } parsed)
                    return parsed;
            }
        }
        return null;
    }

    /// <summary>The art elements for one drawn part, mapped onto <paramref name="figure"/> and recoloured by <paramref name="look"/>.</summary>
    public static IEnumerable<(LimbSide Side, IReadOnlyList<ArtStroke> Elements, SKPoint? Anchor)> Map(BodyFigure figure, WornSticker worn, StickerPart part, PartArt art,
        ParsedArt parsed, CharacterLook look, double height, Func<string, ArtFile?> tiles)
    {
        var elements = parsed.Part(part.Name);
        // Split eyes (docs/sticker-system.md §21): worn restricted to one side, an element
        // tagged for the other side (class="side-left"/"side-right") doesn't draw at all -
        // worn unrestricted, every element draws regardless of its own side tag, as before.
        if (worn.Side is { } wornSide)
            elements = elements.Where(e => e.Side is null || e.Side == wornSide).ToList();
        if (elements.Count == 0)
            yield break;
        var bounds = parsed.Bounds(elements);
        var centre = new SKPoint(bounds.MidX, bounds.MidY);
        var adjust = SKMatrix.CreateTranslation(-centre.X, -centre.Y)
            .PostConcat(SKMatrix.CreateScale((float)(art.Scale ?? 1), (float)(art.Scale ?? 1)))
            .PostConcat(SKMatrix.CreateRotationDegrees((float)(art.Rotation ?? 0)))
            .PostConcat(SKMatrix.CreateTranslation(centre.X + (float)(art.Offset?.X ?? 0), centre.Y + (float)(art.Offset?.Y ?? 0)));
        var pinned = new Point2D(centre.X + (art.Offset?.X ?? 0), centre.Y + (art.Offset?.Y ?? 0));

        IEnumerable<LimbSide> sides = part.Region is BodyRegion.Arm or BodyRegion.Leg or BodyRegion.Hand or BodyRegion.Foot
            ? part.Side is { } only ? [only] : [LimbSide.Left, LimbSide.Right]
            : [LimbSide.Left];
        // Raster art can't bend: it's always pinned.
        var pin = art.Mapping == ArtMapping.Pin || elements.Any(e => e.Image is not null);
        foreach (var side in sides)
        {
            Func<SKPath, SKPath> place;
            SKPoint? anchor = null;
            var matrix = adjust.PostConcat(RegionMapping.Pin(figure, part.Region, side, part.Side, pinned));
            if (pin)
            {
                place = p => FigureGeometry.Transformed(p, matrix);
                if (art.KeepReadable == true)
                    anchor = matrix.MapPoint(centre.X + (float)(art.Offset?.X ?? 0), centre.Y + (float)(art.Offset?.Y ?? 0));
            }
            else
            {
                var warp = RegionMapping.Warp(figure, part.Region, side, part.Side);
                place = p =>
                {
                    using var adjusted = FigureGeometry.Transformed(p, adjust);
                    return PathMapping.Map(adjusted, warp, pieces: 4);
                };
            }

            var mapped = new List<ArtStroke>();
            foreach (var e in elements)
            {
                // A split eye's own elements still say "slot-eyes" (§21) - drawn from the
                // shared "eyes" colour slot, split into "eyesLeft"/"eyesRight" only once a
                // side is chosen for this worn copy, so splitting never repaints on its own.
                // A hair piece's "slot-hair" is drawn from the piece's own key ("hairFringe",
                // which follows "hair" until it's given its own), a streak's from its own.
                var colorSlot = e.Slot == StickerSlots.Eyes && worn.Side is { } eyeSide
                    ? StickerSlots.SidedSlot(e.Slot, eyeSide)
                    : StickerSlots.ColorKey(worn.Slot, worn.Asset.Id, e.Slot);
                var path = place(e.Path);
                var clip = e.Clip is { } c ? place(c) : null;
                var fill = e.Fill is { } f ? Recolor(f.Color, e.Slot, colorSlot, worn, look) : (SKColor?)null;
                var stroke = e.Stroke is { } s ? Recolor(s.Color, e.Slot, colorSlot, worn, look) : (SKColor?)null;
                var fillShader = e.Fill?.Gradient is { } fg ? Gradient(fg, e.Slot, colorSlot, worn, look, place) : null;
                var strokeShader = e.Stroke?.Gradient is { } sg ? Gradient(sg, e.Slot, colorSlot, worn, look, place) : null;
                FabricFill? fabric = null;
                if (fill is { } ground && colorSlot is { } slot && !e.Solid && look.FabricOf(slot) is { } f2)
                {
                    var b = path.TightBounds;
                    var frame = RegionMapping.FabricFrame(figure, part.Region, side, new Point2D(b.MidX, b.MidY));
                    fabric = new FabricFill([new PartPiece(path, FigureLayerKind.Front, frame)], ground, f2, height, tiles);
                }
                mapped.Add(new ArtStroke(path, clip, fill, fillShader, stroke, strokeShader, e.StrokeWidth / ArtItem.TemplateInk,
                    e.Cap, e.Join, e.Dash, fabric, e.EvenOdd, e.Image, matrix));
            }
            yield return (side, mapped, anchor);
        }
    }

    /// <summary>Template units a print's text sizes at by default - the size slider scales it from there.</summary>
    private const float PrintFontSize = 70;

    /// <summary>How wide a print's text may be before it shrinks, in <see cref="StickerImport.RegionBox"/> sizes (on the torso that box is 45% of the chest, so about 70% of it).</summary>
    private const double PrintFitWidth = 1.55;

    /// <summary>
    /// The text piece(s) a text part paints (docs/sticker-system.md, prints) - one per side
    /// for a part on a limb, like <see cref="Map"/>, but laid out from <paramref name="text"/>
    /// instead of an SVG layer, so a text print needs no art files. Measured once, with
    /// <see cref="Lettering.FallbackRuns"/> (so emoji and symbols the chosen font lacks still
    /// draw), then pinned to the region the same way drawn art is - <paramref name="art"/>'s
    /// offset, scale and rotation apply the same way too.
    /// </summary>
    public static IEnumerable<(LimbSide Side, SKMatrix Matrix, IReadOnlyList<Lettering.TextRun> Runs, SKColor Color, SKRect Bounds, SKPoint? Anchor)> MapText(
        BodyFigure figure, StickerPart part, PartArt art, PartText text, CharacterLook look)
    {
        var font = Lettering.Font(PrintFontSize, text.Bold, false, text.FontFamily);
        using var paint = new SKPaint();
        var runs = Lettering.FallbackRuns(text.Text, font);
        var bounds = Lettering.MeasureFallback(runs, paint);
        var color = FigureGeometry.ToSk(look.Color(text.Color, ColorValue.FromHex("#1a1a1a")));
        var (centreP, size) = StickerImport.RegionBox(figure.Angle, part.Region);
        var centre = new SKPoint((float)centreP.X, (float)centreP.Y);
        // Longer text shrinks to fit across the chest (about 70% of it) before Size scales it.
        var fit = bounds.Width > 0 ? (float)Math.Min(1, size * PrintFitWidth / bounds.Width) : 1f;
        var adjust = SKMatrix.CreateTranslation(-centre.X, -centre.Y)
            .PostConcat(SKMatrix.CreateScale((float)(art.Scale ?? 1), (float)(art.Scale ?? 1)))
            .PostConcat(SKMatrix.CreateRotationDegrees((float)(art.Rotation ?? 0)))
            .PostConcat(SKMatrix.CreateTranslation(centre.X + (float)(art.Offset?.X ?? 0), centre.Y + (float)(art.Offset?.Y ?? 0)));
        var pinned = new Point2D(centre.X + (art.Offset?.X ?? 0), centre.Y + (art.Offset?.Y ?? 0));

        IEnumerable<LimbSide> sides = part.Region is BodyRegion.Arm or BodyRegion.Leg or BodyRegion.Hand or BodyRegion.Foot
            ? part.Side is { } only ? [only] : [LimbSide.Left, LimbSide.Right]
            : [LimbSide.Left];
        foreach (var side in sides)
        {
            var pinMatrix = adjust.PostConcat(RegionMapping.Pin(figure, part.Region, side, part.Side, pinned));
            var matrix = SKMatrix.CreateTranslation(-bounds.MidX, -bounds.MidY)
                .PostConcat(SKMatrix.CreateScale(fit, fit))
                .PostConcat(SKMatrix.CreateTranslation(centre.X, centre.Y))
                .PostConcat(pinMatrix);
            var anchor = art.KeepReadable == true ? pinMatrix.MapPoint((float)pinned.X, (float)pinned.Y) : (SKPoint?)null;
            yield return (side, matrix, runs, color, bounds, anchor);
        }
    }

    /// <summary>
    /// What the elements cover, in figure space: their fills and - at about the body's ink
    /// width for a character <paramref name="height"/> tall - their strokes. For
    /// hit-testing, highlighting and the figure's extent. The caller disposes it.
    /// </summary>
    public static SKPath Area(IReadOnlyList<ArtStroke> elements, double height)
    {
        var area = FigureGeometry.Empty();
        foreach (var e in elements)
        {
            SKPath? shape = null;
            if (e.Fill is not null || e.Image is not null)
                shape = FigureGeometry.Copy(e.Path);
            if (e.Stroke is not null && e.StrokeInk > 0)
            {
                using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(NominalInk * height * e.StrokeInk), StrokeCap = e.Cap, StrokeJoin = e.Join };
                using var builder = new SKPathBuilder();
                paint.GetFillPath(e.Path, builder);
                var stroked = builder.Detach();
                shape = shape is null ? stroked : FigureGeometry.Union(shape, stroked);
            }
            if (shape is null)
                continue;
            if (e.Clip is { } clip)
            {
                var clipped = FigureGeometry.Combine(shape, clip, SKPathOp.Intersect);
                shape.Dispose();
                shape = clipped;
            }
            area = FigureGeometry.Union(area, shape);
        }
        return area;
    }

    /// <summary>About the body's ink width in figure units for a character one unit tall - the template's 3 units in 1000.</summary>
    private const double NominalInk = ArtItem.TemplateInk / RegionMapping.TemplateUnits;

    /// <summary>
    /// <paramref name="color"/>, drawn tagged <paramref name="tagged"/> (its <c>slot-*</c> class),
    /// in the colour the look gives <paramref name="key"/> - the tag itself, or the key it's
    /// coloured under (a split eye's side, a hair piece's own). The artist's shading is kept:
    /// each shade moves by its offset from the sticker's default for the tag.
    /// </summary>
    private static SKColor Recolor(SKColor color, string? tagged, string? key, WornSticker worn, CharacterLook look)
    {
        if (key is null)
            return color;
        if (tagged is null || !worn.Asset.Sticker.Colors.TryGetValue(tagged, out var original))
            return look.Colors.TryGetValue(key, out var only) ? FigureGeometry.ToSk(only).WithAlpha(color.Alpha) : color;
        var now = look.Color(key, original);
        return now == original ? color : ColorMath.Recolor(color, FigureGeometry.ToSk(original), FigureGeometry.ToSk(now));
    }

    private static SKShader Gradient(ArtGradient gradient, string? tagged, string? slot, WornSticker worn, CharacterLook look, Func<SKPath, SKPath> place)
    {
        // Map the gradient's defining points by mapping a tiny path through them.
        using var builder = new SKPathBuilder();
        builder.MoveTo(gradient.Start);
        builder.LineTo(gradient.End);
        if (gradient.Radius is { } r)
            builder.LineTo(gradient.Start.X + r, gradient.Start.Y);
        using var raw = builder.Detach();
        using var mapped = place(raw);
        var points = mapped.Points;
        var colors = gradient.Stops.Select(s => Recolor(s.Color, tagged, slot, worn, look)).ToArray();
        var offsets = gradient.Stops.Select(s => s.Offset).ToArray();
        if (gradient.Radius is not null && points.Length >= 3)
            return SKShader.CreateRadialGradient(points[0], SKPoint.Distance(points[0], points[^1]), colors, offsets, SKShaderTileMode.Clamp);
        return SKShader.CreateLinearGradient(points[0], points.Length > 1 ? points[^1] : points[0], colors, offsets, SKShaderTileMode.Clamp);
    }
}
