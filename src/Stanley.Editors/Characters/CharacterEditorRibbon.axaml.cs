using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors;

/// <summary>The character editor's ribbon; see CharacterEditorRibbon.axaml. Each tab lives in its own control under Ribbon/ (<see cref="BodyRibbonTab"/>, <see cref="LookRibbonTab"/>, <see cref="StickerRibbonTab"/>); this owns the tab strip and the file pickers. Everything goes through <see cref="CharacterEditorViewModel"/>.</summary>
public partial class CharacterEditorRibbon : UserControl
{
    public CharacterEditorRibbon()
    {
        InitializeComponent();
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
            _subscribed.TextPrintWorn -= OnTextPrintWorn;
        }
        _subscribed = ViewModel;
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.TileImportRequested += OnTileImportRequested;
            _subscribed.ArtImportRequested += OnArtImportRequested;
            _subscribed.SvgEditorConfigurationRequested += OnSvgEditorConfigurationRequested;
            _subscribed.TextPrintWorn += OnTextPrintWorn;
        }
    }

    /// <summary>Like the page ribbon: the Sticker tab appears with a selection but isn't forced open; if it goes away while shown, back to Look.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterEditorViewModel.HasSelectedSticker) && ViewModel is { HasSelectedSticker: false } && Tabs.SelectedItem == StickerTab)
            Tabs.SelectedItem = LookTab;

    }

    /// <summary>A text print was just put on: straight to its text box on the Sticker tab, to type over.</summary>
    private void OnTextPrintWorn(object? sender, EventArgs e)
    {
        Tabs.SelectedItem = StickerTab;
        (StickerTab.Content as StickerRibbonTab)?.FocusPrintText();
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

    /// <summary>
    /// Finds a named control anywhere in the ribbon. Each tab's controls are named inside that
    /// tab's own control, so a plain lookup from here wouldn't see them; this one looks in every tab as well.
    /// </summary>
    public T? FindControl<T>(string name) where T : Control =>
        NameScopeExtensions.Find<T>(this, name) ?? Tabs.Items.OfType<TabItem>()
            .Select(tab => tab.Content).OfType<Control>().Select(content => NameScopeExtensions.Find<T>(content, name)).FirstOrDefault(found => found is not null);

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;
}
