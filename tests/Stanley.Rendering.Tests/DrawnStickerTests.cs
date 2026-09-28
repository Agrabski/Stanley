using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>The SVG adapter: layers become parts, classes become colour slots, and what isn't drawn is reported.</summary>
public class StickerSvgTests
{
    /// <summary>An SVG around the default head, with <paramref name="body"/> inside.</summary>
    private static string Doc(string body, string attributes = "") =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="-100 -1050 200 200" {attributes}>{body}</svg>""";

    [Fact]
    public void Top_level_layers_are_parts_and_the_template_guide_is_left_out()
    {
        var art = StickerSvg.Parse(Doc("""
              <g inkscape:label="template" data-stanley-guide="true"><circle cx="0" cy="-933" r="60" fill="#eee"/></g>
              <g inkscape:label="back"><rect x="-80" y="-1000" width="160" height="120" fill="#5a3a22" class="slot-hair"/></g>
              <g inkscape:label="front" class="slot-hair">
                <path d="M-60,-990 L60,-990 L60,-960 L-60,-960 Z" fill="#5a3a22" stroke="#111111" stroke-width="3"/>
                <path d="M-10,-980 L10,-980" fill="none" stroke="#3b2414" stroke-width="1.5" class="solid"/>
              </g>
            """, """data-stanley-view="front" data-stanley-slot="hair" """));

        Assert.Equal(["back", "front"], art.Layers.Keys.Order());
        Assert.Equal("front", art.View);
        Assert.Equal("hair", art.Slot);
        var front = art.Part("front");
        Assert.Equal(3, front.Count); // fill, stroke, and the strand's stroke
        Assert.All(front, e => Assert.Equal("hair", e.Slot)); // inherited from the layer
        Assert.True(front[2].Solid);
        Assert.Equal(3, front[1].StrokeWidth);
        Assert.Equal(new SKRect(-60, -990, 60, -960), art.Bounds([front[0]]));
        Assert.Empty(art.Part("missing"));
        Assert.Empty(art.Report);
    }

    [Fact]
    public void A_file_without_layers_is_the_whole_of_any_part()
    {
        var art = StickerSvg.Parse(Doc("""<circle cx="0" cy="-930" r="5" fill="#000"/><circle cx="20" cy="-930" r="5" fill="#000"/>"""));

        Assert.Equal(2, art.Part("eyes").Count);
    }

    [Fact]
    public void Transforms_are_applied_and_scale_line_width()
    {
        var art = StickerSvg.Parse(Doc("""<g transform="translate(10,0) scale(2)"><path d="M0,-500 L10,-500" fill="none" stroke="#000" stroke-width="1"/></g>"""));

        var line = Assert.Single(art.Part(""));
        Assert.Equal(2, line.StrokeWidth, 3);
        Assert.Equal(10, line.Path.TightBounds.Left, 3);
        Assert.Equal(30, line.Path.TightBounds.Right, 3);
        Assert.Equal(-1000, line.Path.TightBounds.Top, 3);
    }

    [Fact]
    public void Ellipses_keep_their_line_width_and_a_clip_path_of_any_shape_clips()
    {
        var art = StickerSvg.Parse(Doc("""
              <clipPath id="c"><circle cx="0" cy="-930" r="10"/></clipPath>
              <ellipse cx="0" cy="-930" rx="5" ry="15" fill="none" stroke="#000" stroke-width="3"/>
              <rect x="-50" y="-980" width="100" height="100" fill="#f00" clip-path="url(#c)"/>
            """));

        var elements = art.Part("");
        Assert.Equal(3, elements[0].StrokeWidth, 3);
        var clipped = elements.Single(e => e.Fill?.Color == new SKColor(255, 0, 0));
        Assert.NotNull(clipped.Clip);
        Assert.Equal(new SKRect(-10, -940, 10, -920), clipped.Clip!.TightBounds);
    }

    [Fact]
    public void Gradients_and_the_inkscape_svg_prefix_are_read()
    {
        var art = StickerSvg.Parse("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:svg="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <defs><linearGradient id="g" x1="0" y1="0" x2="100" y2="0" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient></defs>
              <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
            </svg>
            """);

        var gradient = Assert.Single(art.Part("")).Fill?.Gradient;
        Assert.NotNull(gradient);
        Assert.Equal(2, gradient!.Stops.Count);
    }

    [Fact]
    public void Filters_masks_and_images_are_reported_not_dropped_silently()
    {
        var art = StickerSvg.Parse(Doc("""
              <defs><filter id="blur"><feGaussianBlur stdDeviation="2"/></filter><mask id="m"><rect width="10" height="10" fill="#fff"/></mask></defs>
              <circle cx="0" cy="-930" r="20" fill="#000" filter="url(#blur)"/>
              <circle cx="0" cy="-930" r="10" fill="#000" mask="url(#m)"/>
              <image x="0" y="-930" width="1" height="1" href="data:image/png;base64,iVBORw0KGgo="/>
            """));

        Assert.Contains(art.Report, r => r.Contains("filter", StringComparison.Ordinal));
        Assert.Contains(art.Report, r => r.Contains("mask", StringComparison.Ordinal));
        Assert.Contains(art.Report, r => r.Contains("image", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_text_parses_to_nothing_instead_of_failing()
    {
        Assert.Null(StickerSvg.Parse(ArtFile.Svg("<svg")));
        Assert.Null(StickerSvg.Parse(ArtFile.Png([1, 2, 3])));
    }

    [Fact]
    public void A_template_names_its_view_and_slot_and_offers_the_slot_parts_as_empty_layers()
    {
        var text = StickerTemplates.Export(ViewAngle.Profile, StickerSlots.Hair);
        var art = StickerSvg.Parse(text);

        Assert.Equal("profile", art.View);
        Assert.Equal(StickerSlots.Hair, art.Slot);
        Assert.Equal(StickerTemplates.PartsFor(StickerSlots.Hair).Select(p => p.Name).Order(), art.Layers.Keys.Order());
        Assert.All(art.Layers.Values, Assert.Empty); // the guide body isn't art
        // Cropped to the head: the default head (top at -1000) is inside, the feet aren't.
        Assert.True(art.ViewBox.Top < -1000 && art.ViewBox.Bottom < -500, art.ViewBox.ToString());
        Assert.Contains("data-stanley-guide", text, StringComparison.Ordinal);
    }
}

/// <summary>Recolouring keeps an artist's shading: offsets from the default colour carry over to the new one.</summary>
public class ColorMathTests
{
    private static double Lightness(SKColor c) => 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;

    [Fact]
    public void The_default_itself_becomes_the_new_colour_and_a_darker_shade_stays_darker()
    {
        var brown = new SKColor(0x5a, 0x3a, 0x22);
        var shadow = new SKColor(0x3b, 0x24, 0x14);
        var blonde = new SKColor(0xe8, 0xc8, 0x72);

        var main = ColorMath.Recolor(brown, brown, blonde);
        var dark = ColorMath.Recolor(shadow, brown, blonde);

        Assert.True(Math.Abs(main.Red - blonde.Red) <= 2 && Math.Abs(main.Green - blonde.Green) <= 2 && Math.Abs(main.Blue - blonde.Blue) <= 2, main.ToString());
        Assert.True(Lightness(dark) < Lightness(main) - 10);
        Assert.True(dark.Red > dark.Blue); // blonde's warm hue, not grey
    }

    [Fact]
    public void Ink_and_highlights_are_not_shades_of_the_colour()
    {
        var brown = new SKColor(0x5a, 0x3a, 0x22);
        var ink = new SKColor(0x1a, 0x1a, 0x1a);

        Assert.Equal(ink, ColorMath.Recolor(ink, brown, new SKColor(0xe8, 0xc8, 0x72)));
        Assert.Equal(SKColors.White, ColorMath.Recolor(SKColors.White, brown, new SKColor(0x20, 0x40, 0xff)));
    }
}

/// <summary>SVG pattern tiles: one repeat, recoloured by class to the slot's colours.</summary>
public class PatternTileTests
{
    private const string Tile = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <rect class="slot-ground" width="100" height="100" fill="#2e86c1"/>
          <rect class="slot-1" x="0" y="0" width="50" height="100" fill="#f4f4f4"/>
          <rect x="50" y="50" width="50" height="50" fill="#000000"/>
        </svg>
        """;

    private static SKColor Sample(SKPicture picture, float u, float v)
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(100);
            canvas.DrawPicture(picture);
        }
        return bitmap.GetPixel((int)(u * 100), (int)(v * 100));
    }

    [Fact]
    public void Ground_and_pattern_classes_take_the_slot_colours_and_the_rest_keeps_its_own()
    {
        var file = ArtFile.Svg(Tile);
        var red = new SKColor(0xc0, 0x39, 0x2b);
        var yellow = new SKColor(0xf1, 0xc4, 0x0f);

        var plain = ArtPictures.PatternTile(file, red, [])!;
        Assert.Equal(red, Sample(plain, 0.75f, 0.25f));                     // the ground is the garment
        Assert.Equal(new SKColor(0xf4, 0xf4, 0xf4), Sample(plain, 0.25f, 0.5f)); // no pattern colour yet: as drawn
        Assert.Equal(SKColors.Black, Sample(plain, 0.75f, 0.75f));

        var coloured = ArtPictures.PatternTile(file, red, [yellow])!;
        var motif = Sample(coloured, 0.25f, 0.5f);
        Assert.True(Math.Abs(motif.Red - yellow.Red) + Math.Abs(motif.Green - yellow.Green) + Math.Abs(motif.Blue - yellow.Blue) < 8, motif.ToString());
        Assert.Same(coloured, ArtPictures.PatternTile(file, red, [yellow])); // cached per colours
    }
}

/// <summary>Template space onto a character: Warp hugs the region, Pin keeps the art's shape.</summary>
public class RegionMappingTests
{
    [Fact]
    public void The_template_maps_onto_the_default_body_as_itself()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Front);
        foreach (var region in new[] { BodyRegion.Head, BodyRegion.Torso, BodyRegion.Arm, BodyRegion.Leg })
        {
            var warp = RegionMapping.Warp(figure, region, LimbSide.Right, null);
            var p = warp(new Point2D(-150, -600));
            Assert.Equal(-0.15, p.X, 6);
            Assert.Equal(-0.6, p.Y, 6);
        }
    }

    [Fact]
    public void Warp_takes_the_template_head_outline_onto_the_character_head_outline()
    {
        var template = RegionMapping.Template(ViewAngle.Front).Regions.Head;
        var chibi = BodyRig.Build(BodyPresets.Shape(BodyPreset.Chibi), ViewAngle.Front);
        var head = chibi.Regions.Head;
        var warp = RegionMapping.Warp(chibi, BodyRegion.Head, LimbSide.Left, null);
        for (var a = 0.0; a < 2 * Math.PI; a += 0.4)
        {
            var onTemplate = new Point2D((template.Center.X + template.RadiusX * Math.Cos(a)) * 1000, (template.Center.Y + template.RadiusY * Math.Sin(a)) * 1000);
            var p = warp(onTemplate);
            var (u, v) = ((p.X - head.Center.X) / head.RadiusX, (p.Y - head.Center.Y) / head.RadiusY);
            Assert.Equal(1, u * u + v * v, 6);
        }
    }

    [Fact]
    public void Pin_keeps_the_art_shape_and_scales_it_with_the_head()
    {
        var chibi = BodyRig.Build(BodyPresets.Shape(BodyPreset.Chibi), ViewAngle.Front);
        var ratio = chibi.Regions.Head.RadiusY / RegionMapping.Template(ViewAngle.Front).Regions.Head.RadiusY;
        var pin = RegionMapping.Pin(chibi, BodyRegion.Head, LimbSide.Left, null, new Point2D(0, -930));

        var a = pin.MapPoint(-21, -930);
        var b = pin.MapPoint(21, -930);
        var c = pin.MapPoint(0, -900);

        Assert.Equal(42 / 1000.0 * ratio, SKPoint.Distance(a, b), 5);
        Assert.Equal(Math.Sqrt(21 * 21 + 30 * 30) / 1000 * ratio, SKPoint.Distance(a, c), 5);
    }

    [Fact]
    public void Art_drawn_on_the_template_right_arm_is_mirrored_onto_the_left_arm_in_front()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Front);
        var rightElbow = figure.Regions.Arm(LimbSide.Right).Upper.To;
        var onLeft = RegionMapping.Warp(figure, BodyRegion.Arm, LimbSide.Left, null)(new Point2D(rightElbow.X * 1000, rightElbow.Y * 1000));

        var leftElbow = figure.Regions.Arm(LimbSide.Left).Upper.To;
        Assert.Equal(leftElbow.X, onLeft.X, 6);
        Assert.Equal(leftElbow.Y, onLeft.Y, 6);
    }
}

/// <summary>Which variant a worn sticker shows for an expression.</summary>
public class VariantTests
{
    private static Sticker Eyes(params string[] variants) =>
        new(StickerId.New(), "Eyes", StickerSlots.Eyes, [], new SortedDictionary<string, ColorValue>(), variants);

    [Fact]
    public void The_expression_picks_the_variant_and_a_missing_one_falls_back_to_neutral_then_the_first()
    {
        var wink = new Dictionary<string, string> { [StickerSlots.Eyes] = "wink" };

        Assert.Equal("wink", StickerArtPieces.VariantFor(Eyes("neutral", "wink"), StickerSlots.Eyes, wink));
        Assert.Equal("neutral", StickerArtPieces.VariantFor(Eyes("happy", "neutral"), StickerSlots.Eyes, wink));
        Assert.Equal("happy", StickerArtPieces.VariantFor(Eyes("happy", "sad"), StickerSlots.Eyes, wink));
        Assert.Equal("neutral", StickerArtPieces.VariantFor(Eyes("happy", "neutral"), StickerSlots.Eyes, null));
    }

    [Fact]
    public void A_face_shows_the_pose_expression()
    {
        const string neutral = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150"><rect x="-30" y="-935" width="60" height="10" fill="#000"/></svg>""";
        const string closed = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150"><rect x="-30" y="-935" width="60" height="2" fill="#000"/></svg>""";
        var sticker = Eyes("neutral", "closed") with { Parts = [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))] };
        var character = CharacterDefinition.Create("A") with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [sticker.Id] } };
        character = character with
        {
            Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>
            {
                ["variants/neutral/front.svg"] = ArtFile.Svg(neutral),
                ["variants/closed/front.svg"] = ArtFile.Svg(closed),
            }))
        };
        var renderer = (FigureRenderer)CharacterRenderers.Default;

        using var open = renderer.Drawing(character, ViewAngle.Front, new PoseData(ViewAngle.Front, [], [])).OutlineOf(sticker.Id);
        using var shut = renderer.Drawing(character, ViewAngle.Front, new PoseData(ViewAngle.Front, [], new SortedDictionary<string, string> { [StickerSlots.Eyes] = "closed" })).OutlineOf(sticker.Id);

        Assert.Equal(0.010, open.TightBounds.Height, 4);
        Assert.Equal(0.002, shut.TightBounds.Height, 4);
    }
}

/// <summary>Drawn parts on the figure: layered, recoloured, hit-testable, falling back across views.</summary>
public class DrawnStickerRenderingTests
{
    private static readonly SKColor Brown = new(0x5a, 0x3a, 0x22);

    /// <summary>Hair: a cap above the head ("front") and a block behind the neck ("back", pulled to the back).</summary>
    private const string HairSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="-150 -1060 300 300">
          <g inkscape:label="back"><rect class="slot-hair" x="-75" y="-900" width="150" height="80" fill="#5a3a22"/></g>
          <g inkscape:label="front"><rect class="slot-hair" x="-50" y="-1030" width="100" height="40" fill="#5a3a22" stroke="#1a1a1a" stroke-width="3"/></g>
        </svg>
        """;

    private static (CharacterDefinition Character, StickerId Id) Wearing(IReadOnlyDictionary<string, ArtFile> files, params StickerPart[] parts)
    {
        var sticker = new Sticker(StickerId.New(), "Hair", StickerSlots.Hair, parts, new SortedDictionary<string, ColorValue> { ["hair"] = ColorValue.FromHex("#5a3a22") }, ["default"]);
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Hair] = [sticker.Id] },
        };
        return (character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, files)) }, sticker.Id);
    }

    private static (CharacterDefinition Character, StickerId Id) WearingHair(params ViewAngle[] views) =>
        Wearing(views.ToDictionary(v => $"variants/default/{StickerAsset.ViewFileStem(v)}.svg", _ => ArtFile.Svg(HairSvg)),
            new StickerPart("back", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp), Depth: PartDepth.Back),
            new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp)));

    private static SKBitmap Render(CharacterDefinition character, PoseData pose, float stroke = 2)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), stroke, pose.ViewAngle, pose);
        return bitmap;
    }

    private static SKColor At(SKBitmap bitmap, double x, double y) => bitmap.GetPixel((int)Math.Round(200 + x / 1000 * 400), (int)Math.Round(420 + y / 1000 * 400));

    private static bool Near(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) < 30;

    [Fact]
    public void Drawn_hair_paints_over_the_head_and_its_back_part_behind_the_body()
    {
        var (character, _) = WearingHair(ViewAngle.Front);
        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []));
        var skin = FigureGeometry.ToSk(character.Skin);

        Assert.True(Near(Brown, At(bitmap, 0, -1015)), "the cap above the head");
        Assert.True(Near(Brown, At(bitmap, -65, -850)), "the back part beside the neck");
        Assert.True(Near(skin, At(bitmap, 0, -850)), "the neck in front of the back part");
    }

    [Fact]
    public void Drawn_art_recolours_with_its_slot_but_its_ink_stays_ink()
    {
        var (character, _) = WearingHair(ViewAngle.Front);
        character = character with { ColorSlots = new SortedDictionary<string, ColorValue>(character.ColorSlots) { ["hair"] = ColorValue.FromHex("#e8c872") } };
        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []), stroke: 4);

        Assert.True(Near(new SKColor(0xe8, 0xc8, 0x72), At(bitmap, 0, -1010)));
        var edge = At(bitmap, 0, -1030);
        Assert.True(edge.Red < 80 && edge.Green < 80 && edge.Blue < 80, edge.ToString());
    }

    [Fact]
    public void A_missing_view_falls_back_to_the_nearest_drawn_one()
    {
        var (character, _) = WearingHair(ViewAngle.Front);
        using var bitmap = Render(character, new PoseData(ViewAngle.Profile, [], []));
        var head = BodyRig.Build(character.Body, ViewAngle.Profile).Regions.Head;

        Assert.True(Near(Brown, At(bitmap, head.Center.X * 1000, -1015)));
    }

    [Fact]
    public void Clicking_drawn_art_finds_its_sticker_and_the_extent_includes_it()
    {
        var (character, id) = WearingHair(ViewAngle.Front);
        var placement = new CharacterPlacement(new Point2D(0, 0), 1, false);

        Assert.Equal(id, CharacterRenderers.Default.StickerAt(character, placement, new Point2D(0, -1.015)));
        Assert.True(CharacterRenderers.Default.Extent(character).Top < -1.02);
        using var outline = CharacterRenderers.Default.BuildStickerOutline(character, placement, id);
        Assert.True(outline.Contains(0, -1.01f));
    }

    [Fact]
    public void Line_width_is_relative_to_the_body_ink()
    {
        const string lines = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="-150 -1060 300 300">
              <path d="M-40,-1030 L40,-1030" fill="none" stroke="#000000" stroke-width="3"/>
              <path d="M-40,-950 L40,-950" fill="none" stroke="#000000" stroke-width="1.5"/>
            </svg>
            """;
        var (character, _) = Wearing(new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(lines) },
            new StickerPart("lines", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin)));
        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []), stroke: 6);

        int Thickness(double templateY)
        {
            var x = 200;
            var y0 = (int)Math.Round(420 + templateY / 1000 * 400);
            return Enumerable.Range(y0 - 8, 17).Count(y => bitmap.GetPixel(x, y).Red < 128);
        }
        Assert.InRange(Thickness(-1030), 5, 7); // the body's ink: 6 px
        Assert.InRange(Thickness(-950), 2, 4); // half of it
    }

    [Fact]
    public void Pinned_art_turns_with_the_head()
    {
        const string eyes = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150"><circle cx="-21" cy="-930" r="5" fill="#000"/><circle cx="21" cy="-930" r="5" fill="#000"/></svg>""";
        var (character, id) = Wearing(new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(eyes) },
            new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin)));
        var tilted = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.Head, 30)], []);
        var drawing = ((FigureRenderer)CharacterRenderers.Default).Drawing(character, ViewAngle.Front, tilted);

        using var outline = drawing.OutlineOf(id);
        var head = BodyRig.Build(character.Body, ViewAngle.Front, null, tilted).Regions.Head;
        var expected = RegionMapping.Warp(BodyRig.Build(character.Body, ViewAngle.Front, null, tilted), BodyRegion.Head, LimbSide.Left, null)(new Point2D(0, -930));
        Assert.NotEqual(0, head.RotationDegrees);
        Assert.Equal(expected.X, outline.TightBounds.MidX, 3);
        Assert.Equal(expected.Y, outline.TightBounds.MidY, 3);
        Assert.True(outline.TightBounds.Height > 0.012, "the pair of eyes turned: one sits higher than the other");
    }

    [Fact]
    public void A_cut_part_opens_the_garment_underneath()
    {
        // A top covering the torso, with a V cut out at the neck (drawn art, Warp).
        const string vee = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-200 -900 400 400"><path d="M-40,-830 L40,-830 L0,-730 Z" fill="#000"/></svg>""";
        var sticker = new Sticker(StickerId.New(), "V-neck", StickerSlots.Top,
            [
                new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.9)),
                new StickerPart("neck", BodyRegion.Torso, Art: new PartArt(ArtMapping.Warp), Blend: PartBlend.Cut),
            ],
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff") }, ["default"]);
        var character = CharacterDefinition.Create("A") with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [sticker.Id] } };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(vee) })) };
        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []));

        Assert.True(Near(FigureGeometry.ToSk(character.Skin), At(bitmap, 0, -780)), "skin shows through the V");
        Assert.True(Near(new SKColor(0, 0, 255), At(bitmap, 60, -700)), "the top elsewhere");
    }
}
