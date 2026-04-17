using Automation.Actions;
using Automation.Loop;
using Automation.State;

namespace Automation.Tests.Loop;

/// Unit coverage for the ledger-state decision that PollOrchestrator
/// applies after BranchAndPr returns. The decision is the only
/// post-outcome logic that affects whether an issue gets retried
/// on the next 15-minute tick, so it carries the same weight as
/// branch/PR idempotency — see CLAUDE.md §Operational guardrails.
public class PollOrchestratorTests
{
    private const string UpdatedAt = "2026-04-17T04:00:00Z";

    [Fact]
    public void SuccessfulOutcomeAdvancesLedgerWithReadyVerdictAndPrNumber()
    {
        var outcome = new BranchAndPrOutcome(
            Succeeded: true,
            PrNumber: 42,
            Branch: "auto/issue-1-seed-flag");

        var state = PollOrchestrator.LedgerStateForOutcome(outcome, UpdatedAt);

        Assert.NotNull(state);
        Assert.Equal(UpdatedAt, state!.LastUpdatedAt);
        Assert.Equal("ready", state.Verdict);
        Assert.Equal(42, state.PrNumber);
    }

    [Fact]
    public void PatScopeBlockedOutcomeAdvancesLedgerWithBlockedVerdictAndNullPr()
    {
        // Without this advance, the PAT-scope failure retries every tick
        // forever — observed overnight on Azealoo/miniAgent#10.
        var outcome = new BranchAndPrOutcome(
            Succeeded: false,
            PrNumber: null,
            Branch: "auto/issue-10-seed-flag",
            BlockedReason: "pat_scope");

        var state = PollOrchestrator.LedgerStateForOutcome(outcome, UpdatedAt);

        Assert.NotNull(state);
        Assert.Equal(UpdatedAt, state!.LastUpdatedAt);
        Assert.Equal("pr_blocked_pat_scope", state.Verdict);
        Assert.Null(state.PrNumber);
    }

    [Fact]
    public void GenericFailureReturnsNullSoNextTickRetries()
    {
        // Rate limits, transient gh failures, etc. Ledger must stay
        // absent so the next tick has another go.
        var outcome = new BranchAndPrOutcome(
            Succeeded: false,
            PrNumber: null,
            Branch: "auto/issue-3-whatever");

        Assert.Null(PollOrchestrator.LedgerStateForOutcome(outcome, UpdatedAt));
    }

    [Fact]
    public void SucceededButMissingPrNumberReturnsNull()
    {
        // Defense in depth: BranchAndPr constructs Succeeded = (PrNumber
        // is not null), so this shouldn't happen — but if it ever does
        // (dry-run outcome leaking into the live branch, a bug in RunAsync),
        // we must not record "ready" with a null PR number.
        var outcome = new BranchAndPrOutcome(
            Succeeded: true,
            PrNumber: null,
            Branch: "auto/issue-7-whatever");

        Assert.Null(PollOrchestrator.LedgerStateForOutcome(outcome, UpdatedAt));
    }

    [Fact]
    public void UnrecognizedBlockedReasonDoesNotAdvanceLedger()
    {
        // Future BlockedReason values must be opted into explicitly before
        // they stop the retry loop. An unknown reason falls through to the
        // generic retry path rather than silently poisoning the ledger.
        var outcome = new BranchAndPrOutcome(
            Succeeded: false,
            PrNumber: null,
            Branch: "auto/issue-5-whatever",
            BlockedReason: "future_reason_we_havent_taught_yet");

        Assert.Null(PollOrchestrator.LedgerStateForOutcome(outcome, UpdatedAt));
    }
}
