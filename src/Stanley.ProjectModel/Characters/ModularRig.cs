using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Builds a <see cref="BodyFigure"/> from a <see cref="ModularBody"/> (docs/shape-characters.md §5):
/// the same joints, layers and regions <see cref="BodyRig"/> gives the mannequin, so posing,
/// stickers and drawing work on it unchanged.
///
/// The parts are stacked on each other's sockets (<see cref="ModuleSockets"/>): the legs hang
/// from the torso's hips, the head sits on its neck, the arms hang from its shoulders. With
/// no torso the head takes its place - legs on the head's hip sockets, arms on its sides -
/// and with no head the face goes on the torso. Whatever is left out, the layout has every
/// joint <see cref="BodyRig"/>'s has for the view, a missing one collapsed onto its
/// neighbour (no torso: the hips, spine, chest and neck all at the head's bottom; no arm:
/// its elbow and wrist on its shoulder), so any <see cref="PoseData"/> applies. Posing is
/// BodyRig's own: the same trunk bend (the torso outline curves with it), the same limb
/// rotations, limits and hips shift. The ground (the figure's origin) is under the lowest
/// thing drawn standing at rest, between the hips; the figure's height is its top above it.
/// A three-quarter view isn't generated yet: it draws as the front, as for the mannequin.
/// </summary>
public static class ModularRig
{
    /// <summary>
    /// How far the arms hang off straight down at rest, degrees: out to the side from the
    /// front, forward side on. The same both ways, so a figure whose hands are its lowest
    /// point (one with no legs) is as tall from either side.
    /// </summary>
    public const double ArmSpread = 8;

    /// <summary>How far a head sinks onto the neck socket below it, as a fraction of its height - so the two overlap and join.</summary>
    public const double HeadSink = 0.08;

    /// <summary>A <see cref="LimbStyle.Stick"/> limb's radius, whatever its thickness (figure units).</summary>
    public const double StickRadius = 0.006;

    /// <summary>The size of the frame a missing region gets at its socket (figure units).</summary>
    public const double Degenerate = 1e-3;

    public static BodyFigure Build(ModularBody body, ViewAngle angle = ViewAngle.Front, PoseData? pose = null)
    {
        var provisional = Assemble(body.Normalized(), angle == ViewAngle.Profile);
        // Standing at rest first, to find where the ground goes and how tall it all is.
        var (standing, drawn) = Draw(provisional, 1, null);
        var hips = provisional.At(HumanoidBone.Hips);
        var shift = new Point2D(-hips.X, -(drawn ? standing.Extent.Bottom : hips.Y));
        return Draw(provisional.Moved(shift), drawn ? standing.Extent.Height : 0, pose).Figure;
    }

    /// <summary>How tall the assembly stands: its top above the ground point.</summary>
    public static double Height(ModularBody body) => Math.Max(0, -Build(body).Extent.Top);

    /// <summary>
    /// Where the head's or torso's unit box lands in figure space, standing at rest seen from
    /// <paramref name="angle"/>: the frame <see cref="Build"/> places its outline through once
    /// flipped (<see cref="ModuleSockets.Flipped"/>), its nudge included. Null when the body
    /// hasn't that part (or <paramref name="part"/> is a limb). A pose turns the head and bends
    /// the torso, so this is the figure standing with no pose - what the character editor's
    /// Edit Points and Edit sockets map through.
    /// </summary>
    public static ModuleFrame? FrameOf(ModularBody body, ModulePart part, ViewAngle angle = ViewAngle.Front)
    {
        if (!part.IsShape())
            return null;
        var provisional = Assemble(body.Normalized(), angle == ViewAngle.Profile);
        var placed = part == ModulePart.Head ? provisional.Head : provisional.Torso;
        if (placed is null)
            return null;
        var (standing, drawn) = Draw(provisional, 1, null);
        var hips = provisional.At(HumanoidBone.Hips);
        return placed.Frame.Moved(new Point2D(-hips.X, -(drawn ? standing.Extent.Bottom : hips.Y)));
    }

    // ---------------------------------------------------------------- stacking the parts at rest

    /// <summary>A head or torso as placed: its shape (flipped already), where its unit box is, and its colour slot.</summary>
    private sealed record Placed(ModuleShape Shape, ModuleFrame Frame, string ColorSlot)
    {
        public Placed Moved(Point2D by) => this with { Frame = Frame.Moved(by) };
    }

    /// <summary>The parts stacked standing at rest, before the ground is put under them.</summary>
    private sealed record Assembly(ModularBody Body, bool Profile, ViewAngleRestLayout Rest, Placed? Head, Placed? Torso, bool ArmsOnHead)
    {
        public Point2D At(HumanoidBone bone) => Rest.Bones.First(b => b.Bone == bone).Position;

        public Assembly Moved(Point2D by) => this with
        {
            Rest = Rest with { Bones = Rest.Bones.Select(b => b with { Position = Add(b.Position, by) }).ToList() },
            Head = Head?.Moved(by),
            Torso = Torso?.Moved(by),
        };
    }

    private static Assembly Assemble(ModularBody body, bool profile)
    {
        var (arms, legs) = (body.Arms, body.Legs);
        var armRadius = arms is null ? 0 : Radii(arms).Root;
        var legRadius = legs is null ? 0 : Radii(legs).Root;

        // The part the others hang from: the torso, else the head. Its own nudge moves it and
        // what sits on it (head, arms), not the legs it stands on.
        Placed? host = null;
        var hostBase = default(ModuleFrame);
        if ((body.Torso ?? body.Head) is { } module)
        {
            var shape = ModuleSockets.Flipped(module.Shape, module.FlipX == true, module.FlipY == true);
            hostBase = new ModuleFrame(default, module.Width, module.Height, module.Rotation ?? 0);
            host = new Placed(shape, hostBase.Moved(module.Offset ?? default), module.ColorSlot);
        }
        var sockets = host is null ? null : ModuleSockets.Derive(host.Shape);
        Point2D Socket(string name) => sockets![name];
        bool Derived(params string[] names) => names.All(n => !ModuleSockets.IsHandSet(host!.Shape, n));

        // Leg roots: on the hip sockets of the part as it stands (before its nudge), raised by
        // the leg's radius so the thigh's top sits inside it. Side on, both on its centre
        // line, the far one set back a little so both feet show.
        Point2D Raised(Point2D unit, bool derived) =>
            derived ? unit with { Y = unit.Y - Math.Min(legRadius / host!.Frame.Height, Math.Max(0, unit.Y - 0.5)) } : unit;
        Point2D HipRoot(LimbSide side)
        {
            var sign = side == LimbSide.Left ? 1 : -1;
            if (host is null)
                return profile ? new Point2D(side == LimbSide.Right ? 0.1 * legRadius : -0.3 * legRadius, 0) : new Point2D(sign * legRadius * 1.1, 0);
            if (!profile)
            {
                var name = side == LimbSide.Left ? ModuleSockets.LeftHip : ModuleSockets.RightHip;
                return hostBase.ToFigure(Raised(Socket(name), Derived(name)));
            }
            var middle = Raised(Mid(Socket(ModuleSockets.LeftHip), Socket(ModuleSockets.RightHip)), Derived(ModuleSockets.LeftHip, ModuleSockets.RightHip));
            var back = (side == LimbSide.Right ? 0.1 : -0.3) * legRadius / host.Frame.Width;
            return hostBase.ToFigure(middle with { X = middle.X + back });
        }

        // Arm roots: on the shoulder sockets, moved in by the arm's radius so the arm's top
        // sits inside the shoulder. Side on, on the centre line, the far one set back.
        Point2D ShoulderRoot(LimbSide side)
        {
            var sign = side == LimbSide.Left ? 1 : -1;
            if (host is null)
                return profile ? new Point2D(side == LimbSide.Right ? 0 : -0.6 * armRadius, 0) : new Point2D(sign * (armRadius + legRadius) * 1.5, 0);
            if (!profile)
            {
                var name = side == LimbSide.Left ? ModuleSockets.LeftShoulder : ModuleSockets.RightShoulder;
                var unit = Socket(name);
                if (Derived(name))
                {
                    var inset = Math.Min(armRadius / host.Frame.Width, Math.Abs(unit.X - 0.5));
                    unit = unit with { X = unit.X + (unit.X > 0.5 ? -inset : inset) };
                }
                return host.Frame.ToFigure(unit);
            }
            var middle = Mid(Socket(ModuleSockets.LeftShoulder), Socket(ModuleSockets.RightShoulder));
            var back = (side == LimbSide.Right ? -0.2 : -0.8) * armRadius / host.Frame.Width;
            return host.Frame.ToFigure(middle with { X = middle.X + back });
        }

        // The trunk joints. With a torso: the hips between the legs' roots, the neck on its
        // neck socket, the head joint there too. Without one, all of them at the head's
        // bottom (or, with no head either, at the origin).
        Placed? torso = null, head = null;
        Point2D hips, neck, headJoint;
        if (body.Torso is not null && host is not null)
        {
            torso = host;
            hips = Mid(HipRoot(LimbSide.Left), HipRoot(LimbSide.Right));
            neck = host.Frame.ToFigure(Socket(ModuleSockets.Neck));
            headJoint = neck;
            if (body.Head is { } h)
            {
                var shape = ModuleSockets.Flipped(h.Shape, h.FlipX == true, h.FlipY == true);
                var frame = ModuleFrame.Of(h, ModuleSockets.Chin(shape), new Point2D(neck.X, neck.Y + HeadSink * h.Height));
                head = new Placed(shape, frame.Moved(h.Offset ?? default), h.ColorSlot);
            }
        }
        else if (host is not null)
        {
            head = host;
            hips = neck = headJoint = host.Frame.ToFigure(ModuleSockets.Chin(host.Shape));
        }
        else
            hips = neck = headJoint = default;
        // A neck at or below the hips (a torso turned over) can't bend: the back stays straight.
        var bends = neck.Y < hips.Y - 1e-9;
        var spineNeck = bends ? neck : hips;

        var bones = new List<BoneRestPose>
        {
            new(HumanoidBone.Hips, hips),
            new(HumanoidBone.Spine, Lerp(hips, spineNeck, 0.3)),
            new(HumanoidBone.Chest, Lerp(hips, spineNeck, 0.65)),
            new(HumanoidBone.Neck, spineNeck),
            new(HumanoidBone.Head, headJoint),
        };
        void AddArm(Side side)
        {
            var root = ShoulderRoot(side.Of);
            var spread = (profile || side.Of == LimbSide.Left ? ArmSpread : -ArmSpread) * Math.PI / 180;
            var (_, elbow, wrist) = Chain(arms, side.Of, root, new Point2D(Math.Sin(spread), Math.Cos(spread)));
            bones.Add(new(side.Shoulder, Lerp(neck, root, 0.5)));
            bones.Add(new(side.UpperArm, root));
            bones.Add(new(side.LowerArm, elbow));
            bones.Add(new(side.Hand, wrist));
        }
        void AddLeg(Side side)
        {
            var root = HipRoot(side.Of);
            var (_, knee, ankle) = Chain(legs, side.Of, root, new Point2D(0, 1));
            bones.Add(new(side.UpperLeg, root));
            bones.Add(new(side.LowerLeg, knee));
            bones.Add(new(side.Foot, ankle));
        }
        // The same joints in the same order as BodyRig's layout for the view.
        if (profile)
        {
            AddArm(Side.Right);
            AddLeg(Side.Right);
            AddArm(Side.Left);
            AddLeg(Side.Left);
        }
        else
        {
            foreach (var side in new[] { Side.Left, Side.Right })
            {
                AddArm(side);
                AddLeg(side);
            }
        }
        var rest = new ViewAngleRestLayout(profile ? ViewAngle.Profile : ViewAngle.Front, bones);
        return new Assembly(body, profile, rest, head, torso, ArmsOnHead: torso is null && head is not null);
    }

    /// <summary>A limb's three joints hanging from <paramref name="root"/> along <paramref name="direction"/>; all on the root when the character hasn't that limb.</summary>
    private static (Point2D Root, Point2D Middle, Point2D End) Chain(LimbModule? limb, LimbSide side, Point2D root, Point2D direction) =>
        limb is not null && limb.Has(side)
            ? (root, Along(root, direction, limb.Length * (limb.Bend ?? 0.5)), Along(root, direction, limb.Length))
            : (root, root, root);

    /// <summary>A limb's radius at its root, its elbow or knee, and its end.</summary>
    private static (double Root, double Middle, double End) Radii(LimbModule limb)
    {
        var r = limb.Thickness / 2;
        return limb.Style switch
        {
            LimbStyle.Stick => (StickRadius, StickRadius, StickRadius),
            LimbStyle.Taper => (r, r * (1 - (limb.Taper ?? 0.5) * (limb.Bend ?? 0.5)), r * (1 - (limb.Taper ?? 0.5))),
            _ => (r, r, r),
        };
    }

    // ---------------------------------------------------------------- posing and drawing

    /// <summary>What one limb draws, split so a side view can put its foot in a layer of its own.</summary>
    private sealed record Items(IReadOnlyList<BodyCapsule> Capsules, IReadOnlyList<BodyEllipse> Ellipses, IReadOnlyList<FigureShape> Shapes)
    {
        public static Items None { get; } = new([], [], []);

        public static Items Join(params Items[] items) =>
            new(items.SelectMany(i => i.Capsules).ToList(), items.SelectMany(i => i.Ellipses).ToList(), items.SelectMany(i => i.Shapes).ToList());
    }

    private sealed record LimbParts(bool Present, LimbFrame Frame, BodyEllipse EndRegion, Items Bones, Items End);

    private static (BodyFigure Figure, bool Drawn) Draw(Assembly assembly, double height, PoseData? pose)
    {
        var (body, profile, rest) = (assembly.Body, assembly.Profile, assembly.Rest);
        var trunk = new BodyRig.Trunk(height, rest, pose,
            [assembly.At(HumanoidBone.Hips), assembly.At(HumanoidBone.Spine), assembly.At(HumanoidBone.Chest), assembly.At(HumanoidBone.Neck)],
            assembly.At(HumanoidBone.Head));
        var baseLayout = trunk.Apply(rest);
        var headRest = assembly.At(HumanoidBone.Head);
        var headPosed = Joint(baseLayout, HumanoidBone.Head);
        Point2D HeadMap(Point2D p) => Add(headPosed, Rotate(Sub(p, headRest), trunk.HeadAngle));
        if (assembly.ArmsOnHead)
        {
            // Arms on a head with no torso turn with the head, rigidly.
            var onHead = new HashSet<HumanoidBone>();
            foreach (var side in new[] { Side.Left, Side.Right })
                onHead.UnionWith([side.Shoulder, side.UpperArm, side.LowerArm, side.Hand]);
            baseLayout = baseLayout with
            {
                Bones = baseLayout.Bones.Select(b => onHead.Contains(b.Bone) ? b with { Position = HeadMap(assembly.At(b.Bone)) } : b).ToList()
            };
        }
        var layout = BodyRig.ApplyPose(baseLayout, pose?.BoneRotations);
        Point2D At(HumanoidBone bone) => Joint(layout, bone);
        var absent = new HashSet<FigurePart> { new(BodyRegion.Neck) };

        // The torso: its outline through the trunk's bend, so a lean curves it.
        FigureShape? torsoShape = null;
        TorsoFrame torsoFrame;
        if (assembly.Torso is { } torso)
        {
            var restAnchors = torso.Frame.Place(torso.Shape.Anchors);
            torsoShape = new FigureShape(restAnchors.Select(a => new ShapeAnchor(trunk.Upper(a.Point), trunk.Upper(a.InHandle), trunk.Upper(a.OutHandle), a.HandleKind)).ToList(), torso.ColorSlot, ModulePart.Torso);
            torsoFrame = new TorsoFrame(ModuleSockets.Flatten(restAnchors), trunk.Bend);
        }
        else
        {
            var at = assembly.At(HumanoidBone.Hips);
            torsoFrame = new TorsoFrame(
                [new(at.X - Degenerate, at.Y - Degenerate), new(at.X + Degenerate, at.Y - Degenerate), new(at.X + Degenerate, at.Y + Degenerate), new(at.X - Degenerate, at.Y + Degenerate)],
                trunk.Bend);
            absent.Add(new FigurePart(BodyRegion.Torso));
        }

        // The head: turned rigidly with the head bone. Its face is the largest ellipse inside
        // it; with no head, the face goes on the torso's upper part and bends with it.
        FigureShape? headShape = null;
        BodyEllipse headBlob, face;
        if (assembly.Head is { } head)
        {
            headShape = new FigureShape(head.Frame.Place(head.Shape.Anchors).Select(a => new ShapeAnchor(HeadMap(a.Point), HeadMap(a.InHandle), HeadMap(a.OutHandle), a.HandleKind)).ToList(), head.ColorSlot, ModulePart.Head);
            headBlob = new BodyEllipse(HeadMap(head.Frame.Center), head.Frame.Width / 2, head.Frame.Height / 2, head.Frame.Rotation + trunk.HeadAngle);
            var restFace = head.Frame.Place(ModuleSockets.FaceEllipse(head.Shape, upperBand: false));
            face = restFace with { Center = HeadMap(restFace.Center), RotationDegrees = restFace.RotationDegrees + trunk.HeadAngle };
        }
        else if (assembly.Torso is { } torsoOnly)
        {
            var restFace = torsoOnly.Frame.Place(ModuleSockets.FaceEllipse(torsoOnly.Shape, upperBand: true));
            face = headBlob = restFace with { Center = trunk.Upper(restFace.Center), RotationDegrees = restFace.RotationDegrees + trunk.Bend.AngleAt(restFace.Center.Y) };
        }
        else
        {
            face = headBlob = new BodyEllipse(At(HumanoidBone.Head), Degenerate, Degenerate, trunk.HeadAngle);
            absent.Add(new FigurePart(BodyRegion.Head));
        }

        LimbParts Limb(LimbModule? limb, Side side, bool arm)
        {
            var (rootBone, middleBone, endBone) = arm ? (side.UpperArm, side.LowerArm, side.Hand) : (side.UpperLeg, side.LowerLeg, side.Foot);
            var (s, e, w) = (At(rootBone), At(middleBone), At(endBone));
            if (limb is null || !limb.Has(side.Of))
            {
                var tiny = new BodyCapsule(s, s, Degenerate, Degenerate);
                absent.Add(new FigurePart(arm ? BodyRegion.Arm : BodyRegion.Leg, limb is null ? null : side.Of));
                absent.Add(new FigurePart(arm ? BodyRegion.Hand : BodyRegion.Foot, limb is null ? null : side.Of));
                return new LimbParts(false, new LimbFrame(tiny, tiny), new BodyEllipse(w, Degenerate, Degenerate), Items.None, Items.None);
            }

            var (r0, rm, r1) = Radii(limb);
            var frame = new LimbFrame(new BodyCapsule(s, e, r0, rm), new BodyCapsule(e, w, rm, r1));
            // From the front a pair is drawn mirror images, the right one flipped, so an end
            // shape drawn pointing out (a thumb, a toe) points out on both sides.
            var mirror = !profile && side.Of == LimbSide.Right;
            var slot = limb.ColorSlot;
            var part = arm ? ModulePart.Arms : ModulePart.Legs;
            var boneCapsules = new List<BodyCapsule>();
            var boneShapes = new List<FigureShape>();
            foreach (var (bone, piece) in new[] { (frame.Upper, limb.Upper), (frame.Lower, limb.Lower) })
            {
                if (limb.Style == LimbStyle.Pieces && piece is not null)
                    boneShapes.Add(new FigureShape(PieceAlong(piece, bone, limb.Thickness, mirror), slot, part));
                else
                    boneCapsules.Add(bone);
            }

            var direction = Normalize(Sub(w, e), Normalize(Sub(w, s), new Point2D(0, 1)));
            var turn = arm
                ? Math.Atan2(direction.Y, direction.X) * 180 / Math.PI - 90
                : BodyRig.FootTilt(Math.Max(height, 1e-6), rest, e, w, middleBone, endBone);
            var endSize = limb.EndSize ?? 2 * limb.Thickness;
            BodyEllipse endRegion;
            Items end;
            if (limb.End is { } endShape)
            {
                // Its unit box EndSize square, hung from the wrist or ankle by its top centre
                // (0.5, 0); the shape keeps its own proportions inside it (a shoe fills only the
                // top of the box), and the ground goes under what's actually drawn.
                var shape = ModuleSockets.Flipped(endShape, mirror, false);
                var bounds = AnchorRing.CurveBounds(shape.Anchors);
                var endFrame = new ModuleFrame(default, endSize, endSize, turn).Pinned(new Point2D(0.5, 0), w);
                endRegion = endFrame.Place((new Point2D(bounds.MidX, bounds.MidY), bounds.Width / 2, bounds.Height / 2));
                end = new Items([], [], [new FigureShape(endFrame.Place(shape.Anchors), slot, part)]);
            }
            else
            {
                endRegion = arm
                    ? new BodyEllipse(Along(w, direction, endSize * 0.4), Math.Max(r1 * 1.3, endSize * 0.35), endSize / 2, turn)
                    : profile
                        ? new BodyEllipse(Add(w, Rotate(new Point2D(endSize * 0.45, r1 - endSize * 0.3), turn)), endSize * 0.75, endSize * 0.3, turn)
                        : new BodyEllipse(Add(w, Rotate(new Point2D((side.Of == LimbSide.Left ? 0.3 : -0.3) * r1, r1 - endSize * 0.3), turn)), endSize * 0.5, endSize * 0.3, turn);
                end = new Items([], [endRegion], []);
            }
            var bones = new Items(boneCapsules, [], boneShapes);
            // Capsules and ellipses are filled with the skin colour; a limb of its own colour draws them as outlines instead.
            if (slot != CharacterDefinition.SkinSlot)
                (bones, end) = (AsShapes(bones, slot, part), AsShapes(end, slot, part));
            return new LimbParts(true, frame, endRegion, bones, end);
        }

        var left = (Arm: Limb(body.Arms, Side.Left, arm: true), Leg: Limb(body.Legs, Side.Left, arm: false));
        var right = (Arm: Limb(body.Arms, Side.Right, arm: true), Leg: Limb(body.Legs, Side.Right, arm: false));
        if (body.Legs is null)
            absent.Add(new FigurePart(BodyRegion.Skirt));

        // Seams: discs at the joints in use, where a layer meets the ones painted before it.
        var joined = !body.IsSeparate;
        var hasTrunk = assembly.Torso is not null || assembly.Head is not null;
        BodyEllipse Disc(Point2D at, double radius) => new(at, radius, radius);
        var legRadius = body.Legs is { } legs ? Radii(legs).Root : 0;
        var armRadius = body.Arms is { } arms ? Radii(arms).Root : 0;
        var hipSeams = new[] { (left.Leg, Side.Left), (right.Leg, Side.Right) }
            .Where(l => joined && l.Item1.Present).Select(l => Disc(At(l.Item2.UpperLeg), legRadius * 1.5)).ToList();
        IReadOnlyList<BodyEllipse> ArmSeam(LimbParts armParts, Side side) =>
            joined && hasTrunk && armParts.Present ? [Disc(At(side.UpperArm), armRadius * 1.4)] : [];
        IReadOnlyList<BodyEllipse> headSeams = !joined || assembly.Head is not { } placedHead
            ? []
            : assembly.Torso is { } placedTorso
                ? [Disc(At(HumanoidBone.Head), 0.3 * Math.Min(Math.Abs(placedHead.Frame.Width), Math.Abs(placedTorso.Frame.Width)))]
                : hipSeams;
        IReadOnlyList<FigureShape> Shapes(params FigureShape?[] shapes) => shapes.OfType<FigureShape>().ToList();
        FigureLayer Layer(FigureLayerKind kind, Items items, IReadOnlyList<BodyEllipse> seams, params FigureShape?[] shapes) =>
            new(kind, null, items.Capsules, items.Ellipses, seams, Shapes(shapes).Concat(items.Shapes).ToList());

        List<FigureLayer> layers;
        if (profile)
        {
            // Facing +x, the character's right is the near side: the far arm behind
            // everything, then the body (torso and both legs, the far foot), the head, the
            // near foot and the near arm.
            layers =
            [
                FigureLayer.Empty(FigureLayerKind.Back),
                Layer(FigureLayerKind.FarArm, Items.Join(left.Arm.Bones, left.Arm.End), []),
                Layer(FigureLayerKind.Body, Items.Join(left.Leg.Bones, right.Leg.Bones, left.Leg.End), [], torsoShape),
                Layer(FigureLayerKind.Head, Items.None, headSeams, headShape),
                Layer(FigureLayerKind.NearFoot, right.Leg.End, []),
                Layer(FigureLayerKind.NearArm, Items.Join(right.Arm.Bones, right.Arm.End), ArmSeam(right.Arm, Side.Right)),
                FigureLayer.Empty(FigureLayerKind.Front),
            ];
        }
        else
        {
            // Each limb a layer of its own, the right over the left; legs behind the torso, arms in front.
            layers =
            [
                FigureLayer.Empty(FigureLayerKind.Back),
                Layer(FigureLayerKind.LeftLeg, Items.Join(left.Leg.Bones, left.Leg.End), []),
                Layer(FigureLayerKind.RightLeg, Items.Join(right.Leg.Bones, right.Leg.End),
                    joined && left.Leg.Present && right.Leg.Present ? [Disc(At(Side.Right.UpperLeg), legRadius * 1.5)] : []),
                Layer(FigureLayerKind.Torso, Items.None, assembly.Torso is null ? [] : hipSeams, torsoShape),
                Layer(FigureLayerKind.Head, Items.None, headSeams, headShape),
                Layer(FigureLayerKind.LeftArm, Items.Join(left.Arm.Bones, left.Arm.End), ArmSeam(left.Arm, Side.Left)),
                Layer(FigureLayerKind.RightArm, Items.Join(right.Arm.Bones, right.Arm.End), ArmSeam(right.Arm, Side.Right)),
                FigureLayer.Empty(FigureLayerKind.Front),
            ];
        }

        var regions = new FigureRegions(face, new BodyCapsule(At(HumanoidBone.Head), At(HumanoidBone.Neck), Degenerate, Degenerate), torsoFrame,
            left.Arm.Frame, right.Arm.Frame, left.Arm.EndRegion, right.Arm.EndRegion,
            left.Leg.Frame, right.Leg.Frame, left.Leg.EndRegion, right.Leg.EndRegion);

        var near = profile ? right.Arm : null;
        var limbs = new[] { left.Arm, right.Arm, left.Leg, right.Leg }.Where(l => l.Present && !ReferenceEquals(l, near)).SelectMany(l => new[] { l.Frame.Upper, l.Frame.Lower }).ToList();
        var nearLimbs = near is { Present: true } ? new List<BodyCapsule> { near.Frame.Upper, near.Frame.Lower } : [];
        var ends = new[] { (left.Arm, false), (right.Arm, profile), (left.Leg, false), (right.Leg, profile) };
        var blobs = new List<BodyEllipse> { headBlob };
        blobs.AddRange(ends.Where(x => x.Item1.Present && !x.Item2).Select(x => x.Item1.EndRegion));
        var nearBlobs = ends.Where(x => x.Item1.Present && x.Item2).Select(x => x.Item1.EndRegion).ToList();

        var extent = ExtentOf(layers);
        IReadOnlyList<Point2D> torsoOutline = torsoShape is null ? [] : torsoFrame.RestOutline.Select(trunk.Upper).ToList();
        var figure = new BodyFigure(profile ? ViewAngle.Profile : ViewAngle.Front, rest, baseLayout, layout,
            torsoOutline, limbs, blobs, nearLimbs, nearBlobs,
            extent ?? new Rect2D(At(HumanoidBone.Hips).X, At(HumanoidBone.Hips).Y, 0, 0), layers, regions, absent);
        return (figure, extent is not null);
    }

    /// <summary>A <see cref="LimbStyle.Pieces"/> piece's outline along a bone: its unit box's top centre on the bone's start, its bottom centre on its end, as wide as the limb is thick.</summary>
    private static IReadOnlyList<ShapeAnchor> PieceAlong(ModuleShape piece, BodyCapsule bone, double thickness, bool mirror)
    {
        var shape = ModuleSockets.Flipped(piece, mirror, false);
        var along = Sub(bone.To, bone.From);
        var length = Math.Sqrt(along.X * along.X + along.Y * along.Y);
        var turn = length < 1e-12 ? 0 : Math.Atan2(-along.X, along.Y) * 180 / Math.PI;
        return new ModuleFrame(Mid(bone.From, bone.To), thickness, Math.Max(length, Degenerate), turn).Place(shape.Anchors);
    }

    /// <summary>Capsules and ellipses as outlines filled with <paramref name="slot"/>.</summary>
    private static Items AsShapes(Items items, string slot, ModulePart part) =>
        new([], [], items.Shapes
            .Concat(items.Capsules.Select(c => new FigureShape(CapsuleOutline(c), slot, part)))
            .Concat(items.Ellipses.Select(e => new FigureShape(EllipseOutline(e), slot, part)))
            .ToList());

    /// <summary>A turned ellipse as four smooth anchors.</summary>
    internal static IReadOnlyList<ShapeAnchor> EllipseOutline(BodyEllipse e)
    {
        var ring = AnchorRing.Ellipse(new Rect2D(-e.RadiusX, -e.RadiusY, 2 * e.RadiusX, 2 * e.RadiusY));
        Point2D Map(Point2D p) => Add(e.Center, Rotate(p, e.RotationDegrees));
        return ring.Select(a => a with { Point = Map(a.Point), InHandle = Map(a.InHandle), OutHandle = Map(a.OutHandle) }).ToList();
    }

    /// <summary>
    /// A capsule's outline as anchors: the two circles' outer tangents and the arcs round each
    /// end (beziers of at most a quarter turn each). One circle inside the other is just that circle.
    /// </summary>
    internal static IReadOnlyList<ShapeAnchor> CapsuleOutline(BodyCapsule c)
    {
        var along = Sub(c.To, c.From);
        var d = Math.Sqrt(along.X * along.X + along.Y * along.Y);
        if (d <= Math.Abs(c.FromRadius - c.ToRadius) + 1e-9)
            return c.FromRadius >= c.ToRadius ? EllipseOutline(new BodyEllipse(c.From, c.FromRadius, c.FromRadius)) : EllipseOutline(new BodyEllipse(c.To, c.ToRadius, c.ToRadius));
        var beta = Math.Atan2(along.Y, along.X);
        var phi = Math.Asin(Math.Clamp((c.FromRadius - c.ToRadius) / d, -1, 1));
        var side = Math.PI / 2 - phi;
        var anchors = new List<ShapeAnchor>();
        // Round the end circle from one tangent to the other through the far end, then the start circle back.
        Arc(anchors, c.To, c.ToRadius, beta + side, -(Math.PI - 2 * phi));
        Arc(anchors, c.From, c.FromRadius, beta - side, -(Math.PI + 2 * phi));
        return anchors;
    }

    /// <summary>Adds an arc's anchors, its first and last with a straight edge on their outer side (the tangent lines).</summary>
    private static void Arc(List<ShapeAnchor> anchors, Point2D center, double radius, double from, double span)
    {
        var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 2) - 1e-9));
        var step = span / pieces;
        var handle = 4.0 / 3 * Math.Tan(step / 4) * radius;
        Point2D P(double a) => new(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        Point2D T(double a) => new(-Math.Sin(a), Math.Cos(a));
        for (var i = 0; i <= pieces; i++)
        {
            var a = from + step * i;
            var p = P(a);
            var inHandle = i == 0 ? p : Along(p, T(a), -handle);
            var outHandle = i == pieces ? p : Along(p, T(a), handle);
            anchors.Add(new ShapeAnchor(p, inHandle, outHandle, AnchorHandleKind.Smooth));
        }
    }

    /// <summary>The exact bounding box of everything the layers draw, or null when they draw nothing.</summary>
    private static Rect2D? ExtentOf(IReadOnlyList<FigureLayer> layers)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        void Include(Rect2D r)
        {
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }
        void Circle(Point2D at, double radius) => Include(new Rect2D(at.X - radius, at.Y - radius, 2 * radius, 2 * radius));

        foreach (var layer in layers)
        {
            foreach (var shape in layer.Shapes ?? [])
                Include(AnchorRing.CurveBounds(shape.Anchors));
            foreach (var c in layer.Capsules)
            {
                Circle(c.From, c.FromRadius);
                Circle(c.To, c.ToRadius);
            }
            foreach (var e in layer.Ellipses)
            {
                var r = e.RotationDegrees * Math.PI / 180;
                var (cos, sin) = (Math.Cos(r), Math.Sin(r));
                var (hx, hy) = (Math.Sqrt(e.RadiusX * e.RadiusX * cos * cos + e.RadiusY * e.RadiusY * sin * sin),
                    Math.Sqrt(e.RadiusX * e.RadiusX * sin * sin + e.RadiusY * e.RadiusY * cos * cos));
                Include(Rect2D.FromEdges(e.Center.X - hx, e.Center.Y - hy, e.Center.X + hx, e.Center.Y + hy));
            }
        }
        return left <= right ? Rect2D.FromEdges(left, top, right, bottom) : null;
    }

    // ---------------------------------------------------------------- small helpers

    private static Point2D Joint(ViewAngleRestLayout layout, HumanoidBone bone) => layout.Bones.First(b => b.Bone == bone).Position;

    private static Point2D Add(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);

    private static Point2D Sub(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);

    private static Point2D Mid(Point2D a, Point2D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Point2D Along(Point2D from, Point2D direction, double distance) => new(from.X + direction.X * distance, from.Y + direction.Y * distance);

    private static Point2D Rotate(Point2D v, double degrees)
    {
        if (degrees == 0)
            return v;
        var r = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return new Point2D(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static Point2D Normalize(Point2D v, Point2D fallback)
    {
        var length = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-9 ? fallback : new Point2D(v.X / length, v.Y / length);
    }

    private sealed record Side(
        LimbSide Of,
        HumanoidBone Shoulder, HumanoidBone UpperArm, HumanoidBone LowerArm, HumanoidBone Hand,
        HumanoidBone UpperLeg, HumanoidBone LowerLeg, HumanoidBone Foot)
    {
        public static readonly Side Left = new(LimbSide.Left,
            HumanoidBone.LeftShoulder, HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand,
            HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot);

        public static readonly Side Right = new(LimbSide.Right,
            HumanoidBone.RightShoulder, HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm, HumanoidBone.RightHand,
            HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot);
    }
}
