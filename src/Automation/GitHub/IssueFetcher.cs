using System.Text.Json;
using Automation.Logging;

namespace Automation.GitHub;

public sealed class IssueFetcher
{
    // `gh issue list` scoped to a repo with --repo doesn't accept `repository`
    // in the JSON field list — it's implied. We backfill Issue.Repository
    // below so downstream code can still rely on RepoFullName.
    private const string IssueFields = "number,title,body,state,updatedAt,url,labels";
    private readonly IGhCli _gh;

    public IssueFetcher(IGhCli gh)
    {
        _gh = gh;
    }

    public async Task<IReadOnlyList<Issue>> FetchAssignedIssuesAsync(string repo, int pageSize, CancellationToken ct = default)
    {
        var args = new[]
        {
            "issue", "list",
            "--repo", repo,
            "--assignee", "@me",
            "--state", "open",
            "--limit", pageSize.ToString(),
            "--json", IssueFields,
        };
        var result = await _gh.RunAsync(args, ct);
        if (!result.Success)
            throw new InvalidOperationException($"gh issue list failed for {repo} (exit {result.ExitCode}): {result.Stderr}");

        var issues = JsonSerializer.Deserialize<List<Issue>>(result.Stdout)
            ?? new List<Issue>();
        return issues
            .Select(i => i.Repository is null
                ? i with { Repository = new IssueRepository(repo) }
                : i)
            .ToList();
    }

    /// Maximum single-attachment size, in bytes. Prevents a malicious or
    /// misconfigured URL from OOMing the loop. 10 MB is well above any real
    /// screenshot from a `gh`-uploaded attachment.
    public const int MaxImageBytes = 10 * 1024 * 1024;

    /// Only allow downloads from GitHub's own attachment hosts. Protects
    /// against SSRF: a malicious issue body could otherwise make the loop
    /// request 169.254.169.254 (IMDS), localhost:N, internal LAN hosts, or
    /// arbitrary external servers.
    internal static readonly string[] AllowedHostSuffixes = new[]
    {
        ".githubusercontent.com",
        ".github.com",
        "github.com",
    };

    /// Download image attachments referenced in an issue body (gh-uploaded
    /// assets appear as markdown image links) to `targetDir`. Returns local
    /// file paths for the `claude -p` image list.
    ///
    /// Skipped silently (but logged): URLs outside the GitHub host allowlist,
    /// oversize responses, and HTTP failures. One bad attachment never blocks
    /// classification.
    public static async Task<IReadOnlyList<string>> DownloadImageAttachmentsAsync(
        Issue issue, string targetDir, HttpClient http, ILogger? log = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDir);
        var urls = MarkdownImageExtractor.Extract(issue.Body);
        var paths = new List<string>();
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            if (!IsAllowedUrl(url))
            {
                log?.Warn("issue.attachment_host_blocked", new { issue = issue.Number, url });
                continue;
            }

            var ext = GuessExtension(url);
            var path = Path.Join(targetDir, $"attachment-{i}{ext}");
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    log?.Warn("issue.attachment_bad_status", new { issue = issue.Number, url, status = (int)response.StatusCode });
                    continue;
                }
                if (response.Content.Headers.ContentLength is long cl && cl > MaxImageBytes)
                {
                    log?.Warn("issue.attachment_too_large", new { issue = issue.Number, url, content_length = cl });
                    continue;
                }

                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxImageBytes)
                    {
                        log?.Warn("issue.attachment_too_large", new { issue = issue.Number, url, streamed_bytes = total });
                        dst.Close();
                        try { File.Delete(path); } catch { /* best-effort */ }
                        goto nextUrl;
                    }
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                paths.Add(path);
            }
            catch (Exception ex)
            {
                log?.Warn("issue.attachment_download_failed", new { issue = issue.Number, url, error = ex.Message });
            }
            nextUrl: ;
        }
        return paths;
    }

    internal static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        var host = uri.Host.ToLowerInvariant();
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (suffix.StartsWith('.') && host.EndsWith(suffix, StringComparison.Ordinal)) return true;
            if (!suffix.StartsWith('.') && host == suffix) return true;
        }
        return false;
    }

    private static string GuessExtension(string url)
    {
        var dot = url.LastIndexOf('.');
        if (dot < 0) return ".bin";
        var ext = url[dot..];
        var q = ext.IndexOf('?');
        if (q > 0) ext = ext[..q];
        return ext.Length is > 1 and <= 6 ? ext : ".bin";
    }
}

internal static class MarkdownImageExtractor
{
    public static List<string> Extract(string body)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(body)) return urls;
        var matches = System.Text.RegularExpressions.Regex.Matches(
            body, @"!\[[^\]]*\]\((https?://[^\s)]+)\)");
        foreach (System.Text.RegularExpressions.Match m in matches)
            urls.Add(m.Groups[1].Value);
        return urls;
    }
}
