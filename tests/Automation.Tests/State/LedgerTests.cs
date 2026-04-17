using Automation.State;

namespace Automation.Tests.State;

public class LedgerTests
{
    [Fact]
    public void EmptyLedgerReturnsNullForUnknownKeys()
    {
        var path = NewTempPath();
        var ledger = new Ledger(path);

        Assert.Null(ledger.GetIssue(new IssueKey("you/repo", 1)));
        Assert.Null(ledger.GetPr(new PrKey("you/repo", 1)));
    }

    [Fact]
    public void IsIssueAlreadyHandledReturnsFalseOnFirstSeen()
    {
        var ledger = new Ledger(NewTempPath());
        Assert.False(ledger.IsIssueAlreadyHandled(new IssueKey("you/repo", 1), "2026-04-16T12:00:00Z"));
    }

    [Fact]
    public void IsIssueAlreadyHandledReturnsTrueWhenTimestampMatches()
    {
        var ledger = new Ledger(NewTempPath());
        var key = new IssueKey("you/repo", 1);
        ledger.SetIssue(key, new IssueState("2026-04-16T12:00:00Z", "ready", 42));

        Assert.True(ledger.IsIssueAlreadyHandled(key, "2026-04-16T12:00:00Z"));
    }

    [Fact]
    public void IsIssueAlreadyHandledReturnsFalseWhenIssueWasUpdated()
    {
        var ledger = new Ledger(NewTempPath());
        var key = new IssueKey("you/repo", 1);
        ledger.SetIssue(key, new IssueState("2026-04-16T12:00:00Z", "ready", 42));

        Assert.False(ledger.IsIssueAlreadyHandled(key, "2026-04-17T08:00:00Z"));
    }

    [Fact]
    public void WritesAreDurableAcrossInstances()
    {
        var path = NewTempPath();
        var first = new Ledger(path);
        first.SetIssue(new IssueKey("you/repo", 1), new IssueState("t1", "ready", 42));
        first.SetPr(new PrKey("you/repo", 42), new PrState(100, 200));

        var second = new Ledger(path);
        Assert.Equal(42, second.GetIssue(new IssueKey("you/repo", 1))!.PrNumber);
        Assert.Equal(100, second.GetPr(new PrKey("you/repo", 42))!.LastCommentId);
        Assert.Equal(200, second.GetPr(new PrKey("you/repo", 42))!.LastReviewCommentId);
    }

    private static string NewTempPath() =>
        Path.Join(Path.GetTempPath(), $"ledger-{Guid.NewGuid()}.json");
}
