# ADR-012 — Electron adapter proof-of-concept: NO-GO for the v1.x line

**Status:** Accepted (2026-11-18), v1.5 extension evaluation AW-063.
**Context:** The v1.5 candidate asked whether an optional Electron app should participate in the same
typed-context channel. The project already proves heterogeneous UI interoperability with an
independent WebView2 chart host (v1.2): separate process, broker-launched, typed context through a
strict .NET→JavaScript bridge, Windows E2E on CI. A duplicate Electron PoC would have to add a
Chromium/Node runtime, main↔renderer IPC, preload security boundaries and a second JavaScript bridge
for no new product capability.

**Measured footprint (2026-11-18):**

| Artefact | Size |
|---|---|
| Electron v45.3.4 Windows x64 release ZIP (upstream GitHub asset `electron-v45.3.4-win32-x64.zip`) | 158,262,038 bytes (~151 MiB) |
| WPF sample-app build output (`src/Aether.SampleApps/bin/Release/net10.0-windows`) | ≈1.9 MiB |
| WebView2 chart host build output (`src/Aether.SampleApps.WebView2/bin/Release/net10.0-windows`) | ≈4.0 MiB |
| Published Windows desktop bundle (v1.1.0 release asset, reference) | ≈3.6 MiB |

The Electron runtime alone is roughly two orders of magnitude larger than the complete Windows
desktop bundle. The WebView2 host already uses the system Evergreen runtime and still adds only
~2 MiB over the WPF samples. No Electron PoC was built because the measured/structural case is
decisive and a PoC would add a new attack surface and packaging/update burden for zero incremental
interop evidence.

**IPC complexity comparison:** Electron would require broker gRPC from the Node main process plus a
renderer bridge and preload/context-isolation configuration; the existing WebView2 host uses one
typed .NET host→JS bridge and the same broker session as other apps. The v1.2 Windows E2E already
demonstrates separate-process typed context sync; duplicating it in Electron is not value-positive.

**Decision:** **NO-GO** for the v1.x line. No Electron dependency, prototype executable, packaging
or release-blocking change is added. The recorded decision and measurement method live under
`prototypes/electron/README.md`.

**Consequences / re-evaluation:** Reconsider only with an explicit product requirement not met by
WPF/WebView2 (for example a required third-party Electron app integration), an approved maintenance
and security review, and a measured PoC. Until then, no Electron claim or dependency is implied.

**Evidence:** v1.2 WebView2 E2E (`tests/Aether.Desktop.E2E.Tests/WebView2ChartBridgeTests.cs`, CI
green), bundle sizes above, issue #91.
