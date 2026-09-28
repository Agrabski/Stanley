using System.Globalization;
using System.Text;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>A layer a template offers for a slot: the part it becomes, where it goes, and how it's mapped.</summary>
public sealed record TemplatePart(string Name, BodyRegion Region, ArtMapping Mapping, PartDepth? Depth = null, PartClip? Clip = null)
{
    /// <summary>The sticker part this layer becomes.</summary>
    public StickerPart ToPart() => new(Name, Region, Art: new PartArt(Mapping), Depth: Depth, Clip: Clip);
}

/// <summary>
/// Templates to draw sticker art on (docs/sticker-system.md §6.2): the default body seen
/// from one view, 1000 units tall with the origin at the ground between its feet, as a
/// locked, faded guide layer; an empty named layer per part the slot usually has; a view
/// box cropped to the slot's region; and the view and slot on the root, so the file
/// imports without questions.
/// </summary>
public static class StickerTemplates
{
    /// <summary>The parts a new drawn sticker for <paramref name="slot"/> starts with - one template layer each.</summary>
    public static IReadOnlyList<TemplatePart> PartsFor(string slot) => slot switch
    {
        StickerSlots.Hair or StickerSlots.Headwear or StickerSlots.HairExtras =>
        [
            new("back", BodyRegion.Head, ArtMapping.Warp, PartDepth.Back),
            new("front", BodyRegion.Head, ArtMapping.Warp),
        ],
        // Hair pieces (docs: modular hair): the back hangs behind everything, the rest over the face.
        StickerSlots.HairTop or StickerSlots.HairFringe or StickerSlots.HairSides => [new("front", BodyRegion.Head, ArtMapping.Warp)],
        StickerSlots.HairBack => [new("back", BodyRegion.Head, ArtMapping.Warp, PartDepth.Back)],
        StickerSlots.HairStreaks => [new("streak", BodyRegion.Head, ArtMapping.Warp, Clip: PartClip.Hair)],
        StickerSlots.Eyes or StickerSlots.Brows or StickerSlots.Mouth or StickerSlots.Nose or StickerSlots.Glasses =>
            [new(slot, BodyRegion.Head, ArtMapping.Pin)],
        StickerSlots.FacialHair => [new("beard", BodyRegion.Head, ArtMapping.Warp)],
        StickerSlots.Top or StickerSlots.Outer =>
        [
            new("body", BodyRegion.Torso, ArtMapping.Warp),
            new("sleeves", BodyRegion.Arm, ArtMapping.Warp),
        ],
        StickerSlots.Bottom =>
        [
            new("waist", BodyRegion.Torso, ArtMapping.Warp),
            new("legs", BodyRegion.Leg, ArtMapping.Warp),
        ],
        StickerSlots.Shoes => [new("shoes", BodyRegion.Foot, ArtMapping.Warp)],
        _ => [new("art", BodyRegion.Torso, ArtMapping.Pin)],
    };

    /// <summary>The template SVG for drawing <paramref name="slot"/> art seen from <paramref name="view"/>.</summary>
    public static string Export(ViewAngle view, string slot)
    {
        var template = RegionMapping.Template(view);
        var scale = SKMatrix.CreateScale((float)RegionMapping.TemplateUnits, (float)RegionMapping.TemplateUnits);
        var box = ViewBox(template, StickerSlots.Get(slot).Region, slot);
        var skin = FigureGeometry.ToSk(CharacterDefinition.DefaultSkin);

        var svg = new StringBuilder();
        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\"");
        svg.Append($" width=\"{N(box.Width)}\" height=\"{N(box.Height)}\" viewBox=\"{N(box.Left)} {N(box.Top)} {N(box.Width)} {N(box.Height)}\"");
        svg.Append($" data-stanley-view=\"{StickerAsset.ViewFileStem(view)}\" data-stanley-slot=\"{slot}\">\n");
        svg.Append("  <!-- Draw on the layers below; the template layer is ignored on import. Tag colours with class=\"slot-<name>\". A 3-unit line matches the body's outline. -->\n");
        svg.Append("  <g id=\"template\" inkscape:groupmode=\"layer\" inkscape:label=\"template\" sodipodi:insensitive=\"true\" data-stanley-guide=\"true\" opacity=\"0.4\">\n");
        foreach (var layer in template.Layers.Where(l => l.HasBody))
        {
            using var path = FigureGeometry.LayerSkin(layer);
            using var scaled = FigureGeometry.Transformed(path, scale);
            svg.Append($"    <path d=\"{scaled.ToSvgPathData()}\" fill=\"#{skin.Red:x2}{skin.Green:x2}{skin.Blue:x2}\" stroke=\"#000000\" stroke-width=\"3\" stroke-linejoin=\"round\"/>\n");
        }
        svg.Append("  </g>\n");
        if (StickerSlots.IsHair(slot))
        {
            // Where hair pieces meet: draw to these and any piece fits any other.
            svg.Append("  <g id=\"joins\" inkscape:groupmode=\"layer\" inkscape:label=\"joins\" sodipodi:insensitive=\"true\" data-stanley-guide=\"true\" opacity=\"0.6\">\n");
            foreach (var join in HairJoins.For(view))
            {
                var points = string.Join(" ", join.Points.Select(p => $"{N((float)p.X)},{N((float)p.Y)}"));
                svg.Append($"    <polyline id=\"join-{join.Name}\" points=\"{points}\" fill=\"none\" stroke=\"#d0308a\" stroke-width=\"1\" stroke-dasharray=\"4 3\"/>\n");
            }
            svg.Append("  </g>\n");
        }
        foreach (var part in PartsFor(slot))
            svg.Append($"  <g id=\"{part.Name}\" inkscape:groupmode=\"layer\" inkscape:label=\"{part.Name}\"/>\n");
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    /// <summary>The part of the template worth seeing for a slot: the head and shoulders for hair and faces, the upper body for tops, the feet for shoes.</summary>
    private static SKRect ViewBox(BodyFigure template, BodyRegion region, string slot)
    {
        var u = (float)RegionMapping.TemplateUnits;
        var head = template.Regions.Head;
        SKRect box = slot switch
        {
            StickerSlots.Shoes => Around(template.Regions.Foot(LimbSide.Left), template.Regions.Foot(LimbSide.Right)),
            StickerSlots.Bottom => SKRect.Create(-0.2f, -0.6f, 0.4f, 0.62f),
            _ when region == BodyRegion.Head => SKRect.Create((float)(head.Center.X - 2.4 * head.RadiusY), (float)(head.Center.Y - 1.5 * head.RadiusY),
                (float)(4.8 * head.RadiusY), (float)(4.2 * head.RadiusY)),
            StickerSlots.Top or StickerSlots.Outer => SKRect.Create(-0.25f, -0.9f, 0.5f, 0.5f),
            _ => SKRect.Create(-0.35f, -1.05f, 0.7f, 1.1f),
        };
        return new SKRect(MathF.Round(box.Left * u), MathF.Round(box.Top * u), MathF.Round(box.Right * u), MathF.Round(box.Bottom * u));

        static SKRect Around(BodyEllipse a, BodyEllipse b)
        {
            var r = (float)Math.Max(Math.Max(a.RadiusX, a.RadiusY), Math.Max(b.RadiusX, b.RadiusY)) * 2;
            return new SKRect((float)Math.Min(a.Center.X, b.Center.X) - r, (float)Math.Min(a.Center.Y, b.Center.Y) - r,
                (float)Math.Max(a.Center.X, b.Center.X) + r, (float)Math.Max(a.Center.Y, b.Center.Y) + r * 0.6f);
        }
    }

    private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
