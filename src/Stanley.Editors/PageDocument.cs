using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <param name="IsTitlePage">The comic's title page (<see cref="Page.TitlePage"/>): Insert › Title page redoes this page rather than adding another.</param>
public sealed record PageDocument(
    IReadOnlyList<PanelId> PanelOrder,
    IReadOnlyDictionary<PanelId, Panel> Panels,
    bool LayoutLocked = false,
    bool IsTitlePage = false);
