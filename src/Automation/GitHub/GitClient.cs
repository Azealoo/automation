using System.Diagnostics;
using System.Text;

namespace Automation.GitHub;

public sealed record GitResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

/// Minimal git wrapper. Does only what the loop needs: clone, fetch, reset to
/// default branch, checkout new branch, add/commit/push. No force-push path.
public sealed class GitClient
{
    private readonly string _bin;

    public GitClient(string bin = "git") { _bin = bin; }

    public async Task EnsureCheckoutAsync(string repo, string targetDir, string? defaultBranchHint, CancellationToken ct = default)
    {
        if (!Directory.Exists(Path.Join(targetDir, ".git")))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            var clone = await RunAsync(Directory.GetCurrentDirectory(), new[] {
                "clone", $"https://github.com/{repo}.git", targetDir
            }, ct);
            if (!clone.Success)
                throw new InvalidOperationException($"git clone {repo} failed: {clone.Stderr}");
            return;
        }

        var fetch = await RunAsync(targetDir, new[] { "fetch", "--prune", "origin" }, ct);
        if (!fetch.Success)
            throw new InvalidOperationException($"git fetch failed in {targetDir}: {fetch.Stderr}");

        var defaultBranch = defaultBranchHint ?? await DetectDefaultBranchAsync(targetDir, ct);
        var checkout = await RunAsync(targetDir, new[] { "checkout", defaultBranch }, ct);
        if (!checkout.Success)
            throw new InvalidOperationException($"git checkout {defaultBranch} failed: {checkout.Stderr}");

        var reset = await RunAsync(targetDir, new[] { "reset", "--hard", $"origin/{defaultBranch}" }, ct);
        if (!reset.Success)
            throw new InvalidOperationException($"git reset --hard origin/{defaultBranch} failed: {reset.Stderr}");
    }

    public async Task<string> DetectDefaultBranchAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunAsync(repoDir, new[] { "symbolic-ref", "refs/remotes/origin/HEAD", "--short" }, ct);
        if (result.Success)
        {
            var s = result.Stdout.Trim();
            if (s.StartsWith("origin/", StringComparison.Ordinal)) return s["origin/".Length..];
        }
        // Fallback: try `main`, then `master`.
        foreach (var candidate in new[] { "main", "master" })
        {
            var check = await RunAsync(repoDir, new[] { "show-ref", "--verify", $"refs/remotes/origin/{candidate}" }, ct);
            if (check.Success) return candidate;
        }
        throw new InvalidOperationException($"could not detect default branch for {repoDir}");
    }

    public async Task CheckoutNewBranchAsync(string repoDir, string branch, CancellationToken ct = default)
    {
        var result = await RunAsync(repoDir, new[] { "checkout", "-b", branch }, ct);
        if (!result.Success)
            throw new InvalidOperationException($"git checkout -b {branch} failed: {result.Stderr}");
    }

    /// Switch to `branch` tracking `origin/branch`. Uses `checkout -B` which
    /// force-updates the local ref if it already exists; the PR-comment-loop
    /// caller relies on this so it can resume a branch across runs. Any local
    /// uncommitted state on the branch is discarded — if that happens, the
    /// caller should have committed before invoking.
    public async Task<bool> CheckoutTrackingBranchAsync(string repoDir, string branch, CancellationToken ct = default)
    {
        var result = await RunAsync(repoDir, new[] { "checkout", "-B", branch, $"origin/{branch}" }, ct);
        return result.Success;
    }

    public async Task<bool> HasStagedOrUnstagedChangesAsync(string repoDir, CancellationToken ct = default)
    {
        var result = await RunAsync(repoDir, new[] { "status", "--porcelain" }, ct);
        return result.Success && !string.IsNullOrWhiteSpace(result.Stdout);
    }

    public async Task CommitAllAsync(string repoDir, string message, CancellationToken ct = default)
    {
        var add = await RunAsync(repoDir, new[] { "add", "-A" }, ct);
        if (!add.Success) throw new InvalidOperationException($"git add -A failed: {add.Stderr}");
        var commit = await RunAsync(repoDir, new[] { "commit", "-m", message }, ct);
        if (!commit.Success) throw new InvalidOperationException($"git commit failed: {commit.Stderr}");
    }

    public async Task PushAsync(string repoDir, string branch, CancellationToken ct = default)
    {
        // Explicitly NOT --force. CLAUDE.md forbids it.
        var result = await RunAsync(repoDir, new[] { "push", "-u", "origin", branch }, ct);
        if (!result.Success) throw new InvalidOperationException($"git push failed: {result.Stderr}");
    }

    private async Task<GitResult> RunAsync(string wd, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _bin,
            WorkingDirectory = wd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            throw;
        }
        return new GitResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
