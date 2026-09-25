using System.CommandLine;
using Stanley.Editors;
using Stanley.ProjectModel.Storage;

namespace Stanley.App.Commands;

/// <summary>
/// The <c>stanley issue</c> commands - <c>list</c> and <c>add</c> - the escape hatch for
/// what File &gt; Info's Issues section and its "New issue" button do in the GUI, for
/// scripting or a project that's never opened in the GUI at all.
/// </summary>
internal static class IssueCommand
{
    public static Command Build()
    {
        var command = new Command("issue", "List or add issues of a Stanley comic.");
        command.Add(BuildList());
        command.Add(BuildAdd());
        return command;
    }

    private static Command BuildList()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "The project's folder."
        };

        var command = new Command("list", "List the comic's issues, in storage order: number, title and id, one per line.");
        command.Add(pathArgument);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetRequiredValue(pathArgument);
            if (!ProjectRepository.IsInitialized(path))
            {
                Console.Error.WriteLine($"'{Path.GetFullPath(path)}' isn't a Stanley project (it has no stanley.json).");
                return 1;
            }

            var repository = new ProjectRepository(path);
            foreach (var id in repository.LoadManifest().IssueIds)
            {
                var issue = repository.LoadIssue(id);
                Console.WriteLine($"{issue.Number}\t{issue.Title}\t{issue.Id.Value}");
            }
            return 0;
        });

        return command;
    }

    private static Command BuildAdd()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "The project's folder."
        };
        var numberOption = new Option<string?>("--number")
        {
            Description = "The new issue's number. Defaults to one past the highest numeric issue number so far (\"1\" for the first)."
        };
        var titleOption = new Option<string?>("--title")
        {
            Description = "The new issue's title, if it has one."
        };

        var command = new Command("add", "Add a new issue: one blank page, laid out like a new page of this comic. Prints its id.");
        command.Add(pathArgument);
        command.Add(numberOption);
        command.Add(titleOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetRequiredValue(pathArgument);
            if (!ProjectRepository.IsInitialized(path))
            {
                Console.Error.WriteLine($"'{Path.GetFullPath(path)}' isn't a Stanley project (it has no stanley.json).");
                return 1;
            }

            var project = ComicProject.Open(path);
            var id = project.NewIssue(parseResult.GetValue(numberOption), parseResult.GetValue(titleOption));
            Console.WriteLine(id.Value);
            return 0;
        });

        return command;
    }
}
