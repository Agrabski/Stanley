namespace Stanley.ProjectModel;

/// <summary>A page's physical trim dimensions, in millimetres, independent of bleed.</summary>
public readonly record struct PageSize(double WidthMm, double HeightMm);
