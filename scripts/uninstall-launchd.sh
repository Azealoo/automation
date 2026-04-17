#!/usr/bin/env bash
set -euo pipefail

TARGET_PLIST="$HOME/Library/LaunchAgents/com.user.automation.plist"
LABEL="com.user.automation"

if launchctl list "$LABEL" &>/dev/null; then
  launchctl unload "$TARGET_PLIST" 2>/dev/null || true
  echo "Unloaded $LABEL"
fi

rm -f "$TARGET_PLIST"
echo "Removed $TARGET_PLIST"
