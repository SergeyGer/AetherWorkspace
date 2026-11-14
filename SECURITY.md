# Security policy

## Supported versions

This repository is a pre-release reference implementation. Only the current `main`
baseline is maintained; there are no supported release versions yet.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately**. Do not create a public GitHub
issue, discussion or pull request that discloses an unpatched vulnerability.

Use GitHub's private vulnerability reporting for this repository
(Security → Report a vulnerability) if it is enabled, or contact the maintainers via the
private channel listed in the repository profile. Include:

- affected component/version (commit hash);
- reproduction steps or a minimal synthetic proof of concept;
- impact assessment (what trust boundary is crossed);
- any suggested fix or mitigation.

**Never include real customer data, credentials or provider keys in a report.** Use
synthetic examples. Acknowledged reports will be investigated and, where appropriate,
fixed with a regression test; credit can be given in the release notes if desired.

## Scope

In scope: the local broker IPC and session model, policy/classification/egress
enforcement, plan validation and execution, audit integrity, registry/launcher path
handling, sample apps and scripts, and dependency/supply-chain issues in pinned packages.

## Residual risks (by design)

The threat model documents known residual risks, including same-user process isolation
being a weak OS boundary, rule-based detector limits, local-admin audit tampering, and
the need for separate review of any real cloud provider terms. See
[`docs/security/threat-model.md`](docs/security/threat-model.md).

## Safe harbour

Good-faith security research that stays within this repository's synthetic fixtures and
your own machine is welcome. Do not test against third-party systems or data.
