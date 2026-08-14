"""Make one authenticated REST API call to the BDP endpoint.

This script is a minimal transport check and first data read for consumers.
It supports three resources:

- organizations
- sites
- buildings (requires --site-id)

Usage:
    python call_rest_api.py --resource sites --take 5
    python call_rest_api.py --resource organizations
    python call_rest_api.py --resource buildings --site-id YOUR_SITE_GUID
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

TOKEN_ENV = "BDP_API_TOKEN"
UAT_BASE_URL = "https://ecostruxure-building-platform-api-uat.se.app"
RESOURCE_TO_PATH = {
    "organizations": "/api/Organizations",
    "sites": "/api/Sites",
    "buildings": "/api/Buildings",
}


def token_from_dotenv(dotenv_path: Path) -> str | None:
    """Return token from a dotenv file, or None when absent/unreadable."""
    try:
        with dotenv_path.open(encoding="utf-8") as handle:
            for raw_line in handle:
                line = raw_line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                name, value = line.split("=", 1)
                if name.strip() != TOKEN_ENV:
                    continue
                token = value.strip()
                if token.startswith(("\"", "'")) and token.endswith(("\"", "'")):
                    token = token[1:-1]
                return token or None
    except OSError:
        return None
    return None


def resolve_token() -> str | None:
    """Read token from env first, then fallback to .env in local/script directory."""
    token = os.environ.get(TOKEN_ENV)
    if token:
        return token

    script_dir_env = Path(__file__).with_name(".env")
    cwd_env = Path.cwd() / ".env"

    token = token_from_dotenv(script_dir_env)
    if token:
        return token

    # Support running from a directory different from this script's folder.
    if script_dir_env.resolve() != cwd_env.resolve():
        return token_from_dotenv(cwd_env)
    return None


def build_url(base_url: str, resource: str, site_id: str | None,
              take: int, skip: int) -> str:
    path = RESOURCE_TO_PATH[resource]
    query: dict[str, str] = {"take": str(take), "skip": str(skip)}

    if resource == "buildings":
        if not site_id:
            raise ValueError("--site-id is required when --resource buildings is used")
        query["siteId"] = site_id

    encoded = urllib.parse.urlencode(query)
    return f"{base_url.rstrip('/')}{path}?{encoded}"


def summarize_json(payload: object) -> None:
    """Print a compact, human-first summary of known response shapes."""
    if isinstance(payload, list):
        print(f"items: {len(payload)}")
        for item in payload[:10]:
            if isinstance(item, dict):
                item_id = item.get("id", "-")
                item_name = item.get("name", "-")
                print(f"  - {item_id} | {item_name}")
            else:
                print(f"  - {item}")
        return

    if isinstance(payload, dict):
        if "items" in payload and isinstance(payload["items"], list):
            items = payload["items"]
            print(f"items: {len(items)}")
            for item in items[:10]:
                if isinstance(item, dict):
                    item_id = item.get("id", "-")
                    item_name = item.get("name", "-")
                    print(f"  - {item_id} | {item_name}")
                else:
                    print(f"  - {item}")
            return

        print("top-level fields:", ", ".join(sorted(payload.keys())))
        return

    print(type(payload).__name__)


def request_json(url: str, token: str, api_version: str,
                 timeout: int) -> tuple[int, object]:
    request = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {token}",
            "X-Api-Version": api_version,
            "Accept": "application/json",
        },
        method="GET",
    )

    with urllib.request.urlopen(request, timeout=timeout) as response:
        status = response.status
        body = response.read().decode("utf-8", errors="replace")
        try:
            payload = json.loads(body) if body else {}
        except json.JSONDecodeError:
            payload = {"raw": body}
    return status, payload


def print_error(code: int, body: str) -> None:
    snippet = body[:400].strip().replace("\n", " ")
    if code == 401:
        print(
            "HTTP 401: token expired or malformed. Copy a fresh token from the portal.",
            file=sys.stderr,
        )
        return
    if code == 403 and "Application-Gateway" in body:
        print(
            "HTTP 403: refused before the API. The request did not reach the API.",
            file=sys.stderr,
        )
        return
    if code == 400 and "Unsupported API Version" in body:
        print(
            "HTTP 400: unsupported API version. Use 2.0 or 3.0.",
            file=sys.stderr,
        )
        return

    print(f"HTTP {code}: {snippet}", file=sys.stderr)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--resource",
        choices=sorted(RESOURCE_TO_PATH),
        default="sites",
        help="resource to request",
    )
    parser.add_argument("--site-id", help="required when --resource buildings")
    parser.add_argument("--take", type=int, default=5)
    parser.add_argument("--skip", type=int, default=0)
    parser.add_argument(
        "--base-url",
        default=UAT_BASE_URL,
        help="REST API host, no /api suffix needed",
    )
    parser.add_argument(
        "--api-version",
        default="3.0",
        choices=("2.0", "3.0"),
    )
    parser.add_argument("--timeout", type=int, default=30)
    parser.add_argument(
        "--raw",
        action="store_true",
        help="print full JSON response instead of a compact summary",
    )
    args = parser.parse_args(argv)

    if args.take < 0 or args.skip < 0:
        print("--take and --skip must be non-negative", file=sys.stderr)
        return 2

    token = resolve_token()
    if not token:
        print(
            f"set {TOKEN_ENV} in your environment or add it to a .env file in this folder",
            file=sys.stderr,
        )
        return 2

    try:
        url = build_url(args.base_url, args.resource, args.site_id, args.take, args.skip)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    print(f"GET {url}")

    try:
        status, payload = request_json(url, token, args.api_version, args.timeout)
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        print_error(exc.code, body)
        return 1
    except urllib.error.URLError as exc:
        print(f"network error: {exc.reason}", file=sys.stderr)
        return 1

    print(f"status: {status}")
    if args.raw:
        print(json.dumps(payload, indent=2, ensure_ascii=False))
    else:
        summarize_json(payload)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
