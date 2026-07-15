#!/usr/bin/env bash
# PostToolUse hook: format the file Claude just edited.
# Claude Code passes the hook context as JSON on stdin; we pull out the edited
# file path and run the right formatter. Formatting failures never block the edit.
set -euo pipefail

input="$(cat)"

# Extract the edited file path (jq preferred, grep fallback if jq isn't installed).
if command -v jq >/dev/null 2>&1; then
  file="$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty')"
else
  file="$(printf '%s' "$input" \
    | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')"
fi

[ -z "${file:-}" ] && exit 0
[ -f "$file" ] || exit 0

case "$file" in
  *.cs)
    # Format just this file against the solution.
    dotnet format --include "$file" >/dev/null 2>&1 || true
    ;;
  *.ts|*.html|*.scss|*.css|*.json)
    npx prettier --write "$file" >/dev/null 2>&1 || true
    ;;
esac

exit 0
