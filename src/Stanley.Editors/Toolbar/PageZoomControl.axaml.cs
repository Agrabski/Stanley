using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The canvas layout's zoom control for a page, at the right of the top bar (docs/ui-shell.md §6). See PageZoomControl.axaml.</summary>
public partial class PageZoomControl : UserControl
{
    public PageZoomControl()
    {
        InitializeComponent();
    }

    /// <summary>The zoom menu, for headless UI tests (a closed flyout's items aren't in the visual tree).</summary>
    public MenuFlyout ZoomMenu => (MenuFlyout)ZoomButton.Flyout!;
}
