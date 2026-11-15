#!/usr/bin/env bash
# OS-level restricted-egress evidence on Linux: runs the reference workflow (broker +
# shell + three sample processes) inside an isolated network namespace with no external
# connectivity. Local IPC (Unix domain sockets) must keep working; the workflow asserts
# zero external egress attempts and a valid audit chain.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/dotnet-env.sh"

if ! command -v unshare >/dev/null 2>&1; then
  echo "egress-blocked-check: SKIP (unshare not available)."
  exit 0
fi

if [ "$(id -u)" -eq 0 ]; then
  UN=(unshare -n)
else
  if ! sudo -n true 2>/dev/null; then
    echo "egress-blocked-check: SKIP (passwordless sudo not available for unshare)."
    exit 0
  fi
  UN=(sudo -n -E unshare -n)
fi

echo "== OS-level blocked-egress check: isolated network namespace =="
"${UN[@]}" -- bash -c '
  set -euo pipefail
  cd "'"$ROOT"'"
  ip link set lo up 2>/dev/null || true
  echo "network interfaces inside namespace:"; ip -brief addr 2>/dev/null || true
  # Any outbound TCP/DNS attempt must fail; local IPC must still work.
  if command -v curl >/dev/null 2>&1; then
    if curl -sS --max-time 5 https://api.nuget.org/v3/index.json >/dev/null 2>&1; then
      echo "ERROR: external connectivity unexpectedly available"; exit 1
    fi
    echo "external HTTPS unreachable as expected"
  fi
  SKIP_BUILD=1 ./scripts/demo.sh
'
echo "== blocked-egress workflow succeeded with zero external attempts =="
