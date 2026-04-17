#!/usr/bin/env bash
set -euo pipefail

UNIT_NAME="automation.service"
TARGET_UNIT="$HOME/.config/systemd/user/$UNIT_NAME"

# Uninstall is intentionally lenient: if systemctl isn't on PATH (moved
# machines, non-systemd environment), still clean up the file.
if command -v systemctl >/dev/null 2>&1; then
  systemctl --user disable --now "$UNIT_NAME" 2>/dev/null || true
  rm -f "$TARGET_UNIT"
  systemctl --user daemon-reload
else
  echo "WARN: systemctl not found; removing unit file only." >&2
  rm -f "$TARGET_UNIT"
fi
echo "Removed $TARGET_UNIT"
