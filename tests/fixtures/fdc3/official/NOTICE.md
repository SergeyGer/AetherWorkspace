# Official FDC3 3.0 JSON Schemas (vendored)

These JSON Schema files are vendored **only as test fixtures** for the AW-024 external
interoperability increment. They are unmodified upstream FDC3 context schemas.

- Source: <https://github.com/finos/FDC3> (`packages/fdc3-context/schemas/context/`)
- Pinned commit: `c6adda7a1f66d97d03809343852a4bfc0806f859` (see `SOURCE_COMMIT.txt`)
- Files: `context.schema.json`, `instrument.schema.json`, `organization.schema.json`
- License: Apache License 2.0 (see `LICENSE` in this directory; upstream FDC3 project)
- Validation approach: `context.schema.json#/definitions/*` references are inlined at test
  time to keep validation offline and deterministic; payloads are validated with
  JsonSchema.Net (.NET tests) and Python `jsonschema` (`tools/fdc3-interop/validate_fdc3.py`).

Nothing here implies FDC3 conformance or endorsement by FINOS/OpenJS/any member firm.
