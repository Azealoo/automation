using System.Text.Json;

namespace Automation.GitHub;

public sealed class PrFetcher
{
    private readonly IGhCli _gh;

    public PrFetcher(IGhCli gh)
    {
        _gh = gh;
    }

    /// Find the most recent PR we opened for this issue by matching branch prefix.
    /// We don't rely on GitHub's "linked issue" metadata because gh pr create doesn't
    /// always set it for fork flows — the branch naming convention is authoritative.
    public async Task<PullRequest?> FindPrForIssueAsync(string repo, int issueNumber, string branchPrefix, CancellationToken ct = default)
    {
        var result = await _gh.RunAsync(new[]
        {
            "pr", "list",
            "--repo", repo,
            "--state", "open",
            "--limit", "50",
            "--json", "number,title,state,url,headRefName,baseRefName,isDraft",
        }, ct);
        if (!result.Success) return null;

        var prs = JsonSerializer.Deserialize<List<PullRequest>>(result.Stdout) ?? new List<PullRequest>();
        return prs.FirstOrDefault(p => p.HeadRefName.StartsWith($"{branchPrefix}-{issueNumber}-", StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repo, int prNumber, CancellationToken ct = default)
    {
        var result = await _gh.RunAsync(new[]
        {
            "pr", "view", prNumber.ToString(),
            "--repo", repo,
            "--json", "comments",
        }, ct);
        if (!result.Success) return Array.Empty<IssueComment>();

        using var doc = JsonDocument.Parse(result.Stdout);
        if (!doc.RootElement.TryGetProperty("comments", out var comments))
            return Array.Empty<IssueComment>();
        return JsonSerializer.Deserialize<List<IssueComment>>(comments.GetRawText()) ?? new List<IssueComment>();
    }

    public async Task<IReadOnlyList<ReviewComment>> GetReviewCommentsAsync(string repo, int prNumber, CancellationToken ct = default)
    {
        // gh doesn't expose review-thread comments cleanly via `pr view`; use the REST API.
        var result = await _gh.RunAsync(new[]
        {
            "api",
            $"/repos/{repo}/pulls/{prNumber}/comments",
            "--paginate",
        }, ct);
        if (!result.Success) return Array.Empty<ReviewComment>();

        // The REST API field names differ from GraphQL — map them.
        using var doc = JsonDocument.Parse(result.Stdout);
        var list = new List<ReviewComment>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("id").GetInt64();
            var body = el.GetProperty("body").GetString() ?? "";
            var createdAt = el.GetProperty("created_at").GetString() ?? "";
            var path = el.TryGetProperty("path", out var p) ? p.GetString() : null;
            var line = el.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : (int?)null;
            var login = el.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var loginEl)
                ? loginEl.GetString() ?? "" : "";
            list.Add(new ReviewComment(id, new CommentAuthor(login), body, path, line, createdAt));
        }
        return list;
    }
}
