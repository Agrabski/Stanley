using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Building a body from shapes (docs/shape-characters.md §9.2): every <see cref="ModuleEditing"/> operation, on hand-made shapes.</summary>
public sealed class ModuleEditingTests
{
    private static ModuleShape Box(string? source = "library:shapes/head/geometric/box") =>
        new(AnchorRing.Rectangle(new Rect2D(0, 0, 1, 1)), Source: source);

    private static ModuleShape Oval(string? source = "library:shapes/head/organic/oval") =>
        new(AnchorRing.Ellipse(new Rect2D(0, 0, 1, 1)), Source: source);

    private static ModularBody Body() => new(
        Head: new ShapeModule(Oval(), 0.3, 0.28, Rotation: 10, FlipX: true, Offset: new Point2D(0.01, -0.02), Color: "fur"),
        Torso: new ShapeModule(Box("library:shapes/torso/geometric/box"), 0.32, 0.34),
        Arms: new LimbModule(LimbStyle.Tube, 0.26, 0.06, End: Oval("library:shapes/hand/organic/oval"), EndSize: 0.08),
        Legs: new LimbModule(LimbStyle.Taper, 0.24, 0.08, Taper: 0.4));

    private static ModularBody Ok(Stanley.Editing.Abstractions.EditResult<ModularBody> result)
    {
        Assert.True(result.IsValid, result.Error);
        return result.Value;
    }

    // ---------------------------------------------------------------- swapping

    [Fact]
    public void Swapping_a_head_changes_only_its_outline()
    {
        var body = Body();
        var box = Box("library:shapes/head/geometric/box");

        var head = Ok(ModuleEditing.SwapShape(body, ModulePart.Head, box)).Head!;

        Assert.Same(box, head.Shape);
        Assert.Equal("library:shapes/head/geometric/box", head.Shape.Source);
        Assert.Equal((0.3, 0.28), (head.Width, head.Height));
        Assert.Equal(10, head.Rotation);
        Assert.True(head.FlipX);
        Assert.Equal(new Point2D(0.01, -0.02), head.Offset);
        Assert.Equal("fur", head.Color);
    }

    [Fact]
    public void Swapping_a_missing_part_or_a_bad_shape_is_refused()
    {
        var noHead = Body() with { Head = null };
        Assert.False(ModuleEditing.SwapShape(noHead, ModulePart.Head, Box()).IsValid);
        Assert.False(ModuleEditing.SwapShape(Body(), ModulePart.Arms, Box()).IsValid);

        var line = new ModuleShape([AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(1, 1))]);
        var result = ModuleEditing.SwapShape(Body(), ModulePart.Torso, line);
        Assert.False(result.IsValid);
        Assert.Equal("A shape needs at least three points.", result.Error);

        var flat = new ModuleShape([AnchorRing.Corner(new Point2D(0, 0.5)), AnchorRing.Corner(new Point2D(0.5, 0.5)), AnchorRing.Corner(new Point2D(1, 0.5))]);
        Assert.False(ModuleEditing.SwapShape(Body(), ModulePart.Torso, flat).IsValid);

        var nan = new ModuleShape([AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(double.NaN, 0)), AnchorRing.Corner(new Point2D(1, 1))]);
        Assert.False(ModuleEditing.SwapShape(Body(), ModulePart.Torso, nan).IsValid);
    }

    [Fact]
    public void Swapping_a_hand_keeps_the_arms_and_null_puts_the_round_end_back()
    {
        var body = Body();
        var mitten = Box("library:shapes/hand/organic/mitten");

        var arms = Ok(ModuleEditing.SwapShape(body, ModulePart.Arms, LimbPiece.End, mitten)).Arms!;
        Assert.Same(mitten, arms.End);
        Assert.Equal((LimbStyle.Tube, 0.26, 0.06, 0.08), (arms.Style, arms.Length, arms.Thickness, arms.EndSize));

        Assert.Null(Ok(ModuleEditing.SwapShape(body, ModulePart.Arms, LimbPiece.End, null)).Arms!.End);
    }

    [Fact]
    public void Swapping_in_a_piece_makes_the_limb_pieces()
    {
        var piece = Box("library:shapes/piece/geometric/block");

        var legs = Ok(ModuleEditing.SwapShape(Body(), ModulePart.Legs, LimbPiece.Upper, piece)).Legs!;
        Assert.Equal(LimbStyle.Pieces, legs.Style);
        Assert.Same(piece, legs.Upper);
        Assert.Null(legs.Lower);

        legs = Ok(ModuleEditing.SwapShape(Body(), ModulePart.Legs, LimbPiece.Lower, piece)).Legs!;
        Assert.Same(piece, legs.Lower);
        Assert.Equal(LimbStyle.Pieces, legs.Style);
    }

    // ---------------------------------------------------------------- the character's own shapes

    [Fact]
    public void Moving_a_shapes_points_makes_it_the_characters_own()
    {
        var body = Body();
        var moved = body.Head!.Shape.Anchors.Select((a, i) => i == 0 ? a with { Point = new Point2D(a.Point.X, a.Point.Y - 0.1) } : a).ToList();

        var head = Ok(ModuleEditing.SetAnchors(body, ModulePart.Head, moved)).Head!;

        Assert.Null(head.Shape.Source);
        Assert.Equal(moved, head.Shape.Anchors);
        Assert.Equal(body.Head.Width, head.Width);
        Assert.Equal("library:shapes/head/organic/oval", body.Head.Shape.Source); // the original is untouched
    }

    [Fact]
    public void Moving_a_hands_points_drops_its_source_and_a_capsule_has_none_to_move()
    {
        var body = Body();
        var anchors = AnchorRing.Rectangle(new Rect2D(0.1, 0, 0.8, 1));

        var arms = Ok(ModuleEditing.SetAnchors(body, ModulePart.Arms, LimbPiece.End, anchors)).Arms!;
        Assert.Null(arms.End!.Source);
        Assert.Equal(anchors, arms.End.Anchors);

        Assert.False(ModuleEditing.SetAnchors(body, ModulePart.Arms, LimbPiece.Upper, anchors).IsValid);
        Assert.False(ModuleEditing.SetAnchors(body, ModulePart.Legs, LimbPiece.End, anchors).IsValid);
    }

    [Fact]
    public void A_socket_placed_by_hand_is_kept_and_drops_the_source_and_null_takes_it_back()
    {
        var body = Ok(ModuleEditing.SetSocket(Body(), ModulePart.Torso, ModuleSockets.LeftShoulder, new Point2D(0.1, 0.2)));

        var shape = body.Torso!.Shape;
        Assert.Null(shape.Source);
        Assert.Equal(new Point2D(0.1, 0.2), shape.Sockets![ModuleSockets.LeftShoulder]);

        var far = Ok(ModuleEditing.SetSocket(body, ModulePart.Torso, ModuleSockets.Neck, new Point2D(5, -5))).Torso!.Shape.Sockets!;
        Assert.Equal(new Point2D(1 + ModuleEditing.SocketMargin, -ModuleEditing.SocketMargin), far[ModuleSockets.Neck]);

        var back = Ok(ModuleEditing.SetSocket(body, ModulePart.Torso, ModuleSockets.LeftShoulder, null)).Torso!.Shape;
        Assert.Null(back.Sockets);
    }

    [Fact]
    public void Unknown_sockets_and_limb_sockets_are_refused()
    {
        Assert.False(ModuleEditing.SetSocket(Body(), ModulePart.Torso, "elbow", new Point2D(0.5, 0.5)).IsValid);
        Assert.False(ModuleEditing.SetSocket(Body(), ModulePart.Arms, ModuleSockets.Neck, new Point2D(0.5, 0.5)).IsValid);
        Assert.False(ModuleEditing.SetSocket(Body(), ModulePart.Head, ModuleSockets.Neck, new Point2D(double.NaN, 0)).IsValid);
    }

    // ---------------------------------------------------------------- size, turn, flip, place, colour

    [Fact]
    public void Resizing_holds_to_the_range_and_refuses_nothing_sizes()
    {
        var head = Ok(ModuleEditing.Resize(Body(), ModulePart.Head, 0.5, 9)).Head!;
        Assert.Equal((0.5, ModuleEditing.MaxSize), (head.Width, head.Height));
        Assert.Equal("library:shapes/head/organic/oval", head.Shape.Source); // a size isn't an outline change

        Assert.Equal(ModuleEditing.MinSize, Ok(ModuleEditing.Resize(Body(), ModulePart.Torso, 0.0001, 0.3)).Torso!.Width);
        Assert.False(ModuleEditing.Resize(Body(), ModulePart.Head, 0, 0.3).IsValid);
        Assert.False(ModuleEditing.Resize(Body(), ModulePart.Head, -1, 0.3).IsValid);
        Assert.False(ModuleEditing.Resize(Body(), ModulePart.Head, double.NaN, 0.3).IsValid);
        Assert.False(ModuleEditing.Resize(Body(), ModulePart.Arms, 0.3, 0.3).IsValid);
    }

    [Theory]
    [InlineData(45, 45)]
    [InlineData(370, 10)]
    [InlineData(-190, 170)]
    [InlineData(180, -180)]
    public void Turning_is_kept_between_minus_180_and_180(double degrees, double expected) =>
        Assert.Equal(expected, Ok(ModuleEditing.SetRotation(Body(), ModulePart.Torso, degrees)).Torso!.Rotation!.Value, 9);

    [Fact]
    public void Turning_back_to_zero_clears_it_and_limbs_dont_turn()
    {
        Assert.Null(Ok(ModuleEditing.SetRotation(Body(), ModulePart.Head, 360)).Head!.Rotation);
        Assert.False(ModuleEditing.SetRotation(Body(), ModulePart.Legs, 30).IsValid);
        Assert.False(ModuleEditing.SetRotation(Body(), ModulePart.Head, double.PositiveInfinity).IsValid);
    }

    [Fact]
    public void Flipping_toggles_and_back_is_unset()
    {
        var body = Body();
        var once = Ok(ModuleEditing.FlipVertically(body, ModulePart.Torso));
        Assert.True(once.Torso!.FlipY);
        Assert.Null(Ok(ModuleEditing.FlipVertically(once, ModulePart.Torso)).Torso!.FlipY);

        Assert.Null(Ok(ModuleEditing.FlipHorizontally(body, ModulePart.Head)).Head!.FlipX); // it started flipped
        Assert.True(Ok(ModuleEditing.FlipHorizontally(body, ModulePart.Torso)).Torso!.FlipX);
        Assert.False(ModuleEditing.FlipHorizontally(body, ModulePart.Arms).IsValid);
    }

    [Fact]
    public void Nudging_adds_up_is_held_in_range_and_zero_is_back_on_the_socket()
    {
        var body = Ok(ModuleEditing.Nudge(Body(), ModulePart.Head, 0.02, 0.01));
        var offset = body.Head!.Offset!.Value;
        Assert.Equal(0.03, offset.X, 9);
        Assert.Equal(-0.01, offset.Y, 9);

        Assert.Equal(new Point2D(ModuleEditing.MaxOffset, -ModuleEditing.MaxOffset), Ok(ModuleEditing.SetOffset(body, ModulePart.Head, new Point2D(7, -7))).Head!.Offset);
        Assert.Null(Ok(ModuleEditing.SetOffset(body, ModulePart.Head, new Point2D(0, 0))).Head!.Offset);
        Assert.Null(Ok(ModuleEditing.SetOffset(body, ModulePart.Head, null)).Head!.Offset);
        Assert.Equal(new Point2D(0.05, 0), Ok(ModuleEditing.Nudge(Body(), ModulePart.Torso, 0.05, 0)).Torso!.Offset);
        Assert.False(ModuleEditing.Nudge(Body(), ModulePart.Legs, 0.05, 0).IsValid);
    }

    [Fact]
    public void Colour_slots_go_on_any_part_and_skin_is_unset()
    {
        Assert.Equal("shirt", Ok(ModuleEditing.SetColor(Body(), ModulePart.Torso, "shirt")).Torso!.Color);
        Assert.Equal("shirt", Ok(ModuleEditing.SetColor(Body(), ModulePart.Arms, "shirt")).Arms!.Color);
        Assert.Null(Ok(ModuleEditing.SetColor(Body(), ModulePart.Head, CharacterDefinition.SkinSlot)).Head!.Color);
        Assert.Null(Ok(ModuleEditing.SetColor(Body(), ModulePart.Head, null)).Head!.Color);
        Assert.False(ModuleEditing.SetColor(Body(), ModulePart.Head, " ").IsValid);
        Assert.False(ModuleEditing.SetColor(Body() with { Legs = null }, ModulePart.Legs, "shirt").IsValid);
    }

    // ---------------------------------------------------------------- taking parts off and putting them back

    [Fact]
    public void Removing_and_adding_back_round_trips()
    {
        var body = Body();
        foreach (var part in ModuleParts.All)
        {
            var without = Ok(ModuleEditing.Remove(body, part));
            Assert.False(ModuleEditing.Has(without, part));
            Assert.All(ModuleParts.All.Where(p => p != part), p => Assert.True(ModuleEditing.Has(without, p)));
            Assert.False(ModuleEditing.Remove(without, part).IsValid);

            var back = part.IsShape()
                ? Ok(ModuleEditing.Add(without, part, ModuleEditing.ShapeOf(body, part)!))
                : Ok(ModuleEditing.Add(without, part, ModuleEditing.LimbOf(body, part)!));
            Assert.Equal(body, back);
        }
    }

    [Fact]
    public void A_head_or_a_torso_always_stays()
    {
        var headOnly = Ok(ModuleEditing.Remove(Body(), ModulePart.Torso));
        var result = ModuleEditing.Remove(headOnly, ModulePart.Head);
        Assert.False(result.IsValid);
        Assert.Equal("A character needs a head or a torso to hold the rest together.", result.Error);

        var torsoOnly = Ok(ModuleEditing.Remove(Body(), ModulePart.Head));
        Assert.False(ModuleEditing.Remove(torsoOnly, ModulePart.Torso).IsValid);
    }

    [Fact]
    public void Adding_a_part_already_there_or_of_the_wrong_kind_is_refused()
    {
        var body = Body();
        Assert.False(ModuleEditing.Add(body, ModulePart.Head, body.Torso!).IsValid);
        Assert.False(ModuleEditing.Add(body, ModulePart.Arms, body.Legs!).IsValid);

        var bare = body with { Head = null, Arms = null };
        Assert.False(ModuleEditing.Add(bare, ModulePart.Arms, body.Torso!).IsValid);
        Assert.False(ModuleEditing.Add(bare, ModulePart.Head, body.Arms!).IsValid);
        Assert.False(ModuleEditing.Add(bare, ModulePart.Head, body.Head! with { Width = 0 }).IsValid);
        Assert.False(ModuleEditing.Add(bare, ModulePart.Arms, body.Arms! with { Length = -0.1 }).IsValid);
    }

    // ---------------------------------------------------------------- limbs

    [Fact]
    public void Limb_settings_are_held_to_their_ranges()
    {
        var body = Body();
        Assert.Equal(LimbStyle.Stick, Ok(ModuleEditing.SetLimbStyle(body, ModulePart.Arms, LimbStyle.Stick)).Arms!.Style);
        Assert.Equal(0.4, Ok(ModuleEditing.SetLength(body, ModulePart.Legs, 0.4)).Legs!.Length);
        Assert.Equal(ModuleEditing.MaxLength, Ok(ModuleEditing.SetLength(body, ModulePart.Legs, 99)).Legs!.Length);
        Assert.Equal(ModuleEditing.MinThickness, Ok(ModuleEditing.SetThickness(body, ModulePart.Arms, 1e-6)).Arms!.Thickness);
        Assert.Equal(ModuleEditing.MaxTaper, Ok(ModuleEditing.SetTaper(body, ModulePart.Legs, 2)).Legs!.Taper);
        Assert.Equal(0, Ok(ModuleEditing.SetTaper(body, ModulePart.Legs, -1)).Legs!.Taper);
        Assert.Equal(ModuleEditing.MinBend, Ok(ModuleEditing.SetBend(body, ModulePart.Arms, 0)).Arms!.Bend);
        Assert.Equal(0.6, Ok(ModuleEditing.SetBend(body, ModulePart.Arms, 0.6)).Arms!.Bend);
        Assert.Equal(0.12, Ok(ModuleEditing.SetEndSize(body, ModulePart.Arms, 0.12)).Arms!.EndSize);
        Assert.Null(Ok(ModuleEditing.SetEndSize(body, ModulePart.Arms, null)).Arms!.EndSize);
        Assert.Equal(LimbSides.Left, Ok(ModuleEditing.SetSides(body, ModulePart.Arms, LimbSides.Left)).Arms!.Sides);
        Assert.Null(Ok(ModuleEditing.SetSides(body, ModulePart.Arms, LimbSides.Both)).Arms!.Sides);
    }

    [Fact]
    public void Limb_settings_refuse_nothing_sizes_missing_limbs_and_heads()
    {
        var body = Body();
        Assert.False(ModuleEditing.SetLength(body, ModulePart.Legs, 0).IsValid);
        Assert.False(ModuleEditing.SetThickness(body, ModulePart.Arms, -0.1).IsValid);
        Assert.False(ModuleEditing.SetEndSize(body, ModulePart.Arms, 0).IsValid);
        Assert.False(ModuleEditing.SetTaper(body, ModulePart.Legs, double.NaN).IsValid);
        Assert.False(ModuleEditing.SetBend(body, ModulePart.Legs, double.NaN).IsValid);
        Assert.False(ModuleEditing.SetLength(body with { Legs = null }, ModulePart.Legs, 0.3).IsValid);
        Assert.False(ModuleEditing.SetLength(body, ModulePart.Head, 0.3).IsValid);
    }

    // ---------------------------------------------------------------- the whole body

    [Fact]
    public void Scaling_multiplies_every_size_and_keeps_the_proportions()
    {
        var body = Body();
        var scaled = Ok(ModuleEditing.Scale(body, 0.5));

        Assert.Equal(0.15, scaled.Head!.Width, 9);
        Assert.Equal(0.14, scaled.Head.Height, 9);
        Assert.Equal(new Point2D(0.005, -0.01), scaled.Head.Offset);
        Assert.Equal(0.16, scaled.Torso!.Width, 9);
        Assert.Equal(0.17, scaled.Torso.Height, 9);
        Assert.Equal(0.13, scaled.Arms!.Length, 9);
        Assert.Equal(0.03, scaled.Arms.Thickness, 9);
        Assert.Equal(0.04, scaled.Arms.EndSize!.Value, 9);
        Assert.Equal(0.12, scaled.Legs!.Length, 9);
        Assert.Equal(0.04, scaled.Legs.Thickness, 9);
        Assert.Null(scaled.Legs.EndSize); // the default follows the thickness by itself
        Assert.Equal(0.4, scaled.Legs.Taper);
        Assert.Equal(body.Head!.Rotation, scaled.Head.Rotation);
        Assert.Same(body.Head.Shape, scaled.Head.Shape);

        var back = Ok(ModuleEditing.Scale(scaled, 2));
        Assert.Equal(body.Torso!.Width, back.Torso!.Width, 9);
        Assert.Equal(body.Legs!.Length, back.Legs!.Length, 9);
    }

    [Fact]
    public void Scaling_holds_each_size_to_its_range_and_refuses_nothing()
    {
        var huge = Ok(ModuleEditing.Scale(Body(), 100));
        Assert.Equal(ModuleEditing.MaxSize, huge.Torso!.Width);
        Assert.Equal(ModuleEditing.MaxLength, huge.Legs!.Length);
        Assert.Equal(ModuleEditing.MaxThickness, huge.Legs.Thickness);
        Assert.Equal(ModuleEditing.MaxEndSize, huge.Arms!.EndSize);
        Assert.Equal(new Point2D(ModuleEditing.MaxOffset, -ModuleEditing.MaxOffset), huge.Head!.Offset);

        Assert.False(ModuleEditing.Scale(Body(), 0).IsValid);
        Assert.False(ModuleEditing.Scale(Body(), -2).IsValid);
        Assert.False(ModuleEditing.Scale(Body(), double.NaN).IsValid);
    }

    [Fact]
    public void Separate_pieces_toggle_and_joined_is_unset()
    {
        var separate = ModuleEditing.SetSeparate(Body(), true);
        Assert.True(separate.IsSeparate);
        Assert.Null(ModuleEditing.SetSeparate(separate, false).Separate);
    }

    [Fact]
    public void A_preset_replaces_the_body_if_it_holds_together()
    {
        var preset = Body() with { Head = null, Separate = true };
        Assert.Equal(preset, Ok(ModuleEditing.ApplyPreset(preset)));

        Assert.False(ModuleEditing.ApplyPreset(preset with { Torso = null }).IsValid);
        Assert.False(ModuleEditing.ApplyPreset(preset with { Legs = preset.Legs! with { Thickness = 0 } }).IsValid);
        Assert.Null(ModuleEditing.Validate(Body()));
    }
}
