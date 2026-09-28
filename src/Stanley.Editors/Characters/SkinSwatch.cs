using Stanley.ProjectModel.Geometry;
namespace Stanley.Editors;

/// <summary>A skin colour the ribbon offers as a one-click swatch.</summary>
public sealed record SkinSwatch(string Name, ColorValue Color)
{
	public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Color.Hex));
}