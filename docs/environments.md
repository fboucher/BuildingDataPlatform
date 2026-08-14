## Environments

The following table lists the main endpoints for the different environments:

| What | UAT (sandbox) | Production |
| --- | --- | --- |
| REST | `https://ecostruxure-building-platform-api-uat.se.app/api/…` | `https://ecostruxure-building-platform-api.se.app/api/…` |
| GraphQL | `https://ecostruxure-building-platform-api-uat.se.app/graphql` | `https://ecostruxure-building-platform-api.se.app/graphql` |
| Portal | `https://ecostruxure-building-platform-uat.se.app/` | `https://ecostruxure-building-platform.se.app/` |

REST and GraphQL share a host. Every REST operation sits under `/api/`, for example `/api/Buildings`; GraphQL is the single `/graphql` path. The OpenAPI specification's `servers` entry is the bare host, so a generated client adds the `/api/` prefix itself.

Streaming has no fixed URL, the host and hub name arrive inside the connection string you copy from the portal.

Partner development starts in UAT. It carries representative data and the same API surface as production, so a client built against it needs a base URL change and nothing else.

Platform status: <https://ecostruxurebuildingdataplatform.statuspage.io/>