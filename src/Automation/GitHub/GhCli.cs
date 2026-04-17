using System.Diagnostics;
using System.Text;

namespace Automation.GitHub;

public sealed record GhResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public interface IGhCli
{
    Task<GhResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default);
}

/// Thin wrapper around the `gh` CLI. Used for everything that touches GitHub.
/// We intentionally shell out instead of using Octokit because gh handles auth,
/// rate limiting, and pagination for us — CLAUDE.md §Simplicity First.
public sealed class GhCli : IGhCli
{
    private readonly string _bin;

    public GhCli(string bin = "gh")
    {
        _bin = bin;
    }

    public async Task<GhResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _bin,
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

        return new GhResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
