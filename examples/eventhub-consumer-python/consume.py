"""Consume BDP telemetry from the Streaming API (Azure Event Hub).

A first runnable check that your credentials work and telemetry is flowing. It does the
things a five-line snippet leaves out, each of which otherwise turns into an hour of
guessing:

* **It stops.** ``max_wait_time`` plus an event ceiling means the script exits on its
  own. A bare ``client.receive()`` blocks forever, which is fine in a service and
  useless as a smoke test.
* **It reports errors.** Without ``on_error`` a bad connection string looks identical
  to a quiet stream: nothing happens and nothing is printed.
* **It needs one thing.** The connection string BDP issues, and nothing else. The hub
  name travels inside it as ``EntityPath``, so there is no second value to find and no
  second place to get it wrong.

Every run replays from the start of the retained stream. That is the right default for
a check you run once, and the wrong one for a service — see the README.

Usage:
    pip install -r requirements.txt
    set BDP_EVENTHUB_CONNECTION_STRING=Endpoint=sb://...
    python consume.py
    python consume.py --count 20 --timeout 60 --from-latest
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

from azure.eventhub import EventHubConsumerClient, TransportType

CONNECTION_ENV = "BDP_EVENTHUB_CONNECTION_STRING"


def load_env_file(path: Path) -> None:
    """Load KEY=VALUE pairs from a local .env file if present.

    Existing environment variables win; this only fills missing values.
    """
    if not path.exists():
        return

    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue

        key, value = line.split("=", 1)
        key = key.strip()
        if not key or key in os.environ:
            continue

        value = value.strip()
        if (value.startswith('"') and value.endswith('"')) or (
            value.startswith("'") and value.endswith("'")
        ):
            value = value[1:-1]

        os.environ[key] = value


def main(argv: list[str] | None = None) -> int:
    load_env_file(Path(__file__).with_name(".env"))

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--count", type=int, default=10,
                        help="stop after this many events (default 10)")
    parser.add_argument("--timeout", type=int, default=30,
                        help="stop after this many seconds without an event")
    parser.add_argument("--consumer-group", default="$Default",
                        help="use a dedicated group if another app reads this hub")
    parser.add_argument("--from-latest", action="store_true",
                        help="only new events; default replays the retained stream")
    parser.add_argument("--raw", action="store_true",
                        help="print the raw body instead of a summary")
    parser.add_argument("--transport", choices=("amqp", "websockets"), default="amqp",
                        help="amqp uses port 5671; websockets tunnels AMQP over 443")
    args = parser.parse_args(argv)

    connection_string = os.environ.get(CONNECTION_ENV)
    if not connection_string:
        print(f"set {CONNECTION_ENV} to the primary connection string from\n"
              "  BDP Portal > Partners > {your partner} > {your consumer} > "
              "Event Hubs\n"
              "The Event Hub belongs to the consumer, not to the partner.",
              file=sys.stderr)
        return 2

    client_args = {
        "conn_str": connection_string,
        "consumer_group": args.consumer_group,
    }

    if args.transport == "websockets":
        # The SDK needs websocket-client for this transport. If it is missing it does
        # not raise — it swallows the ImportError inside its load-balancing loop and
        # retries every ~30 seconds forever, so the script simply hangs with no output.
        # Checking here converts a silent hang into one line telling you what to install.
        try:
            import websocket  # noqa: F401
        except ImportError:
            print("the websockets transport needs an extra package:\n"
                  "  pip install websocket-client\n"
                  "Without it the Azure SDK retries silently instead of failing, and "
                  "this script appears to hang.", file=sys.stderr)
            return 2
        client_args["transport_type"] = TransportType.AmqpOverWebsocket

    client = EventHubConsumerClient.from_connection_string(**client_args)

    seen = 0

    def on_event(partition_context, event):
        nonlocal seen
        # close() is asynchronous, and events already in flight on other partitions
        # still arrive after it. Without this guard the script prints "stopping" and
        # then keeps printing events, which reads like the ceiling does not work.
        if seen >= args.count:
            return
        # A None event is how the SDK reports "max_wait_time elapsed, still nothing".
        if event is None:
            print(f"no events for {args.timeout}s on partition "
                  f"{partition_context.partition_id}")
            client.close()
            return

        seen += 1
        body = event.body_as_str()
        if args.raw:
            print(body)
        else:
            print(f"[{seen:>3}] partition={partition_context.partition_id} "
                  f"offset={event.offset} enqueued={event.enqueued_time} "
                  f"{summarize(body)}")

        if seen >= args.count:
            print(f"\nreceived {seen} event(s); stopping")
            client.close()

    def on_error(partition_context, error):
        partition = getattr(partition_context, "partition_id", "unknown")
        print(f"error on partition {partition}: {error}", file=sys.stderr)

    started = datetime.now(timezone.utc)
    port = 443 if args.transport == "websockets" else 5671
    print(f"connecting at {started.isoformat(timespec='seconds')}, "
          f"consumer group {args.consumer_group}, "
          f"{args.transport} (port {port}), "
          f"{'latest only' if args.from_latest else 'replaying retained stream'}")

    with client:
        client.receive(
            on_event=on_event,
            on_error=on_error,
            starting_position="@latest" if args.from_latest else "-1",
            max_wait_time=args.timeout,
        )

    return 0 if seen else 1


def summarize(body: str) -> str:
    """One readable line per telemetry message, whatever shape it arrives in."""
    try:
        payload = json.loads(body)
    except (TypeError, ValueError):
        return body[:120]
    if isinstance(payload, dict):
        keys = ", ".join(list(payload)[:6])
        return f"{{{keys}}}"
    if isinstance(payload, list):
        return f"[{len(payload)} item(s)]"
    return str(payload)[:120]


if __name__ == "__main__":
    raise SystemExit(main())
