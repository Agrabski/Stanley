namespace Stanley.Editing.Tests;

public class IssueNumberingTests
{
    [Fact]
    public void NoExistingIssues_StartsAtOne() =>
        Assert.Equal("1", IssueNumbering.NextNumber([]));

    [Fact]
    public void OnePastTheHighestPlainInteger() =>
        Assert.Equal("2", IssueNumbering.NextNumber(["1"]));

    [Fact]
    public void ANonIntegerAnnual_DoesntMoveTheNextNumberOn() =>
        Assert.Equal("2", IssueNumbering.NextNumber(["1", "1.5"]));

    [Fact]
    public void IgnoresOrder_AndTakesTheHighest() =>
        Assert.Equal("3", IssueNumbering.NextNumber(["2", "1"]));

    [Fact]
    public void APreviewNumberedZero_IsFollowedByOne() =>
        Assert.Equal("1", IssueNumbering.NextNumber(["0"]));

    [Fact]
    public void NoNumericIssuesAtAll_FallsBackToTheCount() =>
        Assert.Equal("3", IssueNumbering.NextNumber(["Annual", "Special"]));
}
