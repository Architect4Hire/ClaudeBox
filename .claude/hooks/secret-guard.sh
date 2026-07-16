#!/usr/bin/env bash
# PreToolUse guard: deny writes containing secret-shaped strings. exit 2 = deny.
set -euo pipefail
payload="$(cat)"
patterns='(sk-[A-Za-z0-9_-]{16,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|password[[:space:]]*=[[:space:]]*["'\''][^"'\'' ]{6,}|(postgres|redis)://[^:@/]+:[^@/]+@)'
if printf '%s' "$payload" | grep -Eiq "$patterns"; then
 echo "secret-guard: blocked a write with a secret-shaped string. Use Aspire-injected config, not literals." >&2
 exit 2
fi
exit 0