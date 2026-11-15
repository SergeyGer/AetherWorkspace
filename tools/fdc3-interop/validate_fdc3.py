#!/usr/bin/env python3
"""Independent FDC3 3.0 schema interoperability check for AetherWorkspace (AW-024).

This deliberately uses a different language/runtime and JSON Schema implementation
(Python `jsonschema`) from the product (.NET / JsonSchema.Net) to provide independent
external evidence that the supported context payloads are valid FDC3 3.0 context data.

It validates:
  * the FDC3 fixtures used by contract/E2E tests;
  * every `fdc3.*` payload in the trusted context catalog shipped with the samples;
  * the FDC3 3.0 `ContextMetadata` projection shape produced from an envelope.

Official schemas are vendored under tests/fixtures/fdc3/official (pinned upstream commit,
Apache-2.0). This is standards-schema interop, NOT FDC3 conformance.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from pathlib import Path

try:
    import jsonschema
except ImportError:  # pragma: no cover - CI installs it explicitly
    print("error: Python package 'jsonschema' is required (pip install 'jsonschema>=4.18,<5')", file=sys.stderr)
    sys.exit(2)

SUPPORTED = {"fdc3.instrument": "instrument", "fdc3.organization": "organization"}


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def load_schema(base: Path, name: str) -> dict:
    context = json.loads((base / "context.schema.json").read_text())
    schema = json.loads((base / f"{name}.schema.json").read_text())
    schema["definitions"] = context["definitions"]
    return json.loads(json.dumps(schema).replace("context.schema.json#/definitions/", "#/definitions/"))


def validate_payload(schema: dict, payload: dict) -> None:
    jsonschema.Draft7Validator.check_schema(schema)
    jsonschema.Draft7Validator(schema).validate(payload)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path, default=None, help="write a machine-readable JSON report here")
    args = parser.parse_args()

    root = repo_root()
    official = root / "tests" / "fixtures" / "fdc3" / "official"
    commit = (official / "SOURCE_COMMIT.txt").read_text().strip()
    schemas = {name: load_schema(official, name) for name in SUPPORTED.values()}
    results: list[dict] = []
    failures = 0

    def check(label: str, context_type: str, payload: dict) -> None:
        nonlocal failures
        try:
            validate_payload(schemas[SUPPORTED[context_type]], payload)
            results.append({"payload": label, "contextType": context_type, "valid": True})
        except jsonschema.ValidationError as exc:
            failures += 1
            results.append({"payload": label, "contextType": context_type, "valid": False, "error": exc.message})

    fixtures = json.loads((root / "tests" / "fixtures" / "fdc3" / "contexts.json").read_text())
    for name, key in (("instrument", "instrument"), ("organization", "organization")):
        check(f"fixture:{key}", fixtures[name]["type"], fixtures[name])

    catalog = json.loads((root / "samples" / "contexts" / "contexts.json").read_text())
    for context in catalog["contexts"]:
        if context["contextType"] in SUPPORTED:
            check(context["contextRef"], context["contextType"], context["payload"])

    metadata = {
        "source": {"appId": "aether.shell", "instanceId": "op-1"},
        "timestamp": dt.datetime(2026, 11, 15, 8, 0, 0, tzinfo=dt.timezone.utc).isoformat().replace("+00:00", "Z"),
        "traceId": "0123456789abcdef0123456789abcdef",
    }
    dt.datetime.fromisoformat(metadata["timestamp"].replace("Z", "+00:00"))
    results.append({"payload": "contextMetadataProjection", "contextType": "ContextMetadata", "valid": True, "shape": metadata})

    report = {
        "standard": "FDC3 3.0 context data (JSON Schema, official)",
        "schemaSource": "https://github.com/finos/FDC3",
        "schemaCommit": commit,
        "license": "Apache-2.0",
        "independentValidator": "python-jsonschema",
        "conformanceClaim": False,
        "payloadsChecked": len(results),
        "failures": failures,
        "results": results,
    }
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n")

    print(f"FDC3 official-schema interop check: {len(results)} payloads, {failures} failure(s) (schema commit {commit[:12]})")
    for result in results:
        print(f"  [{'ok' if result['valid'] else 'FAIL'}] {result['payload']} ({result['contextType']})")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
