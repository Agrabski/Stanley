using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
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
				owner.EditFabric(Slot, f => f with { Pattern = f.Pattern is { } p ? p with { Colors = WithColor(p, c.Color) } : null });
		});
		SetPattern = new RelayCommand<FabricChoice>(c =>
		{
			if (c is { IsCustom: true })
				owner.RequestTileImport(new(Slot, Texture: false));
			else if (c is { Tile: { } tile, TileFile: { } file })
				owner.SetTile(Slot, tile, file, texture: false);
			else if (c != null)
				owner.EditFabric(Slot, f => f with { Pattern = c.Pattern is { } kind ? new PatternFill(kind, f.Pattern is { } was ? CarriedColors(was) : [], f.Pattern?.Size, f.Pattern?.Angle) : null });
		});
		SetDye = new RelayCommand<FabricChoice>(c =>
		{
			if (c != null)
				owner.EditFabric(Slot, f => f with { Pattern = c.Pattern is { } kind ? (f.Pattern is { } was && was.Kind == kind ? was : NewDye(kind, f.Pattern, _color)) : null });
		});
		SetScheme = new RelayCommand<HairSchemeChoice>(c =>
		{
			if (c != null)
				owner.ApplyHairScheme(c.Scheme);
		});
		SetAccent = new RelayCommand<AccentChoice>(c =>
		{
			if (c != null)
				owner.SetHairAccent(c.Color);
		});
		PickCustomAccent = new RelayCommand<Control>(control =>
		{
			if (control != null)
				ColorMenus.ShowMoreColors(control, owner.HairAccent, color => owner.SetHairAccent(color));
		});
		SelectBand = new RelayCommand<RainbowBand>(band =>
		{
			if (band != null && band.Index != _band)
			{
				_band = band.Index;
				OnPropertyChanged(nameof(RainbowBands));
			}
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
				ColorMenus.ShowMoreColors(control, PatternColorInEdit, color => SetPatternColor.Execute(new ColorSwatchChoice(Slot, "Custom", color)));
		});
	}

	public string Slot { get; }

	public string Label { get; }

	/// <summary>Whether this colours hair - the hair itself, one of its pieces or one streak: its pattern section is a hair "Dye", not a clothing "Pattern".</summary>
	public bool IsHairKey => CharacterEditorViewModel.IsHairColorKey(Slot);

	/// <summary>What the dropdown's button says it colours.</summary>
	public string Tip =>
		StickerSlots.IsStreakColorKey(Slot) ? "This streak - its own colour and dye"
		: StickerSlots.HairPieces.Contains(Slot) ? $"{Label} of the hair - its own colour and dye; Same as hair on the Sticker tab gives it back"
		: $"{Label} - colour and fabric; everything this character wears in it follows";

	/// <summary>Clothes get fabrics; skin and eyes (split left/right or not) are just colours.</summary>
	public bool CanHaveFabric => Slot is not (CharacterDefinition.SkinSlot or StickerSlots.Eyes or StickerSlots.EyesLeft or StickerSlots.EyesRight);

	public IReadOnlyList<ColorSwatchChoice> Swatches { get; }

	/// <summary>Colours for the pattern's own colour.</summary>
	public IReadOnlyList<ColorSwatchChoice> PatternSwatches => Swatches;

	public System.Windows.Input.ICommand SetColor { get; }
	public System.Windows.Input.ICommand SetPatternColor { get; }
	public System.Windows.Input.ICommand SetPattern { get; }
	public System.Windows.Input.ICommand SetDye { get; }
	public System.Windows.Input.ICommand SetScheme { get; }
	public System.Windows.Input.ICommand SetAccent { get; }
	public System.Windows.Input.ICommand PickCustomAccent { get; }
	public System.Windows.Input.ICommand SelectBand { get; }
	public System.Windows.Input.ICommand SetTexture { get; }
	public System.Windows.Input.ICommand PickCustomColor { get; }
	public System.Windows.Input.ICommand PickCustomPatternColor { get; }

	public ColorValue Color => _color;

	public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(_color)));

	/// <summary>The slot's fabric as worn (the character's own, or the garment's default), or null for plain.</summary>
	public Fabric? Fabric => _fabric;

	public bool HasPattern => _fabric?.Pattern is not null;

	/// <summary>A clothing-style pattern (stripes, plaid, a tile...) is on: its colours, size and angle show.</summary>
	public bool HasClothingPattern => _fabric?.Pattern is { IsDye: false };

	public bool HasTexture => _fabric?.Texture is not null;

	// ---------------------------------------------------------------- schemes (the Hair colour only)

	/// <summary>Whether the dropdown starts with a row of schemes: the Hair colour's does, a piece's or a streak's doesn't.</summary>
	public bool HasSchemes => Slot == StickerSlots.Hair;

	/// <summary>Natural, Two-tone, Peekaboo, Fringe only, Dip-dye, Ombré and Rainbow, each as a close-up of the character with it applied.</summary>
	public IReadOnlyList<HairSchemeChoice> SchemeChoices => HasSchemes ? _owner.HairSchemeChoices : [];

	/// <summary>The colours a scheme can take as its second: the hair palette, the one picked marked.</summary>
	public IReadOnlyList<AccentChoice> AccentChoices => HasSchemes ? Swatches.Select(s => new AccentChoice(s.Name, s.Color, s.Color == _owner.HairAccent)).ToList() : [];

	/// <summary>Redraws the scheme previews and marks the accent: they show the whole head, so any edit (or a new accent) can change them.</summary>
	internal void RefreshSchemes()
	{
		if (!HasSchemes)
			return;
		OnPropertyChanged(nameof(SchemeChoices));
		OnPropertyChanged(nameof(AccentChoices));
	}

	// ---------------------------------------------------------------- dyes (hair)

	/// <summary>"Dye" for hair - a pattern laid once across each piece - "Pattern" for everything else.</summary>
	public string PatternTitle => IsHairKey ? "Dye" : "Pattern";

	/// <summary>The slot's dye, or null if it has none (or wears a clothing pattern).</summary>
	public PatternFill? Dye => _fabric?.Pattern is { IsDye: true } dye ? dye : null;

	public bool HasDye => Dye is not null;

	/// <summary>The dye picked when none was: a vivid purple - unless the hair is purple already (see <see cref="DyeColorFor"/>).</summary>
	public static ColorValue DefaultDyeColor { get; } = ColorValue.FromHex("#8e24aa");

	/// <summary>Dye colours to fall back on, in order, when the one before is too close to the hair it would dye.</summary>
	private static readonly ColorValue[] DyeFallbacks = [DefaultDyeColor, ColorValue.FromHex("#d6409f"), ColorValue.FromHex("#e0c068"), ColorValue.FromHex("#1e88e5")];

	/// <summary>
	/// The colour a fresh dye starts in on <paramref name="ground"/>: <see cref="DefaultDyeColor"/>,
	/// or the first fallback that stands out from the ground - Tips in purple on a purple fringe
	/// would show nothing, in the gallery or on the character.
	/// </summary>
	public static ColorValue DyeColorFor(ColorValue ground) =>
		DyeFallbacks.FirstOrDefault(c => Distance(c, ground) > 80, DyeFallbacks[0]);

	private static double Distance(ColorValue a, ColorValue b)
	{
		static (int R, int G, int B) Rgb(ColorValue c) => c.Hex is { Length: >= 7 } h
			? (Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16), Convert.ToInt32(h.Substring(5, 2), 16))
			: (-1000, -1000, -1000); // no colour yet: anything stands out
		var (x, y) = (Rgb(a), Rgb(b));
		return Math.Sqrt((x.R - y.R) * (x.R - y.R) + (x.G - y.G) * (x.G - y.G) + (x.B - y.B) * (x.B - y.B));
	}

	/// <summary>How much of a piece a dye covers when it has no <see cref="PatternFill.Weight"/> of its own - what the renderer draws, so the sliders agree with it.</summary>
	public static double DefaultDyeWeight(PatternKind kind) => PatternFill.DefaultDyeWeight(kind);

	/// <summary>A fresh dye of <paramref name="kind"/> in place of <paramref name="previous"/> on <paramref name="ground"/>: its colour carries over, else one that shows on the ground; Rainbow starts with its own six.</summary>
	internal static PatternFill NewDye(PatternKind kind, PatternFill? previous, ColorValue ground)
	{
		if (kind == PatternKind.Rainbow)
			return new PatternFill(kind, previous is { Kind: PatternKind.Rainbow, Colors.Count: > 1 } ? previous.Colors : PatternFill.RainbowColors);
		var color = previous is { Colors.Count: > 0 } && previous.Kind != PatternKind.Rainbow ? previous.Colors[0] : DyeColorFor(ground);
		return new PatternFill(kind, [color], Weight: DefaultDyeWeight(kind));
	}

	/// <summary>The colours a clothing pattern starts with when it replaces <paramref name="was"/>: a dye's one colour (a Rainbow's first band) carries over, a pattern's own stay.</summary>
	private static IReadOnlyList<ColorValue> CarriedColors(PatternFill was) => was.IsDye ? was.Colors.Take(1).ToList() : was.Colors;

	/// <summary>The dyes on offer: None, then Streaks, Tips, Roots, Ombré and Rainbow, each previewed on the slot's colour.</summary>
	public IReadOnlyList<FabricChoice> DyeChoices
	{
		get
		{
			var current = _fabric?.Pattern;
			return new (string Label, PatternKind? Kind)[] { ("None", null), ("Streaks", PatternKind.Streaks), ("Tips", PatternKind.Tips), ("Roots", PatternKind.Roots),
					("Ombré", PatternKind.Ombre), ("Rainbow", PatternKind.Rainbow) }
				.Select(d => new FabricChoice(d.Label, _color, new(d.Kind is { } kind ? NewDye(kind, current is { IsDye: true } ? current : null, _color) : null), d.Kind, null, current?.Kind == d.Kind))
				.ToList();
		}
	}

	/// <summary>The clothing patterns and tiles, behind "More patterns" in a hair dropdown - the pattern gallery without its None (that's the dye gallery's).</summary>
	public IReadOnlyList<FabricChoice> MorePatternChoices => PatternChoices.Skip(1).ToList();

	/// <summary>What the dye's slider sets: "Length" of tips and roots, "Blend" of an ombré, "Width" of streaks - empty for a rainbow, which has none.</summary>
	public string DyeAmountLabel => Dye?.Kind switch
	{
		PatternKind.Tips or PatternKind.Roots => "Length",
		PatternKind.Ombre => "Blend",
		PatternKind.Streaks => "Width",
		_ => ""
	};

	public string DyeAmountTip => Dye?.Kind switch
	{
		PatternKind.Tips => "How far up the ends reach",
		PatternKind.Roots => "How far down the roots reach",
		PatternKind.Ombre => "How much of the hair the fade takes in",
		PatternKind.Streaks => "How wide the streaks are",
		_ => ""
	};

	public bool HasDyeAmount => Dye is { Kind: not PatternKind.Rainbow };

	/// <summary>
	/// The dye's slider, 5-95: <see cref="PatternFill.Weight"/> as a percentage - except an
	/// ombré, whose weight is where the fade starts, so its "Blend" is the rest (more blend, higher up).
	/// </summary>
	public double DyeAmount
	{
		get => Dye is { } dye ? Math.Round(100 * AmountOf(dye.Kind, dye.Weight ?? DefaultDyeWeight(dye.Kind))) : 0;
		set => _owner.EditFabric(Slot, f => f.Pattern is { IsDye: true } dye ? f with { Pattern = dye with { Weight = WeightOf(dye.Kind, Math.Clamp(value, 5, 95) / 100) } } : f);
	}

	private static double AmountOf(PatternKind kind, double weight) => kind == PatternKind.Ombre ? 1 - weight : weight;

	private static double WeightOf(PatternKind kind, double amount) => Math.Round(kind == PatternKind.Ombre ? 1 - amount : amount, 2);

	// ---------------------------------------------------------------- rainbow bands

	private int _band;

	public bool IsRainbow => Dye is { Kind: PatternKind.Rainbow };

	/// <summary>A rainbow's colours to show and edit: its own, or the six it starts with.</summary>
	private static IReadOnlyList<ColorValue> BandColors(PatternFill rainbow) => rainbow.Colors.Count > 1 ? rainbow.Colors : PatternFill.RainbowColors;

	/// <summary>One small swatch per band of a rainbow, the one being recoloured marked; empty for any other dye.</summary>
	public IReadOnlyList<RainbowBand> RainbowBands => Dye is { Kind: PatternKind.Rainbow } rainbow
		? BandColors(rainbow).Select((color, i) => new RainbowBand(i, color, i == Math.Min(_band, BandColors(rainbow).Count - 1))).ToList()
		: [];

	/// <summary>The colour the pattern's colour swatches change: a rainbow's marked band, else its first colour.</summary>
	private ColorValue? PatternColorInEdit => _fabric?.Pattern is { } pattern
		? pattern.Kind == PatternKind.Rainbow ? BandColors(pattern)[Math.Min(_band, BandColors(pattern).Count - 1)] : pattern.Colors.FirstOrDefault()
		: null;

	/// <summary><paramref name="pattern"/>'s colours with <paramref name="color"/> in place of the one being edited: a rainbow's marked band, else the first.</summary>
	private IReadOnlyList<ColorValue> WithColor(PatternFill pattern, ColorValue color)
	{
		if (pattern.Kind != PatternKind.Rainbow)
			return [color, .. pattern.Colors.Skip(1)];
		var bands = BandColors(pattern).ToList();
		bands[Math.Min(_band, bands.Count - 1)] = color;
		return bands;
	}

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
			OnPropertyChanged(nameof(HasClothingPattern));
			OnPropertyChanged(nameof(Dye));
			OnPropertyChanged(nameof(HasDye));
			OnPropertyChanged(nameof(DyeChoices));
			OnPropertyChanged(nameof(MorePatternChoices));
			OnPropertyChanged(nameof(DyeAmountLabel));
			OnPropertyChanged(nameof(DyeAmountTip));
			OnPropertyChanged(nameof(HasDyeAmount));
			OnPropertyChanged(nameof(DyeAmount));
			OnPropertyChanged(nameof(IsRainbow));
			OnPropertyChanged(nameof(RainbowBands));
			OnPropertyChanged(nameof(HasTexture));
			OnPropertyChanged(nameof(PatternChoices));
			OnPropertyChanged(nameof(TextureChoices));
			OnPropertyChanged(nameof(PatternSize));
			OnPropertyChanged(nameof(PatternAngle));
			OnPropertyChanged(nameof(TextureStrength));
		}
	}
}