using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors;

/// <summary>The character editor's ribbon; see CharacterEditorRibbon.axaml. Everything goes through <see cref="CharacterEditorViewModel"/>.</summary>
public partial class CharacterEditorRibbon : UserControl
{
    public CharacterEditorRibbon()
    {
        InitializeComponent();

        // One slider drag = one undo step: the gesture opens on press (before the slider
        // jumps to the pointer) and commits on release.
        foreach (var slider in new[] { HeightSlider, WeightSlider, MuscleSlider, HeadSlider, FrameSlider, LengthSlider, SleevesSlider, FitSlider, ArtScaleSlider, ArtTurnSlider })
        {
            slider.AddHandler(PointerPressedEvent, (_, _) => ViewModel?.BeginSliderDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, (_, _) => ViewModel?.EndSliderDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(PointerCaptureLostEvent, (_, _) => ViewModel?.EndSliderDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        }

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ViewModel is { } vm)
            {
                vm.Name = NameBox.Text ?? "";
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ViewModel is { } current)
            {
                NameBox.Text = current.Name;
                e.Handled = true;
            }
        };
    }

    private CharacterEditorViewModel? ViewModel => DataContext as CharacterEditorViewModel;

    private CharacterEditorViewModel? _subscribed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed.TileImportRequested -= OnTileImportRequested;
            _subscribed.ArtImportRequested -= OnArtImportRequested;
            _subscribed.SvgEditorConfigurationRequested -= OnSvgEditorConfigurationRequested;
        }
        _subscribed = ViewModel;
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.TileImportRequested += OnTileImportRequested;
            _subscribed.ArtImportRequested += OnArtImportRequested;
            _subscribed.SvgEditorConfigurationRequested += OnSvgEditorConfigurationRequested;
        }
    }

    /// <summary>Like the page ribbon: the Sticker tab appears with a selection but isn't forced open; if it goes away while shown, back to Look.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterEditorViewModel.HasSelectedSticker) && ViewModel is { HasSelectedSticker: false } && Tabs.SelectedItem == StickerTab)
            Tabs.SelectedItem = LookTab;
    }

    /// <summary>"Custom..." in a pattern or texture gallery: pick an SVG or PNG and hand it to the editor.</summary>
    private async void OnTileImportRequested(object? sender, TileImportRequest request)
    {
        if (sender is CharacterEditorViewModel editor && await PickArt(editor, request.Texture ? "Choose a texture tile (a greyscale image)" : "Choose a pattern tile (one repeat)") is { } picked)
            editor.ImportTile(request.Slot, picked.Name, picked.File, request.Texture);
    }

    /// <summary>"Import..." in a gallery: pick an SVG or PNG and make it a sticker for that slot.</summary>
    private async void OnArtImportRequested(object? sender, ArtImportRequest request)
    {
        if (sender is CharacterEditorViewModel editor && await PickArt(editor, "Import a picture to wear (SVG or PNG)") is { } picked)
            editor.ImportArt(request.Slot, picked.Name, picked.File);
    }

    /// <summary>No SVG editor is set up yet: ask, and pick up drawing again if one was chosen.</summary>
    private async void OnSvgEditorConfigurationRequested(object? sender, SvgEditorConfigurationRequest request)
    {
        if (sender is not CharacterEditorViewModel editor || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (await SvgEditorPicker.PickAsync(owner, editor.SvgEditorPath) is { Length: > 0 } chosen)
        {
            editor.SetSvgEditorPath(chosen);
            editor.DrawYourOwn(request.Slot);
        }
    }

    private async Task<(string Name, ArtFile File)?> PickArt(CharacterEditorViewModel editor, string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new("SVG or PNG") { Patterns = ["*.svg", "*.png"] }],
        });
        if (files is not [var picked])
            return null;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var file = picked.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? ArtFile.Svg(System.Text.Encoding.UTF8.GetString(bytes)) : ArtFile.Png(bytes);
            return (picked.Name, file);
        }
        catch (IOException e)
        {
            editor.ShowMessage($"Couldn't read {picked.Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;
}
