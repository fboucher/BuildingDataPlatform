# REST Quickstart (Node.js)

![Node.js](https://img.shields.io/badge/Node.js-339933?logo=node.js&logoColor=white)

## Goal

Makes one authenticated REST call to the BDP API and prints a compact result summary.

## Prerequisites

- Node.js 18.0.0 or later
- API token (See [Setup Credentials](../../docs/setup-credentials.md) )
- Network: Outbound HTTPS (443) to `ecostruxure-building-platform-api-uat.se.app`

## Steps

### 1. Go to the folder

```bash
cd examples/rest-quickstart-nodejs
```

### 2. Install dependencies

```bash
npm install
```

### 3. Set your token

Choose one method.

**Option A:** `.env` file

1. Copy `.env.template` to `.env`.
2. Set your token:

    ```dotenv
    BDP_API_TOKEN=eyJ...
    ```

**Option B:** environment variable

```bash
# bash
export BDP_API_TOKEN="eyJ..."
```
or
```powershell
# powershell
$env:BDP_API_TOKEN = "eyJ..."
```

If both are set, the environment variable wins.

### 4. Call one endpoint

```bash
node call_rest_api.js --resource sites --take 5
```

The script supports three resources:

- `sites` (default)
- `organizations`
- `buildings` (requires `--site-id`)

Examples:

```bash
node call_rest_api.js --raw

node call_rest_api.js --resource organizations

node call_rest_api.js --resource buildings --site-id YOUR_SITE_GUID --take 10
```

## Expected Outcome

- Exit code `0` when the API answers with a valid JSON body.
- A one-line call summary showing method, URL and HTTP status.
- A compact list of returned entities by `id` and `name`.

Expected output sample:

```text
GET https://ecostruxure-building-platform-api-uat.se.app/api/Sites?take=5
status: 200
items: 5
  - 8f6...c9d | Example Site
  - 9ab...72e | North Campus
```

## Notes

### The call itself (minimal)

If you strip everything down, the REST call is just:

1. Build a URL
2. Add headers (`Authorization`, `X-Api-Version`)
3. Send a `GET` request

This is the smallest possible version of the call logic:

```javascript
import https from 'https';
import { URL } from 'url';

const url = new URL('https://ecostruxure-building-platform-api-uat.se.app/api/Sites?take=5');

const options = {
  hostname: url.hostname,
  path: url.pathname + url.search,
  method: 'GET',
  headers: {
    'Authorization': 'Bearer YOUR_TOKEN',
    'X-Api-Version': '3.0',
    'Accept': 'application/json',
  },
  timeout: 30000,
};

https.request(options, (res) => {
  let data = '';
  res.on('data', chunk => { data += chunk; });
  res.on('end', () => {
    const body = JSON.parse(data);
  });
}).end();
```

That is the core network call in `call_rest_api.js`, without token loading,
argument parsing, or response summarization.

### Authentication

Every REST call uses the same bearer token model as GraphQL:

```text
Authorization: Bearer ******
```

Tokens are short-lived by design. Re-copy a fresh token when needed.

### `X-Api-Version`

Every REST operation declares this header. This script sends `3.0` by default.

- Use `--api-version 3.0` (default)
- `2.0` is also accepted
- Unsupported values return `400 Unsupported API Version was requested`

### Common failures

Table - Responses and what they mean

| Response | Meaning |
| --- | --- |
| **401** | Token expired or malformed |
| **403** with a JSON body | Token valid, but your consumer is not authorized for that data |
| **403** returning an HTML error page | Refused in front of the API, request did not reach the API |
| **400 Unsupported API Version** | `X-Api-Version` is not `2.0` or `3.0` |
| **200** with an empty result | Subscription rule is missing, or filters out everything |

See [Troubleshooting](../../docs/troubleshooting.md) for the 403 distinction.

## What's Next

- [Access Model and Permissions](../../docs/access-model-and-permissions.md)
- [Consuming Data](../../docs/consuming-data.md)
- [Explore REST operations from the OpenAPI bundle](../explore-rest-operations-python/README.md)
- [REST Quickstart (Python)](../rest-quickstart-python/README.md)
- [REST Quickstart (.NET)](../rest-quickstart-dotnet/README.md)
