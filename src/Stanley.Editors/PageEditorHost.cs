using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

public static class PageEditorHost
{
    /// <summary>An A4 page (the project-wide default trim) holding one panel that fills the live area inside the default margin - a blank page ready to be split or given a layout.</summary>
    public static (EditorWorkspace Workspace, PageEditorViewModel Editor) CreateDemoWorkspace()
    {
        var history = new EditorHistory();
        var paper = MetricPaperSizes.Size(MetricPaperSize.A4);
        var pageBounds = new Rect2D(0, 0, paper.WidthMm, paper.HeightMm);
        var panelId = PanelId.New();
        var panel = new PanelModel(
            panelId,
            PanelShapes.Rectangle(PanelGrid.Default.LiveArea(pageBounds)),
            Background: null,
            CharacterInstances: [],
            Bubbles: []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, PanelModel> { [panelId] = panel });
        var editor = new PageEditorViewModel(history, pageBounds, document);
        return (new EditorWorkspace(history, editor), editor);
    }
}
