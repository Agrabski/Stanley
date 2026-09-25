namespace Stanley.Editors;

/// <summary>Asks the view for an SVG or PNG to import as a sticker for <paramref name="Slot"/>.</summary>
public sealed record ArtImportRequest(string Slot);