using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editing;

/// <summary>
/// Building a character from shapes (docs/shape-characters.md §9.2): swapping a part's
/// shape, sizing, turning, flipping and colouring it, taking parts off and putting them
/// back, shaping the limbs, and scaling the whole body. Pure functions over a
/// <see cref="ModularBody"/>, like <see cref="ShapeEditing"/>; the character editor turns
/// each into one undo step. Nothing here knows the shape library: what "Add arms" puts back
/// is passed in.
/// </summary>
/// <remarks>
/// Sizes are figure units (1.0 = an average adult's height). Values out of range are held to
/// the nearest sensible one; a size of zero or less, or one that isn't a number, is refused.
/// Changing a shape's outline or sockets makes it the character's own (its library
/// <see cref="ModuleShape.Source"/> goes); swapping in a library shape keeps that shape's.
/// </remarks>
public static class ModuleEditing
{
    /// <summary>The smallest a head or torso can be, each way.</summary>
    public const double MinSize = 0.02;

    /// <summary>The biggest a head or torso can be, each way: twice an adult's height.</summary>
    public const double MaxSize = 2;

    public const double MinLength = 0.03;
    public const double MaxLength = 1.5;
    public const double MinThickness = 0.005;
    public const double MaxThickness = 0.5;
    public const double MinEndSize = 0.01;
    public const double MaxEndSize = 1;
    public const double MaxTaper = 0.9;
    public const double MinBend = 0.2;
    public const double MaxBend = 0.8;

    /// <summary>How far a head or torso can be nudged off its socket, each way.</summary>
    public const double MaxOffset = 1;

    /// <summary>How far a hand-placed socket can sit outside its shape's unit box, each way.</summary>
    public const double SocketMargin = 0.5;

    // ---------------------------------------------------------------- reading

    /// <summary>The head or torso, or null if it's left out (or <paramref name="part"/> is a limb).</summary>
    public static ShapeModule? ShapeOf(ModularBody body, ModulePart part) => part switch
    {
        ModulePart.Head => body.Head,
        ModulePart.Torso => body.Torso,
        _ => null
    };

    /// <summary>The arms or legs, or null if they're left out (or <paramref name="part"/> isn't a limb).</summary>
    public static LimbModule? LimbOf(ModularBody body, ModulePart part) => part switch
    {
        ModulePart.Arms => body.Arms,
        ModulePart.Legs => body.Legs,
        _ => null
    };

    public static bool Has(ModularBody body, ModulePart part) => ShapeOf(body, part) is not null || LimbOf(body, part) is not null;

    /// <summary>One of a limb's own shapes: its hand or foot, or a piece; null where it uses its own round end or capsule.</summary>
    public static ModuleShape? ShapeOf(LimbModule limb, LimbPiece piece) => piece switch
    {
        LimbPiece.End => limb.End,
        LimbPiece.Upper => limb.Upper,
        _ => limb.Lower
    };

    // ---------------------------------------------------------------- swapping shapes

    /// <summary>
    /// Gives the head or torso <paramref name="shape"/> instead of its own (the gallery's
    /// click), keeping its size, turn, flips, nudge and colour - only the outline changes.
    /// </summary>
    public static EditResult<ModularBody> SwapShape(ModularBody body, ModulePart part, ModuleShape shape)
    {
        if (ShapeError(shape) is { } error)
            return Fail(error);
        return EditShape(body, part, m => m with { Shape = shape });
    }

    /// <summary>
    /// Gives the arms or legs a new hand or foot (<see cref="LimbPiece.End"/>) or piece, keeping
    /// everything else; null puts back the limb's own round end (or a plain capsule for a
    /// piece). A piece turns the limb into <see cref="LimbStyle.Pieces"/>, the only style that
    /// draws one.
    /// </summary>
    public static EditResult<ModularBody> SwapShape(ModularBody body, ModulePart part, LimbPiece piece, ModuleShape? shape)
    {
        if (shape is not null && ShapeError(shape) is { } error)
            return Fail(error);
        return EditLimb(body, part, l => piece switch
        {
            LimbPiece.End => l with { End = shape },
            LimbPiece.Upper => l with { Upper = shape, Style = shape is null ? l.Style : LimbStyle.Pieces },
            _ => l with { Lower = shape, Style = shape is null ? l.Style : LimbStyle.Pieces }
        });
    }

    /// <summary>
    /// The head's or torso's outline, its points moved (Edit Points, docs/shape-editing.md):
    /// <paramref name="anchors"/> in the shape's unit box. It's the character's own shape from
    /// then on, so it no longer follows the library.
    /// </summary>
    public static EditResult<ModularBody> SetAnchors(ModularBody body, ModulePart part, IReadOnlyList<ShapeAnchor> anchors)
    {
        if (AnchorsError(anchors) is { } error)
            return Fail(error);
        return EditShape(body, part, m => m with { Shape = m.Shape with { Anchors = anchors.ToList(), Source = null, MyAssetsVersion = null } });
    }

    /// <summary>A limb's hand, foot or piece outline with its points moved; like the head's, it's the character's own from then on.</summary>
    public static EditResult<ModularBody> SetAnchors(ModularBody body, ModulePart part, LimbPiece piece, IReadOnlyList<ShapeAnchor> anchors)
    {
        if (AnchorsError(anchors) is { } error)
            return Fail(error);
        if (LimbOf(body, part) is { } limb && ShapeOf(limb, piece) is null)
            return Fail(piece == LimbPiece.End ? $"The {part.Noun()} have no {EndNoun(part)} shape to change." : "That piece is a plain capsule: pick a shape for it first.");
        var edited = (ModuleShape s) => s with { Anchors = anchors.ToList(), Source = null, MyAssetsVersion = null };
        return EditLimb(body, part, l => piece switch
        {
            LimbPiece.End => l with { End = edited(l.End!) },
            LimbPiece.Upper => l with { Upper = edited(l.Upper!) },
            _ => l with { Lower = edited(l.Lower!) }
        });
    }

    /// <summary>
    /// Places one of the head's or torso's sockets by hand (<see cref="ModuleSockets"/>; the
    /// escape hatch when the derived one isn't where it should be), in the shape's unit box;
    /// null goes back to the derived one. The shape is the character's own from then on.
    /// </summary>
    public static EditResult<ModularBody> SetSocket(ModularBody body, ModulePart part, string socket, Point2D? point)
    {
        if (!ModuleSockets.All.Contains(socket))
            return Fail($"There's no socket called '{socket}'.");
        if (point is { } p && !(double.IsFinite(p.X) && double.IsFinite(p.Y)))
            return Fail("A socket needs a place to go.");
        return EditShape(body, part, m =>
        {
            var sockets = m.Shape.Sockets is { } existing
                ? new SortedDictionary<string, Point2D>(existing, StringComparer.Ordinal)
                : new SortedDictionary<string, Point2D>(StringComparer.Ordinal);
            if (point is { } at)
                sockets[socket] = new Point2D(Math.Clamp(at.X, -SocketMargin, 1 + SocketMargin), Math.Clamp(at.Y, -SocketMargin, 1 + SocketMargin));
            else
                sockets.Remove(socket);
            return m with { Shape = m.Shape with { Sockets = sockets.Count == 0 ? null : sockets, Source = null, MyAssetsVersion = null } };
        });
    }

    // ---------------------------------------------------------------- size, turn, flip, place, colour

    /// <summary>The head or torso drawn <paramref name="width"/> by <paramref name="height"/> figure units.</summary>
    public static EditResult<ModularBody> Resize(ModularBody body, ModulePart part, double width, double height)
    {
        if (!Positive(width) || !Positive(height))
            return Fail("A size must be more than zero.");
        if (part.IsLimb())
            return Fail($"The {part.Noun()} are sized by their length and thickness.");
        return EditShape(body, part, m => m with { Width = Math.Clamp(width, MinSize, MaxSize), Height = Math.Clamp(height, MinSize, MaxSize) });
    }

    /// <summary>The head or torso turned <paramref name="degrees"/> clockwise about its centre (kept between -180 and 180).</summary>
    public static EditResult<ModularBody> SetRotation(ModularBody body, ModulePart part, double degrees)
    {
        if (!double.IsFinite(degrees))
            return Fail("A turn needs a number of degrees.");
        if (part.IsLimb())
            return Fail($"The {part.Noun()} turn by posing them; only a head or a torso can be turned here.");
        var turn = degrees % 360;
        if (turn >= 180)
            turn -= 360;
        else if (turn < -180)
            turn += 360;
        return EditShape(body, part, m => m with { Rotation = Math.Abs(turn) < 1e-9 ? null : turn });
    }

    /// <summary>Mirrors the head or torso left to right, or back.</summary>
    public static EditResult<ModularBody> FlipHorizontally(ModularBody body, ModulePart part) =>
        part.IsLimb()
            ? Fail("Only a head or a torso can be flipped.")
            : EditShape(body, part, m => m with { FlipX = m.FlipX == true ? null : true });

    /// <summary>Turns the head or torso upside down (the "upside-down triangle" torso), or back.</summary>
    public static EditResult<ModularBody> FlipVertically(ModularBody body, ModulePart part) =>
        part.IsLimb()
            ? Fail("Only a head or a torso can be flipped.")
            : EditShape(body, part, m => m with { FlipY = m.FlipY == true ? null : true });

    /// <summary>The head or torso nudged <paramref name="offset"/> off its socket (null or zero: back on it).</summary>
    public static EditResult<ModularBody> SetOffset(ModularBody body, ModulePart part, Point2D? offset)
    {
        if (offset is { } o && !(double.IsFinite(o.X) && double.IsFinite(o.Y)))
            return Fail("A nudge needs a distance.");
        if (part.IsLimb())
            return Fail($"The {part.Noun()} stay on their sockets; only a head or a torso can be moved off its place.");
        return EditShape(body, part, m => m with { Offset = Clamped(offset) });
    }

    /// <summary>The head or torso moved <paramref name="dx"/>, <paramref name="dy"/> further off its socket.</summary>
    public static EditResult<ModularBody> Nudge(ModularBody body, ModulePart part, double dx, double dy)
    {
        var current = ShapeOf(body, part)?.Offset ?? default;
        return SetOffset(body, part, new Point2D(current.X + dx, current.Y + dy));
    }

    /// <summary>
    /// The colour slot a part is filled with (<see cref="CharacterDefinition.ColorSlots"/>):
    /// null or <see cref="CharacterDefinition.SkinSlot"/> fills it with the skin, like the rest.
    /// </summary>
    public static EditResult<ModularBody> SetColor(ModularBody body, ModulePart part, string? slot)
    {
        if (slot is not null && string.IsNullOrWhiteSpace(slot))
            return Fail("A colour needs a name.");
        var color = slot is null || slot == CharacterDefinition.SkinSlot ? null : slot;
        return part.IsShape()
            ? EditShape(body, part, m => m with { Color = color })
            : EditLimb(body, part, l => l with { Color = color });
    }

    // ---------------------------------------------------------------- taking parts off and putting them back

    /// <summary>
    /// Leaves a part out (the torso of a head-on-legs, the head of a bean). A head or a torso
    /// always stays, for the rest to hang on.
    /// </summary>
    public static EditResult<ModularBody> Remove(ModularBody body, ModulePart part)
    {
        if (!Has(body, part))
            return Fail(part.IsShape() ? $"There's no {part.Noun()} to take off." : $"There are no {part.Noun()} to take off.");
        var result = Without(body, part);
        if (result.Head is null && result.Torso is null)
            return Fail("A character needs a head or a torso to hold the rest together.");
        return Success(result);
    }

    /// <summary>Puts a head or torso back (or on for the first time): <paramref name="module"/> is what goes on, usually the library's default for the part.</summary>
    public static EditResult<ModularBody> Add(ModularBody body, ModulePart part, ShapeModule module)
    {
        if (part.IsLimb())
            return Fail($"The {part.Noun()} need a limb, not a shape.");
        if (Has(body, part))
            return Fail($"It already has a {part.Noun()}.");
        if (ModuleError(module) is { } error)
            return Fail(error);
        return Success(With(body, part, module));
    }

    /// <summary>Puts arms or legs back (or on for the first time): <paramref name="limbs"/> is what goes on, usually the library's default.</summary>
    public static EditResult<ModularBody> Add(ModularBody body, ModulePart part, LimbModule limbs)
    {
        if (part.IsShape())
            return Fail($"A {part.Noun()} needs a shape, not a limb.");
        if (Has(body, part))
            return Fail($"It already has {part.Noun()}.");
        if (LimbError(limbs) is { } error)
            return Fail(error);
        return Success(With(body, part, limbs));
    }

    // ---------------------------------------------------------------- limbs

    public static EditResult<ModularBody> SetLimbStyle(ModularBody body, ModulePart part, LimbStyle style) =>
        EditLimb(body, part, l => l with { Style = style });

    /// <summary>Root (shoulder, hip) to end (wrist, ankle), figure units.</summary>
    public static EditResult<ModularBody> SetLength(ModularBody body, ModulePart part, double length) =>
        Positive(length)
            ? EditLimb(body, part, l => l with { Length = Math.Clamp(length, MinLength, MaxLength) })
            : Fail("A length must be more than zero.");

    /// <summary>The limb's width at its root, figure units.</summary>
    public static EditResult<ModularBody> SetThickness(ModularBody body, ModulePart part, double thickness) =>
        Positive(thickness)
            ? EditLimb(body, part, l => l with { Thickness = Math.Clamp(thickness, MinThickness, MaxThickness) })
            : Fail("A thickness must be more than zero.");

    /// <summary>How much thinner the end is than the root, 0 (not at all) to <see cref="MaxTaper"/>.</summary>
    public static EditResult<ModularBody> SetTaper(ModularBody body, ModulePart part, double taper) =>
        double.IsFinite(taper)
            ? EditLimb(body, part, l => l with { Taper = Math.Clamp(taper, 0, MaxTaper) })
            : Fail("A taper needs a number.");

    /// <summary>Where the elbow or knee sits along the limb, <see cref="MinBend"/> to <see cref="MaxBend"/> of the way down.</summary>
    public static EditResult<ModularBody> SetBend(ModularBody body, ModulePart part, double bend) =>
        double.IsFinite(bend)
            ? EditLimb(body, part, l => l with { Bend = Math.Clamp(bend, MinBend, MaxBend) })
            : Fail("Say where the bend goes with a number.");

    /// <summary>The hand's or foot's height, figure units; null goes back to the default (twice the limb's thickness).</summary>
    public static EditResult<ModularBody> SetEndSize(ModularBody body, ModulePart part, double? size) =>
        size is { } s && !Positive(s)
            ? Fail("A size must be more than zero.")
            : EditLimb(body, part, l => l with { EndSize = size is { } v ? Math.Clamp(v, MinEndSize, MaxEndSize) : null });

    /// <summary>Both limbs, or just one ("one arm only").</summary>
    public static EditResult<ModularBody> SetSides(ModularBody body, ModulePart part, LimbSides sides) =>
        EditLimb(body, part, l => l with { Sides = sides == LimbSides.Both ? null : sides });

    // ---------------------------------------------------------------- the whole body

    /// <summary>Joined (one creature, seamless joints) or separate pieces (each outlined, like building blocks).</summary>
    public static ModularBody SetSeparate(ModularBody body, bool separate) =>
        body with { Separate = separate ? true : null };

    /// <summary>
    /// The whole body <paramref name="factor"/> times the size ("make her a bit smaller",
    /// Height %), standing on the same ground: every size, nudge, length, thickness and hand or
    /// foot size is multiplied, each held to its own range. Proportions within a part (taper,
    /// bend, sockets) are fractions, so they stay.
    /// </summary>
    public static EditResult<ModularBody> Scale(ModularBody body, double factor)
    {
        if (!Positive(factor))
            return Fail("Scale by more than zero.");

        ShapeModule? ScaleShape(ShapeModule? m) => m is null ? null : m with
        {
            Width = Math.Clamp(m.Width * factor, MinSize, MaxSize),
            Height = Math.Clamp(m.Height * factor, MinSize, MaxSize),
            Offset = m.Offset is { } o ? Clamped(new Point2D(o.X * factor, o.Y * factor)) : null
        };

        LimbModule? ScaleLimb(LimbModule? l) => l is null ? null : l with
        {
            Length = Math.Clamp(l.Length * factor, MinLength, MaxLength),
            Thickness = Math.Clamp(l.Thickness * factor, MinThickness, MaxThickness),
            EndSize = l.EndSize is { } e ? Math.Clamp(e * factor, MinEndSize, MaxEndSize) : null
        };

        return Success(body with { Head = ScaleShape(body.Head), Torso = ScaleShape(body.Torso), Arms = ScaleLimb(body.Arms), Legs = ScaleLimb(body.Legs) });
    }

    /// <summary>A creature preset in place of the whole body (one click; everything stays editable afterwards). The preset is checked like anything else put on.</summary>
    public static EditResult<ModularBody> ApplyPreset(ModularBody preset) =>
        Validate(preset) is { } error ? Fail(error) : Success(preset);

    /// <summary>What's wrong with a whole body, in plain words, or null if nothing is.</summary>
    public static string? Validate(ModularBody body)
    {
        if (body.Head is null && body.Torso is null)
            return "A character needs a head or a torso to hold the rest together.";
        return (body.Head is { } head ? ModuleError(head) : null)
               ?? (body.Torso is { } torso ? ModuleError(torso) : null)
               ?? (body.Arms is { } arms ? LimbError(arms) : null)
               ?? (body.Legs is { } legs ? LimbError(legs) : null);
    }

    // ---------------------------------------------------------------- helpers

    private static EditResult<ModularBody> EditShape(ModularBody body, ModulePart part, Func<ShapeModule, ShapeModule> edit)
    {
        if (part.IsLimb())
            return Fail($"The {part.Noun()} have no outline of their own.");
        return ShapeOf(body, part) is { } module
            ? Success(With(body, part, edit(module)))
            : Fail($"This character has no {part.Noun()}.");
    }

    private static EditResult<ModularBody> EditLimb(ModularBody body, ModulePart part, Func<LimbModule, LimbModule> edit)
    {
        if (part.IsShape())
            return Fail($"Only arms and legs have that; a {part.Noun()} doesn't.");
        return LimbOf(body, part) is { } limb
            ? Success(With(body, part, edit(limb)))
            : Fail($"This character has no {part.Noun()}.");
    }

    private static ModularBody With(ModularBody body, ModulePart part, ShapeModule module) => part switch
    {
        ModulePart.Head => body with { Head = module },
        ModulePart.Torso => body with { Torso = module },
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Arms and legs are limbs, not shapes.")
    };

    private static ModularBody With(ModularBody body, ModulePart part, LimbModule limbs) => part switch
    {
        ModulePart.Arms => body with { Arms = limbs },
        ModulePart.Legs => body with { Legs = limbs },
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "A head or a torso is a shape, not a limb.")
    };

    private static ModularBody Without(ModularBody body, ModulePart part) => part switch
    {
        ModulePart.Head => body with { Head = null },
        ModulePart.Torso => body with { Torso = null },
        ModulePart.Arms => body with { Arms = null },
        _ => body with { Legs = null }
    };

    private static string? ModuleError(ShapeModule module) =>
        !Positive(module.Width) || !Positive(module.Height) ? "A size must be more than zero." : ShapeError(module.Shape);

    private static string? LimbError(LimbModule limb)
    {
        if (!Positive(limb.Length) || !Positive(limb.Thickness) || limb.EndSize is { } e && !Positive(e))
            return "A size must be more than zero.";
        return (limb.End is { } end ? ShapeError(end) : null)
               ?? (limb.Upper is { } upper ? ShapeError(upper) : null)
               ?? (limb.Lower is { } lower ? ShapeError(lower) : null);
    }

    private static string? ShapeError(ModuleShape shape) => AnchorsError(shape.Anchors);

    private static string? AnchorsError(IReadOnlyList<ShapeAnchor> anchors)
    {
        if (anchors.Count < 3)
            return "A shape needs at least three points.";
        foreach (var a in anchors)
        {
            if (!Finite(a.Point) || !Finite(a.InHandle) || !Finite(a.OutHandle))
                return "One of the shape's points is out of reach.";
        }
        var bounds = AnchorRing.CurveBounds(anchors);
        if (bounds.Width < 1e-6 || bounds.Height < 1e-6)
            return "A shape needs some width and some height.";
        return null;
    }

    private static Point2D? Clamped(Point2D? offset)
    {
        if (offset is not { } o)
            return null;
        var clamped = new Point2D(Math.Clamp(o.X, -MaxOffset, MaxOffset), Math.Clamp(o.Y, -MaxOffset, MaxOffset));
        return Math.Abs(clamped.X) < 1e-12 && Math.Abs(clamped.Y) < 1e-12 ? null : clamped;
    }

    private static string EndNoun(ModulePart part) => part == ModulePart.Legs ? "foot" : "hand";

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    private static bool Finite(Point2D p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    private static EditResult<ModularBody> Success(ModularBody body) => EditResult<ModularBody>.Success(body);

    private static EditResult<ModularBody> Fail(string error) => EditResult<ModularBody>.Failure(error);
}
