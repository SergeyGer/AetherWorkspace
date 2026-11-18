# Electron adapter evaluation — NO-GO (documented decision)

v1.5 AW-063 asked whether an optional Electron app is worth adding for typed-context interoperability.
The decision is **NO-GO**; see [ADR-012](../../docs/adr/ADR-012-electron-adapter-no-go.md).

No Electron dependency or executable is added. The evaluation is based on measured footprint and
structural IPC complexity:

```text
Electron v45.3.4 Windows x64 release ZIP  ~151 MiB
WPF sample-app build output                ~1.9 MiB
WebView2 chart host build output           ~4.0 MiB
Windows desktop release bundle             ~3.6 MiB
```

The existing WebView2 host (v1.2) already proves independent-process typed-context synchronization on
Windows CI. A future PoC would need: broker gRPC from an Electron main process, preload/renderer
bridge, strict message validation, packaging/update strategy and a new security review. Reconsider
only with an explicit product requirement not served by WPF/WebView2.

Measurement method: upstream Electron release asset size from the public GitHub release API plus
local `du` of the published build folders. No performance or memory numbers are claimed without a
built PoC.
