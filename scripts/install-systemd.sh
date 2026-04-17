#!/usr/bin/env bash
# Install the systemd user service for the automation loop on Ubuntu.
# Writes ~/.config/systemd/user/automation.service with REPO_ROOT
# interpolated, then enables it. Idempotent.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_UNIT="$HERE/systemd/automation.service"
TARGET_DIR="$HOME/.config/systemd/user"
TARGET_UNIT="$TARGET_DIR/automation.service"
UNIT_NAME="automation.service"

if [[ ! -f "$SOURCE_UNIT" ]]; then
  echo "ERROR: source unit not found: $SOURCE_UNIT" >&2
  exit 1
fi
if [[ ! -f "$HERE/config/config.json" ]]; then
  echo "ERROR: config/config.json missing. Copy config/config.example.json and edit it." >&2
  exit 1
fi
if ! command -v systemctl >/dev/null 2>&1; then
  echo "ERROR: systemctl not found. This installer targets Ubuntu with systemd." >&2
  exit 1
fi

chmod +x "$HERE/scripts/run-automation-linux.sh"

# systemd's StandardOutput=append: won't create the directory for us.
# logs/ holds dotnet + claude stderr; keep it owner-only since it can echo
# env state on crash.
mkdir -p "$HERE/logs"
chmod 0700 "$HERE/logs"
mkdir -p "$TARGET_DIR"

# Interpolate {{REPO_ROOT}} into the unit we install, then lock it to 0600
# so only the user can read the absolute path of this checkout.
sed "s|{{REPO_ROOT}}|$HERE|g" "$SOURCE_UNIT" > "$TARGET_UNIT"
chmod 0600 "$TARGET_UNIT"

# Pick up file changes on a re-install, then enable+start.
systemctl --user daemon-reload
systemctl --user enable --now "$UNIT_NAME"

echo "Installed $UNIT_NAME"
echo "Unit:   $TARGET_UNIT"
echo "Logs:   $HERE/logs/systemd.{out,err}.log and $HERE/logs/automation-*.log"
echo "Status: systemctl --user status $UNIT_NAME"
echo "Follow: journalctl --user -u $UNIT_NAME -f"
echo "Remove: ./scripts/uninstall-systemd.sh"
echo
echo "For the loop to keep running across logouts / before first login, run once:"
echo "  sudo loginctl enable-linger \$USER"
