#!/bin/bash
# Optional LaunchAgent, active only while this production stack is running.
set -euo pipefail
TASK_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export PATH="/usr/local/bin:/opt/homebrew/bin:$PATH"
if docker compose --env-file "$TASK_ROOT/.env" -f "$TASK_ROOT/deploy/macos/compose.yaml" ps --status running --services | /usr/bin/grep -qx web; then
    exec /usr/bin/caffeinate -s -t 65
fi
