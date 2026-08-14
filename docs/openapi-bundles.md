# Get OpenAPI Bundles

## Goal
How to retrieve the REST API v3 OpenAPI bundle and learn about how the bundle is structured,
so you can read operations and parameters directly from the canonical source.

## Steps

1. Download the REST API v3 OpenAPI bundle from Exchange.

   - Link: https://exchange.se.com/devportal/?api=bdp-api-spec-v3-bundle&id=122362

2. Store the file locally as a versioned artifact.

   Suggested names:

   - bdp-api-spec-v3-bundle.json
   - bdp-api-spec-v3-bundle-3.0.json

3. Keep the downloaded bundle alongside your implementation notes and generation inputs.

## Expected Outcome

- You have a local, versioned OpenAPI bundle as the canonical REST API input.
- You can identify where operations, parameters, and schemas are declared.
- You can compare bundle revisions to detect API changes before client updates.

## Bundle Structure

The REST bundle is a standard OpenAPI document. The most important sections are:

Table - OpenAPI sections used most often

| Section | What it tells you |
| --- | --- |
| `openapi` | OpenAPI specification version used by the document |
| `info` | API title and version label |
| `servers` | Base host URLs; REST operations are still under `/api/...` |
| `paths` | Operations grouped by route and HTTP method |
| `components.parameters` | Shared parameters, including `X-Api-Version` |
| `components.schemas` | Response and request data models |

`paths` plus `components.parameters` are the two sections most consumers read first.
Operations often reference shared parameters with `$ref`, so the route-level and
operation-level parameter lists are meant to be read together.

## How REST Operations Are Organized

The bundle declares roughly fifty REST operations, grouped around the same six API areas
used throughout this starter pack.

Table - REST v3 operation groups

| Group | Retrieves |
| --- | --- |
| Buildings, Floors, Spaces | The spatial hierarchy, by site or organization |
| Devices, DeviceGroups | Equipment, with on-demand read and write commands |
| Measurements | Current values, historical series, and write-back |
| DataSets | Consumer-defined collections of entities |
| EventHubs | The streaming endpoints available to your consumer |
| Sources, Organizations, Sites | Provenance and tenancy |

The explorer example that prints this directly from `paths` is in
[explore-rest-operations](../examples/explore-rest-operations-python/README.md).

## Shared Parameters and `X-Api-Version`

The `X-Api-Version` header is declared as a shared parameter and applied by reference
across operations.

- Declared as required in the specification
- Default value `3.0`
- Accepted values `2.0` and `3.0`

Send `3.0` explicitly in clients rather than relying on defaults.

## Notes

- "Bundle" means the packaged OpenAPI document for the REST API release.
- The bundle can be read without calling the API.
- The portal offers both JSON and YAML variants.
- API calls still require credentials from [Setup Credentials](setup-credentials.md).
- For direct API behavior and naming mappings, see [consuming-data.md](consuming-data.md).


### Scaffolding a client

The specification is a normal OpenAPI document, therefore in a bigger projects usual generators can be used to scaffold a client. For example:

**Python**

```bash
openapi-python-client generate --path bdp-api-spec-v3-bundle.json
```

**TypeScript**

```bash
npx openapi-typescript bdp-api-spec-v3-bundle.json -o bdp.d.ts
```