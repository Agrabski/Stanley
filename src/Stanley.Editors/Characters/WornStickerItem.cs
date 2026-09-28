using Stanley.ProjectModel.Ids;
namespace Stanley.Editors;

/// <summary>A worn sticker, for the Sticker tab's picker.</summary>
public sealed record WornStickerItem(StickerId Id, string Name, string SlotLabel)
{
	public override string ToString() => $"{Name} ({SlotLabel})";
}