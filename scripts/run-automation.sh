#!/usr/bin/env bash
# Entry point for launchd. Keeps the long-lived loop alive.
# launchd's KeepAlive + our internal timers handle the cadence; this script
# only has to start the process and let it run.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$HERE"

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"

# If there's a local .env, source it for GITHUB_TOKEN / CLAUDE_BIN overrides.
if [[ -f "$HERE/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$HERE/.env"
  set +a
fi

exec "$DOTNET_ROOT/dotnet" run --project "$HERE/src/Automation/Automation.csproj" -c Release
