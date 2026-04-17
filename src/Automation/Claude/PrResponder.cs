using System.Text;
using Automation.GitHub;
using Automation.Logging;

namespace Automation.Claude;

public sealed class PrResponder
{
    private readonly IClaudeCli _claude;
    private readonly ILogger _log;
    private readonly string _promptTemplate;

    public PrResponder(IClaudeCli claude, ILogger log, string promptTemplate)
    {
        _claude = claude;
        _log = log;
        _promptTemplate = promptTemplate;
    }

    public async Task<bool> RespondAsync(
        string repoCheckoutPath,
        IReadOnlyList<IssueComment> newIssueComments,
        IReadOnlyList<ReviewComment> newReviewComments,
        CancellationToken ct = default)
    {
        if (newIssueComments.Count == 0 && newReviewComments.Count == 0)
            return false;

        var rendered = RenderComments(newIssueComments, newReviewComments);
        var prompt = _promptTemplate.Replace("{{NEW_COMMENTS}}", rendered);

        var invocation = new ClaudeInvocation(
            Prompt: prompt,
            WorkingDirectory: repoCheckoutPath,
            AllowedTools: new[] { "Bash", "Read", "Write", "Edit", "Grep", "Glob" },
            Timeout: TimeSpan.FromMinutes(30));

        _log.Info("pr_responder.start", new { dir = repoCheckoutPath, issue_comments = newIssueComments.Count, review_comments = newReviewComments.Count });
        var result = await _claude.RunAsync(invocation, ct);
        if (!result.Success)
        {
            _log.Error("pr_responder.claude_failed", new { dir = repoCheckoutPath, stderr = result.Stderr });
            return false;
        }
        _log.Info("pr_responder.done", new { dir = repoCheckoutPath });
        return true;
    }

    private static string RenderComments(IReadOnlyList<IssueComment> issueComments, IReadOnlyList<ReviewComment> reviewComments)
    {
        var sb = new StringBuilder();
        foreach (var c in issueComments)
        {
            sb.AppendLine($"[issue-comment id={c.Id} by={c.Author?.Login ?? "?"} at={c.CreatedAt}]");
            sb.AppendLine(c.Body);
            sb.AppendLine();
        }
        foreach (var c in reviewComments)
        {
            sb.AppendLine($"[review-comment id={c.Id} by={c.Author?.Login ?? "?"} path={c.Path} line={c.Line} at={c.CreatedAt}]");
            sb.AppendLine(c.Body);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
