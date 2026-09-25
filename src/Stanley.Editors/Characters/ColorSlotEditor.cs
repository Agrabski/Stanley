using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
namespace Stanley.Editors;

/// <summary>
/// One colour slot the character's clothes use, on the Look tab: its colour, and - for
/// clothes - its fabric (pattern, texture, their size, angle and strength). Kept alive
/// across edits and refreshed in place, so its dropdown stays open while you drag its
/// sliders; each drag is one undo step.
/// </summary>
public sealed class ColorSlotEditor : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IDragGesture
{
	private readonly CharacterEditorViewModel _owner;
	private ColorValue _color;
	private Fabric? _fabric;

	internal ColorSlotEditor(CharacterEditorViewModel owner, string slot, IReadOnlyList<ColorSwatchChoice> swatches)
	{
		_owner = owner;
		Slot = slot;
		Label = CharacterEditorViewModel.ColorSlotLabel(slot);
		Swatches = swatches;
		SetColor = new RelayCommand<ColorSwatchChoice>(c => { if (c != null) owner.SetSlotColor(Slot, c.Color); });
		SetPatternColor = new RelayCommand<ColorSwatchChoice>(c =>
		{
			if (c != null)
				owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Colors = [c.Color, .. p.Colors.Skip(1)] } : null });
		});
		SetPattern = new RelayCommand<FabricChoice>(c =>
		{
			if (c is { IsCustom: true })
				owner.RequestTileImport(new(Slot, Texture: false));
			else if (c is { Tile: { } tile, TileFile: { } file })
				owner.SetTile(Slot, tile, file, texture: false);
			else if (c != null)
				owner.EditFabric(Slot, f => f with { Pattern = c.Pattern is { } kind ? new PatternFill(kind, f.Pattern?.Colors is { Count: > 0 } colors ? colors : [], f.Pattern?.Size, f.Pattern?.Angle) : null });
		});
		SetTexture = new RelayCommand<FabricChoice>(c =>
		{
			if (c is { IsCustom: true })
				owner.RequestTileImport(new(Slot, Texture: true));
			else if (c is { Tile: { } tile, TileFile: { } file })
				owner.SetTile(Slot, tile, file, texture: true);
			else if (c != null)
				owner.EditFabric(Slot, f => f with { Texture = c.Texture is { } kind ? new TextureFill(kind, f.Texture?.Strength, f.Texture?.Size) : null });
		});
		PickCustomColor = new RelayCommand<Control>(control =>
		{
			if (control != null)
				ColorMenus.ShowMoreColors(control, _color, color => SetColor.Execute(new ColorSwatchChoice(Slot, "Custom", color)));
		});
		PickCustomPatternColor = new RelayCommand<Control>(control =>
		{
			if (control != null)
				ColorMenus.ShowMoreColors(control, _fabric?.Pattern?.Colors.FirstOrDefault(), color => SetPatternColor.Execute(new ColorSwatchChoice(Slot, "Custom", color)));
		});
	}

	public string Slot { get; }

	public string Label { get; }

	/// <summary>Clothes get fabrics; skin and eyes are just colours.</summary>
	public bool CanHaveFabric => Slot is not (CharacterDefinition.SkinSlot or "eyes");

	public IReadOnlyList<ColorSwatchChoice> Swatches { get; }

	/// <summary>Colours for the pattern's own colour.</summary>
	public IReadOnlyList<ColorSwatchChoice> PatternSwatches => Swatches;

	public System.Windows.Input.ICommand SetColor { get; }
	public System.Windows.Input.ICommand SetPatternColor { get; }
	public System.Windows.Input.ICommand SetPattern { get; }
	public System.Windows.Input.ICommand SetTexture { get; }
	public System.Windows.Input.ICommand PickCustomColor { get; }
	public System.Windows.Input.ICommand PickCustomPatternColor { get; }

	public ColorValue Color => _color;

	public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(_color)));

	/// <summary>The slot's fabric as worn (the character's own, or the garment's default), or null for plain.</summary>
	public Fabric? Fabric => _fabric;

	public bool HasPattern => _fabric?.Pattern is not null;

	public bool HasTexture => _fabric?.Texture is not null;

	/// <summary>The character's tiles, for drawing this slot's swatch.</summary>
	public IReadOnlyDictionary<string, ArtFile> Tiles => _owner.Working.Wardrobe.Tiles;

	/// <summary>The generated patterns, then the library's tiles, then the character's own, then "Custom...".</summary>
	public IReadOnlyList<FabricChoice> PatternChoices
	{
		get
		{
			var colors = _fabric?.Pattern?.Colors ?? [];
			var angle = _fabric?.Pattern?.Angle;
			var current = _fabric?.Pattern;
			var choices = new (string Label, PatternKind? Kind)[] { ("None", null), ("Stripes", PatternKind.Stripes), ("Pinstripes", PatternKind.Pinstripes), ("Checks", PatternKind.Checks),
					("Plaid", PatternKind.Plaid), ("Dots", PatternKind.Dots), ("Chevron", PatternKind.Chevron) }
				.Select(p => new FabricChoice(p.Label, _color, new(p.Kind is { } kind ? new PatternFill(kind, colors, Angle: angle) : null),
					p.Kind, null, current?.Kind == p.Kind && current?.Kind != PatternKind.Tile))
				.ToList();
			foreach (var (name, file) in CharacterEditorViewModel.TileChoices(_owner.Working, texture: false))
				choices.Add(new(CharacterEditorViewModel.TileLabel(name), _color, new(new(PatternKind.Tile, colors, Angle: angle, Tile: name)),
					PatternKind.Tile, null, current is { Kind: PatternKind.Tile } && current.Tile == name, name, file));
			choices.Add(new("Custom...", _color, new(), null, null, false, IsCustom: true));
			return choices;
		}
	}

	/// <summary>The generated textures, then the character's own texture tiles, then "Custom..." (a greyscale PNG or SVG).</summary>
	public IReadOnlyList<FabricChoice> TextureChoices
	{
		get
		{
			var current = _fabric?.Texture;
			var choices = new (string Label, TextureKind? Kind)[] { ("None", null), ("Denim", TextureKind.Denim), ("Knit", TextureKind.Knit), ("Corduroy", TextureKind.Corduroy),
					("Wool", TextureKind.Wool), ("Leather", TextureKind.Leather), ("Canvas", TextureKind.Canvas), ("Felt", TextureKind.Felt) }
				.Select(t => new FabricChoice(t.Label, _color, new(Texture: t.Kind is { } kind ? new TextureFill(kind, 0.9) : null), null, t.Kind,
					current?.Kind == t.Kind && current?.Kind != TextureKind.Tile))
				.ToList();
			foreach (var (name, file) in CharacterEditorViewModel.TileChoices(_owner.Working, texture: true))
				choices.Add(new(CharacterEditorViewModel.TileLabel(name), _color, new(Texture: new(TextureKind.Tile, 0.9, Tile: name)),
					null, TextureKind.Tile, current is { Kind: TextureKind.Tile } && current.Tile == name, name, file));
			choices.Add(new("Custom...", _color, new(), null, null, false, IsCustom: true));
			return choices;
		}
	}

	/// <summary>Pattern size: one repeat as a percentage of the character's height.</summary>
	public double PatternSize
	{
		get => Math.Round((_fabric?.Pattern?.Size ?? PatternFill.DefaultSize) * 100, 1);
		set => _owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Size = Math.Round(Math.Clamp(value, 1, 25) / 100, 4) } : null });
	}

	public double PatternAngle
	{
		get => Math.Round(_fabric?.Pattern?.Angle ?? 0);
		set => _owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Angle = Math.Round(value) } : null });
	}

	/// <summary>Texture strength, 0-100.</summary>
	public double TextureStrength
	{
		get => Math.Round((_fabric?.Texture?.Strength ?? TextureFill.DefaultStrength) * 100);
		set => _owner.EditFabric(Slot, f => f with { Texture = f.Texture is { } t ? t with { Strength = Math.Round(Math.Clamp(value, 0, 100) / 100, 3) } : null });
	}

	public void BeginDrag() => _owner.BeginSliderDrag();

	public void EndDrag() => _owner.EndSliderDrag();

	private IReadOnlyDictionary<string, ArtFile>? _tiles;

	internal void Refresh(ColorValue color, Fabric? fabric)
	{
		var colorChanged = color != _color;
		var fabricChanged = !Equals(fabric, _fabric);
		var tilesChanged = !ReferenceEquals(_tiles, _owner.Working.Wardrobe.Tiles);
		_tiles = _owner.Working.Wardrobe.Tiles;
		_color = color;
		_fabric = fabric;
		if (colorChanged)
		{
			OnPropertyChanged(nameof(Color));
			OnPropertyChanged(nameof(Brush));
		}
		if (colorChanged || fabricChanged || tilesChanged)
		{
			OnPropertyChanged(nameof(Tiles));
			OnPropertyChanged(nameof(Fabric));
			OnPropertyChanged(nameof(HasPattern));
			OnPropertyChanged(nameof(HasTexture));
			OnPropertyChanged(nameof(PatternChoices));
			OnPropertyChanged(nameof(TextureChoices));
			OnPropertyChanged(nameof(PatternSize));
			OnPropertyChanged(nameof(PatternAngle));
			OnPropertyChanged(nameof(TextureStrength));
		}
	}
}