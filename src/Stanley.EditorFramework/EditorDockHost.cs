using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm;

namespace Stanley.EditorFramework;

/// <summary>
/// Builds the editor-panel tiling layout: one <see cref="IDocumentDock"/> holding every
/// open editor pane. Hand-rolled on top of Dock.Avalonia's own model rather than a
/// bespoke tiling implementation - this is deliberately the minimum needed to host more
/// than one pane; a second pane (an inspector, a layers list) is just another entry in
/// <see cref="CreateLayout"/>'s pane list, not a new mechanism.
/// </summary>
public static class EditorDockHost
{
    public static (IFactory Factory, IRootDock Layout) CreateLayout(params IDockable[] panes)
    {
        var factory = new Factory();

        var documentDock = factory.CreateDocumentDock();
        documentDock.Id = "Editors";
        documentDock.VisibleDockables = factory.CreateList(panes);
        documentDock.CanCreateDocument = false;
        if (panes.Length > 0)
            documentDock.ActiveDockable = panes[0];

        var root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(documentDock);
        root.DefaultDockable = documentDock;

        factory.InitLayout(root);
        return (factory, root);
    }
}
