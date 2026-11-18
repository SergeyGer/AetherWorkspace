# Architecture decision records

| ADR | Decision | Status |
|---|---|---|
| [ADR-001](ADR-001-dotnet10-windows-baseline.md) | .NET 10 LTS + C# 14, Windows 11 reference desktop, cross-platform core | Accepted |
| [ADR-002](ADR-002-grpc-over-local-ipc.md) | gRPC/HTTP2 over Windows named pipes; Unix sockets for Linux dev/CI | Accepted |
| [ADR-003](ADR-003-separate-planning-and-execution.md) | Untrusted AI planning separated from deterministic execution | Accepted (mandatory) |
| [ADR-004](ADR-004-fail-closed-data-policy.md) | Fail-closed classification and explicit-consent cloud egress | Accepted (mandatory) |
| [ADR-005](ADR-005-top-level-window-arrangement.md) | MVP uses top-level arrangement; no reparenting/docking | Accepted; docking not evaluated |
| [ADR-006](ADR-006-bounded-fdc3-subset.md) | Declared bounded FDC3 3.0 alignment, no conformance claim | Accepted |
| [ADR-007](ADR-007-local-only-no-telemetry.md) | No network listener and no telemetry egress by default | Accepted (mandatory) |
| [ADR-008](ADR-008-reference-workflow-action-budget.md) | Reference workflow configures 6 actions; library default stays 5 | Accepted |
| [ADR-009](ADR-009-public-only-cloud-policy.md) | v1.x cloud routing is PUBLIC-only; no INTERNAL/RESTRICTED cloud | Accepted |
| [ADR-010](ADR-010-template-based-contract-studio.md) | Template-based contract studio uses reviewed drafts/apply, no schema writes | Accepted |
| [ADR-011](ADR-011-reject-sanitized-internal-cloud-routing.md) | Reject sanitized INTERNAL cloud routing for v1.x; hard-disable the latent policy branch | Accepted |

Format: Status, Date, Context, Decision, Consequences, Alternatives, Evidence.
