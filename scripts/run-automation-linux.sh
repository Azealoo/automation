#!/usr/bin/env bash
# Entry point for systemd --user on Ubuntu. Mirrors scripts/run-automation.sh
# (macOS) in shape: resolve dotnet, set PATH, source .env, exec.
#
# systemd's Restart=always + our internal timers handle the cadence; this
# script only starts the process and lets it run.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$HERE"

# PATH is sparse under systemd --user; make sure the user-installed tools
# (claude, gh, git, user-local dotnet) can be resolved.
export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"

# If there's a local .env, source it for GITHUB_TOKEN / CLAUDE_BIN /
# DOTNET_ROOT overrides. Same pattern as the macOS wrapper.
if [[ -f "$HERE/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$HERE/.env"
  set +a
fi

# Resolve dotnet across the common Ubuntu install locations. First hit wins.
# Order: explicit override -> Microsoft apt -> distro alt -> Snap -> HOME ->
# anything on PATH.
resolve_dotnet() {
  local candidates=()
  [[ -n "${DOTNET_ROOT:-}" ]] && candidates+=("$DOTNET_ROOT/dotnet")
  candidates+=(
    "/usr/share/dotnet/dotnet"
    "/usr/lib/dotnet/dotnet"
    "/snap/dotnet-sdk/current/dotnet"
    "$HOME/.dotnet/dotnet"
  )
  for c in "${candidates[@]}"; do
    if [[ -x "$c" ]]; then
      echo "$c"
      return 0
    fi
  done
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi
  echo "ERROR: could not locate dotnet. Install dotnet-sdk-10.0 (see README)." >&2
  return 1
}

# Keep error surfacing explicit under `set -e`: a bare assignment from
# command substitution can mask which branch failed.
if ! DOTNET_BIN="$(resolve_dotnet)"; then
  exit 1
fi
# If DOTNET_ROOT wasn't set by .env, derive it from the resolved binary so
# dotnet can find its shared framework.
if [[ -z "${DOTNET_ROOT:-}" ]]; then
  export DOTNET_ROOT="$(dirname "$DOTNET_BIN")"
fi

exec "$DOTNET_BIN" run --project "$HERE/src/Automation/Automation.csproj" -c Release
