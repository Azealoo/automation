using System.Text.Json;
using Automation.Logging;

namespace Automation.GitHub;

public sealed class PrFetcher
{
    private readonly IGhCli _gh;
    private readonly ILogger _log;

    public PrFetcher(IGhCli gh, ILogger log)
    {
        _gh = gh;
        _log = log;
    }

    public async Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repo, int prNumber, CancellationToken ct = default)
    {
        // REST (not `gh pr view --json comments`) because the GraphQL path returns
        // `id` as a string scalar, which doesn't fit our numeric watermark ledger.
        var result = await _gh.RunAsync(new[]
        {
            "api",
            $"/repos/{repo}/issues/{prNumber}/comments",
            "--paginate",
        }, ct);
        if (!result.Success)
        {
            _log.Warn("pr_fetcher.issue_comments_failed", new { repo, pr = prNumber, stderr = result.Stderr });
            return Array.Empty<IssueComment>();
        }

        using var doc = JsonDocument.Parse(result.Stdout);
        var list = new List<IssueComment>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("id").GetInt64();
            var body = el.GetProperty("body").GetString() ?? "";
            var createdAt = el.GetProperty("created_at").GetString() ?? "";
            var login = el.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var loginEl)
                ? loginEl.GetString() ?? "" : "";
            list.Add(new IssueComment(id, new CommentAuthor(login), body, createdAt));
        }
        return list;
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
        if (!result.Success)
        {
            _log.Warn("pr_fetcher.review_comments_failed", new { repo, pr = prNumber, stderr = result.Stderr });
            return Array.Empty<ReviewComment>();
        }

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
