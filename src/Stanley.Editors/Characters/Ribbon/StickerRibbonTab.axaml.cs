using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>The character ribbon's contextual Sticker tab; see StickerRibbonTab.axaml: the selected sticker's fit, styles, colour and text.</summary>
public partial class StickerRibbonTab : UserControl
{
    public StickerRibbonTab()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, ArtScaleSlider, ArtTurnSlider, LengthSlider, SleevesSlider, FitSlider);
        PrintTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ViewModel is { } vm)
            {
                vm.SelectedText = PrintTextBox.Text ?? "";
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ViewModel is { } current)
            {
                PrintTextBox.Text = current.SelectedText;
                e.Handled = true;
            }
        };
    }

    private CharacterEditorViewModel? ViewModel => DataContext as CharacterEditorViewModel;

    /// <summary>A text print was just put on: straight to its text box, to type over.</summary>
    public void FocusPrintText() => Dispatcher.UIThread.Post(() =>
    {
        PrintTextBox.Focus();
        PrintTextBox.SelectAll();
    });
}
