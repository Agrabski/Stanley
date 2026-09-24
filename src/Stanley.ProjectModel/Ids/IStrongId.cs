namespace Stanley.ProjectModel.Ids;

/// <summary>
/// Common shape shared by every strongly-typed entity id: a validated opaque token
/// (never a filename or array position) that also names the entity's on-disk
/// `&lt;id&gt;[-slug]` folder or file, so every cross-reference is checked at
/// construction time instead of failing on a stray '/' deep inside <c>ProjectRepository</c>.
/// </summary>
public interface IStrongId<TSelf> where TSelf : struct, IStrongId<TSelf>
{
    string Value { get; }

    static abstract TSelf FromValue(string value);
}
