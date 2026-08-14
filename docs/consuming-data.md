# Consuming Data

Three ways to read from the platform. They share one bearer token, one authorization
model and one set of subscription rules, and the choice between them is about shape and
timing, not about access.

## Choosing an API

Table – Consumer APIs

| API | Use it when | Transport |
| --- | --- | --- |
| **REST** | You want current values, historical series, on-demand reads or write-back, against a known entity | HTTPS |
| **GraphQL** | You want to walk the building hierarchy and fetch exactly the fields you need in one round trip | HTTPS |
| **Streaming** | You want values as they change, without polling | AMQP over Azure Event Hubs |

Which of these your credentials can reach depends on the API types selected when your
consumer was created: Current Value, Historical, On Demand, Streaming. If an endpoint
returns 403 while another works, that is usually the reason.

## Minimal runnable checks

Use these short checks before writing a client. They confirm that credentials and routing
work, then hand off to the full examples.

### GraphQL check (token and query path)

```bash
cd ../examples/graphql-quickstart-python
python introspect.py
```

Expected outcome:

- Exit code `0`
- A printed list of GraphQL root queries such as `sites`, `buildings`, `equipment`,
  `points`

Minimal GraphQL request shape if you call `/graphql` directly:

```json
{
  "query": "query IntrospectRootQueries { __schema { queryType { fields { name } } } }"
}
```

Minimal REST request and response shape:

```http
GET /api/Buildings/{buildingId}
Authorization: Bearer <token>
X-Api-Version: 3.0
Accept: application/json
```

```json
{
  "measuredId": "00000000-0000-0000-0000-000000000000",
  "buildingId": "00000000-0000-0000-0000-000000000000",
  "floorId": null,
  "spaceId": null
}
```

Parent identifiers are nullable because a measurement can be attached directly to a
building or site rather than to a floor or space.

### REST check (token and API host)

```bash
curl -sS \
  -H "Authorization: Bearer ${BDP_API_TOKEN}" \
  -H "X-Api-Version: 3.0" \
  "https://ecostruxure-building-platform-api-uat.se.app/api/Sites"
```

Expected outcome:

- HTTP `200` with a JSON payload when your token and access are valid
- HTTP `401` if the token is expired or malformed
- HTTP `403` if access is blocked in front of the API or denied by authorization

For operation discovery and client scaffolding from the OpenAPI source, use
[explore-rest-operations](../examples/explore-rest-operations-python/) and
[OpenAPI Bundles](openapi-bundles.md).

## One model, three vocabularies

All three APIs describe the same model, each using the naming conventions of its own
ecosystem: REST follows resource-oriented naming, GraphQL follows the semantic ontology,
and the streaming payload uses field prefixes. A REST `Device` and a GraphQL `equipment`
are the same object under two conventions.

Keep this table to hand if you use more than one API, so a single object stays a single
object in your model.

Table – The same concepts across the three APIs

| Concept | REST | GraphQL | Event Hub fields |
| --- | --- | --- | --- |
| Organization | `Organizations` | `organizations` | `Organization*` |
| Site | `Sites` | `sites` | `Site*` |
| Building | `Buildings` | `buildings` | `Building*` |
| Level / floor | `Floors` | `levels` | `Level*` |
| Room / space | `Spaces` | `rooms` | `Room*` |
| Wing | **not exposed** | `wings` | not sent |
| Zone | **not exposed** | `zones` | not sent |
| Outdoor space | **not exposed** | `outdoorSpaces` | not sent |
| Equipment / device | `Devices` | `equipment` | `Equipment*` |
| Point group | `DeviceGroups` | not a root query | `PointGroup*` |
| Point / measurement | `MeasurementValues` | `points` | `Point*` |

The parent identifiers line up one for one. A REST `MeasurementValue` carries `siteId`,
`buildingId`, `floorId`, `spaceId`, `deviceId` and `deviceGroupId`, and the same measurement
over the Event Hub carries `SiteId`, `BuildingId`, `LevelId`, `RoomId`, `EquipmentId` and
`PointGroupReferenceId`.

The point group is the exception: REST gives you `deviceGroupId`, a UUID, while the
streaming payload carries `PointGroupReferenceId`, the source-issued string. Those two do
not join. Join on the pair below instead.

The point group is worth noting, because it appears under three names: a `DeviceGroup` in
REST, a `PointGroup` in the streaming payload, and a **Point Group Configuration** in the
ingress API that commissions it. A REST `DeviceGroup.referenceId` and `sourceId` carry the
same values as the streaming payload's `PointGroupReferenceId` and `PointGroupSourceId`,
so you can join on them directly.

### Wings, zones and outdoor spaces are served by GraphQL

GraphQL exposes the full spatial model, including `wings`, `zones` and `outdoorSpaces`.
REST covers buildings, floors and spaces, and the streaming payload carries fields for
site, building, level and room.

If your model uses wings, zones or outdoor spaces, use GraphQL to read them. A REST
measurement can report one of the three as its `context`, and GraphQL is where you resolve
it, which is a good reason to reach for GraphQL for discovery even when REST serves your
values.

### Where a point is attached

A REST `MeasurementValue` carries a `context` field saying which level it hangs off. The
specification defines eight values:

Table – `MeasurementValueContext`

| Value | Context | Resolvable over REST |
| --- | --- | --- |
| 0 | Room | yes, via `Spaces` |
| 1 | Building | yes |
| 2 | Level | yes, via `Floors` |
| 3 | Site | yes |
| 4 | OutdoorSpace | via GraphQL |
| 5 | Zone | via GraphQL |
| 6 | Wing | via GraphQL |
| 7 | None | not applicable |

Seven places a point can sit, plus none. The organization is not among them, so treat
every level below the site as optional and let `context` tell you which one applies.

The string form of the enum is `["Room", "Building", "Level", "Site", "outdoorSpace",
"Zone", "Wing", "None"]`. Compare case-insensitively, or match on the integer.

## Authentication

Both HTTP APIs take the same header:

```text
Authorization: Bearer <token>
```

Set up the token using [Setup Credentials](setup-credentials.md). Tokens are short-lived
by design, so expect to re-copy rather than to store.

## Endpoints

Table – UAT (sandbox)

| What | URL |
| --- | --- |
| REST | `https://ecostruxure-building-platform-api-uat.se.app/api/…` |
| GraphQL | `https://ecostruxure-building-platform-api-uat.se.app/graphql` |
| Portal | `https://ecostruxure-building-platform-uat.se.app/` |

Table – Production

| What | URL |
| --- | --- |
| REST | `https://ecostruxure-building-platform-api.se.app/api/…` |
| GraphQL | `https://ecostruxure-building-platform-api.se.app/graphql` |
| Portal | `https://ecostruxure-building-platform.se.app/` |

REST and GraphQL share a host, and GraphQL is the `/graphql` path on it. Moving a client from
UAT to production is a base-URL change and nothing else.

Streaming has no fixed URL. The host and hub name arrive inside the connection string
you copy from the portal.

## The `X-Api-Version` header: REST

Every REST operation declares `X-Api-Version` required, with a default of `3.0`. Versions
`2.0` and `3.0` are supported.

**Send `3.0` explicitly anyway.** Relying on a server-side default is how a client
silently changes behavior the day the default moves. An unsupported value is rejected
outright with `Unsupported API Version was requested`.

On GraphQL the header is optional, and the same accepted values apply if you send it.

## REST operation groups

Fifty operations, in six groups.

Table – REST v3 operation groups

| Group | Retrieves |
| --- | --- |
| Buildings, Floors, Spaces | The spatial hierarchy, by site or organization |
| Devices, DeviceGroups | Equipment, with on-demand read and write commands |
| Measurements | Current values, historical series, and write-back |
| DataSets | Consumer-defined collections of entities |
| EventHubs | The streaming endpoints available to your consumer |
| Sources, Organizations, Sites | Provenance and tenancy |

Rather than transcribing the operation list, which is wrong the first time the
specification moves, read it from the specification itself. The
[explore-rest-operations](../examples/explore-rest-operations-python/) example does exactly that, resolving
`$ref`-ed shared parameters so headers like `X-Api-Version` appear where they apply.

## GraphQL root queries

Ten entry points, mirroring the spatial hierarchy:

`organizations`, `sites`, `outdoorSpaces`, `buildings`, `wings`, `zones`, `levels`,
`rooms`, `equipment`, `points`

Each supports filtering and optional paging. Because they mirror the hierarchy, a GraphQL
client can walk from an organization down to an individual point without the intermediate
lookups REST needs, which is the main reason to prefer it for discovery.

Two vocabularies describe that hierarchy, and an `ExactType` field will carry either.
Sites, buildings and levels are typed against [REC](https://w3id.org/rec)
(`https://w3id.org/rec#Building`), while rooms, equipment and points use Brick
(`https://brickschema.org/schema/Brick#Conference_Room`). Match on the full URI rather
than assuming one namespace.

Containment is a tree, but equipment and points do not sit only at the bottom of it. A
point attaches to equipment *or* directly to any space except the organization, and
equipment sits in a space that need not be a room. A contextless point sits at the site.
Write your traversal to find a point wherever it appears rather than at a fixed depth,
see [graphql-quickstart](../examples/graphql-quickstart-python/) for the shape.

### Exploring interactively

The **Try it** button on Schneider Electric Exchange is the supported explorer, and the
quickest way in. You can also point your own client at the endpoint, and Nitro and Postman
both work, though a tool that issues a very large introspection query on connect may be
refused before the request reaches the API.

That refusal is worth recognizing: it returns **403 with an HTML error page**, not a
GraphQL error document. See [Troubleshooting](troubleshooting.md).

## Streaming

Each consumer has its own Event Hub. Get the connection string from **Partners → *your
partner* → *your consumer* → Event Hubs → Primary Connection String**.

Table – Streaming transports

| Transport | Port | Extra package |
| --- | --- | --- |
| AMQP (default) | 5671 | none |
| AMQP over WebSockets | 443 | `websocket-client` |

Both are confirmed working. Use WebSockets where 5671 is closed outbound, but install
`websocket-client` first, because without it the SDK retries the import failure silently
and your client hangs producing nothing at all.

### Payload shape

By default each message carries one measurement as identifiers only:

```json
{
  "Version": 1,
  "MessageType": "PointValue",
  "Timestamp": "2025-01-01T00:00:00+00:00",
  "Value": "SampleString",
  "PointId": "00000000-0000-0000-0000-000000000000",
  "BuildingId": "00000000-0000-0000-0000-000000000000"
}
```

Selecting all fields adds around fifty, so each message stands alone: point name, unit
and types, equipment, room, level, building and site, site address and time zone,
organization and point group.

**`Version` is a label, not a protocol version.** It is set in the portal alongside the
field selection and defaults to `1`. The two settings are independent, so the number
reflects whatever the administrator chose rather than which fields are present. Use it for
your own bookkeeping if it helps, and detect fields by presence rather than branching on
it.

The trade is size against lookups: self-describing messages cost bandwidth, identifier-only
messages cost an API call per point before you can label a reading. Changing the selection
requires **PartnerAdmin**, so if you hold PartnerViewer this is a request to your
Schneider Electric representative.

Full field list and a runnable consumer: [eventhub-consumer](../examples/eventhub-consumer-python/).

## Network requirements

All connections are outbound.

Table – Outbound connections, UAT

| Destination host | Port | Protocol | Needed for |
| --- | --- | --- | --- |
| `ecostruxure-building-platform-uat.se.app` | 443 | HTTPS | Portal |
| `ecostruxure-building-platform-api-uat.se.app` | 443 | HTTPS | REST and GraphQL |
| `graph-eventhub-uat.servicebus.windows.net` | 5671 | AMQP | Streaming |
| `graph-eventhub-uat.servicebus.windows.net` | 443 | AMQP over WebSockets | Streaming, where 5671 is closed |

## Specifications

- [REST API (v3): OpenAPI specification](https://exchange.se.com/devportal/?api=bdp-api-spec-v3-bundle&id=122362)
- [GraphQL API (v1): schema and reference](https://exchange.se.com/devportal/?api=bdp-graphql-api-spec-v1&id=122362)

The REST bundle format, local file workflow, and client-generation options are documented
in [OpenAPI Bundles](openapi-bundles.md).

The REST specification is a normal OpenAPI document, so the usual generators work:

```bash
openapi-python-client generate --path bdp-api-spec-v3-bundle.json     # Python
npx openapi-typescript bdp-api-spec-v3-bundle.json -o bdp.d.ts        # TypeScript
```

Platform status: <https://ecostruxurebuildingdataplatform.statuspage.io/>
