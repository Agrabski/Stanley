using System.Runtime.CompilerServices;
using System.Xml.Linq;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using VectSharp;

namespace Stanley.Rendering;

/// <summary>How an art element is painted: a flat colour, or a gradient (points in the art's own units).</summary>
public sealed record ArtPaint(SKColor Color, ArtGradient? Gradient = null);

/// <summary>A linear (<see cref="Radius"/> null) or radial gradient, in the art's own units.</summary>
public sealed record ArtGradient(SKPoint Start, SKPoint End, float? Radius, IReadOnlyList<(SKColor Color, float Offset)> Stops);

/// <summary>
/// One drawn element of sticker art, in the SVG's own units: its outline, fill and stroke,
/// the colour slot it follows (<c>class="slot-hair"</c>, its own or an ancestor's), whether
/// it keeps to the colour alone (<c>solid</c>), and the one side of a symmetric slot it's
/// on (<c>class="side-left"</c>/<c>"side-right"</c>, docs/sticker-system.md §21 - split
/// eyes; null for most art, which isn't sided at all) - or, for PNG art, the
/// <paramref name="Image"/> filling <paramref name="Path"/>'s bounds.
/// </summary>
public sealed record ArtElement(SKPath Path, ArtPaint? Fill, ArtPaint? Stroke, float StrokeWidth, SKStrokeCap Cap, SKStrokeJoin Join,
    float[]? Dash, string? Slot, bool Solid, SKPath? Clip, bool EvenOdd, SKImage? Image = null, LimbSide? Side = null);

/// <summary>
/// Sticker art read from an SVG file (docs/sticker-system.md §6.1): its view box, its
/// top-level layers (the sticker's parts) each as a list of elements in paint order, the
/// template metadata, and a report of anything that wasn't drawn.
/// </summary>
public sealed class ParsedArt
{
    internal ParsedArt(SKRect viewBox, IReadOnlyDictionary<string, IReadOnlyList<ArtElement>> layers, IReadOnlyList<string> report, string? view, string? slot)
    {
        ViewBox = viewBox;
        Layers = layers;
        Report = report;
        View = view;
        Slot = slot;
    }

    public SKRect ViewBox { get; }

    /// <summary>Layer name → elements, back to front. A file without layers is one layer named "".</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ArtElement>> Layers { get; }

    /// <summary>What was skipped or approximated ("1 blur ignored"), for the import message.</summary>
    public IReadOnlyList<string> Report { get; }

    /// <summary>The template the file was drawn from (<c>data-stanley-view</c>/<c>-slot</c> on its root), if any.</summary>
    public string? View { get; }
    public string? Slot { get; }

    /// <summary>A part by this name takes every layer of its file, in order - what an imported file that wasn't made from a template becomes.</summary>
    public const string WholeFile = "all";

    /// <summary>The elements of the layer for <paramref name="part"/>: the layer of that name, or - for a file without layers, or a part named <see cref="WholeFile"/> - all of it.</summary>
    public IReadOnlyList<ArtElement> Part(string part) =>
        Layers.TryGetValue(part, out var elements) ? elements
        : Layers.Count == 1 && Layers.ContainsKey("") ? Layers[""]
        : part == WholeFile ? Layers.Values.SelectMany(l => l).ToList()
        : [];

    /// <summary>The bounds of everything drawn in a layer (art units), or empty.</summary>
    public SKRect Bounds(IReadOnlyList<ArtElement> elements)
    {
        var bounds = SKRect.Empty;
        foreach (var e in elements)
        {
            var b = e.Path.TightBounds;
            bounds = bounds.IsEmpty ? b : SKRect.Union(bounds, b);
        }
        return bounds;
    }
}

/// <summary>
/// The one place Stanley reads SVG: VectSharp.SVG (LGPL-3.0-only) behind this adapter, so
/// swapping the reader touches nothing else (docs/sticker-system.md §6.1). It splits the
/// file into its top-level layers (skipping the template's guide layer), remembers each
/// element's colour slot, parses each layer with VectSharp and replays the result into
/// Skia paths. The file's text itself is never changed - parsing is derived and cached.
/// </summary>
public static class StickerSvg
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";
    private const string TagPrefix = "stanley-art-";
    private static readonly ConditionalWeakTable<ArtFile, ParsedArt> Cache = new();

    /// <summary>
    /// Any art file as parsed art: an SVG through <see cref="Parse(ArtFile)"/>, a PNG as one
    /// fixed-colour image element the size of its pixels (cached per file value). Null if
    /// it can't be read.
    /// </summary>
    public static ParsedArt? ParseAny(ArtFile file)
    {
        if (file.Text is not null)
            return Parse(file);
        if (file.Bytes is not { Length: > 0 } bytes)
            return null;
        if (Cache.TryGetValue(file, out var cached))
            return cached;
        var image = SKImage.FromEncodedData(bytes);
        if (image is null)
            return null;
        var rect = SKRect.Create(0, 0, image.Width, image.Height);
        using var builder = new SKPathBuilder();
        builder.AddRect(rect);
        var element = new ArtElement(builder.Detach(), null, null, 0, SKStrokeCap.Butt, SKStrokeJoin.Miter, null, null, true, null, false, image);
        var parsed = new ParsedArt(rect, new Dictionary<string, IReadOnlyList<ArtElement>> { [""] = [element] }, [], null, null);
        Cache.AddOrUpdate(file, parsed);
        return parsed;
    }

    /// <summary>The parsed art of an SVG file (cached per file value); null for a PNG or unreadable text.</summary>
    public static ParsedArt? Parse(ArtFile file)
    {
        if (file.Text is null)
            return null;
        if (Cache.TryGetValue(file, out var cached))
            return cached;
        ParsedArt? parsed;
        try
        {
            parsed = Parse(file.Text);
        }
        catch (Exception e) when (e is System.Xml.XmlException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
        {
            parsed = null;
        }
        if (parsed != null)
            Cache.AddOrUpdate(file, parsed);
        return parsed;
    }

    /// <summary>Parses SVG text (throws on malformed XML).</summary>
    public static ParsedArt Parse(string svg)
    {
        var document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new FormatException("Not an SVG file.");
        // Inkscape declares the SVG namespace twice (default and as "svg:"); rebuilt
        // documents would then come out as <svg:svg>, which VectSharp doesn't look for.
        foreach (var alias in root.DescendantsAndSelf().SelectMany(e => e.Attributes())
                     .Where(a => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns && a.Value == Svg.NamespaceName).ToList())
            alias.Remove();
        var viewBox = ViewBoxOf(root);
        // Everything below reads the art in its view box's units, so VectSharp must draw in
        // them too: given a width and height of their own ("800px" over a 24-unit icon, as
        // icon sites write them), it scales the drawing to that size and the view box shows an
        // empty corner of it (#112). Sized to its own view box, the file draws 1:1.
        root.SetAttributeValue("viewBox", string.Join(" ", new[] { viewBox.Left, viewBox.Top, viewBox.Width, viewBox.Height }.Select(Number)));
        root.SetAttributeValue("width", Number(viewBox.Width));
        root.SetAttributeValue("height", Number(viewBox.Height));
        var report = new List<string>();
        Normalize(root, report);

        // Remember what each drawable element follows, by an id VectSharp passes through as
        // its drawing tag (elements that already have an id keep it - <use> refers to them).
        var slots = new Dictionary<string, (string? Slot, bool Solid, LimbSide? Side)>(StringComparer.Ordinal);
        var n = 0;
        foreach (var element in root.Descendants().Where(IsDrawable))
        {
            var id = (string?)element.Attribute("id");
            if (string.IsNullOrEmpty(id))
                element.SetAttributeValue("id", id = TagPrefix + n++);
            slots[id] = SlotOf(element);
        }

        var children = root.Elements().ToList();
        var layerElements = children.Where(e => e.Name.LocalName == "g" && !IsGuide(e) && IsNamedLayer(e)).ToList();
        var shared = children.Where(e => e.Name.LocalName is "defs" or "style" or "symbol").ToList();

        var layers = new Dictionary<string, IReadOnlyList<ArtElement>>(StringComparer.Ordinal);
        if (layerElements.Count == 0)
        {
            // No layers: the whole file (minus any guide) is one part.
            var copy = new XElement(root);
            foreach (var guide in copy.Elements().Where(IsGuide).ToList())
                guide.Remove();
            layers[""] = Replay(copy, viewBox, slots, report);
        }
        else
        {
            if (children.Any(e => (IsDrawable(e) || e.Name.LocalName == "g") && !IsGuide(e) && !layerElements.Contains(e)))
                report.Add("drawing outside the named layers left out");
            foreach (var layer in layerElements)
            {
                var name = (string?)layer.Attribute(Inkscape + "label") ?? (string)layer.Attribute("id")!;
                var single = new XElement(root.Name, root.Attributes(), shared.Select(s => new XElement(s)), new XElement(layer));
                var elements = Replay(single, viewBox, slots, report);
                if (layers.TryGetValue(name, out var existing))
                    elements = [.. existing, .. elements];
                layers[name] = elements;
            }
        }

        // What VectSharp may pass over silently, found in the file itself.
        var art = root.Descendants().Where(e => !e.AncestorsAndSelf().Any(IsGuide)).ToList();
        foreach (var e in art)
        {
            var style = (string?)e.Attribute("style") ?? "";
            if (e.Attribute("filter") != null || style.Contains("filter:", StringComparison.Ordinal))
                report.Add("filter (blur, shadow) ignored");
            if (e.Attribute("mask") != null || style.Contains("mask:", StringComparison.Ordinal))
                report.Add("mask ignored");
            if (e.Name.LocalName == "image")
                report.Add("embedded image skipped");
        }
        if (art.Any(e => e.Name.LocalName == "pattern"))
            report.Add("SVG pattern fill drawn as a flat colour");
        report.RemoveAll(r => r is "embedded image skipped" && !art.Any(e => e.Name.LocalName == "image"));
        var summary = report.Distinct().Select(r => report.Count(x => x == r) is var count && count > 1 ? $"{count} x {r}" : r).ToList();
        return new ParsedArt(viewBox, layers, summary, (string?)root.Attribute("data-stanley-view"), (string?)root.Attribute("data-stanley-slot"));
    }

    /// <summary>
    /// Rewrites what VectSharp reads differently from the SVG spec into what it reads
    /// right: an ellipse becomes a path (VectSharp draws it scaled, so its stroke would
    /// be scaled too), and a clip path becomes one path (VectSharp only takes a single
    /// path or rectangle there).
    /// </summary>
    private static void Normalize(XElement root, List<string> report)
    {
        foreach (var ellipse in root.Descendants().Where(e => e.Name.LocalName == "ellipse").ToList())
        {
            if (ShapeData(ellipse) is { } d)
                ellipse.ReplaceWith(AsPath(ellipse, d, "cx", "cy", "rx", "ry"));
        }
        foreach (var clip in root.Descendants().Where(e => e.Name.LocalName == "clipPath").ToList())
        {
            if ((string?)clip.Attribute("clipPathUnits") == "objectBoundingBox")
            {
                report.Add("clip path in bounding-box units ignored");
                continue;
            }
            var shapes = clip.Elements().Where(e => e.Name.LocalName is "path" or "rect" or "circle" or "polygon" or "polyline").ToList();
            if (shapes.Count == 0 || shapes.Count == 1 && shapes[0].Name.LocalName is "path" or "rect")
                continue;
            if (shapes.Any(e => e.Attribute("transform") != null))
            {
                report.Add("clip path of several transformed shapes: only the first used");
                continue;
            }
            var data = shapes.Select(ShapeData).OfType<string>().ToList();
            foreach (var shape in shapes)
                shape.Remove();
            clip.Add(new XElement(Svg + "path", new XAttribute("d", string.Join(" ", data))));
        }
    }

    private static XElement AsPath(XElement shape, string d, params string[] geometry)
    {
        var path = new XElement(Svg + "path", shape.Attributes().Where(a => !geometry.Contains(a.Name.LocalName) || a.Name.Namespace != XNamespace.None));
        path.SetAttributeValue("d", d);
        return path;
    }

    /// <summary>A basic shape's outline as path data, or null if it has none.</summary>
    private static string? ShapeData(XElement shape)
    {
        float A(string name) => ParseLength((string?)shape.Attribute(name)) ?? 0;
        string N(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (shape.Name.LocalName)
        {
            case "path":
                return (string?)shape.Attribute("d");
            case "rect":
                var (x, y, w, h) = (A("x"), A("y"), A("width"), A("height"));
                return w > 0 && h > 0 ? $"M{N(x)},{N(y)} h{N(w)} v{N(h)} h{N(-w)} Z" : null;
            case "circle" or "ellipse":
                var (cx, cy) = (A("cx"), A("cy"));
                var r = ParseLength((string?)shape.Attribute("r"));
                var rx = ParseLength((string?)shape.Attribute("rx")) ?? ParseLength((string?)shape.Attribute("ry")) ?? r ?? 0;
                var ry = ParseLength((string?)shape.Attribute("ry")) ?? ParseLength((string?)shape.Attribute("rx")) ?? r ?? 0;
                return rx > 0 && ry > 0
                    ? $"M{N(cx + rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1,1 {N(cx - rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1,1 {N(cx + rx)},{N(cy)} Z"
                    : null;
            case "polygon" or "polyline":
                var points = ((string?)shape.Attribute("points") ?? "").Split([' ', ',', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
                if (points.Length < 4)
                    return null;
                var pairs = Enumerable.Range(0, points.Length / 2).Select(i => points[2 * i] + "," + points[2 * i + 1]);
                return "M" + string.Join(" L", pairs) + " Z";
            default:
                return null;
        }
    }

    private static List<ArtElement> Replay(XElement svg, SKRect viewBox, IReadOnlyDictionary<string, (string? Slot, bool Solid, LimbSide? Side)> slots, List<string> report)
    {
        var page = VectSharp.SVG.Parser.FromString(svg.ToString(SaveOptions.DisableFormatting));
        var context = new ReplayContext(viewBox, slots, report);
        page.Graphics.CopyToIGraphicsContext(context);
        // VectSharp draws in page space, which starts at the view box's corner.
        return context.Elements;
    }

    private static SKRect ViewBoxOf(XElement root)
    {
        var box = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (box is { Length: 4 } && box.Select(ParseNumber).ToArray() is [var x, var y, var w, var h] && w > 0 && h > 0)
            return SKRect.Create(x, y, w, h);
        // No view box: the drawing is in pixels, as far as the file's own size reaches.
        var width = Pixels((string?)root.Attribute("width")) ?? 100;
        var height = Pixels((string?)root.Attribute("height")) ?? 100;
        return SKRect.Create(0, 0, width, height);
    }

    private static float ParseNumber(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static string Number(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An SVG length in pixels (CSS's 96 to the inch), or null for none, a percentage or a size of nothing.</summary>
    private static float? Pixels(string? s)
    {
        if (ParseLength(s) is not { } value || value <= 0)
            return null;
        var unit = s!.Trim().TrimStart('+', '-', '.', 'e', 'E', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim().ToLowerInvariant();
        return unit switch
        {
            "" or "px" => value,
            "in" => value * 96,
            "cm" => value * 96 / 2.54f,
            "mm" => value * 96 / 25.4f,
            "pt" => value * 96 / 72,
            "pc" => value * 16,
            _ => null,
        };
    }

    private static float? ParseLength(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var digits = new string(s.TakeWhile(c => char.IsDigit(c) || c is '.' or '-' or 'e' or 'E' or '+').ToArray());
        return float.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static bool IsDrawable(XElement e) => e.Name.LocalName is "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text" or "use" or "image";

    /// <summary>A top-level group that names a part: by its Inkscape layer name, or its id (ids this adapter gave out don't count).</summary>
    private static bool IsNamedLayer(XElement g) =>
        g.Attribute(Inkscape + "label") != null || (string?)g.Attribute("id") is { Length: > 0 } id && !id.StartsWith(TagPrefix, StringComparison.Ordinal);

    /// <summary>The template's own drawing of the body, which the art is drawn over - never part of the art.</summary>
    private static bool IsGuide(XElement e) =>
        e.Attribute("data-stanley-guide") != null
        || string.Equals((string?)e.Attribute(Inkscape + "label"), "template", StringComparison.OrdinalIgnoreCase);

    private static (string? Slot, bool Solid, LimbSide? Side) SlotOf(XElement element)
    {
        string? slot = null;
        var solid = false;
        LimbSide? side = null;
        for (var e = element; e != null; e = e.Parent)
        {
            foreach (var c in ((string?)e.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (c.StartsWith("slot-", StringComparison.Ordinal))
                    slot ??= c["slot-".Length..];
                else if (c == "solid")
                    solid = true;
                else if (c == "side-left")
                    side ??= LimbSide.Left;
                else if (c == "side-right")
                    side ??= LimbSide.Right;
            }
        }
        return (slot, solid, side);
    }

    /// <summary>
    /// Receives VectSharp's drawing and keeps it as Skia paths in the SVG's own units: the
    /// transform stack, the path being built, clipping, and each fill or stroke as an
    /// element with its tag's colour slot.
    /// </summary>
    private sealed class ReplayContext(SKRect viewBox, IReadOnlyDictionary<string, (string? Slot, bool Solid, LimbSide? Side)> slots, List<string> report) : IGraphicsContext
    {
        private SKMatrix _matrix = SKMatrix.CreateTranslation(viewBox.Left, viewBox.Top);
        private SKPath? _clip;
        private readonly Stack<(SKMatrix Matrix, SKPath? Clip)> _saved = new();
        private SKPathBuilder _path = new();

        public List<ArtElement> Elements { get; } = [];

        public double Width => viewBox.Width;
        public double Height => viewBox.Height;
        public Font Font { get; set; } = new(FontFamily.ResolveFontFamily(FontFamily.StandardFontFamilies.Helvetica), 12);
        public TextBaselines TextBaseline { get; set; }
        public Brush FillStyle { get; private set; } = Colour.FromRgb(0, 0, 0);
        public Brush StrokeStyle { get; private set; } = Colour.FromRgb(0, 0, 0);
        public double LineWidth { get; set; } = 1;
        public LineCaps LineCap { get; set; }
        public LineJoins LineJoin { get; set; }
        public string Tag { get; set; } = "";
        private LineDash _dash = LineDash.SolidLine;

        public void Save() => _saved.Push((_matrix, _clip));

        public void Restore()
        {
            if (_saved.Count > 0)
                (_matrix, _clip) = _saved.Pop();
        }

        public void Translate(double x, double y) => _matrix = _matrix.PreConcat(SKMatrix.CreateTranslation((float)x, (float)y));
        public void Rotate(double angle) => _matrix = _matrix.PreConcat(SKMatrix.CreateRotation((float)angle));
        public void Scale(double x, double y) => _matrix = _matrix.PreConcat(SKMatrix.CreateScale((float)x, (float)y));

        public void Transform(double a, double b, double c, double d, double e, double f) =>
            _matrix = _matrix.PreConcat(new SKMatrix((float)a, (float)c, (float)e, (float)b, (float)d, (float)f, 0, 0, 1));

        private SKPoint P(double x, double y) => _matrix.MapPoint((float)x, (float)y);

        public void MoveTo(double x, double y) => _path.MoveTo(P(x, y));
        public void LineTo(double x, double y) => _path.LineTo(P(x, y));
        public void CubicBezierTo(double p1X, double p1Y, double p2X, double p2Y, double p3X, double p3Y) => _path.CubicTo(P(p1X, p1Y), P(p2X, p2Y), P(p3X, p3Y));
        public void Close() => _path.Close();

        public void Rectangle(double x0, double y0, double width, double height)
        {
            MoveTo(x0, y0);
            LineTo(x0 + width, y0);
            LineTo(x0 + width, y0 + height);
            LineTo(x0, y0 + height);
            Close();
        }

        public void SetFillStyle((int r, int g, int b, double a) style) => FillStyle = Colour.FromRgba((byte)style.r, (byte)style.g, (byte)style.b, style.a);
        public void SetFillStyle(Brush style) => FillStyle = style;
        public void SetStrokeStyle((int r, int g, int b, double a) style) => StrokeStyle = Colour.FromRgba((byte)style.r, (byte)style.g, (byte)style.b, style.a);
        public void SetStrokeStyle(Brush style) => StrokeStyle = style;
        public void SetLineDash(LineDash dash) => _dash = dash;

        public void Fill(FillRule fillRule = FillRule.NonZeroWinding) => Emit(fill: true, fillRule == FillRule.EvenOdd);

        public void Stroke() => Emit(fill: false, evenOdd: false);

        public void SetClippingPath()
        {
            var path = TakePath();
            if (_clip is null)
            {
                _clip = path;
                return;
            }
            var both = _clip.Op(path, SKPathOp.Intersect) ?? path;
            _clip = both;
        }

        public void FillText(string text, double x, double y)
        {
            TextAsPath(text, x, y);
            Emit(fill: true, evenOdd: false);
            report.Add("text drawn with a standard font (convert text to paths to keep yours)");
        }

        public void StrokeText(string text, double x, double y)
        {
            TextAsPath(text, x, y);
            Emit(fill: false, evenOdd: false);
        }

        private void TextAsPath(string text, double x, double y)
        {
            var glyphs = new GraphicsPath().AddText(x, y, text, Font, TextBaseline);
            foreach (var segment in glyphs.Segments)
            {
                var pts = segment.Points;
                switch (segment.Type)
                {
                    case SegmentType.Move: MoveTo(pts[0].X, pts[0].Y); break;
                    case SegmentType.Line: LineTo(pts[0].X, pts[0].Y); break;
                    case SegmentType.CubicBezier: CubicBezierTo(pts[0].X, pts[0].Y, pts[1].X, pts[1].Y, pts[2].X, pts[2].Y); break;
                    case SegmentType.Close: Close(); break;
                    default:
                        foreach (var linear in segment.Linearise(null, 1))
                            if (linear.Points is { Length: > 0 } lp)
                                LineTo(lp[^1].X, lp[^1].Y);
                        break;
                }
            }
        }

        // Images and filters are reported from the file itself (VectSharp doesn't always get here).
        public void DrawRasterImage(int sourceX, int sourceY, int sourceWidth, int sourceHeight, double destinationX, double destinationY, double destinationWidth, double destinationHeight, RasterImage image) { }

        /// <summary>A filtered group (a blur, a mask): drawn without its filter.</summary>
        public void DrawFilteredGraphics(Graphics graphics, VectSharp.Filters.IFilter filter) => graphics.CopyToIGraphicsContext(this);

        private SKPath TakePath(bool evenOdd = false)
        {
            _path.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            var path = _path.Detach();
            _path.Dispose();
            _path = new SKPathBuilder();
            return path;
        }

        private void Emit(bool fill, bool evenOdd)
        {
            var path = TakePath(evenOdd);
            if (path.IsEmpty)
                return;
            var (slot, solid, side) = slots.TryGetValue(Tag ?? "", out var s) ? s : (null, false, null);
            var scale = MathF.Sqrt(MathF.Abs(_matrix.ScaleX * _matrix.ScaleY - _matrix.SkewX * _matrix.SkewY));
            var paint = ToPaint(fill ? FillStyle : StrokeStyle);
            if (paint is null)
                return;
            Elements.Add(new ArtElement(path,
                fill ? paint : null,
                fill ? null : paint,
                (float)LineWidth * scale,
                LineCap switch { LineCaps.Round => SKStrokeCap.Round, LineCaps.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt },
                LineJoin switch { LineJoins.Round => SKStrokeJoin.Round, LineJoins.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter },
                _dash.DashArray is { Length: > 0 } dashes && dashes.Any(d => d > 0) ? dashes.Select(d => (float)(d * scale)).ToArray() : null,
                slot, solid, _clip, evenOdd, Side: side));
        }

        private ArtPaint? ToPaint(Brush brush)
        {
            switch (brush)
            {
                case SolidColourBrush solid:
                    return new ArtPaint(ToSk(solid.Colour));
                case LinearGradientBrush linear:
                    return new ArtPaint(ToSk(linear.GradientStops[0].Colour),
                        new ArtGradient(P(linear.StartPoint.X, linear.StartPoint.Y), P(linear.EndPoint.X, linear.EndPoint.Y), null, Stops(linear.GradientStops)));
                case RadialGradientBrush radial:
                    var centre = P(radial.Centre.X, radial.Centre.Y);
                    var edge = P(radial.Centre.X + radial.Radius, radial.Centre.Y);
                    var r = SKPoint.Distance(centre, edge);
                    return new ArtPaint(ToSk(radial.GradientStops[0].Colour), new ArtGradient(centre, centre, r, Stops(radial.GradientStops)));
                default:
                    return null;
            }
        }

        private static List<(SKColor, float)> Stops(GradientStops stops) =>
            Enumerable.Range(0, stops.Count).Select(i => (ToSk(stops[i].Colour), (float)stops[i].Offset)).ToList();

        private static SKColor ToSk(Colour c) =>
            new((byte)Math.Round(Math.Clamp(c.R, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.G, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.B, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.A, 0, 1) * 255));
    }
}
