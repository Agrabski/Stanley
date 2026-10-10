using Avalonia.Controls;

namespace Stanley.App;

/// <summary>The canvas layout's top bar; see CanvasTopBar.axaml. Everything on it goes through <see cref="MainWindowViewModel"/> or the active editor's view model.</summary>
public partial class CanvasTopBar : UserControl
{
    public CanvasTopBar()
    {
        InitializeComponent();
    }

    /// <summary>The main menu's items, for headless UI tests (a closed flyout's items aren't in the visual tree).</summary>
    public MenuFlyout MainMenu => (MenuFlyout)MainMenuButton.Flyout!;
}
