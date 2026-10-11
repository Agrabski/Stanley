using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>The worn sticker picked on the stage: its fit, styles, colour and text. See StickerSection.axaml.</summary>
public partial class StickerSection : UserControl
{
    public StickerSection()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, ArtScaleSlider, ArtTurnSlider, LengthSlider, SleevesSlider, FitSlider);
        // Enter applies the text (as leaving the box does), Escape puts the old text back.
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

    /// <summary>
    /// A text print was just put on: its text box scrolled into view and focused, to type over.
    /// Posted, so the section is laid out with the print's group showing by then.
    /// </summary>
    public void FocusPrintText() => Dispatcher.UIThread.Post(() =>
    {
        UpdateLayout();
        PrintTextBox.BringIntoView();
        PrintTextBox.Focus();
        PrintTextBox.SelectAll();
    });
}
