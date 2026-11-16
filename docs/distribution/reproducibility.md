# Release verification and reproducibility (v1.0 AW-036)

Every `v*` tag triggers the Release workflow, which builds on the pinned .NET SDK and publishes:

| Asset | Purpose |
|---|---|
| `AetherWorkspace-<tag>-windows-x64-desktop.zip` | **Windows x64 WPF desktop bundle** (Shell, three WPF sample apps, broker, registry/contexts/fixtures, `Start-AetherWorkspace.ps1`, README) |
| `AetherWorkspace-<tag>-headless-binaries.zip` | headless portable Windows/Linux runtime (broker, headless shell and headless samples) |
| `AetherWorkspace-<tag>-source.zip` | tagged source snapshot |
| `manifest.spdx.json` | SPDX SBOM for the published artifacts |
| `SHA256SUMS-<tag>.txt` | SHA-256 checksums of all release assets |

## Verify downloaded assets (offline)

Download **all** assets listed on the release page (checksums reference every one of them), then run:

```powershell
$tag = 'v1.0.1'
$dir = "release/$tag"
New-Item -ItemType Directory -Force $dir | Out-Null
$base = "https://github.com/SergeyGer/AetherWorkspace/releases/download/$tag"
$files = @(
  "AetherWorkspace-$tag-windows-x64-desktop.zip",
  "AetherWorkspace-$tag-headless-binaries.zip",
  "AetherWorkspace-$tag-source.zip",
  'manifest.spdx.json',
  "SHA256SUMS-$tag.txt"
)
foreach ($file in $files) { Invoke-WebRequest "$base/$file" -OutFile (Join-Path $dir $file) }

pwsh tools/release/Verify-Release.ps1 -AssetDirectory $dir
# expected: "VERIFY PASS"
```

`gh release download <tag> --dir <dir>` is an equivalent convenience when GitHub CLI is installed.
The script validates every checksum line, missing assets and the SPDX SBOM structure.

For a quick check of a subset only, add `-AllowPartialDownload`: missing checksum-listed files are
reported as `SKIP` and the result is `PARTIAL VERIFY PASS`. This is **not** a full release
verification — the release is verified only when every listed asset is present.

On Linux/macOS use `sha256sum -c SHA256SUMS-<tag>.txt` after renaming the sums file, plus the same
script via `pwsh`.

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
