using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

public sealed record PageDocument(
    IReadOnlyList<PanelId> PanelOrder,
    IReadOnlyDictionary<PanelId, Panel> Panels,
    bool LayoutLocked = false);
