using Avalonia.Media;
using Stanley.ProjectModel.Geometry;
namespace Stanley.Editors;

/// <summary>A colour on offer for one colour slot.</summary>
public sealed record ColorSwatchChoice(string Slot, string Name, ColorValue Color)
{
	public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorHex(Color)));

	internal static string ColorHex(ColorValue c) => c.Hex.Length == 9 ? "#" + c.Hex[7..] + c.Hex[1..7] : c.Hex;
}

/// <summary>One band of a Rainbow dye, as a small swatch: clicking it marks the band, and the colour swatches then recolour it.</summary>
public sealed record RainbowBand(int Index, ColorValue Color, bool IsSelected)
{
	public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(Color)));

	public string Tip => IsSelected ? $"Band {Index + 1} - pick its colour below" : $"Band {Index + 1} - click to change its colour";
}
