AetherWorkspace — Windows x64 desktop reference bundle
=====================================================

WHAT THIS IS
  A runnable reference desktop implementation: WPF shell, three synthetic WPF sample
  applications (Watchlist, Risk, Chart) and the local Aether.BrokerApp. All data is
  synthetic; this is not production software.

PREREQUISITES
  - Windows 11 x64 (Windows 10 may work but is not the reference target).
  - .NET 10 Desktop Runtime (x64) for the framework-dependent build in this bundle:
    https://dotnet.microsoft.com/download/dotnet/10.0
  - A local interactive desktop session (no RDP/headless requirement beyond normal UI).
  - No network listener is opened; everything is local IPC over Windows named pipes.

QUICK START
  1. Unblock the ZIP before extracting if Windows marked it as downloaded:
       Unblock-File .\AetherWorkspace-*-windows-x64-desktop.zip
  2. Extract the archive to a local folder (avoid network shares).
  3. Start everything with:
       pwsh -NoProfile -ExecutionPolicy Bypass -File .\Start-AetherWorkspace.ps1
     The script starts the broker, waits for its handshake file, then opens the WPF shell.
  4. In the shell run the reference command (pre-filled): the broker launches the three
     sample windows, publishes the typed context, arranges windows and shows a private summary.
  5. Close the shell window and stop the broker process when finished.
6. If sample windows close unexpectedly or the workflow is denied, run the diagnostics collector:
     pwsh -NoProfile -ExecutionPolicy Bypass -File .\Collect-Diagnostics.ps1
   and share the produced ZIP. The endpoint handshake file (operator token) is excluded.
7. Headless reproduction without UI automation:
     pwsh -NoProfile -ExecutionPolicy Bypass -File .\Test-AetherWorkflow.ps1
   It starts the packaged broker, runs the reference workflow through the bundled headless shell,
   writes workflow result/diagnostics under .\diagnostics, and stops the broker afterwards.

WHAT GETS CREATED LOCALLY
  - endpoint.json   broker/operator handshake file (contains a local single-use token)
  - layouts/        durable workspace layout store (synthetic geometry only)
  - logs/           optional child-process stdout/stderr logs

SECURITY AND PRIVACY
  - No telemetry uploads and no cloud calls by default; cloud is denied unless explicitly
    enabled with consent and only for PUBLIC data.
  - The bundle is UNSIGNED: Windows SmartScreen may warn. Verify the SHA-256 checksum against
    SHA256SUMS published on the GitHub release page before running.
  - Synthetic data only. Do not place real customer, personal or market data in the samples.

VERIFY THE DOWNLOAD
  Download SHA256SUMS-<version>.txt, compute:
    Get-FileHash .\AetherWorkspace-<version>-windows-x64-desktop.zip -Algorithm SHA256
  and compare with the matching checksum line.
  For full verification (all release assets: desktop, headless, source, SPDX SBOM), download every
  asset listed on the release page into one directory and run:
    pwsh tools/release/Verify-Release.ps1 -AssetDirectory <dir-with-all-downloaded-assets>
  Partial single-asset check: add -AllowPartialDownload (NOT a full release verification).

KNOWN LIMITATIONS / NON-GOALS
  - Unsigned portable distribution; no installer and no code-signing claim.
  - Bit-for-bit rebuild equivalence is not claimed; verifiable contract is tagged source +
    checksums + SPDX SBOM.
  - Owned-window docking/reparenting is not planned; only top-level window arrangement.
  - FDC3 support is a declared bounded subset; no conformance claim.
