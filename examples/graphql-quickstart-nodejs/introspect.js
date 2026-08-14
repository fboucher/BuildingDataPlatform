#!/usr/bin/env node
import process from 'node:process';
import { setTimeout as delay } from 'node:timers/promises';
import dotenv from 'dotenv';

dotenv.config();

const UAT_ENDPOINT = 'https://ecostruxure-building-platform-api-uat.se.app/graphql';
const TOKEN_ENV = 'BDP_API_TOKEN';
const GRAPHQL_NAME = /^[_A-Za-z][_0-9A-Za-z]*$/;

class GatewayRefused extends Error {}
class GraphQLError extends Error {}
class TransportError extends Error {}

function parseArgs(argv) {
  const result = { endpoint: UAT_ENDPOINT, type: null, skipTokenCheck: false, help: false };

  for (let i = 2; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      result.help = true;
      return result;
    }
    if (arg === '--skip-token-check') {
      result.skipTokenCheck = true;
      continue;
    }
    if (arg === '--endpoint') {
      result.endpoint = argv[++i];
      continue;
    }
    if (arg === '--type') {
      result.type = argv[++i];
      continue;
    }
    throw new Error(`unknown argument: ${arg}`);
  }

  return result;
}

function helpText() {
  return `Usage:\n  node introspect.js [--endpoint URL] [--type TYPE] [--skip-token-check]\n\nExamples:\n  node introspect.js\n  node introspect.js --type Site\n  node introspect.js --endpoint https://ecostruxure-building-platform-api.se.app/graphql`;
}

function resolveToken() {
  const token = process.env[TOKEN_ENV];
  return token && token.trim() ? token.trim() : null;
}

async function post(endpoint, token, query, variables = undefined) {
  const body = { query };
  if (variables !== undefined) {
    body.variables = variables;
  }

  const response = await fetch(endpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(body),
  });

  const raw = await response.text();

  if (!response.ok) {
    if (raw.includes('Application-Gateway')) {
      throw new GatewayRefused(
        `HTTP ${response.status} from the gateway - the request never reached the API`,
      );
    }

    let payload;
    try {
      payload = JSON.parse(raw);
    } catch {
      throw new TransportError(`HTTP ${response.status}: ${raw.slice(0, 300)}`);
    }

    if (Array.isArray(payload.errors) && payload.errors.length > 0) {
      throw new GraphQLError(`GraphQL errors:\n${JSON.stringify(payload.errors, null, 2)}`);
    }

    throw new TransportError(`HTTP ${response.status}: ${raw.slice(0, 300)}`);
  }

  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    throw new TransportError('response was not valid JSON');
  }

  if (Array.isArray(payload.errors) && payload.errors.length > 0) {
    throw new GraphQLError(`GraphQL errors:\n${JSON.stringify(payload.errors, null, 2)}`);
  }

  return payload.data;
}

async function tryPost(endpoint, token, query, label, variables = undefined) {
  try {
    return await post(endpoint, token, query, variables);
  } catch (err) {
    if (err instanceof GatewayRefused || err instanceof GraphQLError || err instanceof TransportError) {
      console.error(`  (${label}: ${err.message})`);
      return null;
    }
    throw err;
  }
}

function inspectable(types) {
  return types
    .filter((item) => item.kind === 'OBJECT' && !item.name.startsWith('__'))
    .map((item) => item.name)
    .sort((a, b) => a.localeCompare(b));
}

async function checkToken(endpoint, token) {
  const probe = endpoint.endsWith('/graphql') ? `${endpoint.slice(0, -8)}/api/Sites` : `${endpoint}/api/Sites`;

  const response = await fetch(probe, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      'X-Api-Version': '3.0',
      Accept: 'application/json',
    },
  });

  if (response.status === 401) {
    console.error(
      'token rejected: HTTP 401 from the REST API.\n' +
        '  Copy a fresh one from BDP Portal > Credentials > System > API Token.\n' +
        '  Tokens are short-lived by design.',
    );
    return false;
  }

  console.log(`token accepted (REST /api/Sites answered ${response.status})\n`);
  return true;
}

async function main() {
  let args;
  try {
    args = parseArgs(process.argv);
  } catch (err) {
    console.error(String(err.message ?? err));
    console.error(helpText());
    return 2;
  }

  if (args.help) {
    console.log(helpText());
    return 0;
  }

  const token = resolveToken();
  if (!token) {
    console.error(`set ${TOKEN_ENV} in your environment or add it to a .env file in this folder`);
    return 2;
  }

  if (args.type && !GRAPHQL_NAME.test(args.type)) {
    console.error(`${JSON.stringify(args.type)} is not a GraphQL type name`);
    return 2;
  }

  if (!args.skipTokenCheck) {
    try {
      const ok = await checkToken(args.endpoint, token);
      if (!ok) {
        return 1;
      }
    } catch (err) {
      console.error(`could not verify the token: ${String(err.message ?? err)}\n`);
    }
  }

  if (args.type) {
    const data = await tryPost(
      args.endpoint,
      token,
      'query TypeDetail($name: String!) { __type(name: $name) { name kind fields { name } } }',
      'type detail',
      { name: args.type },
    );

    const node = data?.__type;
    if (!node) {
      console.error(`no detail available for ${JSON.stringify(args.type)}`);
      return 1;
    }

    console.log(`${node.kind} ${node.name}`);
    for (const field of node.fields ?? []) {
      console.log(`  - ${field.name}`);
    }
    return 0;
  }

  const data = await tryPost(
    args.endpoint,
    token,
    '{ __schema { queryType { fields { name description } } } }',
    'root queries',
  );

  if (!data) {
    console.error('could not list the root queries; see the message above');
    return 1;
  }

  const fields = data.__schema.queryType.fields;
  console.log(`endpoint: ${args.endpoint}\n`);
  console.log(`${fields.length} root queries:`);
  for (const field of fields) {
    console.log(`  ${field.name}`);
    if (field.description) {
      console.log(`      ${field.description}`);
    }
  }

  const names = await tryPost(args.endpoint, token, '{ __schema { types { name kind } } }', 'types');
  if (names) {
    const visible = inspectable(names.__schema.types);
    if (visible.length > 0) {
      console.log(`\n${visible.length} object type(s) exposed. Inspect one with:`);
      console.log(`  node introspect.js --type ${visible[0]}`);
    }
  }

  console.log(
    "\nFor exploratory querying use the 'Try it' playground on the Schneider Electric Exchange portal.\n" +
      'A gateway sits in front of this endpoint and may refuse a request it does not like with a\n' +
      '403 and an HTML page. That is not a credentials problem: the request never reached the API.',
  );

  await delay(0);
  return 0;
}

main()
  .then((code) => {
    process.exitCode = code;
  })
  .catch((err) => {
    console.error(String(err?.stack ?? err));
    process.exitCode = 1;
  });
