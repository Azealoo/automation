# automation

Personal GitHub-issue → PR loop modeled on the workflow in `demo.jpeg`
(r/ClaudeAI, "I automated most of my job"), with one swap: GitHub instead of
GitLab. Governance and guardrails live in [`CLAUDE.md`](./CLAUDE.md) — read it
before touching this.

## What it does

A long-lived .NET console app runs two timers inside one process:

- **15-min poll** — `gh search`es issues assigned to you across `watched_repos`,
  classifies each through `claude -p` (with the repo checked out and any image
  attachments attached), then either (a) writes a draft reply to `drafts/` for
  your review, or (b) creates a branch, runs a second `claude -p` to implement
  the work, pushes the branch, and opens a **draft** PR.
- **1-min jiggle** — nudges the mouse 1 px and back so Teams/Slack doesn't mark
  you idle and the screen doesn't sleep.

Nothing is auto-posted, auto-merged, or force-pushed. You review every change.

## Prereqs

- macOS (the jiggler uses CoreGraphics P/Invoke)
- `.NET 10 SDK` — `brew install dotnet`
- `gh` CLI — `brew install gh`, then `gh auth login`
- `claude` on PATH
- `git`

## Setup

```bash
git clone <this-repo> automation && cd automation
cp config/config.example.json config/config.json
$EDITOR config/config.json    # set watched_repos[]
gh auth status                # must be clean before live runs
```

## Run

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"

# One iteration, no writes, no network posts, jiggler off.
dotnet run --project src/Automation -- --dry-run --once

# One live iteration.
dotnet run --project src/Automation -- --once

# Full unattended loop (foreground).
dotnet run --project src/Automation
```

### Install as a launchd agent (recommended for unattended use)

```bash
./scripts/install-launchd.sh
tail -f logs/automation-*.log       # watch the JSON-line audit trail
./scripts/uninstall-launchd.sh      # to stop
```

The agent runs at login, restarts if it crashes (`KeepAlive=true`), and writes
stdout/stderr to `logs/launchd.*.log`.

## Flags

| Flag | Effect |
|------|--------|
| `--dry-run` | Classifier runs; draft replies, git pushes, and PR creation are logged but not performed. Forces jiggler off. |
| `--no-jiggle` | Disable the mouse-jiggler timer for this run only. |
| `--once` | Run a single poll iteration and exit. |

## Layout

```
src/Automation/
  Program.cs                      # entry; wires timers + signals
  Config/AppConfig.cs             # config.json loader + CLI overrides
  Logging/JsonLineLogger.cs       # JSON-line audit trail
  State/Ledger.cs                 # idempotency ledger
  GitHub/{GhCli,GitClient,IssueFetcher,PrFetcher,Models}.cs
  Claude/{ClaudeCli,Classifier,Implementer,PrResponder}.cs
  Actions/{DraftReply,BranchAndPr}.cs
  Loop/{JiggleTimer,PollOrchestrator}.cs
  Prompts/{classifier,implementer,pr_responder}.md
tests/Automation.Tests/           # xunit, 25+ tests
launchd/com.user.automation.plist # launchd template (REPO_ROOT interpolated at install)
scripts/{run-automation,install-launchd,uninstall-launchd,anti-check}.sh
config/config.example.json        # template; real file is gitignored
```

## Guardrails (enforced, not aspirational)

- No `git push --force` anywhere. `scripts/anti-check.sh` greps for it.
- No `gh pr merge` anywhere. Ditto.
- Draft PRs only.
- Non-ready issues → draft `.md` in `drafts/`, never posted.
- `Ledger` keyed by `updated_at` prevents double-processing.
- `--dry-run` short-circuits every write-side effect from day one.

## Test

```bash
dotnet test            # 25 tests, all green on main
./scripts/anti-check.sh
```
