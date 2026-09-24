using System.Text.Json.Serialization;

namespace Stanley.ProjectModel.Ids;

// One strong id type per stable-id entity in the project/data model design
// (docs/character-and-project-plan.md, "Git-friendliness rules"). All of them share the
// same shape, so they're kept together here rather than one near-empty file each.
//
// `New()` mints a fresh opaque token for a brand-new entity; `FromValue`/`Parse` bring
// an id back in from a folder/file name or a JSON cross-reference, validating it as
// filename-safe either way.

/// <summary>Identifies a <c>characters/&lt;id&gt;-slug/</c> character definition.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<CharacterId>))]
public readonly record struct CharacterId : IStrongId<CharacterId>, IParsable<CharacterId>, IComparable<CharacterId>
{
    public string Value { get; }
    private CharacterId(string value) => Value = value;
    public static CharacterId New() => new(EntityIdValue.NewToken());
    public static CharacterId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static CharacterId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out CharacterId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new CharacterId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(CharacterId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>characters/&lt;characterId&gt;-slug/revisions/&lt;id&gt;-slug.json</c> revision.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<CharacterRevisionId>))]
public readonly record struct CharacterRevisionId : IStrongId<CharacterRevisionId>, IParsable<CharacterRevisionId>, IComparable<CharacterRevisionId>
{
    public string Value { get; }
    private CharacterRevisionId(string value) => Value = value;
    public static CharacterRevisionId New() => new(EntityIdValue.NewToken());
    public static CharacterRevisionId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static CharacterRevisionId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out CharacterRevisionId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new CharacterRevisionId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(CharacterRevisionId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>characters/&lt;characterId&gt;-slug/stickers/&lt;id&gt;-slug/</c> sticker.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<StickerId>))]
public readonly record struct StickerId : IStrongId<StickerId>, IParsable<StickerId>, IComparable<StickerId>
{
    public string Value { get; }
    private StickerId(string value) => Value = value;
    public static StickerId New() => new(EntityIdValue.NewToken());
    public static StickerId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static StickerId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out StickerId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new StickerId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(StickerId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>poses/&lt;id&gt;-slug.json</c> pose library entry.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<PoseId>))]
public readonly record struct PoseId : IStrongId<PoseId>, IParsable<PoseId>, IComparable<PoseId>
{
    public string Value { get; }
    private PoseId(string value) => Value = value;
    public static PoseId New() => new(EntityIdValue.NewToken());
    public static PoseId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static PoseId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out PoseId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new PoseId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(PoseId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>props/&lt;id&gt;-slug/</c> reusable prop.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<PropId>))]
public readonly record struct PropId : IStrongId<PropId>, IParsable<PropId>, IComparable<PropId>
{
    public string Value { get; }
    private PropId(string value) => Value = value;
    public static PropId New() => new(EntityIdValue.NewToken());
    public static PropId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static PropId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out PropId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new PropId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(PropId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies one prop placement within a background layer's ordered placement list.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<PropPlacementId>))]
public readonly record struct PropPlacementId : IStrongId<PropPlacementId>, IParsable<PropPlacementId>, IComparable<PropPlacementId>
{
    public string Value { get; }
    private PropPlacementId(string value) => Value = value;
    public static PropPlacementId New() => new(EntityIdValue.NewToken());
    public static PropPlacementId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static PropPlacementId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out PropPlacementId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new PropPlacementId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(PropPlacementId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>backgrounds/&lt;id&gt;-slug/</c> reusable location.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<BackgroundId>))]
public readonly record struct BackgroundId : IStrongId<BackgroundId>, IParsable<BackgroundId>, IComparable<BackgroundId>
{
    public string Value { get; }
    private BackgroundId(string value) => Value = value;
    public static BackgroundId New() => new(EntityIdValue.NewToken());
    public static BackgroundId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static BackgroundId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out BackgroundId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new BackgroundId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(BackgroundId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>backgrounds/&lt;backgroundId&gt;-slug/revisions/&lt;id&gt;-slug.json</c> revision.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<BackgroundRevisionId>))]
public readonly record struct BackgroundRevisionId : IStrongId<BackgroundRevisionId>, IParsable<BackgroundRevisionId>, IComparable<BackgroundRevisionId>
{
    public string Value { get; }
    private BackgroundRevisionId(string value) => Value = value;
    public static BackgroundRevisionId New() => new(EntityIdValue.NewToken());
    public static BackgroundRevisionId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static BackgroundRevisionId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out BackgroundRevisionId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new BackgroundRevisionId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(BackgroundRevisionId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a flat base image under a background's <c>backdrops/</c> folder.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<BackdropId>))]
public readonly record struct BackdropId : IStrongId<BackdropId>, IParsable<BackdropId>, IComparable<BackdropId>
{
    public string Value { get; }
    private BackdropId(string value) => Value = value;
    public static BackdropId New() => new(EntityIdValue.NewToken());
    public static BackdropId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static BackdropId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out BackdropId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new BackdropId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(BackdropId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies an <c>issues/&lt;id&gt;-slug/</c> issue.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<IssueId>))]
public readonly record struct IssueId : IStrongId<IssueId>, IParsable<IssueId>, IComparable<IssueId>
{
    public string Value { get; }
    private IssueId(string value) => Value = value;
    public static IssueId New() => new(EntityIdValue.NewToken());
    public static IssueId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static IssueId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out IssueId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new IssueId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(IssueId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies an <c>issues/&lt;issueId&gt;-slug/pages/&lt;id&gt;-slug/</c> page.</summary>
[JsonConverter(typeof(StrongIdJsonConverter<PageId>))]
public readonly record struct PageId : IStrongId<PageId>, IParsable<PageId>, IComparable<PageId>
{
    public string Value { get; }
    private PageId(string value) => Value = value;
    public static PageId New() => new(EntityIdValue.NewToken());
    public static PageId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static PageId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out PageId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new PageId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(PageId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Identifies a <c>.../panels/&lt;id&gt;.json</c> panel (no slug: panels aren't user-named).</summary>
[JsonConverter(typeof(StrongIdJsonConverter<PanelId>))]
public readonly record struct PanelId : IStrongId<PanelId>, IParsable<PanelId>, IComparable<PanelId>
{
    public string Value { get; }
    private PanelId(string value) => Value = value;
    public static PanelId New() => new(EntityIdValue.NewToken());
    public static PanelId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static PanelId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out PanelId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new PanelId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(PanelId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a speech bubble placed in a panel. Placeholder: bubble persistence isn't
/// designed yet (Stanley.Bubbles has no JSON format), so panels only reference bubble
/// ids for now.
/// </summary>
[JsonConverter(typeof(StrongIdJsonConverter<BubbleId>))]
public readonly record struct BubbleId : IStrongId<BubbleId>, IParsable<BubbleId>, IComparable<BubbleId>
{
    public string Value { get; }
    private BubbleId(string value) => Value = value;
    public static BubbleId New() => new(EntityIdValue.NewToken());
    public static BubbleId FromValue(string value) => new(EntityIdValue.Validate(value, nameof(value)));
    public static BubbleId Parse(string s, IFormatProvider? provider = null) => FromValue(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out BubbleId result)
    {
        if (EntityIdValue.TryValidate(s, out var validated)) { result = new BubbleId(validated); return true; }
        result = default;
        return false;
    }
    public int CompareTo(BubbleId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}
