using Avalonia.Controls;
using Avalonia.Data.Converters;
using Stanley.Editors;

namespace Stanley.App;

/// <summary>The canvas layout's top bar; see CanvasTopBar.axaml. Everything on it goes through <see cref="MainWindowViewModel"/> or the active editor's view model.</summary>
public partial class CanvasTopBar : UserControl
{
    public CanvasTopBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether the active editor is a page: the main menu's Edit and View items act on
    /// <see cref="MainWindowViewModel.Editor"/>, the current page, which is behind a character
    /// being edited - so they're off then, rather than changing a page nobody can see.
    /// </summary>
    public static IValueConverter IsPageEditor { get; } = new FuncValueConverter<object?, bool>(editor => editor is PageEditorViewModel);

    /// <summary>The main menu's items, for headless UI tests (a closed flyout's items aren't in the visual tree).</summary>
    public MenuFlyout MainMenu => (MenuFlyout)MainMenuButton.Flyout!;
}
