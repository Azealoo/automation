using Automation.Config;

namespace Automation.Tests.Config;

public class AppConfigTests
{
    [Fact]
    public void LoadsExampleConfigSuccessfully()
    {
        var path = Path.Combine(RepoRoot(), "config", "config.example.json");
        var config = AppConfigLoader.Load(path);

        Assert.NotEmpty(config.WatchedRepos);
        Assert.True(config.PollIntervalSeconds >= 60);
        Assert.True(config.JiggleIntervalSeconds >= 10);
    }

    [Fact]
    public void ExpandedWorkdirReplacesTildeWithHome()
    {
        var config = MinimalConfig() with { Workdir = "~/automation-checkouts" };
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(home, config.ExpandedWorkdir);
        Assert.EndsWith("automation-checkouts", config.ExpandedWorkdir);
    }

    [Theory]
    [InlineData("owner/repo", true)]
    [InlineData("owner-name/repo.name", true)]
    [InlineData("owner/repo_with_underscores", true)]
    [InlineData("no-slash", false)]
    [InlineData("owner/../etc", false)]
    [InlineData("../etc", false)]
    [InlineData("owner/repo\nmalicious", false)]
    [InlineData("owner/repo\0payload", false)]
    [InlineData("owner//repo", false)]
    [InlineData("-dash-leading/repo", false)]
    [InlineData("/owner/repo", false)]
    [InlineData("", false)]
    [InlineData("owner/repo/extra", false)]
    public void IsSafeRepoNameRejectsPathTraversalAndControlChars(string repo, bool expected)
    {
        Assert.Equal(expected, AppConfigLoader.IsSafeRepoName(repo));
    }

    [Fact]
    public void LoadRejectsRepoWithoutSlash()
    {
        var bad = Path.GetTempFileName();
        File.WriteAllText(bad, """
        {
          "watched_repos": ["not-a-valid-repo"],
          "workdir": "~/wd",
          "default_branch_hint": null,
          "poll_interval_seconds": 900,
          "jiggle_interval_seconds": 60,
          "jiggle_enabled": true,
          "dry_run": false,
          "max_issues_per_tick": 5,
          "github_api_page_size": 30,
          "claude_bin": "claude",
          "gh_bin": "gh",
          "git_bin": "git"
        }
        """);
        Assert.Throws<InvalidDataException>(() => AppConfigLoader.Load(bad));
    }

    [Fact]
    public void DryRunOverrideForcesJiggleOff()
    {
        var baseCfg = MinimalConfig() with { DryRun = false, JiggleEnabled = true };
        var parsed = CliOverridesParser.Parse(new[] { "--dry-run" });
        var applied = AppConfigLoader.ApplyCliOverrides(baseCfg, parsed);

        Assert.True(applied.DryRun);
        Assert.False(applied.JiggleEnabled);
    }

    [Fact]
    public void OnceFlagIsCapturedSeparately()
    {
        var parsed = CliOverridesParser.Parse(new[] { "--once" });
        Assert.True(parsed.RunOnce);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "CLAUDE.md")) &&
                File.Exists(Path.Join(dir.FullName, "demo.jpeg")))
                return dir.FullName;
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("could not locate repo root");
    }

    private static AppConfig MinimalConfig() => new(
        WatchedRepos: new[] { "you/sandbox" },
        Workdir: "~/wd",
        DefaultBranchHint: null,
        PollIntervalSeconds: 900,
        JiggleIntervalSeconds: 60,
        JiggleEnabled: true,
        DryRun: false,
        MaxIssuesPerTick: 5,
        GithubApiPageSize: 30,
        ClaudeBin: "claude",
        GhBin: "gh",
        GitBin: "git");
}
