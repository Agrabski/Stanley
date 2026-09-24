using System.CommandLine;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Storage;

namespace Stanley.Cli.Commands;

/// <summary>The <c>stanley init &lt;path&gt;</c> command: creates a brand-new, empty Stanley project.</summary>
internal static class InitCommand
{
    // US comic trim (6.625 x 10.25 in) with a 1/8 in bleed, in millimetres - a sensible
    // default so `stanley init` works with no page-size flags, per the project's "ease
    // of use" priority; --page-*-mm overrides it for anything else.
    private const double DefaultWidthMm = 168.275;
    private const double DefaultHeightMm = 260.35;
    private const double DefaultBleedMm = 3.175;

    public static Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Directory to create the new project in (created if it doesn't already exist)."
        };
        var titleOption = new Option<string?>("--title")
        {
            Description = "The series title. Defaults to the target directory's name."
        };
        var widthOption = new Option<double>("--page-width-mm")
        {
            Description = "Default page trim width, in millimetres.",
            DefaultValueFactory = _ => DefaultWidthMm
        };
        var heightOption = new Option<double>("--page-height-mm")
        {
            Description = "Default page trim height, in millimetres.",
            DefaultValueFactory = _ => DefaultHeightMm
        };
        var bleedOption = new Option<double>("--page-bleed-mm")
        {
            Description = "Default page bleed, in millimetres.",
            DefaultValueFactory = _ => DefaultBleedMm
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite the project at the target directory if one already exists there."
        };

        var command = new Command("init", "Create a new, empty Stanley project.");
        command.Add(pathArgument);
        command.Add(titleOption);
        command.Add(widthOption);
        command.Add(heightOption);
        command.Add(bleedOption);
        command.Add(forceOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetRequiredValue(pathArgument);
            var title = parseResult.GetValue(titleOption) ?? DefaultTitle(path);
            var trim = new PageTrim(
                parseResult.GetRequiredValue(widthOption),
                parseResult.GetRequiredValue(heightOption),
                parseResult.GetRequiredValue(bleedOption));

            if (!parseResult.GetRequiredValue(forceOption) && ProjectRepository.IsInitialized(path))
            {
                Console.Error.WriteLine($"'{Path.GetFullPath(path)}' already contains a Stanley project. Pass --force to overwrite it.");
                return 1;
            }

            ProjectRepository.Initialize(path, title, trim);
            Console.WriteLine($"Created '{title}' at {Path.GetFullPath(path)}");
            return 0;
        });

        return command;
    }

    private static string DefaultTitle(string path) => new DirectoryInfo(Path.GetFullPath(path)).Name;
}
