using Dock.Model.Mvvm.Controls;

namespace Stanley.Editors;

/// <summary>
/// The Layers side pane (a dock <see cref="Tool"/> on the right of the editors): what the
/// current page's panels hold, in the order it's drawn, so overlapping things can be picked
/// from a list and moved forward or back. Like the other side panes it isn't an editor - the
/// ribbon keeps following the page being worked in.
/// </summary>
public sealed class LayersViewModel : Tool
{
    public LayersViewModel()
    {
        Id = "Layers";
        Title = "Layers";
        CanClose = false;
        CanFloat = false;
        // It lives in its own column: no pinning it away, dragging it elsewhere or turning it into a tab among the pages.
        CanPin = false;
        CanDrag = false;
        CanDockAsDocument = false;
    }
}
