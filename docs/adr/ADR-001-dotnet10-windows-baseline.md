# ADR-001: .NET 10 LTS, C# 14 and a Windows-first desktop baseline

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

The reference workflow targets three independent Windows desktop processes, local IPC
and an AI-assisted shell. The specification pins .NET 10 LTS, C# 14 and WPF for the
Windows UI, while the development/CI environment for this baseline is Linux.

## Decision

- Target .NET 10 / C# 14 with `global.json` pinned to SDK 10.0.100 (roll forward to the
  latest 10.0 feature band) and central package versions in `Directory.Packages.props`.
- Windows UI projects (`Aether.Shell`, `Aether.SampleApps`) target `net10.0-windows`
  with WPF. All other libraries, the broker, workflow and headless hosts target
  `net10.0` so they can be exercised on Linux.
- Enable `EnableWindowsTargeting` and `TreatWarningsAsErrors` so Windows-targeted code is
  compile-verified even on non-Windows agents.
- Provide a headless host and headless sample processes as the deterministic CI/E2E
  substitute; Windows runtime verification runs in the Windows CI job.

## Consequences

Positive: one language/toolchain, strong SDK/package pinning, cross-platform testability,
and the full solution builds warning-free on Linux. Negative: WPF runtime behaviour and
named pipes cannot be validated in this Linux environment; the Windows CI job is the
evidence source. The Windows desktop runtime is a deployment prerequisite.

## Alternatives

Targeting .NET Framework (rejected: not LTS, no modern gRPC/hosting), MAUI/Avalonia
(rejected: specification targets Windows 11 WPF), or splitting the repository per OS
(rejected: unnecessary; cross-platform core is the point).

## Evidence

`global.json`, `Directory.Build.props`, `AetherWorkspace.slnx`, `src/Aether.Shell`,
`src/Aether.SampleApps`, `.github/workflows/ci.yml`.
