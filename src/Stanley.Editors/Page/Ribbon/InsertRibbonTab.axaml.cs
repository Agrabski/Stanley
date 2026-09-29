using Avalonia.Controls;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>The page ribbon's Insert tab; see InsertRibbonTab.axaml. Its galleries close behind a pick.</summary>
public partial class InsertRibbonTab : UserControl
{
    public InsertRibbonTab()
    {
        InitializeComponent();

        // A title page design is picked with one click, like Word's cover pages - the gallery
        // closes behind it. Posted: a button runs its command after its Click event, and a
        // closed flyout's buttons have lost the DataContext their commands are bound through.
        void CloseTitlePageGallery() => Dispatcher.UIThread.Post(() => TitlePageButton.Flyout?.Hide());
        TitlePageGallery.AddHandler(Button.ClickEvent, (_, _) => CloseTitlePageGallery());
        RemoveTitlePageButton.Click += (_, _) => CloseTitlePageGallery();
        // Insert › My Assets closes behind a pick the same way.
        MyAssetsGroupGallery.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(() => InsertFromMyAssetsButton.Flyout?.Hide()));
    }
}
