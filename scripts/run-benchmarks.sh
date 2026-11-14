#!/usr/bin/env bash
# Runs microbenchmarks and the measured IPC latency harness, storing raw output.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/dotnet-env.sh"
cd "$ROOT"
OUT="$ROOT/.aether/benchmarks"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "== microbenchmarks =="
dotnet run -c Release --project benchmarks/Aether.Benchmarks -- --filter "*" 2>&1 | tee "$OUT/microbenchmarks.txt"

echo "== IPC latency harness =="
dotnet run -c Release --project benchmarks/Aether.IpcBench -- --samples 1000 --warmup 100 --payload-bytes 1024 2>&1 | tee "$OUT/ipc-latency.txt"

echo "Raw outputs: $OUT"
echo "BenchmarkDotNet reports: $ROOT/BenchmarkDotNet.Artifacts/results"
