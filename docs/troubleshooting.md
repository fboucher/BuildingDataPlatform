# Troubleshooting

Organized by **what you observed**, because access is configured in several places and
several of them present the same way from a client: nothing useful comes back. A
subscription rule not yet created, a site not yet authorized for your company, and an
expired token all look alike from the outside, so start from the symptom and this table
will tell you which one to check.

## Something returned an error

Table – HTTP responses

| Response | Most likely cause | What to do |
| --- | --- | --- |
| **401 Unauthorized** | Token expired or malformed | Copy a fresh token from Credentials → System → API Token. They are short-lived by design |
| **403 Forbidden**, JSON body | Token valid, but your consumer is not authorized for that data | Check your subscription rule and your company's site authorization with your Schneider Electric representative |
| **403 Forbidden**, HTML error page | Refused *in front of* the API, the request never arrived | Not a credentials problem. Use the **Try it** explorer on Exchange, or reduce the request size. See below |
| **400 Unsupported API Version** | `X-Api-Version` is not `2.0` or `3.0` | Send `3.0` |
| **400** with a GraphQL `errors` array | The API answered — a real query error | Read the message; it says what is wrong |
| **429 Too Many Requests** | You are calling faster than the API will serve | Back off and retry with exponential backoff; a plain retry loop makes it worse |
| **504 Gateway Time-out** | The request did not complete in time | Retry with backoff. Narrow the request — a filter or a smaller page — if it recurs |

Every REST operation can answer `429` and `504`, so a polling client needs backoff before
it needs anything else.

### Reading a 400 body

Most 400 responses are a **bare JSON array of message strings**, not an object:

```json
["MeasurementValue 00000000-0000-0000-0000-000000000000 was not found",
 "Unsupported API version was requested"]
```

Only a few operations return the object form with `title` and `errors`. Branch on the
parsed type before indexing it — a client that reaches straight for `["title"]` raises on
the common case.

### Telling the two 403s apart

This distinction is worth building into your client's error handling, because the two
have nothing in common.

A **JSON** 403 came from the API: you authenticated, and you are not entitled to that
data. That is a configuration question.

An **HTML** 403 came from the protection layer in front of the API: the request was
stopped before it arrived, and your token was never examined. Very large GraphQL
introspection queries are the usual trigger, some third-party IDEs issue one
automatically on connect, which is why a tool can fail to connect while ordinary queries
from the same token succeed.

## Something returned nothing, and said nothing

Table – Silent failures

| Symptom | Most likely cause | What to do |
| --- | --- | --- |
| **200 with an empty result** | Your subscription rule is missing, or filters out everything | Ask for the rule to be created or widened |
| **Stream connects, no messages arrive** | Same, a consumer with no rule authenticates perfectly and receives nothing | Ask for a subscription rule on that consumer |
| **Stream client hangs, no error at all** | Using AMQP over WebSockets without `websocket-client` installed | `pip install websocket-client`. See below |
| **Values arrive with no names or location** | The rule was created **Without Context** | Either resolve identifiers over the API, or ask for a rule with context. On streaming, also check the consumer's **Telemetry Payload** field selection — that is a portal setting a PartnerAdmin can change, not a rule change |
| **`floorId` or `spaceId` is null and your client breaks** | Not every point sits in a room | Equipment can sit on a building, and a point can have no equipment at all. Treat every level below the site as optional — see [graphql-quickstart](../examples/graphql-quickstart-python/) |
| **Another application stopped receiving** | Two clients sharing one consumer group | Give each application its own consumer group — they are not a load balancer. Consumer groups belong to the hub: read the ones you have from `GET /api/EventHubs`, and ask your Schneider Electric representative to add one if you need another |
| **Ingress returns 2xx, nothing appears** | An identifier is unknown, or the point group is not commissioned | A 2xx is IoT Hub accepting the message, not the platform ingesting it. Verify identifiers against the point group and confirm in the portal |

### The silent WebSocket hang

The Azure Event Hub SDK needs the `websocket-client` package for the
`AmqpOverWebsocket` transport. Without it, the SDK **swallows the `ImportError` inside
its load-balancing loop** and retries roughly every 30 seconds. There is no exception, no
log line and no event, a client that should fail in one second instead sits there
producing nothing.

It is not a credentials problem, a rule problem or a firewall problem, and it looks like
all three.

```bash
pip install websocket-client
```

The [eventhub-consumer](../examples/eventhub-consumer-python/) example checks for the package up
front and tells you, rather than inheriting the hang.

## Connectivity

Every connection is outbound. If a client works on one network and not another, check
these before anything else.

Table – Required outbound connections, UAT

| Destination host | Port | Protocol | Needed for |
| --- | --- | --- | --- |
| `ecostruxure-building-platform-uat.se.app` | 443 | HTTPS | Portal |
| `ecostruxure-building-platform-api-uat.se.app` | 443 | HTTPS | REST and GraphQL |
| `graph-eventhub-uat.servicebus.windows.net` | 5671 | AMQP | Streaming |
| `graph-eventhub-uat.servicebus.windows.net` | 443 | AMQP over WebSockets | Streaming, where 5671 is closed |
| `graph-bdpdif-uat.azure-devices.net` | 5671 | AMQP | Provider ingress |
| `graph-bdpdif-uat.azure-devices.net` | 443 | HTTPS | Provider ingress |

Where 5671 is closed, streaming falls back to AMQP over 443, read the WebSocket note
above before you try it.

## Ingress returns 401

Almost always one of three things:

1. **DeviceId casing.** It is case sensitive in the URL *and* inside the SAS token, and
   the two must match the portal exactly. A mismatch reads like a bad key.
2. **The SAS token expired.** Tokens are short-lived by design, generate one per run, or
   refresh before expiry in a long-running connector.
3. **Wrong shared access key** for that device.

## Before you raise it with us

The answer usually depends on configuration only Schneider Electric can see, so the
fastest path is to include what we cannot infer:

- The **environment** (UAT or production) and roughly **when** it happened.
- The **consumer** or **data source** name, not the credential.
- The **request** you made and the **exact response**, including whether a 403 body was
  JSON or HTML.
- For ingress: the `SourceId`, `PointGroupReferenceId` and `PointReferenceId` involved.

Never paste a token, connection string or shared access key into a ticket or a chat. If
one has been exposed, say so, it needs revoking on the platform side too.

Reply to your welcome email, or contact your Schneider Electric representative.
Platform status: <https://ecostruxurebuildingdataplatform.statuspage.io/>
