"""List what the BDP GraphQL API offers, by asking the API itself.

Introspection means this script embeds no copy of the schema, so it cannot go stale.

**Introspection alone does not prove your token is valid.** The schema is public: it is
served whether or not the token is good. So this checks the token separately, against the
REST API on the same host, and reports the two answers separately.

A gateway sits in front of the API and inspects requests before they reach it. A single
large introspection query — the kind a GraphQL IDE issues automatically on connect — is
refused there, which is why this asks in small pieces and reports honestly when a piece
is refused. For exploratory querying, use the **Try it** playground on the Schneider
Electric Exchange portal.

Distinguishing the failure modes matters when something goes wrong:

* HTTP 401 — the token is expired or malformed.
* HTTP 403 returning an HTML error page — refused before the API saw it. The request
  never arrived, and your token was never examined.
* HTTP 400 with a JSON ``errors`` array — the API answered. That is a real GraphQL
  error, and the message tells you what is wrong.

Usage:
    set BDP_API_TOKEN=eyJ...
    python introspect.py
    python introspect.py --type Site
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

UAT_ENDPOINT = "https://ecostruxure-building-platform-api-uat.se.app/graphql"
TOKEN_ENV = "BDP_API_TOKEN"

#: GraphQL names are letters, digits and underscores. The type name is interpolated into
#: a query, and while it comes from your own command line rather than from anywhere
#: untrusted, a name that cannot be a name is a typo worth catching before it becomes a
#: confusing server-side parse error.
GRAPHQL_NAME = re.compile(r"^[_A-Za-z][_0-9A-Za-z]*$")


class GatewayRefused(RuntimeError):
    """The gateway rejected the request before the API saw it."""


class GraphQLError(RuntimeError):
    """The API answered, and the answer was an error document."""


class TransportError(RuntimeError):
    """The endpoint could not be reached, or answered something unreadable."""


def _token_from_dotenv(dotenv_path: str) -> str | None:
    """Return the token from a dotenv file, or None when absent/unreadable."""
    try:
        with open(dotenv_path, encoding="utf-8") as handle:
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

    script_dir_env = os.path.join(os.path.dirname(__file__), ".env")
    cwd_env = os.path.join(os.getcwd(), ".env")

    token = _token_from_dotenv(script_dir_env)
    if token:
        return token

    # Support running the script from a different working directory.
    if os.path.normcase(cwd_env) != os.path.normcase(script_dir_env):
        return _token_from_dotenv(cwd_env)
    return None


def post(endpoint: str, token: str, query: str,
         variables: dict | None = None) -> dict:
    """Send one GraphQL document. Raises rather than exiting, so callers decide.

    ``variables`` keeps a parameterised query out of the document text. Nothing here
    needs it for safety - the only parameter comes from the caller's own argv - but this
    is the reference client, and a reader copying the transport should find the
    parameterised path already in it.
    """
    document = {"query": query}
    if variables is not None:
        document["variables"] = variables
    request = urllib.request.Request(
        endpoint,
        data=json.dumps(document).encode(),
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {token}",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = json.loads(response.read().decode())
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode(errors="replace")
        if "Application-Gateway" in raw:
            raise GatewayRefused(
                f"HTTP {exc.code} from the gateway — the request never reached the API"
            ) from exc
        try:
            payload = json.loads(raw)
        except ValueError:
            raise TransportError(f"HTTP {exc.code}: {raw[:300]}") from exc
        if payload.get("errors"):
            raise GraphQLError(
                "GraphQL errors:\n" + json.dumps(payload["errors"], indent=2)
            ) from exc
        raise TransportError(f"HTTP {exc.code}: {raw[:300]}") from exc
    except urllib.error.URLError as exc:
        raise TransportError(f"could not reach {endpoint}: {exc.reason}") from exc

    if payload.get("errors"):
        raise GraphQLError("GraphQL errors:\n" + json.dumps(payload["errors"], indent=2))
    return payload["data"]


def try_post(endpoint: str, token: str, query: str, label: str,
             variables: dict | None = None):
    """Run a query, reporting any failure on stderr and returning None.

    Catches every failure ``post`` raises. Catching only the gateway meant three of the
    four modes this function exists to contain escaped as a process exit.
    """
    try:
        return post(endpoint, token, query, variables)
    except (GatewayRefused, GraphQLError, TransportError) as exc:
        print(f"  ({label}: {exc})", file=sys.stderr)
        return None


def inspectable(types: list[dict]) -> list[str]:
    """Type names worth passing to ``--type``, sorted.

    Only object types have fields. Every GraphQL schema carries Boolean, Float, ID, Int
    and String, and none of them sorts far from the front, so suggesting the first name
    from an unfiltered list sends the reader to a command that prints two words.
    """
    return sorted(
        t["name"] for t in types
        if t.get("kind") == "OBJECT" and not t["name"].startswith("__")
    )


def check_token(endpoint: str, token: str) -> bool:
    """Say whether the token is accepted, using a REST call rather than introspection.

    The GraphQL schema is served without authentication, so a successful introspection
    proves nothing about the token. The REST API on the same host does enforce it and
    answers 401 when it is expired or malformed, which is the question you actually have.

    Anything other than 401 means the token was accepted. A 403 with a JSON body is a
    valid token that is not entitled to that data, which is a subscription-rule question
    and not a credentials one.
    """
    probe = endpoint.rsplit("/graphql", 1)[0] + "/api/Sites"
    request = urllib.request.Request(
        probe, headers={"Authorization": f"Bearer {token}", "X-Api-Version": "3.0"}
    )
    try:
        with urllib.request.urlopen(request, timeout=30):
            print("token accepted (REST /api/Sites answered 200)\n")
            return True
    except urllib.error.HTTPError as exc:
        if exc.code == 401:
            print(
                "token rejected: HTTP 401 from the REST API.\n"
                "  Copy a fresh one from BDP Portal > Credentials > System > API Token."
                "\n  Tokens are short-lived by design.",
                file=sys.stderr,
            )
            return False
        print(f"token accepted (REST /api/Sites answered {exc.code}, not 401)\n")
        return True
    except urllib.error.URLError as exc:
        print(f"could not verify the token: {exc.reason}\n", file=sys.stderr)
        return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--endpoint", default=UAT_ENDPOINT)
    parser.add_argument("--type", help="show one type's field names")
    parser.add_argument("--skip-token-check", action="store_true",
                        help="do not call the REST API to verify the token first")
    args = parser.parse_args(argv)

    token = resolve_token()
    if not token:
        print(
            f"set {TOKEN_ENV} in your environment or add it to a .env file "
            "in this folder",
              file=sys.stderr)
        return 2

    if args.type and not GRAPHQL_NAME.match(args.type):
        print(f"{args.type!r} is not a GraphQL type name", file=sys.stderr)
        return 2

    if not args.skip_token_check and not check_token(args.endpoint, token):
        return 1

    if args.type:
        data = try_post(
            args.endpoint, token,
            "query TypeDetail($name: String!) "
            "{ __type(name: $name) { name kind fields { name } } }",
            "type detail",
            variables={"name": args.type},
        )
        node = (data or {}).get("__type")
        if not node:
            print(f"no detail available for {args.type!r}", file=sys.stderr)
            return 1
        print(f"{node['kind']} {node['name']}")
        for field in node.get("fields") or []:
            print(f"  - {field['name']}")
        return 0

    data = try_post(args.endpoint, token,
                    "{ __schema { queryType { fields { name description } } } }",
                    "root queries")
    if data is None:
        # The headline query used to bypass try_post, so a refusal on the one call the
        # script always makes escaped as a traceback.
        print("could not list the root queries; see the message above", file=sys.stderr)
        return 1
    fields = data["__schema"]["queryType"]["fields"]
    print(f"endpoint: {args.endpoint}\n")
    print(f"{len(fields)} root queries:")
    for field in fields:
        print(f"  {field['name']}")
        if field.get("description"):
            print(f"      {field['description']}")

    names = try_post(args.endpoint, token, "{ __schema { types { name kind } } }",
                     "types")
    if names:
        visible = inspectable(names["__schema"]["types"])
        if visible:
            print(f"\n{len(visible)} object type(s) exposed. Inspect one with:")
            print(f"  python introspect.py --type {visible[0]}")

    print("\nFor exploratory querying use the 'Try it' playground on the Schneider "
          "Electric Exchange portal.\nA gateway sits in front of this endpoint and "
          "refuses a request it does not like with a\n403 and an HTML page - a large "
          "introspection query, for instance. That is not a\ncredentials problem: the "
          "request never reached the API.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
