using Stanley.App.Commands;
using Stanley.ProjectModel;

namespace Stanley.App.Tests;

/// <summary>
/// Pure parsing behaviour of `stanley init` - argument/option values, defaults, and
/// parse errors - as opposed to <see cref="InitCommandTests"/>, which exercises what
/// <c>Invoke()</c> actually does on disk. These never call <c>Invoke()</c>, so they
/// never touch the filesystem.
/// </summary>
[Collection(ConsoleOutput.Name)]
public class InitCommandParsingTests
{
    [Fact]
    public void Parses_the_positional_path_argument()
    {
        var result = InitCommand.Build().Parse(["/tmp/my-comic"]);

        Assert.Empty(result.Errors);
        Assert.Equal("/tmp/my-comic", result.GetValue<string>("path"));
    }

    [Fact]
    public void Options_apply_regardless_of_position_relative_to_the_positional_argument()
    {
        var before = InitCommand.Build().Parse(["--title", "Custom", "/tmp/my-comic"]);
        var after = InitCommand.Build().Parse(["/tmp/my-comic", "--title", "Custom"]);

        foreach (var result in new[] { before, after })
        {
            Assert.Empty(result.Errors);
            Assert.Equal("/tmp/my-comic", result.GetValue<string>("path"));
            Assert.Equal("Custom", result.GetValue<string?>("--title"));
        }
    }

    [Fact]
    public void Defaults_apply_when_options_are_omitted()
    {
        var result = InitCommand.Build().Parse(["/tmp/my-comic"]);

        var a4 = MetricPaperSizes.Size(MetricPaperSize.A4);

        Assert.Empty(result.Errors);
        Assert.Null(result.GetValue<string?>("--title"));
        Assert.Equal(a4.WidthMm, result.GetValue<double>("--page-width-mm"));
        Assert.Equal(a4.HeightMm, result.GetValue<double>("--page-height-mm"));
        Assert.Equal(3, result.GetValue<double>("--page-bleed-mm"));
        Assert.False(result.GetValue<bool>("--force"));
    }

    [Fact]
    public void Given_options_override_the_defaults()
    {
        var result = InitCommand.Build().Parse([
            "/tmp/my-comic",
            "--title", "Custom",
            "--page-width-mm", "210",
            "--page-height-mm", "297",
            "--page-bleed-mm", "5",
            "--force"
        ]);

        Assert.Empty(result.Errors);
        Assert.Equal("Custom", result.GetValue<string?>("--title"));
        Assert.Equal(210, result.GetValue<double>("--page-width-mm"));
        Assert.Equal(297, result.GetValue<double>("--page-height-mm"));
        Assert.Equal(5, result.GetValue<double>("--page-bleed-mm"));
        Assert.True(result.GetValue<bool>("--force"));
    }

    [Fact]
    public void Force_defaults_to_false_and_needs_no_explicit_value_when_present()
    {
        var absent = InitCommand.Build().Parse(["/tmp/my-comic"]);
        var present = InitCommand.Build().Parse(["/tmp/my-comic", "--force"]);

        Assert.False(absent.GetValue<bool>("--force"));
        Assert.True(present.GetValue<bool>("--force"));
    }

    [Fact]
    public void Missing_the_required_path_argument_is_a_parse_error()
    {
        var result = InitCommand.Build().Parse([]);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void A_non_numeric_page_size_value_is_a_parse_error()
    {
        var result = InitCommand.Build().Parse(["/tmp/my-comic", "--page-width-mm", "not-a-number"]);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void An_unknown_option_is_rejected()
    {
        var result = InitCommand.Build().Parse(["/tmp/my-comic", "--bogus", "value"]);

        Assert.True(result.Errors.Count > 0 || result.UnmatchedTokens.Count > 0);
    }

    [Fact]
    public void Help_short_circuits_without_a_parse_error_even_with_no_arguments()
    {
        var result = InitCommand.Build().Parse(["--help"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_parse_error_makes_Invoke_return_a_non_zero_exit_code()
    {
        var exitCode = InitCommand.Build().Parse([]).Invoke();

        Assert.NotEqual(0, exitCode);
    }
}
