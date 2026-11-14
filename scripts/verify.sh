#!/usr/bin/env bash
# Full local verification: Release build (warnings-as-errors), test suite and demo.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/dotnet-env.sh"
cd "$ROOT"

echo "== build (Release, warnings are errors) =="
dotnet build AetherWorkspace.slnx -c Release -v q --nologo

echo "== tests (unit, contract, integration, E2E) =="
dotnet test AetherWorkspace.slnx -c Release -v q --nologo

echo "== reference workflow demo =="
SKIP_BUILD=1 ./scripts/demo.sh

echo "All verification steps completed."
