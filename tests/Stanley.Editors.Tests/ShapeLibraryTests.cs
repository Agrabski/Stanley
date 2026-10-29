using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;
using Stanley.StickerLibrary;
using Library = Stanley.StickerLibrary.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>
/// The starter shape library and creature presets (docs/shape-characters.md §8): every shape
/// loads as a closed outline in its unit box, under the key its folder names; every kind has
/// shapes in each style; every preset is built from library shapes only and holds together;
/// and the sticker library still loads beside them.
/// </summary>
public sealed class ShapeLibraryTests
{
    /// <summary>How many of each kind ship, at least, and which styles each must have.</summary>
    private static readonly IReadOnlyDictionary<string, int> Planned = new Dictionary<string, int>
    {
        [ShapeLibrary.Head] = 8,
        [ShapeLibrary.Torso] = 8,
        [ShapeLibrary.Hand] = 5,
        [ShapeLibrary.Foot] = 5,
        [ShapeLibrary.Piece] = 3
    };

    public static TheoryData<string> ShapeKeys() => [.. ShapeLibrary.All.Select(s => s.Key)];

    public static TheoryData<string> PresetNames() => [.. CreaturePresets.All.Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(ShapeKeys))]
    public void Every_shape_is_a_closed_outline_filling_its_box(string key)
    {
        var shape = ShapeLibrary.Find(key)!;
        var anchors = shape.Shape.Anchors;

        Assert.True(anchors.Count >= 3, $"{key} has {anchors.Count} points");
        Assert.All(anchors, a => Assert.True(Finite(a.Point) && Finite(a.InHandle) && Finite(a.OutHandle), $"{key} has a point that isn't a number"));
        Assert.Null(shape.Shape.Source);

        var bounds = AnchorRing.CurveBounds(anchors);
        const double tolerance = 1e-3;
        Assert.True(bounds.Left >= -tolerance && bounds.Top >= -tolerance && bounds.Right <= 1 + tolerance && bounds.Bottom <= 1 + tolerance,
            $"{key} spills out of its unit box: {bounds}");
        if (shape.Kind is ShapeLibrary.Hand or ShapeLibrary.Foot)
        {
            // An end keeps its own proportions: its long side spans the box, it hangs from the top centre.
            Assert.True(Math.Abs(Math.Max(bounds.Width, bounds.Height) - 1) < 0.01, $"{key}: {bounds}");
            Assert.True(Math.Abs(bounds.Top) < 0.01, $"{key} doesn't start at the wrist or ankle: {bounds}");
            Assert.True(Math.Abs(bounds.Left + bounds.Width / 2 - 0.5) < 0.01, $"{key} isn't centred: {bounds}");
        }
        else
        {
            Assert.True(bounds.Width > 0.99 && bounds.Height > 0.99, $"{key} doesn't fill its unit box: {bounds}");
        }
        Assert.True(Area(anchors) > 0.05, $"{key} is all but empty");
        Assert.Null(ModuleEditing.SwapShape(new ModularBody(new ShapeModule(shape.Instantiate(), 0.3, 0.3), null, null, null), ModulePart.Head, shape.Instantiate()).Error);
    }

    [Theory]
    [MemberData(nameof(ShapeKeys))]
    public void Every_shape_is_filed_under_its_kind_and_style_and_has_a_name(string key)
    {
        var shape = ShapeLibrary.Find(key)!;
        var parts = key.Split('/');
        Assert.Equal(3, parts.Length);
        Assert.Contains(shape.Kind, ShapeLibrary.Kinds);
        Assert.Contains(shape.Category, ShapeLibrary.Categories);
        Assert.Matches("^[a-z]+(-[a-z]+)*$", shape.Name);
        Assert.False(string.IsNullOrWhiteSpace(shape.DisplayName));
        Assert.Equal($"library:shapes/{key}", shape.Source);
    }

    [Fact]
    public void A_copy_carries_its_source_and_owns_its_points()
    {
        var shape = ShapeLibrary.Find("head/organic/egg")!;
        var copy = shape.Instantiate();

        Assert.Equal("library:shapes/head/organic/egg", copy.Source);
        Assert.Equal(shape.Shape.Anchors, copy.Anchors);
        Assert.NotSame(shape.Shape.Anchors, copy.Anchors);
        Assert.Same(shape, ShapeLibrary.SourceOf(copy));
        Assert.Null(ShapeLibrary.SourceOf(copy with { Source = null }));
        Assert.Null(ShapeLibrary.SourceOf(copy with { Source = "library:hair/bob" }));

        // A copy is written in the project format and reads back the same.
        var json = ProjectJson.Serialize(copy);
        var read = ProjectJson.Deserialize<ModuleShape>(json);
        Assert.Equal(copy.Source, read.Source);
        Assert.Equal(copy.Anchors, read.Anchors);
    }

    [Fact]
    public void Every_kind_has_its_planned_shapes_in_every_style()
    {
        foreach (var (kind, count) in Planned)
        {
            Assert.True(ShapeLibrary.ForKind(kind).Count() >= count, $"{kind}: only {ShapeLibrary.ForKind(kind).Count()} shapes");
            foreach (var category in ShapeLibrary.Categories)
                Assert.True(ShapeLibrary.ForKind(kind, category).Any(), $"no {category} {kind}");
        }
        Assert.Equal(ShapeLibrary.All.Count, ShapeLibrary.Kinds.Sum(k => ShapeLibrary.ForKind(k).Count()));
        Assert.Equal(ShapeLibrary.All.Count, ShapeLibrary.All.Select(s => s.Key).Distinct().Count());
    }

    [Fact]
    public void Display_names_read_as_words() =>
        Assert.Equal("Rounded square", ShapeLibrary.DisplayName("rounded-square"));

    [Fact]
    public void Each_part_and_limb_piece_has_a_kind_of_shape()
    {
        Assert.Equal(ShapeLibrary.Head, ShapeLibrary.KindFor(ModulePart.Head));
        Assert.Equal(ShapeLibrary.Torso, ShapeLibrary.KindFor(ModulePart.Torso));
        Assert.Null(ShapeLibrary.KindFor(ModulePart.Arms));
        Assert.Equal(ShapeLibrary.Hand, ShapeLibrary.KindFor(ModulePart.Arms, LimbPiece.End));
        Assert.Equal(ShapeLibrary.Foot, ShapeLibrary.KindFor(ModulePart.Legs, LimbPiece.End));
        Assert.Equal(ShapeLibrary.Piece, ShapeLibrary.KindFor(ModulePart.Legs, LimbPiece.Upper));
    }

    [Theory]
    [MemberData(nameof(PresetNames))]
    public void Every_preset_is_built_from_library_shapes_and_holds_together(string name)
    {
        var body = CreaturePresets.Get(name)!.Body;
        AssertFromLibrary(name, body);
        Assert.Null(ModuleEditing.Validate(body));
        Assert.True(ModuleEditing.ApplyPreset(body).IsValid);
    }

    [Fact]
    public void The_defaults_are_built_from_library_shapes_and_put_every_part_back()
    {
        var body = CreaturePresets.Default;
        AssertFromLibrary("Default", body);
        Assert.Null(ModuleEditing.Validate(body));
        Assert.All(ModuleParts.All, p => Assert.True(ModuleEditing.Has(body, p)));

        foreach (var part in ModuleParts.All)
        {
            var without = ModuleEditing.Remove(body, part).Value;
            var back = part.IsShape()
                ? ModuleEditing.Add(without, part, CreaturePresets.DefaultShape(part))
                : ModuleEditing.Add(without, part, CreaturePresets.DefaultLimbs(part));
            Assert.True(back.IsValid, back.Error);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => CreaturePresets.DefaultShape(ModulePart.Arms));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreaturePresets.DefaultLimbs(ModulePart.Head));
    }

    [Fact]
    public void Presets_have_their_own_names_and_hand_out_fresh_copies()
    {
        var names = CreaturePresets.All.Select(p => p.Name).ToList();
        Assert.True(names.Count >= 6);
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.NotSame(CreaturePresets.Get("Bean")!.Body.Torso!.Shape.Anchors, CreaturePresets.Get("Bean")!.Body.Torso!.Shape.Anchors);
        Assert.Null(CreaturePresets.Get("No such creature"));
    }

    [Fact]
    public void Every_sticker_still_loads_beside_the_shapes()
    {
        Assert.NotEmpty(Library.All);
        Assert.DoesNotContain(Library.All, s => s.Key.StartsWith("shapes/", StringComparison.Ordinal));
        Assert.All(Library.All, s => Assert.False(string.IsNullOrEmpty(s.Slot), $"{s.Key} has no slot"));
        Assert.NotEmpty(Library.PatternTiles);
    }

    private static void AssertFromLibrary(string name, ModularBody body)
    {
        var shapes = new List<ModuleShape?> { body.Head?.Shape, body.Torso?.Shape, body.Arms?.End, body.Arms?.Upper, body.Arms?.Lower, body.Legs?.End, body.Legs?.Upper, body.Legs?.Lower };
        foreach (var shape in shapes.OfType<ModuleShape>())
        {
            var source = ShapeLibrary.SourceOf(shape);
            Assert.True(source is not null, $"{name} has a shape that isn't from the library ({shape.Source})");
            Assert.Equal(source!.Shape.Anchors, shape.Anchors);
        }
        foreach (var limbs in new[] { body.Arms, body.Legs }.OfType<LimbModule>())
        {
            Assert.True(limbs.End is null || ShapeLibrary.SourceOf(limbs.End)!.Kind is ShapeLibrary.Hand or ShapeLibrary.Foot, $"{name}: a limb ends in something that isn't a hand or a foot");
            Assert.True(limbs.Upper is null || ShapeLibrary.SourceOf(limbs.Upper)!.Kind == ShapeLibrary.Piece, $"{name}: an upper piece that isn't a piece");
        }
        Assert.True(body.Arms?.End is null || ShapeLibrary.SourceOf(body.Arms.End)!.Kind == ShapeLibrary.Hand, $"{name}: arms end in a foot");
        Assert.True(body.Legs?.End is null || ShapeLibrary.SourceOf(body.Legs.End)!.Kind == ShapeLibrary.Foot, $"{name}: legs end in a hand");
    }

    private static bool Finite(Point2D p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    /// <summary>The outline's area by the shoelace formula over sampled points.</summary>
    private static double Area(IReadOnlyList<ShapeAnchor> anchors)
    {
        const int samples = 400;
        var area = 0.0;
        var prev = AnchorRing.PointAt(anchors, 0);
        for (var i = 1; i <= samples; i++)
        {
            var p = AnchorRing.PointAt(anchors, (double)i / samples);
            area += prev.X * p.Y - p.X * prev.Y;
            prev = p;
        }
        return Math.Abs(area) / 2;
    }
}
