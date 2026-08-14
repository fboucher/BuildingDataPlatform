"""Send a telemetry message to BDP over the Data Integration Framework ingress.

For connector/provider developers. Generates a SAS token from a Data Source connection
string, builds a protocol-conformant message, and posts it to the IoT Hub endpoint.

**The thing to understand before anything else:** a 2xx from this endpoint confirms that
IoT Hub received the message. Ingestion happens downstream of that. If the SourceId,
PointGroupReferenceId or PointReferenceId do not match a commissioned point group
authorized for the site, the message is discarded downstream and the response is
unchanged. So a successful POST is necessary and not by itself sufficient, and this
script says so on every run rather than printing a reassuring "sent".

That is also why it validates the payload locally first. Only the protocol-level
mistakes can be caught here — a missing required field, a NotificationType the protocol
does not define, a Value of a type the protocol does not allow. Whether an identifier
exists, and whether the value suits the point it names, cannot be checked from the
sending side at all. Catching the ones that can be caught is worth doing precisely
because the platform will not report the rest.

Usage:
    set BDP_DIF_CONNECTION_STRING=HostName=...;DeviceId=...;SharedAccessKey=...
    python send_telemetry.py --source-id <uuid> --point-group <id> --point <ref> --value 42
    python send_telemetry.py ... --dry-run      # print the message, send nothing
"""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import hmac
import json
import math
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

CONNECTION_ENV = "BDP_DIF_CONNECTION_STRING"
API_VERSION = "2020-09-30"
PROTOCOL_VERSION = "1.0"
NOTIFICATION_TYPE = "TimeSeries"

#: The Ingress specification's published ceiling. Roughly ten time-series points fit.
MAX_MESSAGE_BYTES = 4096


def parse_connection_string(value: str) -> dict:
    try:
        parts = dict(item.split("=", 1) for item in value.split(";") if item)
    except ValueError as exc:
        raise SystemExit(
            "connection string is malformed; expected "
            "HostName=...;DeviceId=...;SharedAccessKey=..."
        ) from exc
    for key in ("HostName", "DeviceId", "SharedAccessKey"):
        if key not in parts:
            raise SystemExit(f"connection string is missing {key}")
    return parts


def generate_sas_token(parts: dict, expiry_seconds: int = 3600) -> str:
    """A SAS token for the device, valid for ``expiry_seconds``.

    The DeviceId is case sensitive here *and* in the URL, and the two must match
    exactly. A casing mismatch produces a 401 that looks like a bad key.
    """
    uri = f"{parts['HostName']}/devices/{parts['DeviceId']}"
    encoded_uri = urllib.parse.quote(uri, safe="")
    expiry = int(time.time()) + expiry_seconds

    try:
        key = base64.b64decode(parts["SharedAccessKey"], validate=True)
    except (binascii.Error, ValueError) as exc:
        # parse_connection_string turns a malformed shape into one line; without this
        # a truncated paste got a base64 traceback that names nothing the reader set.
        raise SystemExit(
            "SharedAccessKey in the connection string is not valid base64 — re-copy "
            "the Primary Connection String from the portal"
        ) from exc

    to_sign = f"{encoded_uri}\n{expiry}".encode("utf-8")
    signature = base64.b64encode(
        hmac.new(key, to_sign, hashlib.sha256).digest()
    ).decode("utf-8")

    return (
        "SharedAccessSignature "
        f"sr={encoded_uri}"
        f"&sig={urllib.parse.quote(signature, safe='')}"
        f"&se={expiry}"
    )


def build_message(source_id: str, point_group: str, readings: list[tuple[str, object]],
                  generated_at: str | None = None) -> dict:
    now = generated_at or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    return {
        "ProtocolVersion": PROTOCOL_VERSION,
        "SourceId": source_id,
        "PointGroupReferenceId": point_group,
        "NotificationType": NOTIFICATION_TYPE,
        "GeneratedAt": now,
        "Timeseries": [
            {"PointReferenceId": reference, "Timestamp": now, "Value": value}
            for reference, value in readings
        ],
    }


def validate(message: dict) -> list[str]:
    """Protocol-level problems, found before sending rather than never."""
    problems = []
    for field in ("ProtocolVersion", "SourceId", "PointGroupReferenceId",
                  "NotificationType", "GeneratedAt", "Timeseries"):
        if not message.get(field):
            problems.append(f"missing required field {field}")

    if message.get("NotificationType") not in (None, NOTIFICATION_TYPE):
        problems.append(f"NotificationType must be {NOTIFICATION_TYPE!r}")

    for index, reading in enumerate(message.get("Timeseries") or []):
        for field in ("PointReferenceId", "Timestamp", "Value"):
            if field not in reading:
                problems.append(f"Timeseries[{index}] missing {field}")
        value = reading.get("Value")
        if value is not None and not isinstance(value, (bool, int, float, str)):
            problems.append(
                f"Timeseries[{index}].Value is {type(value).__name__}; the protocol "
                "allows boolean, integer, number, string or an ISO 8601 date-time"
            )
        elif isinstance(value, float) and not math.isfinite(value):
            # json.dumps writes bare NaN and Infinity. Python reads them back, so a dry
            # run looks correct; a strict parser downstream rejects the document.
            problems.append(
                f"Timeseries[{index}].Value is {value}; JSON has no NaN or Infinity "
                "literal, so this message would not parse downstream"
            )

    size = len(json.dumps(message).encode("utf-8"))
    if size > MAX_MESSAGE_BYTES:
        problems.append(
            f"the message is {size} bytes, over the 4 KB limit; send fewer readings "
            "per message (about ten is the recommended shape)"
        )
    return problems


def coerce(raw: str):
    """Interpret a command-line value as the protocol's nearest allowed type."""
    lowered = raw.strip().lower()
    if lowered in ("true", "false"):
        return lowered == "true"
    for cast in (int, float):
        try:
            return cast(raw)
        except ValueError:
            continue
    return raw


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source-id", required=True,
                        help="SourceId from the BDP portal source details")
    parser.add_argument("--point-group", required=True,
                        help="PointGroupReferenceId, unique within the data source")
    parser.add_argument("--point", action="append", required=True,
                        help="PointReferenceId; repeatable, paired with --value")
    parser.add_argument("--value", action="append", required=True,
                        help="measurement value; repeatable, paired with --point")
    parser.add_argument("--expiry", type=int, default=3600,
                        help="SAS token lifetime in seconds (default 3600)")
    parser.add_argument("--dry-run", action="store_true",
                        help="print the message and exit without sending")
    args = parser.parse_args(argv)

    if len(args.point) != len(args.value):
        print(f"got {len(args.point)} --point and {len(args.value)} --value; "
              "they pair up one to one", file=sys.stderr)
        return 2

    readings = [(p, coerce(v)) for p, v in zip(args.point, args.value)]
    message = build_message(args.source_id, args.point_group, readings)

    problems = validate(message)
    if problems:
        print("message is not protocol-conformant:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    if args.dry_run:
        print(json.dumps(message, indent=2))
        return 0

    connection = os.environ.get(CONNECTION_ENV)
    if not connection:
        print(f"set {CONNECTION_ENV} from the BDP portal:\n"
              "  Data Sources > your company > your source > Connections >\n"
              "  Primary Connection String", file=sys.stderr)
        return 2

    parts = parse_connection_string(connection)
    token = generate_sas_token(parts, args.expiry)
    url = (f"https://{parts['HostName']}/devices/"
           f"{urllib.parse.quote(parts['DeviceId'], safe='')}"
           f"/messages/events?api-version={API_VERSION}")

    request = urllib.request.Request(
        url,
        data=json.dumps(message).encode("utf-8"),
        headers={"Authorization": token, "Content-Type": "application/json"},
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status = response.status
    except urllib.error.HTTPError as exc:
        body = exc.read().decode(errors="replace")[:300]
        print(f"HTTP {exc.code}: {body}", file=sys.stderr)
        if exc.code == 401:
            print("\n401 is almost always one of:\n"
                  f"  - DeviceId casing: URL and token both use {parts['DeviceId']!r}, "
                  "which must match the portal exactly\n"
                  "  - the SAS token expired (this one lasts "
                  f"{args.expiry}s)\n"
                  "  - the shared access key is not the one for this device",
                  file=sys.stderr)
        return 1
    except urllib.error.URLError as exc:
        print(f"could not reach {parts['HostName']}: {exc.reason}", file=sys.stderr)
        return 1

    print(f"HTTP {status} — IoT Hub accepted the message.")
    print(f"  device      {parts['DeviceId']}")
    print(f"  source      {args.source_id}")
    print(f"  point group {args.point_group}")
    print(f"  readings    {len(readings)}")
    print("\nThis confirms delivery, not ingestion. A message whose SourceId,\n"
          "PointGroupReferenceId or PointReferenceId does not match a commissioned\n"
          "point group authorized for the site is discarded downstream, and this\n"
          "response looks the same. Confirm the values in the portal when bringing a\n"
          "new connector up.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
