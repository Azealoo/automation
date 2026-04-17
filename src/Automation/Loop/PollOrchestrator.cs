using Automation.Actions;
using Automation.Claude;
using Automation.Config;
using Automation.GitHub;
using Automation.Logging;
using Automation.State;

namespace Automation.Loop;

/// One iteration of the main loop. Extracted so PollTimer and the --once CLI
/// path share the same code path.
public sealed class PollOrchestrator
{
    private readonly AppConfig _config;
    private readonly ILogger _log;
    private readonly IssueFetcher _issues;
    private readonly PrFetcher _prs;
    private readonly GitClient _git;
    private readonly Classifier _classifier;
    private readonly BranchAndPr _branchAndPr;
    private readonly PrResponder _prResponder;
    private readonly DraftReply _draft;
    private readonly Ledger _ledger;
    private readonly HttpClient _http;

    public PollOrchestrator(
        AppConfig config,
        ILogger log,
        IssueFetcher issues,
        PrFetcher prs,
        GitClient git,
        Classifier classifier,
        BranchAndPr branchAndPr,
        PrResponder prResponder,
        DraftReply draft,
        Ledger ledger,
        HttpClient http)
    {
        _config = config;
        _log = log;
        _issues = issues;
        _prs = prs;
        _git = git;
        _classifier = classifier;
        _branchAndPr = branchAndPr;
        _prResponder = prResponder;
        _draft = draft;
        _ledger = ledger;
        _http = http;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        _log.Info("loop.tick.start", new { dry_run = _config.DryRun });
        foreach (var repo in _config.WatchedRepos)
        {
            try
            {
                await HandleRepoAsync(repo, ct);
            }
            catch (Exception ex)
            {
                _log.Error("loop.repo_failed", new { repo, error = ex.Message });
            }
        }
        _log.Info("loop.tick.done");
    }

    private async Task HandleRepoAsync(string repo, CancellationToken ct)
    {
        var issues = await _issues.FetchAssignedIssuesAsync(repo, _config.GithubApiPageSize, ct);
        _log.Info("loop.repo.fetched", new { repo, count = issues.Count });

        var toProcess = issues
            .Where(i => !_ledger.IsIssueAlreadyHandled(new IssueKey(repo, i.Number), i.UpdatedAt))
            .Take(_config.MaxIssuesPerTick)
            .ToList();

        foreach (var issue in toProcess)
            await HandleIssueAsync(repo, issue, ct);

        // PR comment loop for any PRs we previously opened against this repo.
        await HandleOpenPrsAsync(repo, issues, ct);
    }

    private async Task HandleIssueAsync(string repo, Issue issue, CancellationToken ct)
    {
        var checkoutDir = Path.Join(_config.ExpandedWorkdir, RepoDirName(repo));
        await _git.EnsureCheckoutAsync(repo, checkoutDir, _config.DefaultBranchHint, ct);

        var attachmentsDir = Path.Join(checkoutDir, ".automation-attachments", $"issue-{issue.Number}");
        Directory.CreateDirectory(attachmentsDir);
        var imagePaths = await IssueFetcher.DownloadImageAttachmentsAsync(
            issue, attachmentsDir, _http, _log, ct);

        var verdict = await _classifier.ClassifyAsync(issue, checkoutDir, imagePaths, ct);

        if (verdict.ParsedVerdict == Verdict.NotReady)
        {
            _draft.Write(issue, verdict);
            // Dry-run must not advance the ledger for either verdict —
            // otherwise a dry-run run permanently hides the issue from
            // subsequent live runs (exact bug demonstrated on the
            // Azealoo/automation-sandbox #2 smoke test).
            if (_config.DryRun)
            {
                _log.Info("loop.issue.dry_run_not_ready", new { repo, issue = issue.Number });
                return;
            }
            _ledger.SetIssue(new IssueKey(repo, issue.Number),
                new IssueState(issue.UpdatedAt, "not_ready", null));
            return;
        }

        var outcome = await _branchAndPr.RunAsync(issue, checkoutDir, verdict, imagePaths, ct);

        // Only advance the ledger when we actually produced a PR. If the PR
        // creation failed, leave the entry absent so the next tick retries.
        // In dry-run we don't record "ready" either — the next real run must
        // still see this issue fresh.
        if (_config.DryRun)
        {
            _log.Info("loop.issue.dry_run_ready", new { repo, issue = issue.Number });
            return;
        }
        if (outcome.Succeeded && outcome.PrNumber is not null)
        {
            _ledger.SetIssue(new IssueKey(repo, issue.Number),
                new IssueState(issue.UpdatedAt, "ready", outcome.PrNumber));
        }
        else
        {
            _log.Warn("loop.issue.pr_creation_skipped", new
            {
                repo,
                issue = issue.Number,
                branch = outcome.Branch,
                succeeded = outcome.Succeeded,
            });
        }
    }

    private async Task HandleOpenPrsAsync(string repo, IReadOnlyList<Issue> currentIssues, CancellationToken ct)
    {
        foreach (var issue in currentIssues)
        {
            var key = new IssueKey(repo, issue.Number);
            var state = _ledger.GetIssue(key);
            if (state?.PrNumber is null) continue;
            var prNumber = state.PrNumber.Value;

            var issueComments = await _prs.GetIssueCommentsAsync(repo, prNumber, ct);
            var reviewComments = await _prs.GetReviewCommentsAsync(repo, prNumber, ct);
            var prKey = new PrKey(repo, prNumber);
            var prState = _ledger.GetPr(prKey) ?? new PrState(null, null);

            var newIssue = issueComments.Where(c => c.Id > (prState.LastCommentId ?? 0)).ToList();
            var newReview = reviewComments.Where(c => c.Id > (prState.LastReviewCommentId ?? 0)).ToList();
            if (newIssue.Count == 0 && newReview.Count == 0) continue;

            var checkoutDir = Path.Join(_config.ExpandedWorkdir, RepoDirName(repo));
            await _git.EnsureCheckoutAsync(repo, checkoutDir, _config.DefaultBranchHint, ct);
            var branch = $"{BranchAndPr.BranchPrefix}-{issue.Number}-{BranchAndPr.Slug(issue.Title)}";

            if (_config.DryRun)
            {
                _log.Info("pr_comment_loop.dry_run", new
                {
                    repo, pr = prNumber, branch,
                    new_issue_comments = newIssue.Count,
                    new_review_comments = newReview.Count,
                });
                // Dry-run must NOT advance the PR watermark; otherwise a dry-run
                // permanently hides these comments from the next real run.
                continue;
            }

            var switched = await _git.CheckoutTrackingBranchAsync(checkoutDir, branch, ct);
            if (!switched)
            {
                _log.Warn("pr_comment_loop.checkout_failed", new { repo, pr = prNumber, branch });
                continue;
            }

            await _prResponder.RespondAsync(checkoutDir, newIssue, newReview, ct);
            if (await _git.HasStagedOrUnstagedChangesAsync(checkoutDir, ct))
            {
                await _git.CommitAllAsync(checkoutDir, $"auto: address review comments on PR #{prNumber}", ct);
                await _git.PushAsync(checkoutDir, branch, ct);
            }

            _ledger.SetPr(prKey, new PrState(
                LastCommentId: issueComments.Count > 0 ? issueComments.Max(c => c.Id) : prState.LastCommentId,
                LastReviewCommentId: reviewComments.Count > 0 ? reviewComments.Max(c => c.Id) : prState.LastReviewCommentId));
        }
    }

    private static string RepoDirName(string repo) => repo.Replace('/', '-');
}
