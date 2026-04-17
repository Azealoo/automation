using Automation.Actions;

namespace Automation.Tests.Actions;

public class BranchAndPrTests
{
    [Theory]
    [InlineData("Fix the null pointer in UserService", "fix-the-null-pointer-in-userservice")]
    [InlineData("   Whitespace around   ", "whitespace-around")]
    [InlineData("Émojis 🚀 and UTF-8", "mojis-and-utf-8")]
    public void SlugProducesStableAsciiKebab(string title, string expected)
    {
        Assert.Equal(expected, BranchAndPr.Slug(title));
    }

    [Fact]
    public void SlugClampsLongTitlesTo40Chars()
    {
        var title = new string('a', 100);
        var result = BranchAndPr.Slug(title);
        Assert.True(result.Length <= 40, $"expected <= 40, got {result.Length}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("🚀🚀🚀")]   // all non-ASCII: collapses to empty
    [InlineData("---")]      // all separators: collapses to empty
    public void SlugReturnsFallbackForBlankOrAllStripped(string title)
    {
        Assert.Equal("untitled", BranchAndPr.Slug(title));
    }

    [Fact]
    public void ParsePrNumberFromUrlWorksForGitHubPrUrl()
    {
        var url = "https://github.com/owner/repo/pull/42";
        Assert.Equal(42, BranchAndPr.ParsePrNumberFromUrl(url));
    }

    [Fact]
    public void ParsePrNumberFromUrlReturnsNullForUnparsableInput()
    {
        Assert.Null(BranchAndPr.ParsePrNumberFromUrl(""));
        Assert.Null(BranchAndPr.ParsePrNumberFromUrl("not a url"));
        Assert.Null(BranchAndPr.ParsePrNumberFromUrl("https://github.com/owner/repo/pull/"));
    }

    [Theory]
    [InlineData("pull request create failed: GraphQL: Resource not accessible by personal access token (createPullRequest)\n")]
    [InlineData("GraphQL: Resource not accessible by integration (createPullRequest)")]
    public void ClassifyPrCreateErrorRecognizesPatScopeFailures(string stderr)
    {
        Assert.Equal("pat_scope", BranchAndPr.ClassifyPrCreateError(stderr));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GraphQL: A pull request already exists for branch X")]
    [InlineData("fatal: could not read Username for 'https://github.com'")]
    public void ClassifyPrCreateErrorReturnsNullForUnrelatedFailures(string? stderr)
    {
        Assert.Null(BranchAndPr.ClassifyPrCreateError(stderr));
    }
}
