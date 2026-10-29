using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.StickerLibrary;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>
/// Drawing a body built from shapes (docs/shape-characters.md §6): shapes filled and inked
/// per colour, seams kept, and sticker parts on regions the figure leaves out skipped. The
/// figures are the mannequin's with layers swapped for hand-made shapes, so these tests
/// don't depend on the shape rig.
/// </summary>
public class ShapeBodyRenderingTests
{
    private const float Unit = 400;
    private static readonly CharacterPlacement Placement = new(new Point2D(200, 420), Unit, false);
    private static readonly SKColor Green = new(0, 255, 0);
    private static readonly SKColor Red = new(255, 0, 0);
    private static readonly SKColor Blue = new(0, 0, 255);
    private const string Shirt = "shirt";

    /// <summary>A mannequin character in green skin, with a red "shirt" colour slot.</summary>
    private static CharacterDefinition Character() => CharacterDefinition.Create("A") with
    {
        ColorSlots = new SortedDictionary<string, ColorValue>
        {
            [CharacterDefinition.SkinSlot] = ColorValue.FromHex("#00ff00"),
            [Shirt] = ColorValue.FromHex("#ff0000"),
        }
    };

    private static BodyFigure Mannequin(CharacterDefinition character, ViewAngle angle = ViewAngle.Front) =>
        BodyRig.Build(character.Body, angle, character.Skeleton);

    /// <summary><paramref name="figure"/> with its <paramref name="kind"/> layer's body swapped for <paramref name="shapes"/> (its seams kept).</summary>
    private static BodyFigure WithShapes(BodyFigure figure, FigureLayerKind kind, params FigureShape[] shapes) =>
        figure with { Layers = figure.Layers.Select(l => l.Kind == kind ? l with { Torso = null, Capsules = [], Ellipses = [], Shapes = shapes } : l).ToList() };

    private static BodyFigure Without(BodyFigure figure, params FigurePart[] absent) => figure with { Absent = absent.ToHashSet() };

    /// <summary>A closed outline with straight sides through <paramref name="points"/>.</summary>
    private static FigureShape Polygon(string slot, params Point2D[] points) =>
        new(points.Select(p => new ShapeAnchor(p, p, p, AnchorHandleKind.Corner)).ToList(), slot);

    private static FigureShape Box(string slot, double left, double top, double right, double bottom) =>
        Polygon(slot, new Point2D(left, top), new Point2D(right, top), new Point2D(right, bottom), new Point2D(left, bottom));

    private static FigureDrawing Draw(CharacterDefinition character, BodyFigure figure, CharacterInstanceOverrides? overrides = null) =>
        FigureRenderer.Build(character, figure, CharacterLooks.Resolve(character, null, overrides), null);

    private static SKBitmap Render(FigureDrawing drawing)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var matrix = FigureRenderer.ToPage(Placement);
        canvas.Concat(in matrix);
        drawing.Draw(canvas, 2f / Unit);
        return bitmap;
    }

    private static (int X, int Y) Pixel(Point2D figure) => ((int)Math.Round(200 + figure.X * Unit), (int)Math.Round(420 + figure.Y * Unit));

    private static SKColor At(SKBitmap bitmap, Point2D figure)
    {
        var (x, y) = Pixel(figure);
        return bitmap.GetPixel(x, y);
    }

    private static bool IsInk(SKColor c) => c.Red < 90 && c.Green < 90 && c.Blue < 90;

    private static bool InkNear(SKBitmap bitmap, Point2D figure, bool across)
    {
        var (x, y) = Pixel(figure);
        return Enumerable.Range(-3, 7).Any(d => IsInk(across ? bitmap.GetPixel(x + d, y) : bitmap.GetPixel(x, y + d)));
    }

    /// <summary>Everything the figure's other layers cover - to check a probe sees only the layer under test.</summary>
    private static bool OnOtherLayers(BodyFigure figure, FigureLayerKind kind, Point2D p)
    {
        foreach (var layer in figure.Layers.Where(l => l.Kind != kind && l.HasBody))
        {
            using var skin = FigureGeometry.LayerSkin(layer);
            if (skin.Contains((float)p.X, (float)p.Y))
                return true;
        }
        return false;
    }

    /// <summary>The upright torso's height range and its half width at the chest.</summary>
    private static (double Top, double Height, double HalfWidth) Chest(BodyFigure figure)
    {
        var torso = figure.Regions.Torso;
        var height = torso.Bottom - torso.Top;
        var (left, right) = torso.RowAt(torso.Top + 0.2 * height);
        return (torso.Top, height, (right - left) / 2);
    }

    /// <summary>The mannequin's torso swapped for an upside-down triangle in <paramref name="slot"/>; also its middle and a point the old torso covered but the triangle doesn't.</summary>
    private static (BodyFigure Figure, Point2D Inside, Point2D Outside) Triangle(CharacterDefinition character, string slot)
    {
        var mannequin = Mannequin(character);
        var (top, height, half) = Chest(mannequin);
        var triangle = Polygon(slot,
            new Point2D(-0.8 * half, top + 0.05 * height),
            new Point2D(0.8 * half, top + 0.05 * height),
            new Point2D(0, top + 0.9 * height));
        var inside = new Point2D(0, top + (0.05 + 0.05 + 0.9) / 3 * height);
        var outside = new Point2D(0.75 * half, top + 0.35 * height);
        using var oldTorso = FigureGeometry.SmoothClosed(mannequin.Regions.Torso.RestOutline);
        Assert.True(oldTorso.Contains((float)outside.X, (float)outside.Y), "the probe is on the mannequin's torso");
        Assert.False(OnOtherLayers(mannequin, FigureLayerKind.Torso, outside), "nothing else covers the probe");
        return (WithShapes(mannequin, FigureLayerKind.Torso, triangle), inside, outside);
    }

    [Fact]
    public void A_triangle_torso_is_filled_with_the_skin_colour_and_only_where_it_is()
    {
        var character = Character();
        var (figure, inside, outside) = Triangle(character, CharacterDefinition.SkinSlot);

        using var bitmap = Render(Draw(character, figure));

        Assert.Equal(Green, At(bitmap, inside));
        Assert.Equal(SKColors.White, At(bitmap, outside));
        var (top, height, _) = Chest(figure);
        Assert.True(InkNear(bitmap, new Point2D(0, top + 0.05 * height), across: false), "the triangle's top edge is inked");
    }

    [Fact]
    public void A_point_inside_a_shape_hits_the_figure_and_one_only_the_old_torso_covered_does_not()
    {
        var character = Character();
        var (figure, inside, outside) = Triangle(character, Shirt);

        var drawing = Draw(character, figure);

        Assert.True(drawing.Outline.Contains((float)inside.X, (float)inside.Y));
        Assert.False(drawing.Outline.Contains((float)outside.X, (float)outside.Y));
        Assert.Null(drawing.StickerAt(inside)); // bare body, not a sticker
    }

    [Fact]
    public void A_shape_in_its_own_colour_slot_draws_in_that_colour_and_follows_a_panel_override()
    {
        var character = Character();
        var (figure, inside, _) = Triangle(character, Shirt);
        var blue = new CharacterInstanceOverrides(null, new SortedDictionary<string, ColorValue> { [Shirt] = ColorValue.FromHex("#0000ff") });

        using var own = Render(Draw(character, figure));
        using var overridden = Render(Draw(character, figure, blue));

        Assert.Equal(Red, At(own, inside));
        Assert.Equal(Blue, At(overridden, inside));
    }

    [Fact]
    public void A_shape_in_a_slot_the_character_never_set_is_the_skin_colour()
    {
        var character = Character();
        var (figure, inside, _) = Triangle(character, "shell");

        using var bitmap = Render(Draw(character, figure));

        Assert.Equal(Green, At(bitmap, inside));
    }

    /// <summary>The torso swapped for two boxes side by side, overlapping a little at x = 0.</summary>
    private static (BodyFigure Figure, Point2D Left, Point2D Join, Point2D Right) TwoBoxes(CharacterDefinition character, string leftSlot, string rightSlot)
    {
        var mannequin = Mannequin(character);
        var (top, height, half) = Chest(mannequin);
        var (y0, y1, w) = (top + 0.15 * height, top + 0.55 * height, 0.6 * half);
        var figure = WithShapes(mannequin, FigureLayerKind.Torso,
            Box(leftSlot, -w, y0, 0.005, y1),
            Box(rightSlot, -0.005, y0, w, y1));
        var middle = (y0 + y1) / 2;
        var join = new Point2D(0, middle);
        Assert.False(OnOtherLayers(mannequin, FigureLayerKind.Torso, join));
        return (figure, new Point2D(-w / 2, middle), join, new Point2D(w / 2, middle));
    }

    [Fact]
    public void Two_colours_in_one_layer_are_parted_by_an_ink_line()
    {
        var character = Character();
        var (figure, left, join, right) = TwoBoxes(character, CharacterDefinition.SkinSlot, Shirt);

        using var bitmap = Render(Draw(character, figure));

        Assert.Equal(Green, At(bitmap, left));
        Assert.Equal(Red, At(bitmap, right));
        Assert.True(InkNear(bitmap, join, across: true), "a line where the colours meet");
    }

    [Fact]
    public void Two_shapes_of_one_colour_in_a_layer_merge_with_no_line_between_them()
    {
        var character = Character();
        var (figure, left, join, right) = TwoBoxes(character, Shirt, Shirt);

        using var bitmap = Render(Draw(character, figure));

        Assert.Equal(Red, At(bitmap, left));
        Assert.Equal(Red, At(bitmap, right));
        Assert.False(InkNear(bitmap, join, across: true), "one shape, inked round its union");
    }

    [Theory]
    [InlineData(CharacterDefinition.SkinSlot)]
    [InlineData(Shirt)]
    public void A_square_head_has_no_ink_inside_its_neck_seam_but_keeps_its_edge_outside_it(string slot)
    {
        var character = Character();
        var mannequin = Mannequin(character);
        var headLayer = mannequin.Layers.Single(l => l.Kind == FigureLayerKind.Head);
        var seam = Assert.Single(headLayer.Seams);
        var head = mannequin.Regions.Head;
        // The square's bottom edge runs through the seam's centre, over the neck's end.
        var half = Math.Max(head.RadiusX, 2.5 * seam.RadiusX);
        var bottom = seam.Center.Y;
        var figure = WithShapes(mannequin, FigureLayerKind.Head, Box(slot, seam.Center.X - half, bottom - 2 * half, seam.Center.X + half, bottom));
        var inSeam = new Point2D(seam.Center.X, bottom);
        var outsideSeam = new Point2D(seam.Center.X + 1.8 * seam.RadiusX, bottom);
        using (var neck = FigureGeometry.LayerSkin(mannequin.Layers.Single(l => l.Kind == FigureLayerKind.Torso)))
        {
            Assert.True(neck.Contains((float)inSeam.X, (float)inSeam.Y), "the seam's centre is over the neck");
            Assert.False(neck.Contains((float)outsideSeam.X, (float)outsideSeam.Y + 0.004f), "beside the seam the edge is over nothing");
        }

        using var bitmap = Render(Draw(character, figure));

        Assert.False(InkNear(bitmap, inSeam, across: false), "the joint stays seamless");
        Assert.True(InkNear(bitmap, outsideSeam, across: false), "the edge is inked away from the joint");
    }

    // Absent regions: stickers on a part the figure doesn't have.

    private static (CharacterDefinition Character, StickerId Id) Wearing(CharacterDefinition character, IReadOnlyDictionary<string, ArtFile> files, string slot, params StickerPart[] parts)
    {
        var sticker = new Sticker(StickerId.New(), "Sticker", slot, parts,
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff"), ["print"] = ColorValue.FromHex("#0000ff") }, ["default"]);
        character = character with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [slot] = [sticker.Id] } };
        return (character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, files)) }, sticker.Id);
    }

    private static (CharacterDefinition Character, StickerId Id) WearingCover(params StickerPart[] parts) =>
        Wearing(Character(), new Dictionary<string, ArtFile>(), StickerSlots.Top, parts);

    private static Point2D ChestPoint(BodyFigure figure) => figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));

    private static Point2D Along(BodyCapsule c, double t) => new(c.From.X + (c.To.X - c.From.X) * t, c.From.Y + (c.To.Y - c.From.Y) * t);

    [Fact]
    public void A_cover_on_an_absent_torso_draws_nothing_and_cannot_be_picked()
    {
        var (character, id) = WearingCover(new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var figure = Mannequin(character);
        var chest = ChestPoint(figure);

        var dressed = Draw(character, figure);
        var absent = Draw(character, Without(figure, new FigurePart(BodyRegion.Torso)));
        using var dressedBitmap = Render(dressed);
        using var absentBitmap = Render(absent);
        using var outline = absent.OutlineOf(id);

        Assert.Equal(Blue, At(dressedBitmap, chest));
        Assert.Equal(Green, At(absentBitmap, chest));
        Assert.False(absent.HasStickers);
        Assert.Null(absent.StickerAt(chest));
        Assert.True(outline.IsEmpty);
    }

    [Fact]
    public void Sleeves_are_drawn_only_on_the_arm_the_figure_has()
    {
        var (character, id) = WearingCover(new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 1)));
        var figure = Without(Mannequin(character), new FigurePart(BodyRegion.Arm, LimbSide.Left));
        var left = Along(figure.Regions.LeftArm.Upper, 0.5);
        var right = Along(figure.Regions.RightArm.Upper, 0.5);

        var drawing = Draw(character, figure);
        using var bitmap = Render(drawing);

        Assert.Equal(Green, At(bitmap, left));
        Assert.Equal(Blue, At(bitmap, right));
        Assert.Null(drawing.StickerAt(left));
        Assert.Equal(id, drawing.StickerAt(right));
    }

    [Fact]
    public void Drawn_art_on_an_absent_head_draws_nothing()
    {
        const string eyes = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150"><circle cx="-21" cy="-930" r="5" fill="#000"/><circle cx="21" cy="-930" r="5" fill="#000"/></svg>""";
        var (character, id) = Wearing(Character(), new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(eyes) }, StickerSlots.Eyes,
            new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin)));
        var figure = Mannequin(character);

        using var present = Draw(character, figure).OutlineOf(id);
        var drawing = Draw(character, Without(figure, new FigurePart(BodyRegion.Head)));
        using var absent = drawing.OutlineOf(id);

        Assert.False(present.IsEmpty);
        Assert.True(absent.IsEmpty);
        Assert.False(drawing.HasStickers);
    }

    [Fact]
    public void A_text_print_on_an_absent_torso_draws_nothing()
    {
        var (character, id) = Wearing(Character(), new Dictionary<string, ArtFile>(), StickerSlots.Print,
            new StickerPart("print", BodyRegion.Torso, Art: new PartArt(ArtMapping.Pin), Text: new PartText("HI")));
        var figure = Mannequin(character);

        using var present = Draw(character, figure).OutlineOf(id);
        var drawing = Draw(character, Without(figure, new FigurePart(BodyRegion.Torso)));
        using var absent = drawing.OutlineOf(id);

        Assert.False(present.IsEmpty);
        Assert.True(absent.IsEmpty);
    }

    // The mannequin is unchanged.

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_mannequin_draws_through_the_rig_seam_exactly_as_from_its_generated_body(ViewAngle angle)
    {
        var (character, _) = WearingCover(
            new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.8)),
            new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 0.4)));
        var pose = new PoseData(angle, [new BoneRotation(HumanoidBone.LeftLowerArm, 80), new BoneRotation(HumanoidBone.RightUpperLeg, -20)], []);

        using var throughRig = new SKBitmap(400, 440);
        using (var canvas = new SKCanvas(throughRig))
        {
            canvas.Clear(SKColors.White);
            new FigureRenderer().Draw(canvas, character, Placement, 2f, angle, pose);
        }
        using var direct = Render(FigureRenderer.Build(character, BodyRig.Build(character.Body, angle, character.Skeleton, pose), CharacterLooks.Resolve(character), pose));

        Assert.Equal(direct.Pixels, throughRig.Pixels);
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_mannequin_layer_is_one_skin_fill(ViewAngle angle)
    {
        foreach (var layer in Mannequin(Character(), angle).Layers)
        {
            var fills = FigureGeometry.LayerFills(layer);
            try
            {
                Assert.Equal(layer.HasBody ? [CharacterDefinition.SkinSlot] : [], fills.Select(f => f.Slot));
            }
            finally
            {
                foreach (var (_, path) in fills)
                    path.Dispose();
            }
        }
    }

    /// <summary>
    /// Skia's union of a stick figure's legs with its thin, slightly bent body came back as the
    /// body alone, and the 3 x 3 sample grid that guards a union missed it - every point fell
    /// between the thin legs - so walking side on drew the stick figure with no legs.
    /// </summary>
    [Fact]
    public void A_thin_limb_unioned_with_a_thin_body_is_never_dropped()
    {
        var character = CharacterDefinition.Create("Stick") with { Modules = CreaturePresets.Get("Stick figure")!.Body };
        // Walk, side on, as the pose gallery gives it.
        var pose = new PoseData(ViewAngle.Profile,
        [
            new(HumanoidBone.Spine, 3), new(HumanoidBone.LeftUpperArm, -1.1), new(HumanoidBone.LeftLowerArm, -26.16),
            new(HumanoidBone.RightUpperArm, 35.14), new(HumanoidBone.RightLowerArm, -24.52),
            new(HumanoidBone.LeftUpperLeg, 8.48), new(HumanoidBone.LeftLowerLeg, 15.89),
            new(HumanoidBone.RightUpperLeg, -22.49), new(HumanoidBone.RightLowerLeg, 9.94),
        ], [], HipsShift: new Point2D(0, 0.012));
        var figure = CharacterRig.Build(character, ViewAngle.Profile, pose);
        var body = figure.Layers.Single(l => l.Kind == FigureLayerKind.Body);

        using var skin = FigureGeometry.LayerSkin(body);

        Assert.All(body.Capsules, c =>
        {
            var middle = Along(c, 0.5);
            Assert.True(skin.Contains((float)middle.X, (float)middle.Y), $"the leg bone through {middle} is drawn");
        });
    }
}
