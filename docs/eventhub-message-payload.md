# Event Hub Message Payload

Event Hub messages deliver telemetry as structured point-value records. This page describes the message shape and how to move from testing credentials to a production service.

## Important: Consumer Ownership

The Event Hub belongs to the **consumer**, not to the partner, which is why running two applications gives you two hubs, one per consumer. This is important when managing multiple applications or scaling your architecture.

## What a Message Looks Like

Each run replays from the start of the retained stream. This is useful when you are confirming credentials, but not what you want in a service — a restart would reprocess everything it already handled.

Example output from a consumer run:

```text
connecting at 2026-08-05T12:00:00+00:00, consumer group $Default, amqp (port 5671), replaying retained stream
[  1] partition=0 offset=12345 enqueued=2026-08-05T11:59:58+00:00 {Version, MessageType, Timestamp, Value, PointId, BuildingId}
received 1 event(s); stopping
```

The message contains:
- `Version`: Message format version
- `MessageType`: Type of message (typically PointValue)
- `Timestamp`: When the measurement was recorded
- `Value`: The measurement value
- `PointId`: Identifier for the measurement point
- `BuildingId`: Identifier for the building

## All Fields

You can configure the Event Hub to include additional fields beyond the basic identifiers. The payload width is configurable per consumer. The portal displays "Telemetry Payload" settings next to the Event Hub connection string.

Selecting every field produces a self-contained message with ~50 fields organized by hierarchy level:

| Group | Fields |
| --- | --- |
| Point | PointId, PointName, PointExactType, PointType, PointReferenceId, PointUnit, PointUnitExactType, PointDataType, PointIsWriteable, PointIsVirtual, MeasurementId |
| Equipment | EquipmentId, EquipmentName, EquipmentExactType, EquipmentType, EquipmentReferenceId, EquipmentProductType, EquipmentVendor |
| Room / Level | Room* and Level* (id, name, exact type, type, reference id) |
| Building | BuildingId, BuildingName, BuildingExactType, BuildingType, BuildingReferenceId |
| Site | SiteId, SiteName, SiteExactType, SiteType, plus SiteAddress, SiteCity, SiteState, SiteCountry, SitePostalCode, SiteTimeZone |
| Organization | OrganizationId, OrganizationName |
| Point group | PointGroupSourceId, PointGroupSourceName, PointGroupReferenceId |


Five details worth knowing before you write a parser:

- **Detect fields by presence rather than branching on `Version`.** `Version` is a label set in the portal alongside the field selection, and the two are independent, the number reflects what the administrator chose, not which fields a message carries.

  Treat everything beyond `Timestamp`, `Value` and the identifiers as optional, and assert on the fields you actually need. That way a later change to the selection widens your data rather than breaking your parser.

- **Site carries no `SiteReferenceId`.** Every other level of the hierarchy has one.

- **`ExactType` spans two vocabularies.** Site, building and level are typed against [REC](https://w3id.org/rec) (`https://w3id.org/rec#Level`), room, equipment and point against Brick (`https://brickschema.org/schema/Brick#Illuminance_Sensor`). Match on the full URI rather than assuming one namespace.

- **The field prefixes are streaming's own names.** `Point*` is a `MeasurementValue` in REST, `Equipment*` is a `Device`, `Level*` is a `Floor`, `Room*` is a `Space`, and `PointGroup*` is a `DeviceGroup`. If you correlate a stream against REST, map the names as you go. [Consuming Data](../docs/consuming-data.md#one-model-three-vocabularies) has the full table.

- **Do not assume every message carries the full chain.** Which location fields a message carries follows from where the point is attached, and a point does not have to sit in a room. Handle a missing or null `Room*` and `Level*` rather than relying on either being  present.

The trade is size against lookups: self-describing messages cost bandwidth,
   identifier-only messages cost an API call per point.

**Changing the payload requires PartnerAdmin.** A PartnerViewer can read the stream but not reshape it, so if you hold PartnerViewer this is a request to your Schneider Electric representative.

## Moving From a Check to a Service

The Event Hub consumer example is designed as a **first check**, it reads a few events and exits. It does not block waiting for data that may never come, making it useful for validating your credentials.

However, every run replays from the start of the retained stream. For a production service, you need to **resume where you stopped**, which requires a **checkpoint store**, an Azure SDK feature. You supply your own Azure storage account, and BDP is not involved.

This means:
- The simple check will reprocess all historical data on every restart
- A production service needs you to implement `BlobCheckpointStore` from the Azure Event Hubs SDK to track your position
- The checkpoint store is your responsibility; BDP provides only the Event Hub connection credentials


## Related Example

For examples and connection patterns, see:
- [Event Hub Consumer Example](../examples/eventhub-consumer-python/)
