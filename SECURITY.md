# Security Policy

## Reporting a Vulnerability

If you discover or suspect a security vulnerability in the EcoStruxure&trade; Building
Data Platform or in the examples in this repository, report it through the Schneider
Electric vulnerability reporting portal:

> **https://www.se.com/ww/en/work/support/cybersecurity/report-a-vulnerability.jsp**

Do **not** open a public GitHub issue for security vulnerabilities. Use the portal above
to establish secure communication with the Schneider Electric Product Security Incident
Response Team (PSIRT).

## What to Include

When reporting, please provide:

- A description of the vulnerability and its potential impact.
- Steps to reproduce or a proof of concept (if available).
- The affected component (REST API, GraphQL API, streaming, DIF ingress, portal, or one
  of the examples in this repository).
- The environment (UAT or production) and the approximate time of the observation.

## Response

Schneider Electric PSIRT will acknowledge your report and work with you to understand and
address the issue. For details on Schneider Electric's vulnerability handling process,
see:

- [Schneider Electric Cybersecurity Support](https://www.se.com/ww/en/work/support/cybersecurity/overview.jsp)
- [Security Notifications](https://www.se.com/ww/en/work/support/cybersecurity/security-notifications.jsp)

## Credentials

Every credential the platform issues is a bearer credential: anything holding it is you.
There is no second factor on an API token, an Event Hub connection string or an IoT Hub
shared access key.

Table – Credentials and where they come from

| Credential | Issued at | Grants |
| --- | --- | --- |
| **API token** | Portal → Credentials → System → API Token | REST and GraphQL, as the consumer it belongs to |
| **Event Hub connection string** | Portal → Partners → *partner* → *consumer* → Event Hubs | Read access to that consumer's telemetry stream |
| **IoT Hub connection string** | Portal → Data Sources → *company* → *source* → Connections | Write access to ingest under that data source |

The examples in this repository demonstrate the baseline every client should follow, and
they are worth keeping when you copy one:

- **No credential in any file.** Every one comes from an environment variable. Each
  example that takes a credential ships a `.env.template` naming the variables it reads,
  and `.gitignore` excludes `.env`. `explore-rest-operations` and `point-group-config`
  need no credential and carry none.
- **No credential as a command-line argument.** Arguments land in shell history and in
  process listings.
- **Standard library only**, unless a vendor SDK is genuinely required. Only
  `eventhub-consumer` has a dependency.
- **Every script terminates on its own** and returns a meaningful exit code, so it drops
  into a smoke test or a CI step unchanged.
- **Errors say which layer failed** — credentials, configuration, network or code.

Each example also ships its own `SECURITY.md` describing what that example touches; use
one as a template for your own.

Practices every example in this repository follows, and that your own client should:

- **Read credentials from the environment or a secret store, never from a file in the
  repository.** No example accepts a credential as a command-line argument either, because
  arguments land in shell history and in process listings.
- **Keep credentials out of logs.** An error message that echoes a request header defeats
  every other control on this list.
- **Enforce TLS.** All endpoints are HTTPS or AMQPS, never disable certificate validation
  to work around a proxy.
- **Scope one credential per application.** If you run two applications you get two
  consumers and two connection strings, so one can be rotated without stopping the other.
- **Treat a token as short-lived.** Generate SAS tokens per run rather than storing them,
  and re-copy an API token rather than persisting one that expired.
- **Report a leak rather than rotating quietly.** If a credential reaches a public
  repository, a ticket or a chat log, tell your Schneider Electric representative, the
  credential identifies a consumer or data source, and the platform side needs revoking
  too.

## Responsibility for Client Code

The examples in this repository are illustrations of the platform's APIs, not a supported
product; code you derive from one is yours to maintain.

Applications you build against the platform are not covered by the platform's security
response process. As the author you are responsible for:

- Addressing vulnerabilities in your own code.
- Monitoring your dependencies for known CVEs — including the Azure Event Hub SDK the
  streaming example uses, and the Azure IoT SDK if you adopt it for a production
  connector.
- Validating and bounding data received from the platform before acting on it, and
  validating your own data before sending it.

The `.gitignore` in this repository excludes the file patterns credentials usually arrive
in (`.env`, `*.pem`, `*.key`, `local.settings.json`). Keep it in place when you copy an
example into your own project.
