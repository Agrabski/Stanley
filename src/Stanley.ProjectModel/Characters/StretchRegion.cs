namespace Stanley.ProjectModel.Characters;

/// <summary>
/// A <see cref="StickerKind.BuildStretch"/> sticker's <c>stretch.json</c>: a 9-slice-style
/// safe region declaring fixed margins (in the sticker art's own local units) around a
/// stretchable centre, so one asset covers the whole build slider.
/// </summary>
public sealed record StretchRegion(double LeftMargin, double TopMargin, double RightMargin, double BottomMargin);
