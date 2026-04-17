#!/usr/bin/env bash
# Install the launchd agent for the automation loop.
# Loads com.user.automation at ~/Library/LaunchAgents/ with the repo root
# interpolated into the plist.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_PLIST="$HERE/launchd/com.user.automation.plist"
TARGET_DIR="$HOME/Library/LaunchAgents"
TARGET_PLIST="$TARGET_DIR/com.user.automation.plist"
LABEL="com.user.automation"

if [[ ! -f "$SOURCE_PLIST" ]]; then
  echo "ERROR: source plist not found: $SOURCE_PLIST" >&2
  exit 1
fi
if [[ ! -f "$HERE/config/config.json" ]]; then
  echo "ERROR: config/config.json missing. Copy config/config.example.json and edit it." >&2
  exit 1
fi

chmod +x "$HERE/scripts/run-automation.sh"
mkdir -p "$TARGET_DIR"

# Interpolate {{REPO_ROOT}} into the plist we install.
sed "s|{{REPO_ROOT}}|$HERE|g" "$SOURCE_PLIST" > "$TARGET_PLIST"

# Replace an existing load cleanly.
if launchctl list "$LABEL" &>/dev/null; then
  launchctl unload "$TARGET_PLIST" 2>/dev/null || true
fi
launchctl load "$TARGET_PLIST"

echo "Installed $LABEL"
echo "Plist:  $TARGET_PLIST"
echo "Logs:   $HERE/logs/launchd.{out,err}.log and $HERE/logs/automation-*.log"
echo "Unload: ./scripts/uninstall-launchd.sh"
