# automation

Greenfield project. The only artifact in the repo today is `demo.jpeg` — a
screenshot of a Reddit post (r/ClaudeAI, "I automated most of my job") that
serves as the design reference. No source code, no build system, no language
commitment has been made yet. Everything below must stay aligned with that
reference until the user says otherwise.

## Reference architecture (from demo.jpeg)

A small console app polls GitLab on a 15-minute loop and delegates the actual
work to `claude` CLI. High-level flow:

1. **Poll** — call GitLab API for issues assigned to the user.
2. **Classify** — for each found issue, start `claude` with the repo checked
   out and all image attachments + the issue description. The classifier
   decides: ready-for-dev, or not.
3. **Respond to non-ready issues** — save a draft answer to GitLab; the user
   manually reviews and posts it.
4. **Implement ready issues** — hand the issue (plus classifier summary) to an
   implementation subagent that does the work, pushes a branch, opens an MR,
   and leaves the review to the user.
5. **PR loop** — check existing MRs for the issue, pull down any new review
   comments, and implement them.
6. **Presence** — a mouse-jiggler side process runs every minute to keep
   Teams/laptop from going idle.

Runs unattended on a 15-minute cadence. The user reviews/merges everything;
the app never ships code autonomously.

## Working principles

These four principles govern every change. They exist because this project is
a personal automation: small surface, high blast radius (it pushes branches,
comments on issues, runs unattended). Cleverness is expensive here.

### 1. Think Before Coding

- Before writing code, state in one or two sentences: what problem this
  solves, and which part of the reference flow it belongs to (poll, classify,
  respond, implement, PR loop, presence).
- If the task doesn't map to the reference flow, stop and ask — don't invent
  a new subsystem.
- Prefer reading `demo.jpeg` and any existing code once more over guessing.
  The reference is the spec until there's a real one.
- Confirm the target language/runtime is already chosen before generating
  project scaffolding. The Reddit post uses .NET; the user may or may not
  follow that. Ask if unclear.

### 2. Simplicity First

- The reference explicitly calls the workflow "super simple." Match that tone.
  A console app + a loop + shelling out to `claude` is the baseline; beat it
  only with evidence.
- No frameworks, queues, databases, or web dashboards unless a concrete
  requirement forces one. In-memory state + GitLab as the source of truth is
  the default.
- Shell out to `claude` and `git` rather than reimplementing their behavior.
- One file / one module per responsibility (poll, classify, implement, PR,
  jiggler). Keep each under a few hundred lines. No premature abstractions,
  no plugin systems, no DI containers for a handful of call sites.
- Secrets (GitLab token, etc.) come from env vars or a local config file that
  is gitignored. Never hardcoded, never committed.

### 3. Surgical Changes

- Touch only what the task requires. A bug fix in the classifier does not
  pull in a refactor of the poller.
- Don't rename, reformat, or reorganize files you aren't otherwise changing.
- When adding a new step to the flow, insert it at the seam; don't rewrite
  the surrounding pipeline to accommodate it.
- Before deleting anything that looks dead, confirm it isn't referenced by
  the unattended loop — this app runs without a human watching, so silent
  regressions are the worst failure mode.
- Prefer additive config (a new flag, a new env var defaulting to off) over
  changing existing behavior when evolving the loop.

### 4. Goal-Driven Execution

- The goal is: "the user spends 2–3 hours a day reviewing instead of writing
  code." Every change is judged against that outcome, not against internal
  elegance.
- Every automated action must be reviewable: draft replies are saved, not
  posted; branches are pushed, MRs are opened, but nothing is merged; the
  user is always the final gate.
- If a change makes the loop faster but harder to audit, it's the wrong
  trade. Logs and draft artifacts are features, not debt.
- Stop when the task is done. Don't expand scope ("while I'm here, I'll also
  add…") unless the user asked for it.
- When a run misbehaves, fix the root cause in the step that owns it — don't
  paper over it in a later step.

## Operational guardrails

Because this runs unattended:

- **Never force-push, never push to protected branches, never merge MRs
  from the loop.** The app opens MRs; humans merge them.
- **Rate-limit and back off** on GitLab API calls. A runaway loop spamming
  the API is a realistic failure mode.
- **Idempotency**: re-running the loop on the same issue must not create
  duplicate branches, duplicate MRs, or duplicate comments. Key off issue ID
  and existing MR state.
- **Dry-run mode** should exist from day one — a flag that runs classify and
  prints what *would* be posted/pushed, without touching GitLab.
- **Structured logs** per loop iteration: which issues were seen, how each
  was classified, what action was taken. These are the audit trail.

## When the repo grows

Re-read `demo.jpeg` first, then this file. If the code diverges from the
reference flow, update this file in the same change — don't let it drift.
When a real language/runtime lands, add a short "Build & run" section above
this line and keep it to the actual commands, nothing aspirational.
