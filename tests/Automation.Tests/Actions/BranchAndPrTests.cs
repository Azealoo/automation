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
}
