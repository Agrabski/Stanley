using Avalonia.Controls;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>The selected placed character: body, view, pose, expression, look, size and order. See CharacterSection.axaml.</summary>
public partial class CharacterSection : UserControl
{
    public CharacterSection()
    {
        InitializeComponent();

        // A pose is picked with one click and then adjusted on the page, so the gallery closes
        // behind it (the face and look galleries stay open, for mixing and comparing). Posted:
        // a button runs its command after its Click event, and a closed flyout's buttons have
        // lost the DataContext their commands are bound through.
        PoseGallery.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(() => PoseGalleryButton.Flyout?.Hide()));
    }
}
