using Avalonia.Media;
using Stanley.ProjectModel.Geometry;
namespace Stanley.Editors;

/// <summary>A colour on offer for one colour slot.</summary>
public sealed record ColorSwatchChoice(string Slot, string Name, ColorValue Color)
{
	public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorHex(Color)));

	internal static string ColorHex(ColorValue c) => c.Hex.Length == 9 ? "#" + c.Hex[7..] + c.Hex[1..7] : c.Hex;
}