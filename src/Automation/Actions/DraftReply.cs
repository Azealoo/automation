using System.Text;
using Automation.Claude;
using Automation.GitHub;
using Automation.Logging;

namespace Automation.Actions;

public sealed class DraftReply
{
    private readonly string _draftsDir;
    private readonly ILogger _log;
    private readonly bool _dryRun;

    public DraftReply(string draftsDir, ILogger log, bool dryRun)
    {
        _draftsDir = draftsDir;
        _log = log;
        _dryRun = dryRun;
    }

    public void Write(Issue issue, ClassifierResult verdict)
    {
        var body = Render(issue, verdict);
        var relRepo = issue.RepoFullName.Replace('/', '-');
        var path = Path.Join(_draftsDir, $"{relRepo}-{issue.Number}.md");

        if (_dryRun)
        {
            _log.Info("draft.dry_run", new { path, body_preview = body[..Math.Min(200, body.Length)] });
            return;
        }

        Directory.CreateDirectory(_draftsDir);
        File.WriteAllText(path, body);
        _log.Info("draft.written", new { path, issue = issue.Number, repo = issue.RepoFullName });
    }

    internal static string Render(Issue issue, ClassifierResult verdict)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<!-- Draft reply for {issue.RepoFullName}#{issue.Number} -->");
        sb.AppendLine($"<!-- URL: {issue.Url} -->");
        sb.AppendLine($"<!-- Review and adjust before posting. This was NOT auto-posted. -->");
        sb.AppendLine();
        sb.AppendLine("Thanks for filing this. Before I can pick it up I need a bit more detail:");
        sb.AppendLine();
        foreach (var q in verdict.Questions)
            sb.AppendLine($"- {q}");
        if (!string.IsNullOrWhiteSpace(verdict.Summary))
        {
            sb.AppendLine();
            sb.AppendLine("Context from my first read:");
            sb.AppendLine();
            sb.AppendLine(verdict.Summary);
        }
        return sb.ToString();
    }
}
