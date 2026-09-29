using System.CommandLine;
using Stanley.Editors;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Storage;

namespace Stanley.App.Commands;

/// <summary>
/// The <c>stanley issue</c> commands - <c>list</c>, <c>add</c> and <c>remove</c> - the
/// escape hatch for what File &gt; Info's Issues section and its "New issue"/"Delete"
/// buttons do in the GUI, for scripting or a project that's never opened in the GUI at all.
/// </summary>
internal static class IssueCommand
{
    public static Command Build()
    {
        var command = new Command("issue", "List, add or remove issues of a Stanley comic.");
        command.Add(BuildList());
        command.Add(BuildAdd());
        command.Add(BuildRemove());
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

    private static Command BuildRemove()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "The project's folder."
        };
        var idArgument = new Argument<string>("id")
        {
            Description = "The issue's id (from `issue list`)."
        };

        var command = new Command("remove", "Delete an issue: its pages, panels and pictures. Fails if it's the comic's only issue.");
        command.Add(pathArgument);
        command.Add(idArgument);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetRequiredValue(pathArgument);
            if (!ProjectRepository.IsInitialized(path))
            {
                Console.Error.WriteLine($"'{Path.GetFullPath(path)}' isn't a Stanley project (it has no stanley.json).");
                return 1;
            }

            var idText = parseResult.GetRequiredValue(idArgument);
            if (!IssueId.TryParse(idText, null, out var id))
            {
                Console.Error.WriteLine($"'{idText}' isn't a valid issue id.");
                return 1;
            }

            var repository = new ProjectRepository(path);
            var manifest = repository.LoadManifest();
            if (!manifest.IssueIds.Contains(id))
            {
                Console.Error.WriteLine($"No issue with id '{id.Value}' was found.");
                return 1;
            }
            if (manifest.IssueIds.Count <= 1)
            {
                Console.Error.WriteLine("A comic must keep at least one issue.");
                return 1;
            }

            repository.DeleteIssue(id);
            repository.SaveManifest(manifest with { IssueIds = manifest.IssueIds.Where(x => x != id).ToList() });
            return 0;
        });

        return command;
    }
}
