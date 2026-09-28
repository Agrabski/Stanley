using Stanley.App.Documents;
using Stanley.Editors;
using Stanley.ProjectModel.Characters;

namespace Stanley.App.Tests;

/// <summary>Where My Assets lives, File › Options' My Assets folder and File › My Assets (docs/asset-packs.md §9's Stanley.App row).</summary>
public sealed class MyAssetsSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-my-assets-settings-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFileDialogs _dialogs = new();

    public MyAssetsSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_default_folder_follows_the_data_directory_override_so_tests_never_touch_the_real_one()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _root);
            Assert.Equal(Path.Combine(_root, "My Assets"), AppPaths.MyAssetsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, previous);
        }
    }

    [Fact]
    public void A_chosen_folder_is_remembered_and_an_unset_one_means_the_default()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.Null(new AppSettings(path).MyAssetsDirectory);

        new AppSettings(path).MyAssetsDirectory = Path.Combine(_root, "Shared");

        Assert.Equal(Path.Combine(_root, "Shared"), new AppSettings(path).MyAssetsDirectory);
    }

    [Fact]
    public async Task Changing_the_folder_in_Options_moves_My_Assets_there_and_saves_the_choice()
    {
        var settings = new AppSettings(null);
        var library = new MyAssetsLibrary(Path.Combine(_root, "Old"));
        var vm = new MainWindowViewModel(_dialogs, new RecentProjects(null), settings: settings, myAssets: library);
        var elsewhere = Path.Combine(_root, "Elsewhere");
        new MyAssetsLibrary(elsewhere).KeepCharacter(CharacterDefinition.Create("Alice"));

        _dialogs.Folders.Enqueue(elsewhere);
        await vm.ChooseMyAssetsFolderCommand.ExecuteAsync(null);

        Assert.Equal(elsewhere, settings.MyAssetsDirectory);
        Assert.Equal(elsewhere, vm.MyAssetsFolderText);
        Assert.Equal("Alice", Assert.Single(library.Characters()).Name);
        vm.ShowBackstage(BackstagePage.MyAssets);
        Assert.True(vm.IsMyAssetsPage);
        Assert.Equal("Alice", Assert.Single(vm.MyAssetsPage!.Tiles).Name);
    }

    [Fact]
    public void Without_My_Assets_nothing_about_it_is_offered()
    {
        var vm = new MainWindowViewModel(_dialogs, new RecentProjects(null));

        Assert.False(vm.HasMyAssets);
        Assert.Null(vm.MyAssetsPage);
        Assert.False(vm.ChooseMyAssetsFolderCommand.CanExecute(null));
        Assert.False(vm.Characters!.HasMyAssets);
        Assert.False(vm.Editor!.HasMyAssets);
    }

    [Fact]
    public async Task The_characters_pane_finds_the_other_recent_comics_but_not_the_one_that_is_open()
    {
        var recent = new RecentProjects(null);
        var library = new MyAssetsLibrary(Path.Combine(_root, "My Assets"));
        var vm = new MainWindowViewModel(_dialogs, recent, myAssets: library);
        vm.Characters!.NewCharacterCommand.Execute(null);
        var first = Path.Combine(_root, "First");
        _dialogs.Folders.Enqueue(first);
        Assert.True(await vm.SaveAsync());

        await vm.NewAsync(null);
        vm.Characters!.RefreshGallery();
        Assert.Equal("First", Assert.Single(vm.Characters.GalleryOtherComics).Title);

        await vm.OpenAsync(first);
        vm.Characters!.RefreshGallery();
        Assert.Empty(vm.Characters.GalleryOtherComics); // it's this comic now
    }
}
