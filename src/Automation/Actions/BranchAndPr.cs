using System.Text.Json;
using Automation.Claude;
using Automation.GitHub;
using Automation.Logging;

namespace Automation.Actions;

public sealed record BranchAndPrOutcome(bool Succeeded, int? PrNumber, string Branch);

public sealed class BranchAndPr
{
    public const string BranchPrefix = "auto/issue";
    private readonly GitClient _git;
    private readonly IGhCli _gh;
    private readonly Implementer _implementer;
    private readonly ILogger _log;
    private readonly bool _dryRun;

    public BranchAndPr(GitClient git, IGhCli gh, Implementer implementer, ILogger log, bool dryRun)
    {
        _git = git;
        _gh = gh;
        _implementer = implementer;
        _log = log;
        _dryRun = dryRun;
    }

    public async Task<BranchAndPrOutcome> RunAsync(
        Issue issue,
        string repoCheckoutPath,
        ClassifierResult classifier,
        IReadOnlyList<string> imagePaths,
        CancellationToken ct = default)
    {
        var branch = $"{BranchPrefix}-{issue.Number}-{Slug(issue.Title)}";

        if (_dryRun)
        {
            _log.Info("branch_and_pr.dry_run", new { repo = issue.RepoFullName, issue = issue.Number, branch });
            return new BranchAndPrOutcome(true, null, branch);
        }

        var existingPr = await FindOpenPrForBranchAsync(issue.RepoFullName, branch, ct);
        if (existingPr is not null)
        {
            _log.Info("branch_and_pr.pr_already_open", new { repo = issue.RepoFullName, issue = issue.Number, branch, pr = existingPr });
            return new BranchAndPrOutcome(true, existingPr, branch);
        }

        if (await _git.LocalBranchExistsAsync(repoCheckoutPath, branch, ct))
        {
            _log.Info("branch_and_pr.local_branch_cleanup", new { repo = issue.RepoFullName, issue = issue.Number, branch });
            await _git.DeleteLocalBranchAsync(repoCheckoutPath, branch, ct);
        }

        if (await _git.OriginBranchExistsAsync(repoCheckoutPath, branch, ct))
        {
            // Previous tick pushed the branch but PR creation failed (e.g. missing PAT
            // scope). Re-check out from origin and retry only the PR-create step —
            // re-running the implementer would lose or conflict with committed work
            // and CLAUDE.md forbids force-push.
            _log.Info("branch_and_pr.resume_from_origin", new { repo = issue.RepoFullName, issue = issue.Number, branch });
            if (!await _git.CheckoutTrackingBranchAsync(repoCheckoutPath, branch, ct))
                throw new InvalidOperationException($"failed to resume origin/{branch}");
            var resumedPr = await CreateDraftPrAsync(issue, branch, ct);
            return new BranchAndPrOutcome(resumedPr is not null, resumedPr, branch);
        }

        await _git.CheckoutNewBranchAsync(repoCheckoutPath, branch, ct);
        var implementOk = await _implementer.ImplementAsync(issue, repoCheckoutPath, classifier, imagePaths, ct);
        if (!implementOk)
            return new BranchAndPrOutcome(false, null, branch);

        if (!await _git.HasStagedOrUnstagedChangesAsync(repoCheckoutPath, ct))
        {
            _log.Warn("branch_and_pr.no_changes", new { repo = issue.RepoFullName, issue = issue.Number });
            return new BranchAndPrOutcome(false, null, branch);
        }

        var commitMessage = $"auto: issue #{issue.Number} {issue.Title}".Trim();
        await _git.CommitAllAsync(repoCheckoutPath, commitMessage, ct);
        await _git.PushAsync(repoCheckoutPath, branch, ct);

        var prNumber = await CreateDraftPrAsync(issue, branch, ct);
        return new BranchAndPrOutcome(prNumber is not null, prNumber, branch);
    }

    private async Task<int?> FindOpenPrForBranchAsync(string repo, string branch, CancellationToken ct)
    {
        var result = await _gh.RunAsync(new[]
        {
            "pr", "list",
            "--repo", repo,
            "--head", branch,
            "--state", "open",
            "--json", "number",
            "--limit", "1",
        }, ct);
        if (!result.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(result.Stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number)
                    return n.GetInt32();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<int?> CreateDraftPrAsync(Issue issue, string branch, CancellationToken ct)
    {
        var body = $"Closes #{issue.Number}\n\n" +
                   "Opened by the personal automation loop. Draft until human-reviewed.";
        var result = await _gh.RunAsync(new[]
        {
            "pr", "create",
            "--repo", issue.RepoFullName,
            "--draft",
            "--title", $"auto: #{issue.Number} {issue.Title}",
            "--body", body,
            "--head", branch,
        }, ct);
        if (!result.Success)
        {
            _log.Error("branch_and_pr.gh_pr_create_failed", new { stderr = result.Stderr });
            return null;
        }
        return ParsePrNumberFromUrl(result.Stdout.Trim());
    }

    internal static int? ParsePrNumberFromUrl(string url)
    {
        var idx = url.LastIndexOf('/');
        if (idx < 0 || idx == url.Length - 1) return null;
        return int.TryParse(url[(idx + 1)..], out var n) ? n : null;
    }

    internal static string Slug(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "untitled";
        var chars = title.ToLowerInvariant()
            .Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var s = new string(chars);
        while (s.Contains("--")) s = s.Replace("--", "-");
        s = s.Trim('-');
        if (s.Length == 0) return "untitled";
        return s.Length > 40 ? s[..40].TrimEnd('-') : s;
    }
}
