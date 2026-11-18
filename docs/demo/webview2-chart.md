# WebView2 chart sample (v1.2 / AW-057 + AW-058)

The v1.2 demonstration proves UI heterogeneity: a chart rendered by **local JavaScript inside a
WebView2 control**, hosted by its own process, while the rest of the workspace remains WPF. The app
is launched by the broker exactly like the WPF sample apps and receives the **same broker-approved
typed `fdc3.instrument` contexts** through a bounded .NET-host → JavaScript bridge.

## Process and trust boundary

```text
broker (authoritative policy + context catalog)
  → named-pipe gRPC session → Aether.SampleApps.WebView2.exe (independent PID)
      → WebView2 control (local chart.html/chart.js; no remote origin)
          → bridge-ack (ticker/trace/classification/point count) → WPF host status
```

- The host process is `sample.chart.webview2`; the WPF automation badge exposes its real PID, app id
  and transport.
- The host only forwards approved `InstrumentContext` payloads (the same `SampleRuntime`
  subscription used by the WPF samples). The bridge projection is strict and allowlists the three
  synthetic tickers `NRST`, `VEGA`, `ORIN`.
- JavaScript validates the message again before rendering and rejects unknown types, schema
  versions, tickers, malformed series and extra fields. It acknowledges successful renders through
  `chrome.webview.postMessage`; the WPF `WebView2BridgeStatus` automation surface exposes that ack.
- No remote scripts, CDNs, fetches or WebView2 host objects are used. The page has a restrictive CSP;
  the chart data is synthetic and local.

## Runtime prerequisite and fallback

WebView2 uses the **Microsoft Edge WebView2 Evergreen Runtime** (normally present on Windows 11).
The runtime is deliberately not bundled; the packaged bundle README documents the dependency and
the install link. If the runtime is missing, the independent host stays alive, keeps its broker
session and shows a readable fallback (`WebView2FallbackStatus`) instead of crashing; the rest of
the workspace is unaffected. The fallback path is covered by an automated Windows E2E test using
`AETHER_WEBVIEW2_FORCE_FALLBACK=1`.

## Bridge message (strict subset)

```json
{
  "type": "aether.webview2.instrument",
  "schemaVersion": "1.0",
  "ticker": "VEGA",
  "name": "Vega Energy 2029 Note",
  "classification": "Public",
  "traceId": "9a681503…",
  "series": [ { "month": "2026-01", "value": 4.1 } ]
}
```

Unknown JSON fields are rejected by the strict contract; the same bounded ticker allowlist is
enforced by both C# and JavaScript. No arbitrary scripts, HTML or host objects cross the bridge.

## Test coverage

- C# bridge contract unit tests: `tests/Aether.Fdc3.Tests/WebView2ContextBridgeTests.cs`.
- Windows named-pipe interop E2E: `tests/Aether.Desktop.E2E.Tests/WebView2ChartBridgeTests.cs`
  (separate PID, real broker registry, all three tickers, JS render ack).
- Fallback E2E: `tests/Aether.Desktop.E2E.Tests/WebView2FallbackTests.cs`.
- Package/CI guards: `tests/Aether.Contracts.Tests/WebView2PackagingContractTests.cs` and the
  Windows UI CI lane's Evergreen Runtime setup.
- FDC3 subset: [supported subset](../fdc3/supported-subset.md).

## Packaging

The Windows desktop bundle publishes the host under `samples\webview2\` and includes the
`WEBVIEW2_CHART_EXE` launch token in the broker registry. No default ActionPlan step launches the
extra app in v1.2 (the reference workflow still has six bounded actions); interop and physical
evidence use an explicit broker `LaunchApp`/typed-context scenario. This keeps the v1.1 demo plan and
budget unchanged while proving heterogeneous UI interop separately.
