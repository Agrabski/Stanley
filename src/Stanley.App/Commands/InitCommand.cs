using System.CommandLine;
using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Storage;

namespace Stanley.App.Commands;

/// <summary>The <c>stanley init &lt;path&gt;</c> command: creates a brand-new, empty Stanley project.</summary>
internal static class InitCommand
{
    // A4 with a 3mm bleed - a sensible metric default so `stanley init` works with no
    // page-size flags, per the project's "ease of use" priority; --page-*-mm overrides
    // it for anything else. Always metric: no inch-derived defaults.
    private static readonly PageSize DefaultSize = MetricPaperSizes.Size(MetricPaperSize.A4);
    private const double DefaultBleedMm = 3;

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
            DefaultValueFactory = _ => DefaultSize.WidthMm
        };
        var heightOption = new Option<double>("--page-height-mm")
        {
            Description = "Default page trim height, in millimetres.",
            DefaultValueFactory = _ => DefaultSize.HeightMm
        };
        var bleedOption = new Option<double>("--page-bleed-mm")
        {
            Description = "Default page bleed, in millimetres.",
            DefaultValueFactory = _ => DefaultBleedMm
        };
        var templateOption = new Option<string?>("--template")
        {
            Description = "Start from a comic strip or webcomic template (its page size, panels and spacing) instead of a comic book page. "
                + "--page-*-mm still override its size."
        };
        templateOption.AcceptOnlyFromAmong([.. ComicTemplates.All.Select(t => t.Key)]);
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
        command.Add(templateOption);
        command.Add(forceOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetRequiredValue(pathArgument);
            var title = parseResult.GetValue(titleOption) ?? DefaultTitle(path);
            var template = ComicTemplates.All.SingleOrDefault(t => t.Key == parseResult.GetValue(templateOption));
            // A template supplies the page unless a --page-* option was actually typed.
            double Mm(Option<double> option, double fromTemplate) =>
                template is null || parseResult.GetResult(option) is { Implicit: false } ? parseResult.GetRequiredValue(option) : fromTemplate;
            var trim = new PageTrim(
                new PageSize(Mm(widthOption, template?.Trim.Size.WidthMm ?? 0), Mm(heightOption, template?.Trim.Size.HeightMm ?? 0)),
                Mm(bleedOption, template?.Trim.BleedMm ?? 0));

            if (!parseResult.GetRequiredValue(forceOption) && ProjectRepository.IsInitialized(path))
            {
                Console.Error.WriteLine($"'{Path.GetFullPath(path)}' already contains a Stanley project. Pass --force to overwrite it.");
                return 1;
            }

            ProjectRepository.Initialize(path, title, trim, template?.Format);
            Console.WriteLine($"Created '{title}' at {Path.GetFullPath(path)}");
            return 0;
        });

        return command;
    }

    private static string DefaultTitle(string path) => new DirectoryInfo(Path.GetFullPath(path)).Name;
}
