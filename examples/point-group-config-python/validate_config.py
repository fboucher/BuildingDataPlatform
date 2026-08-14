"""Validate a BDP Point Group Configuration before uploading it.

A point group is commissioned by uploading this file in the portal; the platform then
returns an anomaly report. Every round trip through that loop costs you a day or more,
and the mistakes that cause one are almost all mechanical — a missing field, a data type
that is not one of the five allowed, an ExactType that is not a URI. Those can be caught
locally in a second.

Structural validation only. Confirming that ``Temperature_Sensor`` is a real Brick class
or ``DEG_C`` a real QUDT unit needs the full published vocabularies; this checks that
types sit in the right namespace and are well formed, which is where the errors that
cost a round trip actually live.

Each rule has a stable ``DIF6xx`` code so a finding can be referred to by code rather
than by quoting its message. The codes are this validator's own; they are not the
platform's, and a finding here does not carry the same code in an anomaly report.

A code ending ``N`` is the namespace advisory for the rule it follows: ``DIF617`` is
"ExactType is not a URI", ``DIF617N`` is "ExactType is a URI, but outside the accepted
namespaces".

Exit code is 0 when there are no errors and 1 when there are, so this drops into a
pre-upload check or a CI step unchanged. Warnings alone do not fail it.

Usage:
    python validate_config.py point_group_config.json
    python validate_config.py point_group_config.json --json
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

BRICK_NAMESPACE = "https://brickschema.org/schema/Brick#"
QUDT_NAMESPACE = "http://qudt.org/vocab/unit/"

#: The platform publishes its own equipment and point classes for things Brick does not
#: model - access control, several electrical measurements - and enumerates them in the
#: Ingress API specification. They are as valid as a Brick class.
SE_BRICK_EXTENSION_NAMESPACE = "https://schneider-electric.com/schema/BrickExtension#"

#: Applies to ExactType on equipment and on points. UnitExactType is QUDT only.
TYPE_NAMESPACES = (BRICK_NAMESPACE, SE_BRICK_EXTENSION_NAMESPACE)
TYPE_NAMESPACE_LABEL = "Brick or Schneider Electric Brick Extension"

VALID_DATA_TYPES = ("Boolean", "Integer", "Double", "String", "DateTimeOffset")

#: Anything else is reported at INFO rather than as an error: an unrecognized field is
#: usually a typo, but the platform may accept fields this validator has not been told
#: about, and refusing them outright would be worse than mentioning them.
ALLOWED_TOP_LEVEL = {
    "Version", "Description", "ProtocolVersion", "SourceId",
    "PointGroupReferenceId", "Equipments",
}

ERROR, WARNING, INFO = "error", "warning", "info"


class Report:
    def __init__(self) -> None:
        self.issues: list[dict] = []

    def add(self, severity: str, code: str, where: str, message: str) -> None:
        self.issues.append(
            {"severity": severity, "code": code, "where": where, "message": message}
        )

    @property
    def errors(self) -> list[dict]:
        return [i for i in self.issues if i["severity"] == ERROR]


def _require(report: Report, obj: dict, key: str, code: str, where: str) -> bool:
    value = obj.get(key)
    if value is None:
        report.add(ERROR, code, where, f"{key} is required")
        return False
    if isinstance(value, str) and value.strip().startswith("<") \
            and value.strip().endswith(">"):
        # The documented workflow is copy-the-template-then-edit, so a field still
        # holding its own placeholder is the likeliest mistake in the flow this tool
        # exists to short-circuit - and the only one it can always be sure about.
        report.add(ERROR, code, where,
                   f"{key} still holds the template placeholder {value!r}")
        return False
    if not isinstance(value, str):
        report.add(ERROR, code, where,
                   f"{key} must be a string (got {type(value).__name__})")
        return False
    if not value.strip():
        report.add(ERROR, code, where, f"{key} must not be empty")
        return False
    return True


def _require_uri(report: Report, obj: dict, key: str, code: str, uri_code: str,
                 where: str, namespaces: tuple[str, ...],
                 namespace_label: str) -> None:
    if not _require(report, obj, key, code, where):
        return
    value = obj[key]
    if not value.startswith(("http://", "https://")):
        report.add(ERROR, uri_code, where,
                   f"{key} must be a URI starting with http:// or https:// "
                   f"(got {value!r})")
        return
    if not value.startswith(namespaces):
        report.add(WARNING, f"{uri_code}N", where,
                   f"{key} is outside the {namespace_label} namespace "
                   f"({', '.join(namespaces)}); the platform may not resolve it")


def validate(config) -> Report:
    report = Report()

    if not isinstance(config, dict):
        report.add(ERROR, "DIF600", "$", "Root must be a JSON object")
        return report

    if "ProtocolVersion" in config and config["ProtocolVersion"] != "1.0":
        report.add(ERROR, "DIF601", "$",
                   f'ProtocolVersion must be "1.0" (got {config["ProtocolVersion"]!r})')

    _require(report, config, "SourceId", "DIF602", "$")
    _require(report, config, "PointGroupReferenceId", "DIF603", "$")

    for key in config:
        if key not in ALLOWED_TOP_LEVEL:
            report.add(INFO, "DIF606", "$",
                       f"Unexpected top-level field {key!r}")

    equipments = config.get("Equipments")
    if not isinstance(equipments, list):
        report.add(ERROR, "DIF604", "$", "Equipments must be an array")
        return report
    if not equipments:
        report.add(ERROR, "DIF605", "$",
                   "Equipments must contain at least one equipment")
        return report

    # ReferenceIds share one namespace across equipment and points, and must be
    # unique per Data Source — a duplicate silently rebinds telemetry to the wrong
    # point, which is far worse than a rejected upload.
    seen: dict[str, str] = {}

    for index, equipment in enumerate(equipments):
        where = f"Equipments[{index}]"
        if not isinstance(equipment, dict):
            report.add(ERROR, "DIF610", where, "must be a JSON object")
            continue

        for key, code in (("ReferenceId", "DIF611"), ("VendorId", "DIF612"),
                          ("ProductType", "DIF613"), ("Name", "DIF614"),
                          ("LocatedInReferenceId", "DIF615")):
            _require(report, equipment, key, code, where)

        _require_uri(report, equipment, "ExactType", "DIF616", "DIF617", where,
                     TYPE_NAMESPACES, TYPE_NAMESPACE_LABEL)

        monitors = equipment.get("MonitorsReferenceId")
        if monitors is not None and not isinstance(monitors, str):
            report.add(ERROR, "DIF618", where,
                       "MonitorsReferenceId must be a string or null")

        reference = equipment.get("ReferenceId")
        if isinstance(reference, str) and reference:
            if reference in seen:
                report.add(ERROR, "DIF619", where,
                           f"Duplicate ReferenceId {reference!r}, already used by "
                           f"{seen[reference]}")
            seen[reference] = where

        points = equipment.get("Points")
        if not isinstance(points, list):
            report.add(ERROR, "DIF620", where, "Points must be an array")
            continue
        if not points:
            report.add(WARNING, "DIF621", where,
                       "Points is empty; this equipment contributes no telemetry")
            continue

        for point_index, point in enumerate(points):
            point_where = f"{where}.Points[{point_index}]"
            if not isinstance(point, dict):
                report.add(ERROR, "DIF630", point_where, "must be a JSON object")
                continue

            _require(report, point, "ReferenceId", "DIF631", point_where)
            _require(report, point, "Name", "DIF632", point_where)
            _require_uri(report, point, "ExactType", "DIF633", "DIF634", point_where,
                         TYPE_NAMESPACES, TYPE_NAMESPACE_LABEL)

            if _require(report, point, "DataType", "DIF635", point_where):
                if point["DataType"] not in VALID_DATA_TYPES:
                    report.add(ERROR, "DIF636", point_where,
                               f"DataType {point['DataType']!r} is not valid; must be "
                               f"one of {', '.join(VALID_DATA_TYPES)}")

            _require_uri(report, point, "UnitExactType", "DIF637", "DIF638",
                         point_where, (QUDT_NAMESPACE,), "QUDT")

            if "IsWritable" in point and not isinstance(point["IsWritable"], bool):
                report.add(ERROR, "DIF639", point_where,
                           "IsWritable must be a boolean")

            reference = point.get("ReferenceId")
            if isinstance(reference, str) and reference:
                if reference in seen:
                    report.add(ERROR, "DIF640", point_where,
                               f"Duplicate ReferenceId {reference!r}, already used by "
                               f"{seen[reference]}")
                seen[reference] = point_where

    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("config", type=Path)
    parser.add_argument("--json", action="store_true", dest="as_json",
                        help="emit the issue list as JSON")
    args = parser.parse_args(argv)

    if not args.config.exists():
        print(f"config not found: {args.config}", file=sys.stderr)
        return 2

    try:
        # utf-8-sig: a config saved by a Windows editor carries a BOM that json
        # rejects with an error that reads like the file is corrupt.
        config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as exc:
        print(f"{args.config.name} is not valid JSON: line {exc.lineno} "
              f"column {exc.colno}: {exc.msg}", file=sys.stderr)
        return 1

    report = validate(config)

    if args.as_json:
        print(json.dumps(report.issues, indent=2))
        return 1 if report.errors else 0

    if not report.issues:
        equipment_count = len(config.get("Equipments") or [])
        points = sum(len(e.get("Points") or [])
                     for e in config.get("Equipments") or []
                     if isinstance(e, dict))
        print(f"{args.config.name}: valid — {equipment_count} equipment, {points} point(s)")
        return 0

    order = {ERROR: 0, WARNING: 1, INFO: 2}
    for issue in sorted(report.issues, key=lambda i: order[i["severity"]]):
        print(f"  {issue['severity'].upper():<8}{issue['code']:<8}"
              f"{issue['where']}: {issue['message']}")

    errors = len(report.errors)
    print(f"\n{len(report.issues)} issue(s), {errors} error(s)")
    if errors:
        print("Upload will produce anomalies. Fix the errors first.")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
