namespace Stanley.ProjectModel;

/// <summary>Fixed print dimensions for a page: trim size plus bleed, in millimetres.</summary>
public sealed record PageTrim(double WidthMm, double HeightMm, double BleedMm);
