using Automation.GitHub;

namespace Automation.Tests.GitHub;

public class MarkdownImageExtractorTests
{
    [Fact]
    public void ReturnsEmptyListForEmptyBody()
    {
        Assert.Empty(MarkdownImageExtractor.Extract(""));
    }

    [Fact]
    public void ExtractsSingleImageUrl()
    {
        var body = "See screenshot: ![screenshot](https://user-images.githubusercontent.com/1/2.png)";
        var result = MarkdownImageExtractor.Extract(body);
        Assert.Single(result);
        Assert.Equal("https://user-images.githubusercontent.com/1/2.png", result[0]);
    }

    [Fact]
    public void ExtractsMultipleImages()
    {
        var body = """
        Some context.
        ![one](https://example.com/one.png)
        Middle prose.
        ![two](https://example.com/two.jpg)
        """;
        var result = MarkdownImageExtractor.Extract(body);
        Assert.Equal(2, result.Count);
        Assert.Contains("https://example.com/one.png", result);
        Assert.Contains("https://example.com/two.jpg", result);
    }

    [Fact]
    public void IgnoresNonImageLinks()
    {
        var body = "[just a link](https://example.com)";
        Assert.Empty(MarkdownImageExtractor.Extract(body));
    }
}
