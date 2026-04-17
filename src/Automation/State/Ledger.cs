using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.State;

public sealed record IssueKey(string Repo, int Number);
public sealed record PrKey(string Repo, int Number);

public sealed record IssueState(
    [property: JsonPropertyName("last_updated_at")] string? LastUpdatedAt,
    [property: JsonPropertyName("verdict")] string? Verdict,
    [property: JsonPropertyName("pr_number")] int? PrNumber);

public sealed record PrState(
    [property: JsonPropertyName("last_comment_id")] long? LastCommentId,
    [property: JsonPropertyName("last_review_comment_id")] long? LastReviewCommentId);

public sealed record LedgerDocument(
    [property: JsonPropertyName("issues")] Dictionary<string, IssueState> Issues,
    [property: JsonPropertyName("prs")] Dictionary<string, PrState> Prs)
{
    public static LedgerDocument Empty() => new(new(), new());
}

public sealed class Ledger
{
    private readonly string _path;
    private LedgerDocument _doc;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public Ledger(string path)
    {
        _path = path;
        _doc = Load(path);
    }

    public IssueState? GetIssue(IssueKey key) =>
        _doc.Issues.TryGetValue(Serialize(key), out var v) ? v : null;

    public void SetIssue(IssueKey key, IssueState state)
    {
        _doc.Issues[Serialize(key)] = state;
        Save();
    }

    public PrState? GetPr(PrKey key) =>
        _doc.Prs.TryGetValue(Serialize(key), out var v) ? v : null;

    public void SetPr(PrKey key, PrState state)
    {
        _doc.Prs[Serialize(key)] = state;
        Save();
    }

    /// Returns true if the issue has already been handled at the given updated_at timestamp.
    /// The CLAUDE.md idempotency rule: re-running the loop must not duplicate work.
    public bool IsIssueAlreadyHandled(IssueKey key, string updatedAt)
    {
        var existing = GetIssue(key);
        return existing is not null && existing.LastUpdatedAt == updatedAt;
    }

    private static string Serialize(IssueKey k) => $"{k.Repo}#{k.Number}";
    private static string Serialize(PrKey k) => $"{k.Repo}!pr{k.Number}";

    private static LedgerDocument Load(string path)
    {
        if (!File.Exists(path)) return LedgerDocument.Empty();
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return LedgerDocument.Empty();
        return JsonSerializer.Deserialize<LedgerDocument>(json, Options) ?? LedgerDocument.Empty();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_doc, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
