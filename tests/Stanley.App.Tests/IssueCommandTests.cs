using Stanley.App.Commands;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Storage;

namespace Stanley.App.Tests;

[Collection(ConsoleOutput.Name)]
public class IssueCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-issue-command-tests").FullName;

    private string InitProject(string name)
    {
        var path = Path.Combine(_root, name);
        ProjectRepository.Initialize(path, name, new PageTrim(MetricPaperSizes.Size(MetricPaperSize.A4), 3));
        return path;
    }

    /// <summary>Runs the command, capturing what it wrote to stdout (init, unlike Program, never writes to stderr on success).</summary>
    private static (int ExitCode, string Output) Run(params string[] args)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exitCode = IssueCommand.Build().Parse(args).Invoke();
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void List_OnAFreshProjectWithNoIssuesYet_PrintsNothing()
    {
        var path = InitProject("fresh");

        var (exitCode, output) = Run("list", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("", output);
    }

    [Fact]
    public void Add_OnAFreshProject_CreatesTheFirstIssueNumberedOne_AndPrintsItsId()
    {
        var path = InitProject("first-issue");

        var (exitCode, output) = Run("add", path);

        Assert.Equal(0, exitCode);
        var repository = new ProjectRepository(path);
        var issueId = Assert.Single(repository.LoadManifest().IssueIds);
        Assert.Equal(issueId.Value, output.Trim());
        var issue = repository.LoadIssue(issueId);
        Assert.Equal("1", issue.Number);
        Assert.Single(issue.PageIds);
    }

    [Fact]
    public void Add_WithoutANumber_DefaultsToOnePastTheHighestSoFar()
    {
        var path = InitProject("sequel");
        Run("add", path); // issue "1"

        var (exitCode, output) = Run("add", path);

        Assert.Equal(0, exitCode);
        var issue = new ProjectRepository(path).LoadIssue(IssueId.FromValue(output.Trim()));
        Assert.Equal("2", issue.Number);
    }

    [Fact]
    public void Add_WithNumberAndTitleOptions_UsesThemInstead()
    {
        var path = InitProject("annual");
        Run("add", path); // issue "1"

        var (exitCode, output) = Run("add", path, "--number", "1.5", "--title", "Annual");

        Assert.Equal(0, exitCode);
        var issue = new ProjectRepository(path).LoadIssue(IssueId.FromValue(output.Trim()));
        Assert.Equal("1.5", issue.Number);
        Assert.Equal("Annual", issue.Title);
    }

    [Fact]
    public void Add_NeverTouchesAnEarlierIssuesFiles()
    {
        var path = InitProject("untouched");
        var firstId = Run("add", path).Output.Trim();
        var firstDir = Directory.EnumerateDirectories(Path.Combine(path, "issues")).Single(d => Path.GetFileName(d).StartsWith(firstId + "-", StringComparison.Ordinal));
        var before = Directory.EnumerateFiles(firstDir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        Run("add", path);

        var after = Directory.EnumerateFiles(firstDir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (file, bytes) in before)
            Assert.Equal(bytes, after[file]);
    }

    [Fact]
    public void Add_OnAFolderThatIsntAProject_Fails()
    {
        var path = Path.Combine(_root, "not-a-project");
        Directory.CreateDirectory(path);

        var (exitCode, _) = Run("add", path);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void List_PrintsNumberTitleAndIdOnePerLineInStorageOrder()
    {
        var path = InitProject("listing");
        var firstId = Run("add", path).Output.Trim();
        var secondId = Run("add", path, "--title", "Annual").Output.Trim();

        var (exitCode, output) = Run("list", path);

        Assert.Equal(0, exitCode);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal([$"1\t\t{firstId}", $"2\tAnnual\t{secondId}"], lines);
    }

    [Fact]
    public void List_OnAFolderThatIsntAProject_Fails()
    {
        var path = Path.Combine(_root, "nope");
        Directory.CreateDirectory(path);

        var (exitCode, _) = Run("list", path);

        Assert.NotEqual(0, exitCode);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
