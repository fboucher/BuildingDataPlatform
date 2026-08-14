# GraphQL Quickstart (Node.js)

![Node.js](https://img.shields.io/badge/Node.js-339933?logo=node.js&logoColor=white)

## Goal

Checks your API token, then shows what the API offers by introspecting the live schema.

## Prerequisites

- Node.js 18.0.0 or later
- API token (See [Setup Credentials](../../docs/setup-credentials.md))
- Network access, Outbound HTTPS (443) to `ecostruxure-building-platform-api-uat.se.app`

## Steps

### 1. Go to the folder

```bash
cd examples/graphql-quickstart-nodejs
```

### 2. Install dependencies

```bash
npm install
```

### 3. Set token

Choose one method.

#### Option A: `.env` file

1. Copy `.env.template` to `.env`.
2. Set your token:

```dotenv
BDP_API_TOKEN=eyJ...
```

#### Option B: environment variable

```bash
export BDP_API_TOKEN="eyJ..."
```

```powershell
$env:BDP_API_TOKEN = "eyJ..."
```

If both are set, the environment variable wins. `.env` is excluded by `.gitignore`.

### 4. Make first call: `node introspect.js`

```bash
node introspect.js
```

### 5. Make second call: `node introspect.js --type Site`

```bash
node introspect.js --type Site
```

## Expected outcome

- Exit code `0` when token check and introspection succeed.
- `token accepted (REST /api/Sites answered 200)`.
- A printed list of root queries.
- Type details when using `--type`.

## Notes

### Endpoint and authentication

Table – GraphQL endpoint

| | |
| --- | --- |
| UAT endpoint | `https://ecostruxure-building-platform-api-uat.se.app/graphql` |
| Production endpoint | `https://ecostruxure-building-platform-api.se.app/graphql` |
| Header | `Authorization: ******` |
| Token | Portal -> Credentials -> System -> API Token |

### Reading failures

Table – Responses and what they mean

| Response | Meaning |
| --- | --- |
| **401** | Token expired or malformed |
| **403** returning an HTML error page | Refused in front of the API. The request never arrived, and your token was never examined |
| **403** with a JSON body | The API answered. You are authenticated but not entitled to that data |
| **400** with a GraphQL `errors` array | The API answered with a query/model error |
| **200** with an empty result | Your subscription rule is missing, or filters out everything |

## Project structure

| File | Responsibility |
| --- | --- |
| `introspect.js` | Token check, introspection query, and type lookup |
| `package.json` | Runtime metadata and scripts |
| `.env.template` | Names the environment variables, copy to `.env` and fill in |
| `README.md` / `SECURITY.md` | Usage and security posture |

## What's next

- [Access Model and Permissions](../../docs/access-model-and-permissions.md)
- [Consuming Data](../../docs/consuming-data.md)
- [GraphQL API (v1) on Exchange](https://exchange.se.com/devportal/?api=bdp-graphql-api-spec-v1&id=122362)
