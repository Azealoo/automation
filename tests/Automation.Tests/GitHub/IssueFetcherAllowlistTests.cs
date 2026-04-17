using Automation.GitHub;

namespace Automation.Tests.GitHub;

public class IssueFetcherAllowlistTests
{
    [Theory]
    [InlineData("https://user-images.githubusercontent.com/1/2.png", true)]
    [InlineData("https://github.com/owner/repo/issues/1/files/a.png", true)]
    [InlineData("https://api.github.com/repos/owner/repo", true)]
    [InlineData("https://objects.githubusercontent.com/foo", true)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://localhost:8080/admin", false)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("http://192.168.1.1/", false)]
    [InlineData("https://evil.com/img.png", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://githubusercontent.com.evil.com/", false)] // suffix confusion guard
    [InlineData("not a url", false)]
    public void OnlyAllowsGithubHosts(string url, bool expected)
    {
        Assert.Equal(expected, IssueFetcher.IsAllowedUrl(url));
    }
}
