namespace Stanley.Editors.Tests;

public sealed class SvgEditorCandidatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "stanley-svg-editor-candidates-" + Guid.NewGuid().ToString("N"));
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

    public SvgEditorCandidatesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Detect_finds_a_known_editor_on_PATH_and_ignores_names_it_doesnt_know()
    {
        File.WriteAllText(Path.Combine(_folder, "inkscape"), "");
        File.WriteAllText(Path.Combine(_folder, "some-random-tool"), "");
        Environment.SetEnvironmentVariable("PATH", _folder + Path.PathSeparator + _originalPath);

        var found = SvgEditorCandidates.Detect();

        var inkscape = Assert.Single(found, c => c.Name == "Inkscape");
        Assert.Equal(Path.Combine(_folder, "inkscape"), inkscape.Path);
        Assert.DoesNotContain(found, c => c.Path.Contains("some-random-tool"));
    }

    [Fact]
    public void Detect_finds_nothing_when_PATH_has_none_of_the_known_names()
    {
        Environment.SetEnvironmentVariable("PATH", _folder);

        Assert.Empty(SvgEditorCandidates.Detect());
    }
}
