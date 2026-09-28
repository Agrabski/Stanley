using Stanley.ProjectModel.Issues;

namespace Stanley.ProjectModel.Tests;

public class TextFieldsTests
{
    private static readonly TextFields Fields = new("Moon Pie", "7");

    [Theory]
    [InlineData("Issue #{issue}", "Issue #7")]
    [InlineData("Wydanie #{issue}", "Wydanie #7")]
    [InlineData("{title} - {ISSUE}", "Moon Pie - 7")]
    [InlineData("{title}{title}", "Moon PieMoon Pie")]
    [InlineData("No fields here", "No fields here")]
    [InlineData("{author} and {issue", "{author} and {issue")]
    [InlineData("", "")]
    public void Fill_replaces_the_fields_it_knows_and_leaves_the_rest_as_typed(string text, string shown) =>
        Assert.Equal(shown, Fields.Fill(text));

    [Fact]
    public void A_value_that_looks_like_a_field_isnt_filled_again() =>
        Assert.Equal("The {issue} Mystery #3", new TextFields("The {issue} Mystery", "3").Fill("{title} #{issue}"));
}
