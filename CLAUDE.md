# automation

Personal GitHub-issue → PR loop modeled on `demo.jpeg` (r/ClaudeAI, "I automated
most of my job"), swapped from GitLab to GitHub. Runs unattended on this Mac as
a `launchd` agent. Every write-side effect (draft reply, branch push, PR) is
reviewable before it lands; the app never merges its own work.

## Reference architecture (what exists today)

A single .NET 10 console app (`src/Automation/Program.cs`) runs two timers in
one process:

- **Poll loop** (`Loop/PollOrchestrator.cs`, every `poll_interval_seconds`,
  default 900s)
  1. **Fetch** (`GitHub/IssueFetcher.cs`) — `gh issue list --repo <r>
     --assignee @me --state open` for every repo in `watched_repos`.
  2. **Skip handled** (`State/Ledger.cs`) — keyed by `(repo, issue.Number,
     updated_at)` in `logs/ledger.json`.
  3. **Classify** (`Claude/Classifier.cs`) — `claude -p` with read-only tools
     (`Read, Grep, Glob`), the repo checked out under `workdir`, and any
     `githubusercontent.com` image attachments downloaded through
     `IssueFetcher.DownloadImageAttachmentsAsync` (SSRF allowlist enforced).
     Returns strict JSON: `{verdict, summary, questions, likely_files}`.
  4. **Branch** (`verdict == "not_ready"`, `Actions/DraftReply.cs`) — writes
     `drafts/<repo>-<n>.md` for the user to review and post manually.
  5. **Implement** (`verdict == "ready"`, `Actions/BranchAndPr.cs` →
     `Claude/Implementer.cs`) — creates `auto/issue-<n>-<slug>`, runs `claude
     -p` with `Bash, Read, Write, Edit, Grep, Glob` and a 30-min timeout,
     commits, pushes, opens a **draft** PR via `gh pr create --draft`.
  6. **PR comment loop** (`PollOrchestrator.HandleOpenPrsAsync` →
     `Claude/PrResponder.cs`) — for every PR the ledger knows about, pulls new
     issue-comments and review-comments via `gh api` (REST, not GraphQL — see
     `PrFetcher.cs` comment), watermarks by comment `id`, re-checks out the
     branch, runs the PR responder, commits and pushes.
- **Jiggle loop** (`Loop/JiggleTimer.cs`, every `jiggle_interval_seconds`,
  default 60s) — nudges the cursor 1px and back via CoreGraphics P/Invoke so
  Teams/Slack doesn't mark the user idle. Gated by `jiggle_enabled` and
  implicitly disabled by `--dry-run`.

Everything is logged as JSON lines to `logs/automation-<date>.log` via
`Logging/JsonLineLogger.cs`.

## Build & run

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"

# Config
cp config/config.example.json config/config.json
$EDITOR config/config.json      # watched_repos[] is required

# One iteration, no writes, no network posts
dotnet run --project src/Automation -- --dry-run --once

# Live, single tick
dotnet run --project src/Automation -- --once

# Foreground loop
dotnet run --project src/Automation

# Unattended (launchd agent)
./scripts/install-launchd.sh
tail -f logs/automation-*.log
./scripts/uninstall-launchd.sh

# Tests + guardrail grep
dotnet test
./scripts/anti-check.sh
```

CLI flags: `--dry-run`, `--no-jiggle`, `--once`. Real config flags live in
`config/config.json` (see `Config/AppConfig.cs`).

## Working principles

These four principles govern every change. They exist because this project is a
personal automation: small surface, high blast radius (it pushes branches,
opens PRs, runs unattended, and shells out to `claude -p` with write tools on
your user account). Cleverness is expensive here.

### 1. Think Before Coding

- Before writing code, state in one or two sentences: what problem this solves,
  and which stage of the loop it belongs to (poll, classify, draft, branch/PR,
  PR comment loop, jiggle, ledger, logging, config). If it doesn't map, stop
  and ask — don't invent a new subsystem.
- Re-read `demo.jpeg` when unsure about the shape of the flow, and re-read the
  relevant source file before editing it. The in-repo code is the spec; `demo.jpeg`
  is the origin story.
- Runtime is .NET 10 with nullable + implicit usings enabled. Target framework
  is `net10.0`. Don't drag in new runtimes, languages, or big framework
  dependencies without asking.

### 2. Simplicity First

- The reference explicitly calls the workflow "super simple." The baseline is:
  one console app, two timers, shell out to `claude` / `gh` / `git`. Beat that
  baseline only with evidence.
- No frameworks, queues, databases, web dashboards, or DI containers. State of
  record is GitHub; the local ledger is just a JSON file for idempotency. The
  logger is append-only JSON lines. Keep it that way.
- Shell out to `gh`, `git`, and `claude` via `GhCli`, `GitClient`, `ClaudeCli`
  rather than reimplementing their behavior or reaching for Octokit/LibGit2.
- One file per responsibility (`IssueFetcher`, `PrFetcher`, `Classifier`,
  `Implementer`, `PrResponder`, `BranchAndPr`, `DraftReply`, `Ledger`,
  `JiggleTimer`, `PollOrchestrator`). Keep each file focused; don't merge
  stages together.
- Secrets come from `gh auth` (for GitHub) and `config/config.json`
  (gitignored). Never hardcoded, never committed. The repo also ignores
  `.env`, `logs/`, and `drafts/`.

### 3. Surgical Changes

- Touch only what the task requires. A bug in the classifier does not pull in
  a refactor of the poller. A change to `PollOrchestrator` is not an
  invitation to rename `Ledger` keys.
- Don't rename, reformat, or reorganize files you aren't otherwise changing.
  The test suite (`tests/Automation.Tests/`) and `anti-check.sh` both grep for
  exact strings — gratuitous rewrites will break them loudly.
- When adding a new step to the flow, insert it at the seam in
  `PollOrchestrator`; don't rewrite the surrounding pipeline to accommodate
  it. The `Actions/` and `Claude/` boundaries are deliberate.
- Ledger keys (`"<repo>#<n>"`, `"<repo>!pr<n>"`) are persisted on disk.
  Don't change them without a migration story.
- Before deleting anything that looks dead, confirm it isn't referenced by the
  unattended loop — silent regressions are the worst failure mode when nobody
  is watching.
- Prefer additive config (a new field in `AppConfig`, defaulting to off) over
  changing existing behavior.

### 4. Goal-Driven Execution

- The goal: the user spends time *reviewing* PRs and draft replies, not
  writing code. Every change is judged against that outcome, not internal
  elegance.
- Every automated action must be reviewable: draft replies land in `drafts/`
  (never posted), branches are pushed but PRs are **draft-only** and never
  merged, and the ledger advances *only* after success (so failures retry, not
  silently skip).
- Dry-run must not advance the ledger — on either verdict, or for PR comment
  watermarks. The `--dry-run` path in `PollOrchestrator` explicitly short-
  circuits `ledger.SetIssue` / `ledger.SetPr`. Don't regress this; the
  sandbox #2 smoke test burned us already (see the comment at
  `PollOrchestrator.cs:116`).
- If a change makes the loop faster but harder to audit, it's the wrong
  trade. Structured JSON-line logs and saved drafts are features, not debt.
- Stop when the task is done. No "while I'm here" scope creep.
- When a run misbehaves, fix the root cause in the step that owns it. Don't
  paper over it in a later stage.

## Operational guardrails (enforced, not aspirational)

Because this runs unattended:

- **No force-push, no `gh pr merge`, no `gh pr edit --ready`, no push to
  `main`/`master`.** Enforced by `scripts/anti-check.sh`, which greps the
  `.cs` sources. `GitClient.PushAsync` is plain `git push -u`; there is no
  `--force` path and there must not be.
- **Draft PRs only.** `BranchAndPr.CreateDraftPrAsync` passes `--draft`.
- **Non-ready → draft file, never posted.** `DraftReply.Write` always writes
  to `drafts/`. The app has no code path that calls `gh issue comment`.
- **Idempotency via ledger.** `Ledger.IsIssueAlreadyHandled` keys off
  `updated_at`, so a re-edited issue re-enters the loop and an already-handled
  issue is skipped. Previously-pushed branches resume at the PR-create step
  (`BranchAndPr.RunAsync` `resume_from_origin` path) — the implementer is
  *not* re-run.
- **SSRF allowlist on attachments.** `IssueFetcher.IsAllowedUrl` only permits
  `*.githubusercontent.com`, `*.github.com`, and `github.com`. Size-capped at
  10 MB. Don't widen this without thinking hard.
- **Dry-run from day one.** `--dry-run` forces `jiggle_enabled = false`,
  short-circuits every write-side effect, and does not advance the ledger.
  New stages must honor this flag.
- **Argument safety.** `AppConfig.IsSafeRepoName` restricts `watched_repos`
  entries to `owner/name` with no path-traversal or control characters —
  these flow directly into subprocess `ArgumentList`. Don't bypass validation.
- **Structured logs per tick.** `JsonLineLogger` writes to
  `logs/automation-<date>.log` and echoes to stdout (captured by
  `logs/launchd.out.log`). Every new stage should emit an `info`/`warn`/
  `error` event — the logs are the audit trail.
- **Serialized ticks.** `Program.cs` uses `PeriodicTimer`, not
  `System.Threading.Timer`, so an unhandled exception can't crash the
  process silently. Keep it that way.

## Trust boundary (read this before adding a repo to `watched_repos`)

The implementer and PR-responder stages invoke `claude -p` with write-capable
tools (`Bash, Read, Write, Edit, Grep, Glob`) inside a repo checkout on your
laptop. The raw issue body and PR comments are copied directly into the
prompt. That means **anyone with permission to write an issue or PR comment
in a watched repo has effective code-execution on your Mac before any human
review**: a prompt-injection payload can tell `claude` to read `~/.ssh/`, pull
the `GITHUB_TOKEN` out of the environment, `curl` it to an attacker, or write
files anywhere your user account can write.

Mitigations that exist in code — partial, not sufficient alone:

- PRs are draft-only; merges stay manual.
- The implementer and PR-responder sessions each have a 30-min timeout
  (`Implementer.cs`, `PrResponder.cs`). The classifier has a 10-min timeout
  and read-only tools.
- URLs embedded in issue bodies are only downloaded from the GitHub host
  allowlist (`IssueFetcher.AllowedHostSuffixes`), size-capped at 10 MB.
- `watched_repos` entries are validated against `owner/name` shape before
  reaching subprocess args.
- `scripts/anti-check.sh` forbids force-push, `gh pr merge`,
  `gh pr edit --ready`, and literal pushes to `main`/`master` in the source
  tree.

Mitigations that do **not** exist and cannot be retrofitted generically:

- No sandbox around `claude -p`. Bash tool access is full user-level.
- No outbound network allowlist. A `curl` inside the implementer session can
  reach the open internet.
- No reliable way to "sanitize" a prompt-injection payload out of an issue
  body without also destroying legitimate requirements text.

Rule of thumb: **only watch repos whose issue-writer set you already trust
with shell access to your machine.** For anything else, run the loop with
`--dry-run` and review the classifier's summary and the implementer prompt
by hand.

## Layout

```
src/Automation/
  Program.cs                              # entry; wires timers + signals
  Config/AppConfig.cs                     # config.json loader + CLI overrides
  Logging/JsonLineLogger.cs               # JSON-line audit trail
  State/Ledger.cs                         # idempotency ledger (logs/ledger.json)
  GitHub/{GhCli,GitClient,IssueFetcher,PrFetcher,Models}.cs
  Claude/{ClaudeCli,Classifier,Implementer,PrResponder}.cs
  Actions/{DraftReply,BranchAndPr}.cs
  Loop/{JiggleTimer,PollOrchestrator}.cs
  Prompts/{classifier,implementer,pr_responder}.md   # loaded at startup
tests/Automation.Tests/                   # xunit
launchd/com.user.automation.plist         # REPO_ROOT interpolated at install
scripts/{run-automation,install-launchd,uninstall-launchd,anti-check}.sh
config/config.example.json                # template; real file is gitignored
drafts/                                   # gitignored — user's review inbox
logs/                                     # gitignored — audit trail + ledger
```

## When the repo grows

Re-read `demo.jpeg` and this file first. If the code diverges from the flow
described above, update this file in the same change — don't let it drift.
The five stages (fetch → classify → draft OR branch+PR → PR comment loop +
jiggle) are the contract; anything new should slot into them or be justified
as a new stage here.
