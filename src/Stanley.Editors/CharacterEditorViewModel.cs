using CommunityToolkit.Mvvm.Input;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>One body type in the ribbon's gallery, with a preview figure.</summary>
public sealed record BodyPresetChoice(BodyPreset Preset, string Name, CharacterDefinition Preview);

/// <summary>A skin colour the ribbon offers as a one-click swatch.</summary>
public sealed record SkinSwatch(string Name, ColorValue Color)
{
    public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Color.Hex));
}

/// <summary>
/// Edits one character's definition - for the POC, its body: a preset gallery and five
/// sliders (height, weight, muscle, head size, shoulders/hips), plus name and skin colour.
/// Like a page, each character has its own editor, all committing into the one shared
/// <see cref="EditorHistory"/>; every placed instance draws from the definition, so an
/// edit here shows up on every page at once (live, while a slider is still being dragged).
///
/// Sliders are gestures: the view calls <see cref="BeginSliderDrag"/> on press and
/// <see cref="EndSliderDrag"/> on release, so one drag is one undo step. A value set
/// outside a drag (keyboard, a spin box) is its own undo step.
/// </summary>
public sealed partial class CharacterEditorViewModel : EditorViewModel<CharacterDefinition>
{
    private bool _showLineUp = true;
    private ViewAngle _previewAngle = ViewAngle.Front;

    public CharacterEditorViewModel(EditorHistory history, CharacterDefinition initial, CharacterLibraryViewModel? library = null)
        : base(history, initial.Name, initial)
    {
        Library = library;
        Id = $"character-{initial.Id.Value}";
        CanClose = false;
        CanFloat = false;

        _presetSkin = initial.Skin;
        ApplyPresetCommand = new RelayCommand<BodyPresetChoice>(choice =>
        {
            if (choice != null)
                SetBody(BodyPresets.Shape(choice.Preset));
        });
        SetSkinCommand = new RelayCommand<SkinSwatch>(swatch =>
        {
            if (swatch != null)
                SetSkin(swatch.Color);
        });
        BackToPageCommand = new RelayCommand(() => Library?.ReturnToPage(), () => Library != null);
        InitializeLook();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Working))
            {
                RaiseBodyChanged();
                RaiseLookChanged();
            }
        };
        if (library != null)
            library.CharactersChanged += () =>
            {
                OnPropertyChanged(nameof(LineUp));
                OnPropertyChanged(nameof(ShownLineUp));
            };
    }

    public CharacterLibraryViewModel? Library { get; }

    public CharacterId CharacterId => Committed.Id;

    /// <summary>The body-type gallery, previewed in this character's skin colour.</summary>
    public IReadOnlyList<BodyPresetChoice> Presets =>
        BodyPresets.All.Select(p => new BodyPresetChoice(p, p.ToString(), Working with { Body = BodyPresets.Shape(p), Skeleton = new Skeleton([]) })).ToList();

    public IReadOnlyList<SkinSwatch> SkinSwatches => Swatches;

    public IRelayCommand<BodyPresetChoice> ApplyPresetCommand { get; }
    public IRelayCommand<SkinSwatch> SetSkinCommand { get; }

    /// <summary>"Close" in the ribbon, like Word's "Close Header and Footer": back to the page being worked on.</summary>
    public IRelayCommand BackToPageCommand { get; }

    // ---------------------------------------------------------------- name

    /// <summary>Renaming is one undo step (the ribbon's box commits on Enter / leaving it).</summary>
    public string Name
    {
        get => Working.Name;
        set
        {
            var name = (value ?? "").Trim();
            if (name.Length == 0 || name == Working.Name)
            {
                OnPropertyChanged();
                return;
            }
            Apply(EditResult<CharacterDefinition>.Success(Committed with { Name = name }));
        }
    }

    // ---------------------------------------------------------------- body sliders

    /// <summary>Height relative to an average adult, in percent (100 = average adult).</summary>
    public double HeightPercent
    {
        get => Math.Round(Working.Body.Height * 100);
        set => EditBody(b => b with { Height = value / 100 });
    }

    /// <summary>Weight: 0 slim - 100 heavy.</summary>
    public double Weight
    {
        get => Math.Round(Working.Body.Build * 100);
        set => EditBody(b => b with { Build = value / 100 });
    }

    public double Muscle
    {
        get => Math.Round(Working.Body.Muscle * 100);
        set => EditBody(b => b with { Muscle = value / 100 });
    }

    /// <summary>Head size, as "heads tall" (3 chibi - 9 heroic).</summary>
    public double HeadsTall
    {
        get => Math.Round(Working.Body.HeadsTall, 1);
        set => EditBody(b => b with { HeadsTall = value });
    }

    /// <summary>Shoulders vs hips: 0 broad shoulders - 100 wide hips.</summary>
    public double Frame
    {
        get => Math.Round(Working.Body.Frame * 100);
        set => EditBody(b => b with { Frame = value / 100 });
    }

    public string HeightText => $"{HeightPercent:0}%";
    public string HeadsTallText => $"{HeadsTall:0.#} heads";

    public double MinHeightPercent => BodyShape.MinHeight * 100;
    public double MaxHeightPercent => BodyShape.MaxHeight * 100;
    public double MinHeadsTall => BodyShape.MinHeadsTall;
    public double MaxHeadsTall => BodyShape.MaxHeadsTall;

    public void BeginSliderDrag() => BeginGesture();

    public void EndSliderDrag() => CommitGesture();

    /// <summary>Applies <paramref name="edit"/> (which sets one value) to the body: a live preview inside a slider drag, otherwise one undo step.</summary>
    private void EditBody(Func<BodyShape, BodyShape> edit)
    {
        var body = edit(Working.Body).Normalized();
        if (body == Working.Body)
            return;
        var result = EditResult<CharacterDefinition>.Success(Working with { Body = body });
        if (IsGestureActive)
            UpdateGesture(result);
        else
            Apply(result);
    }

    public void SetBody(BodyShape body) =>
        Apply(EditResult<CharacterDefinition>.Success(Committed with { Body = body.Normalized() }));

    public void SetSkin(ColorValue color)
    {
        var slots = new SortedDictionary<string, ColorValue>(Committed.ColorSlots) { [CharacterDefinition.SkinSlot] = color };
        Apply(EditResult<CharacterDefinition>.Success(Committed with { ColorSlots = slots }));
    }

    private void RaiseBodyChanged()
    {
        Title = Working.Name;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HeightPercent));
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(Muscle));
        OnPropertyChanged(nameof(HeadsTall));
        OnPropertyChanged(nameof(Frame));
        OnPropertyChanged(nameof(HeightText));
        OnPropertyChanged(nameof(HeadsTallText));
        if (Working.Skin != _presetSkin)
        {
            _presetSkin = Working.Skin;
            OnPropertyChanged(nameof(Presets));
        }
    }

    private ColorValue _presetSkin;

    // ---------------------------------------------------------------- view

    /// <summary>Whether the other characters stand faded beside this one, for comparing heights.</summary>
    public bool ShowLineUp
    {
        get => _showLineUp;
        set
        {
            if (SetProperty(ref _showLineUp, value))
                OnPropertyChanged(nameof(ShownLineUp));
        }
    }

    /// <summary>Which way the stage shows the character (and the line-up): only a preview, not part of the character.</summary>
    public ViewAngle PreviewAngle
    {
        get => _previewAngle;
        set
        {
            SetProperty(ref _previewAngle, value);
            OnPropertyChanged(nameof(IsFrontPreview));
            OnPropertyChanged(nameof(IsSidePreview));
        }
    }

    public bool IsFrontPreview { get => PreviewAngle == ViewAngle.Front; set => PreviewAngle = value ? ViewAngle.Front : PreviewAngle; }
    public bool IsSidePreview { get => PreviewAngle == ViewAngle.Profile; set => PreviewAngle = value ? ViewAngle.Profile : PreviewAngle; }

    /// <summary>What the stage draws beside the character: the line-up, or nobody.</summary>
    public IReadOnlyList<CharacterDefinition> ShownLineUp => ShowLineUp ? LineUp : [];

    /// <summary>The project's other characters, for the line-up.</summary>
    public IReadOnlyList<CharacterDefinition> LineUp =>
        Library?.InOrder.Where(c => c.Id != CharacterId).ToList() ?? [];

    public string Hint => _message ??
        "Pick a body type and fine-tune it with the sliders; dress them on the Look tab, and click something they wear to adjust it. " +
        "Every panel this character is in updates as you go.";

    private string? _message;

    /// <summary>Shows <paramref name="message"/> in the status bar instead of the usual hint (an import's report, say); null goes back to the hint.</summary>
    public void ShowMessage(string? message)
    {
        if (_message == message)
            return;
        _message = message;
        OnPropertyChanged(nameof(Hint));
    }

    private static readonly IReadOnlyList<SkinSwatch> Swatches =
    [
        new("Porcelain", ColorValue.FromHex("#fbe3d3")),
        new("Light", CharacterDefinition.DefaultSkin),
        new("Tan", ColorValue.FromHex("#d9a47c")),
        new("Olive", ColorValue.FromHex("#c08a5b")),
        new("Brown", ColorValue.FromHex("#94603b")),
        new("Dark", ColorValue.FromHex("#5e3a24")),
        new("Grey", ColorValue.FromHex("#c7cbd1")),
        new("Green", ColorValue.FromHex("#9ccc8a")),
        new("Blue", ColorValue.FromHex("#8fb8e8")),
    ];
}
