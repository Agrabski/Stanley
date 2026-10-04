using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;

namespace Stanley.EditorFramework;

/// <summary>
/// Builds the pane layout: an optional column of tool panes on the left (e.g. the page
/// navigator), one <see cref="IDocumentDock"/> holding the editor panes, and an optional
/// column of tool panes on the right (the Layers pane), with a splitter between each.
/// Hand-rolled on top of Dock.Avalonia's own model rather than a bespoke tiling
/// implementation.
/// </summary>
public static class EditorDockHost
{
    public const string EditorsDockId = "Editors";

    /// <summary>The row the left tools, the editors and the right tools sit in side by side.</summary>
    public const string MainRowId = "MainRow";

    public const string RightToolsDockId = "RightTools";

    /// <summary>The narrowest the right-hand pane may be squeezed to, in device-independent pixels.</summary>
    public const double RightToolsMinWidth = 160;

    public static (IFactory Factory, IRootDock Layout) CreateLayout(params IDockable[] panes) => CreateLayout(panes, []);

    public static (IFactory Factory, IRootDock Layout) CreateLayout(IReadOnlyList<IDockable> panes, IReadOnlyList<IDockable> leftTools) =>
        CreateLayout(panes, leftTools, []);

    public static (IFactory Factory, IRootDock Layout) CreateLayout(IReadOnlyList<IDockable> panes, IReadOnlyList<IDockable> leftTools, IReadOnlyList<IDockable> rightTools)
    {
        var factory = new Factory();

        var documentDock = factory.CreateDocumentDock();
        documentDock.Id = EditorsDockId;
        documentDock.VisibleDockables = factory.CreateList(panes.ToArray());
        documentDock.CanCreateDocument = false;
        if (panes.Count > 0)
            documentDock.ActiveDockable = panes[0];

        IDockable main = documentDock;
        if (leftTools.Count > 0 || rightTools.Count > 0)
        {
            var children = new List<IDockable>();
            if (leftTools.Count > 0)
            {
                var toolDock = factory.CreateToolDock();
                toolDock.Id = "LeftTools";
                toolDock.Alignment = Alignment.Left;
                toolDock.Proportion = 0.15;
                toolDock.VisibleDockables = factory.CreateList(leftTools.ToArray());
                toolDock.ActiveDockable = leftTools[0];
                children.Add(toolDock);
                children.Add(factory.CreateProportionalDockSplitter());
            }

            documentDock.Proportion = double.NaN;
            children.Add(documentDock);

            if (rightTools.Count > 0)
            {
                var rightDock = factory.CreateToolDock();
                rightDock.Id = RightToolsDockId;
                rightDock.Alignment = Alignment.Right;
                rightDock.Proportion = 0.15;
                rightDock.MinWidth = RightToolsMinWidth;
                rightDock.CanDrop = false;
                rightDock.VisibleDockables = factory.CreateList(rightTools.ToArray());
                rightDock.ActiveDockable = rightTools[0];
                children.Add(factory.CreateProportionalDockSplitter());
                children.Add(rightDock);
            }

            var row = factory.CreateProportionalDock();
            row.Id = MainRowId;
            row.Orientation = Orientation.Horizontal;
            row.VisibleDockables = factory.CreateList(children.ToArray());
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
