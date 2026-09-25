namespace Stanley.Editors.Tests;

public sealed class SystemArtEditingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "stanley-art-editing-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Edit_refuses_without_an_editor_path_and_writes_nothing()
    {
        var editing = new SystemArtEditing(_folder);

        var session = editing.Edit("hat-front.svg", "<svg/>", _ => { }, out var error);

        Assert.Null(session);
        Assert.NotNull(error);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Edit_persists_the_editor_path_through_the_given_delegates()
    {
        string? saved = null;
        var editing = new SystemArtEditing(_folder, () => saved, v => saved = v);

        editing.EditorPath = "/usr/bin/inkscape";

        Assert.Equal("/usr/bin/inkscape", saved);
        Assert.Equal("/usr/bin/inkscape", editing.EditorPath);
    }

    [Fact]
    public void Edit_writes_the_file_and_watches_it_even_when_the_configured_editor_cant_start()
    {
        var editing = new SystemArtEditing(_folder) { EditorPath = Path.Combine(_folder, "no-such-editor") };

        using var session = editing.Edit("hat-front.svg", "<svg/>", _ => { }, out var error);

        Assert.NotNull(session);
        Assert.Contains("no-such-editor", error);
        Assert.Equal("<svg/>", File.ReadAllText(session!.Path));
    }
}
