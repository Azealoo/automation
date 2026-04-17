using System.Text.Json.Serialization;

namespace Automation.GitHub;

public sealed record Issue(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("repository")] IssueRepository? Repository,
    [property: JsonPropertyName("labels")] IReadOnlyList<Label>? Labels)
{
    public string RepoFullName =>
        Repository?.NameWithOwner ?? throw new InvalidOperationException("Issue.Repository was not populated — call IssueFetcher.FetchAssignedIssuesAsync to backfill");
}

public sealed record IssueRepository(
    [property: JsonPropertyName("nameWithOwner")] string NameWithOwner);

public sealed record Label(
    [property: JsonPropertyName("name")] string Name);

public sealed record PullRequest(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("headRefName")] string HeadRefName,
    [property: JsonPropertyName("baseRefName")] string BaseRefName,
    [property: JsonPropertyName("isDraft")] bool IsDraft);

public sealed record IssueComment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("author")] CommentAuthor? Author,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("createdAt")] string CreatedAt);

public sealed record ReviewComment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("author")] CommentAuthor? Author,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("createdAt")] string CreatedAt);

public sealed record CommentAuthor(
    [property: JsonPropertyName("login")] string Login);
