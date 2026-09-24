namespace Stanley.ProjectModel;

/// <summary>ISO 216 "A" series paper sizes.</summary>
public enum MetricPaperSize
{
    A0,
    A1,
    A2,
    A3,
    A4,
    A5,
    A6
}

/// <summary>
/// Portrait <see cref="PageSize"/> for each <see cref="MetricPaperSize"/>, in millimetres
/// (the standard ISO 216 rounded values). Swap <c>WidthMm</c>/<c>HeightMm</c> for a
/// landscape page.
/// </summary>
public static class MetricPaperSizes
{
    public static PageSize Size(MetricPaperSize size) => size switch
    {
        MetricPaperSize.A0 => new PageSize(841, 1189),
        MetricPaperSize.A1 => new PageSize(594, 841),
        MetricPaperSize.A2 => new PageSize(420, 594),
        MetricPaperSize.A3 => new PageSize(297, 420),
        MetricPaperSize.A4 => new PageSize(210, 297),
        MetricPaperSize.A5 => new PageSize(148, 210),
        MetricPaperSize.A6 => new PageSize(105, 148),
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
    };
}
