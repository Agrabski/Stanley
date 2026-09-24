using Stanley.App.Commands;
using Stanley.ProjectModel.Storage;

namespace Stanley.App.Tests;

public class InitCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-init-command-tests").FullName;

    private static int Run(params string[] args) => InitCommand.Build().Parse(args).Invoke();

    [Fact]
    public void Creates_a_project_with_the_given_title()
    {
        var path = Path.Combine(_root, "project");

        var exitCode = Run(path, "--title", "My Comic");

        Assert.Equal(0, exitCode);
        Assert.True(ProjectRepository.IsInitialized(path));
        Assert.Equal("My Comic", new ProjectRepository(path).LoadManifest().Title);
    }

    [Fact]
    public void Defaults_the_title_to_the_target_directory_name()
    {
        var path = Path.Combine(_root, "another-comic");

        Assert.Equal(0, Run(path));

        Assert.Equal("another-comic", new ProjectRepository(path).LoadManifest().Title);
    }

    [Fact]
    public void Uses_the_documented_default_page_trim_when_not_overridden()
    {
        var path = Path.Combine(_root, "trim-defaults");

        Assert.Equal(0, Run(path));

        var trim = new ProjectRepository(path).LoadManifest().DefaultPageTrim;
        Assert.Equal(168.275, trim.Size.WidthMm);
        Assert.Equal(260.35, trim.Size.HeightMm);
        Assert.Equal(3.175, trim.BleedMm);
    }

    [Fact]
    public void Page_trim_options_override_the_defaults()
    {
        var path = Path.Combine(_root, "custom-trim");

        Assert.Equal(0, Run(path, "--page-width-mm", "210", "--page-height-mm", "297", "--page-bleed-mm", "5"));

        var trim = new ProjectRepository(path).LoadManifest().DefaultPageTrim;
        Assert.Equal(210, trim.Size.WidthMm);
        Assert.Equal(297, trim.Size.HeightMm);
        Assert.Equal(5, trim.BleedMm);
    }

    [Fact]
    public void Refuses_to_overwrite_an_existing_project_without_force()
    {
        var path = Path.Combine(_root, "existing");
        Run(path, "--title", "Original");

        var exitCode = Run(path, "--title", "Overwritten");

        Assert.Equal(1, exitCode);
        Assert.Equal("Original", new ProjectRepository(path).LoadManifest().Title);
    }

    [Fact]
    public void Force_overwrites_an_existing_project()
    {
        var path = Path.Combine(_root, "existing-forced");
        Run(path, "--title", "Original");

        var exitCode = Run(path, "--title", "Overwritten", "--force");

        Assert.Equal(0, exitCode);
        Assert.Equal("Overwritten", new ProjectRepository(path).LoadManifest().Title);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
