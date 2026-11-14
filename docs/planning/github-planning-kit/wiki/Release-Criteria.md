# Release Criteria

All numbers below are **proposed acceptance targets**, not achieved benchmarks.

| Dimension | Proposed acceptance |
|---|---|
| End-to-end MVP | Three separate sample apps complete the instrument workflow using synthetic data. |
| Authorization | 100% of documented unsafe ActionPlan negative fixtures are denied before side effects. |
| Cloud egress | No external AI call observed for RESTRICTED/UNKNOWN, including errors and retries in the defined test corpus. |
| Local IPC | Measure p50/p95/p99 on pinned Windows reference system; candidate p95 ≤25 ms for 1 KB to 3 apps, after warmup. |
| Plan validity | Candidate ≥95% exact plan correctness over 100 versioned synthetic commands at v0.4. |
| Resilience | Sample-app crash cannot terminate Broker or Shell; bounded memory/queue behavior shown. |
| Packaging | Clean Windows checkout/build/test and published dependency/license record. |

## Evidence per release
Record tagged source, test logs, environment fingerprint, seed/fixture version, measured performance distributions, security negative test results and known limitations. If an objective is not met, link a blocking issue or explicitly revise scope; never present estimates as measurements.
