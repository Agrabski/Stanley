using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Editing.Tests;

public class ThoughtCloudEditingTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    private static Panel NewRectangle(Rect2D bounds) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);

    [Fact]
    public void Create_MakesAnEmptyCloudOfTheRightKind()
    {
        var result = ThoughtCloudEditing.Create(new Rect2D(10, 10, 60, 40));

        Assert.True(result.IsValid);
        Assert.Equal(PanelKind.Cloud, result.Value.Kind);
        Assert.Empty(result.Value.CharacterInstances);
        Assert.Empty(result.Value.Bubbles);
        Assert.Null(result.Value.Trail);
        Assert.Equal(PanelShapes.Lobes * PanelShapes.AnchorsPerLobe, result.Value.Shape.Anchors.Count);
    }

    [Fact]
    public void Create_BelowMinimumSize_Fails()
    {
        var result = ThoughtCloudEditing.Create(new Rect2D(0, 0, 5, 5));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void DefaultBounds_WithAReferencePanel_IsAboutAThirdOfItAndNearItsTop()
    {
        var reference = new Rect2D(10, 10, 150, 90);

        var bounds = ThoughtCloudEditing.DefaultBounds(reference, PageBounds, PanelGrid.Default);

        Assert.Equal(50, bounds.Width, 3);
        Assert.Equal(30, bounds.Height, 3);
        Assert.True(bounds.Top >= reference.Top, "should sit at or below the reference panel's own top");
        Assert.True(bounds.Top < reference.Top + reference.Height / 2, "should stay near the top, not drift towards the middle");
        Assert.True(bounds.Left >= reference.Left && bounds.Right <= reference.Right, "should stay horizontally within the reference panel");
    }

    [Fact]
    public void DefaultBounds_WithNoSelection_IsNearTheTopOfThePage()
    {
        var live = PanelGrid.Default.LiveArea(PageBounds);

        var bounds = ThoughtCloudEditing.DefaultBounds(null, PageBounds, PanelGrid.Default);

        Assert.Equal(live.Top, bounds.Top, 3);
        Assert.True(bounds.Width < live.Width, "should be a third of the live area, not the whole page");
    }

    [Fact]
    public void DefaultBounds_NeverExtendsPastThePage()
    {
        var bounds = ThoughtCloudEditing.DefaultBounds(new Rect2D(0, 0, 10, 10), PageBounds, PanelGrid.Default);

        Assert.True(bounds.Left >= PageBounds.Left && bounds.Top >= PageBounds.Top);
        Assert.True(bounds.Right <= PageBounds.Right && bounds.Bottom <= PageBounds.Bottom);
        Assert.True(bounds.Width >= PanelLayoutEditing.MinPanelSizeMm && bounds.Height >= PanelLayoutEditing.MinPanelSizeMm);
    }

    [Fact]
    public void Resize_RegeneratesTheCloudOutlineInsteadOfStretchingIt()
    {
        var cloud = ThoughtCloudEditing.Create(new Rect2D(10, 10, 60, 40)).Value;
        var freshAtNewBounds = PanelShapes.Cloud(new Rect2D(20, 20, 90, 70));

        var resized = PanelLayoutEditing.Resize(cloud, new Rect2D(20, 20, 90, 70), PageBounds);

        Assert.True(resized.IsValid);
        Assert.Equal(PanelKind.Cloud, resized.Value.Kind);
        Assert.Equivalent(freshAtNewBounds, resized.Value.Shape, strict: true);
    }

    [Fact]
    public void Move_RegeneratesTheCloudOutlineAtTheNewPosition()
    {
        var cloud = ThoughtCloudEditing.Create(new Rect2D(10, 10, 60, 40)).Value;

        var moved = PanelLayoutEditing.Move(cloud, 15, 5, PageBounds);

        Assert.True(moved.IsValid);
        var bounds = AnchorRing.BoundingBox(moved.Value.Shape.Anchors);
        var expected = AnchorRing.BoundingBox(PanelShapes.Cloud(new Rect2D(25, 15, 60, 40)).Anchors);
        Assert.Equal(expected.Left, bounds.Left, 3);
        Assert.Equal(expected.Top, bounds.Top, 3);
    }

    [Fact]
    public void An_ordinary_rectangle_panel_still_regenerates_a_rectangle_on_resize()
    {
        var panel = NewRectangle(new Rect2D(0, 0, 100, 100));

        var resized = PanelLayoutEditing.Resize(panel, new Rect2D(10, 10, 50, 50), PageBounds);

        Assert.True(resized.IsValid);
        Assert.Equal(PanelKind.Rectangle, resized.Value.Kind);
        Assert.Equal(4, resized.Value.Shape.Anchors.Count);
    }

    [Fact]
    public void Split_ACloud_Fails()
    {
        var cloud = ThoughtCloudEditing.Create(new Rect2D(10, 10, 80, 60)).Value;

        var result = PanelLayoutEditing.Split(cloud, BoundaryOrientation.Vertical, 0.5);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PanelGutters_IgnoreThoughtClouds_SoTheyNeverFormAGutterWithAnything()
    {
        var left = NewRectangle(new Rect2D(10, 10, 93, 100));
        var right = NewRectangle(new Rect2D(107, 10, 93, 100));
        var cloud = ThoughtCloudEditing.Create(new Rect2D(20, 20, 60, 40)).Value;

        // The point that would otherwise land right in the left/right gutter.
        var hit = PanelGutters.FindAt([left, right, cloud], new Point2D(105, 50), tolerance: 1);

        Assert.NotNull(hit);
        Assert.DoesNotContain(cloud.Id, hit.Drag.PanelsBefore);
        Assert.DoesNotContain(cloud.Id, hit.Drag.PanelsAfter);
    }

    // ---------------------------------------------------------------- thought trail

    [Fact]
    public void SetTrail_AddsATrailAimedAtTheTargetFromTheNearestPointOnTheOutline()
    {
        var cloud = ThoughtCloudEditing.Create(new Rect2D(0, 0, 80, 80)).Value;
        var target = new Point2D(40, 200);

        var withTrail = ThoughtCloudEditing.SetTrail(cloud, target);

        Assert.NotNull(withTrail.Trail);
        Assert.Equal(target, withTrail.Trail!.Target);
        var expectedT = AnchorRing.NearestT(cloud.Shape.Anchors, target);
        Assert.Equal(expectedT, withTrail.Trail.AttachmentT, 6);
    }

    [Fact]
    public void RemoveTrail_TakesItOff_AndItCanBeAddedBackAfter()
    {
        var cloud = ThoughtCloudEditing.SetTrail(ThoughtCloudEditing.Create(new Rect2D(0, 0, 80, 80)).Value, new Point2D(40, 200));

        var removed = ThoughtCloudEditing.RemoveTrail(cloud);
        Assert.Null(removed.Trail);

        var readded = ThoughtCloudEditing.SetTrail(removed, new Point2D(10, 150));
        Assert.NotNull(readded.Trail);
        Assert.Equal(new Point2D(10, 150), readded.Trail!.Target);
    }

    [Fact]
    public void MoveTrailTarget_DragsOnlyTheTip()
    {
        var cloud = ThoughtCloudEditing.SetTrail(ThoughtCloudEditing.Create(new Rect2D(0, 0, 80, 80)).Value, new Point2D(40, 200));
        var attachmentBefore = cloud.Trail!.AttachmentT;

        var moved = ThoughtCloudEditing.MoveTrailTarget(cloud, new Point2D(60, 210));

        Assert.Equal(new Point2D(60, 210), moved.Trail!.Target);
        Assert.Equal(attachmentBefore, moved.Trail.AttachmentT, 6);
    }

    [Fact]
    public void SlideTrailAttachment_MovesOnlyTheBase()
    {
        var cloud = ThoughtCloudEditing.SetTrail(ThoughtCloudEditing.Create(new Rect2D(0, 0, 80, 80)).Value, new Point2D(40, 200));

        var slid = ThoughtCloudEditing.SlideTrailAttachment(cloud, new Point2D(80, 40));

        Assert.Equal(new Point2D(40, 200), slid.Trail!.Target);
        Assert.NotEqual(cloud.Trail!.AttachmentT, slid.Trail.AttachmentT);
    }

    [Fact]
    public void TrailOperations_OnACloudWithNoTrail_AreANoOp()
    {
        var cloud = ThoughtCloudEditing.Create(new Rect2D(0, 0, 80, 80)).Value;

        Assert.Same(cloud, ThoughtCloudEditing.MoveTrailTarget(cloud, new Point2D(1, 1)));
        Assert.Same(cloud, ThoughtCloudEditing.SlideTrailAttachment(cloud, new Point2D(1, 1)));
    }

    [Fact]
    public void ScaleTrail_TakesTheTargetWhereThePictureGoes()
    {
        var trail = new ThoughtTrail(0.1, new Point2D(50, 150));

        var scaled = ThoughtCloudEditing.ScaleTrail(trail, new PanelContentScale(0.5, 10, 20));

        Assert.NotNull(scaled);
        Assert.Equal(35, scaled!.Target.X, 6);
        Assert.Equal(95, scaled.Target.Y, 6);
        Assert.Equal(trail.AttachmentT, scaled.AttachmentT, 6); // a ring fraction needs no remapping
    }

    [Fact]
    public void ScaleTrail_WithNoTrail_StaysNull() =>
        Assert.Null(ThoughtCloudEditing.ScaleTrail(null, new PanelContentScale(2, 0, 0)));

    [Fact]
    public void Resize_CarriesTheTrailAlongWithTheCloudsContents()
    {
        var cloud = ThoughtCloudEditing.SetTrail(ThoughtCloudEditing.Create(new Rect2D(0, 0, 100, 100)).Value, new Point2D(50, 150));

        // Half as tall: everything in it halves, held to the top-left corner that stayed put.
        var resized = PanelLayoutEditing.Resize(cloud, new Rect2D(0, 0, 200, 50), PageBounds);

        Assert.True(resized.IsValid);
        Assert.NotNull(resized.Value.Trail);
        Assert.Equal(25, resized.Value.Trail!.Target.X, 3);
        Assert.Equal(75, resized.Value.Trail.Target.Y, 3);
    }
}
