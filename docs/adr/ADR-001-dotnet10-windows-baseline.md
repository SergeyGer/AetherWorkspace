# ADR-001: .NET 10 LTS, C# 14 and a WPF primary UI on cross-platform core libraries

- **Status:** Accepted
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers
- **Related:** [Architecture overview](../architecture/overview.md), [ADR-002](ADR-002-grpc-over-local-ipc.md)

## Context

The design baseline (`_source/Technical_Specification.md` §1.5) targets Windows 11 x64
with .NET 10 LTS, C# 14 and a WPF desktop UI, while this development and CI environment
is Linux. Contracts, policy, execution, the broker and the headless workflow must build
and test on Linux, yet the Windows shell, WPF samples and named-pipe path must still
compile. Unpinned SDK or language versions would make contract and test evidence
irreproducible.

## Decision

- Pin the SDK in `global.json` to `10.0.100` (`rollForward: latestFeature`,
  `allowPrerelease: false`); this tree resolved to SDK 10.0.404.
- Use C# 14 (`<LangVersion>14.0</LangVersion>`) with nullable reference types, implicit
  usings, warnings-as-errors and deterministic builds centrally.
- Target `net10.0` for every cross-platform library and host: Contracts, Security,
  Execution, AI, Broker, Broker.Client, Workflow, BrokerApp, Shell.Headless and
  SampleApps.Headless.
- Target `net10.0-windows` with `<UseWPF>true</UseWPF>` only for `Aether.Shell` and
  `Aether.SampleApps`.
- Set `<EnableWindowsTargeting>true</EnableWindowsTargeting>` in
  `Directory.Build.props` so Linux can **compile** Windows-targeted projects; this does
  not make them runnable on Linux.
- Keep one solution (`AetherWorkspace.slnx`), central package management and pinned
  package versions as the build contract.
- WPF is the primary reference UI; the demonstration chart uses WPF vector rendering
  and no WebView2 package is referenced.

## Consequences

### Positive

- One SDK/language baseline for Windows and Linux, with reproducible restore inputs.
- Linux can compile the WPF shell and samples, catching C#/XAML API breaks without a
  Windows machine, while the portable core is fully testable.
- `EnableWindowsTargeting` avoids a second solution or conditional project graph, so
  contracts and dependency direction are identical on both platforms.
- WPF needs no extra runtime or web engine for the deterministic chart.

### Negative

- Windows runtime behaviour (WPF rendering, named pipes, Win32 placement) cannot be
  validated in this Linux workspace; compile success is not runtime proof.
- `EnableWindowsTargeting` does not emulate Windows APIs; `net10.0-windows` projects
  cannot execute on Linux.
- WPF is Windows-only, so there is no cross-platform desktop UI; only core libraries and
  headless hosts are portable.
- Feature-band roll-forward can change tooling behaviour; the tested SDK version must be
  recorded with benchmark and release evidence.

## Alternatives considered

- **.NET 8 LTS** — stable but older than the specified baseline; rejected.
- **Avalonia or MAUI** — different UI stack, dependencies and design review; the
  reference workflow is Windows-first; rejected for this baseline.
- **Build Windows projects only on Windows** — loses local compile feedback and splits
  the solution/CI graph; `EnableWindowsTargeting` gives earlier breakage signal.
- **Target `net10.0-windows` everywhere** — would make contracts and policy Windows-only
  and remove the Linux headless CI substitute; rejected.
- **WebView2 chart** — adds a browser runtime/dependency without being required;
  rejected.

## Evidence

- `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
  `AetherWorkspace.slnx`.
- `src/Aether.Shell/Aether.Shell.csproj` and
  `src/Aether.SampleApps/Aether.SampleApps.csproj` (`net10.0-windows`, `UseWPF`); all
  other `src/*/*.csproj` target `net10.0`.
- Linux build/test evidence: `tests/` and `benchmarks/` run on the `net10.0` graph;
  `docs/BENCHMARKS.md` records the Linux environment.
- Windows projects are compile-verified in this tree; `.github/workflows/ci.yml` is the
  configured Windows runtime evidence path, not a result claim for this workspace.
- No WebView2 reference exists; the chart is WPF vector rendering
  (`src/Aether.SampleApps/ChartWindow.xaml`).
