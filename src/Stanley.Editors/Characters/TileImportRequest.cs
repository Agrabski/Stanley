namespace Stanley.Editors;

/// <summary>Asks the view for a tile file to import for a colour slot - as its pattern, or its texture.</summary>
public sealed record TileImportRequest(string Slot, bool Texture);