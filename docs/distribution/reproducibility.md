# Release verification and reproducibility (v1.0 AW-036)

Every `v*` tag triggers the Release workflow, which builds on the pinned .NET SDK and publishes:

| Asset | Purpose |
|---|---|
| `AetherWorkspace-<tag>-binaries.zip` | portable, unsigned, framework-dependent Windows/Linux binaries |
| `AetherWorkspace-<tag>-source.zip` | tagged source snapshot |
| `manifest.spdx.json` | SPDX SBOM for the published artifacts |
| `SHA256SUMS-<tag>.txt` | SHA-256 checksums of all release assets |

## Verify downloaded assets (offline)

```powershell
gh release download v0.6.0 --repo SergeyGer/AetherWorkspace --dir release/v0.6.0
pwsh tools/release/Verify-Release.ps1 -AssetDirectory release/v0.6.0
# expected: "VERIFY PASS"
```

The script validates every checksum line, missing assets and the SPDX SBOM structure. On Linux/macOS
use `sha256sum -c SHA256SUMS-<tag>.txt` after renaming the sums file, plus the same script via `pwsh`.

## Independent rebuild

```bash
git checkout v0.6.0
dotnet build AetherWorkspace.slnx -c Release
dotnet test  AetherWorkspace.slnx -c Release
```

The Windows UI and desktop lanes require Windows; portable suites run cross-platform. Build inputs are
the tagged commit, pinned SDK in `global.json`, and centrally managed package versions.

## Reproducibility boundaries (honest statement)

- Checksums and the SBOM identify the exact published artifacts; verification is independent of the
  release workflow.
- The distribution is **unsigned portable** (decision recorded for v1.0); Windows SmartScreen may warn
  and no code-signing identity is claimed.
- Bit-for-bit rebuild equivalence is **not claimed** for .NET compilation output (timestamps/PDB paths
  can vary); the verifiable contract is the tagged source + checksums + SBOM, with deterministic test
  evidence for functional behaviour.
- No telemetry uploads and no cloud transmission occur during verification.
