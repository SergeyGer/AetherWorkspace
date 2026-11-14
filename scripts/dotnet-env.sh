#!/usr/bin/env bash
# Locates a .NET 10 SDK. Uses the workspace-local SDK when the machine has none.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then
  return 0 2>/dev/null || exit 0
fi
if [ -x "$ROOT/.tools/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$ROOT/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ROOT/.tools/home}"
  export NUGET_PACKAGES="${NUGET_PACKAGES:-$ROOT/.tools/nuget}"
  export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  export DOTNET_NOLOGO=1
  return 0 2>/dev/null || exit 0
fi
echo "error: .NET 10 SDK not found. Install it or see README.md." >&2
exit 1
