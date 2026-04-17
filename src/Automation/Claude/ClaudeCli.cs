using System.Diagnostics;
using System.Text;

namespace Automation.Claude;

public sealed record ClaudeResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public interface IClaudeCli
{
    Task<ClaudeResult> RunAsync(ClaudeInvocation invocation, CancellationToken ct = default);
}

public sealed record ClaudeInvocation(
    string Prompt,
    string WorkingDirectory,
    IReadOnlyList<string>? ImagePaths = null,
    IReadOnlyList<string>? AllowedTools = null,
    string? OutputFormat = null,
    TimeSpan? Timeout = null);

public sealed class ClaudeCli : IClaudeCli
{
    private readonly string _bin;

    public ClaudeCli(string bin = "claude")
    {
        _bin = bin;
    }

    public async Task<ClaudeResult> RunAsync(ClaudeInvocation invocation, CancellationToken ct = default)
    {
        var args = new List<string> { "-p" };
        if (invocation.AllowedTools is { Count: > 0 })
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", invocation.AllowedTools));
        }
        if (!string.IsNullOrEmpty(invocation.OutputFormat))
        {
            args.Add("--output-format");
            args.Add(invocation.OutputFormat);
        }
        // `--allowedTools <tools...>` is variadic in the claude CLI and will
        // otherwise eat the prompt as another tool name. The `--` marker
        // forces everything after it to be parsed as positional.
        args.Add("--");
        args.Add(BuildPrompt(invocation));

        var psi = new ProcessStartInfo
        {
            FileName = _bin,
            WorkingDirectory = invocation.WorkingDirectory,
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

        using var timeoutCts = new CancellationTokenSource();
        if (invocation.Timeout.HasValue) timeoutCts.CancelAfter(invocation.Timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            return new ClaudeResult(-1, stdout.ToString(), "claude invocation cancelled or timed out\n" + stderr);
        }

        return new ClaudeResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string BuildPrompt(ClaudeInvocation invocation)
    {
        if (invocation.ImagePaths is null || invocation.ImagePaths.Count == 0)
            return invocation.Prompt;
        var sb = new StringBuilder();
        sb.AppendLine("Image attachments from the issue (read with the Read tool):");
        foreach (var p in invocation.ImagePaths) sb.AppendLine($"- {p}");
        sb.AppendLine();
        sb.Append(invocation.Prompt);
        return sb.ToString();
    }
}
