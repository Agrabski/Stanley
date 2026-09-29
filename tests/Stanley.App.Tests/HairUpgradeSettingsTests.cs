using Stanley.App.Documents;
using Stanley.Editors;
using Stanley.ProjectModel.Ids;

namespace Stanley.App.Tests;

/// <summary>The character editor's "Keep" on the old-hairstyle upgrade bar is the user's preference, kept in their settings and never in the comic.</summary>
public sealed class HairUpgradeSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-hair-settings-" + Guid.NewGuid().ToString("N"));

    public HairUpgradeSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Kept_hairstyles_are_remembered_per_character_across_runs()
    {
        var path = Path.Combine(_root, "settings.txt");
        var first = CharacterId.New();
        var second = CharacterId.New();
        Assert.Empty(new AppSettings(path).DeclinedHairUpgrades);

        var settings = new AppSettings(path);
        var memory = new HairUpgradeMemory(() => settings.DeclinedHairUpgrades, ids => settings.DeclinedHairUpgrades = ids);
        memory.Decline(first);
        memory.Decline(second);
        memory.Decline(first);

        var later = new HairUpgradeMemory(() => new AppSettings(path).DeclinedHairUpgrades, _ => { });
        Assert.True(later.IsDeclined(first));
        Assert.True(later.IsDeclined(second));
        Assert.False(later.IsDeclined(CharacterId.New()));
        Assert.Equal(2, new AppSettings(path).DeclinedHairUpgrades.Count);
    }

    [Fact]
    public void Settings_kept_in_memory_only_still_remember()
    {
        var settings = new AppSettings(null);
        var id = CharacterId.New();
        var memory = new HairUpgradeMemory(() => settings.DeclinedHairUpgrades, ids => settings.DeclinedHairUpgrades = ids);

        memory.Decline(id);

        Assert.True(memory.IsDeclined(id));
        Assert.Equal([id.Value], settings.DeclinedHairUpgrades);
    }
}
