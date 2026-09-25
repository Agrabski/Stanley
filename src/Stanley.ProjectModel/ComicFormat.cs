namespace Stanley.ProjectModel;

/// <summary>
/// What a comic was set up as - the template File › New started it from, such as a
/// newspaper strip or a webcomic: the spacing its panels are laid out with, the panels a
/// new page starts with, and, for a comic read on screen, how wide its pictures are
/// exported. The page size itself is the manifest's <see cref="SeriesManifest.DefaultPageTrim"/>.
/// </summary>
/// <param name="MarginMm">Space between the page edge and the panels.</param>
/// <param name="GutterMm">Space between neighbouring panels.</param>
/// <param name="PanelsPerRow">The panels a new page starts with: rows top to bottom, each split into this many equal panels (a daily strip is one row of four). Null starts a new page with one panel.</param>
/// <param name="ExportWidthPx">For a comic read on screen: the width in pixels a page is exported at as a picture, its height in proportion. Null exports at print resolution (300 dpi).</param>
public sealed record ComicFormat(double MarginMm, double GutterMm, IReadOnlyList<int>? PanelsPerRow = null, int? ExportWidthPx = null);
