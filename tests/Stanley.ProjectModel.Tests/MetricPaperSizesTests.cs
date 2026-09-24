namespace Stanley.ProjectModel.Tests;

public class MetricPaperSizesTests
{
    [Theory]
    [InlineData(MetricPaperSize.A0, 841, 1189)]
    [InlineData(MetricPaperSize.A1, 594, 841)]
    [InlineData(MetricPaperSize.A2, 420, 594)]
    [InlineData(MetricPaperSize.A3, 297, 420)]
    [InlineData(MetricPaperSize.A4, 210, 297)]
    [InlineData(MetricPaperSize.A5, 148, 210)]
    [InlineData(MetricPaperSize.A6, 105, 148)]
    public void Returns_the_standard_ISO_216_portrait_dimensions(MetricPaperSize preset, double expectedWidthMm, double expectedHeightMm)
    {
        var size = MetricPaperSizes.Size(preset);

        Assert.Equal(expectedWidthMm, size.WidthMm);
        Assert.Equal(expectedHeightMm, size.HeightMm);
    }

    [Fact]
    public void Each_size_is_half_the_area_of_the_next_size_up()
    {
        var sizes = new[]
        {
            MetricPaperSizes.Size(MetricPaperSize.A0),
            MetricPaperSizes.Size(MetricPaperSize.A1),
            MetricPaperSizes.Size(MetricPaperSize.A2),
            MetricPaperSizes.Size(MetricPaperSize.A3),
            MetricPaperSizes.Size(MetricPaperSize.A4),
            MetricPaperSizes.Size(MetricPaperSize.A5),
            MetricPaperSizes.Size(MetricPaperSize.A6)
        };

        for (var i = 0; i < sizes.Length - 1; i++)
        {
            var largerArea = sizes[i].WidthMm * sizes[i].HeightMm;
            var smallerArea = sizes[i + 1].WidthMm * sizes[i + 1].HeightMm;
            // Each size is independently rounded to the nearest mm, so halving isn't exact.
            Assert.Equal(largerArea / 2, smallerArea, tolerance: 500);
        }
    }

    [Fact]
    public void Each_size_keeps_the_root_two_aspect_ratio()
    {
        foreach (MetricPaperSize preset in Enum.GetValues<MetricPaperSize>())
        {
            var size = MetricPaperSizes.Size(preset);
            Assert.Equal(Math.Sqrt(2), size.HeightMm / size.WidthMm, tolerance: 0.005);
        }
    }
}
