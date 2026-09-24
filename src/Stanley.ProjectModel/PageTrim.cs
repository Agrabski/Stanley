namespace Stanley.ProjectModel;

/// <summary>Fixed print dimensions for a page: a <see cref="PageSize"/> plus a uniform bleed margin, in millimetres.</summary>
public sealed record PageTrim(PageSize Size, double BleedMm);
