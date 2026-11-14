# Third-party notices

This reference implementation currently depends on the following NuGet packages (pinned
in `Directory.Packages.props`). Licenses are recorded for information; **a complete
license/notice audit and SBOM is a v1.0 release-gate requirement** (plan AW-036) and has
not been produced yet. No model packages or weights are bundled.

| Package | Version | License (upstream) |
|---|---|---|
| Google.Protobuf | 3.36.2 | BSD-3-Clause |
| Grpc.AspNetCore | 2.85.0 | Apache-2.0 |
| Grpc.Core.Api | 2.85.0 | Apache-2.0 |
| Grpc.Net.Client | 2.85.0 | Apache-2.0 |
| Grpc.Tools | 2.85.0 | Apache-2.0 |
| JsonSchema.Net | 9.7.0 | MIT |
| BenchmarkDotNet | 0.15.8 | MIT |
| Microsoft.NET.Test.Sdk | 18.12.0 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |
| coverlet.collector | 10.1.0 | MIT |

Transitive dependencies inherit their upstream licenses. The Windows desktop/WPF
targeting packs are provided by the .NET SDK. Verify exact license texts from the
upstream packages before any distribution; do not rely on this summary for legal advice.

## Fonts, assets and data

No third-party fonts, icon sets, market data or customer datasets are included.
Synthetic fixture data was authored for this repository and is covered by the project
license. Screenshots and demonstration recordings must use synthetic content only.

## Optional model services

Any future cloud or local model integration is optional and separately licensed by the
provider/model author. Model identifiers, licence metadata and hardware requirements
must be documented before enabling such a configuration; no terms are implied here.
