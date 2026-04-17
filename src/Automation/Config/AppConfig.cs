using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.Config;

public sealed record AppConfig(
    [property: JsonPropertyName("watched_repos")] IReadOnlyList<string> WatchedRepos,
    [property: JsonPropertyName("workdir")] string Workdir,
    [property: JsonPropertyName("default_branch_hint")] string? DefaultBranchHint,
    [property: JsonPropertyName("poll_interval_seconds")] int PollIntervalSeconds,
    [property: JsonPropertyName("jiggle_interval_seconds")] int JiggleIntervalSeconds,
    [property: JsonPropertyName("jiggle_enabled")] bool JiggleEnabled,
    [property: JsonPropertyName("dry_run")] bool DryRun,
    [property: JsonPropertyName("max_issues_per_tick")] int MaxIssuesPerTick,
    [property: JsonPropertyName("github_api_page_size")] int GithubApiPageSize,
    [property: JsonPropertyName("claude_bin")] string ClaudeBin,
    [property: JsonPropertyName("gh_bin")] string GhBin,
    [property: JsonPropertyName("git_bin")] string GitBin)
{
    public string ExpandedWorkdir =>
        Workdir.StartsWith("~", StringComparison.Ordinal)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Workdir[1..].TrimStart('/'))
            : Workdir;
}

public static class AppConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"config file not found: {path}");
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, Options)
            ?? throw new InvalidDataException($"config file is empty or malformed: {path}");
        Validate(config, path);
        return config;
    }

    public static AppConfig ApplyCliOverrides(AppConfig config, CliOverrides overrides) =>
        config with
        {
            DryRun = overrides.DryRun ?? config.DryRun,
            JiggleEnabled = overrides.JiggleEnabled ?? config.JiggleEnabled,
        };

    private static void Validate(AppConfig config, string path)
    {
        if (config.WatchedRepos.Count == 0)
            throw new InvalidDataException($"{path}: watched_repos must contain at least one entry");
        foreach (var repo in config.WatchedRepos)
        {
            if (!repo.Contains('/'))
                throw new InvalidDataException($"{path}: repo must be in owner/name form: {repo}");
        }
        if (config.PollIntervalSeconds < 60)
            throw new InvalidDataException($"{path}: poll_interval_seconds must be >= 60");
        if (config.JiggleIntervalSeconds < 10)
            throw new InvalidDataException($"{path}: jiggle_interval_seconds must be >= 10");
        if (config.MaxIssuesPerTick < 1)
            throw new InvalidDataException($"{path}: max_issues_per_tick must be >= 1");
    }
}

public sealed record CliOverrides(bool? DryRun, bool? JiggleEnabled, bool RunOnce);

public static class CliOverridesParser
{
    public static CliOverrides Parse(string[] args)
    {
        bool? dryRun = null;
        bool? jiggleEnabled = null;
        bool runOnce = false;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--dry-run":
                    dryRun = true;
                    jiggleEnabled = false;
                    break;
                case "--no-jiggle":
                    jiggleEnabled = false;
                    break;
                case "--once":
                    runOnce = true;
                    break;
            }
        }
        return new CliOverrides(dryRun, jiggleEnabled, runOnce);
    }
}
