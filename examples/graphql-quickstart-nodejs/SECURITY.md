# Security: GraphQL Quickstart (Node.js)

This example makes one REST token check plus GraphQL introspection requests over HTTPS to
the same host. It reads one bearer token from the environment.

## Credential management

Table – Credentials

| Credential | Source | Grants | Notes |
| --- | --- | --- | --- |
| API token | `BDP_API_TOKEN` environment variable | REST and GraphQL as the consumer it belongs to | Never accepted as a command-line argument, never written to disk |

- No credential appears in source or in `.env.template` defaults.
- `.env` is excluded by the repository `.gitignore`.

## Network security

Table – Connections

| Connection | Protocol | Port | Authentication |
| --- | --- | --- | --- |
| REST `/api/Sites` token check | HTTPS | 443 | `Authorization: ******`, `X-Api-Version: 3.0` |
| GraphQL endpoint | HTTPS | 443 | `Authorization: ******` |

## Input validation

- `--type` must match GraphQL identifier grammar: `[_A-Za-z][_0-9A-Za-z]*`.
- Responses are parsed as JSON and only selected text is printed.

## Logging practices

- The token is never printed.
- Errors surface status and safe message context only.

## Compliance

See [SECURITY.md](../../SECURITY.md) at repository root for vulnerability reporting.
