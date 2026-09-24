using Dock.Model.Core;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

public static class PageEditorHost
{
    public static (EditorHistory History, IRootDock Layout, PageEditorViewModel Editor) CreateDemoLayout()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297); // A4 in mm
        var panelId = PanelId.New();
        var panel = new PanelModel(
            panelId,
            PanelShapes.Rectangle(pageBounds),
            Background: null,
            CharacterInstances: [],
            Bubbles: []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, PanelModel> { [panelId] = panel });
        var editor = new PageEditorViewModel(history, pageBounds, document);
        var (_, layout) = EditorDockHost.CreateLayout(editor);
        return (history, layout, editor);
    }
}
