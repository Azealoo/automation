using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automation.GitHub;
using Automation.Logging;

namespace Automation.Claude;

public enum Verdict
{
    Ready,
    NotReady,
}

public sealed record ClassifierResult(
    [property: JsonPropertyName("verdict")] string VerdictRaw,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("questions")] IReadOnlyList<string> Questions,
    [property: JsonPropertyName("likely_files")] IReadOnlyList<string> LikelyFiles)
{
    public Verdict ParsedVerdict =>
        VerdictRaw == "ready" ? Verdict.Ready : Verdict.NotReady;
}

public sealed class Classifier
{
    private readonly IClaudeCli _claude;
    private readonly ILogger _log;
    private readonly string _promptTemplate;

    public Classifier(IClaudeCli claude, ILogger log, string promptTemplate)
    {
        _claude = claude;
        _log = log;
        _promptTemplate = promptTemplate;
    }

    public async Task<ClassifierResult> ClassifyAsync(
        Issue issue,
        string repoCheckoutPath,
        IReadOnlyList<string> imagePaths,
        CancellationToken ct = default)
    {
        var prompt = _promptTemplate.Replace("{{ISSUE_PAYLOAD}}", RenderIssuePayload(issue));
        var invocation = new ClaudeInvocation(
            Prompt: prompt,
            WorkingDirectory: repoCheckoutPath,
            ImagePaths: imagePaths,
            AllowedTools: new[] { "Read", "Grep", "Glob" },
            OutputFormat: null,
            Timeout: TimeSpan.FromMinutes(10));

        _log.Info("classifier.start", new { repo = issue.RepoFullName, issue = issue.Number });
        var result = await _claude.RunAsync(invocation, ct);
        if (!result.Success)
        {
            _log.Error("classifier.claude_failed", new { repo = issue.RepoFullName, issue = issue.Number, stderr = result.Stderr });
            throw new InvalidOperationException($"classifier claude invocation failed: {result.Stderr}");
        }

        var parsed = ParseVerdictJson(result.Stdout);
        _log.Info("classifier.done", new
        {
            repo = issue.RepoFullName,
            issue = issue.Number,
            verdict = parsed.VerdictRaw,
            question_count = parsed.Questions.Count,
        });
        return parsed;
    }

    internal static ClassifierResult ParseVerdictJson(string stdout)
    {
        var json = ExtractFirstJsonObject(stdout)
            ?? throw new InvalidDataException("classifier output did not contain a JSON object");
        var parsed = JsonSerializer.Deserialize<ClassifierResult>(json)
            ?? throw new InvalidDataException("classifier JSON failed to deserialize");
        if (parsed.VerdictRaw is not ("ready" or "not_ready"))
            throw new InvalidDataException($"classifier verdict must be 'ready' or 'not_ready', got: {parsed.VerdictRaw}");
        return parsed;
    }

    /// claude -p may prepend or append prose around the JSON block despite our
    /// "strict JSON only" instruction. Extract the first balanced {...} chunk.
    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') inString = !inString;
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    private static string RenderIssuePayload(Issue issue)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Repository: {issue.RepoFullName}");
        sb.AppendLine($"Issue #{issue.Number}: {issue.Title}");
        sb.AppendLine($"URL: {issue.Url}");
        sb.AppendLine($"State: {issue.State}");
        if (issue.Labels is { Count: > 0 })
            sb.AppendLine($"Labels: {string.Join(", ", issue.Labels.Select(l => l.Name))}");
        sb.AppendLine();
        sb.AppendLine("--- BODY ---");
        sb.AppendLine(issue.Body);
        return sb.ToString();
    }
}
