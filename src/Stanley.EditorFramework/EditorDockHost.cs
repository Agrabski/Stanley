using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;

namespace Stanley.EditorFramework;

/// <summary>
/// Builds the pane layout: an optional column of tool panes on the left (e.g. the page
/// navigator), a splitter, and one <see cref="IDocumentDock"/> holding the editor panes.
/// Hand-rolled on top of Dock.Avalonia's own model rather than a bespoke tiling
/// implementation.
/// </summary>
public static class EditorDockHost
{
    public const string EditorsDockId = "Editors";

    public static (IFactory Factory, IRootDock Layout) CreateLayout(params IDockable[] panes) => CreateLayout(panes, []);

    public static (IFactory Factory, IRootDock Layout) CreateLayout(IReadOnlyList<IDockable> panes, IReadOnlyList<IDockable> leftTools)
    {
        var factory = new Factory();

        var documentDock = factory.CreateDocumentDock();
        documentDock.Id = EditorsDockId;
        documentDock.VisibleDockables = factory.CreateList(panes.ToArray());
        documentDock.CanCreateDocument = false;
        if (panes.Count > 0)
            documentDock.ActiveDockable = panes[0];

        IDockable main = documentDock;
        if (leftTools.Count > 0)
        {
            var toolDock = factory.CreateToolDock();
            toolDock.Id = "LeftTools";
            toolDock.Alignment = Alignment.Left;
            toolDock.Proportion = 0.13;
            toolDock.VisibleDockables = factory.CreateList(leftTools.ToArray());
            toolDock.ActiveDockable = leftTools[0];

            documentDock.Proportion = double.NaN;
            var row = factory.CreateProportionalDock();
            row.Orientation = Orientation.Horizontal;
            row.VisibleDockables = factory.CreateList<IDockable>(toolDock, factory.CreateProportionalDockSplitter(), documentDock);
            row.ActiveDockable = documentDock;
            main = row;
        }

        var root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList(main);
        root.DefaultDockable = main;
        root.ActiveDockable = main;

        factory.InitLayout(root);
        return (factory, root);
    }
}
