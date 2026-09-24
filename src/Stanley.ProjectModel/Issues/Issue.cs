using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// <c>issues/&lt;id&gt;-slug/issue.json</c>. <see cref="Number"/> and <see cref="Title"/>
/// are separate from storage order (the series manifest's own <c>IssueIds</c> array), so
/// a #0 preview or a #1.5 annual doesn't force renumbering folders - <see cref="Number"/>
/// is therefore a free-form display label, not a sequential integer.
/// </summary>
/// <param name="CharacterRevisions">Default characterId -&gt; revisionId map for this issue; a panel's character instance can still override it per-panel.</param>
public sealed record Issue(
    IssueId Id,
    string Number,
    string Title,
    IReadOnlyList<PageId> PageIds,
    SortedDictionary<CharacterId, CharacterRevisionId> CharacterRevisions);
