#!/usr/bin/env python3
"""Aggregate coverlet Cobertura reports into a unique-source-line coverage summary.

Only files under ``src/`` are counted; test projects, tooling, generated code and
Windows-only lanes are excluded. Multiple test projects covering the same source file are
merged per (file, line): a line counts as covered if any suite hit it.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = "{http://coverage.cobertura.xml}"


def local_name(tag: str) -> str:
    return tag.split("}", 1)[1] if "}" in tag else tag


def summarize(results: Path) -> dict:
    files: dict[str, dict[int, bool]] = {}
    reports = sorted(results.rglob("coverage.cobertura.xml"))
    for report in reports:
        root = ET.parse(report).getroot()
        for package in root.iter():
            if local_name(package.tag) != "package":
                continue
            for cls in package.iter():
                if local_name(cls.tag) != "class":
                    continue
                filename = (cls.get("filename") or "").replace("\\", "/")
                normalized = filename.lower()
                if "/src/" not in ("/" + normalized) or "/tests/" in normalized or "/tools/" in normalized:
                    continue
                source = files.setdefault(filename, {})
                for line in cls.iter():
                    if local_name(line.tag) != "line":
                        continue
                    number = int(line.get("number", "0"))
                    hits = int(float(line.get("hits", "0")))
                    source[number] = source.get(number, False) or hits > 0

    covered = sum(1 for lines in files.values() for hit in lines.values() if hit)
    valid = sum(len(lines) for lines in files.values())
    percent = round((covered / valid) * 100, 1) if valid else 0.0
    return {
        "reports": len(reports),
        "sourceFiles": len(files),
        "linesValid": valid,
        "linesCovered": covered,
        "lineCoveragePercent": percent,
    }


def badge_svg(percent: float) -> str:
    if percent >= 90:
        color = "#2EA043"
    elif percent >= 80:
        color = "#3FB950"
    elif percent >= 70:
        color = "#7EE787"
    elif percent >= 60:
        color = "#D29922"
    else:
        color = "#DA3633"

    label = "coverage"
    value = f"{percent:g}%"
    # Approximate 6.2px per character for the 11px bold label/value with padding.
    label_width = 10 + int(len(label) * 6.3)
    value_width = 14 + int(len(value) * 6.6)
    width = label_width + value_width
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="20" role="img" aria-label="{label}: {value}">
  <title>{label}: {value}</title>
  <linearGradient id="s" x2="0" y2="100%">
    <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
    <stop offset="1" stop-opacity=".1"/>
  </linearGradient>
  <clipPath id="r"><rect width="{width}" height="20" rx="3" fill="#fff"/></clipPath>
  <g clip-path="url(#r)">
    <rect width="{label_width}" height="20" fill="#0B2237"/>
    <rect x="{label_width}" width="{value_width}" height="20" fill="{color}"/>
    <rect width="{width}" height="20" fill="url(#s)"/>
  </g>
  <g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,DejaVu Sans,sans-serif" font-size="11">
    <text x="{label_width / 2:.1f}" y="14" fill="#9FB0C0">{label}</text>
    <text x="{label_width + value_width / 2:.1f}" y="14" font-weight="bold">{value}</text>
  </g>
</svg>
'''


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", required=True, type=Path, help="Directory with coverage.cobertura.xml files")
    parser.add_argument("--badge", type=Path, help="Write a coverage badge SVG to this path")
    parser.add_argument("--json", type=Path, help="Write the summary JSON to this path")
    arguments = parser.parse_args()

    summary = summarize(arguments.results)
    if summary["reports"] == 0:
        print("No coverage.cobertura.xml reports found.", file=sys.stderr)
        return 1

    if arguments.badge:
        arguments.badge.parent.mkdir(parents=True, exist_ok=True)
        arguments.badge.write_text(badge_svg(summary["lineCoveragePercent"]), encoding="utf-8")

    payload = json.dumps(summary, indent=2)
    if arguments.json:
        arguments.json.parent.mkdir(parents=True, exist_ok=True)
        arguments.json.write_text(payload + "\n", encoding="utf-8")

    print(payload)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
