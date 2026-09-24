using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The character editor pane; see CharacterEditorView.axaml. Its ribbon is <see cref="CharacterEditorRibbon"/>.</summary>
public partial class CharacterEditorView : UserControl
{
    public CharacterEditorView()
    {
        InitializeComponent();
        // Click a garment on the character to work on it (the Sticker tab); click skin or
        // the background to let it go.
        Stage.PointerPressed += (_, e) =>
        {
            if (DataContext is CharacterEditorViewModel vm && e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed)
                vm.SelectSticker(Stage.StickerAt(e.GetPosition(Stage)));
        };
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public CharacterFigure Figure => Stage;
}
