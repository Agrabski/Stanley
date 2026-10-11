using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;

namespace Stanley.EditorFramework;

/// <summary>Where the side panes go around the editors.</summary>
public enum SidePaneArrangement
{
    /// <summary>The left tools left of the editors, the right tools (the Layers pane) right of them - the ribbon layout.</summary>
    Beside,

    /// <summary>
    /// One column left of the editors: the left tools on top, the right tools (the Layers
    /// pane) under them - the canvas layout, whose right-hand side belongs to the inspector,
    /// which lives outside the dock area the way the ribbon does.
    /// </summary>
    Stacked
}

/// <summary>
/// Builds the pane layout: an optional column of tool panes on the left (e.g. the page
/// navigator), one <see cref="IDocumentDock"/> holding the editor panes, and an optional
/// column of tool panes on the right (the Layers pane), with a splitter between each - or,
/// <see cref="SidePaneArrangement.Stacked"/>, both sets of tool panes in one column on the
/// left, one above the other. Hand-rolled on top of Dock.Avalonia's own model rather than a
/// bespoke tiling implementation.
/// </summary>
public static class EditorDockHost
{
    public const string EditorsDockId = "Editors";

    /// <summary>The row the left tools, the editors and the right tools sit in side by side.</summary>
    public const string MainRowId = "MainRow";

    /// <summary>The dock holding the right tools (the Layers pane) - right of the editors, or under the left tools when stacked.</summary>
    public const string RightToolsDockId = "RightTools";

    /// <summary>The column the left tools and the right tools share when <see cref="SidePaneArrangement.Stacked"/>.</summary>
    public const string SideColumnId = "SideColumn";

    /// <summary>The shortest the right tools may be squeezed to when stacked under the left ones.</summary>
    public const double StackedToolsMinHeight = 120;

    /// <summary>The narrowest the right-hand pane may be squeezed to, in device-independent pixels.</summary>
    public const double RightToolsMinWidth = 160;

    public static (IFactory Factory, IRootDock Layout) CreateLayout(params IDockable[] panes) => CreateLayout(panes, []);

    public static (IFactory Factory, IRootDock Layout) CreateLayout(IReadOnlyList<IDockable> panes, IReadOnlyList<IDockable> leftTools) =>
        CreateLayout(panes, leftTools, []);

    /// <param name="arrangement">Beside: left tools | editors | right tools. Stacked: left tools over right tools | editors.</param>
    public static (IFactory Factory, IRootDock Layout) CreateLayout(IReadOnlyList<IDockable> panes, IReadOnlyList<IDockable> leftTools, IReadOnlyList<IDockable> rightTools,
        SidePaneArrangement arrangement = SidePaneArrangement.Beside)
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
            IToolDock? leftDock = null;
            if (leftTools.Count > 0)
            {
                var toolDock = factory.CreateToolDock();
                toolDock.Id = "LeftTools";
                toolDock.Alignment = Alignment.Left;
                toolDock.Proportion = 0.15;
                toolDock.VisibleDockables = factory.CreateList(leftTools.ToArray());
                toolDock.ActiveDockable = leftTools[0];
                leftDock = toolDock;
            }

            IToolDock? rightDock = null;
            if (rightTools.Count > 0)
            {
                var toolDock = factory.CreateToolDock();
                toolDock.Id = RightToolsDockId;
                toolDock.Alignment = Alignment.Right;
                toolDock.Proportion = 0.15;
                toolDock.MinWidth = RightToolsMinWidth;
                toolDock.CanDrop = false;
                toolDock.VisibleDockables = factory.CreateList(rightTools.ToArray());
                toolDock.ActiveDockable = rightTools[0];
                rightDock = toolDock;
            }

            if (arrangement == SidePaneArrangement.Stacked && leftDock != null && rightDock != null)
            {
                // One column: the left tools on top, the right tools under them, the editors to the right.
                leftDock.Proportion = 0.4;
                rightDock.Proportion = 0.6;
                rightDock.Alignment = Alignment.Left;
                rightDock.MinWidth = double.NaN;
                rightDock.MinHeight = StackedToolsMinHeight;
                var column = factory.CreateProportionalDock();
                column.Id = SideColumnId;
                column.Orientation = Orientation.Vertical;
                column.Proportion = 0.18;
                column.VisibleDockables = factory.CreateList<IDockable>(leftDock, factory.CreateProportionalDockSplitter(), rightDock);
                column.ActiveDockable = leftDock;
                children.Add(column);
                children.Add(factory.CreateProportionalDockSplitter());
                leftDock = rightDock = null;
            }
            else if (leftDock != null)
            {
                children.Add(leftDock);
                children.Add(factory.CreateProportionalDockSplitter());
            }

            documentDock.Proportion = double.NaN;
            children.Add(documentDock);

            if (rightDock != null)
            {
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
