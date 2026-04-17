using Automation.Claude;
using Automation.GitHub;
using Automation.Logging;

namespace Automation.Claude;

public sealed class Implementer
{
    private readonly IClaudeCli _claude;
    private readonly ILogger _log;
    private readonly string _promptTemplate;

    public Implementer(IClaudeCli claude, ILogger log, string promptTemplate)
    {
        _claude = claude;
        _log = log;
        _promptTemplate = promptTemplate;
    }

    public async Task<bool> ImplementAsync(
        Issue issue,
        string repoCheckoutPath,
        ClassifierResult classifier,
        IReadOnlyList<string> imagePaths,
        CancellationToken ct = default)
    {
        var prompt = _promptTemplate
            .Replace("{{CLASSIFIER_SUMMARY}}", classifier.Summary)
            .Replace("{{LIKELY_FILES}}", string.Join(", ", classifier.LikelyFiles))
            .Replace("{{ISSUE_PAYLOAD}}", RenderIssuePayload(issue));

        var invocation = new ClaudeInvocation(
            Prompt: prompt,
            WorkingDirectory: repoCheckoutPath,
            ImagePaths: imagePaths,
            AllowedTools: new[] { "Bash", "Read", "Write", "Edit", "Grep", "Glob" },
            Timeout: TimeSpan.FromMinutes(30));

        _log.Info("implementer.start", new { repo = issue.RepoFullName, issue = issue.Number });
        var result = await _claude.RunAsync(invocation, ct);
        if (!result.Success)
        {
            _log.Error("implementer.claude_failed", new { repo = issue.RepoFullName, issue = issue.Number, stderr = result.Stderr });
            return false;
        }
        _log.Info("implementer.done", new { repo = issue.RepoFullName, issue = issue.Number });
        return true;
    }

    private static string RenderIssuePayload(Issue issue) =>
        $"Issue #{issue.Number}: {issue.Title}\nURL: {issue.Url}\n\n{issue.Body}";
}
