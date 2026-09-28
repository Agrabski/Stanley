using SkiaSharp;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

public class ThoughtCloudRenderingTests
{
    private static readonly Rect2D Page = new(0, 0, 100, 100);

    private static Panel CloudPanel(PanelBackground? background = null, ThoughtTrail? trail = null) =>
        new(PanelId.New(), PanelShapes.Cloud(new Rect2D(10, 10, 80, 80)), background, [], [], Kind: PanelKind.Cloud, Trail: trail);

    private static SKBitmap Render(Panel panel)
    {
        const float scale = 4;
        var bitmap = new SKBitmap((int)(Page.Width * scale), (int)(Page.Height * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(scale);
        PageRenderer.Draw(canvas, Page, [panel]);
        return bitmap;
    }

    private static SKColor Pixel(SKBitmap bitmap, double xMm, double yMm) => bitmap.GetPixel((int)(xMm * 4), (int)(yMm * 4));

    [Fact]
    public void The_cloud_outline_generator_is_deterministic_and_keeps_its_lobe_count()
    {
        var bounds = new Rect2D(5, 5, 50, 30);

        var first = PanelShapes.Cloud(bounds);
        var second = PanelShapes.Cloud(bounds);

        // PanelShape holds its anchors in a plain List<T>, which record equality compares by
        // reference, not by content - Equivalent does the deep, element-wise comparison this
        // determinism check actually needs (the same way JSON round-trip tests do).
        Assert.Equivalent(first, second, strict: true);
        Assert.Equal(PanelShapes.Lobes * PanelShapes.AnchorsPerLobe, first.Anchors.Count);
    }

    [Fact]
    public void The_cloud_outline_generator_regenerates_fresh_anchors_at_new_bounds_instead_of_scaling_the_old_ones()
    {
        var small = PanelShapes.Cloud(new Rect2D(0, 0, 40, 40));
        var big = PanelShapes.Cloud(new Rect2D(0, 0, 80, 80));

        var smallWidth = AnchorRing.BoundingBox(small.Anchors).Width;
        var bigWidth = AnchorRing.BoundingBox(big.Anchors).Width;
        Assert.True(bigWidth > smallWidth * 1.5, $"doubling the bounds should give a visibly bigger outline ({smallWidth} vs {bigWidth})");
        Assert.Equal(small.Anchors.Count, big.Anchors.Count);
    }

    [Fact]
    public void The_cloud_outline_never_bulges_past_the_bounds_it_was_given()
    {
        var bounds = new Rect2D(10, 10, 80, 50);

        var box = AnchorRing.BoundingBox(PanelShapes.Cloud(bounds).Anchors);

        Assert.True(box.Left >= bounds.Left - 0.01 && box.Top >= bounds.Top - 0.01, $"{box} should stay inside {bounds}");
        Assert.True(box.Right <= bounds.Right + 0.01 && box.Bottom <= bounds.Bottom + 0.01, $"{box} should stay inside {bounds}");
    }

    [Fact]
    public void A_cloud_panel_draws_and_clips_its_background_to_the_scalloped_outline_not_its_bounding_box()
    {
        using var bitmap = Render(CloudPanel(new ColorBackground(ColorValue.FromHex("#2266cc"))));

        var center = Pixel(bitmap, 50, 50);
        var boxCorner = Pixel(bitmap, 11, 11); // inside the panel's bounding box, but outside the cloud's own outline

        Assert.True(center.Blue > 150 && center.Red < 100, $"the cloud's fill should show at its centre, was {center}");
        Assert.Equal(SKColors.White, boxCorner); // still bare paper - the fill didn't leak into the box's corner
    }

    [Fact]
    public void A_cloud_panels_border_follows_its_scalloped_outline()
    {
        var panel = CloudPanel();
        using var bitmap = Render(panel);

        // Anchor 0 is one bump's peak, touching the bounding box's edge exactly - the border
        // inks it, while the box's own corner, well outside any bump, stays bare paper.
        var peak = panel.Shape.Anchors[0].Point;
        var peakPixel = Pixel(bitmap, peak.X, peak.Y);
        var boxCorner = Pixel(bitmap, 11, 11);

        Assert.True(peakPixel.Red < 64 && peakPixel.Green < 64 && peakPixel.Blue < 64, $"the outline's border should ink the bump peak, was {peakPixel}");
        Assert.Equal(SKColors.White, boxCorner);
    }

    [Fact]
    public void A_thought_trail_draws_as_shrinking_dots_leading_towards_its_target()
    {
        var panel = CloudPanel(trail: new ThoughtTrail(0.5, new Point2D(50, 95)));

        using var bitmap = Render(panel);

        var dots = ThoughtTrailRenderer.Dots(panel.Trail!, panel.Shape);
        Assert.Equal(3, dots.Count);
        // Each dot's fill is white, like a bubble's, so its centre alone wouldn't tell it apart
        // from bare paper - its stroked rim does.
        foreach (var (center, radius) in dots)
        {
            var rim = Pixel(bitmap, center.X + radius, center.Y);
            Assert.NotEqual(SKColors.White, rim);
        }
        // ...getting smaller from the cloud towards the target.
        Assert.True(dots[0].Radius > dots[1].Radius && dots[1].Radius > dots[2].Radius,
            $"dots should shrink towards the target, radii were {string.Join(", ", dots.Select(d => d.Radius))}");
    }
}
