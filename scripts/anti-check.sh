#!/usr/bin/env bash
# Grep-based anti-checks. CLAUDE.md §Operational guardrails: never force-push,
# never merge from the loop. Run before commit; fail loudly if either appears.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
fail=0

# Ignore this script itself and the README which documents what's banned.
if grep -RIn --include='*.cs' -e 'git.*push.*--force' -e 'push.*-f' "$HERE/src" "$HERE/tests" >&2; then
  echo "ANTI-CHECK FAILED: force-push reference detected in sources" >&2
  fail=1
fi

if grep -RIn --include='*.cs' -e 'gh.*pr.*merge' "$HERE/src" "$HERE/tests" >&2; then
  echo "ANTI-CHECK FAILED: gh pr merge reference detected in sources" >&2
  fail=1
fi

if [[ $fail -eq 0 ]]; then
  echo "anti-check: OK (no force-push or pr merge in code)"
fi
exit $fail
