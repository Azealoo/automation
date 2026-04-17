#!/usr/bin/env bash
# Grep-based anti-checks. CLAUDE.md §Operational guardrails: never force-push,
# never merge from the loop, never push to the default branch. Run before
# commit; fail loudly if any of these guardrails is crossed in code.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
fail=0

check() {
  local label="$1" ; shift
  if grep -RIn --include='*.cs' "$@" "$HERE/src" "$HERE/tests" >&2; then
    echo "ANTI-CHECK FAILED: $label" >&2
    fail=1
  fi
}

# Force-push — no single-line form should survive in *.cs sources.
check "force-push flag in sources" -e 'push.*--force' -e 'push.*-f\b' -e '--force-with-lease'

# Merge from the loop.
check "gh pr merge in sources" -e 'gh.*pr.*merge' -e '"pr",\s*"merge"'

# Converting a draft PR to ready (would allow merge-queue automation).
check "gh pr edit --ready in sources" -e 'pr.*edit.*--ready' -e '--ready.*pr'

# Pushes that target the default branch literally.
check "push to default branch literal" -e 'push.*"main"' -e 'push.*"master"' -e 'origin/main.*push' -e 'origin/master.*push'

if [[ $fail -eq 0 ]]; then
  echo "anti-check: OK (no force-push, no pr merge, no --ready, no default-branch push)"
fi
exit $fail
