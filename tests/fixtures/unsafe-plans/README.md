# Unsafe plan negative corpus

`unsafe-plans.json` contains 100 documented, synthetic unsafe plans used to prove
that the deterministic executor denies malformed or unauthorized plans **before
any side effect**. The corpus is generated reproducibly by
`scripts/generate_unsafe_plans.py` (do not edit the JSON by hand).

Categories:

| Category | Count | Expected first denial code |
|---|---|---|
| unknown action | 10 | `plan.action.unknown` |
| unknown application | 10 | `plan.app.unknown` |
| path-traversal context reference | 10 | `plan.unsafe_content` |
| URL-like context reference | 10 | `plan.unsafe_content` |
| over action budget | 10 | `plan.steps.too_many` |
| unsupported schema major | 10 | `plan.schema.version_unsupported` |
| cloud summary mode requested by plan | 10 | `plan.mode.unsupported` |
| unknown trusted context | 10 | `plan.context.unknown` |
| unsupported layout | 5 | `plan.layout.unsupported` |
| invalid status text | 5 | `plan.status.invalid` |
| duplicate app launch | 5 | `plan.app.duplicate_launch` |
| forbidden JSON field (strict parse) | 5 | `plan.invalid_json` |

All entries are pure planning artifacts; none can execute because validation
rejects them first. The corpus contains no real financial or personal data.
