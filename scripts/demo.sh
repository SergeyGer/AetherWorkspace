#!/usr/bin/env bash
# End-to-end reference workflow demo (synthetic data only, local IPC only).
# Requires: .NET 10 SDK. On Linux this exercises the headless sample processes
# and Unix domain socket transport; on Windows it can additionally use named pipes.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/dotnet-env.sh"
CONFIG="${CONFIG:-Release}"
RUN_DIR="$ROOT/.aether/demo"
ENDPOINT_FILE="$RUN_DIR/broker-endpoint.json"
BROKER_AUDIT="$RUN_DIR/broker-audit.jsonl"
SHELL_AUDIT="$RUN_DIR/shell-audit.jsonl"
CHILD_LOGS="$RUN_DIR/child-logs"
RESULT="$RUN_DIR/result.json"

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== building ($CONFIG) =="
  dotnet build "$ROOT/src/Aether.BrokerApp/Aether.BrokerApp.csproj" -c "$CONFIG" -v q --nologo
  dotnet build "$ROOT/src/Aether.SampleApps.Headless/Aether.SampleApps.Headless.csproj" -c "$CONFIG" -v q --nologo
  dotnet build "$ROOT/src/Aether.Shell.Headless/Aether.Shell.Headless.csproj" -c "$CONFIG" -v q --nologo
fi

rm -rf "$RUN_DIR"
mkdir -p "$RUN_DIR"

DOTNET_BIN="$(command -v dotnet)"
SAMPLES_DLL="$ROOT/src/Aether.SampleApps.Headless/bin/$CONFIG/net10.0/Aether.SampleApps.Headless.dll"
BROKER_DLL="$ROOT/src/Aether.BrokerApp/bin/$CONFIG/net10.0/Aether.BrokerApp.dll"
SHELL_DLL="$ROOT/src/Aether.Shell.Headless/bin/$CONFIG/net10.0/Aether.Shell.Headless.dll"
[ -f "$SAMPLES_DLL" ] || { echo "missing $SAMPLES_DLL"; exit 1; }
[ -f "$BROKER_DLL" ] || { echo "missing $BROKER_DLL"; exit 1; }
[ -f "$SHELL_DLL" ] || { echo "missing $SHELL_DLL"; exit 1; }

BROKER_PID=""
cleanup() {
  if [ -n "$BROKER_PID" ] && kill -0 "$BROKER_PID" 2>/dev/null; then
    kill -TERM "$BROKER_PID" 2>/dev/null || true
    wait "$BROKER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "== starting local broker =="
"$DOTNET_BIN" "$BROKER_DLL" \
  --registry "$ROOT/samples/registry/apps.json" \
  --contexts "$ROOT/samples/contexts/contexts.json" \
  --token "DOTNET=$DOTNET_BIN" \
  --token "SAMPLES_DLL=$SAMPLES_DLL" \
  --token "AETHER_HOME=$ROOT" \
  --token "FIXTURES_DIR=$ROOT/samples/fixtures" \
  --allowed-root "$(dirname "$DOTNET_BIN")" \
  --allowed-root "$ROOT" \
  --endpoint-file "$ENDPOINT_FILE" \
  --audit-file "$BROKER_AUDIT" \
  --child-log-dir "$CHILD_LOGS" \
  --max-actions 6 \
  > "$RUN_DIR/broker.out" 2> "$RUN_DIR/broker.err" &
BROKER_PID=$!

for _ in $(seq 1 200); do
  [ -f "$ENDPOINT_FILE" ] && break
  if ! kill -0 "$BROKER_PID" 2>/dev/null; then
    echo "broker exited unexpectedly:"; cat "$RUN_DIR/broker.err" >&2; exit 1
  fi
  sleep 0.05
done
[ -f "$ENDPOINT_FILE" ] || { echo "broker did not become ready"; cat "$RUN_DIR/broker.err" >&2; exit 1; }
BROKER_ENDPOINT_DESCRIPTION=$(python3 - "$ENDPOINT_FILE" <<'PY'
import json, sys
info = json.load(open(sys.argv[1]))
print(f"{info['transport']} {info['endpoint']}")
PY
)
echo "broker endpoint: $BROKER_ENDPOINT_DESCRIPTION"

REFERENCE_COMMAND="Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately."

echo "== running reference workflow (classification RESTRICTED, cloud disabled) =="
set +e
"$DOTNET_BIN" "$SHELL_DLL" \
  --endpoint-file "$ENDPOINT_FILE" \
  --classification RESTRICTED \
  --command "$REFERENCE_COMMAND" \
  --audit-file "$SHELL_AUDIT" \
  --output-file "$RESULT" \
  --max-actions 6 \
  --text
SHELL_EXIT=$?
set -e

echo
echo "== evidence =="
python3 - "$RESULT" <<'PY'
import json, sys
result = json.load(open(sys.argv[1]))
print(f"trace:          {result['traceId']}")
print(f"success:        {result['success']} ({result['status']})")
print(f"plan:           {result['planId']} planner={result['planner']} usedModel={result['usedModel']}")
print(f"steps:          " + ", ".join(f"{s['action']}:{s['state']}" for s in result['steps']))
publish = next((s for s in result['steps'] if s['action'] == 'PublishContext' and s.get('artifacts')), None)
if publish:
    print(f"delivered:      {publish['artifacts'].get('deliveredCount')} subscriber(s), dropped={publish['artifacts'].get('droppedCount')}")
arrange = next((s for s in result['steps'] if s['action'] == 'ArrangeWindows' and s.get('artifacts')), None)
if arrange:
    layout = json.loads(arrange['artifacts']['layoutJson'])
    print(f"layout:         {layout['layout']} -> " + ", ".join(f"{p['appId']}@({p['bounds']['x']},{p['bounds']['y']},{p['bounds']['width']}x{p['bounds']['height']})" for p in layout['placements']))
if result.get('summary'):
    print(f"summary:        {result['summary']['text']}")
    print(f"summary route:  {result['summary']['provider']} / {result['summary']['classification']}")
print(f"egress:         attempts={result['egress']['attempts']} external={result['egress']['externalAttempts']} denied={result['egress']['denied']}")
print(f"audit:          entries={result['audit']['entryCount']} chainValid={result['audit']['chainValid']}")
assert result['success'] is True, "reference workflow must succeed"
assert result['egress']['externalAttempts'] == 0, "no external AI calls are permitted for RESTRICTED data"
assert result['audit']['chainValid'] is True, "audit chain must verify"
PY

echo
echo "== context deliveries captured from sample apps =="
if [ -d "$CHILD_LOGS" ]; then
  CONTEXT_COUNT=$(grep -h '"event":"context"' "$CHILD_LOGS"/*.log 2>/dev/null | wc -l | tr -d ' ')
  APP_COUNT=$(grep -l '"event":"context"' "$CHILD_LOGS"/*.log 2>/dev/null | wc -l | tr -d ' ')
  echo "context events: $CONTEXT_COUNT across $APP_COUNT app log(s)"
  TRACE_ID=$(python3 -c "import json;print(json.load(open('$RESULT'))['traceId'])")
  MATCHING=$(grep -h '"event":"context"' "$CHILD_LOGS"/*.log 2>/dev/null | grep -c "\"traceId\":\"$TRACE_ID\"" || true)
  echo "events carrying workflow traceId: $MATCHING"
  [ "$MATCHING" -ge 2 ] || { echo "expected at least two apps to receive the context with the workflow trace id" >&2; exit 1; }
fi

echo
echo "audit files:    $BROKER_AUDIT / $SHELL_AUDIT"
echo "child logs:     $CHILD_LOGS"
echo "result json:    $RESULT"
echo
echo "Demo finished (shell exit code $SHELL_EXIT)."
