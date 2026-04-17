using System.Text.Json;

namespace Automation.GitHub;

public sealed class IssueFetcher
{
    // `gh issue list` scoped to a repo with --repo doesn't accept `repository`
    // in the JSON field list — it's implied. We backfill Issue.Repository
    // below so downstream code can still rely on RepoFullName.
    private const string IssueFields = "number,title,body,state,updatedAt,url,labels";
    private readonly IGhCli _gh;

    public IssueFetcher(IGhCli gh)
    {
        _gh = gh;
    }

    /// Fetch open issues assigned to the authenticated user across the watched repo.
    /// Uses `gh issue list --assignee=@me` scoped to the specific repo to match
    /// the reference architecture (demo.jpeg step 1: "issues assigned to me").
    public async Task<IReadOnlyList<Issue>> FetchAssignedIssuesAsync(string repo, int pageSize, CancellationToken ct = default)
    {
        var args = new[]
        {
            "issue", "list",
            "--repo", repo,
            "--assignee", "@me",
            "--state", "open",
            "--limit", pageSize.ToString(),
            "--json", IssueFields,
        };
        var result = await _gh.RunAsync(args, ct);
        if (!result.Success)
            throw new InvalidOperationException($"gh issue list failed for {repo} (exit {result.ExitCode}): {result.Stderr}");

        var issues = JsonSerializer.Deserialize<List<Issue>>(result.Stdout)
            ?? new List<Issue>();
        // gh issue list doesn't include repository by default in older versions;
        // backfill it so downstream code can rely on Issue.RepoFullName.
        return issues
            .Select(i => i.Repository is null
                ? i with { Repository = new IssueRepository(repo) }
                : i)
            .ToList();
    }

    /// Download image attachments referenced in an issue body (gh-uploaded assets
    /// appear as markdown image links). Returns local file paths for claude -p.
    public static async Task<IReadOnlyList<string>> DownloadImageAttachmentsAsync(
        Issue issue, string targetDir, HttpClient http, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDir);
        var urls = MarkdownImageExtractor.Extract(issue.Body);
        var paths = new List<string>();
        foreach (var (index, url) in urls.Select((u, i) => (i, u)))
        {
            var ext = GuessExtension(url);
            var path = Path.Join(targetDir, $"attachment-{index}{ext}");
            try
            {
                var bytes = await http.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(path, bytes, ct);
                paths.Add(path);
            }
            catch
            {
                // Don't fail classification because of one broken image link.
                // The classifier can still reason about the text.
            }
        }
        return paths;
    }

    private static string GuessExtension(string url)
    {
        var dot = url.LastIndexOf('.');
        if (dot < 0) return ".bin";
        var ext = url[dot..];
        var q = ext.IndexOf('?');
        if (q > 0) ext = ext[..q];
        return ext.Length is > 1 and <= 6 ? ext : ".bin";
    }
}

internal static class MarkdownImageExtractor
{
    public static List<string> Extract(string body)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(body)) return urls;
        var matches = System.Text.RegularExpressions.Regex.Matches(
            body, @"!\[[^\]]*\]\((https?://[^\s)]+)\)");
        foreach (System.Text.RegularExpressions.Match m in matches)
            urls.Add(m.Groups[1].Value);
        return urls;
    }
}
